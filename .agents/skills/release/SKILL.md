---
name: release
description: Releases a new version of the kit. Gets the notes approved, bumps the version, tags vX.Y.Z, lets CI create the GitHub Release, then hands the pin bump to each consuming app. Use when asked to release, tag or publish the kit. Do not use for bumping the kit pin inside an app; that is the app's own change.
---

Publish the current `main` as a tagged kit version that apps can pin.

Invoking this skill authorises its own bump commit, tag and push. It still stops twice: for approval of the notes, and on a dirty tree.

1. **Check the tree.** `git status` is clean and `git fetch` shows `HEAD` equal to `origin/main`. Stop and report if not; never commit unrelated work with the release.
2. **Run the gate.** `npm run validate` passes. CI on `main` is green for `HEAD` (`gh run list --branch main --limit 5`), including the Windows compile.
3. **Choose the version.** Read the changes since the last tag (`git log $(git describe --tags --abbrev=0)..HEAD`). Apply SemVer as the README's "Versioning" section defines it: a major for anything that breaks a consuming app, a minor for new API or behaviour, a patch for fixes only.
4. **Draft the notes and stop for approval.** Write the proposed `release-notes/<version>.md` content in the reply: bullets only, no heading and no disclosure line, written for the app developers who consume the kit (what they can now do, what changed in behaviour, and every edit a consumer must make). Wait for Ryan to approve or edit it. Change no files before that.
5. **Bump.** Set `<Version>` in `Directory.Build.props` and `KIT_VERSION` in `.github/workflows/store-publish.yml` to the same version, write the approved notes file, and commit them together with the subject `Bump version to v<version>`. The `Release` workflow fails if the two versions differ.
6. **Tag and push.** `git tag -a v<version> -m "WinUI App Kit <version>"`, then `git push origin main v<version>`.
7. **Watch the Release.** Follow the `Release` workflow for the tag (`gh run watch`). It reruns the gate, checks the tag, version and notes agree, and creates the GitHub Release titled `WinUI App Kit <version>`. If it fails, report the failing job; do not create or edit the Release by hand.
8. **Open the pin bump in each consumer.** Each consuming app takes the bump as its own change, with the subject `Update WinUI App Kit: <what it changes for this app>`. Do not edit an app repo from this session: hand each app's session the tag, the Release link and the exact edits a breaking change requires, or list them for Ryan if no session is open.

Report: the version and why, the tag, the Release URL and its workflow result, and per consumer what its bump needs.
