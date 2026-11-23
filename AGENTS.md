# WinUI App Kit: Agent Instructions

Shared library for small WinUI 3 desktop apps that live in the system tray. App developers use it for the tray icon and menu, the tray flyout, window chrome, the settings file and credentials, start with Windows, theming, localization, updates, the settings window shell and About page, and the shared build configuration. Apps consume it as a git submodule at `external/winui-app-kit`, pinned to a release tag, and build it through a `ProjectReference` plus the imports in `build/` and `Xaml/`; it builds on no other kit. [README.md](README.md) is the canonical usage doc for app developers, with every public type and member.

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards. Read the index for this stack, https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/kits.md, then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

The kit owns the plumbing below and nothing else: no app's pages, settings fields, strings or feature behaviour. The README's "What's in it" table lists the types in each area.

| Area | Files |
| --- | --- |
| Tray icon, menu and icon rasteriser | `WinUIAppKit/Tray/` |
| Tray flyout, window chrome and placement, confirmations | `WinUIAppKit/Windowing/` |
| App start, hidden host window and exit | `WinUIAppKit/Hosting/` |
| Settings file and credentials | `WinUIAppKit/Settings/` |
| Start with Windows | `WinUIAppKit/Startup/` |
| Theme switching | `WinUIAppKit/Theming/` |
| Localization mechanism and the kit's own shared labels | `WinUIAppKit/Localization/`, catalogs in `WinUIAppKit/Localization/Catalogs/` |
| Update check | `WinUIAppKit/Updates/` |
| Logging | `WinUIAppKit/Logging/` |
| About page data | `WinUIAppKit/Pages/` |
| App environment, single instance, Win32 declarations | `WinUIAppKit/AppEnvironment.cs`, `WinUIAppKit/SingleInstance.cs`, `WinUIAppKit/Interop/` |
| Shared XAML (styles, settings shell, About page, Settings page rows), compiled by each app | `Xaml/`, with `Xaml/CompileCheck/` building it for the kit |
| Shared build configuration for apps and the kit | `build/`, imported by the root `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` |
| Checks, generators and the formatter apps run in `validate` | `scripts/format`, `scripts/check-localization.py`, `scripts/check-listing.py`, `scripts/check-gitignore`, `scripts/generate-notices.py` |
| devkit's icon, Store hero and pre-commit commands for apps, installed inside the kit | `scripts/devkit`, pinned in `tools/devkit/package-lock.json`, tested by `tests/devkit-tools/` |
| Packaging and Windows build entry point for apps | `build/build-msix.ps1` (driven by each app's `windows-build.json`) with `build/package-output.ps1` (deliverable names, pruning) and `build/runtime-dependency.ps1` (the manifest's Windows App Runtime MinVersion gate), both tested by `tests/build/`, `build/languages.json`, `scripts/windows-build` |
| Microsoft Store screenshot crops and checks for apps | `scripts/store/render-screenshots.py`, crop geometry and hero entries tested by `tests/store-screenshots/`; the listing check's hero fields by `tests/listing/` |
| UI tour of an installed app, run in a VM through windows-build-tools' app-test recipe | `scripts/app-test/tour.ps1` |
| Microsoft Store publishing for apps | `.github/workflows/store-publish.yml` and `credential-expiry.yml` (reusable), `scripts/store/`, tests and caller examples in `tests/store/`; `KIT_VERSION` in `store-publish.yml` moves with `<Version>` |

## Conventions and gotchas

