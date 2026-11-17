using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Pages;

namespace WinUIAppKit.XamlCompileCheck;

/// <summary>
/// Uses the shared XAML the way an app does, so a breaking change to the shell, the About page or their
/// library types fails the kit's own build. Never run.
/// </summary>
public partial class App : Application
{
    /// <summary>Create the app.</summary>
    public App() => InitializeComponent();

    /// <summary>Host the shell in a window and navigate to the About page, as an app's SettingsWindow does.</summary>
    /// <param name="args">Launch details.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var shell = new SettingsShell { Caption = "Compile check", IconPngPath = "C:\\icon.png" };
        shell.MenuItems.Add(new NavigationViewItem { Content = "Home", Tag = "home" });

        var window = new Window { Content = shell };
        Windowing.WindowChrome.SetUpSettingsWindow(window, shell.TitleBar, "C:\\icon.ico", null);

        var (source, support, license) = AboutInfo.GitHubLinks("owner", "repo");
        shell.PageSelected += tag =>
        {
            if (tag == SettingsShell.AboutTag)
            {
                shell.ContentFrame.Navigate(typeof(AboutPage), new AboutInfo
                {
                    ProductName = "Compile check",
                    DescriptionKey = "kit.about.updates",
                    IconPngPath = "C:\\icon.png",
                    PrivacyPolicy = new Uri("https://example.invalid/privacy"),
                    Updates = new Updates.UpdateService("owner", "repo"),
                    SourceCode = source,
                    Support = support,
                    License = license,
                });
            }
        };

        // The settings rows, as an app's Settings page uses them.
        var rows = new StackPanel();
        var startup = new Controls.StartupSetting { Registration = new Startup.StartupRegistration("CompileCheckStartup", "Compile check") };
        startup.StatusChanged += isOn => { };
        var theme = new Controls.ThemeSetting();
        theme.ChoiceChanged += choice => { };
        var language = new Controls.LanguageSetting();
        language.ChoiceChanged += identity => { };
        rows.Children.Add(startup);
        rows.Children.Add(theme);
        rows.Children.Add(language);

        // The reset notice, as a settings page shows it after an unreadable settings file.
        rows.Children.Insert(0, Settings.SettingsResetNotice.Create("C:\\data\\settings.json.bad"));

        // Selecting before the window has loaded, as a tray "Settings" entry does: the shell applies it on load.
        shell.Select(SettingsShell.SettingsTag);
        shell.Select(SettingsShell.AboutTag);
        window.Activate();
    }
}
