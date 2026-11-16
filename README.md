# WinUI App Kit

Shared plumbing for small WinUI 3 desktop apps that live in the system tray: the tray icon, its menu and flyout window, the settings file, "start with Windows", theme switching, and main-window chrome. Each app keeps its own content, pages and settings; the kit only provides the parts that were being copied between apps.

## What's in it

| Area | Type | What it does |
|---|---|---|
| Tray | `TrayIcon` | Notification-area icon with a stable GUID, re-added after Explorer restarts, crisp at any scaling, click / double-click / context-menu events |
| Tray | `TrayMenu` | Native right-click menu that follows dark mode |
| Windows | `FlyoutController` | Turns a `Window` into a tray flyout: acrylic, rounded corners, placed by the taskbar, dismissed on focus loss or Esc, parked off-screen for instant reopening |
| Windows | `WindowChrome`, `WindowIcon`, `WindowPlacement` | Sharp window icons, saved size and position (ignored if the monitor is gone), bring-to-front |
| Settings | `JsonSettingsStore<T>` | Atomic JSON load/save of the app's own settings class |
| Startup | `StartupRegistration`, `StartupStatus` | Start with Windows for packaged (startup task) and unpackaged (Run key) builds; reports when the user (in Windows Settings) or a policy decides instead of the app |
| Theming | `AppTheme` | System / light / dark choice applied to every open window |
| Localization | `SupportedLanguages`, `UiText`, `Localize`, `LocalizedMessage` | Interface text from per-language JSON catalogs embedded in the app, chosen from the user's override or Windows' languages; XAML attached properties that relabel open windows live; messages stored as key plus values so they follow a language change |
| App | `AppEnvironment`, `SingleInstance` | Packaged or not, version, data folders, one running copy |

Win32 declarations shared by these live in `Interop/NativeMethods.cs`.

## Using it

Add it as a submodule and reference the project:

```bash
git submodule add https://github.com/Dynamic-Solutions-Canada/winui-app-kit.git external/winui-app-kit
```

```xml
<ProjectReference Include="..\external\winui-app-kit\WinUIAppKit\WinUIAppKit.csproj" />
```

The kit targets `net10.0-windows10.0.22000.0` and Windows App SDK 1.8. An app may use a newer SDK; NuGet then resolves the higher version for both.

## Rules for changes

- Keep it domain-neutral: no app names, settings fields or feature behaviour. If an app needs different behaviour, add a general option or event and keep the policy in the app.
- Every consuming app builds against the kit's current commit, so a breaking change needs those apps updated in the same pass.
- No XAML (yet): pages and windows stay in each app. Shipping XAML from a library needs extra MSIX packaging work in every app.

## Licence

PolyForm Noncommercial 1.0.0; see `LICENSE.md`.
