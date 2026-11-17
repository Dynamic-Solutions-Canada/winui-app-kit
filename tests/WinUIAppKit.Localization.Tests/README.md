# WinUIAppKit.Localization.Tests

Portable checks for the interface text core. The project compiles in `WinUIAppKit/Localization/TextCatalogs.cs` and `SupportedLanguages.cs`, the dependency-free files a Native AOT companion links, so it runs on Linux. `npm test` runs it with every other harness under `tests/`.

## What it covers

- **Catalogs:**
  - area files (`<catalog>/<area>.json`, embedded with the folder in the name) merge into one catalog;
  - a key in two areas keeps the first and is reported with both file names;
  - a catalog with no files, and an unreadable file, are reported and read empty;
  - a single `<catalog>.json` still works, and a catalog is read once.
- **Lookup:** a regional identity reads its catalog (fr-CA reads fr-FR), a missing key falls back to en-US and then to the key (and is reported), `Source` reads the default language, and the language is read on every lookup.
- **Placeholders:** filled once, so a value containing braces is never expanded; an unknown placeholder stays.
- **Plurals:** the rule for every family language, and `Plural` picking `.one` or `.other` and filling `{{count}}` with other values.
- **An app's real catalogs:** run with a folder argument (`dotnet run --project tests/WinUIAppKit.Localization.Tests -- <Localization folder>`) to check that every `<catalog>/<area>.json` set merges with no duplicate, every catalog has en-US's keys, and every plural key has both forms.

## What it does not prove

- **`UiText` itself:** its Windows language preferences, logging and the kit's shared labels need WinRT and NLog; `check:l10n` and the Windows compile cover its keys and build.
- **Embedding in an app:** that the app's `EmbeddedResource` names match its prefix; an app's own test should read one key through `UiText`.
