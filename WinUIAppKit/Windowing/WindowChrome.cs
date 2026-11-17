using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.IO;
using WinRT.Interop;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Windowing;

/// <summary>A saved window position and size in physical pixels (serialized in app settings).</summary>
public sealed class WindowPlacement
{
    /// <summary>Left edge in physical screen pixels.</summary>
    public int X { get; set; }

    /// <summary>Top edge in physical screen pixels.</summary>
    public int Y { get; set; }

    /// <summary>Width in physical pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height in physical pixels.</summary>
    public int Height { get; set; }
}

/// <summary>
/// Helpers for an app's main (normal, titled) window: crisp icons at the window's scaling, saved
/// placement that survives monitor changes, and bringing an existing window back to the front.
/// </summary>
public static class WindowChrome
{
    /// <summary>The window's Win32 handle.</summary>
    public static IntPtr Hwnd(Window window) => WindowNative.GetWindowHandle(window);

    /// <summary>The window's <see cref="AppWindow"/> (presenter, size, icon, title bar).</summary>
    public static AppWindow AppWindowOf(Window window) =>
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Hwnd(window)));

    /// <summary>Restore a minimized window and bring it to the foreground.</summary>
    public static void BringToFront(Window window)
    {
        if (AppWindowOf(window).Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
        SetForegroundWindow(Hwnd(window));
    }

    /// <summary>
    /// Set up an app's settings window (UI-1): Mica, content extended into a custom title bar, the
    /// window icon, a minimum size, the saved size and position, and the app's theme. Call from the
    /// window's constructor after <c>InitializeComponent</c>; the window usually hosts the shared <c>SettingsShell</c>.
    /// </summary>
    /// <param name="window">The settings window.</param>
    /// <param name="titleBar">The element that acts as the title bar (the shell's <c>TitleBar</c>).</param>
    /// <param name="icoPath">Full path of the app's multi-size .ico, for the title bar and taskbar.</param>
    /// <param name="saved">The saved placement from settings, or null for the default size, centred.</param>
    /// <param name="defaultWidthDip">Width when nothing is saved, in DIPs.</param>
    /// <param name="defaultHeightDip">Height when nothing is saved, in DIPs.</param>
    /// <param name="minWidthDip">The narrowest the user can make the window, in DIPs. At the default the
    /// shell's compact pane leaves pages about 450 wide.</param>
    /// <param name="minHeightDip">The shortest the user can make the window, in DIPs.</param>
    public static void SetUpSettingsWindow(Window window, UIElement titleBar, string icoPath, WindowPlacement? saved,
        double defaultWidthDip = 980, double defaultHeightDip = 720, double minWidthDip = 500, double minHeightDip = 500)
    {
        window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        WindowIcon.Apply(window, icoPath);
        SetMinimumSize(window, minWidthDip, minHeightDip);
        RestorePlacement(window, saved, defaultWidthDip, defaultHeightDip);
        Theming.AppTheme.Track(window);
    }

    /// <summary>
    /// Keep a window at least <paramref name="widthDip"/> × <paramref name="heightDip"/> DIPs. The
    /// presenter's limits are in physical pixels and follow only the main display's scaling
    /// (WindowsAppSDK issue 5251), so they are set from the window's own scale and set again whenever
    /// that scale changes, for example when the window moves to a monitor with different scaling.
    /// Does nothing for a window whose presenter is not overlapped.
    /// </summary>
    /// <param name="window">The window to limit.</param>
    /// <param name="widthDip">Minimum width in DIPs.</param>
    /// <param name="heightDip">Minimum height in DIPs.</param>
    public static void SetMinimumSize(Window window, double widthDip, double heightDip)
    {
        if (AppWindowOf(window).Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        void Apply(double scale)
        {
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(widthDip * scale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(heightDip * scale);
        }

        // The window's scale now; the content's XamlRoot reports later changes once it has loaded.
        Apply(GetDpiForWindow(Hwnd(window)) / 96.0);
        if (window.Content is FrameworkElement content)
        {
            // Loaded can fire again when the content is re-parented; listen to the root only once.
            var listening = false;
            content.Loaded += (_, _) =>
            {
                Apply(content.XamlRoot.RasterizationScale);
                if (!listening)
                {
                    listening = true;
                    content.XamlRoot.Changed += (root, _) => Apply(root.RasterizationScale);
                }
            };
        }
    }

    /// <summary>
    /// Size and position a window from saved placement, or centre it on the cursor's monitor at
    /// <paramref name="defaultWidthDip"/> × <paramref name="defaultHeightDip"/>. Saved placement that is
    /// no longer on any monitor (a display was removed) is ignored.
    /// </summary>
    public static void RestorePlacement(Window window, WindowPlacement? saved, double defaultWidthDip, double defaultHeightDip)
    {
        var appWindow = AppWindowOf(window);
        if (saved is { Width: >= 300, Height: >= 200 } && IsOnAMonitor(saved))
        {
            appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
            return;
        }

        GetCursorPos(out var pt);
        var (_, work, scale) = MonitorArea(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST));
        int width = Math.Min((int)(defaultWidthDip * scale), work.Right - work.Left);
        int height = Math.Min((int)(defaultHeightDip * scale), work.Bottom - work.Top);
        appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
            work.Left + (work.Right - work.Left - width) / 2,
            work.Top + (work.Bottom - work.Top - height) / 2,
            width, height));
    }

    /// <summary>
    /// The window's current placement, or null when it is minimized or maximized (saving those would
    /// restore an odd normal size next time).
    /// </summary>
    public static WindowPlacement? CapturePlacement(Window window)
    {
        var appWindow = AppWindowOf(window);
        if (appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return null;
        }

        return new WindowPlacement
        {
            X = appWindow.Position.X,
            Y = appWindow.Position.Y,
            Width = appWindow.Size.Width,
            Height = appWindow.Size.Height,
        };
    }

    private static bool IsOnAMonitor(WindowPlacement p)
    {
        // Require the title-bar strip to be visible, not just any corner of the window.
        var titleStrip = new RECT { Left = p.X, Top = p.Y, Right = p.X + p.Width, Bottom = p.Y + 40 };
        return MonitorFromRect(ref titleStrip, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }
}

