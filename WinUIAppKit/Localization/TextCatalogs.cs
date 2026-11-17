using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinUIAppKit.Localization;

// The dependency-free core of the kit's interface text: catalog loading, lookup, placeholder filling
// and plurals. Self-contained (no NLog, WinRT, AppEnvironment or other kit types), so a Native AOT
// companion process and the portable test harnesses can link this file with SupportedLanguages.cs.
// UiText wraps it with Windows' language preferences, logging and the kit's shared labels.

/// <summary>
/// An app's text catalogs, read from embedded JSON resources and cached. A catalog is either one file,
/// <c>{prefix}{catalog}.json</c>, or several area files, <c>{prefix}{catalog}/{area}.json</c> (MSBuild
/// may write the folder separator as <c>/</c>, <c>\</c> or <c>.</c>), merged into one flat key space.
/// A key defined in two areas is reported and the first definition kept; check:l10n fails on it first.
/// A catalog with no files, or an unreadable file, is reported; a bad file is skipped, so one broken
/// area never blanks the rest.
/// </summary>
public sealed class TextCatalogs
{
    private readonly Func<IEnumerable<(string Name, Func<Stream?> Open)>> _resources;
    private readonly string _prefix;
    private readonly Action<string, Exception>? _onError;
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.Ordinal);

    /// <summary>Catalogs embedded in <paramref name="assembly"/> under <paramref name="resourcePrefix"/>.</summary>
    /// <param name="assembly">The assembly holding the catalogs as embedded resources.</param>
    /// <param name="resourcePrefix">The manifest name prefix, for example <c>MyApp.Catalogs.</c>.</param>
    /// <param name="onError">Told about a duplicate key or an unreadable file (the catalog name and the error); null ignores them.</param>
    public TextCatalogs(Assembly assembly, string resourcePrefix, Action<string, Exception>? onError = null)
        : this(() => assembly.GetManifestResourceNames().Select(name => (name, (Func<Stream?>)(() => assembly.GetManifestResourceStream(name)))),
               resourcePrefix, onError)
    {
    }

    /// <summary>
    /// Catalogs from any named source, for tests and tools that read files from disk. Names follow the
    /// same <c>{prefix}{catalog}.json</c> or <c>{prefix}{catalog}/{area}.json</c> shape.
    /// </summary>
    public TextCatalogs(Func<IEnumerable<(string Name, Func<Stream?> Open)>> resources, string resourcePrefix, Action<string, Exception>? onError = null)
    {
        _resources = resources;
        _prefix = Normalize(resourcePrefix);
        _onError = onError;
    }

    /// <summary>The merged text of one catalog (for example <c>fr-FR</c>), empty when it has no files.</summary>
    public IReadOnlyDictionary<string, string> Catalog(string catalog)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(catalog, out var merged))
            {
                merged = Load(catalog);
                _cache.Add(catalog, merged);
            }

            return merged;
        }
    }

    /// <summary>Read and merge every file of <paramref name="catalog"/>, area files in name order.</summary>
    private Dictionary<string, string> Load(string catalog)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var origin = new Dictionary<string, string>(StringComparer.Ordinal);
        string start = _prefix + catalog + ".";

        var files = _resources()
            .Where(resource => Normalize(resource.Name) is { } name
                && name.StartsWith(start, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(resource => resource.Name, StringComparer.Ordinal);

        var found = files.ToList();
        if (found.Count == 0)
        {
            _onError?.Invoke(catalog, new FileNotFoundException($"No text catalog files for {catalog} under {_prefix}"));
        }

        foreach (var (name, open) in found)
        {
            Dictionary<string, string>? entries;
            try
            {
                using Stream? stream = open();
                if (stream is null)
                {
                    continue;
                }

                entries = JsonSerializer.Deserialize(stream, TextCatalogJsonContext.Default.DictionaryStringString);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _onError?.Invoke(catalog, new InvalidDataException($"Unreadable text catalog file {name}", ex));
                continue;
            }

            foreach (var (key, value) in entries ?? [])
            {
                if (merged.TryAdd(key, value))
                {
                    origin[key] = name;
                }
                else
                {
                    _onError?.Invoke(catalog, new InvalidDataException($"Key {key} is defined in both {origin[key]} and {name}; the first is used"));
                }
            }
        }

        return merged;
    }

    /// <summary>Resource names compared with every folder separator written as a dot.</summary>
    private static string Normalize(string name) => name.Replace('\\', '.').Replace('/', '.');
}

