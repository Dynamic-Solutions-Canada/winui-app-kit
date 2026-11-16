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

## Conventions and gotchas

- **Domain-neutral only.** No app names, app settings, or feature policy. Add general hooks (events, options) and keep policy in the caller; see "Rules for changes" in the README.
- **No XAML in the kit.** Library XAML needs `.xbf`/PRI handling in every app's MSIX script. Pages, windows and resource dictionaries stay in the apps.
- **Namespaces:** never create a `WinUIAppKit.Windows` namespace; it shadows WinRT `Windows.*` (the windowing helpers live in `WinUIAppKit.Windowing`). Write `global::Windows...` for WinRT types.
- **Windows App SDK stays on the 1.8 line** while every consuming app ships with `Microsoft.WindowsAppRuntime.1.8`. Raising it means updating each app's manifest dependency.
- **Window procedures:** never show UI inside a subclass procedure; defer with `DispatcherQueue.TryEnqueue`. Each kit subclass uses its own fixed subclass id (`0x7AA1` tray, `0x7AA2` flyout) so it never collides with an app's own subclass.
- Keep delegates passed to native code (`SUBCLASSPROC`) in fields so they are not garbage-collected.
- **Localization:** the kit holds the mechanism only. Each app owns its language list, catalogs and keys; never put app wording in the kit. `UiText.SetOverride` and `Changed` run on the UI thread.
- **Behaviour apps rely on is documented in the README's "Gotchas" section.** Apps link to it instead of keeping their own copy, so a change to that behaviour updates the README in the same commit.

## Commands

There is no build or test of the kit's own yet. To compile a change, build a consuming app against this working tree with that app's `verify:desktop` script, using a copy of the app rather than editing its submodule checkout. That proves the kit compiles and the app still builds against it; it does not prove behaviour. Tray, flyout and menu behaviour can only be checked in an installed app.

## Guidance map

| Guide, doc, or skill | Read or use when | Governs |
| --- | --- | --- |
| [README.md](README.md) | Changing any public type, or behaviour an app relies on | `WinUIAppKit/**` |
| [Kits stack index](https://github.com/Dynamic-Solutions-Canada/app-standards/blob/main/stacks/kits.md) | Changing repo layout, scripts, CI, releases or docs | the whole repo |

## Keeping docs current

| What changed | Update |
| --- | --- |
| A public type added, renamed or removed | README "What's in it" table and the Architecture table above |
| Behaviour an app relies on (identity values, flyout lifetime, language resolution, settings serialization, startup) | README "Gotchas" |
| How an app adds or references the kit, or the target framework | README "Using it" |
| The Windows App SDK line | The rule above and README "Using it" |
| A rule for kit changes | README "Rules for changes", or "Conventions and gotchas" above if only an agent needs it |
