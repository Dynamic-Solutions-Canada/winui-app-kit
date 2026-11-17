# WinUI App Kit

Shared plumbing for small WinUI 3 desktop apps that live in the system tray: the tray icon, its menu and flyout window, the settings file, "start with Windows", theme switching, and main-window chrome. Each app keeps its own content, pages and settings; the kit only provides the parts that were being copied between apps.

## What's in it

| Area | Type | What it does |
|---|---|---|
| Tray | `TrayIcon` | Notification-area icon with a stable GUID, re-added after Explorer restarts, crisp at any scaling, click / double-click / context-menu events |
| Tray | `TrayIconHost` | Several tray icons on one host window, each with its own stable GUID, clicks and visibility |
| Tray | `TrayMenu` | Native right-click menu that follows dark mode |
| Tray | `TrayIconRasterizer` | Turns tray artwork drawn in code into a crisp PNG at the tray's exact pixel size, with the taskbar ink colours and a rounded-rectangle helper |
| Windows | `FlyoutController` | Turns a `Window` into a tray flyout: acrylic, rounded corners, placed by the taskbar, dismissed on focus loss or Esc, parked off-screen for instant reopening |
| Windows | `ConfirmDialog` | The one yes/no confirmation: Cancel is the default button, one question per window, and "no" when the window closes |
| Windows | `WindowChrome`, `WindowIcon`, `WindowPlacement` | Sharp window icons, saved size and position (ignored if the monitor is gone), bring-to-front |
| Settings | `JsonSettingsStore<T>`, `SettingsLoadResult<T>` | Atomic JSON load/save of the app's own settings class, with a schema version, ordered migrations, backups, and first run, unreadable and reset reported apart |
| Settings | `ProtectedStore`, `IDataProtector`, `AtomicFile` | Credentials kept outside `settings.json`, encrypted to the Windows user; atomic file replacement |
| Startup | `StartupRegistration`, `StartupStatus`, `RunEntry` | Start with Windows for packaged (startup task) and unpackaged (Run key) builds; reports when the user (in Windows Settings) or a policy decides instead of the app; re-points a moved portable copy's entry |
| Theming | `AppTheme` | System / light / dark choice applied to every open window |
| Localization | `SupportedLanguages`, `UiText`, `Localize`, `LocalizedMessage` | Interface text from per-language JSON catalogs embedded in the app, chosen from the user's override or Windows' languages; XAML attached properties that relabel open windows live; messages stored as key plus values so they follow a language change; the kit's own translated shared labels (`kit.*`) |
| Shared XAML | `Styles.xaml`, `SettingsShell`, `AboutPage`, `AboutInfo`, `WindowChrome.SetUpSettingsWindow` | The family's page and card styles, the settings window shell with its fixed footer, and the About page with the standard cards; compiled by the app (see "Shared XAML") |
| Shared XAML | `StartupSetting`, `ThemeSetting`, `LanguageSetting` | The Settings page rows every app has: Start with Windows, App theme and Language |
| App | `AppBootstrap`, `HostWindow` | Crash logging, then settings, then the hidden host window, in that order; the host is a 1x1 off-screen tool window that answers a second launch and runs the app's exit steps before `Environment.Exit` |
| App | `AppEnvironment`, `SingleInstance` | Packaged or not, version, data folders, one running copy |
| Updates | `UpdateService`, `UpdateCheckResult`, `GitHubReleases` | The About page's "Check for updates": the Store's updates page when packaged, the app's GitHub Releases when not; never in the background |
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
<!-- Directory.Build.props: any opt-out switch first, then the shared properties, then what is this app's own -->
<Project>
  <!-- Only while the app still has warnings to fix; must come before the import (see below). -->
  <PropertyGroup>
    <WinUIAppKitWarningsAsErrors>false</WinUIAppKitWarningsAsErrors>
  </PropertyGroup>

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

- **Tests and the .NET helper:** call the kit's `scripts/run-tests` and `scripts/dotnet` from the app's npm scripts too, for example `"test": "external/winui-app-kit/scripts/run-tests"`, and delete any copies. Like the wrapper, both work on the folder npm runs them in: `run-tests` runs every harness under the app's own `tests/` and fails when it finds none, so a test command that ran nothing never passes. An app with no harness yet passes `--allow-none` until it adds one. `dotnet` uses the app's `global.json`.
- **Windows build commands:** the kit holds the one `scripts/windows-build` wrapper (SCRIPT-6). The app deletes its own copy and calls the kit's from its npm scripts, for example `"verify:desktop": "external/winui-app-kit/scripts/windows-build --build-only --configuration Debug"`. The wrapper builds the folder it runs in, which npm sets to the package root, and stops unless that folder has a `windows-build.json`. A repo with steps to run first, such as generated files, runs them in its own npm script before calling the wrapper. `WINDOWS_BUILD_TOOLS` overrides where the build tooling is checked out.

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

