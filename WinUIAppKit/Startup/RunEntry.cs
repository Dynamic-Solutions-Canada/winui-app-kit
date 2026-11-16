namespace WinUIAppKit.Startup;

/// <summary>
/// The value of an unpackaged app's "start with Windows" entry under the HKCU Run key, and whether a
/// moved copy should re-point it (TRAY-10). Pure string logic, free of the registry, WinUI and logging,
/// so the portable test harness compiles it in.
/// </summary>
public static class RunEntry
{
    /// <summary>The entry's value: the quoted executable path, then the silent-launch argument.</summary>
    /// <param name="exePath">Full path of the executable.</param>
    /// <param name="silentArgument">The argument that marks an automatic launch, such as <c>--silent</c>; empty for none.</param>
    public static string Format(string exePath, string silentArgument) =>
        string.IsNullOrEmpty(silentArgument) ? $"\"{exePath}\"" : $"\"{exePath}\" {silentArgument}";

    /// <summary>
    /// The value to write so an existing entry starts this copy, or null to leave the entry alone.
    /// <list type="bullet">
    /// <item>No entry: null. Moving a copy is not consent to start it with Windows, so an entry is never created here.</item>
    /// <item>The entry already says exactly what <see cref="Format"/> would write (ignoring case): null.</item>
    /// <item>Anything else, such as another folder's copy or an older format: the current value.</item>
    /// </list>
    /// Re-pointing keeps the entry's name, so a choice the user made in Task Manager or Windows Settings
    /// (kept by Windows under that name) still applies.
    /// </summary>
    /// <param name="existingValue">The entry's current value, or null when there is none.</param>
    /// <param name="exePath">Full path of the running executable.</param>
    /// <param name="silentArgument">The silent-launch argument.</param>
    public static string? Repoint(string? existingValue, string exePath, string silentArgument)
    {
        if (existingValue is null)
        {
            return null;
        }

        string wanted = Format(exePath, silentArgument);
        return string.Equals(existingValue.Trim(), wanted, StringComparison.OrdinalIgnoreCase) ? null : wanted;
    }
}
