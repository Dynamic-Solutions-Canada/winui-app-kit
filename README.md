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

## Gotchas

Apps link to this section rather than repeating it in their own docs.

- **Identity values never change once shipped.** The app passes its own tray icon GUID, single-instance mutex name, startup task id and Run value name. Windows keys the user's show/hide choice for the tray icon on the GUID, so a new GUID loses that choice.
- **Claim `SingleInstance` before loading settings,** so a second launch exits without touching the settings file.
- **An app namespace ending in `.Windows` shadows WinRT `Windows.*`.** Write `global::Windows...` for WinRT types in that app.
- **Never hide a flyout window yourself.** `FlyoutController` closes it by cloaking it and parking it off-screen, so WinUI keeps its composition surfaces and the next open is instant. It reveals the window only after it has rendered, and ignores a toggle within 300 ms of a dismissal (the tray click that closed it).
- **Paths handed to another process** (Explorer, a file picker) use `AppEnvironment.PhysicalDataDirectory`. When packaged, `%AppData%` writes are redirected into the package, and other processes do not see them at the path the app uses.
- **Settings serialization:** `JsonSettingsStore<T>.DefaultOptions` omits nulls but writes defaults, because an omitted `false` for a bool that defaults to `true` would load back as `true`. Options an app passes in must keep that.
- **Start with Windows never overrides the user or a policy.** When `StartupStatus` says the user (in Windows Settings) or a policy decided, the app explains that instead of flipping its switch back. Packaged builds use the manifest startup task; unpackaged builds use the HKCU Run key with a silent-launch argument.
- **Interface text:** use `UiText.Get` and `UiText.Format` (placeholders as `{{name}}`) in code, and the `Localize` attached properties (`xmlns:l10n="using:WinUIAppKit.Localization"`, then `l10n:Localize.Text`, `Content`, `Header`, `ToolTip` and the others) in XAML. Store a key or a `LocalizedMessage` on models and services, not finished text, so a language change relabels it. `Localize` relabels XAML live; anything an app builds in code must also refresh on `UiText.Changed`.
- **Do not set `ApplicationLanguages.PrimaryLanguageOverride`.** When packaged, `UiText` resolves "Match Windows" from the app's language list, which an override replaces. Pass the user's choice to `UiText.Configure` and `UiText.SetOverride` instead.

## Rules for changes

- Keep it domain-neutral: no app names, settings fields or feature behaviour. If an app needs different behaviour, add a general option or event and keep the policy in the app.
- Every consuming app builds against the kit's current commit, so a breaking change needs those apps updated in the same pass.
- No XAML (yet): pages and windows stay in each app. Shipping XAML from a library needs extra MSIX packaging work in every app.

## License

MIT; see [LICENSE](LICENSE). Copyright (c) 2026 Dynamic Solutions.