An app never hand-edits any of these, or keeps its own copy of one: the build files and `NLog.config` are used straight from the submodule, and `.editorconfig` and the `.gitignore` block are copied unchanged. A change goes into the kit and reaches every app with the next pin bump. Two switches exist for an app that cannot take a default yet, each set in its `Directory.Build.props` with a comment saying why:

- **`WinUIAppKitWarningsAsErrors`:** `false` until the app builds warning-free. Set it **before** the `WinUIAppKit.props` import, as in the example above. The shared props decide warnings-as-errors when they are imported, so a value set after the import is ignored.
- **`WinUIAppKitIncludeNLogConfig`:** `false` for an executable that must not ship the log config. It is read by `WinUIAppKit.targets`, which MSBuild evaluates after every props file, so it works anywhere in `Directory.Build.props`; keeping it next to the other switch, before the import, is clearest.

## Shared XAML

The styles, the settings window shell and the About page live in `Xaml/` and reach the app as a shared-items import, the same way the promo page does, so the app compiles them as part of its own project and needs no extra packaging work (KIT-5). Add to the app's csproj:

```xml
<Import Project="..\external\winui-app-kit\Xaml\WinUIAppKit.Xaml.projitems" Label="Shared" />
```

### Styles

Merge the styles once in `App.xaml`, after `XamlControlsResources`, and delete the app's own copies:

```xml
<ResourceDictionary Source="ms-appx:///WinUIAppKit/Styles.xaml" />
```

Pages use these keys and never write padding, radius or header sizes inline (UI-3). The keys are public API: renaming or removing one is a major version.

| Key | Target | Use |
| --- | --- | --- |
| `PageContentStyle` | `StackPanel` | The root of a page's content: padding `36,12,36,36`, spacing 8, at most 960 wide |
| `PageTitleStyle` | `TextBlock` | The page title (`TitleTextBlockStyle`, 12 below) |
| `SectionHeaderStyle` | `TextBlock` | A heading above a card (`BodyStrongTextBlockStyle`) |
| `FirstSectionHeaderStyle` | `TextBlock` | The first heading, right under the title |
| `CardBorderStyle` | `Border` | A card: corner radius 8, padding `16,14`, card fill and stroke |
| `IconLabelStyle` | `StackPanel` | Button content: a glyph then a label, spacing 8 |
| `ButtonGlyphStyle` | `FontIcon` | The glyph in `IconLabelStyle` content, size 14 |
| `NavigationViewContentBackground`, `NavigationViewContentGridBorderBrush` | brushes | Transparent, so the settings window's Mica shows through |

### Settings window

The app's `SettingsWindow` hosts `SettingsShell` (UI-1, UI-2), adds its own pages and maps each tag to a page:

```xml
<Window x:Class="MyApp.SettingsWindow" xmlns:kit="using:WinUIAppKit.Pages" ...>
    <kit:SettingsShell x:Name="Shell" />
</Window>
```

```csharp
Shell.Caption = "My App";
Shell.IconPngPath = App.IconPngPath;
Shell.MenuItems.Add(new NavigationViewItem { Content = UiText.Get("nav.home"), Tag = "home", Icon = new FontIcon { Glyph = "\uE80F" } });
WindowChrome.SetUpSettingsWindow(this, Shell.TitleBar, App.IconPath, settings.SettingsWindowPlacement);
Shell.PageSelected += tag => Shell.ContentFrame.Navigate(tag switch
{
    SettingsShell.SettingsTag => typeof(SettingsPage),
    SettingsShell.AboutTag => typeof(AboutPage),          // with the app's AboutInfo as the parameter
    SettingsShell.OtherAppsTag => typeof(PromotedAppsPage),
    _ => typeof(HomePage),
});
Shell.Select("home");
```

The footer is always "Our other apps" then "About", and the built-in Settings item follows the app's language. `SetUpSettingsWindow` applies Mica, the custom title bar, the window icon, the saved placement and the app's theme. The app keeps one instance of the window and saves `WindowChrome.CapturePlacement` when it closes.

### About page

