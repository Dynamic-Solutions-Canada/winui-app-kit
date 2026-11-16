using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinUIAppKit.Localization;

/// <summary>
/// The app's interface wording: flat JSON catalogs (key to text) embedded in the app assembly, one per
/// catalog name in <see cref="SupportedLanguages"/>, chosen from the user's override or Windows'
/// language list. Missing keys fall back to the default catalog, then to the key itself, so a gap in a
/// translation shows English rather than breaking the app.
/// <para>
/// Look up only app-owned keys. Text from users, files or other programs is passed as a
/// <see cref="Format"/> value, never as a key.
/// </para>
/// <para>
/// Keys starting with <see cref="KitKeyPrefix"/> are the shared labels the kit owns (LOC-7, D-072). They
/// are read from the kit's own catalogs, never from the app's, so a shared label reads the same in
/// every app; an app does not define <c>kit.</c> keys.
/// </para>
/// </summary>
public static partial class UiText
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<string, string>> KitCatalogs = new(StringComparer.Ordinal);

    /// <summary>The prefix of the shared labels the kit owns and translates.</summary>
    public const string KitKeyPrefix = "kit.";

    /// <summary>The catalog the kit falls back to for a shared label, and the source wording.</summary>
    private const string KitDefaultCatalog = "en-US";

    private static Assembly? _assembly;
    private static string _resourcePrefix = "";
    private static SupportedLanguages? _languages;

    /// <summary>The supported languages passed to <see cref="Configure"/>.</summary>
    public static SupportedLanguages Languages =>
        _languages ?? throw new InvalidOperationException("UiText.Configure has not been called.");

    /// <summary>The user's explicit choice, or null to follow Windows.</summary>
    public static string? Override { get; private set; }

    /// <summary>The identity in use (for example "fr-CA").</summary>
    public static string Locale { get; private set; } = "";

    /// <summary>Raised after the language in use changes. <see cref="SetOverride"/> raises it on the caller's (UI) thread.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Set up lookups. Call once at startup, before any window is created.
    /// </summary>
    /// <param name="catalogAssembly">Assembly holding the catalogs as embedded resources.</param>
    /// <param name="resourcePrefix">
    /// Manifest resource name prefix; the catalog for "fr-FR" is read from <c>{prefix}fr-FR.json</c>.
    /// </param>
    /// <param name="languages">The app's supported languages.</param>
    /// <param name="languageOverride">The saved choice, or null to follow Windows. Unknown values are ignored.</param>
    public static void Configure(Assembly catalogAssembly, string resourcePrefix, SupportedLanguages languages, string? languageOverride)
    {
        _assembly = catalogAssembly;
        _resourcePrefix = resourcePrefix;
        _languages = languages;
        Override = languages.NormalizeOverride(languageOverride);
        Locale = Resolve();
        Logger.Info("Interface language {Locale} (override {Override}, Windows {Preferences})",
            Locale, Override ?? "none", string.Join(",", PreferredLanguages()));
    }

    /// <summary>
    /// Change the user's choice (null follows Windows) and raise <see cref="Changed"/> when the
    /// language in use changes. Persisting the choice is the caller's job. Call on the UI thread.
    /// </summary>
    public static void SetOverride(string? language)
    {
        Override = Languages.NormalizeOverride(language);
        string resolved = Resolve();
        if (resolved == Locale)
        {
            return;
        }

        Locale = resolved;
        Logger.Info("Interface language changed to {Locale}", Locale);
        Changed?.Invoke();
    }

    /// <summary>The text for <paramref name="key"/> in the current language.</summary>
    public static string Get(string key)
    {
        if (key.StartsWith(KitKeyPrefix, StringComparison.Ordinal))
        {
            return GetKitText(key);
        }

        if (_languages is null)
        {
            return key;
        }

        if (ReadCatalog(Languages.CatalogFor(Locale)).TryGetValue(key, out string? text))
        {
            return text;
        }

        if (ReadCatalog(Languages.CatalogFor(Languages.Default)).TryGetValue(key, out text))
        {
            return text;
        }

        Logger.Warn("Missing interface text {Key}", key);
        return key;
    }

    /// <summary>The default-language (source) text for <paramref name="key"/>, for matching fixed English copy.</summary>
    public static string Source(string key)
    {
        if (key.StartsWith(KitKeyPrefix, StringComparison.Ordinal))
        {
            return ReadKitCatalog(KitDefaultCatalog).GetValueOrDefault(key) ?? key;
        }

        if (_languages is null)
        {
            return key;
        }

        return ReadCatalog(Languages.CatalogFor(Languages.Default)).GetValueOrDefault(key) ?? key;
    }

    /// <summary>
    /// The text for <paramref name="key"/> with each <c>{{name}}</c> placeholder replaced by its value.
    /// Values are inserted once, so a value containing braces is never treated as a placeholder.
    /// Numbers use the device's regional formatting.
    /// </summary>
    public static string Format(string key, params (string Name, object? Value)[] values)
    {
        var lookup = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return PlaceholderPattern().Replace(Get(key), match =>
            lookup.TryGetValue(match.Groups[1].Value, out var value)
                ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""
                : match.Value);
    }

    /// <summary>
    /// Windows' ordered language preferences for this app (its own language list when packaged,
    /// which lists only languages the package declares, else the UI culture).
    /// </summary>
    public static IReadOnlyList<string> PreferredLanguages()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                return global::Windows.Globalization.ApplicationLanguages.Languages.ToArray();
            }

            return global::Windows.System.UserProfile.GlobalizationPreferences.Languages.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Couldn't read Windows language preferences");
            return [CultureInfo.CurrentUICulture.Name];
        }
    }

    private static string Resolve() => Override ?? Languages.Resolve(PreferredLanguages());

    /// <summary>
    /// A shared label from the kit's catalogs: the current language's catalog, then the kit's en-US.
    /// An app language the kit has no catalog for reads the kit's en-US.
    /// </summary>
    private static string GetKitText(string key)
    {
        string catalog = _languages is null ? KitDefaultCatalog : Languages.CatalogFor(Locale);
        if (ReadKitCatalog(catalog).TryGetValue(key, out string? text)
            || ReadKitCatalog(KitDefaultCatalog).TryGetValue(key, out text))
        {
            return text;
        }

        Logger.Warn("Missing kit interface text {Key}", key);
        return key;
    }

    /// <summary>The kit's embedded catalogs, which hold only the shared labels. A catalog the kit lacks reads as empty.</summary>
    private static Dictionary<string, string> ReadKitCatalog(string catalog)
    {
        lock (KitCatalogs)
        {
            if (KitCatalogs.TryGetValue(catalog, out var cached))
            {
                return cached;
            }

            Dictionary<string, string> entries = [];
            try
            {
                using Stream? stream = typeof(UiText).Assembly.GetManifestResourceStream($"WinUIAppKit.Catalogs.{catalog}.json");
                if (stream is not null)
                {
                    entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Kit language catalog {Catalog} unavailable", catalog);
            }

            KitCatalogs.Add(catalog, entries);
            return entries;
        }
    }

    /// <summary>Load (once) and cache a catalog. A missing or corrupt catalog reads as empty and is logged.</summary>
    private static Dictionary<string, string> ReadCatalog(string catalog)
    {
        lock (Catalogs)
        {
            if (Catalogs.TryGetValue(catalog, out var cached))
            {
                return cached;
            }

            Dictionary<string, string> entries;
            try
            {
                string name = _resourcePrefix + catalog + ".json";
                using Stream stream = _assembly?.GetManifestResourceStream(name)
                    ?? throw new InvalidDataException($"Missing embedded catalog {name}");
                entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Language catalog {Catalog} unavailable", catalog);
                entries = [];
            }

            Catalogs.Add(catalog, entries);
            return entries;
        }
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}
