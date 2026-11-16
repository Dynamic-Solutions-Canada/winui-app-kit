using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WinUIAppKit;

/// <summary>
/// Facts about how the app is running (packaged or not, its version) and where its per-user data
/// lives. Packaging matters because MSIX redirects %AppData% writes into the package's own folder.
/// </summary>
public static class AppEnvironment
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Lazy<bool> IsPackagedLazy = new(DetectPackaged);

    /// <summary>True when running with MSIX package identity.</summary>
    public static bool IsPackaged => IsPackagedLazy.Value;

    private static bool DetectPackaged()
    {
        try { return global::Windows.ApplicationModel.Package.Current.Id != null; }
        catch { return false; }
    }

    /// <summary>
    /// Four-part version for display: the package version when packaged (dev builds bump only the
    /// package revision), otherwise the entry assembly's version.
    /// </summary>
    public static string Version
    {
        get
        {
            if (IsPackaged)
            {
                try
                {
                    var v = global::Windows.ApplicationModel.Package.Current.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
                catch { }
            }
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "";
        }
    }

    /// <summary>
    /// The app's data folder as the app itself sees it (%AppData%\<paramref name="folderName"/>).
    /// When packaged this path is transparently redirected, so use it for the app's own reads and writes.
    /// </summary>
    public static string DataDirectory(string folderName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), folderName);

    /// <summary>
    /// The real on-disk location of <see cref="DataDirectory"/>, for handing to another process such as
    /// Explorer. Packaged %AppData% writes land in the package's LocalCache\Roaming, which other
    /// processes don't see through the redirected path.
    /// </summary>
    public static string PhysicalDataDirectory(string folderName)
    {
        if (IsPackaged)
        {
            try
            {
                return Path.Combine(global::Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Roaming", folderName);
            }
            catch (Exception ex) { Logger.Warn(ex, "Couldn't resolve the packaged data folder"); }
        }
        return DataDirectory(folderName);
    }

    /// <summary>Open a folder in Explorer, creating it first so the window never errors.</summary>
    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Warn(ex, "Couldn't open folder {Path}", path); }
    }
}