Navigate to `AboutPage` with an `AboutInfo` (UI-5, BRAND-8). The page lays out the identity, updates, links and diagnostics cards in that order, then any `ExtraCards`:

```csharp
var (source, support, license) = AboutInfo.GitHubLinks("<owner>", "<repo>");
Shell.ContentFrame.Navigate(typeof(AboutPage), new AboutInfo
{
    ProductName = "My App",
    DescriptionKey = "app.description",          // the app's own catalog key
    IconPngPath = App.IconPngPath,
    PrivacyPolicy = new Uri("<the product's registered privacy URL>"),
    Updates = Updates,                            // the app's UpdateService; null hides the card
    SourceCode = source, Support = support, License = license,
});
```

The version is the 4-part package version, marked "(unpackaged)" when the app runs unpackaged. A link with no target is hidden. "Open logs folder" opens the folder named in `AppLogging.UseDataFolder`.

### Settings page rows

The rows every app's Settings page has (UI-4) are three controls in `WinUIAppKit.Controls`. Each applies a change at once and raises an event so the app saves the choice in its own settings; every edit saves immediately, with no Save button.

```xml
<Page xmlns:kitc="using:WinUIAppKit.Controls" ...>
    <Border Style="{StaticResource CardBorderStyle}">
        <StackPanel Spacing="12">
            <kitc:StartupSetting x:Name="StartupRow" />
            <kitc:LanguageSetting x:Name="LanguageRow" />
            <kitc:ThemeSetting x:Name="ThemeRow" />
        </StackPanel>
    </Border>
</Page>
```

```csharp
StartupRow.Registration = App.Startup;                       // the StartupRegistration the app uses at launch
StartupRow.StatusChanged += isOn => { Settings.Startup = isOn; Save(); };
LanguageRow.ChoiceChanged += identity => { Settings.Language = identity; Save(); };
ThemeRow.ChoiceChanged += choice => { Settings.AppTheme = choice; Save(); };
```

| Control | Shows | When changed |
| --- | --- | --- |
| `StartupSetting` | Windows' real startup state, read when it loads. When the user turned the app off in Windows Settings, or a policy decides, the switch is disabled and a note says where the decision lives (TRAY-10) | Asks Windows; raises `StatusChanged` with the resulting state, also after the first read |
| `ThemeSetting` | `AppTheme.Current`: Use system setting, Light, Dark | `AppTheme.Set`, so every open window changes; raises `ChoiceChanged` |
| `LanguageSetting` | "Use system setting", then each of the app's languages under its own name (LOC-4). Needs `UiText.Configure` first | `UiText.SetOverride`, so open windows relabel; raises `ChoiceChanged` with the identity, or null |

An app that isn't localized leaves out `LanguageSetting`, and an app that isn't resident leaves out `StartupSetting`.

### Shared labels

The shell, the About page and the settings every app has use labels the kit owns and translates into the seven wording catalogs (D-072, LOC-7). They are `kit.*` keys that apps use through `UiText` and `Localize` like their own; an app never defines a `kit.` key, so a shared label reads the same in every app. A different wording is a change to the kit.

| Key | English |
| --- | --- |
| `kit.settings.title` | Settings |
| `kit.settings.app-theme` | App theme |
| `kit.settings.use-system-setting` | Use system setting |
| `kit.settings.light`, `kit.settings.dark` | Light, Dark |
| `kit.settings.start-with-windows` | Start with Windows |
| `kit.settings.on`, `kit.settings.off` | On, Off |
| `kit.settings.startup-user-off`, `kit.settings.startup-policy-off`, `kit.settings.startup-policy-on` | Why the startup switch is disabled |
| `kit.settings.language` | Language |
| `kit.dialog.cancel` | Cancel |
| `kit.settings.reset-title`, `kit.settings.reset-notice` | The "settings were reset" notice |
| `kit.nav.about`, `kit.nav.other-apps` | About, Our other apps |
| `kit.about.*` | The About page's labels and update messages |

The full list, with every translation, is in `WinUIAppKit/Localization/Catalogs/`. Translations new in the kit are machine translations, not yet reviewed.

### Interface text check

`scripts/check-localization.py` is the one catalog check (LOC-9). Apps run it as `check:l10n`, part of `validate`, from their repo root: `"check:l10n": "python3 external/winui-app-kit/scripts/check-localization.py"`. It reads a `localization` block in `windows-build.json`:

```json
"localization": {
  "catalog_dir": "MyApp/Localization/Catalogs",
  "unused_ok": ["promo.*"]
}
```

