namespace WinUIAppKit.Logging;

/// <summary>
/// Connects the app to the shared NLog config the kit links into every app (LOG-1, LOG-2): logs go to
/// one file per launch in the app's data folder, %AppData%\&lt;data folder&gt;\logs.&lt;start time&gt;.txt,
/// keeping the current file and three older ones.
/// </summary>
public static class AppLogging
{
    /// <summary>The NLog global context key the shared NLog.config reads the folder name from.</summary>
    public const string DataFolderKey = "WinUIAppKit.DataFolderName";

    /// <summary>
    /// Name the app's data folder for the log files. Call it first at startup, before anything logs:
    /// until then the shared config falls back to the process name, which can be a different folder.
    /// Use the same name as <see cref="AppEnvironment.DataDirectory"/>, so logs sit beside settings.json.
    /// </summary>
    /// <param name="dataFolderName">The folder under %AppData%, such as the app's name. Not a path.</param>
    /// <exception cref="ArgumentException">The name is empty or contains path separators.</exception>
    public static void UseDataFolder(string dataFolderName)
    {
        // A path here would move logs out of %AppData% and widen NLog's cleanup of older log files.
        if (string.IsNullOrWhiteSpace(dataFolderName) || dataFolderName.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException("Pass a folder name, not a path.", nameof(dataFolderName));
        }

        NLog.GlobalDiagnosticsContext.Set(DataFolderKey, dataFolderName);
        _dataFolderName = dataFolderName;
    }

    /// <summary>
    /// Open the folder holding the log files in Explorer (LOG-8): the "Open logs folder" action on the
    /// About page and in the tray menu. Opens the real on-disk folder, since a packaged app's %AppData%
    /// is redirected and Explorer would not find the logs at the path the app uses.
    /// </summary>
    public static void OpenLogsFolder() =>
        AppEnvironment.OpenFolder(AppEnvironment.PhysicalDataDirectory(DataFolderName));

    /// <summary>The folder name given to <see cref="UseDataFolder"/>, or the process name the shared config falls back to.</summary>
    public static string DataFolderName => _dataFolderName ?? System.Diagnostics.Process.GetCurrentProcess().ProcessName;

    private static string? _dataFolderName;
}
