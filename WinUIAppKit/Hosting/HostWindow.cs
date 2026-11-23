using Microsoft.UI.Xaml;
using WinRT.Interop;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Hosting;

/// <summary>
/// Turns a resident app's <c>MainWindow</c> into the hidden host that owns its tray icon (TRAY-7, D-074):
/// a 1x1 tool window parked off-screen, never in the taskbar or Alt+Tab, never activated (so it never
/// takes the foreground; see <see cref="AppBootstrap.Launch"/>), that cancels its own close.
/// It also answers a second launch's "show yourself" message, and runs the app's exit steps before
/// WinUI's orderly shutdown (<see cref="Application.Exit"/>), both when the app exits itself and when
/// Windows ends the session (shut down, restart, sign out).
/// </summary>
public sealed class HostWindow
{
    /// <summary>Fixed subclass id for the host window procedure (0x7AA1 tray, 0x7AA2 flyout, 0x7AA3 host).</summary>
    private static readonly UIntPtr SubclassId = 0x7AA3;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Window _window;
    private readonly List<(string Name, Action Step)> _exitSteps = [];
    private readonly SUBCLASSPROC _wndProc;   // held in a field so the GC can't collect the delegate
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
        }

        // Always subclassed, even without a show message, so the host hears the session ending.
        _wndProc = WndProc;
        SetWindowSubclass(Hwnd, _wndProc, SubclassId, 0);
    }

    /// <summary>The host window's handle, for <see cref="Tray.TrayIcon"/> and native menus.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>
    /// Raised on the UI thread when a second launch asks the running copy to show itself. The app
    /// decides what showing means (its settings window, its flyout).
    /// </summary>
    public event Action? ShowRequested;

    /// <summary>
    /// Whether the exit steps run when Windows ends the session (shut down, restart, sign out, or Windows
    /// closing the app to update files it uses). On by default; an app turns it off only when its steps must not run
    /// then. Without it, the process ends with no exit steps at all. Read when the message arrives, so
    /// it can change at any time.
    /// </summary>
    public bool ExitOnSessionEnd { get; set; } = true;

    /// <summary>
    /// Add a step to run when the app exits, such as saving settings, closing a connection,
    /// unregistering notifications or removing the tray icon. Steps run in the order they were added.
    /// </summary>
    /// <param name="name">A short name for the log when the step fails.</param>
    /// <param name="step">The work to do. It runs on the calling thread before the process ends.</param>
    public void AddExitStep(string name, Action step) => _exitSteps.Add((name, step));

    /// <summary>Windows <see cref="Exit"/> closes before WinUI's shutdown; each leaves the list when it closes.</summary>
    private static readonly List<Window> WindowsToClose = [];

    /// <summary>
    /// Have <see cref="Exit"/> close <paramref name="window"/> before shutting WinUI down. WinUI's
    /// <see cref="Application.Exit"/> never finishes while a window it did not close is still open, and
    /// it does not close windows that were created but never activated, such as a flyout or an
    /// on-screen popup that is only ever shown without focus. Such windows must be registered here, or
    /// every exit waits for the backstop. The kit registers its own (settings windows, the flyout);
    /// call it for any other window the app keeps open. A window that closes earlier drops out, and
    /// registering one twice is harmless. Call it on the UI thread.
    /// </summary>
    /// <param name="window">A window the app keeps for its lifetime.</param>
    public static void CloseOnExit(Window window)
    {
        if (WindowsToClose.Contains(window))
        {
            return;
        }

        WindowsToClose.Add(window);
        window.Closed += (_, _) => WindowsToClose.Remove(window);
    }

    /// <summary>How long the orderly shutdown may take before the process is ended anyway.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Exit the app: run every exit step (a failing step is logged and the rest still run), flush the
    /// log, close the windows registered with <see cref="CloseOnExit"/>, then shut WinUI down in order with
    /// <see cref="Application.Exit"/>, which ends the message loop and the process.
    /// <para>
    /// Ending the process outright from the UI thread (<see cref="Environment.Exit(int)"/>) leaves the
    /// dispatcher and composition objects to the OS, and Windows App SDK 2 on Windows 10 answers that with
    /// a CoreMessaging fail-fast. <see cref="Application.Exit"/> never finishes while one of those windows
    /// is open, so they are closed first. If the shutdown still hasn't ended the process within
    /// <see cref="ShutdownGrace"/> (a window of the app's own that stays open, say), it is ended from a
    /// background thread, which does not crash, and the log says so.
    /// </para>
    /// <para>
    /// Exit returns while the app shuts down, so nothing should follow it. A second call does nothing.
    /// </para>
    /// </summary>
    public void Exit()
    {
        if (!BeginExit("Exit requested"))
        {
            return;
        }

        ShutDown();
    }

    /// <summary>
    /// Mark the app as exiting, log <paramref name="reason"/>, then run every exit step in order (a
    /// failing step is logged and the rest still run) and flush the log.
    /// </summary>
    /// <param name="reason">The log line saying why the app is exiting.</param>
    /// <returns>False when an exit had already begun, in which case nothing ran.</returns>
    private bool BeginExit(string reason)
    {
        if (_exiting)
        {
            return false;
        }

        _exiting = true;
        Logger.Info(reason);
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
        return true;
    }

    /// <summary>
    /// Shut WinUI down in order once the exit steps have run: start the backstop that ends a stalled
    /// shutdown, close the windows registered with <see cref="CloseOnExit"/>, then <see cref="Application.Exit"/>.
    /// </summary>
    private void ShutDown()
    {
        // A background thread never keeps the process alive, so this only fires if the shutdown stalls.
        var backstop = new Thread(() =>
        {
            Thread.Sleep(ShutdownGrace);
            Logger.Warn("Orderly shutdown took longer than {Seconds} s; ending the process", ShutdownGrace.TotalSeconds);
            NLog.LogManager.Flush();
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "Exit backstop",
        };
        backstop.Start();

        foreach (var window in WindowsToClose.ToList())
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                // A window already torn down can refuse; the rest still close.
                Logger.Warn(ex, "Could not close a window before exit; continuing");
            }
        }

        Application.Current.Exit();
    }

    /// <summary>Make the host invisible for good: tool window, out of switchers, 1x1 off-screen, never closed.</summary>
    private void Hide()
    {
        AddExStyle(Hwnd, WS_EX_TOOLWINDOW);
        var appWindow = _window.AppWindow;
        appWindow.IsShownInSwitchers = false;
        appWindow.Move(new global::Windows.Graphics.PointInt32(-9999, -9999));
        appWindow.Resize(new global::Windows.Graphics.SizeInt32(1, 1));
        // The host refuses every close except the app's own exit.
        appWindow.Closing += (_, e) => e.Cancel = !_exiting;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (_showMessage != 0 && (int)msg == _showMessage)
        {
            // Never show UI inside a window procedure: defer to the dispatcher.
            _window.DispatcherQueue.TryEnqueue(() => ShowRequested?.Invoke());
            return IntPtr.Zero;
        }

        // wParam FALSE means a shutdown was cancelled, which needs nothing: let it fall through.
        if (msg == WM_ENDSESSION && wParam != IntPtr.Zero && ExitOnSessionEnd)
        {
            OnSessionEnd(lParam);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Windows is ending the session, or closing the app so an update can replace
    /// files it uses (WM_ENDSESSION with wParam TRUE; ENDSESSION_CLOSEAPP, from Restart Manager).
    /// <para>
    /// The exit steps run here, before the window procedure returns, because Windows may end the
    /// process as soon as it does, and waits only a few seconds for it to. The WinUI shutdown that
    /// follows closes windows, so it is deferred to the dispatcher; when the session really is ending,
    /// Windows usually ends the process first, which is fine once the steps have run. When Restart Manager
    /// closes the app without ending the session, that deferred shutdown is what ends it.
    /// </para>
    /// </summary>
    /// <param name="lParam">The WM_ENDSESSION flags (ENDSESSION_CLOSEAPP, _CRITICAL, _LOGOFF), logged as is.</param>
    private void OnSessionEnd(IntPtr lParam)
    {
        if (!BeginExit($"Windows is ending the session (flags 0x{lParam:X}); running the exit steps"))
        {
            return;
        }

        // A refused enqueue would leave the app running with its tray icon gone and no backstop.
        if (!_window.DispatcherQueue.TryEnqueue(ShutDown))
        {
            ShutDown();
        }
    }
}