| Key | Use |
| --- | --- |
| `catalog_dir` | The app's `<catalog>.json` files. Required |
| `source_dirs` | Folders searched for keys. Default: the project's folder |
| `english_keys` | `true` when the English text is the key, so each en-US value must equal its key |
| `extra_key_patterns` | Regexes with one group, for keys the code builds from constants |
| `unused_ok` | Key globs used by text matching rather than as literals |

The check fails when:

- a catalog's keys differ from en-US, a value is empty, a translation's `{{placeholders}}` differ, or any text has an em dash;
- code or XAML uses a key no catalog defines, or a catalog key is never used. It counts `UiText.Get`, `Format` and `Source` calls, `Localize.SetX` calls, `l10n:Localize` attributes and the extra patterns;
- the app defines a `kit.` key, or uses one the kit lacks;
- the manifest's languages, or the catalog files on disk, differ from `build/languages.json`, or from en-US alone for an English-only app (LOC-3).

The kit runs the same script on its own catalogs with `--kit`.

### Store listing check

`scripts/check-listing.py` is the one Microsoft Store listing check (LIST-5, LIST-10, LIST-12). Apps run it as `check:listing`, part of `validate`, from their repo root: `"check:listing": "python3 external/winui-app-kit/scripts/check-listing.py"`. It reads a `listing` block in `windows-build.json`:

```json
"listing": {
  "product_name": "My App",
  "retired_names": ["Old Name"]
}
```

| Key | Use |
| --- | --- |
| `product_name` | The title every locale must carry. Required |
| `listing_path` | Default `store-assets/microsoft/listings.json` |
| `catalog_dir` | The app's wording catalogs. Default: `localization.catalog_dir`; none means an English-only app |
| `notes_dir` | Default `release-notes` |
| `readme` | Default `README.md` |
| `retired_names` | Earlier product names that must not appear in listing text (matched as written, so a lower-case search keyword is allowed) |

The check fails when:

- the listing isn't `schemaVersion` 1;
- a localized app's listing lacks the nine identities from `build/languages.json`, in order, or its Canadian identities aren't exactly `{"copyFrom": ...}`, or its full records differ from its wording catalogs (an English-only app lists en-US alone);
- a record lacks a field or has an extra one, such as `releaseNotes`;
- a field is over the Microsoft Store's limits, including 21 keyword words combined;
- caption counts differ between locales, or any text has an em dash or a retired name;
- a title isn't the product name, or the README's header tagline isn't the first sentence of the en-US `shortDescription`;
- the release notes for the current `<Version>` are missing for any wording catalog, have a heading, run over 1,500 characters, or include a file for a Canadian identity.

## Packaging

`build/build-msix.ps1` is the one MSIX packaging script for every app (KIT-9, DIST-1). The app deletes its own script and points `package_script` in `windows-build.json` at the kit's; the shared Windows build tooling runs it for `build:desktop`, `package:desktop` and `install:desktop`. Everything that differs between apps is a `windows-build.json` key, listed with its default at the top of the script; the minimum is:

```json
{
  "project": "MyApp/MyApp.csproj",
  "package_script": "external/winui-app-kit/build/build-msix.ps1",
  "msix_dir": "MyAppMSIX",
  "manifest_strings": { "catalog_dir": "MyApp/Strings" }
}
```

- **Steps:** it publishes the app self-contained, plus any `companions`, assembles the layout with the compiled XAML, stamps the manifest's version and identity, builds one `resources.pri` for every declared language, checks the manifest strings resolve in each, then packs and signs.
- **Languages:** the regional identities (en-CA, fr-CA) reuse the US English and French catalogs, as listed in `build/languages.json`, the family's one language list (LOC-2).
- **Signing:**
  - Signing material is only ever a parameter. There is no fallback certificate, no generated certificate and no written password, and `-NoSign` builds the unsigned Store upload.
  - `-StoreIdentity` builds a signed bundle with the Store identity, for updating a Store install in place.
  - signtool takes the password on its command line, where other processes of the same user can see it while it runs. That is accepted for the local development certificate and the per-run CI certificate.

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
- **Confirmations:** `ConfirmDialog.ConfirmAsync(window, title, body, confirmText)`. Name the action on the confirm button; the question and that label come from the app's own catalogs, and Cancel is the kit's shared label unless the app passes `cancelText`.
- **Tray icon:** `TrayIconRasterizer.Render(TrayIcon.IconPixelSize, (g, side) => ...)` draws the app's artwork on a canvas `side` pixels square, using `Ink` and `Surface` for taskbar-aware colours. Re-render on `TrayIcon.RenderNeeded` and when the taskbar colour changes.

