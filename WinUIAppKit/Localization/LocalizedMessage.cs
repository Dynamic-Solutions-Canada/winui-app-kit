namespace WinUIAppKit.Localization;

/// <summary>
/// A message to show later: a <see cref="UiText"/> key plus its placeholder values, rendered in the
/// language current when it is shown. Store this, not finished text, on models and services, so an
/// error or status line follows a language change instead of staying in the old language.
/// </summary>
public sealed class LocalizedMessage
{
    private readonly (string Name, object? Value)[] _values;

    /// <param name="key">Catalog key.</param>
    /// <param name="values">
    /// Values for the key's <c>{{name}}</c> placeholders. A value's <see cref="object.ToString"/> runs
    /// at display time, so a value type may render itself in the current language too.
    /// </param>
    public LocalizedMessage(string key, params (string Name, object? Value)[] values)
    {
        Key = key;
        _values = values;
    }

    /// <summary>The catalog key.</summary>
    public string Key { get; }

    /// <summary>The message in the current interface language.</summary>
    public string Text => UiText.Format(Key, _values);

    /// <summary>Key and values, for logs and change detection (not for display).</summary>
    public override string ToString() =>
        $"{Key}({string.Join(", ", _values.Select(v => $"{v.Name}={v.Value}"))})";
}
