using Microsoft.UI.Xaml;
using WinUIAppKit.Updates;

namespace WinUIAppKit.Pages;

/// <summary>
/// What an app's About page shows (UI-5, BRAND-8). The shared <c>AboutPage</c> from the kit's shared XAML
/// lays these out in the standard card order; the app passes this object when it navigates to the page:
/// <c>ContentFrame.Navigate(typeof(AboutPage), aboutInfo)</c>.
/// </summary>
public sealed class AboutInfo
{
    /// <summary>The product name (BRAND-1), shown as is: product names are not translated.</summary>
    public required string ProductName { get; init; }

    /// <summary>
    /// The app's own catalog key for its one-line description, matching the store tagline, for an app
    /// whose text is in <c>UiText</c> catalogs. Set this or <see cref="Description"/>, not both.
    /// </summary>
    public string? DescriptionKey { get; init; }

    /// <summary>
    /// The one-line description from the app's own localization, for an app whose text is not in
    /// <c>UiText</c> catalogs (.resw, LOC-6). Read whenever the page draws its text, so it follows a
    /// language change. Set this or <see cref="DescriptionKey"/>, not both.
    /// </summary>
    public Func<string>? Description { get; init; }

    /// <summary>Full path of the app's 256 px PNG icon (UI-1); XAML would decode an .ico at its 16 px frame.</summary>
    public required string IconPngPath { get; init; }

    /// <summary>The privacy policy (DOCS-8), required for every app.</summary>
    public required Uri PrivacyPolicy { get; init; }

    /// <summary>The app's update check; null hides the Updates card (an app with no update path yet).</summary>
    public UpdateService? Updates { get; init; }

    /// <summary>The public source repository; null when the repo is private.</summary>
    public Uri? SourceCode { get; init; }

    /// <summary>Where users report a problem: the repo's new-issue page (D-086), or the company contact for a private repo.</summary>
    public Uri? Support { get; init; }

    /// <summary>The licence text.</summary>
    public Uri? License { get; init; }

    /// <summary>Third-party notices, where the app ships them (HYG-3).</summary>
    public Uri? ThirdPartyNotices { get; init; }

    /// <summary>
    /// Product-specific cards, shown after the standard ones; they never replace them (UI-5). Each is
    /// created when the page opens, so it can use the current language.
    /// </summary>
    public IReadOnlyList<Func<UIElement>> ExtraCards { get; init; } = [];

    /// <summary>The description in the current language, from <see cref="Description"/> or <see cref="DescriptionKey"/>.</summary>
    /// <exception cref="InvalidOperationException">Both or neither are set.</exception>
    public string ResolveDescription()
    {
        if ((Description is null) == (DescriptionKey is null))
        {
            throw new InvalidOperationException("Set exactly one of AboutInfo.Description and AboutInfo.DescriptionKey.");
        }

        return Description is not null ? Description() : Localization.UiText.Get(DescriptionKey!);
    }

    /// <summary>
    /// The standard links for an app in a public GitHub repository: source code, support (new issue,
    /// which offers the repo's issue template) and the licence file on the default branch.
    /// </summary>
    /// <param name="owner">The repository's current owner (BRAND-5).</param>
    /// <param name="repo">The repository name.</param>
    /// <returns>The three links, for use in an object initializer.</returns>
    public static (Uri SourceCode, Uri Support, Uri License) GitHubLinks(string owner, string repo)
    {
        var root = new Uri($"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}");
        return (root, new Uri(root + "/issues/new/choose"), new Uri(root + "/blob/main/LICENSE"));
    }
}
