using Microsoft.UI.Xaml;
using WinRT.Interop;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Hosting;

/// <summary>
/// Turns a resident app's <c>MainWindow</c> into the hidden host that owns its tray icon (TRAY-7, D-074):
/// a 1x1 tool window parked off-screen, never in the taskbar or Alt+Tab, that cancels its own close.
/// It also answers a second launch's "show yourself" message, and runs the app's exit steps before
/// <see cref="Environment.Exit(int)"/>.
/// </summary>
public sealed class HostWindow
{
    /// <summary>Fixed subclass id for the host window procedure (0x7AA1 tray, 0x7AA2 flyout, 0x7AA3 host).</summary>
    private static readonly UIntPtr SubclassId = 0x7AA3;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Window _window;
    private readonly List<(string Name, Action Step)> _exitSteps = [];
    private readonly SUBCLASSPROC? _wndProc;   // held in a field so the GC can't collect the delegate
    private readonly int _showMessage;
    private bool _exiting;

    /// <summary>Hide <paramref name="window"/> and make it the app's host. Call from its constructor, after <c>InitializeComponent</c>.</summary>
    /// <param name="window">The app's host window.</param>
    /// <param name="title">
    /// The window title, "&lt;App&gt; Host" by convention. <see cref="SingleInstance.SignalRunningInstance"/>
    /// finds the running copy by this title, so the app passes the same string to both.
    /// </param>
    /// <param name="showMessageName">
    /// The registered window message a second launch posts (the app's value given to
    /// <see cref="SingleInstance.SignalRunningInstance"/>), or null when the app does not answer one.
    /// </param>
    public HostWindow(Window window, string title, string? showMessageName = null)
    {
        _window = window;
        _window.Title = title;
        Hwnd = WindowNative.GetWindowHandle(window);
        Hide();

        if (showMessageName is not null)
        {
            _showMessage = RegisterWindowMessage(showMessageName);
            _wndProc = WndProc;
            SetWindowSubclass(Hwnd, _wndProc, SubclassId, 0);
        }
    }

    /// <summary>The host window's handle, for <see cref="Tray.TrayIcon"/> and native menus.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>
    /// Raised on the UI thread when a second launch asks the running copy to show itself. The app
    /// decides what showing means (its settings window, its flyout).
    /// </summary>
    public event Action? ShowRequested;

    /// <summary>
    /// Add a step to run when the app exits, such as saving settings, closing a connection,
    /// unregistering notifications or removing the tray icon. Steps run in the order they were added.
    /// </summary>
    /// <param name="name">A short name for the log when the step fails.</param>
    /// <param name="step">The work to do. It runs on the calling thread before the process ends.</param>
    public void AddExitStep(string name, Action step) => _exitSteps.Add((name, step));

    /// <summary>
    /// Exit the app: run every exit step (a failing step is logged and the rest still run), flush the
    /// log, then end the process with <see cref="Environment.Exit(int)"/>. A second call while exiting
    /// does nothing.
    /// </summary>
    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Logger.Info("Exit requested");
        foreach (var (name, step) in _exitSteps)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exit step {Step} failed; continuing", name);
            }
        }

        NLog.LogManager.Flush();
        Environment.Exit(0);
    }

    /// <summary>Make the host invisible for good: tool window, out of switchers, 1x1 off-screen, never closed.</summary>
    private void Hide()
    {
        AddExStyle(Hwnd, WS_EX_TOOLWINDOW);
        var appWindow = _window.AppWindow;
        appWindow.IsShownInSwitchers = false;
        appWindow.Move(new global::Windows.Graphics.PointInt32(-9999, -9999));
        appWindow.Resize(new global::Windows.Graphics.SizeInt32(1, 1));
        appWindow.Closing += (_, e) => e.Cancel = true;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (_showMessage != 0 && (int)msg == _showMessage)
        {
            // Never show UI inside a window procedure: defer to the dispatcher.
            _window.DispatcherQueue.TryEnqueue(() => ShowRequested?.Invoke());
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
