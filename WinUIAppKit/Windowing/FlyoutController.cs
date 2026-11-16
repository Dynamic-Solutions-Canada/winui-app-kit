using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Windowing;

/// <summary>
/// Turns an ordinary WinUI <see cref="Window"/> into a tray flyout: context-menu presenter (no title
/// bar), desktop acrylic, rounded DWM corners with a shadow, hidden from Alt+Tab, placed next to the taskbar beside its anchor, and dismissed on
/// deactivation or Esc.
/// <para>
/// It is never hidden with SW_HIDE: when closed it is cloaked and parked off-screen, so WinUI keeps its
/// composition surfaces and the next open is instant. The app supplies the content; the controller
/// only manages the window.
/// </para>
/// </summary>
public sealed class FlyoutController
{
    /// <summary>A click on the tray icon deactivates the flyout first; ignore a toggle that soon after.</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);
    private static readonly UIntPtr SubclassId = 0x7AA2;

    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly double _widthDip;
    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _wndProc;   // held in a field so the GC can't collect the delegate
    private bool _isShowing;
    private long _dismissedAtMs = long.MinValue / 2;   // Environment.TickCount64; immune to clock changes
    private (int X, int Y)? _anchor;
    private int _revealGeneration;   // a newer Show or a Dismiss cancels an earlier pending reveal

