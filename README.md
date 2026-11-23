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
| Windows | `OsdController` | Turns a `Window` into an on-screen display like Windows' volume popup: bottom centre, never activated, click-through, held then faded, parked off-screen for instant reappearance |
| Windows | `ConfirmDialog` | The one yes/no confirmation: Cancel is the default button, one question per window, and "no" when the window closes |
| Windows | `WindowChrome`, `WindowIcon`, `WindowPlacement` | Sharp window icons, saved size and position (ignored if the monitor is gone), a minimum size that follows each monitor's scaling, bring-to-front |
| Settings | `JsonSettingsStore<T>`, `SettingsLoadResult<T>` | Atomic JSON load/save of the app's own settings class, with a schema version, ordered migrations, backups, and first run, unreadable and reset reported apart |
| Settings | `ProtectedStore`, `IDataProtector`, `AtomicFile` | Credentials kept outside `settings.json`, encrypted to the Windows user; atomic file replacement |
| Startup | `StartupRegistration`, `StartupStatus`, `RunEntry` | Start with Windows for packaged (startup task) and unpackaged (Run key) builds; reports when the user (in Windows Settings) or a policy decides instead of the app; re-points a moved portable copy's entry |
| Theming | `AppTheme` | System / light / dark choice applied to every open window |
| Localization | `SupportedLanguages`, `UiText`, `Localize`, `LocalizedMessage`, `TextCatalogs`, `TextLookup`, `PluralRules`, `TextFormat` | Interface text from per-language JSON catalogs embedded in the app (one file per language, or area files merged at load), chosen from the user's override or Windows' languages; plurals; a dependency-free core a companion process can link; XAML attached properties that relabel open windows live; messages stored as key plus values so they follow a language change; the kit's own translated shared labels (`kit.*`) |
| Shared XAML | `Styles.xaml`, `SettingsShell`, `AboutPage`, `AboutInfo`, `WindowChrome.SetUpSettingsWindow` | The family's page and card styles, the settings window shell with its fixed footer, and the About page with the standard cards; compiled by the app (see "Shared XAML") |
| Shared XAML | `StartupSetting`, `ThemeSetting`, `LanguageSetting`, `ResetSetting` | The Settings page rows every app has: Start with Windows, App theme, Language and Reset settings |
| App | `AppBootstrap`, `HostWindow` | Crash logging, then settings, then the hidden host window, in that order; the host is a 1x1 off-screen tool window that answers a second launch and runs the app's exit steps before WinUI's orderly shutdown (`Application.Exit`), also when Windows shuts down, restarts or signs out |
| App | `AppEnvironment`, `SingleInstance` | Packaged or not, version, data folders, one running copy (bringing the first copy forward, with or without a host window) |
| Updates | `UpdateService`, `UpdateCheckResult`, `GitHubReleases` | The About page's "Check for updates": the Store's updates page when packaged, the app's GitHub Releases when not; never in the background |
| Logging | `AppLogging` | Names the app's data folder for the shared NLog config (one log file per launch beside `settings.json`, named from its start time in UTC, keeping the current file and three older ones), and opens that folder for "Open logs folder" |

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

The kit targets `net10.0-windows10.0.22000.0` and Windows App SDK 2.x, and runs on Windows 10 22H2 (build 19045) and later; see "Windows 10". The app takes its Windows App SDK version from the kit (below) and never moves ahead of it.

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

