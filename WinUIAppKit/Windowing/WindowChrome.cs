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
            presenter.Restore();
        window.Activate();
        SetForegroundWindow(Hwnd(window));
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
        if (appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return null;
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
        if (!File.Exists(icoPath)) return;

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
        if (_small != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, ICON_SMALL, _small);
        if (_big != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, ICON_BIG, _big);
    }

    private void Destroy()
    {
        if (_small != IntPtr.Zero) DestroyIcon(_small);
        if (_big != IntPtr.Zero) DestroyIcon(_big);
        _small = _big = IntPtr.Zero;
    }
}