    /// <param name="window">The flyout window. Its content must be <paramref name="root"/>.</param>
    /// <param name="root">The element measured to size the flyout's height.</param>
    /// <param name="widthDip">Fixed flyout width in device-independent pixels.</param>
    public FlyoutController(Window window, FrameworkElement root, double widthDip)
    {
        _window = window;
        _root = root;
        _widthDip = widthDip;
        _hwnd = WindowNative.GetWindowHandle(window);

        ConfigureWindow();

        _wndProc = WndProc;
        SetWindowSubclass(_hwnd, _wndProc, SubclassId, 0);
        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && !_isShowing)
            {
                Dismiss();
            }
        };

        // Warm up: create the composition surfaces now, cloaked and parked off-screen.
        Cloak(_hwnd, true);
        AppWindow.Show(false);
        Park();
    }

    /// <summary>True while the flyout is on screen.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised just before the flyout is measured and shown, so content can be refreshed.</summary>
    public event Action? Opening;

    /// <summary>Raised after the flyout closes.</summary>
    public event Action? Dismissed;

    private AppWindow AppWindow => AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        _window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        AddExStyle(_hwnd, WS_EX_TOOLWINDOW);
    }

    // ── Show / hide ──────────────────────────────────────────────────────
    /// <summary>Open at the anchor, or close if open. Ignores the click that just dismissed it.</summary>
    public void Toggle(int anchorX, int anchorY)
    {
        if (IsOpen)
        {
            Dismiss();
            return;
        }
        if (Environment.TickCount64 - _dismissedAtMs < (long)ReopenGuard.TotalMilliseconds)
        {
            return;
        }

        Show(anchorX, anchorY);
    }

    /// <summary>Open (or move and re-size, if already open) at the anchor, ignoring the reopen guard.</summary>
    public void Show(int anchorX, int anchorY)
    {
        _isShowing = true;
        _anchor = (anchorX, anchorY);
        Opening?.Invoke();

        // Move and resize while still cloaked, then reveal only once XAML has drawn at the new size.
        // Uncloaking straight away shows a frame of bare (black) acrylic with the old layout in it.
        PlaceAtAnchor(anchorX, anchorY, SWP_SHOWWINDOW);
        IsOpen = true;
        RevealAfterRender();
    }

    /// <summary>
    /// Uncloak and focus the flyout after two rendered frames (one to lay out at the new size, one for
    /// that to reach the screen), with a 150 ms fallback so it can never stay invisible. Does nothing
    /// if the flyout was dismissed or shown again in the meantime.
    /// </summary>
    private void RevealAfterRender()
    {
        int generation = ++_revealGeneration;
        int frames = 0;
        bool done = false;
        var fallback = _window.DispatcherQueue.CreateTimer();

        void Reveal()
        {
            if (done)
            {
                return;
            }

            done = true;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            fallback.Stop();

            if (generation != _revealGeneration)
            {
                return;   // superseded by a newer Show
            }

            if (!IsOpen)
            {
                _isShowing = false;   // dismissed before it appeared
                return;
            }

            Cloak(_hwnd, false);
            SetForegroundWindow(_hwnd);
            SetFocus(_hwnd);

            // Activation messages from the show arrive asynchronously; only then may deactivation dismiss.
            _window.DispatcherQueue.TryEnqueue(() => _isShowing = false);
        }

        void OnRendering(object? sender, object e)
        {
            if (++frames >= 2)
            {
                Reveal();
            }
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
        fallback.Interval = TimeSpan.FromMilliseconds(150);
        fallback.IsRepeating = false;
        fallback.Tick += (_, _) => Reveal();
        fallback.Start();
    }

    /// <summary>Re-measure and re-place an open flyout after its content changed size.</summary>
    public void UpdateSize()
    {
        if (!IsOpen || _anchor is not { } a)
        {
            return;
        }

        PlaceAtAnchor(a.X, a.Y, 0);
    }

    /// <summary>Close the flyout (cloak and park it). Safe to call when already closed.</summary>
    public void Dismiss()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _dismissedAtMs = Environment.TickCount64;
        Park();
        Dismissed?.Invoke();
    }

    /// <summary>
    /// Cloak and move off-screen. Also call after display changes: Windows pulls off-screen windows
    /// back into view when the virtual screen shrinks.
    /// </summary>
    public void Park()
    {
        if (IsOpen)
        {
            return;
        }

        Cloak(_hwnd, true);

        // Keep the last shown size so the next open doesn't re-lay out from scratch. Before the first
        // open the window has no useful size yet, so start from the flyout width.
        var size = AppWindow.Size;
        int w = size.Width > 1 ? size.Width : (int)_widthDip;
        int h = size.Height > 1 ? size.Height : 400;
        var (x, y) = ParkingSpot(w, h);
        bool sized = size.Width > 1 && size.Height > 1;
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | (sized ? SWP_NOSIZE : 0u));
    }

    private void PlaceAtAnchor(int anchorX, int anchorY, uint extraFlags)
    {
        var monitor = MonitorFromPoint(new POINT { X = anchorX, Y = anchorY }, MONITOR_DEFAULTTONEAREST);
        var (bounds, work, scale) = MonitorArea(monitor);

        _root.Measure(new global::Windows.Foundation.Size(_widthDip, double.PositiveInfinity));
        int w = (int)Math.Ceiling(_widthDip * scale);
        int h = (int)Math.Ceiling(_root.DesiredSize.Height * scale);
        var (left, top) = Placement(anchorX, anchorY, w, h, bounds, work, scale);

        SetWindowPos(_hwnd, IntPtr.Zero, left, top, w, h, SWP_NOZORDER | SWP_NOACTIVATE | extraFlags);
    }

    /// <summary>Placement rules: beside a side taskbar, above a bottom one, below a top one, clamped to the work area.</summary>
    private static (int left, int top) Placement(int ax, int ay, int w, int h, RECT mon, RECT work, double scale)
    {
        int gap = (int)Math.Max(4, 8 * scale);
        int edge = (int)(16 * scale);
        int left = ax - w / 2;
        int top;

        bool taskbarLeft = work.Left > mon.Left && ax - work.Left < edge;
        bool taskbarRight = work.Right < mon.Right && work.Right - ax < edge;
        if (taskbarLeft)
        {
            left = work.Left + gap;
            top = ay - h / 2;
        }
        else if (taskbarRight)
        {
            left = work.Right - w - gap;
            top = ay - h / 2;
        }
        else if (work.Bottom - ay < edge * 3 || ay >= work.Bottom)
        {
            top = work.Bottom - h - gap;
        }
        else if (ay - work.Top < edge * 3 || ay <= work.Top)
        {
            top = work.Top + gap;
        }
        else
        {
            top = ay - h - gap;   // e.g. an icon in the tray overflow popup
        }

        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - h));
        return (left, top);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (msg == WM_KEYDOWN && (int)wParam == VK_ESCAPE)
        {
            _window.DispatcherQueue.TryEnqueue(Dismiss);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
