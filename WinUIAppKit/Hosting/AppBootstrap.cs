using Microsoft.UI.Xaml;

namespace WinUIAppKit.Hosting;

/// <summary>
/// The start of a resident app's <c>OnLaunched</c>, in the order every app needs: crash logging first,
/// then the app's settings, then its hidden host window (LOG-7, TRAY-7).
/// </summary>
public static class AppBootstrap
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Log every unhandled exception and flush the log, so a crash always leaves a trace (LOG-7). Covers
    /// both the app-domain and the WinUI unhandled-exception events.
    /// </summary>
    /// <param name="app">The app (<c>this</c> in <c>App</c>).</param>
    /// <param name="keepRunningAfterUiException">
    /// True to mark WinUI exceptions handled so the resident app keeps running, as the family's apps do;
    /// false to let WinUI end the process after logging.
    /// </param>
    public static void InstallCrashLogging(Application app, bool keepRunningAfterUiException = true)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Logger.Error(args.ExceptionObject as Exception, "Unhandled exception");
            NLog.LogManager.Flush();
        };

        app.UnhandledException += (_, args) =>
        {
            Logger.Error(args.Exception, "Unhandled UI exception");
            NLog.LogManager.Flush();
            args.Handled = keepRunningAfterUiException;
        };
    }

    /// <summary>
    /// Install crash logging, restore the app's settings, then create and activate its host window.
    /// Call it from <c>OnLaunched</c>; keep the returned window in a field.
    /// </summary>
    /// <param name="app">The app (<c>this</c> in <c>App</c>).</param>
    /// <param name="restoreSettings">Loads the app's settings, and anything that depends on them such as <c>UiText.Configure</c>.</param>
    /// <param name="createHost">Creates the app's host window, which sets itself up with <see cref="HostWindow"/>.</param>
    /// <returns>The activated host window.</returns>
    public static Window Launch(Application app, Action restoreSettings, Func<Window> createHost)
    {
        InstallCrashLogging(app);
        restoreSettings();

        var host = createHost();
        host.Activate();
        return host;
    }
}