/// <summary>
/// A window's title-bar and taskbar icon loaded at that window's scaling. Fixed 16/32 px frames get
/// stretched and look blurry at 125–250%. Re-applied on activation because WinUI resets it. Disposed
/// automatically when the window closes.
/// </summary>
public sealed class WindowIcon
{
    private IntPtr _small, _big;

    private WindowIcon() { }

    /// <summary>Apply a multi-size .ico file to the window (does nothing if the file is missing).</summary>
    public static void Apply(Window window, string icoPath)
    {
        if (!File.Exists(icoPath))
        {
            return;
        }

        var icon = new WindowIcon();
        var hwnd = WindowChrome.Hwnd(window);
        uint dpi = GetDpiForWindow(hwnd);
        int small = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        int big = GetSystemMetricsForDpi(SM_CXICON, dpi);
        icon._small = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, small, small, LR_LOADFROMFILE);
        icon._big = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, big, big, LR_LOADFROMFILE);

        // AppWindow.SetIcon copies the icon, so a temporary handle is enough there.
        var forAppWindow = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
        if (forAppWindow != IntPtr.Zero)
        {
            try { WindowChrome.AppWindowOf(window).SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(forAppWindow)); }
            finally { DestroyIcon(forAppWindow); }
        }

        icon.Send(hwnd);
        window.Activated += (_, _) => icon.Send(hwnd);
        window.Closed += (_, _) => icon.Destroy();
    }

    private void Send(IntPtr hwnd)
    {
        if (_small != IntPtr.Zero)
        {
            SendMessage(hwnd, WM_SETICON, ICON_SMALL, _small);
        }

        if (_big != IntPtr.Zero)
        {
            SendMessage(hwnd, WM_SETICON, ICON_BIG, _big);
        }
    }

    private void Destroy()
    {
        if (_small != IntPtr.Zero)
        {
            DestroyIcon(_small);
        }

        if (_big != IntPtr.Zero)
        {
            DestroyIcon(_big);
        }

        _small = _big = IntPtr.Zero;
    }
}
