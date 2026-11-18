using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Tray;

/// <summary>Where a tray click happened: the icon's centre, or the cursor if Windows can't say.</summary>
public readonly record struct TrayAnchor(int X, int Y);

/// <summary>
/// A notification-area icon owned by a (usually hidden) host window, using raw <c>Shell_NotifyIcon</c>:
/// <list type="bullet">
/// <item>a stable GUID, so Windows remembers the user's show/hide choice across updates;</item>
/// <item>re-added automatically when Explorer restarts (<c>TaskbarCreated</c>);</item>
/// <item>icons supplied as PNG bytes rendered at <see cref="IconPixelSize"/>, so they stay sharp at any scaling;</item>
/// <item>click events raised on the UI thread after the window procedure returns.</item>
/// </list>
/// Click policy: the first click of a double-click is reported as <see cref="Clicked"/> (it can't be
/// told apart in time), then the double-click as <see cref="DoubleClicked"/>, and the trailing
/// button-up of the double-click is swallowed so it can't toggle anything a second time. Only a
/// button-up within the system double-click time is swallowed, so if that button-up never reaches
/// the icon the next real click still counts.
/// <para>
/// One icon: use the constructor. Several icons on one host window: create a <see cref="TrayIconHost"/>
/// and call <see cref="TrayIconHost.Add"/> for each.
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly TrayIconHost _host;
    private readonly bool _ownsHost;

    private IntPtr _hIcon;
    private string _tooltip = "";
    private bool _added, _disposed, _visible = true;

    /// <summary>
    /// Whether the shell identifies this icon by its GUID (the normal case) or, after the GUID was refused,
    /// by host window and number for the rest of the session. See <see cref="Add"/>.
    /// </summary>
    private bool _useGuid = true;

    /// <summary>
    /// Until when (Environment.TickCount64) a button-up is the tail of a double-click and is ignored.
    /// Time-limited so a tail that lands off the icon can't eat the next real click.
    /// </summary>
    private long _swallowUpUntilMs;

    /// <summary>Create an app's single tray icon, with a host of its own.</summary>
    /// <param name="hostHwnd">The window that receives tray messages (lives as long as the icon).</param>
    /// <param name="id">Stable per-app GUID. Never reuse another app's GUID: Windows treats them as the same icon.</param>
    /// <param name="callbackMessageName">App-unique name for the registered callback message.</param>
    public TrayIcon(IntPtr hostHwnd, Guid id, string callbackMessageName)
    {
        _host = new TrayIconHost(hostHwnd, callbackMessageName);
        _ownsHost = true;
        Uid = 1;
        Id = id;
        _host.Adopt(this);
    }

    /// <summary>An icon created by <see cref="TrayIconHost.Add"/>.</summary>
    internal TrayIcon(TrayIconHost host, uint uid, Guid id, bool ownsHost)
    {
        _host = host;
        _ownsHost = ownsHost;
        Uid = uid;
        Id = id;
    }

    /// <summary>The icon's stable GUID.</summary>
    public Guid Id { get; }

    /// <summary>The icon's number on its host window, which tray messages carry.</summary>
    internal uint Uid { get; }

    /// <summary>
    /// Whether the icon is in the tray. Hiding removes it but keeps its image and tooltip, so showing
    /// it again needs no new <see cref="Update"/>. New icons are visible.
    /// </summary>
    public bool IsVisible
    {
        get => _visible;
        set
        {
            if (_visible == value || _disposed)
            {
                return;
            }

            _visible = value;
            if (!value)
            {
                Delete();
            }
            else if (_hIcon != IntPtr.Zero)
            {
                Add();
            }
        }
    }

    /// <summary>Left click (also the first click of a double-click).</summary>
    public event Action<TrayAnchor>? Clicked;

    /// <summary>Double-click (raised after <see cref="Clicked"/> for its first click).</summary>
    public event Action<TrayAnchor>? DoubleClicked;

    /// <summary>Right click or keyboard context menu, with the cursor position for the menu.</summary>
    public event Action<int, int>? ContextMenuRequested;

    /// <summary>
    /// The icon should be re-rendered: scaling, taskbar theme or display layout changed. Call
    /// <see cref="Update"/> with a fresh image at <see cref="IconPixelSize"/>.
    /// </summary>
    public event Action? RenderNeeded;

    /// <summary>The display layout changed (monitors added/removed/rearranged). Flyouts should re-park.</summary>
    public event Action? DisplayChanged;

    /// <summary>Pixel size the tray uses right now: the small-icon size at the main display's scaling (e.g. 24 px at 150%).</summary>
    public static int IconPixelSize
    {
        get
        {
            var (_, _, scale) = PrimaryMonitorArea();
            return Math.Clamp(GetSystemMetricsForDpi(SM_CXSMICON, (uint)Math.Round(96 * scale)), 16, 256);
        }
    }

    /// <summary>True when the taskbar uses the dark theme (Windows' system mode, not the app mode).</summary>
    public static bool TaskbarIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not int v || v == 0;
        }
        catch { return true; }
    }

    /// <summary>
    /// Show or update the icon. <paramref name="png"/> is a PNG rendered at <see cref="IconPixelSize"/>;
    /// <paramref name="tooltip"/> is truncated to the shell's 127-character limit.
    /// </summary>
    public void Update(byte[] png, string tooltip)
    {
        if (_disposed)
        {
            return;
        }

        var size = IconPixelSize;
        var newIcon = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0);
        if (newIcon == IntPtr.Zero)
        {
            Logger.Warn("CreateIconFromResourceEx failed");
        }

        var oldIcon = _hIcon;
        _hIcon = newIcon;
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        if (!_visible)
        {
            // Kept for when the icon is shown again.
        }
        else if (_added)
        {
            var data = Data();
            if (!Shell_NotifyIcon(NIM_MODIFY, ref data))
            {
                Add();
            }
        }
        else
        {
            Add();
        }

        if (oldIcon != IntPtr.Zero)
        {
            DestroyIcon(oldIcon);
        }
    }

    /// <summary>The icon's centre on screen, or the cursor position if the shell can't report it.</summary>
    /// <remarks>Use <see cref="TryGetAnchor"/> instead when the caller needs to know which one it got.</remarks>
    public TrayAnchor GetAnchor()
    {
        if (TryGetAnchor(out var anchor))
        {
            return anchor;
        }

        GetCursorPos(out var pt);
        return new TrayAnchor(pt.X, pt.Y);
    }

    /// <summary>
    /// The icon's centre on screen, as the shell reports it. Returns false, with a default
    /// <paramref name="anchor"/>, when the shell has no rectangle for the icon, for example while the
    /// icon is not in the tray (hidden, not yet added, or removed). Callers that would rather place UI
    /// somewhere else than at the cursor use this; <see cref="GetAnchor"/> falls back to the cursor.
    /// </summary>
    /// <param name="anchor">The icon's centre in physical screen pixels when this returns true.</param>
    /// <returns>True when the shell reported a non-empty rectangle for the icon.</returns>
    public bool TryGetAnchor(out TrayAnchor anchor)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _host.Hwnd,
            uID = Uid,
            guidItem = _useGuid ? Id : Guid.Empty,
        };

        if (Shell_NotifyIconGetRect(ref id, out var r) == 0 && r.Right > r.Left && r.Bottom > r.Top)
        {
            anchor = new TrayAnchor((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            return true;
        }

        anchor = default;
        return false;
    }

    /// <summary>Remove the icon from the tray and stop listening. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Delete();
        _host.Remove(this);
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
        }

        _hIcon = IntPtr.Zero;
        if (_ownsHost)
        {
            _host.Dispose();
        }
    }

    /// <summary>
    /// The taskbar was recreated (Explorer restarted, or a display or scaling change): add the icon again
    /// if it is meant to be showing.
    /// </summary>
    internal void Readd()
    {
        if (!_disposed && _visible && _hIcon != IntPtr.Zero)
        {
            Add();
        }
    }

    internal void RaiseRenderNeeded() => RenderNeeded?.Invoke();

    internal void RaiseDisplayChanged() => DisplayChanged?.Invoke();

    // ── Shell registration ───────────────────────────────────────────────
    private NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _host.Hwnd,
        uID = Uid,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | (_useGuid ? NIF_GUID : 0),
        uCallbackMessage = (uint)_host.CallbackMessage,
        hIcon = _hIcon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
        guidItem = _useGuid ? Id : Guid.Empty,
    };

    /// <summary>
    /// Register the icon with the shell, replacing any registration it already has. Sets
    /// <see cref="_added"/>, and logs a warning only when the icon could not be put in the tray at all.
    /// </summary>
    private void Add()
    {
        if (TryAdd())
        {
            return;
        }

        if (_useGuid)
        {
            // Windows ties an icon GUID to the path of the program that first registered it, and an
            // MSIX update installs to a new versioned folder. Unless both programs are Authenticode-signed
            // by the same company (a packaged exe is not), Windows 10 then refuses the GUID for good.
            // Register by host window and number instead, for the rest of this session: the icon shows,
            // though the user's show-or-hide choice for it starts over.
            Delete();
            _useGuid = false;
            if (TryAdd())
            {
                Logger.Warn("Windows refused the tray icon's GUID {Id} (it was registered by an earlier install of the app); the icon was added without it for this session", Id);
                return;
            }

            // Neither identity worked, so the tray itself isn't there yet (sign-in, Explorer restarting).
            // Keep the GUID for the TaskbarCreated retry.
            _useGuid = true;
        }

        _added = false;
        Logger.Warn("Shell_NotifyIcon(NIM_ADD) and NIM_MODIFY both failed; the icon is not in the tray");
    }

    /// <summary>One attempt to register the icon under its current identity. Returns whether it is in the tray.</summary>
    private bool TryAdd()
    {
        // A stale registration under the same identity (crash, previous run) makes NIM_ADD fail, so remove it first.
        Delete();
        var data = Data();
        if (Shell_NotifyIcon(NIM_ADD, ref data))
        {
            _added = true;
            return true;
        }

        // Explorer can still hold the icon despite the delete: TaskbarCreated also arrives on display and
        // scaling changes, when nothing was lost. Updating it in place then succeeds and the icon is fine.
        if (Shell_NotifyIcon(NIM_MODIFY, ref data))
        {
            _added = true;
            Logger.Debug("Shell_NotifyIcon(NIM_ADD) failed; the shell still held the icon, so it was updated in place");
            return true;
        }

        return false;
    }

    /// <summary>Remove the icon from the tray under its current identity (GUID, or host window and number).</summary>
    private void Delete()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _host.Hwnd,
            uID = Uid,
            uFlags = _useGuid ? NIF_GUID : 0,
            guidItem = _useGuid ? Id : Guid.Empty,
            szTip = "",
            szInfo = "",
            szInfoTitle = "",
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
    }

    /// <summary>A mouse message for this icon, from its host's window procedure.</summary>
    internal void HandleNotification(int notification)
    {
        switch (notification)
        {
            case WM_LBUTTONUP:
                if (Environment.TickCount64 < _swallowUpUntilMs)
                {
                    _swallowUpUntilMs = 0;
                    return;
                }
                var clickAnchor = GetAnchor();
                _host.Dispatcher.TryEnqueue(() => Clicked?.Invoke(clickAnchor));
                break;

            case WM_LBUTTONDBLCLK:
                _swallowUpUntilMs = Environment.TickCount64 + GetDoubleClickTime();
                var doubleAnchor = GetAnchor();
                _host.Dispatcher.TryEnqueue(() => DoubleClicked?.Invoke(doubleAnchor));
                break;

            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                GetCursorPos(out var pt);
                _host.Dispatcher.TryEnqueue(() => ContextMenuRequested?.Invoke(pt.X, pt.Y));
                break;
        }
    }
}
