# WinUIAppKit.Updates.Tests

Portable checks for the update check's release logic. The project compiles in `WinUIAppKit/Updates/GitHubReleases.cs` and runs on Linux. `npm test` runs it with every other harness under `tests/`.

## What it covers

- **Tags:** which tags count as a release for the primary lane (`vX.Y.Z`) and for a lane prefix (`desktop-vX.Y.Z`), and that other lanes, suffixed and malformed tags never match.
- **Picking:** the newest release by version (numeric, so 1.10.0 beats 1.9.0), whatever the listing order, with drafts and prereleases skipped.
- **Comparing:** the MSIX revision a dev build adds is ignored.
- **Parsing:** GitHub's response, including an error object in place of a listing.

## What it does not prove

- **The network call:** GitHub's live API, rate limits, timeouts.
- **Packaged builds:** opening the Store's updates page. Both can only be checked in an installed app.
