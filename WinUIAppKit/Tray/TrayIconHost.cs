using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Tray;

/// <summary>
/// One host window's tray icons (TRAY-1, TRAY-3). It listens on the window once, routes each icon's
/// clicks to that icon, re-adds every icon when Explorer restarts, and tells the icons when they need
/// re-rendering. An app with one icon can keep using <see cref="TrayIcon"/>'s own constructor, which
/// creates a host for it; an app with several (one per launcher, say) creates a host and calls
/// <see cref="Add"/> for each.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    /// <summary>Fixed subclass id for tray message handling (0x7AA1 tray, 0x7AA2 flyout, 0x7AA3 host window).</summary>
    private static readonly UIntPtr SubclassId = 0x7AA1;

    private readonly Dictionary<uint, TrayIcon> _icons = [];
    private readonly SUBCLASSPROC _wndProc;   // held in a field so the GC can't collect the delegate
    private uint _nextUid = 1;
    private bool _disposed;

    /// <summary>Start listening for tray messages on the host window.</summary>
    /// <param name="hostHwnd">The window that receives tray messages; it must outlive every icon.</param>
    /// <param name="callbackMessageName">App-unique name for the registered callback message every icon uses.</param>
    public TrayIconHost(IntPtr hostHwnd, string callbackMessageName)
    {
        Hwnd = hostHwnd;
        Dispatcher = DispatcherQueue.GetForCurrentThread();
        CallbackMessage = RegisterWindowMessage(callbackMessageName);
        TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _wndProc = WndProc;
        SetWindowSubclass(Hwnd, _wndProc, SubclassId, 0);
    }

    /// <summary>The scaling, taskbar theme or display layout changed: re-render every icon (each icon also raises its own event).</summary>
    public event Action? RenderNeeded;

    /// <summary>The display layout changed. Flyouts should re-park.</summary>
    public event Action? DisplayChanged;

    /// <summary>The icons this host owns.</summary>
    public IReadOnlyCollection<TrayIcon> Icons => _icons.Values;

    internal IntPtr Hwnd { get; }

    internal DispatcherQueue Dispatcher { get; }

    internal int CallbackMessage { get; }

    private int TaskbarCreatedMessage { get; }

    /// <summary>
    /// Add an icon. It appears on its first <see cref="TrayIcon.Update"/> (while <see cref="TrayIcon.IsVisible"/>).
    /// </summary>
    /// <param name="id">
    /// The icon's stable GUID: Windows remembers the user's show-or-hide choice by it, so it must never
    /// change for the same icon. Use <see cref="GuidFromName"/> for icons created from app data.
    /// </param>
    public TrayIcon Add(Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icons.Values.Any(icon => icon.Id == id))
        {
            throw new ArgumentException($"An icon with id {id} is already on this host.", nameof(id));
        }

        var icon = new TrayIcon(this, _nextUid++, id, ownsHost: false);
        _icons.Add(icon.Uid, icon);
        return icon;
    }

    /// <summary>
    /// A stable GUID derived from a name, for icons created from app data (one per launcher). The same
    /// name always gives the same GUID: an MD5 name hash with the version-3 and RFC 4122 variant bits set.
    /// Include the app in the name ("MyApp.Tray." + id) so two apps never share one.
    /// </summary>
    /// <param name="name">A name unique to the icon across apps.</param>
    public static Guid GuidFromName(string name)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }

    /// <summary>Remove every icon from the tray and stop listening. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var icon in _icons.Values.ToList())
        {
            icon.Dispose();
        }

        RemoveWindowSubclass(Hwnd, _wndProc, SubclassId);
    }

    /// <summary>Register the single icon a <see cref="TrayIcon"/> constructor created with this host.</summary>
    internal void Adopt(TrayIcon icon)
    {
        _icons.Add(icon.Uid, icon);
        _nextUid = Math.Max(_nextUid, icon.Uid + 1);
    }

    /// <summary>Forget a disposed icon.</summary>
    internal void Remove(TrayIcon icon) => _icons.Remove(icon.Uid);

    // Work is deferred with TryEnqueue: showing windows or menus from inside the window procedure
    // re-enters the message loop and misbehaves.
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (CallbackMessage != 0 && (int)msg == CallbackMessage)
        {
            // Without NOTIFYICON_VERSION_4, wParam is the icon's uID and lParam's low word the mouse message.
            if (_icons.TryGetValue((uint)wParam.ToInt64(), out var icon))
            {
                icon.HandleNotification((int)(lParam.ToInt64() & 0xFFFF));
            }

            return IntPtr.Zero;
        }

        if (TaskbarCreatedMessage != 0 && (int)msg == TaskbarCreatedMessage)
        {
            // Explorer restarted: every icon is gone and must be added again.
            Dispatcher.TryEnqueue(() =>
            {
                foreach (var icon in _icons.Values.ToList())
                {
                    icon.Readd();
                }
            });
        }
        else if (msg == WM_DISPLAYCHANGE)
        {
            Dispatcher.TryEnqueue(() =>
            {
                DisplayChanged?.Invoke();
                RenderNeeded?.Invoke();
                foreach (var icon in _icons.Values.ToList())
                {
                    icon.RaiseDisplayChanged();
                    icon.RaiseRenderNeeded();
                }
            });
        }
        else if (msg == WM_DPICHANGED || msg == WM_SETTINGCHANGE)
        {
            Dispatcher.TryEnqueue(() =>
            {
                RenderNeeded?.Invoke();
                foreach (var icon in _icons.Values.ToList())
                {
                    icon.RaiseRenderNeeded();
                }
            });
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
