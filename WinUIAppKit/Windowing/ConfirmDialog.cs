using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUIAppKit.Windowing;

/// <summary>
/// The one way an app asks a yes/no confirmation (UI-6). The safe answer is the only default: Enter
/// picks Cancel, so a stray key press never confirms a destructive action. The app supplies the
/// question and the confirm label through its own localization; Cancel is the kit's shared label.
/// </summary>
public static class ConfirmDialog
{
    /// <summary>Windows with a question showing; a second question on the same window is refused.</summary>
    private static readonly ConditionalWeakTable<Window, ContentDialog> OpenDialogs = new();

    /// <summary>
    /// Ask a yes/no question on <paramref name="window"/>, whichever page it shows. Returns false (no)
    /// when the window has no loaded content yet, when it already shows a question, when the window
    /// closes while the question is up, or when the dialog closes any way other than the confirm
    /// button, so a waiting operation never proceeds on a guess.
    /// </summary>
    /// <param name="window">The window to show the question on.</param>
    /// <param name="title">The question.</param>
    /// <param name="body">What will happen, in a sentence or two.</param>
    /// <param name="confirmText">The confirm button, naming the action ("Delete", "Stop"), not "OK".</param>
    /// <param name="cancelText">The cancel button; null for the kit's shared "Cancel" label (<c>kit.dialog.cancel</c>).</param>
    /// <returns>True only when the user pressed the confirm button.</returns>
    public static async Task<bool> ConfirmAsync(Window window, string title, string body, string confirmText, string? cancelText = null)
    {
        if (window.Content is not FrameworkElement root || root.XamlRoot is null)
        {
            return false;
        }

        // One question at a time per window: ContentDialog allows only one open per XamlRoot.
        if (OpenDialogs.TryGetValue(window, out _))
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText ?? Localization.UiText.Get("kit.dialog.cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = root.ActualTheme,
        };

        // Closing the window while the question is up hides the dialog, so the caller gets "no"
        // instead of waiting forever on a dialog nobody can see.
        void OnWindowClosed(object sender, WindowEventArgs args) => dialog.Hide();

        OpenDialogs.Add(window, dialog);
        window.Closed += OnWindowClosed;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            window.Closed -= OnWindowClosed;
            OpenDialogs.Remove(window);
        }
    }
}