/// <summary>
/// Looks up text in the language in use: that language's catalog, then the default catalog, then the key
/// itself, so a gap shows the source wording rather than breaking the screen.
/// </summary>
public sealed class TextLookup
{
    private readonly TextCatalogs _catalogs;
    private readonly SupportedLanguages _languages;
    private readonly Func<string> _locale;
    private readonly Action<string>? _onMissing;

    /// <param name="catalogs">The catalogs to read.</param>
    /// <param name="languages">The supported identities and the catalog each reads.</param>
    /// <param name="locale">The identity in use, read on every lookup (for example the saved choice, or the device's language).</param>
    /// <param name="onMissing">Told the key when no catalog has it; null ignores it.</param>
    public TextLookup(TextCatalogs catalogs, SupportedLanguages languages, Func<string> locale, Action<string>? onMissing = null)
    {
        _catalogs = catalogs;
        _languages = languages;
        _locale = locale;
        _onMissing = onMissing;
    }

    /// <summary>The text for <paramref name="key"/> in the language in use.</summary>
    public string Get(string key)
    {
        if (_catalogs.Catalog(_languages.CatalogFor(_locale())).TryGetValue(key, out string? text)
            || _catalogs.Catalog(_languages.CatalogFor(_languages.Default)).TryGetValue(key, out text))
        {
            return text;
        }

        _onMissing?.Invoke(key);
        return key;
    }

    /// <summary>The default-language (source) text for <paramref name="key"/>, or the key.</summary>
    public string Source(string key) =>
        _catalogs.Catalog(_languages.CatalogFor(_languages.Default)).GetValueOrDefault(key) ?? key;

    /// <summary>The text for <paramref name="key"/> with its <c>{{name}}</c> placeholders filled (see <see cref="TextFormat.Fill"/>).</summary>
    public string Format(string key, params (string Name, object? Value)[] values) => TextFormat.Fill(Get(key), values);

    /// <summary>
    /// Count-dependent text: <c>{key}.one</c> or <c>{key}.other</c> by the language's plural rule
    /// (<see cref="PluralRules"/>), with <c>{{count}}</c> filled along with any other values.
    /// </summary>
    public string Plural(string key, long count, params (string Name, object? Value)[] values) =>
        TextFormat.Fill(Get(PluralRules.KeyFor(key, _locale(), count)), TextFormat.WithCount(count, values));

    /// <summary>The merged catalog read by <paramref name="identity"/>, for completeness checks and searches.</summary>
    public IReadOnlyDictionary<string, string> Catalog(string identity) => _catalogs.Catalog(_languages.CatalogFor(identity));
}

/// <summary>
/// The two plural forms the family's languages need (CLDR "one" and "other"): English, Spanish and German
/// use "one" only for 1; French and Portuguese also for 0; Chinese and Japanese never inflect, so their
/// catalogs give both forms the same text. A new language with other forms needs a rule here first.
/// </summary>
public static class PluralRules
{
    /// <summary>"one" or "other" for <paramref name="count"/> in <paramref name="locale"/> (an identity such as "fr-CA").</summary>
    public static string Category(string locale, long count)
    {
        string language = locale.Split('-')[0].ToLowerInvariant();
        bool one = language switch
        {
            "fr" or "pt" => count is 0 or 1,
            "zh" or "ja" => false,
            _ => count == 1,
        };
        return one ? "one" : "other";
    }

    /// <summary>The catalog key for <paramref name="key"/>'s form: <c>{key}.one</c> or <c>{key}.other</c>.</summary>
    public static string KeyFor(string key, string locale, long count) => key + "." + Category(locale, count);
}

/// <summary>Placeholder filling shared by every lookup.</summary>
public static partial class TextFormat
{
    /// <summary>
    /// Replace each <c>{{name}}</c> in <paramref name="template"/> with its value, in one pass, so a value
    /// containing braces is inserted literally and never expanded. Values use the device's regional
    /// formatting. An unknown placeholder is left as written.
    /// </summary>
    public static string Fill(string template, params (string Name, object? Value)[] values)
    {
        if (values.Length == 0)
        {
            return template;
        }

        return PlaceholderPattern().Replace(template, match =>
        {
            string name = match.Groups[1].Value;
            foreach (var (candidate, value) in values)
            {
                if (candidate == name)
                {
                    return Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
                }
            }

            return match.Value;
        });
    }

    /// <summary><paramref name="values"/> with <c>count</c> first, for plural text.</summary>
    internal static (string Name, object? Value)[] WithCount(long count, (string Name, object? Value)[] values)
    {
        var all = new (string Name, object? Value)[values.Length + 1];
        all[0] = ("count", count);
        values.CopyTo(all, 1);
        return all;
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}

/// <summary>Source-generated reader for catalog files, so Native AOT needs no reflection.</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class TextCatalogJsonContext : JsonSerializerContext;
