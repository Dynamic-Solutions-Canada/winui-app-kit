# WinUI App Kit

Shared plumbing for small WinUI 3 desktop apps that live in the system tray: the tray icon, its menu and flyout window, the settings file, "start with Windows", theme switching, and main-window chrome. Each app keeps its own content, pages and settings; the kit only provides the parts that were being copied between apps.

## What's in it

| Area | Type | What it does |
|---|---|---|
| Tray | `TrayIcon` | Notification-area icon with a stable GUID, re-added after Explorer restarts, crisp at any scaling, click / double-click / context-menu events |
| Tray | `TrayMenu` | Native right-click menu that follows dark mode |
| Tray | `TrayIconRasterizer` | Turns tray artwork drawn in code into a crisp PNG at the tray's exact pixel size, with the taskbar ink colours and a rounded-rectangle helper |
| Windows | `FlyoutController` | Turns a `Window` into a tray flyout: acrylic, rounded corners, placed by the taskbar, dismissed on focus loss or Esc, parked off-screen for instant reopening |
| Windows | `ConfirmDialog` | The one yes/no confirmation: Cancel is the default button, one question per window, and "no" when the window closes |
| Windows | `WindowChrome`, `WindowIcon`, `WindowPlacement` | Sharp window icons, saved size and position (ignored if the monitor is gone), bring-to-front |
| Settings | `JsonSettingsStore<T>`, `SettingsLoadResult<T>` | Atomic JSON load/save of the app's own settings class, with a schema version, ordered migrations, backups, and first run, unreadable and reset reported apart |
| Settings | `ProtectedStore`, `IDataProtector`, `AtomicFile` | Credentials kept outside `settings.json`, encrypted to the Windows user; atomic file replacement |
| Startup | `StartupRegistration`, `StartupStatus` | Start with Windows for packaged (startup task) and unpackaged (Run key) builds; reports when the user (in Windows Settings) or a policy decides instead of the app |
| Theming | `AppTheme` | System / light / dark choice applied to every open window |
| Localization | `SupportedLanguages`, `UiText`, `Localize`, `LocalizedMessage` | Interface text from per-language JSON catalogs embedded in the app, chosen from the user's override or Windows' languages; XAML attached properties that relabel open windows live; messages stored as key plus values so they follow a language change |
| App | `AppBootstrap`, `HostWindow` | Crash logging, then settings, then the hidden host window, in that order; the host is a 1x1 off-screen tool window that answers a second launch and runs the app's exit steps before `Environment.Exit` |
| App | `AppEnvironment`, `SingleInstance` | Packaged or not, version, data folders, one running copy |
| Logging | `AppLogging` | Names the app's data folder for the shared NLog config (one log file per launch beside `settings.json`, keeping the current file and three older ones), and opens that folder for "Open logs folder" |

Win32 declarations shared by these live in `Interop/NativeMethods.cs`.

The `build/` folder holds the build configuration every app shares: properties, the security floors, package versions, the NLog config and the `.gitignore` block. See "Shared build configuration".

## Using it

Add it as a submodule pinned to the latest release tag (`vX.Y.Z`; see "Versioning"), and reference the project:

```bash
git submodule add https://github.com/Dynamic-Solutions-Canada/winui-app-kit.git external/winui-app-kit
git -C external/winui-app-kit checkout vX.Y.Z
```

```xml
<ProjectReference Include="..\external\winui-app-kit\WinUIAppKit\WinUIAppKit.csproj" />
```

The kit targets `net10.0-windows10.0.22000.0` and Windows App SDK 1.8, and runs on Windows 10 22H2 (build 19045) and later; see "Windows 10". The app takes its Windows App SDK version from the kit (below) and never moves ahead of it.

### Shared build configuration

Four imports give the app the family's build settings. Each goes in the file named, at the repo root:

```xml
<!-- Directory.Build.props: shared properties first, then what is this app's own -->
<Project>
  <Import Project="$(MSBuildThisFileDirectory)external/winui-app-kit/build/WinUIAppKit.props" />

  <PropertyGroup>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
```

```xml
<!-- Directory.Build.targets -->
<Project>
  <Import Project="$(MSBuildThisFileDirectory)external/winui-app-kit/build/WinUIAppKit.targets" />
</Project>
```

