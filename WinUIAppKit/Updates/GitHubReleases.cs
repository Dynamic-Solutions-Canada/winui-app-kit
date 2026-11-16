using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinUIAppKit.Updates;

/// <summary>
/// Picks the newest release of an app's lane from a GitHub Releases listing and compares versions
/// (DIST-6). Kept free of WinUI, WinRT and logging so a portable test harness can compile it in.
/// </summary>
public static partial class GitHubReleases
{
    /// <summary>
    /// Read a <c>GET /repos/{owner}/{repo}/releases</c> response. Entries without a tag are dropped.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <exception cref="JsonException">The body is not a JSON array of releases.</exception>
    public static IReadOnlyList<GitHubRelease> Parse(string json) =>
        (JsonSerializer.Deserialize(json, GitHubReleasesJsonContext.Default.ListGitHubRelease) ?? [])
            .Where(release => !string.IsNullOrWhiteSpace(release.TagName))
            .ToList();

    /// <summary>
    /// The newest published, non-prerelease release whose tag is <c>&lt;tagPrefix&gt;vX.Y.Z</c>, by version
    /// rather than by date, or null when there is none. Other lanes' tags never match: with no prefix
    /// only plain <c>vX.Y.Z</c> tags count, and with <c>desktop-</c> only <c>desktop-vX.Y.Z</c>.
    /// </summary>
    /// <param name="releases">The listing, in any order.</param>
    /// <param name="tagPrefix">The lane's tag prefix (REL-3), such as <c>desktop-</c>, or null for the primary lane.</param>
    public static (GitHubRelease Release, Version Version)? PickLatest(IEnumerable<GitHubRelease> releases, string? tagPrefix)
    {
        (GitHubRelease Release, Version Version)? best = null;
        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease)
            {
                continue;
            }

            var version = VersionFromTag(release.TagName, tagPrefix);
            if (version is not null && (best is null || version > best.Value.Version))
            {
                best = (release, version);
            }
        }

        return best;
    }

    /// <summary>
    /// The three-part version in a tag of the form <c>&lt;tagPrefix&gt;vX.Y.Z</c>, or null when the tag
    /// belongs to another lane or is not a release version.
    /// </summary>
    /// <param name="tag">The tag name.</param>
    /// <param name="tagPrefix">The lane's tag prefix, or null for the primary lane.</param>
    public static Version? VersionFromTag(string? tag, string? tagPrefix)
    {
        if (tag is null)
        {
            return null;
        }

        string prefix = tagPrefix ?? "";
        if (!tag.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var match = TagVersion().Match(tag[prefix.Length..]);
        return match.Success ? Version.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// True when <paramref name="latest"/> is newer than <paramref name="current"/>, comparing only
    /// major, minor and patch: the fourth part is the MSIX revision dev builds bump, not a release.
    /// </summary>
    /// <param name="current">The running app's version, three or four parts.</param>
    /// <param name="latest">The newest release's version.</param>
    public static bool IsNewer(Version current, Version latest) => ThreePart(latest) > ThreePart(current);

    /// <summary>The version as major.minor.patch, with a missing patch read as 0.</summary>
    /// <param name="version">Any parsed version.</param>
    public static Version ThreePart(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TagVersion();
}

/// <summary>The fields of a GitHub release the update check uses.</summary>
public sealed class GitHubRelease
{
    /// <summary>The release's tag, such as <c>v1.2.3</c>.</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>The release page on github.com.</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary>True for an unpublished draft.</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    /// <summary>True for a prerelease.</summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}

/// <summary>Source-generated JSON reading, so trimmed builds keep working.</summary>
[JsonSerializable(typeof(List<GitHubRelease>))]
internal sealed partial class GitHubReleasesJsonContext : JsonSerializerContext;
