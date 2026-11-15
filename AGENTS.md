# WinUI App Kit: Agent Instructions

Shared library for the WinUI 3 tray apps (WSL Monitor first; Hub Display Switch, Little Launcher, Repilot, Immich Drive and Persistent are planned to migrate one at a time). It is consumed as a git submodule at `external/winui-app-kit` and built by each app through a `ProjectReference`; it has no build or packaging of its own.

## Rules

- **Domain-neutral only.** No app names, app settings, or feature policy. Add general hooks (events, options) and keep policy in the caller.
- **No XAML in the kit.** Library XAML needs `.xbf`/PRI handling in every app's MSIX script. Pages, windows and resource dictionaries stay in the apps.
- **Namespaces:** never create a `WinUIAppKit.Windows` namespace; it shadows WinRT `Windows.*` (the windowing helpers live in `WinUIAppKit.Windowing`). Write `global::Windows...` for WinRT types.
- **Windows App SDK stays on the 1.8 line** while every consuming app ships with `Microsoft.WindowsAppRuntime.1.8`. Raising it means updating each app's manifest dependency.
- Window procedures: never show UI inside a subclass procedure; defer with `DispatcherQueue.TryEnqueue`. Each kit subclass uses its own fixed subclass id (`0x7AA1` tray, `0x7AA2` flyout).
- Keep delegates passed to native code (`SUBCLASSPROC`) in fields so they are not garbage-collected.

## Verifying

Build a consuming app from WSL (for example `npm run verify:desktop -- --platform x64` in `/development/wsl-monitor`). Tray, flyout and menu behaviour can only be checked in the installed app.
