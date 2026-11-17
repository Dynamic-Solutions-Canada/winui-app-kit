using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WinUIAppKit.Localization;
using WinUIAppKit.Logging;
using WinUIAppKit.Updates;

namespace WinUIAppKit.Pages;

/// <summary>
/// The shared About page. Navigate to it with an <see cref="AboutInfo"/>; see that type for what each
/// card shows. Text built in code (version, update status, description) follows language changes live.
/// </summary>
public sealed partial class AboutPage : Page
{
    private AboutInfo? _info;
    private UpdateCheckResult? _lastCheck;

    /// <summary>Create the page; the Frame does this when the app navigates to it.</summary>
    public AboutPage()
    {
        InitializeComponent();
        Loaded += (_, _) => UiText.Changed += ShowText;
        Unloaded += (_, _) => UiText.Changed -= ShowText;
    }

    /// <summary>Fill the cards from the <see cref="AboutInfo"/> the app navigated with.</summary>
    /// <param name="e">Navigation details; its parameter must be an <see cref="AboutInfo"/>.</param>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _info = e.Parameter as AboutInfo
            ?? throw new ArgumentException("Navigate to AboutPage with an AboutInfo parameter.", nameof(e));

        ProductNameText.Text = _info.ProductName;
        try
        {
            AppIcon.Source = new BitmapImage(new Uri(_info.IconPngPath));
        }
        catch (Exception)
        {
            // A missing icon file leaves the image empty rather than failing the page.
        }

        UpdatesCard.Visibility = _info.Updates is null ? Visibility.Collapsed : Visibility.Visible;
        SetLink(SourceLink, _info.SourceCode);
        SetLink(PrivacyLink, _info.PrivacyPolicy);
        SetLink(SupportLink, _info.Support);
        SetLink(LicenseLink, _info.License);
        SetLink(NoticesLink, _info.ThirdPartyNotices);
        LogsPath.Text = AppEnvironment.PhysicalDataDirectory(AppLogging.DataFolderName);

        // Product-specific cards follow the standard ones (UI-5).
        foreach (var createCard in _info.ExtraCards)
        {
            Cards.Children.Add(createCard());
        }

        ShowText();
    }

    /// <summary>Show a link only when the app supplied its target.</summary>
    private static void SetLink(HyperlinkButton link, Uri? target)
    {
        link.NavigateUri = target;
        link.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The text built in code, redrawn when the language changes.</summary>
    private void ShowText()
    {
        if (_info is null)
        {
            return;
        }

        // The 4-part package version, marked when running unpackaged (UI-5).
        VersionText.Text = UiText.Format(
            AppEnvironment.IsPackaged ? "kit.about.version" : "kit.about.version-unpackaged",
            ("version", AppEnvironment.Version));
        DescriptionText.Text = _info.ResolveDescription();
        ShowUpdateStatus();
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_info?.Updates is not { } updates)
        {
            return;
        }

        CheckButton.IsEnabled = false;
        _lastCheck = null;
        UpdateStatus.Text = UiText.Get("kit.about.checking");
        ReleaseLink.Visibility = Visibility.Collapsed;
        try
        {
            _lastCheck = await updates.CheckForUpdatesAsync();
        }
        finally
        {
            CheckButton.IsEnabled = true;
            ShowUpdateStatus();
        }
    }

    /// <summary>The result line under "Updates", and the release link when a newer version exists.</summary>
    private void ShowUpdateStatus()
    {
        if (_lastCheck is null)
        {
            if (CheckButton.IsEnabled)
            {
                UpdateStatus.Text = "";
            }

            return;
        }

        UpdateStatus.Text = _lastCheck.Outcome switch
        {
            UpdateCheckOutcome.UpdateAvailable => UiText.Format("kit.about.update-available", ("version", _lastCheck.LatestVersion ?? "")),
            UpdateCheckOutcome.UpToDate => UiText.Get("kit.about.up-to-date"),
            UpdateCheckOutcome.OpenedStore => UiText.Get("kit.about.store-opened"),
            _ => UiText.Get("kit.about.check-failed"),
        };
        ReleaseLink.Visibility = _lastCheck.Outcome == UpdateCheckOutcome.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReleaseLink_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCheck is not null)
        {
            UpdateService.OpenReleasePage(_lastCheck);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => AppLogging.OpenLogsFolder();
}
