using System.IO;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;

namespace WinUIAppKit.Settings;

/// <summary>
/// The "settings were reset" notice (CONF-2, UI-15): a warning bar for the top of the settings page,
/// shown when <see cref="SettingsLoadResult{T}.NeedsResetNotice"/> is true. A bar, not a dialog,
/// because it is standing status (UI-6). The user closes it.
/// </summary>
/// <remarks>
/// The store wrote defaults after the unreadable file, so the next launch loads normally and the
/// notice cannot come back. An app that starts silently must therefore open its settings window on the
/// launch that needs the notice, as it does on first run.
/// </remarks>
public static class SettingsResetNotice
{
    /// <summary>Create the notice, labelled in the current language and relabelled when it changes.</summary>
    /// <param name="backupPath">
    /// <see cref="SettingsLoadResult{T}.BackupPath"/>: where the unreadable file was kept. Only its file
    /// name is shown, never the folder, which can contain the user's name.
    /// </param>
    /// <returns>An open, closable warning bar.</returns>
    public static InfoBar Create(string? backupPath)
    {
        string file = string.IsNullOrEmpty(backupPath) ? "" : Path.GetFileName(backupPath);
        var bar = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = true,
        };

        void Label()
        {
            bar.Title = UiText.Get("kit.settings.reset-title");
            bar.Message = UiText.Format("kit.settings.reset-notice", ("file", file));
        }

        Label();
        bar.Loaded += (_, _) => UiText.Changed += Label;
        bar.Unloaded += (_, _) => UiText.Changed -= Label;
        return bar;
    }
}
