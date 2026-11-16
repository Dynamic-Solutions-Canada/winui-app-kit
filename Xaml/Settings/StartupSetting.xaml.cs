using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Startup;

namespace WinUIAppKit.Controls;

/// <summary>
/// The shared "Start with Windows" row. Set <see cref="Registration"/> before the control loads, and save
/// <see cref="StatusChanged"/>'s value in the app's settings. Windows may refuse a change (the user turned
/// the app off in Windows Settings, or a policy decides); the switch then shows the real state, is
/// disabled, and a note says where the decision lives.
/// </summary>
public sealed partial class StartupSetting : UserControl
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private bool _updating = true;

    /// <summary>Create the row; it reads Windows' state when it loads.</summary>
    public StartupSetting()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync(() => RegistrationOrThrow().GetStatusAsync());
    }

    /// <summary>The app's startup registration, the same object it uses at launch.</summary>
    public StartupRegistration? Registration { get; set; }

    /// <summary>Raised with whether the app starts with Windows, after every read or change, so the app can save it.</summary>
    public event Action<bool>? StatusChanged;

    private StartupRegistration RegistrationOrThrow() =>
        Registration ?? throw new InvalidOperationException("Set StartupSetting.Registration before the control loads.");

    private async void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        bool requested = Switch.IsOn;
        await RunAsync(() => RegistrationOrThrow().SetEnabledAsync(requested));
    }

    /// <summary>Run a read or change and show the outcome; after an error, show the state re-read from Windows.</summary>
    private async Task RunAsync(Func<Task<StartupStatus>> operation)
    {
        _updating = true;
        try
        {
            Show(await operation());
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Couldn't read or change the startup state");
            try
            {
                Show(await RegistrationOrThrow().GetStatusAsync());
            }
            catch (Exception readEx)
            {
                Logger.Warn(readEx, "Couldn't re-read the startup state");
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void Show(StartupStatus status)
    {
        Switch.IsOn = status.IsOn();
        Switch.IsEnabled = status.CanChange();

        string? note = status switch
        {
            StartupStatus.DisabledByUser => "kit.settings.startup-user-off",
            StartupStatus.DisabledByPolicy => "kit.settings.startup-policy-off",
            StartupStatus.EnabledByPolicy => "kit.settings.startup-policy-on",
            _ => null,
        };
        Note.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is not null)
        {
            Localize.SetText(Note, note);
        }

        StatusChanged?.Invoke(status.IsOn());
    }
}
