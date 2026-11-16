using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace WinUIAppKit.Updates;

/// <summary>
/// The "Check for updates" action on an app's About page (DIST-6, D-014). A packaged copy opens the
/// Microsoft Store's updates page, since the Store owns updating it; an unpackaged copy asks the app's
/// GitHub Releases for a newer version and gives back the release page. It never checks in the
/// background: call it only when the user asks.
/// </summary>
public sealed class UpdateService
{
    private const string StoreUpdatesUri = "ms-windows-store://downloadsandupdates";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Uri _releasesUri;
    private readonly string? _tagPrefix;
    private readonly HttpClient _http;

    /// <summary>Create the service for one app's repository.</summary>
    /// <param name="owner">The repository's current GitHub owner (BRAND-5), defined once in the app.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="tagPrefix">The app's lane tag prefix in a monorepo (REL-3), such as <c>desktop-</c>; null for a repo whose app is tagged <c>vX.Y.Z</c>.</param>
    /// <param name="currentVersion">The running app's version; null for <see cref="AppEnvironment.Version"/>.</param>
    public UpdateService(string owner, string repo, string? tagPrefix = null, string? currentVersion = null)
    {
        _releasesUri = new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases?per_page=50");
        _tagPrefix = tagPrefix;
        CurrentVersion = currentVersion ?? AppEnvironment.Version;

        // GitHub requires a user agent; the repo and version say which app is asking and nothing else.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{repo}/{GitHubReleases.ThreePart(ParseOrZero(CurrentVersion))}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>The running app's version, as given or from <see cref="AppEnvironment.Version"/>.</summary>
    public string CurrentVersion { get; }

    /// <summary>
    /// What the About page button does: open the Store's updates page when packaged, otherwise check
    /// GitHub Releases. Never throws.
    /// </summary>
    public Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        if (AppEnvironment.IsPackaged)
        {
            var outcome = OpenStoreUpdates() ? UpdateCheckOutcome.OpenedStore : UpdateCheckOutcome.Failed;
            return Task.FromResult(new UpdateCheckResult(outcome, CurrentVersion, null, null));
        }

        return CheckGitHubAsync();
    }

    /// <summary>Open the Microsoft Store's updates page. Returns false when it could not be opened.</summary>
    public static bool OpenStoreUpdates()
    {
        try
        {
            Process.Start(new ProcessStartInfo(StoreUpdatesUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not open the Microsoft Store updates page");
            return false;
        }
    }

    /// <summary>
    /// Ask GitHub Releases whether a newer version of this app's lane exists. A repository with no
    /// releases counts as up to date. Network, rate-limit and parse failures give
    /// <see cref="UpdateCheckOutcome.Failed"/>. Never throws.
    /// </summary>
    public async Task<UpdateCheckResult> CheckGitHubAsync()
    {
        try
        {
            using var response = await _http.GetAsync(_releasesUri).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.UpToDate, CurrentVersion, null, null);
            }

            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Evaluate(GitHubReleases.Parse(body));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Update check against {Uri} failed", _releasesUri);
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, CurrentVersion, null, null);
        }
    }

    /// <summary>Open a result's release page in the browser. Does nothing when there is none.</summary>
    /// <param name="result">A result from <see cref="CheckGitHubAsync"/>.</param>
    public static void OpenReleasePage(UpdateCheckResult result)
    {
        if (result.ReleaseUrl is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not open {Url}", result.ReleaseUrl);
        }
    }

    /// <summary>Turn the release listing into a result for the running version.</summary>
    private UpdateCheckResult Evaluate(IReadOnlyList<GitHubRelease> releases)
    {
        var latest = GitHubReleases.PickLatest(releases, _tagPrefix);
        if (latest is null)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.UpToDate, CurrentVersion, null, null);
        }

        var (release, version) = latest.Value;
        bool newer = GitHubReleases.IsNewer(ParseOrZero(CurrentVersion), version);
        return new UpdateCheckResult(
            newer ? UpdateCheckOutcome.UpdateAvailable : UpdateCheckOutcome.UpToDate,
            CurrentVersion,
            version.ToString(3),
            release.HtmlUrl);
    }

    private static Version ParseOrZero(string version) =>
        Version.TryParse(version.Trim().TrimStart('v', 'V'), out var parsed) ? parsed : new Version(0, 0, 0);
}

/// <summary>What a "Check for updates" action found.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>The app is packaged and the Store's updates page was opened.</summary>
    OpenedStore,

    /// <summary>No newer release of this app's lane exists.</summary>
    UpToDate,

    /// <summary>A newer release exists; show <see cref="UpdateCheckResult.LatestVersion"/> and link to its page.</summary>
    UpdateAvailable,

    /// <summary>The Store could not be opened, or GitHub could not be reached or read.</summary>
    Failed,
}

/// <summary>The result of a "Check for updates" action.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="CurrentVersion">The running app's version.</param>
/// <param name="LatestVersion">The newest release's three-part version, when one was found.</param>
/// <param name="ReleaseUrl">That release's page on github.com, when one was found.</param>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string CurrentVersion, string? LatestVersion, string? ReleaseUrl);
