using System.Globalization;
using System.Reflection;
using WinUIAppKit.Localization;

// Portable checks for the interface text core: merging area files, lookup and fallback, placeholders and
// plurals. With a folder argument (an app's Localization folder of <catalog>/<area>.json files) it also
// checks that app's real catalogs: each merges cleanly, matches en-US, and has both plural forms.
// Exits non-zero when any check fails.

var failures = new List<string>();
int passed = 0;

void Check(bool condition, string description)
{
    if (condition)
    {
        passed++;
    }
    else
    {
        failures.Add(description);
    }
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Assembly fixtures = typeof(Program).Assembly;
var family = new SupportedLanguages("en-US",
[
    ("en-US", "en-US", "English (United States)"),
    ("en-CA", "en-US", "English (Canada)"),
    ("fr-FR", "fr-FR", "Français (France)"),
    ("fr-CA", "fr-FR", "Français (Canada)"),
    ("ja-JP", "ja-JP", "日本語"),
    ("de-DE", "de-DE", "Deutsch"),
    ("pt-BR", "pt-BR", "Português (Brasil)"),
]);

// ── Area files merge into one catalog ───────────────────────────────────

var errors = new List<(string Catalog, Exception Error)>();
var areas = new TextCatalogs(fixtures, "Fixture.Areas.", (catalog, ex) => errors.Add((catalog, ex)));
var english = areas.Catalog("en-US");
Check(english.Count == 7, "areas: every area file of a catalog merges into one");
Check(english["pinning.pin"] == "Pin {{name}}" && english["common.save"] == "Save", "areas: keys from each area are found");
Check(errors.Count == 0, "areas: a clean catalog reports nothing");

var japanese = areas.Catalog("ja-JP");
Check(japanese["common.save"] == "保存", "duplicates: the first area's definition is kept (common before pinning)");
Check(errors.Count == 1 && errors[0].Error.Message.Contains("common.save", StringComparison.Ordinal)
      && errors[0].Error.Message.Contains("common.json", StringComparison.Ordinal)
      && errors[0].Error.Message.Contains("pinning.json", StringComparison.Ordinal), "duplicates: reported with the key and both files");

errors.Clear();
Check(areas.Catalog("zh-CN").Count == 0 && errors.Count == 1 && errors[0].Error is FileNotFoundException, "missing: a catalog with no files reads empty and is reported");
Check(ReferenceEquals(areas.Catalog("en-US"), english), "cache: a catalog is read once");

// ── One file per catalog, as before ─────────────────────────────────────

errors.Clear();
var flat = new TextCatalogs(fixtures, "Fixture.Flat.", (catalog, ex) => errors.Add((catalog, ex)));
Check(flat.Catalog("fr-FR")["hello"] == "Bonjour", "flat: a single <catalog>.json still works");
Check(flat.Catalog("de-DE").Count == 0 && errors.Count == 1 && errors[0].Error is InvalidDataException, "flat: an unreadable file is reported and reads empty");

// ── Lookup and fallback ─────────────────────────────────────────────────

string locale = "fr-CA";
var missing = new List<string>();
var text = new TextLookup(areas, family, () => locale, missing.Add);
Check(text.Get("common.save") == "Enregistrer", "lookup: fr-CA reads the fr-FR catalog");
Check(text.Get("common.only-english") == "Only in English", "lookup: a key the translation lacks falls back to en-US");
Check(text.Get("no.such.key") == "no.such.key" && missing.SequenceEqual(["no.such.key"]), "lookup: an unknown key returns itself and is reported");
Check(text.Source("common.save") == "Save", "lookup: Source is the default-language text");
locale = "en-CA";
Check(text.Get("common.save") == "Save", "lookup: the locale is read on every lookup");

// ── Placeholders ────────────────────────────────────────────────────────

locale = "fr-FR";
Check(text.Format("pinning.pin", ("name", "Notes")) == "Épingler Notes", "format: a placeholder is filled");
Check(text.Format("pinning.pin", ("name", "{{name}}")) == "Épingler {{name}}", "format: a value with braces is inserted literally, not expanded");
Check(TextFormat.Fill("{{a}} {{ b }} {{c}}", ("a", 1), ("b", 2)) == "1 2 {{c}}", "format: spaces inside braces are allowed, an unknown placeholder stays");

// ── Plurals ─────────────────────────────────────────────────────────────

Check(PluralRules.Category("en-US", 1) == "one" && PluralRules.Category("en-US", 0) == "other" && PluralRules.Category("en-US", 2) == "other", "plural: English uses one only for 1");
Check(PluralRules.Category("de-DE", 1) == "one" && PluralRules.Category("es-ES", 0) == "other", "plural: German and Spanish follow English");
Check(PluralRules.Category("fr-CA", 0) == "one" && PluralRules.Category("fr-FR", 1) == "one" && PluralRules.Category("fr-FR", 2) == "other", "plural: French uses one for 0 and 1");
Check(PluralRules.Category("pt-BR", 0) == "one" && PluralRules.Category("pt-BR", 2) == "other", "plural: Portuguese uses one for 0 and 1");
Check(PluralRules.Category("ja-JP", 1) == "other" && PluralRules.Category("zh-CN", 1) == "other", "plural: Chinese and Japanese never inflect");

Check(text.Plural("common.files", 0) == "0 fichier" && text.Plural("common.files", 3) == "3 fichiers", "plural: French text picks its form and fills count");
locale = "en-US";
Check(text.Plural("common.files", 1) == "1 file" && text.Plural("common.files", 0) == "0 files", "plural: English text picks its form");
Check(text.Plural("pinning.left", 2, ("folder", "Work")) == "2 pins left in Work", "plural: other values are filled with count");
locale = "ja-JP";
Check(text.Plural("common.files", 1) == "1 個のファイル", "plural: Japanese reads the other form");

// ── An app's real catalogs (optional folder argument) ───────────────────

if (args.Length > 0)
{
    string root = Path.GetFullPath(args[0]);
    var appErrors = new List<string>();
    var app = new TextCatalogs(
        () => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(file => ("App." + Path.GetRelativePath(root, file).Replace('\\', '/'), (Func<Stream?>)(() => File.OpenRead(file)))),
        "App.",
        (catalog, ex) => appErrors.Add($"{catalog}: {ex.Message}"));

    string[] catalogs = Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
    var source = app.Catalog("en-US");
    foreach (string catalog in catalogs)
    {
        var merged = app.Catalog(catalog);
        Check(merged.Keys.ToHashSet().SetEquals(source.Keys), $"app {catalog}: the same keys as en-US");
    }

    Check(appErrors.Count == 0, "app: every catalog merges with no duplicate or unreadable file" + (appErrors.Count > 0 ? " (" + string.Join("; ", appErrors) + ")" : ""));

    var plurals = source.Keys.Where(key => key.EndsWith(".one", StringComparison.Ordinal) || key.EndsWith(".other", StringComparison.Ordinal))
        .Select(key => key[..key.LastIndexOf('.')]).Distinct().ToArray();
    foreach (string stem in plurals)
    {
        Check(source.ContainsKey(stem + ".one") && source.ContainsKey(stem + ".other"), $"app: {stem} has both plural forms");
    }

    Console.WriteLine($"app: {catalogs.Length} catalogs, {source.Count} keys, {plurals.Length} plural keys");
}

// ── Report ──────────────────────────────────────────────────────────────

foreach (string failure in failures)
{
    Console.WriteLine("FAIL: " + failure);
}

Console.WriteLine($"{passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;