```xml
<!-- Directory.Packages.props: shared versions, then this app's own packages -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <Import Project="$(MSBuildThisFileDirectory)external/winui-app-kit/build/WinUIAppKit.Packages.props" />

  <ItemGroup>
    <PackageVersion Include="System.Management" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Then, in the app:

- **Project files:** `PackageReference` items carry no `Version` (central package management supplies it). Remove `Nullable`, `ImplicitUsings` and `LangVersion`, and the platform auto-detect block in `Directory.Build.props`; the shared props set them.
- **Logging:** delete the app's own `NLog.config` and its `Content` item; the shared config is linked into every `WinExe` project. Call `AppLogging.UseDataFolder("<data folder>")` first thing in the `App` constructor, with the folder `AppEnvironment.DataDirectory` uses.
- **`.editorconfig`:** copy the kit's root `.editorconfig` unchanged.
- **`.gitignore`:** end it with `build/shared.gitignore`, unchanged. Add `external/winui-app-kit/scripts/check-gitignore .` to the app's `validate` to keep it that way.

What each file sets:

| File | Sets |
| --- | --- |
| `build/WinUIAppKit.props` | Platform auto-detect, nullable and language settings, .NET analyzers with code style in the build and a pinned analysis level, warnings as errors in Release and CI, and the security floors |
| `build/WinUIAppKit.targets` | Fails the build below the .NET SDK floor, or when a self-contained build resolves a runtime below the runtime floor; links the shared `NLog.config` into app executables |
| `build/WinUIAppKit.Packages.props` | Versions of Microsoft.WindowsAppSDK, NLog and CommunityToolkit.Mvvm |
| `build/NLog.config` | One log file per launch in `%AppData%\<data folder>`, Info and above, the shared layout, current file plus three older ones |
| `build/shared.gitignore` | The shared ignore block: build output, local signing material, secrets, editor and OS files |
| `.editorconfig` (repo root) | The family's code style |

An app never hand-edits any of these, or keeps its own copy of one: the build files and `NLog.config` are used straight from the submodule, and `.editorconfig` and the `.gitignore` block are copied unchanged. A change goes into the kit and reaches every app with the next pin bump. Two switches exist for an app that cannot take a default yet, each set in its `Directory.Build.props` with a comment saying why: `WinUIAppKitWarningsAsErrors` (`false` until the app builds warning-free) and `WinUIAppKitIncludeNLogConfig` (`false` for an executable that must not ship the log config).

## Starting a resident app

```csharp
// App.xaml.cs
protected override void OnLaunched(LaunchActivatedEventArgs args) =>
    _host = AppBootstrap.Launch(this, RestoreSettings, () => new MainWindow());

// MainWindow constructor, after InitializeComponent and the single-instance check
_host = new HostWindow(this, "My App Host", "MyApp_Show");
_host.ShowRequested += OpenSettingsWindow;
_host.AddExitStep("save settings", SettingsManager.Save);
_host.AddExitStep("remove the tray icon", _tray.Dispose);
// Tray menu: .Add(UiText.Get("Open logs folder"), AppLogging.OpenLogsFolder) ... .Add(UiText.Get("Exit"), _host.Exit)
```

- **Crash logging:** `AppBootstrap.Launch` installs the two unhandled-exception handlers (log, then flush) before anything else runs. WinUI exceptions are marked handled so the app keeps running; call `InstallCrashLogging` yourself to choose otherwise.
- **Host window:** `HostWindow` gives the host the title `SingleInstance.SignalRunningInstance` looks for. Pass the same title and message name to both.
- **Exit:** `Exit` runs every exit step in the order added (a failing step is logged and the rest still run), flushes the log, then ends the process. Put all teardown in exit steps (closing connections, unregistering notifications, removing the tray icon), never after `Exit`.
- **Confirmations:** `ConfirmDialog.ConfirmAsync(window, title, body, confirmText, cancelText)`. Name the action on the confirm button. All the text comes from the app's own catalogs.
- **Tray icon:** `TrayIconRasterizer.Render(TrayIcon.IconPixelSize, (g, side) => ...)` draws the app's artwork on a canvas `side` pixels square, using `Ink` and `Surface` for taskbar-aware colours. Re-render on `TrayIcon.RenderNeeded` and when the taskbar colour changes.

## Settings

### The settings file

Create the store with the app's migrations to make it versioned (CONF-3). Each step upgrades the file's JSON by one version, editing it in place; a file with no version is version 0, so the current version is the number of steps. Steps are never removed or reordered once shipped.

```csharp
var store = new JsonSettingsStore<UserSettings>(
    Path.Combine(AppEnvironment.DataDirectory("MyApp"), "settings.json"),
    [
        root => { if (root.Remove("OldName", out var v)) root["NewName"] = v; },   // 0 -> 1
    ]);

