# WinUI App Kit: Agent Instructions

Shared library for small WinUI 3 desktop apps that live in the system tray. App developers use it for the tray icon and menu, the tray flyout, window chrome, the settings file, start with Windows, theming, localization and app environment facts. Apps consume it as a git submodule at `external/winui-app-kit` and build it through a `ProjectReference`; the kit has no build or packaging of its own and builds on no other kit. [README.md](README.md) is the canonical usage doc for app developers.

## Standards

This repo follows the shared standards at https://github.com/Dynamic-Solutions-Canada/app-standards. Read the index for this stack, https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/kits.md, then the topic file for the area you are changing. If `external/standards/` exists, it is the same content pinned for offline use. Where this file differs from the standards, this file wins and says why.

## Architecture

The kit owns the plumbing below and nothing else: no app's pages, settings fields, strings or feature behaviour.

| Area | Types | Files |
| --- | --- | --- |
| Tray icon and menu | `TrayIcon`, `TrayAnchor`, `TrayMenu` | `WinUIAppKit/Tray/` |
| Tray flyout | `FlyoutController` | `WinUIAppKit/Windowing/FlyoutController.cs` |
| Window chrome and placement | `WindowChrome`, `WindowIcon`, `WindowPlacement` | `WinUIAppKit/Windowing/WindowChrome.cs` |
| Settings file | `JsonSettingsStore<T>` | `WinUIAppKit/Settings/` |
| Start with Windows | `StartupRegistration`, `StartupStatus` | `WinUIAppKit/Startup/` |
| Theme switching | `AppTheme` | `WinUIAppKit/Theming/` |
| Localization mechanism | `SupportedLanguages`, `UiText`, `Localize`, `LocalizedMessage` | `WinUIAppKit/Localization/` |
| App environment, single instance | `AppEnvironment`, `SingleInstance` | `WinUIAppKit/AppEnvironment.cs`, `WinUIAppKit/SingleInstance.cs` |
| Win32 declarations | `NativeMethods` | `WinUIAppKit/Interop/NativeMethods.cs` |
| Logging | `AppLogging` | `WinUIAppKit/Logging/` |
| Shared build configuration for apps (and the kit itself) | props, security floors, package versions, `NLog.config`, `.gitignore` block | `build/`, imported by the root `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` |

## Conventions and gotchas

- **Domain-neutral only.** No app names, app settings, or feature policy. Add general hooks (events, options) and keep policy in the caller; see "Rules for changes" in the README.
- **No XAML in the kit.** Library XAML needs `.xbf`/PRI handling in every app's MSIX script. Pages, windows and resource dictionaries stay in the apps.
- **Namespaces:** never create a `WinUIAppKit.Windows` namespace; it shadows WinRT `Windows.*` (the windowing helpers live in `WinUIAppKit.Windowing`). Write `global::Windows...` for WinRT types.
- **Windows App SDK stays on the 1.8 line** while every consuming app ships with `Microsoft.WindowsAppRuntime.1.8`. Raising it means updating each app's manifest dependency. Shared package versions live only in `build/WinUIAppKit.Packages.props`.
- **`build/` is imported by every app.** A change there changes every app's build at its next pin bump: keep it domain-neutral, and treat anything that can fail a clean app build (a new analyzer setting, a raised floor) as a major version.
- **Window procedures:** never show UI inside a subclass procedure; defer with `DispatcherQueue.TryEnqueue`. Each kit subclass uses its own fixed subclass id (`0x7AA1` tray, `0x7AA2` flyout) so it never collides with an app's own subclass.
- Keep delegates passed to native code (`SUBCLASSPROC`) in fields so they are not garbage-collected.
- **Windows 10 floor (build 19045):** a call that needs Windows 11 goes in a helper marked `[SupportedOSPlatform("windows10.0.22000")]` and is called only behind `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)`, so the analyzer rejects an unguarded call. Win32 and Windows SDK calls added after build 19041 carry no platform attribute, so check each new one by hand. List every degraded feature in the README's "Windows 10" table.
- **Localization:** the kit holds the mechanism only. Each app owns its language list, catalogs and keys; never put app wording in the kit. `UiText.SetOverride` and `Changed` run on the UI thread.
- **Behaviour apps rely on is documented in the README's "Gotchas" section.** Apps link to it instead of keeping their own copy, so a change to that behaviour updates the README in the same commit.

## Commands

| Command | Proves | Does not prove |
| --- | --- | --- |
| `npm run validate` | The gate CI runs on Linux: `format:check` (whitespace, `.editorconfig` style and analyzers through `dotnet format`) and `test` | That the library compiles for Windows |
| `npm run format` | Rewrites files to the `.editorconfig` style | Anything; review the diff |
| `npm run verify` | The library compiles for x64 and ARM64 in Release (warnings are errors), through the shared Windows build tooling | Behaviour; needs the Windows build host |
| `npm test` | Every test project under `tests/` passes (none exist yet) | Windows-only behaviour |

`scripts/dotnet` uses the host .NET SDK when there is one, otherwise the SDK container matching `global.json` (Docker needed). The same Windows compile runs in CI (`windows-compile` in `.github/workflows/ci.yml`); `verify` reaches the tooling through `scripts/windows-build` and `windows-build.json`. Release and CI builds treat warnings as errors, and so does every app's Release build of the kit, so keep the kit warning-free.

To check a change against an app, copy the app into `.artifacts/` with this working tree in place of its `external/winui-app-kit`, then run the app's `verify:desktop` there. Never edit an app's submodule checkout. Tray, flyout and menu behaviour can only be checked in an installed app.

## Release

The kit is released as an annotated `vX.Y.Z` tag that apps pin; the version lives in `<Version>` in `Directory.Build.props`, and each version has notes in `release-notes/<version>.md` for the app developers who consume it. Use the `release` skill (`.agents/skills/release/SKILL.md`); the `Release` workflow creates the GitHub Release from the tag. SemVer meaning and how apps take a new version are in the README's "Versioning" section.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [README.md](README.md) | Changing any public type, or behaviour an app relies on | `WinUIAppKit/**` |
| [Kits stack index](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/kits.md) | Changing repo layout, scripts, CI, releases or docs | the whole repo |
| `release` skill | Releasing or tagging the kit | `Directory.Build.props` `<Version>`, `release-notes/`, `.github/workflows/release.yml` |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A public type added, renamed or removed | README "What's in it" table and the Architecture table above |
| Behaviour an app relies on (identity values, flyout lifetime, language resolution, settings serialization, startup) | README "Gotchas" |
| How an app adds or references the kit, or the target framework | README "Using it" |
| A file in `build/`, or what an app must do to import it | README "Shared build configuration" |
| A feature that behaves differently on Windows 10 | README "Windows 10" |
| The Windows App SDK line | The rule above and README "Using it" |
| A rule for kit changes | README "Rules for changes", or "Conventions and gotchas" above if only an agent needs it |
| A script in `package.json` or `scripts/`, or a CI job | The Commands table above |
| `.editorconfig` | Nothing here, but every app copies it from the next tag: say so in the release notes |
| The release procedure, the version source or the `Release` workflow | The `release` skill, the Release section above and README "Versioning" |
| What counts as breaking | README "Versioning" |
