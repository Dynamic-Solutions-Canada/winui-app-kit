using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Theming;

namespace WinUIAppKit.Controls;

/// <summary>
/// The shared "App theme" row. It shows <see cref="AppTheme.Current"/>, applies a new choice to every
/// open window through <see cref="AppTheme.Set"/>, and raises <see cref="ChoiceChanged"/> so the app can
/// save it (stored as 0, 1, 2, UI-4).
/// </summary>
public sealed partial class ThemeSetting : UserControl
{
    private bool _loading = true;

    /// <summary>Create the row, showing the current theme.</summary>
    public ThemeSetting()
    {
        InitializeComponent();
        Choices.SelectedIndex = Math.Clamp((int)AppTheme.Current, 0, 2);
        _loading = false;
    }

    /// <summary>Raised after the user picks a theme and it has been applied.</summary>
    public event Action<AppThemeChoice>? ChoiceChanged;

    private void Choices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Choices.SelectedIndex < 0)
        {
            return;
        }

        var choice = (AppThemeChoice)Choices.SelectedIndex;
        AppTheme.Set(choice);
        ChoiceChanged?.Invoke(choice);
    }
}
