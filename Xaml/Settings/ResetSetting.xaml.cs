using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Windowing;

namespace WinUIAppKit.Controls;

/// <summary>
/// The shared "Reset settings" row (CONF-4). Clicking it asks with <see cref="ConfirmDialog"/> (Cancel is
/// the default), then awaits <see cref="ResetAsync"/>, then raises <see cref="ResetCompleted"/> so the
/// page or window can show the defaults. The row knows nothing about what the app keeps: the app's
/// callback does the reset (usually <c>JsonSettingsStore.Reset</c>, carrying over anything it keeps, then
/// applying the new theme and language), and <see cref="KeptNote"/> tells the user what stays.
/// </summary>
public sealed partial class ResetSetting : UserControl
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Create the row. Set <see cref="Window"/> and <see cref="ResetAsync"/> before it is used.</summary>
    public ResetSetting()
    {
        InitializeComponent();
    }

    /// <summary>The settings window, which the confirmation is shown on.</summary>
    public Window? Window { get; set; }

    /// <summary>
    /// The app's reset, run after the user confirms: reset the store, carry over what the app keeps,
    /// apply the reset theme and language, and save. A failure is logged and nothing further happens.
    /// </summary>
    public Func<Task>? ResetAsync { get; set; }

    /// <summary>
    /// An optional extra line for the confirmation, saying what the reset keeps ("Your drive stays
    /// connected."). Read when the question opens, so it is in the current language; pass
    /// <c>() =&gt; UiText.Get("...")</c>.
    /// </summary>
    public Func<string>? KeptNote { get; set; }

    /// <summary>Raised on the UI thread after <see cref="ResetAsync"/> succeeds, so the host can show the defaults.</summary>
    public event Action? ResetCompleted;

    /// <summary>Ask, reset, then report. The button stays disabled until the run is over, so one click is one reset.</summary>
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window is null || ResetAsync is null)
        {
            Logger.Warn("ResetSetting has no Window or ResetAsync; nothing was reset");
            return;
        }

        ResetButton.IsEnabled = false;
        try
        {
            // The kit's sentence, then the app's note on what stays, as a paragraph of its own.
            string body = UiText.Get("kit.settings.reset-body");
            if (KeptNote?.Invoke() is { Length: > 0 } note)
            {
                body += "\n\n" + note;
            }

            bool confirmed = await ConfirmDialog.ConfirmAsync(
                Window,
                UiText.Get("kit.settings.reset-question"),
                body,
                UiText.Get("kit.settings.reset-confirm"));
            if (!confirmed)
            {
                return;
            }

            await ResetAsync();
            ResetCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Resetting the settings failed");
        }
        finally
        {
            ResetButton.IsEnabled = true;
        }
    }
}
