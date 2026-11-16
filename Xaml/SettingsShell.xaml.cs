using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUIAppKit.Localization;

namespace WinUIAppKit.Pages;

/// <summary>
/// The shared settings window shell (UI-1, UI-2). The app's <c>SettingsWindow</c> hosts it, passes
/// <see cref="TitleBar"/> to <c>WindowChrome.SetUpSettingsWindow</c>, adds its own pages to
/// <see cref="MenuItems"/> (main content first), and navigates <see cref="ContentFrame"/> when
/// <see cref="PageSelected"/> fires. The footer ("Our other apps", then "About") and the built-in
/// Settings item are fixed; nothing else goes in the footer.
/// </summary>
public sealed partial class SettingsShell : UserControl
{
    /// <summary>The tag reported for the built-in Settings item.</summary>
    public const string SettingsTag = "kit.settings";

    /// <summary>The tag of the "Our other apps" footer item; navigate to the promo page.</summary>
    public const string OtherAppsTag = "kit.other-apps";

    /// <summary>The tag of the "About" footer item; navigate to <see cref="AboutPage"/> with the app's <see cref="AboutInfo"/>.</summary>
    public const string AboutTag = "kit.about";

    /// <summary>Create the shell. Set <see cref="Caption"/> and <see cref="IconPngPath"/>, then add menu items.</summary>
    public SettingsShell()
    {
        InitializeComponent();

        // The built-in Settings item is labelled by WinUI in Windows' language; relabel it so it follows
        // the app's language choice like every other item.
        Loaded += (_, _) =>
        {
            UiText.Changed += LabelSettingsItem;
            LabelSettingsItem();
        };
        Unloaded += (_, _) => UiText.Changed -= LabelSettingsItem;
    }

    /// <summary>Raised when the user selects an item: the item's <c>Tag</c>, or <see cref="SettingsTag"/> for Settings.</summary>
    public event Action<string>? PageSelected;

    /// <summary>The element the window uses as its title bar.</summary>
    public UIElement TitleBar => TitleBarArea;

    /// <summary>The frame the app navigates to show a page.</summary>
    public Frame ContentFrame => Frame;

    /// <summary>The app's own navigation items, main content first (UI-2). Give each a string <c>Tag</c>.</summary>
    public IList<object> MenuItems => Navigation.MenuItems;

    /// <summary>The product name shown in the title bar (not translated).</summary>
    public string Caption
    {
        get => CaptionText.Text;
        set => CaptionText.Text = value;
    }

    /// <summary>Full path of the app's 256 px PNG icon, shown at 16 px in the title bar.</summary>
    public string IconPngPath
    {
        set
        {
            try
            {
                TitleBarIcon.Source = new BitmapImage(new Uri(value));
            }
            catch (Exception)
            {
                // A missing icon leaves the title bar without one rather than failing the window.
            }
        }
    }

    /// <summary>
    /// Select the item with <paramref name="tag"/> (or Settings for <see cref="SettingsTag"/>), which
    /// raises <see cref="PageSelected"/>. Returns false when no item has that tag.
    /// </summary>
    /// <param name="tag">An item's tag.</param>
    public bool Select(string tag)
    {
        if (tag == SettingsTag)
        {
            Navigation.SelectedItem = Navigation.SettingsItem;
            return true;
        }

        var item = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => candidate.Tag as string == tag);
        if (item is null)
        {
            return false;
        }

        Navigation.SelectedItem = item;
        return true;
    }

    /// <summary>The tag of the selected item, <see cref="SettingsTag"/> for Settings, or null.</summary>
    public string? SelectedTag =>
        Navigation.SelectedItem == Navigation.SettingsItem
            ? SettingsTag
            : (Navigation.SelectedItem as NavigationViewItem)?.Tag as string;

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (SelectedTag is { } tag)
        {
            PageSelected?.Invoke(tag);
        }
    }

    private void LabelSettingsItem()
    {
        if (Navigation.SettingsItem is NavigationViewItem settings)
        {
            settings.Content = UiText.Get("kit.settings.title");
        }
    }
}