- **Domain-neutral only.** No app names, app settings, or feature policy. Add general hooks (events, options) and keep policy in the caller; see "Rules for changes" in the README.
- **XAML lives only in `Xaml/`,** delivered through `Xaml/WinUIAppKit.Xaml.projitems` and compiled by each app (KIT-5). Never add XAML to the `WinUIAppKit/` library project: library XAML needs `.xbf`/PRI handling in every app's MSIX script. Its code-behind may use library types; library code never references XAML types. Style keys in `Xaml/Styles.xaml` are public API.
- **Namespaces:** never create a `WinUIAppKit.Windows` namespace; it shadows WinRT `Windows.*` (the windowing helpers live in `WinUIAppKit.Windowing`). Write `global::Windows...` for WinRT types.
- **Windows App SDK stays on the 2.x major line** while every consuming app's manifest depends on `Microsoft.WindowsAppRuntime.2`. Minor versions update that framework in place; moving to 3.0 means updating each app's manifest dependency. When the version moves, raise the runtime MinVersion in the CompileCheck manifest and the release notes to the one in the package's `WindowsAppSDK-VersionInfo.json`. A move within 2.x is a minor version, because `build/runtime-dependency.ps1` makes every app's packaging fail with the MinVersion to set until its manifest is edited; a new runtime line (3.0) is a major. Shared package versions live only in `build/WinUIAppKit.Packages.props`.
- **`build/` and `Xaml/` are used by every app.** A change there changes every app at its next pin bump: keep them domain-neutral, and treat anything that can fail a clean app build (a new analyzer setting, a raised floor, a removed style key) as a major version. The one exception is a raised Windows App Runtime MinVersion within the SDK's line, which the packaging gate reports with the value to set: that is a minor, called out first in the release notes.
- **Window procedures:** never show UI inside a subclass procedure; defer with `DispatcherQueue.TryEnqueue`. Each kit subclass uses its own fixed subclass id (`0x7AA1` tray, `0x7AA2` flyout, `0x7AA3` host) so it never collides with an app's own subclass.
- Keep delegates passed to native code (`SUBCLASSPROC`) in fields so they are not garbage-collected.
- **Self-contained files:** `ProtectedStore.cs`, `AtomicFile.cs`, `GitHubReleases.cs`, `RunEntry.cs`, `Localization/TextCatalogs.cs` and `Localization/SupportedLanguages.cs` use no NLog, WinUI or other kit types, because apps link them into second processes and the harnesses compile them on Linux.
- **Windows 10 floor (build 19045):** a call that needs Windows 11 goes in a helper marked `[SupportedOSPlatform("windows10.0.22000")]` and is called only behind `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)`, so the analyzer rejects an unguarded call. Win32 and Windows SDK calls added after build 19041 carry no platform attribute, so check each new one by hand. List every degraded feature in the README's "Windows 10" table.
- **Localization:** the kit holds the mechanism and only the shared labels (`kit.*` keys, D-072, LOC-7), in all seven catalogs under `WinUIAppKit/Localization/Catalogs/`; add every new `kit.` key to all seven. App wording never goes in the kit, and apps never define `kit.` keys. `UiText.SetOverride` and `Changed` run on the UI thread.
- **Behaviour apps rely on is documented in the README's "Gotchas" section.** Apps link to it instead of keeping their own copy, so a change to that behaviour updates the README in the same commit.

## Commands

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run validate` | The gate CI runs on Linux: `format:check` (whitespace, `.editorconfig` style and analyzers, every project including the test harnesses, through the shared `scripts/format`), `check:gitignore`, `check:l10n` (the kit's catalogs against its code, through the same script apps run) and `test` | That anything compiles for Windows |
| `npm run format` | Rewrites files to the `.editorconfig` style | Anything; review the diff |
| `npm run verify` | The library and the shared XAML compile for x64 and ARM64 in Release (warnings are errors), by building `Xaml/CompileCheck` through the shared Windows build tooling | Behaviour; needs the Windows build host |
| `npm test` | Every portable harness under `tests/` passes (each has a README saying what it proves); the devkit-tools test needs Node and network on its first run | Windows-only behaviour: DPAPI, packaged paths, windows |

`scripts/dotnet` uses the host .NET SDK when there is one, otherwise the SDK container matching `global.json` (Docker needed). The same Windows compile runs in CI (`windows-compile`). Release and CI builds treat warnings as errors, and so does every app's Release build of the kit, so keep the kit warning-free.

To check a change against an app, copy the app into `.artifacts/` with this working tree in place of its `external/winui-app-kit`, then run the app's `verify:desktop` there. Never edit an app's submodule checkout. Tray, flyout, menu and XAML rendering can only be checked in an installed app.

## Release

The kit is released as an annotated `vX.Y.Z` tag that apps pin; the version lives in `<Version>` in `Directory.Build.props`, and each version has notes in `release-notes/<version>.md` for the app developers who consume it. Use the `release` skill (`.agents/skills/release/SKILL.md`); the `Release` workflow creates the GitHub Release from the tag. SemVer meaning and how apps take a new version are in the README's "Versioning" section.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [README.md](README.md) | Changing any public type, style key, shared label or behaviour an app relies on | `WinUIAppKit/**`, `Xaml/**`, `build/**` |
| [Kits stack index](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/kits.md) | Changing repo layout, scripts, CI, releases or docs | the whole repo |
| `release` skill | Releasing or tagging the kit | `Directory.Build.props` `<Version>`, `release-notes/`, `.github/workflows/release.yml` |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A public type, member, style key or shared label added, renamed or removed | README (its "What's in it" table and the section for that area) |
| Behaviour an app relies on (identity values, flyout lifetime, language resolution, settings serialization, startup) | README "Gotchas" |
| How an app adds the kit, its imports, or the target framework | README "Using it" and "Shared build configuration" |
| A harness's coverage | That harness's README |
| A feature that behaves differently on Windows 10 | README "Windows 10" |
| A script, a CI job, or an area or folder | The Commands or Architecture table above |
