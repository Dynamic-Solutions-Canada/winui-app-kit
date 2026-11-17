using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    /// <summary>A selection asked for before the navigation view had its items, applied once it loads.</summary>
    private string? _pendingTag;

    /// <summary>Create the shell. Set <see cref="Caption"/> and <see cref="IconPngPath"/>, then add menu items.</summary>
    public SettingsShell()
    {
        InitializeComponent();

        // The built-in Settings item only exists once the navigation view's template has loaded. Relabel
        // it then so it follows the app's language like every other item, and apply any selection that
        // was asked for earlier.
        Navigation.Loaded += (_, _) =>
        {
            UiText.Changed += LabelSettingsItem;
            LabelSettingsItem();
            if (_pendingTag is { } tag)
            {
                _pendingTag = null;
                Select(tag);
            }
        };
        Navigation.Unloaded += (_, _) => UiText.Changed -= LabelSettingsItem;
        Navigation.ItemInvoked += Navigation_ItemInvoked;
    }

    /// <summary>Raised when the user selects an item: the item's <c>Tag</c>, or <see cref="SettingsTag"/> for Settings.</summary>
    public event Action<string>? PageSelected;

    /// <summary>
    /// Raised with an action item's <c>Tag</c> when the user invokes it. An action item is a menu item the
    /// app adds with <c>SelectsOnInvoked="False"</c> ("Add ..."): it runs something instead of opening a
    /// page, so it is never selected and never raises <see cref="PageSelected"/>.
    /// </summary>
    public event Action<string>? ActionInvoked;

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
    /// raises <see cref="PageSelected"/>. Returns false when no item has that tag, or the item is an action item. It can be called before
    /// the window has loaded (a tray "Settings" entry, or the settings reset notice, opening the window
    /// straight on a page): the selection is then applied when the shell loads, so callers need no
    /// ordering of their own.
    /// </summary>
    /// <param name="tag">An item's tag.</param>
    public bool Select(string tag)
    {
        if (tag == SettingsTag)
        {
            if (Navigation.SettingsItem is null)
            {
                // Not loaded yet: the built-in item doesn't exist, so select it on load.
                _pendingTag = tag;
                return Navigation.IsSettingsVisible;
            }

            Navigation.SelectedItem = Navigation.SettingsItem;
            return true;
        }

        var item = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => candidate.Tag as string == tag);
        if (item is null || !item.SelectsOnInvoked)
        {
            // No such item, or an action item, which is invoked rather than selected.
            return false;
        }

        if (!Navigation.IsLoaded)
        {
            _pendingTag = tag;
            return true;
        }

        Navigation.SelectedItem = item;
        return true;
    }

    /// <summary>The tag of the selected item, <see cref="SettingsTag"/> for Settings, or null.</summary>
    public string? SelectedTag =>
        Navigation.SettingsItem is not null && Navigation.SelectedItem == Navigation.SettingsItem
            ? SettingsTag
            : (Navigation.SelectedItem as NavigationViewItem)?.Tag as string;

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (SelectedTag is { } tag)
        {
            PageSelected?.Invoke(tag);
        }
    }

    /// <summary>Report an action item (one that doesn't select) to the app; pages are reported by selection.</summary>
    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { SelectsOnInvoked: false, Tag: string tag })
        {
            ActionInvoked?.Invoke(tag);
        }
    }

    /// <summary>
    /// The built-in Settings item's label, tooltip and screen-reader name, all from the kit's label: WinUI
    /// sets them in Windows' language, which differs when the user chose another language (UI-14).
    /// </summary>
    private void LabelSettingsItem()
    {
        if (Navigation.SettingsItem is NavigationViewItem settings)
        {
            string label = UiText.Get("kit.settings.title");
            settings.Content = label;
            ToolTipService.SetToolTip(settings, label);
            AutomationProperties.SetName(settings, label);
        }
    }
}
