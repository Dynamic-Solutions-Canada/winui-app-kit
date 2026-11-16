using WinUIAppKit.Updates;

// Portable checks for the update check's release filtering and version comparison.
// Exits non-zero when any check fails.

var failures = new List<string>();
int passed = 0;

void Check(bool condition, string description)
{
    if (condition)
    {
        passed++;
    }
    else
    {
        failures.Add(description);
    }
}

GitHubRelease Release(string tag, bool draft = false, bool prerelease = false) =>
    new() { TagName = tag, HtmlUrl = $"https://github.com/o/r/releases/tag/{tag}", Draft = draft, Prerelease = prerelease };

// ── Tags ────────────────────────────────────────────────────────────────

Check(GitHubReleases.VersionFromTag("v1.2.3", null) == new Version(1, 2, 3), "tag: v1.2.3 is 1.2.3");
Check(GitHubReleases.VersionFromTag("v10.0.14", null) == new Version(10, 0, 14), "tag: multi-digit parts");
Check(GitHubReleases.VersionFromTag("1.2.3", null) is null, "tag: a tag without v is not a release (REL-3)");
Check(GitHubReleases.VersionFromTag("v1.2", null) is null, "tag: two parts are not a release version");
Check(GitHubReleases.VersionFromTag("v1.2.3.4", null) is null, "tag: four parts are not a release version");
Check(GitHubReleases.VersionFromTag("v1.2.3-beta", null) is null, "tag: a suffixed tag is not a release");
Check(GitHubReleases.VersionFromTag("desktop-v1.2.3", null) is null, "tag: another lane's tag does not match the primary lane");
Check(GitHubReleases.VersionFromTag("desktop-v1.2.3", "desktop-") == new Version(1, 2, 3), "tag: the lane's own tag matches its prefix");
Check(GitHubReleases.VersionFromTag("v1.2.3", "desktop-") is null, "tag: the primary lane's tag does not match a lane prefix");
Check(GitHubReleases.VersionFromTag("linux-v1.2.3", "desktop-") is null, "tag: one lane does not match another");
Check(GitHubReleases.VersionFromTag(null, null) is null, "tag: a missing tag is not a version");

// ── Picking the latest ──────────────────────────────────────────────────

var listing = new[]
{
    Release("v1.9.0"),
    Release("v1.10.0"),                     // newer than 1.9.0 by version, not by string
    Release("v2.0.0", prerelease: true),
    Release("v3.0.0", draft: true),
    Release("desktop-v5.0.0"),
    Release("server-abc123"),
    Release("v1.2.3"),
};

var latest = GitHubReleases.PickLatest(listing, null);
Check(latest?.Version == new Version(1, 10, 0), "pick: highest version wins, compared numerically (1.10.0 over 1.9.0)");
Check(latest?.Release.HtmlUrl == "https://github.com/o/r/releases/tag/v1.10.0", "pick: the release page of the chosen release");
Check(GitHubReleases.PickLatest(listing, "desktop-")?.Version == new Version(5, 0, 0), "pick: a lane prefix picks only that lane");
Check(GitHubReleases.PickLatest(listing.Reverse(), null)?.Version == new Version(1, 10, 0), "pick: listing order does not matter");
Check(GitHubReleases.PickLatest([Release("v2.0.0", prerelease: true), Release("v3.0.0", draft: true)], null) is null, "pick: drafts and prereleases never count");
Check(GitHubReleases.PickLatest([], null) is null, "pick: no releases gives none");

// ── Comparing ───────────────────────────────────────────────────────────

Check(GitHubReleases.IsNewer(new Version(1, 2, 3), new Version(1, 2, 4)), "compare: a higher patch is newer");
Check(!GitHubReleases.IsNewer(new Version(1, 2, 3), new Version(1, 2, 3)), "compare: the same version is not newer");
Check(!GitHubReleases.IsNewer(new Version(1, 3, 0), new Version(1, 2, 9)), "compare: an older release is not newer");
Check(!GitHubReleases.IsNewer(new Version(1, 2, 3, 7), new Version(1, 2, 3)), "compare: a dev build's MSIX revision is ignored");
Check(GitHubReleases.IsNewer(new Version(1, 2), new Version(1, 2, 1)), "compare: a two-part current version reads as patch 0");

// ── Parsing GitHub's response ───────────────────────────────────────────

const string Json = """
[
  { "tag_name": "v1.1.0", "html_url": "https://github.com/o/r/releases/tag/v1.1.0", "draft": false, "prerelease": false, "name": "ignored" },
  { "tag_name": "v1.2.0-rc1", "html_url": "https://x", "draft": false, "prerelease": true },
  { "html_url": "https://no-tag" }
]
""";
var parsed = GitHubReleases.Parse(Json);
Check(parsed.Count == 2, "parse: entries without a tag are dropped, unknown fields ignored");
Check(GitHubReleases.PickLatest(parsed, null)?.Version == new Version(1, 1, 0), "parse: the parsed listing picks v1.1.0");
Check(GitHubReleases.Parse("[]").Count == 0, "parse: an empty listing");

bool rejected = false;
try
{
    GitHubReleases.Parse("""{ "message": "API rate limit exceeded" }""");
}
catch (System.Text.Json.JsonException)
{
    rejected = true;
}

Check(rejected, "parse: an error object instead of a listing throws, so the caller reports a failed check");

// ── Report ──────────────────────────────────────────────────────────────

foreach (string failure in failures)
{
    Console.Error.WriteLine($"FAIL {failure}");
}

Console.WriteLine($"WinUIAppKit.Updates.Tests: {passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;
