using Microsoft.UI.Xaml;

namespace WinUIAppKit.Theming;

/// <summary>The user's theme choice. Values match the 0/1/2 integers older settings files stored.</summary>
public enum AppThemeChoice
{
    /// <summary>Follow Windows' app mode.</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,
}

/// <summary>
/// Holds the app-wide theme choice and tells open windows when it changes. Persisting the choice is
/// the app's job (it owns the settings file); call <see cref="Set"/> after loading and on every edit.
/// </summary>
public static class AppTheme
{
    private static readonly global::Windows.UI.ViewManagement.UISettings UiSettings = new();

    /// <summary>The current choice.</summary>
    public static AppThemeChoice Current { get; private set; }

    /// <summary>Raised on the UI thread after <see cref="Set"/> changes the choice.</summary>
    public static event Action? Changed;

    /// <summary>Change the choice and notify listeners. A no-op when the value is unchanged.</summary>
    public static void Set(AppThemeChoice choice)
    {
        if (choice == Current)
        {
            return;
        }

        Current = choice;
        Changed?.Invoke();
    }

    /// <summary>The WinUI theme for a choice (Default follows Windows' app mode).</summary>
    public static ElementTheme ToElementTheme(AppThemeChoice choice) => choice switch
    {
        AppThemeChoice.Light => ElementTheme.Light,
        AppThemeChoice.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Apply the current choice to a window's root element now and whenever it changes.</summary>
    public static void Track(Window window)
    {
        void Apply()
        {
            if (window.Content is FrameworkElement root)
            {
                root.RequestedTheme = ToElementTheme(Current);
            }
        }

        Apply();
        Changed += Apply;
        window.Closed += (_, _) => Changed -= Apply;
    }

    /// <summary>True when the effective app theme is dark (the explicit choice, else Windows' app mode).</summary>
    public static bool IsDark()
    {
        if (Current == AppThemeChoice.Light)
        {
            return false;
        }

        if (Current == AppThemeChoice.Dark)
        {
            return true;
        }

        var fg = UiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Foreground);
        return fg.R > 128;
    }
}
