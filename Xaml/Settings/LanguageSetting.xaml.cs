using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;

namespace WinUIAppKit.Controls;

/// <summary>
/// The shared "Language" row for a localized app. It lists "Use system setting", then each of the app's
/// supported languages under its own name, so a user who cannot read the current language still finds
/// theirs. A choice is applied at once through <see cref="UiText.SetOverride"/> (open windows relabel
/// live) and raised through <see cref="ChoiceChanged"/> so the app can save it.
/// </summary>
public sealed partial class LanguageSetting : UserControl
{
    private bool _loading = true;

    /// <summary>Create the row from <see cref="UiText.Languages"/>, showing the current choice.</summary>
    public LanguageSetting()
    {
        InitializeComponent();
        Fill();
        _loading = false;
    }

    /// <summary>Raised after the user picks a language and it has been applied: the identity, or null to follow Windows.</summary>
    public event Action<string?>? ChoiceChanged;

    private void Fill()
    {
        var system = new ComboBoxItem { Tag = null };
        Localize.SetContent(system, "kit.settings.use-system-setting");
        Choices.Items.Add(system);

        foreach (string identity in UiText.Languages.Identities)
        {
            Choices.Items.Add(new ComboBoxItem
            {
                Content = UiText.Languages.NativeName(identity),
                Tag = identity,
                Language = identity,   // so the font and line breaking suit that language's script
            });
        }

        Choices.SelectedItem = Choices.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag as string == UiText.Override) ?? system;
    }

    private void Choices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Choices.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        string? choice = item.Tag as string;
        UiText.SetOverride(choice);
        ChoiceChanged?.Invoke(choice);
    }
}
