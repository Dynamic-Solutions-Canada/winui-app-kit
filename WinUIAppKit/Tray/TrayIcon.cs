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
/// button-up of the double-click is swallowed so it can't toggle anything a second time.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const uint IconUid = 1;
    private static readonly UIntPtr SubclassId = 0x7AA1;

    private readonly IntPtr _hwnd;
    private readonly Guid _guid;
    private readonly DispatcherQueue _dispatcher;
    private readonly SUBCLASSPROC _wndProc;   // held in a field so the GC can't collect the delegate
    private readonly int _wmCallback, _wmTaskbarCreated;

    private IntPtr _hIcon;
    private string _tooltip = "";
    private bool _added, _swallowNextUp, _disposed;

    /// <param name="hostHwnd">The window that receives tray messages (lives as long as the icon).</param>
    /// <param name="id">Stable per-app GUID. Never reuse another app's GUID: Windows treats them as the same icon.</param>
    /// <param name="callbackMessageName">App-unique name for the registered callback message.</param>
    public TrayIcon(IntPtr hostHwnd, Guid id, string callbackMessageName)
    {
        _hwnd = hostHwnd;
        _guid = id;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _wmCallback = RegisterWindowMessage(callbackMessageName);
        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _wndProc = WndProc;
        SetWindowSubclass(_hwnd, _wndProc, SubclassId, 0);
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
        if (_disposed) return;
        var size = IconPixelSize;
        var newIcon = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0);
        if (newIcon == IntPtr.Zero) Logger.Warn("CreateIconFromResourceEx failed");

        var oldIcon = _hIcon;
        _hIcon = newIcon;
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        if (_added)
        {
            var data = Data();
            if (!Shell_NotifyIcon(NIM_MODIFY, ref data)) Add();
        }
        else
        {
            Add();
        }

        if (oldIcon != IntPtr.Zero) DestroyIcon(oldIcon);
    }

    /// <summary>The icon's centre on screen, or the cursor position if the shell can't report it.</summary>
    public TrayAnchor GetAnchor()
    {
        GetCursorPos(out var pt);
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconUid,
            guidItem = _guid,
        };
        if (Shell_NotifyIconGetRect(ref id, out var r) == 0 && r.Right > r.Left && r.Bottom > r.Top)
            return new TrayAnchor((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        return new TrayAnchor(pt.X, pt.Y);
    }

    /// <summary>Remove the icon from the tray and stop listening. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Delete();
        RemoveWindowSubclass(_hwnd, _wndProc, SubclassId);
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        _hIcon = IntPtr.Zero;
    }

    // ── Shell registration ───────────────────────────────────────────────
    private NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconUid,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID,
        uCallbackMessage = (uint)_wmCallback,
        hIcon = _hIcon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
        guidItem = _guid,
    };

    private void Add()
    {
        // A stale registration for the same GUID (crash, previous run) makes NIM_ADD fail.
        Delete();
        var data = Data();
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added) Logger.Warn("Shell_NotifyIcon(NIM_ADD) failed");
    }

    private void Delete()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            uFlags = NIF_GUID,
            guidItem = _guid,
            szTip = "",
            szInfo = "",
            szInfoTitle = "",
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
    }

    // ── Window procedure ─────────────────────────────────────────────────
    // Work is deferred with TryEnqueue: showing windows or menus from inside the window procedure
    // re-enters the message loop and misbehaves.
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (_wmCallback != 0 && (int)msg == _wmCallback)
        {
            HandleTrayNotification((int)(lParam.ToInt64() & 0xFFFF));
            return IntPtr.Zero;
        }

        if (_wmTaskbarCreated != 0 && (int)msg == _wmTaskbarCreated)
        {
            // Explorer restarted: the icon is gone and must be added again.
            _dispatcher.TryEnqueue(Add);
        }
        else if (msg == WM_DISPLAYCHANGE)
        {
            _dispatcher.TryEnqueue(() =>
            {
                DisplayChanged?.Invoke();
                RenderNeeded?.Invoke();
            });
        }
        else if (msg == WM_DPICHANGED || msg == WM_SETTINGCHANGE)
        {
            _dispatcher.TryEnqueue(() => RenderNeeded?.Invoke());
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void HandleTrayNotification(int notification)
    {
        switch (notification)
        {
            case WM_LBUTTONUP:
                if (_swallowNextUp)
                {
                    _swallowNextUp = false;
                    return;
                }
                var clickAnchor = GetAnchor();
                _dispatcher.TryEnqueue(() => Clicked?.Invoke(clickAnchor));
                break;

            case WM_LBUTTONDBLCLK:
                _swallowNextUp = true;
                var doubleAnchor = GetAnchor();
                _dispatcher.TryEnqueue(() => DoubleClicked?.Invoke(doubleAnchor));
                break;

            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                GetCursorPos(out var pt);
                _dispatcher.TryEnqueue(() => ContextMenuRequested?.Invoke(pt.X, pt.Y));
                break;
        }
    }
}
