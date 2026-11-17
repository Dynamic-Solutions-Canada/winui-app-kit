using System.Globalization;
using System.Reflection;

namespace WinUIAppKit.Localization;

/// <summary>
/// The app's interface wording: JSON catalogs (key to text) embedded in the app assembly, one per
/// catalog name in <see cref="SupportedLanguages"/>, each either one file or several area files merged
/// at load (see <see cref="TextCatalogs"/>), chosen from the user's override or Windows' language list.
/// Missing keys fall back to the default catalog, then to the key itself, so a gap in a translation
/// shows English rather than breaking the app.
/// <para>
/// The lookup itself is the dependency-free <see cref="TextLookup"/>, which a process without WinRT or
/// NLog (a Native AOT companion, a test harness) can use directly; this class adds Windows' language
/// preferences, logging and the kit's shared labels.
/// </para>
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
public static class UiText
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>The kit's own catalogs, holding only the shared labels.</summary>
    private static readonly TextCatalogs KitCatalogs = new(
        typeof(UiText).Assembly, "WinUIAppKit.Catalogs.",
        (catalog, ex) => Logger.Warn(ex, "Kit language catalog {Catalog}: {Problem}", catalog, ex.Message));

    /// <summary>The prefix of the shared labels the kit owns and translates.</summary>
    public const string KitKeyPrefix = "kit.";

    /// <summary>The catalog the kit falls back to for a shared label, and the source wording.</summary>
    private const string KitDefaultCatalog = "en-US";

    private static SupportedLanguages? _languages;
    private static TextLookup? _lookup;

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
    /// Manifest resource name prefix; the catalog for "fr-FR" is read from <c>{prefix}fr-FR.json</c>, or
    /// merged from every <c>{prefix}fr-FR/{area}.json</c>.
    /// </param>
    /// <param name="languages">The app's supported languages.</param>
    /// <param name="languageOverride">The saved choice, or null to follow Windows. Unknown values are ignored.</param>
    public static void Configure(Assembly catalogAssembly, string resourcePrefix, SupportedLanguages languages, string? languageOverride)
    {
        _languages = languages;
        var catalogs = new TextCatalogs(
            catalogAssembly, resourcePrefix,
            (catalog, ex) => Logger.Warn(ex, "Language catalog {Catalog}: {Problem}", catalog, ex.Message));
        _lookup = new TextLookup(catalogs, languages, () => Locale, key => Logger.Warn("Missing interface text {Key}", key));
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

        return _lookup?.Get(key) ?? key;
    }

    /// <summary>The default-language (source) text for <paramref name="key"/>, for matching fixed English copy.</summary>
    public static string Source(string key)
    {
        if (key.StartsWith(KitKeyPrefix, StringComparison.Ordinal))
        {
            return KitCatalogs.Catalog(KitDefaultCatalog).GetValueOrDefault(key) ?? key;
        }

        return _lookup?.Source(key) ?? key;
    }

    /// <summary>
    /// The text for <paramref name="key"/> with each <c>{{name}}</c> placeholder replaced by its value.
    /// Values are inserted once, so a value containing braces is never treated as a placeholder.
    /// Numbers use the device's regional formatting.
    /// </summary>
    public static string Format(string key, params (string Name, object? Value)[] values) => TextFormat.Fill(Get(key), values);

    /// <summary>
    /// Count-dependent text: <c>{key}.one</c> or <c>{key}.other</c> by the current language's plural rule
    /// (<see cref="PluralRules"/>), with <c>{{count}}</c> filled along with any other values. Define both
    /// forms in every catalog; Chinese and Japanese give both the same text.
    /// </summary>
    public static string Plural(string key, long count, params (string Name, object? Value)[] values) =>
        TextFormat.Fill(Get(PluralRules.KeyFor(key, Locale, count)), TextFormat.WithCount(count, values));

    /// <summary>The identity Windows' preferences resolve to, which "Use system setting" would use.</summary>
    public static string DeviceLocale => _languages is null ? "" : Languages.Resolve(PreferredLanguages());

    /// <summary>
    /// The merged catalog <paramref name="identity"/> reads, for completeness checks and for searching
    /// another language's wording. Empty before <see cref="Configure"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Catalog(string identity) =>
        _lookup?.Catalog(identity) ?? new Dictionary<string, string>();

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
        if (KitCatalogs.Catalog(catalog).TryGetValue(key, out string? text)
            || KitCatalogs.Catalog(KitDefaultCatalog).TryGetValue(key, out text))
        {
            return text;
        }

        Logger.Warn("Missing kit interface text {Key}", key);
        return key;
    }
}