### Several tray icons

An app with more than one icon (one per launcher, say) creates a `TrayIconHost` on its host window and adds an icon for each. Every icon has the same events and `Update` as a single `TrayIcon`, and `IsVisible` hides or shows it without losing its image:

```csharp
_trayHost = new TrayIconHost(_host.Hwnd, "MyApp_TrayCallback");
foreach (var launcher in settings.Launchers)
{
    var icon = _trayHost.Add(TrayIconHost.GuidFromName("MyApp.Tray." + launcher.Id));
    icon.Clicked += anchor => ShowFlyout(launcher, anchor);
    icon.RenderNeeded += () => icon.Update(Render(launcher), launcher.Name);
    icon.IsVisible = launcher.ShowInTray;
    icon.Update(Render(launcher), launcher.Name);
}
_host.AddExitStep("remove the tray icons", _trayHost.Dispose);
```

- **Stable identity:** each icon's GUID must never change, because Windows remembers the user's show-or-hide choice by it. `GuidFromName` derives one from a name the app chooses (an MD5 name hash with version-3 bits), so the same launcher always gets the same icon. A launcher whose id is already a GUID can use it directly.
- **One listener:** the host listens on the window once, routes each click to its icon, re-adds every visible icon when Explorer restarts, and raises `RenderNeeded` and `DisplayChanged` on itself and on every icon.
- **Single icons:** `new TrayIcon(hwnd, guid, callbackMessageName)` still creates an app's single icon, with a host of its own.

## Checking for updates

Each app creates one service with its repository, defined once in the app (BRAND-5), and calls it only from the About page's "Check for updates" button (DIST-6). Nothing checks in the background.

```csharp
static readonly UpdateService Updates = new("<owner>", "<repo>");   // a monorepo lane adds its tag prefix: new(owner, repo, "desktop-")

var result = await Updates.CheckForUpdatesAsync();
switch (result.Outcome)
{
    case UpdateCheckOutcome.UpdateAvailable: /* show result.LatestVersion; a button calls UpdateService.OpenReleasePage(result) */ break;
    case UpdateCheckOutcome.UpToDate: /* "You have the latest version" */ break;
    case UpdateCheckOutcome.Failed: /* "Couldn't check for updates" */ break;
    case UpdateCheckOutcome.OpenedStore: break;   // packaged: the Store's updates page is open
}
```

- **Packaged builds:** the check opens the Microsoft Store's updates page. The Store owns updating packaged copies.
- **Unpackaged builds:**
  - It lists the repository's GitHub Releases and picks the newest published, non-prerelease release by version (1.10.0 beats 1.9.0).
  - Only tags for the app's lane count: `vX.Y.Z`, or `<prefix>vX.Y.Z` with a tag prefix.
  - A repository with no releases counts as up to date.
- **Comparison:** major, minor and patch only, so a dev build's MSIX revision never looks like an update.
- **The running version:** `AppEnvironment.Version` unless the app passes another.
- **Failures:** the method never throws. Network, rate-limit and parse failures all give `Failed` and are logged.

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

**Telling the user (CONF-2, UI-15):** when `result.NeedsResetNotice` is true (only after an unreadable file), open the settings window on this launch, as on first run, and put the notice at the top of its page:

```csharp
if (App.SettingsResult.NeedsResetNotice)
{
    PageContent.Children.Insert(0, SettingsResetNotice.Create(App.SettingsResult.BackupPath));
}
```

The store has already written defaults, so the next launch loads normally and the notice can't come back. A silently started app that waits for the user to open Settings would lose it. The notice is a closable warning bar with the kit's translated text, naming only the backup file, never its folder.

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
- **Portable copies move.** Each download of a portable build unzips to its own folder, leaving the Run entry aimed at the old copy. Call `StartupRegistration.RepointRunEntry()` once at startup in builds the app treats as portable. It rewrites only an entry that already exists, under the same name, so it never turns startup on and never undoes a choice the user made in Task Manager or Windows Settings. It does nothing when packaged.
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
- XAML lives only in `Xaml/` and reaches apps through the shared-items import, compiled by each app; the library project holds none, so no app needs library `.xbf` or PRI handling. App pages and windows stay in the app.

## License

MIT; see [LICENSE](LICENSE). Copyright (c) 2026 Dynamic Solutions.
