using System.Runtime.Versioning;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Windowing;

/// <summary>
/// Turns an ordinary WinUI <see cref="Window"/> into an on-screen display like Windows' volume popup:
/// bottom centre of the main display, above everything, never activated, click-through and hidden from
/// Alt+Tab. <see cref="Show"/> brings it up (or keeps it up) for a hold time, after which it fades and
/// is parked off-screen, cloaked, so the next one appears instantly.
/// <para>
/// Its look follows the system: desktop acrylic with rounded corners on Windows 11; on Windows 10, square
/// corners and a solid background in the window's theme (the root panel's own background, if it set
/// one, is kept). The app supplies the content and calls <see cref="Show"/>; the controller manages the
/// window, and registers it with <see cref="Hosting.HostWindow.CloseOnExit"/>.
/// </para>
/// </summary>
public sealed class OsdController
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Window _window;
    private readonly Panel _root;
    private readonly double _minWidthDip, _maxWidthDip, _heightDip;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _hold, _fade;
    private int _fadeStep;

    /// <param name="window">The popup window. Its content must be <paramref name="root"/>.</param>
    /// <param name="root">The content's root panel: measured for the width, named for screen readers,
    /// and given the solid background on Windows 10.</param>
    /// <param name="heightDip">The popup's height in DIPs.</param>
    /// <param name="minWidthDip">Its narrowest width in DIPs; the content's desired width beyond this widens it.</param>
    /// <param name="maxWidthDip">Its widest width in DIPs; null for a fixed width of <paramref name="minWidthDip"/>.</param>
    public OsdController(Window window, Panel root, double heightDip, double minWidthDip, double? maxWidthDip = null)
    {
        _window = window;
        _root = root;
        _heightDip = heightDip;
        _minWidthDip = minWidthDip;
        _maxWidthDip = maxWidthDip ?? minWidthDip;
        _hwnd = WindowNative.GetWindowHandle(window);

        ConfigureWindow();

        // Created once and never activated: WinUI's shutdown would wait for it otherwise.
        Hosting.HostWindow.CloseOnExit(window);

        _hold = window.DispatcherQueue.CreateTimer();
        _hold.IsRepeating = false;
        _hold.Tick += (_, _) => StartFade();
        _fade = window.DispatcherQueue.CreateTimer();
        _fade.Interval = TimeSpan.FromMilliseconds(OsdLayout.FadeStepMs);
        _fade.Tick += (_, _) => FadeTick();

        // Warm up cloaked and parked off-screen so the first popup is composed instantly.
        Cloak(_hwnd, true);
        AppWindow.Show(false);
        Park();
    }

    /// <summary>True from <see cref="Show"/> until the fade finishes.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>Raised after the popup has faded out and been parked, for example to stop an animation.</summary>
    public event Action? Hidden;

    private AppWindow AppWindow => AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));

    /// <summary>
    /// Bring the popup up with its current content, or keep it up, and restart its hold. Set the content
    /// first: the width is measured from it.
    /// </summary>
    /// <param name="hold">How long it stays after this call before fading.</param>
    /// <param name="accessibleName">What screen readers hear for it, when its content has no visible text to read.</param>
    public void Show(TimeSpan hold, string? accessibleName = null)
    {
        if (accessibleName != null)
        {
            AutomationProperties.SetName(_root, accessibleName);
        }

        _fade.Stop();
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
        IsVisible = true;
        Reposition();

        _hold.Stop();
        _hold.Interval = hold;
        _hold.Start();
    }

    /// <summary>Start fading now instead of waiting for the hold. Does nothing while hidden.</summary>
    public void Hide()
    {
        if (!IsVisible)
        {
            return;
        }

        _hold.Stop();
        StartFade();
    }

    /// <summary>
    /// Place the popup bottom centre of the main display, or re-park it while hidden. Call on display
    /// changes too: Windows pulls off-screen windows back into view when displays change.
    /// </summary>
    public void Reposition()
    {
        if (!IsVisible)
        {
            Park();
            return;
        }

        var (bounds, _, scale) = PrimaryMonitorArea();
        _root.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, _heightDip));
        double widthDip = OsdLayout.Width(_root.DesiredSize.Width, _minWidthDip, _maxWidthDip);
        var (x, y, w, h) = OsdLayout.Place(bounds.Left, bounds.Right, bounds.Bottom, scale, widthDip, _heightDip);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Cloak(_hwnd, false);
    }

    // ── Window setup ─────────────────────────────────────────────────────

    /// <summary>Acrylic and rounded corners need Windows 11; Windows 10 gets the solid look.</summary>
    [SupportedOSPlatformGuard("windows10.0.22000")]
    private static bool UsesAcrylic() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
        && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        if (UsesAcrylic())
        {
            _window.SystemBackdrop = new DesktopAcrylicBackdrop();
            RoundCorners(_hwnd);
        }
        else
        {
            Logger.Info("On-screen display: solid background (acrylic and rounded corners need Windows 11)");

            // A background the app set on its root wins; otherwise the kit's follows the theme.
            if (_root.Background == null)
            {
                ApplySolidBackground();
                _root.ActualThemeChanged += (_, _) => ApplySolidBackground();
            }
        }

        var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        AddExStyle(_hwnd, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
    }

    /// <summary>Ask DWM for rounded corners. The attribute is Windows 11 only, so callers check the build first.</summary>
    [SupportedOSPlatform("windows10.0.22000")]
    private static void RoundCorners(IntPtr hwnd)
    {
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    /// <summary>
    /// The Windows 10 background: Fluent's solid base colour for the root's current theme, or the system
    /// window colour in high contrast. Set in code because a brush looked up from code follows the app's
    /// theme, not this window's (AppTheme.Track sets the window's).
    /// </summary>
    private void ApplySolidBackground()
    {
        if (new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            && Application.Current.Resources.TryGetValue("SystemColorWindowColor", out object? system)
            && system is global::Windows.UI.Color systemColor)
        {
            _root.Background = new SolidColorBrush(systemColor);
            return;
        }

        bool dark = _root.ActualTheme == ElementTheme.Dark;
        _root.Background = new SolidColorBrush(dark
            ? global::Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20)
            : global::Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
    }

    // ── Hold, fade and park ──────────────────────────────────────────────

    private void StartFade()
    {
        _fadeStep = 0;
        _fade.Start();
    }

    private void FadeTick()
    {
        _fadeStep++;
        SetLayeredWindowAttributes(_hwnd, 0, OsdLayout.FadeAlpha(_fadeStep), LWA_ALPHA);
        if (_fadeStep < OsdLayout.FadeSteps)
        {
            return;
        }

        _fade.Stop();
        IsVisible = false;
        Park();
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
        Hidden?.Invoke();
    }

    /// <summary>Cloak and move off-screen, keeping the window's composition surfaces.</summary>
    private void Park()
    {
        Cloak(_hwnd, true);
        int w = (int)Math.Ceiling(_minWidthDip), h = (int)Math.Ceiling(_heightDip);
        var (x, y) = ParkingSpot(w, h);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