- **Tests and the .NET helper:** call the kit's `scripts/run-tests` and `scripts/dotnet` from the app's npm scripts too, for example `"test": "external/winui-app-kit/scripts/run-tests"`, and delete any copies. Like the wrapper, both work on the folder npm runs them in: `run-tests` runs every harness under the app's own `tests/` (.NET console projects, `*.Tests.ps1` scripts with PowerShell, and `*.test.sh` scripts with bash on the host) and fails when it finds none, so a test command that ran nothing never passes. An app with no harness yet passes `--allow-none` until it adds one. `dotnet` uses the app's `global.json`.
- **Formatting:** call the kit's `scripts/format` too, `"format": "external/winui-app-kit/scripts/format"` and `"format:check": "external/winui-app-kit/scripts/format --check"`, and delete any copy. It formats every `.csproj` under the folder it runs in, skipping `external/`, `.artifacts/`, `.worktrees/` and `node_modules/`. A rewrite repeats until `dotnet format --verify-no-changes` passes, at most three passes, because one pass can leave work for the next (inserting braces creates new whitespace to fix); a project that never settles fails with its name. A project that can't compile on Linux (a UWP project whose WinRT projections only a Windows tool generates) sets `<WinUIAppKitFormatWhitespaceOnly>true</WinUIAppKitFormatWhitespaceOnly>`: it is restored, then gets `dotnet format whitespace` only, and its code style is enforced by its Windows build (`EnforceCodeStyleInBuild`, warnings as errors in Release).
- **Icons and the pre-commit check:** call the kit's `scripts/devkit` instead of installing devkit, so the app keeps no Node dependencies (SCRIPT-1). `"icons": "external/winui-app-kit/scripts/devkit icons"` and `"check:icons": "external/winui-app-kit/scripts/devkit icons --check"` (in `validate`) run devkit's `devkit-icons` on the app's `assets/brand/` masters, and `external/winui-app-kit/scripts/devkit precommit --max-size 5MB` in `lefthook.yml` runs `devkit-precommit`, refusing staged signing material, env files, `.secrets/`, private keys and oversized files. It replaces lefthook jobs for those; keep the app's jobs for anything it doesn't cover, such as `.aws/`, `.windows-build/`, `*.snk`, `settings.json` and `protected-*.dat`. Every argument passes through, so the policy (`--allow`, `--max-size`) stays in the app. See devkit's README, "Icons" and "Refusing secrets", for what each does. The kit pins devkit and the icon renderer exactly in `tools/devkit/package-lock.json` and installs them into its own ignored `tools/devkit/node_modules` on first use and after a pin bump. It needs Node 22.12 or later, below 25; a CI job that runs it sets up Node first (the kit's Store workflow does).
- **Windows build commands:** the kit holds the one `scripts/windows-build` wrapper (SCRIPT-6). The app deletes its own copy and calls the kit's from its npm scripts, for example `"verify:desktop": "external/winui-app-kit/scripts/windows-build --build-only --configuration Debug"`. The wrapper builds the folder it runs in, which npm sets to the package root, and stops unless that folder has a `windows-build.json`. A repo with steps to run first, such as generated files, runs them in its own npm script before calling the wrapper. `WINDOWS_BUILD_TOOLS` overrides where the build tooling is checked out.

- **Project files:** `PackageReference` items carry no `Version` (central package management supplies it). Remove `Nullable`, `ImplicitUsings` and `LangVersion`, and the platform auto-detect block in `Directory.Build.props`; the shared props set them.
- **Logging:** delete the app's own `NLog.config` and its `Content` item; the shared config is linked into every `WinExe` project. Call `AppLogging.UseDataFolder("<data folder>")` first thing in the `App` constructor, with the folder `AppEnvironment.DataDirectory` uses.
- **`.editorconfig`:** copy the kit's root `.editorconfig` unchanged.
- **`.gitignore`:** end it with `build/shared.gitignore`, unchanged, and paste it again when a kit release changes it. Add `external/winui-app-kit/scripts/check-gitignore .` to the app's `validate` to keep it that way.

What each file sets:

| File | Sets |
| --- | --- |
| `build/WinUIAppKit.props` | Platform auto-detect, nullable and language settings, .NET analyzers with code style in the build and a pinned analysis level, warnings as errors in Release and CI, and the security floors |
| `build/WinUIAppKit.targets` | Fails the build below the .NET SDK floor, or when a self-contained build resolves a runtime below the runtime floor; links the shared `NLog.config` into app executables; keeps the Windows ML natives the framework supplies out of the published app (see Packaging) |
| `build/WinUIAppKit.Packages.props` | Versions of Microsoft.WindowsAppSDK, NLog and CommunityToolkit.Mvvm |
| `build/NLog.config` | One log file per launch in `%AppData%\<data folder>`, Info and above, the shared layout, current file plus three older ones |
| `build/shared.gitignore` | The shared ignore block: build output, git worktrees (`.worktrees/`), local signing material, secrets, editor and OS files |
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
| `IconLabelStyle` | `StackPanel` | Button content: a glyph then a label, spacing 8. Screen readers get no name from it, so set `l10n:Localize.Name` on the button to the label's key (UI-14) |
| `ButtonGlyphStyle` | `FontIcon` | The glyph in `IconLabelStyle` content, size 14 |
| `NavigationViewContentBackground`, `NavigationViewContentGridBorderBrush` | brushes | Transparent, so the settings window's Mica shows through |
| `SettingsWindowBackgroundBrush` | brush | The settings window's background behind the shell: transparent over Mica, and the high-contrast window colour in high contrast. A window that doesn't host `SettingsShell` sets it on its root element, or Windows 10 shows a light background behind high-contrast content |

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

An item that runs something instead of opening a page ("Add ...") goes in `MenuItems` with `SelectsOnInvoked="False"` and a string `Tag`. The shell never selects it and raises `ActionInvoked` with its tag instead of `PageSelected`; `Select` returns false for it.

The footer is always "Our other apps" then "About", and the built-in Settings item follows the app's language. `SetUpSettingsWindow` applies Mica, the custom title bar, the window icon, a minimum size (500 × 500 DIPs unless the app passes `minWidthDip` and `minHeightDip`), the saved placement and the app's theme. The shell shows the full 240-wide pane from 860 epx wide, and a compact icon rail below that, so pages keep about 450 epx at the minimum; `WindowChrome.SetMinimumSize` gives any other window the same scale-aware minimum. The app keeps one instance of the window and saves `WindowChrome.CapturePlacement` when it closes.

### About page

Navigate to `AboutPage` with an `AboutInfo` (UI-5, BRAND-8). The page lays out the identity, updates, links and diagnostics cards in that order, then any `ExtraCards`:

```csharp
var (source, support, license) = AboutInfo.GitHubLinks("<owner>", "<repo>");
Shell.ContentFrame.Navigate(typeof(AboutPage), new AboutInfo
{
    ProductName = "My App",
    DescriptionKey = "app.description",          // the app's own catalog key, or Description = () => ... for .resw text
    IconPngPath = App.IconPngPath,
    PrivacyPolicy = new Uri("<the product's registered privacy URL>"),
    Updates = Updates,                            // the app's UpdateService; null hides the card
    SourceCode = source, Support = support, License = license,
});
```

An app whose text is in `.resw` files (LOC-6) sets `Description = () => resources.GetString("AppDescription")` instead of `DescriptionKey`; exactly one of the two is set. Every other app-supplied piece of the kit's pages is already built by the app (menu items, `ExtraCards`) or a `Func<string>` (`ResetSetting.KeptNote`), so a `.resw` app can use the whole kit.

The version is the 4-part package version, marked "(unpackaged)" when the app runs unpackaged. A link with no target is hidden. "Open logs folder" opens the folder named in `AppLogging.UseDataFolder`.

Set `ThirdPartyNotices` to the GitHub link of the file the [third-party notices](#third-party-notices) generator writes. Keep its path in a constant, `public const string NoticesPath = "MyApp/THIRD-PARTY-NOTICES.txt";`, build the link as `new Uri(source + "/blob/main/" + NoticesPath)`, and name the constant's file as `notices.about_source`, so `check:notices` fails if the path and the file ever part.

### Settings page rows

The rows every app's Settings page has (UI-4) are controls in `WinUIAppKit.Controls`. The first three each apply a change at once and raise an event so the app saves the choice in its own settings; every edit saves immediately, with no Save button.

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

The fourth row, `ResetSetting` (CONF-4), puts the app's settings back to their defaults. It goes in its own card below the others. It asks first with `ConfirmDialog` (Cancel is the default), then runs the app's reset, and knows nothing about what the app keeps:

```xml
<Border Style="{StaticResource CardBorderStyle}">
    <kitc:ResetSetting x:Name="ResetRow" />
</Border>
```

```csharp
ResetRow.Window = SettingsWindow.GetCurrent();
ResetRow.ResetAsync = () => { SettingsManager.Reset(); return Task.CompletedTask; };  // store Reset, carry over what stays, apply theme and language, save
ResetRow.KeptNote = () => UiText.Get("settings.reset-kept");                          // optional: what the reset keeps
ResetRow.ResetCompleted += () => Frame.Navigate(typeof(SettingsPage));              // show the defaults
```

The app's `ResetAsync` does the whole reset: `JsonSettingsStore.Reset`, which keeps the old file beside the new one, then anything it carries over, then `AppTheme.Set` and `UiText.SetOverride` with the reset values, then the save. The kit's wording says the current settings are kept as a backup next to the settings file, which holds when the reset goes through `JsonSettingsStore.Reset`. It says nothing about Start with Windows, which not every app has; an app with a startup entry can say it stays as it is in its `KeptNote`. `KeptNote` adds the app's own sentence below the kit's in the question, read when it opens so it is in the current language. A failing `ResetAsync` is logged, and `ResetCompleted` is not raised.

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
| `kit.settings.reset-header`, `kit.settings.reset-desc`, `kit.settings.reset-button` | Reset settings, its description, Reset settings |
| `kit.settings.reset-question`, `kit.settings.reset-body`, `kit.settings.reset-confirm` | The reset question, what it resets, Reset |
| `kit.settings.language` | Language |
| `kit.dialog.cancel` | Cancel |
| `kit.settings.reset-title`, `kit.settings.reset-notice` | The "settings were reset" notice |
| `kit.nav.about`, `kit.nav.other-apps` | About, Our other apps |
| `kit.about.*` | The About page's labels and update messages |

The full list, with every translation, is in `WinUIAppKit/Localization/Catalogs/`. Translations new in the kit are machine translations, not yet reviewed.

### Interface text in code

`UiText.Get`, `Format` and `Source` are unchanged. Also:

- **Plurals:** `UiText.Plural("files.count", n, ("folder", name))` reads `files.count.one` or `files.count.other` by the language's rule and fills `{{count}}` with `n`. English, Spanish and German use "one" only for 1; French and Portuguese also for 0; Chinese and Japanese always use "other", so their catalogs give both forms the same text. Define both forms in every catalog. A language needing more forms needs a rule in `PluralRules` first.
- **Catalogs split by area:** instead of `<catalog>.json`, an app may embed `<catalog>/<area>.json` files under the same prefix (for example `LogicalName="MyApp.Catalogs.%(RecursiveDir)%(Filename)%(Extension)"`). They are merged when a catalog is first read; a key in two areas keeps the first and is logged, and `check:l10n` with `catalog_layout: "folders"` fails on it before anything ships.
- **`UiText.DeviceLocale`** is the identity "Use system setting" resolves to, for a label such as "Use system setting (Français)". **`UiText.Catalog(identity)`** returns a whole merged catalog, for completeness tests or searching another language's wording.
- **A process without WinUI, WinRT or NLog** (a Native AOT companion, a test harness) links `Localization/TextCatalogs.cs` and `Localization/SupportedLanguages.cs` and uses `new TextLookup(new TextCatalogs(assembly, prefix), languages, () => locale)` directly, with the same `Get`, `Format`, `Plural` and `Catalog`. It works out `locale` itself, for example `languages.NormalizeOverride(saved) ?? languages.Resolve(preferences)`. `UiText` is a wrapper over the same core.

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
| `english_keys` | `true` when the English text is the key, so each en-US value must equal its key. Keys the manifest uses (`ms-resource:Resources/<Key>`) are exempt and count as used; see [Packaging](#packaging) |
| `extra_key_patterns` | Regexes with one group, for keys the code builds from constants |
| `unused_ok` | Key globs used by text matching rather than as literals |
| `catalog_layout` | `"files"` (default): one `<catalog>.json` each. `"folders"`: `<catalog>/<area>.json` files, merged the way `UiText` merges them |

The check fails when:

- a catalog's keys differ from en-US, a value is empty, a translation's `{{placeholders}}` differ (a plural form may leave out `{{count}}`), or any text has an em dash;
- with `"folders"`, a key is defined in two area files of one catalog;
- a plural key lacks one of its forms: `UiText.Plural("x", ...)` uses `x.one` and `x.other`, and both must exist;
- code or XAML uses a key no catalog defines, or a catalog key is never used. It counts the literals at the top level of `UiText.Get`, `Format`, `Source` and `Plural` calls (literals inside nested parentheses are values, not keys), `Localize.SetX` calls, `l10n:Localize` attributes and the extra patterns;
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
| `catalog_dir` | The app's wording catalogs. Default: `localization.catalog_dir`; none means an English-only app. Set it to `null` to keep the listing and Store notes English only while the interface is localized |
| `catalog_layout` | How `catalog_dir` holds the catalogs: `"files"` (default), one `<catalog>.json` each, or `"folders"`, one `<catalog>/` folder each, as with `.resw` files (LOC-6) |
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

### Store screenshots

`scripts/store/render-screenshots.py` crops the app's real desktop captures into its Microsoft Store screenshots and checks them (LIST-8, ASSET-9). Apps run it from their repo root as two scripts, with the check in `validate`:

```json
"shots:render": "python3 external/winui-app-kit/scripts/store/render-screenshots.py",
"check:screenshots": "python3 external/winui-app-kit/scripts/store/render-screenshots.py --check"
```

It reads `store-assets/manifest.json`, every Store screenshot in listing order:

```json
{
  "microsoft": [
    { "file": "screenshots/00-home-dark.png", "source": "raw/microsoft/00-home-dark.png",
      "theme": "dark", "caption": "The en-US caption", "alt": "What the screenshot shows" }
  ]
}
```

| Key | Use |
| --- | --- |
| `size` | Optional, top level. The output size, default `1366x768` |
| `file` | The screenshot, under `store-assets/microsoft/` |
| `source` | The desktop capture, under `store-assets/` (committed, with its `.json` beside it) |
| `theme` | Always `dark`: Store screenshots show dark mode |
| `crop` | `window` (default): centred on the app's window, taskbar left out. `tray`: the bottom-right corner, taskbar included, for a tray flyout; the flyout must be on screen with its margin on the left and top, so a tall one needs a larger `size`, such as `1920x1080`. `bottom`: the bottom of the screen, taskbar included, centred on the window, for a popup placed above the taskbar |
| `caption`, `alt` | The en-US caption (the same text and order as `screenshotCaptions` in `listings.json`) and alt text |

- **Capture** the installed app on the test VM with the windows-build-tools app-test helpers (windows-build-tools 73b96b8 or later: `screenshot-desktop.ps1 -Mode Prepare`, then `Set-WindowCentered` and `Save-DesktopCapture <name> <window>` from `ui-lib.ps1`), run from the app's repo root (so the VM is claimed for the app): the whole desktop at 1:1, at 1920 x 1080 and 100% scale, fully dark, with the real wallpaper (the Windows 11 dark default, `img19.jpg`), window effects on (rounded corners, border, shadow, Mica), and the app's window focused and centred on the work area (a `bottom` popup stays where the app places it). Click a neutral spot first so no focus rectangle shows. Beside each `<name>.png` the helper writes `<name>.json`: the screen size, display scale, the window's frame (DWM extended frame bounds), the work area, the apps and system theme, the wallpaper, whether effects were on, the OS build and the capture time in UTC.
- **Render** crops each capture to the output size, with nothing composited, shaved or scaled, and writes it as 8-bit RGB without alpha, with unfiltered rows so the check can read it without Docker (files are larger, around 0.3 to 1.2 MB each). `window` centres the crop on the window and moves it only as far as needed to keep the taskbar out; the window and a 24-pixel margin for its shadow must fit, so keep it under about 1318 x 720. `bottom` centres the crop on the window sideways, keeps it on the screen, and takes the screen's bottom strip; the window and its margin must fit inside it. ImageMagick runs in a disposable container capped at 1 GB, so the host needs only Docker; files stay owned by the caller. It renders nothing while any capture is unusable.
- **Check** (no Docker) fails when a capture has no `.json`, or that says it was scaled, taken with apps or the system in light mode; when the app's window in the screenshot is bright (mean brightness 100 of 255 or more), which catches an app set to its own light theme on a dark Windows; when a screenshot wasn't written by this renderer; without window effects, on another wallpaper, or at a size other than the PNG's; when the window doesn't fit its crop; when an entry isn't dark; when a screenshot is missing, isn't exactly the output size, or isn't 8-bit RGB without alpha; when a caption or alt text is empty; when a PNG in the screenshots folder isn't in the manifest; when there are fewer than 1 or more than 10; or when the captions differ from the en-US `screenshotCaptions` in `store-assets/microsoft/listings.json`. The `.json` records how a capture was made; it can't prove the pixels weren't edited afterwards, which is why the captures are committed for review. Nor can the check tell an out-of-date screenshot from a fresh one: render again after recapturing.
- **Sync** the files and each locale's captions to Partner Center with the next submission, in the same upload archive as its package: a second archive replaces the package.

### Third-party notices

`scripts/generate-notices.py` writes the app's third-party notices from its NuGet restore graph (HYG-3). Apps run it from their repo root as two scripts, and put `check:notices` in `validate` after `format:check`, which restores the project:

```json
"generate:notices": "python3 external/winui-app-kit/scripts/generate-notices.py",
"check:notices": "python3 external/winui-app-kit/scripts/generate-notices.py --check"
```

It reads an optional `notices` block in `windows-build.json`. Most apps need none of it:

| Key | Use |
| --- | --- |
| `project` | The project whose packages ship. Default: the top-level `project` |
| `output` | Where the notices go. Default: `THIRD-PARTY-NOTICES.txt` beside the project |
| `product_name` | The name in the file's header. Default: `listing.product_name` |
| `about_source` | The C# file defining `NoticesPath`, the repo-relative path the app builds its About link from. Optional; when set, the check holds the constant to the output path |

Every package in the app's graph and each `companions` project's (they ship in the same package) is listed with its version, licence, copyright and project link, except build tooling (names containing `BuildTools`, and the .NET runtime's own Native AOT and trimming compilers, ILCompiler and ILLink, whose output is the runtime itself), followed by each distinct licence text in full and a link to the .NET runtime's notices. An Apache-2.0 package's own NOTICE file is passed on too, as that licence requires. Licence texts the script knows are MIT, BSD-3-Clause and Apache-2.0 (the last kept verbatim in `scripts/licences/`). Include the output in the package as `Content`, so it ships with the app.

`check:notices` fails when:

- the committed file differs from what the generator writes now (run `generate:notices` and commit it);
- a package declares no licence, names a licence file it doesn't contain, or uses an SPDX licence the script has no text for (add the text to `SPDX_TEXTS` in the kit);
- a string in the project's C# code links to the app's own notices on GitHub (`/blob/<branch>/` or `/raw/<branch>/`, ending in the output's file name) but names another path, such as the About page's link. Links into another repository, such as the .NET runtime's notices, are skipped; the app's repository comes from its `origin` remote, and without one every such link is checked.
- `about_source` is set and that file has no `NoticesPath = "..."` constant, or the constant isn't the output path.

Both `generate:notices` and `check:notices` stop when the restore graph is older than the package versions declared now (the kit's and the app's `Directory.Packages.props`), naming each package that moved, so neither writes nor passes an old list. Restore again, with `npm run format:check`, then regenerate.

**After a kit pin bump**, run `npm run format:check` and then `npm run generate:notices`, and commit the notices with the bump. A bump that moves a shared package changes the list: v2.0.0's Windows App SDK 2.6.5 adds Microsoft.Windows.AI.MachineLearning and a newer WebView2, for example.

**Dependabot package updates** change the restore graph but not the notices, so `check:notices` fails on their pull requests. Redo the bump on main instead: change the version yourself, run `npm run generate:notices`, and commit both together, then close the Dependabot PR; history stays one commit per change. If you'd rather keep the PR, push a commit with the regenerated notices to its branch. Dependabot stops updating a PR once someone else has pushed to it, so comment `@dependabot recreate` if it needs rebuilding. Don't automate the regeneration in a workflow: a push made with the workflow token doesn't start the checks again, and generating runs the new package's build files with write access.

## Packaging

`build/build-msix.ps1` is the one MSIX packaging script for every app (KIT-9, DIST-1). The app deletes its own script and points `package_script` in `windows-build.json` at the kit's; the shared Windows build tooling runs it for `build:desktop`, `package:desktop` and `install:desktop`. Everything that differs between apps is a `windows-build.json` key, listed with its default at the top of the script. The minimum is below; `package_output` has a default in the script but the shared Windows build tooling reads it to collect the packages, so set it:

```json
{
  "project": "MyApp/MyApp.csproj",
  "package_script": "external/winui-app-kit/build/build-msix.ps1",
  "msix_dir": "MyAppMSIX",
  "package_output": "MyAppMSIX/bin/msix-output",
  "manifest_strings": { "catalog_dir": "MyApp/Strings" }
}
```

- **Companions:** each `companions` entry is another project in the same package, published self-contained for the same platform. `"builder": "msbuild"` builds it with Visual Studio's MSBuild instead of `dotnet` (a UWP XAML project, whose compiler ships only with Visual Studio), finding an installation with every component in `"requires"` and naming them when none has them. A nested array in `"requires"` lists alternatives, any one of which will do, for tools whose component id differs between Build Tools and the full Visual Studio: `[["Microsoft.VisualStudio.ComponentGroup.UWP.BuildTools", "Microsoft.VisualStudio.Workload.Universal"]]` finds the UWP XAML compiler in either, including GitHub's hosted Windows images. `"xaml": true` copies its compiled XAML (`.xbf`) beside it, keeping paths; both apps' XAML share the package's `ms-appx:///` namespace, so the companion keeps its XAML in a folder of its own, and a file that would replace one already in the layout fails the build. `"files"` lists what to copy (default everything), `"destination"` where, and `"remove"` what to drop.
- **Steps:** it publishes the app self-contained, plus any `companions`, assembles the layout with the compiled XAML, stamps the manifest's version and identity, builds one `resources.pri` for every declared language, checks the manifest strings resolve in each, then packs and signs.
- **Deliverables:** every finished package is also copied to `.artifacts/packages/<version>/` under its LAYOUT-12 name, `<package_name>-<version>-windows-<arch>.msix`, or `<package_name>-<version>-windows-<arch>-store.msixbundle` for a `-StoreIdentity` build. The version is as written in the version file, so three parts for a release and four for a dev build, and a later build of the same version replaces the earlier copy. `package_output` keeps its own copy under the old name, which the Store workflow reads. Name `package_name` in PascalCase with no dots or spaces; any other name still builds, with a warning.
- **Older packages:** after a build, packages of other versions (`<package_name>-<version>-<x64|ARM64>.msix` or `.msixbundle`) are removed from `package_output`, so after building both platforms it holds just the current pair to upload by hand. Their copies in `.artifacts/packages/` stay.
- **Manifest strings:** each `ms-resource:Resources/<Key>` in the manifest is read from the `manifest_strings` catalogs. `check:l10n` counts those keys as used and exempts them from `english_keys`, since the manifest needs identifier keys, so an English-keyed app can keep `AppDescription` beside its English-text keys.
- **Windows App Runtime:** the app's `Package.appxmanifest` depends on the runtime framework matching the kit's Windows App SDK, in its `<Dependencies>`: `<PackageDependency Name="Microsoft.WindowsAppRuntime.2" MinVersion="2.7.0.0" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />`. Raise `MinVersion` when the kit's release notes say so; `build-msix.ps1` fails with the value to set when the manifest has no such dependency or a `MinVersion` below the runtime of the Windows App SDK the app restored. The Store installs and updates the framework for users; a test PC needs it installed first.
- **Windows ML:** the Windows App SDK brings Windows ML into every app, and the Windows App Runtime framework supplies its natives (`onnxruntime.dll`, `DirectML.dll`, `Microsoft.Windows.AI.MachineLearning.dll`). The SDK leaves them out of the build output but `dotnet publish` adds them back, about 17.7 MB compressed per package, so `WinUIAppKit.targets` removes them from the publish list too, and `tests/build/ml-publish.Tests.ps1` checks it. An app that uses Windows ML needs no change: the framework supplies them (Microsoft's framework-dependent deployment), through the `PackageDependency` it already declares. Only the small managed projections stay in the package.
- **Languages:** the regional identities (en-CA, fr-CA) reuse the US English and French catalogs, as listed in `build/languages.json`, the family's one language list (LOC-2).
- **Signing:**
  - Signing material is only ever a parameter. There is no fallback certificate, no generated certificate and no written password, and `-NoSign` builds the unsigned Store upload.
  - `-StoreIdentity` builds a signed bundle with the Store identity, for updating a Store install in place.
  - signtool takes the password on its command line, where other processes of the same user can see it while it runs. That is accepted for the local development certificate and the per-run CI certificate.

## Touring an installed app

`scripts/app-test/tour.ps1` screenshots every page of a kit app's settings window in the app's light and dark themes and in Windows high contrast, plus "Our other apps" at a narrow width. It runs in a test VM through the windows-build-tools app-test recipe (`vm/app-test/` in that repo, which installs the package and owns `ui-lib.ps1`), never on the host's own desktop. Copy the tour and `ui-lib.ps1` to `C:\apptest`, write `C:\apptest\tour.json`, and run the tour with `app_test_ui`:

```json
{ "package": "MyCompany.MyApp", "process": "MyApp", "prefix": "myapp", "skip": [] }
```

| Key | Use |
| --- | --- |
| `package` | The installed package's name, as `Get-AppxPackage` finds it |
| `process` | The app's process name, without `.exe` |
| `prefix` | The start of each screenshot's name: `C:\apptest\out\<prefix>-<theme>-<page>.png` |
| `skip` | Navigation items the tour must not click, because clicking one acts (creates or changes something) instead of opening a page |

It relies on the kit's Settings page (its "App theme" combo box) and the "Our other apps" item, and leaves the app on "Use system settings" and closed.

## Publishing to the Microsoft Store

The kit holds the one Store publishing workflow (CI-10, REL-7) and its credential reminder (CI-11) as reusable workflows. An app calls them from two short workflows of its own; copy them from the kit's tested examples, `tests/store/callers/store-publish.yml` and `tests/store/callers/credential-expiry.yml`, and set the Store product ID, the price tier and any steps of its own.

- **The app's gate first (CI-4):** every run starts by running the app's `npm run validate` on Linux (`gate_command` changes the command), and nothing is built, uploaded or submitted unless it passes. The app needs no gate job of its own in its Store workflow.
- **Draft, then submit (REL-7):** a `v*` tag builds both architectures unsigned, bundles them into one Store upload, uploads it with the release notes from `release-notes/<version>.md`, and stops at a draft. A manual run on the same tag with `submit_draft` set to that draft's ID checks the draft against the tag and commits it. Manual runs can also repair a draft, or replace a canceled or failed submission.
- **Release notes per locale (REL-6, LOC-11):** each listing locale's "What's new" comes from its wording catalog's notes, `release-notes/<version>.<catalog>.md`, using the identities in `build/languages.json` (en-CA reads the en-US notes, fr-CA the fr-FR notes). A catalog with no notes file, and any locale outside that list, gets the English `<version>.md`. With `"listing": {"catalog_dir": null}` (an English-only listing), every locale gets the English notes. Every file used must be 1 to 1,500 characters; one that isn't stops the run before anything is built, and nothing is truncated.
- **Dry run:** a manual run with `dry_run` builds, bundles and checks everything, then stops before any Store call. It needs no secrets. The kit runs it on its own compile check whenever the publishing path changes.
- **Inputs:** `store_product_id` (required), `price_tier` (empty for a free app), `gate_command` (default `npm run validate`; it runs on a clean Linux runner with only .NET set up from `global.json`, so it must install anything else the app's `validate` needs, for example `npm ci --prefix web && npm run validate`), `pre_build` (the app's own steps before packaging; any command that fails, including a native command that exits non-zero before a later one succeeds, stops the run), `windows_build_json`, `notes_dir` and `kit_path`. Packaging settings come from `windows-build.json`.
- **A build-time secret:** an app whose build compiles in one secret (an OAuth client secret, say) passes it as `BUILD_SECRET` and names the environment variable its build reads with `build_secret_env_name`, for example `GoogleClientSecret`, which MSBuild reads as that property. It is set only for `pre_build` and the packaging step and never printed. Pass the secrets explicitly instead of `secrets: inherit`, so the app's own secret name maps onto `BUILD_SECRET` and the workflow gets only what it needs: the four Store secrets below plus `BUILD_SECRET: ${{ secrets.GOOGLE_CLIENT_SECRET }}`. A name without a secret, or a secret without a name, stops the run; with neither, nothing changes.
- **Holding a release (`hold_publication`):** with `submit_draft`, the draft is set to manual publication, so a certified release waits for someone to press Publish in Partner Center. It is committed only if the Store kept the hold and the draft's price tier is unchanged, and the hold is checked again after the commit. Running the same submit again after a commit only reports the status. Off by default. A caller may default it to on: dry runs, repairs, inspections and tag runs that stop at a draft ignore it, and only a run that would commit without `submit_draft` (no `no_commit`) stops, rather than going out without the hold.
- **Inspecting a submission (`inspect_draft`):** a manual run with a submission ID reports its status, pricing model, price tier, publishing mode and packages, and changes nothing. It skips the gate and builds nothing.
- **Secrets:** the organization's `AZURE_AD_TENANT_ID`, `AZURE_AD_APPLICATION_CLIENT_ID`, `AZURE_AD_APPLICATION_SECRET` and `SELLER_ID`, passed with `secrets: inherit`.
- **Credential reminder:** the second workflow fails 60 days before the Store secret expires. The caller passes the date from the organization's `STORE_SECRET_EXPIRES` variable (one date, because the apps share one secret; a repository variable of the same name would shadow it), set with `gh variable set STORE_SECRET_EXPIRES --org Dynamic-Solutions-Canada --body YYYY-MM-DD` whenever the secret is created or rotated.
- **Same version everywhere:** the workflow uses the Store helpers from the app's kit submodule (`scripts/store/`). Keep the `uses:` tag equal to the submodule pin; the workflow fails at its first step if they differ.
- **Paid apps:** packages are never uploaded as workflow artifacts, so a paid app's MSIX can't be downloaded from its public repo.

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
- **Apps with no host window:** an app whose only window is visible (not resident) calls `SingleInstance.RegisterWindow(WindowChrome.Hwnd(window))` once its window exists. A second launch calls `SingleInstance.BringRunningInstanceForward(mutexName)` after `TryClaim` returns false, then exits: it restores the first copy's window if minimized and brings it to the front, using the foreground right the user's launch carries. No window title is needed; the window is recorded in session-local shared memory under the mutex name.
- **Exit:** `Exit` runs every exit step in the order added (a failing step is logged and the rest still run), flushes the log, closes the open settings windows (those set up with `SetUpSettingsWindow`), then shuts WinUI down in order with `Application.Exit`. Put all teardown in exit steps (closing connections, unregistering notifications, removing the tray icon), including closing any other window the app keeps open; nothing goes after `Exit`, which now returns while the app shuts down. If the shutdown hasn't ended the process within 5 seconds, it is ended anyway and the log says so.
- **Session end:** when Windows shuts down, restarts or signs out (or Windows closes the app to update files it uses), the host runs the same exit steps before Windows ends the process, and logs it as a session end rather than an exit request. A cancelled shutdown does nothing. Set `ExitOnSessionEnd = false` only for an app whose steps must not run then; the process then ends with no exit steps, as it did before.
- **Confirmations:** `ConfirmDialog.ConfirmAsync(window, title, body, confirmText)`. Name the action on the confirm button; the question and that label come from the app's own catalogs, and Cancel is the kit's shared label unless the app passes `cancelText`.
- **Tray icon:** `TrayIconRasterizer.Render(TrayIcon.IconPixelSize, (g, side) => ...)` draws the app's artwork on a canvas `side` pixels square, using `Ink` and `Surface` for taskbar-aware colours. Re-render on `TrayIcon.RenderNeeded` and when the taskbar colour changes.
- **Tray anchor:** `Clicked` and `DoubleClicked` pass the icon's centre, or the cursor position when the shell can't report where the icon is; `GetAnchor()` returns the same. `TryGetAnchor(out anchor)` returns false instead of falling back, for an app that would rather place its window somewhere else than at the cursor.

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
| `OsdController` look | Desktop acrylic, rounded corners | A solid background in the window's theme (Fluent's base colour, or the system window colour in high contrast) and square corners; a background the app set on the root panel is kept |

Everything else behaves the same on both: the tray icon and its dark menu, flyout placement (including a taskbar on any screen edge), start with Windows, theme switching, window chrome, settings and localization.

## Gotchas

Apps link to this section rather than repeating it in their own docs.

- **Identity values never change once shipped.** The app passes its own tray icon GUID, single-instance mutex name, startup task id and Run value name. Windows keys the user's show/hide choice for the tray icon on the GUID, so a new GUID loses that choice.
- **After an app update, Windows 10 may refuse the tray icon's GUID.** Windows ties an icon GUID to the path of the program that first registered it, and every MSIX update installs to a new folder; only programs Authenticode-signed by the same company keep it. When the GUID is refused, `TrayIcon` adds the icon by host window and number instead for that session and logs a warning, so the icon still shows, but the user's show-or-hide choice for it starts over. Keep the GUID anyway: Windows 11 keeps it across updates (tested), and so do unchanged installs, so only Windows 10 falls back.
- **Pass the real tray tooltip on the first `Update`.** The icon is not added until then. Explorer keeps the first tooltip in the icon's screen-reader name, and later tooltip changes have been seen appended to it rather than replacing it, so a placeholder such as "Starting" can be read out for as long as the app runs. Work out the state first, or use the app's name alone.
- **Claim `SingleInstance` before loading settings,** so a second launch exits without touching the settings file End that second launch with `SingleInstance.ExitThisLaunch()` (after `SignalRunningInstance` or `BringRunningInstanceForward`), not `Environment.Exit`.
- **Exit steps must be quick.** They also run when Windows shuts down, restarts or signs out, on the UI thread inside the host's window procedure, and Windows waits only a few seconds before ending the process. Do no network calls, waits or UI in a step; save state, release resources and put things back. A step cut short there leaves the rest unrun. A cancelled shutdown runs nothing.
- **The host window never takes the foreground.** `AppBootstrap.Launch` shows it with `AppWindow.Show(activateWindow: false)`, not `Window.Activate()`: activating the off-screen host made it the foreground window at every start, which pulled focus from whatever the user was in at sign-in and closed overlays such as Xbox Game Bar when they started the app. Never activate the host; show the app's own windows when it needs focus.
- **Register every window the app keeps open with `HostWindow.CloseOnExit`.** WinUI's `Application.Exit` never finishes while a window it did not close is open, and it does not close windows that were created but never activated (a popup only ever shown without focus, say), so the process hangs until `HostWindow.Exit`'s 5-second backstop ends it. The kit registers its settings windows and the windows of `FlyoutController` and `OsdController` itself; an app registers the rest.
- **Never call `Environment.Exit` on the UI thread once WinUI has started.** On Windows App SDK 2 under Windows 10 it crashes in CoreMessaging (fail-fast 0xc0000602) on every exit. Use `HostWindow.Exit` for a resident app, `SingleInstance.ExitThisLaunch` for a second launch, or `Application.Current.Exit()` after closing the app's windows.
- **An app namespace ending in `.Windows` shadows WinRT `Windows.*`.** Write `global::Windows...` for WinRT types in that app.
- **Never hide a flyout window yourself.** `FlyoutController` closes it by cloaking it and parking it off-screen, so WinUI keeps its composition surfaces and the next open is instant. It reveals the window only after it has rendered, and ignores a toggle within 300 ms of a dismissal (the tray click that closed it).
- **Drive an on-screen display only through `OsdController`.** Set the content, then call `Show(hold)`; `Hide()` fades it early. Call `Reposition()` when displays change (`WM_DISPLAYCHANGE`), since Windows pulls off-screen windows back into view. Never activate, show or hide its window yourself: it is shown without activation and parked off-screen, cloaked, between uses, so it never takes focus from a game and appears instantly.
- **Paths handed to another process** (Explorer, a file picker) use `AppEnvironment.PhysicalDataDirectory`. When packaged, `%AppData%` writes are redirected into the package, and other processes do not see them at the path the app uses.
- **Settings serialization:** `JsonSettingsStore<T>.DefaultOptions` omits nulls but writes defaults, because an omitted `false` for a bool that defaults to `true` would load back as `true`. Options an app passes in must keep that.
- **Start with Windows never overrides the user or a policy.** When `StartupStatus` says the user (in Windows Settings) or a policy decided, the app explains that instead of flipping its switch back. Packaged builds use the manifest startup task; unpackaged builds use the HKCU Run key with a silent-launch argument.
- **Portable copies move.** Each download of a portable build unzips to its own folder, leaving the Run entry aimed at the old copy. Call `StartupRegistration.RepointRunEntry()` once at startup in builds the app treats as portable. It rewrites only an entry that already exists, under the same name, so it never turns startup on and never undoes a choice the user made in Task Manager or Windows Settings. It does nothing when packaged.
- **Interface text:** use `UiText.Get` and `UiText.Format` (placeholders as `{{name}}`) in code, and the `Localize` attached properties (`xmlns:l10n="using:WinUIAppKit.Localization"`, then `l10n:Localize.Text`, `Content`, `Header`, `ToolTip` and the others) in XAML. Store a key or a `LocalizedMessage` on models and services, not finished text, so a language change relabels it. `Localize` relabels XAML live; anything an app builds in code must also refresh on `UiText.Changed`.
- **Buttons with an icon and a label (`IconLabelStyle`) need `l10n:Localize.Name`** set to the label's key. Their content is a panel, so the button's accessible name is empty otherwise (UI-14). The kit's own buttons do this.
- **`Localize` targets:** headers and placeholders work on `TextBox`, `PasswordBox`, `NumberBox`, `AutoSuggestBox` and `ComboBox` (headers also on `ToggleSwitch`). A `Localize` property on a control without it is logged as a warning, once per control type, instead of doing nothing silently.
- **Do not set `ApplicationLanguages.PrimaryLanguageOverride`.** When packaged, `UiText` resolves "Match Windows" from the app's language list, which an override replaces. Pass the user's choice to `UiText.Configure` and `UiText.SetOverride` instead. A consequence: WinUI's own built-in control strings (a text box's context menu, "Select All" and the like) follow Windows' language, not the language chosen in the app.

## Versioning

Each release is an annotated `vX.Y.Z` tag with a GitHub Release whose notes say what changed for apps. Versions follow SemVer:

- **Major:** anything that can break an app that builds against the kit: removing or renaming a public member, changing behaviour listed under "Gotchas", moving to a new Windows App SDK line (a new runtime package name, such as `Microsoft.WindowsAppRuntime.3`) or a new target framework, or a `.editorconfig` or analyzer change that turns code that built cleanly into an error.
- **Minor:** new types, members, options or events, with existing behaviour unchanged. A Windows App SDK update within its line is also a minor, even though it raises the runtime `MinVersion` each app's manifest needs: the release notes name the new `MinVersion` first, and the packaging script refuses to pack an app whose manifest still has a lower one, with a message saying what to set.
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
