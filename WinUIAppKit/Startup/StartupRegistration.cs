using Microsoft.Win32;
using Windows.ApplicationModel;

namespace WinUIAppKit.Startup;

/// <summary>
/// "Start with Windows" for an app that runs packaged (manifest <c>StartupTask</c>) or unpackaged
/// (HKCU Run key with a silent-launch argument). It never overrides a user's or policy's choice to
/// disable the packaged startup task.
/// </summary>
public sealed class StartupRegistration
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly string _taskId;
    private readonly string _runValueName;
    private readonly string _silentArgument;

    /// <param name="startupTaskId">The manifest's <c>desktop:StartupTask TaskId</c>.</param>
    /// <param name="runValueName">The Run-key value name used when unpackaged.</param>
    /// <param name="silentArgument">Argument that marks a launch as automatic (no window).</param>
    public StartupRegistration(string startupTaskId, string runValueName, string silentArgument = "--silent")
    {
        _taskId = startupTaskId;
        _runValueName = runValueName;
        _silentArgument = silentArgument;
    }

    /// <summary>
    /// True when this launch came from sign-in (startup task activation or the silent argument), so the
    /// app should start in the tray without opening a window.
    /// </summary>
    public bool IsSilentLaunch()
    {
        if (Environment.GetCommandLineArgs().Any(a => a.Equals(_silentArgument, StringComparison.OrdinalIgnoreCase)))
            return true;
        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                   == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch { return false; }
    }

    /// <summary>Whether the app currently starts at sign-in.</summary>
    public async Task<bool> IsEnabledAsync()
    {
        if (!AppEnvironment.IsPackaged)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(_runValueName) != null;
        }
        var task = await StartupTask.GetAsync(_taskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>Turn starting at sign-in on or off. Returns the resulting state (the user or policy can refuse).</summary>
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (!AppEnvironment.IsPackaged)
        {
            SetRunValue(enabled);
            return enabled;
        }

        var task = await StartupTask.GetAsync(_taskId);
        if (!enabled)
        {
            task.Disable();
            return false;
        }
        var state = await task.RequestEnableAsync();
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>
    /// First-run registration: enable the startup task only while it is still in its initial Disabled
    /// state (never DisabledByUser or DisabledByPolicy). Failures are logged, not thrown.
    /// </summary>
    public async Task RegisterOnFirstRunAsync()
    {
        try
        {
            if (!AppEnvironment.IsPackaged)
            {
                SetRunValue(true);
                return;
            }
            var task = await StartupTask.GetAsync(_taskId);
            if (task.State == StartupTaskState.Disabled)
                await task.RequestEnableAsync();
        }
        catch (Exception ex) { Logger.Warn(ex, "First-run startup registration failed"); }
    }

    /// <summary>
    /// When packaged, remove Run-key entries left behind by earlier unpackaged builds, which would
    /// otherwise start a second (stale) copy at sign-in.
    /// </summary>
    /// <param name="legacyNames">Older Run value names to remove in addition to the current one.</param>
    public void RemoveUnpackagedEntries(params string[] legacyNames)
    {
        if (!AppEnvironment.IsPackaged) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            foreach (var name in legacyNames.Append(_runValueName))
            {
                if (key?.GetValue(name) == null) continue;
                key.DeleteValue(name);
                Logger.Info("Removed unpackaged Run entry {Name}", name);
            }
        }
        catch (Exception ex) { Logger.Warn(ex, "Couldn't clean unpackaged Run entries"); }
    }

    private void SetRunValue(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled) key.SetValue(_runValueName, $"\"{Environment.ProcessPath}\" {_silentArgument}");
        else if (key.GetValue(_runValueName) != null) key.DeleteValue(_runValueName);
    }
}
