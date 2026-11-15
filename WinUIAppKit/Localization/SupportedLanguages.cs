namespace WinUIAppKit.Localization;

/// <summary>
/// The interface languages an app ships, and how a list of Windows language preferences maps onto
/// them. Wording identity only: regional formatting (dates, numbers) is left to the device culture.
/// <para>
/// Several identities may share one catalog (for example en-CA reusing en-US wording) while staying
/// separate choices in a language picker.
/// </para>
/// </summary>
public sealed class SupportedLanguages
{
    private readonly Dictionary<string, string> _catalogs;
    private readonly Dictionary<string, string> _names;

    /// <param name="defaultLanguage">Identity used when no preference matches; must be one of <paramref name="languages"/>.</param>
    /// <param name="languages">
    /// Supported identities in picker order: (identity such as "fr-CA", catalog it reads such as "fr-FR",
    /// name shown to users in that language such as "Français (Canada)").
    /// </param>
    public SupportedLanguages(string defaultLanguage, IEnumerable<(string Identity, string Catalog, string NativeName)> languages)
    {
        _catalogs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var (identity, catalog, name) in languages)
        {
            _catalogs.Add(identity, catalog);
            _names.Add(identity, name);
            order.Add(identity);
        }

        if (!_catalogs.ContainsKey(defaultLanguage))
            throw new ArgumentException("The default language must be one of the supported languages.", nameof(defaultLanguage));

        Default = defaultLanguage;
        Identities = order;
    }

    /// <summary>Fallback identity, and the catalog every other catalog falls back to per key.</summary>
    public string Default { get; }

    /// <summary>Supported identities in picker order.</summary>
    public IReadOnlyList<string> Identities { get; }

    /// <summary>The catalog an identity reads its wording from.</summary>
    public string CatalogFor(string identity) => _catalogs[identity];

    /// <summary>The identity's own name for itself, for language pickers.</summary>
    public string NativeName(string identity) => _names[identity];

    /// <summary>Returns the canonical spelling of a supported identity, or null for automatic/unknown values.</summary>
    public string? NormalizeOverride(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;

        string tag = language.Trim();
        return Identities.FirstOrDefault(supported => supported.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Chooses the first preference that has a supported identity: an exact match first, then a
    /// regional relative in the same language and script (fr-BE selects fr-FR). Scripts are never
    /// crossed, so Traditional Chinese does not select Simplified Chinese. Falls back to
    /// <see cref="Default"/>.
    /// </summary>
    /// <param name="preferences">Ordered BCP-47 tags, most preferred first (Windows' language list).</param>
    public string Resolve(IEnumerable<string> preferences)
    {
        foreach (string preference in preferences)
        {
            if (string.IsNullOrWhiteSpace(preference)) continue;

            // Unicode extensions (-u-ca-..., -x-...) describe formatting, not wording.
            string[] parts = preference.Trim().Split('-');
            int extension = Array.FindIndex(parts, 1, part => part.Length == 1);
            string baseTag = extension < 0 ? preference.Trim() : string.Join("-", parts.Take(extension));
            string tag = DropDefaultLatinScript(baseTag);

            foreach (string supported in Identities)
            {
                if (string.Equals(tag, supported, StringComparison.OrdinalIgnoreCase)) return supported;
            }

            string family = LanguageFamily(tag);
            if (family.Length == 0) continue;
            foreach (string supported in Identities)
            {
                if (string.Equals(family, LanguageFamily(supported), StringComparison.OrdinalIgnoreCase)) return supported;
            }
        }

        return Default;
    }

    /// <summary>Removes an explicit "Latn" script (en-Latn-US) so it matches the plain regional identity.</summary>
    private static string DropDefaultLatinScript(string tag)
    {
        string[] parts = tag.Split('-');
        if (parts.Length > 1 && parts[1].Equals("Latn", StringComparison.OrdinalIgnoreCase) && !HasScriptChoice(parts[0]))
            return string.Join("-", parts.Where((_, index) => index != 1));
        return tag;
    }

    /// <summary>
    /// Language plus script, used to match regional relatives without crossing writing systems.
    /// Returns empty for malformed tags.
    /// </summary>
    private static string LanguageFamily(string tag)
    {
        string[] parts = tag.Split('-');
        if (parts.Any(part => part.Length is < 1 or > 8 || !part.All(char.IsAsciiLetterOrDigit))) return "";

        string language = parts[0].ToLowerInvariant();
        string? script = parts.Skip(1).FirstOrDefault(part => part.Length == 4 && part.All(char.IsAsciiLetter));

        if (language == "zh")
        {
            // An explicit script decides; otherwise Taiwan, Hong Kong and Macao imply Traditional.
            if (script is not null) return "zh-" + script;
            bool traditional = parts.Any(part => part.Equals("TW", StringComparison.OrdinalIgnoreCase)
                                              || part.Equals("HK", StringComparison.OrdinalIgnoreCase)
                                              || part.Equals("MO", StringComparison.OrdinalIgnoreCase));
            return traditional ? "zh-Hant" : "zh-Hans";
        }

        if (script?.Equals("Latn", StringComparison.OrdinalIgnoreCase) == true && !HasScriptChoice(language)) script = null;
        return script is null ? language : language + "-" + script;
    }

    /// <summary>Languages commonly written in more than one script, where "Latn" is a real choice.</summary>
    private static bool HasScriptChoice(string language) =>
        language.ToLowerInvariant() is "zh" or "sr" or "bs" or "az" or "uz" or "pa" or "ha" or "mn" or "sd" or "ks";
}
