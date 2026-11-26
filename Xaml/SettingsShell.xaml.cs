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

    /// <summary>The open pane's usual width (UI-1), and the narrowest it gets.</summary>
    private const double MinPaneLength = 240;

    /// <summary>
    /// The widest the open pane may grow to fit a long label, as wide as Windows' own Settings pane, so a
    /// page keeps most of the window.
    /// </summary>
    private const double MaxPaneLength = 320;

    /// <summary>
    /// Everything a left-pane item spends on something other than its label, in DIPs, from WinUI's
    /// NavigationViewItem template: 4 + 4 button margin, the 40 wide icon column, the label's 4 + 8 margin
    /// and the 14 the content grid keeps at its right. A label this much narrower than the pane fits.
    /// </summary>
    private const double ItemChromeWidth = 74;

    /// <summary>Spare width so layout rounding at 125% or 150% never clips the longest label's last pixel.</summary>
    private const double RoundingSlack = 2;

    /// <summary>The body text size of a navigation item's label.</summary>
    private const double ItemLabelFontSize = 14;

    /// <summary>A selection asked for before the navigation view had its items, applied once it loads.</summary>
    private string? _pendingTag;

    /// <summary>True while a pane width fit is queued, so a burst of changes runs it once.</summary>
    private bool _fitQueued;

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
            UiText.Changed += QueuePaneFit;
            LabelSettingsItem();
            FitPaneToLabels();
            if (_pendingTag is { } tag)
            {
                _pendingTag = null;
                Select(tag);
            }
        };
        Navigation.Unloaded += (_, _) =>
        {
            UiText.Changed -= LabelSettingsItem;
            UiText.Changed -= QueuePaneFit;
        };
        Navigation.ItemInvoked += Navigation_ItemInvoked;

        // Items the app adds later (a setup, a launcher) can be longer than the ones there now. Renaming an
        // item in place raises no change here; the next language change or added item refits.
        if (Navigation.MenuItems is global::Windows.Foundation.Collections.IObservableVector<object> items)
        {
            items.VectorChanged += (_, _) => QueuePaneFit();
        }
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
    /// Fit the pane after the current change has settled: a language change relabels every item from its
    /// own <see cref="UiText.Changed"/> handler, which may run after this one, so the fit waits its turn.
    /// </summary>
    private void QueuePaneFit()
    {
        if (_fitQueued)
        {
            return;
        }

        _fitQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _fitQueued = false;
            FitPaneToLabels();
        });
    }

    /// <summary>
    /// Widen the open pane so the longest label fits, from <see cref="MinPaneLength"/> up to
    /// <see cref="MaxPaneLength"/>; a label longer than that still trims. Translations such as
    /// "Nuestras otras aplicaciones" run past the usual 240, and WinUI clips a label rather than
    /// wrapping it. The compact rail and the threshold for the open pane are unchanged.
    /// </summary>
    private void FitPaneToLabels()
    {
        double widest = 0;
        var measure = new TextBlock { FontSize = ItemLabelFontSize };
        foreach (var item in Navigation.MenuItems.Concat(Navigation.FooterMenuItems).Append(Navigation.SettingsItem))
        {
            if (item is not NavigationViewItem { Content: string label } || string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            measure.Text = label;
            measure.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, measure.DesiredSize.Width);
        }

        Navigation.OpenPaneLength = Math.Clamp(Math.Ceiling(widest + ItemChromeWidth + RoundingSlack), MinPaneLength, MaxPaneLength);
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
