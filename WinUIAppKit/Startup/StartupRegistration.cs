using Microsoft.Win32;

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
        {
            return true;
        }

        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                   == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch { return false; }
    }

    /// <summary>Whether, and on whose authority, the app starts at sign-in.</summary>
    public async Task<StartupStatus> GetStatusAsync()
    {
        if (!AppEnvironment.IsPackaged)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(_runValueName) != null ? StartupStatus.Enabled : StartupStatus.Disabled;
        }

        var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(_taskId);
        return ToStatus(task.State);
    }

    /// <summary>
    /// Turn starting at sign-in on or off and return the resulting status. Windows can refuse: a user
    /// who turned the app off in Settings, or a policy, wins; the status says which, so the caller can
    /// explain instead of silently flipping the switch back.
    /// </summary>
    public async Task<StartupStatus> SetEnabledAsync(bool enabled)
    {
        if (!AppEnvironment.IsPackaged)
        {
            SetRunValue(enabled);
            return enabled ? StartupStatus.Enabled : StartupStatus.Disabled;
        }

        var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(_taskId);
        if (enabled)
        {
            return ToStatus(await task.RequestEnableAsync());
        }

        // Disable() has no effect on a policy-enabled task. Query afresh rather than trusting this
        // object's State, so the caller shows what Windows actually holds now.
        task.Disable();
        return await GetStatusAsync();
    }

    private static StartupStatus ToStatus(global::Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        global::Windows.ApplicationModel.StartupTaskState.Enabled => StartupStatus.Enabled,
        global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupStatus.EnabledByPolicy,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupStatus.DisabledByUser,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupStatus.DisabledByPolicy,
        _ => StartupStatus.Disabled,
    };

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
            var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(_taskId);
            if (task.State == global::Windows.ApplicationModel.StartupTaskState.Disabled)
            {
                await task.RequestEnableAsync();
            }
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
        if (!AppEnvironment.IsPackaged)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            foreach (var name in legacyNames.Append(_runValueName))
            {
                if (key?.GetValue(name) == null)
                {
                    continue;
                }

                key.DeleteValue(name);
                Logger.Info("Removed unpackaged Run entry {Name}", name);
            }
        }
        catch (Exception ex) { Logger.Warn(ex, "Couldn't clean unpackaged Run entries"); }
    }

    private void SetRunValue(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(_runValueName, $"\"{Environment.ProcessPath}\" {_silentArgument}");
        }
        else if (key.GetValue(_runValueName) != null)
        {
            key.DeleteValue(_runValueName);
        }
    }
}

/// <summary>Whether the app starts at sign-in, and who decided.</summary>
public enum StartupStatus
{
    /// <summary>Off; the app may turn it on.</summary>
    Disabled,

    /// <summary>On; the app may turn it off.</summary>
    Enabled,

    /// <summary>Turned off by the user in Windows Settings; only they can turn it back on there.</summary>
    DisabledByUser,

    /// <summary>Turned off by policy; the app can't change it.</summary>
    DisabledByPolicy,

    /// <summary>Turned on by policy; the app can't change it.</summary>
    EnabledByPolicy,
}

/// <summary>Helpers for <see cref="StartupStatus"/>.</summary>
public static class StartupStatusExtensions
{
    /// <summary>True when the app starts at sign-in.</summary>
    public static bool IsOn(this StartupStatus status) => status is StartupStatus.Enabled or StartupStatus.EnabledByPolicy;

    /// <summary>True when the app can change it (no user-in-Settings or policy decision in the way).</summary>
    public static bool CanChange(this StartupStatus status) => status is StartupStatus.Enabled or StartupStatus.Disabled;
}