var result = store.Load();
switch (result.Outcome)
{
    case SettingsLoadOutcome.FirstRun: /* open the main surface once (UI-15) */ break;
    case SettingsLoadOutcome.Unreadable: /* tell the user once that settings were reset */ break;
}
```

`Load()` never throws, and always writes the file it returns, so a notice is shown only once:

| Outcome | When | Kept |
| --- | --- | --- |
| `FirstRun` | No file | Nothing; defaults are written |
| `Loaded` | The file is current | Nothing |
| `Migrated` | The file is older | `settings.json.v<old>.bak`, before the first step runs |
| `NewerVersion` | A newer app version wrote it | `settings.json.v<its version>.bak`; the file is loaded as far as this version understands it and is not rewritten until the next save |
| `Unreadable` | Not JSON, the wrong shape, or a migration step threw | `settings.json.bad`; defaults are written |

`Reset()` is the "Reset settings" action (CONF-4). It renames the file to `settings.json.<UTC time>.reset`, writes defaults and returns `Reset`. It touches nothing else: the startup task and stored credentials stay as they are.

The store writes `SchemaVersion` as the first property and owns it, so the settings class should not declare one. Default values are written and nulls omitted. The unversioned constructor and `Load(out bool isFirstRun)` keep their old behaviour, writing no version, for apps that have not switched yet.

### Credentials

`ProtectedStore` keeps credentials out of `settings.json` (SEC-1, CONF-5): one file per name in the app's data folder, encrypted with Windows data protection for the current user.

```csharp
static readonly ProtectedStore Credentials = new(
    () => AppEnvironment.PhysicalDataDirectory("MyApp"),
    "protected-{0}.dat",
    Encoding.UTF8.GetBytes("MyApp.ProtectedStore.v1"),
    logError: (message, ex) => Logger.Warn(ex, message));

Credentials.Save("api-key", new ApiKey(value));
ApiKey? key = Credentials.Load<ApiKey>("api-key");
```

- **Permanent values:** the folder, the file-name pattern and the entropy are permanent once the app ships. Changing any of them makes every stored value unreadable, so an app already keeping credentials passes its existing values.
- **Unreadable files:** `Load` returns null for a missing or unreadable file and never deletes it, because another process may read the same file. Use `Exists` to tell "never saved" from "unreadable" and tell the user.
- **Failures:** `Save` and `Clear` report failures to `logError` and do not throw. Messages never contain the stored value. Names that could leave the folder (`..`, separators) throw `ArgumentException`.
- **Second processes and tests:** `ProtectedStore.cs` and `AtomicFile.cs` depend on nothing else in the kit, NLog or WinUI. A second process, such as a shell extension, or a portable test harness can compile them in with `<Compile Include="...\ProtectedStore.cs" Link="..." />` and the `System.Security.Cryptography.ProtectedData` package. Off Windows, pass an `IDataProtector`.

## Windows 10

The kit's minimum is Windows 10 22H2 (build 19045), so an app whose manifest allows Windows 10 can use all of it. Each app's own minimum is still set by its `Package.appxmanifest`. Features that need Windows 11 check the build at runtime and degrade without throwing:

| Feature | Windows 11 | Windows 10 22H2 |
| --- | --- | --- |
| `FlyoutController` corners | Rounded by DWM | Square (the rounded-corner attribute is Windows 11 only) |
| `FlyoutController` backdrop | Desktop acrylic | Desktop acrylic when the system reports support; otherwise the flyout shows its content's own background. With Windows' transparency effects off, both show a solid colour |

Everything else behaves the same on both: the tray icon and its dark menu, flyout placement (including a taskbar on any screen edge), start with Windows, theme switching, window chrome, settings and localization.

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

## Versioning

Each release is an annotated `vX.Y.Z` tag with a GitHub Release whose notes say what changed for apps. Versions follow SemVer:

- **Major:** anything that can break an app that builds against the kit: removing or renaming a public member, changing behaviour listed under "Gotchas", raising the Windows App SDK line or the target framework, or a `.editorconfig` or analyzer change that turns code that built cleanly into an error.
- **Minor:** new types, members, options or events, with existing behaviour unchanged.
- **Patch:** fixes that do not change documented behaviour.

Apps pin a tag, never a bare commit, and every app stays on the same tag; an app that falls behind catches up in its next release at the latest. To take a new version, check the tag out in the submodule and commit the pin bump with the subject `Update WinUI App Kit: <what it changes for this app>`:

```bash
git -C external/winui-app-kit fetch --tags
git -C external/winui-app-kit checkout vX.Y.Z
git add external/winui-app-kit
```

## Rules for changes

- Keep it domain-neutral: no app names, settings fields or feature behaviour. If an app needs different behaviour, add a general option or event and keep the policy in the app.
- A breaking change is a new major version, made together with the edits each consuming app needs (see "Versioning").
- No XAML (yet): pages and windows stay in each app. Shipping XAML from a library needs extra MSIX packaging work in every app.

## License

MIT; see [LICENSE](LICENSE). Copyright (c) 2026 Dynamic Solutions.
