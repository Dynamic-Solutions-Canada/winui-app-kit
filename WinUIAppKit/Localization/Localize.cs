using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace WinUIAppKit.Localization;

/// <summary>
/// Attached properties that set a control's text from a <see cref="UiText"/> key and update it in
/// place when the language changes, so open windows relabel without losing their state:
/// <code>
/// xmlns:l10n="using:WinUIAppKit.Localization"
/// &lt;TextBlock l10n:Localize.Text="settings.title" /&gt;
/// </code>
/// Supported: TextBlock text, ContentControl content (buttons, navigation items), headers and placeholder
/// text of ToggleSwitch / ComboBox / TextBox / PasswordBox / NumberBox / AutoSuggestBox (placeholder not
/// on ToggleSwitch), ToggleSwitch on/off labels, tooltips and the accessible name. A key on a control
/// without the property is logged as a warning, once per control type and property.
/// </summary>
public static class Localize
{
    private sealed record Binding(WeakReference<DependencyObject> Target, DependencyProperty Property);

    private static readonly List<Binding> Bindings = [];

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Control types and properties already reported as unsupported, so each is logged once.</summary>
    private static readonly HashSet<(Type, string)> Reported = [];

    /// <summary>Each attached property's name, for the unsupported-target warning.</summary>
    private static readonly Dictionary<DependencyProperty, string> PropertyNames = [];

    /// <summary>TextBlock.Text.</summary>
    public static readonly DependencyProperty TextProperty = Register("Text");

    /// <summary>ContentControl.Content (buttons, NavigationViewItem, ComboBoxItem).</summary>
    public static readonly DependencyProperty ContentProperty = Register("Content");

    /// <summary>Header of a ToggleSwitch, ComboBox, TextBox, PasswordBox, NumberBox or AutoSuggestBox.</summary>
    public static readonly DependencyProperty HeaderProperty = Register("Header");

    /// <summary>ToggleSwitch.OnContent.</summary>
    public static readonly DependencyProperty OnContentProperty = Register("OnContent");

    /// <summary>ToggleSwitch.OffContent.</summary>
    public static readonly DependencyProperty OffContentProperty = Register("OffContent");

    /// <summary>Placeholder text of a TextBox, ComboBox, PasswordBox, NumberBox or AutoSuggestBox.</summary>
    public static readonly DependencyProperty PlaceholderProperty = Register("Placeholder");

    /// <summary>Tooltip.</summary>
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");

    /// <summary>Accessible name (AutomationProperties.Name).</summary>
    public static readonly DependencyProperty NameProperty = Register("Name");

    static Localize()
    {
        UiText.Changed += () =>
        {
            Bindings.RemoveAll(binding => !binding.Target.TryGetTarget(out _));
            foreach (var binding in Bindings.ToArray())
            {
                if (binding.Target.TryGetTarget(out var target))
                {
                    Apply(target, binding.Property);
                }
            }
        };
    }

    private static DependencyProperty Register(string name)
    {
        var property = DependencyProperty.RegisterAttached(
            name, typeof(string), typeof(Localize), new PropertyMetadata(null, OnKeyChanged));
        PropertyNames[property] = name;
        return property;
    }

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        // Prune dead entries occasionally so long-lived apps that rebuild views don't accumulate them.
        if (Bindings.Count > 0 && Bindings.Count % 256 == 0)
        {
            Bindings.RemoveAll(binding => !binding.Target.TryGetTarget(out _));
        }

        if (args.OldValue is null)
        {
            Bindings.Add(new Binding(new WeakReference<DependencyObject>(target), args.Property));
        }

        Apply(target, args.Property);
    }

    /// <summary>
    /// Write the current text for the target's key into the matching control property. A key on a
    /// control that has no such property is reported once per control type and property, instead of
    /// being dropped silently.
    /// </summary>
    private static void Apply(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string key)
        {
            return;
        }

        string value = UiText.Get(key);
        bool applied = property switch
        {
            _ when property == TextProperty => ApplyText(target, value),
            _ when property == ContentProperty => ApplyContent(target, value),
            _ when property == HeaderProperty => ApplyHeader(target, value),
            _ when property == OnContentProperty => ApplyOnOff(target, value, on: true),
            _ when property == OffContentProperty => ApplyOnOff(target, value, on: false),
            _ when property == PlaceholderProperty => ApplyPlaceholder(target, value),
            _ when property == ToolTipProperty => ApplyToolTip(target, value),
            _ when property == NameProperty => ApplyName(target, value),
            _ => false,
        };

        if (!applied)
        {
            ReportUnsupported(target, property, key);
        }
    }

    private static bool ApplyText(DependencyObject target, string value)
    {
        if (target is not TextBlock text)
        {
            return false;
        }

        text.Text = value;
        return true;
    }

    private static bool ApplyContent(DependencyObject target, string value)
    {
        if (target is not ContentControl content)
        {
            return false;
        }

        content.Content = value;
        return true;
    }

    private static bool ApplyHeader(DependencyObject target, string value)
    {
        switch (target)
        {
            case ToggleSwitch toggle: toggle.Header = value; return true;
            case ComboBox combo: combo.Header = value; return true;
            case TextBox box: box.Header = value; return true;
            case PasswordBox password: password.Header = value; return true;
            case NumberBox number: number.Header = value; return true;
            case AutoSuggestBox suggest: suggest.Header = value; return true;
            default: return false;
        }
    }

    private static bool ApplyOnOff(DependencyObject target, string value, bool on)
    {
        if (target is not ToggleSwitch toggle)
        {
            return false;
        }

        if (on)
        {
            toggle.OnContent = value;
        }
        else
        {
            toggle.OffContent = value;
        }

        return true;
    }

    private static bool ApplyPlaceholder(DependencyObject target, string value)
    {
        switch (target)
        {
            case TextBox box: box.PlaceholderText = value; return true;
            case ComboBox combo: combo.PlaceholderText = value; return true;
            case PasswordBox password: password.PlaceholderText = value; return true;
            case NumberBox number: number.PlaceholderText = value; return true;
            case AutoSuggestBox suggest: suggest.PlaceholderText = value; return true;
            default: return false;
        }
    }

    private static bool ApplyToolTip(DependencyObject target, string value)
    {
        ToolTipService.SetToolTip(target, value);
        return true;
    }

    private static bool ApplyName(DependencyObject target, string value)
    {
        AutomationProperties.SetName(target, value);
        return true;
    }

    /// <summary>Log a Localize property on a control that can't show it, once per control type and property.</summary>
    private static void ReportUnsupported(DependencyObject target, DependencyProperty property, string key)
    {
        string name = PropertyNames.TryGetValue(property, out var known) ? known : "?";
        if (Reported.Add((target.GetType(), name)))
        {
            Logger.Warn("Localize.{Property} (key {Key}) has no effect on a {Control}; the text was not applied", name, key, target.GetType().Name);
        }
    }

    /// <summary>Gets the TextBlock text key.</summary>
    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    /// <summary>Sets the TextBlock text key.</summary>
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    /// <summary>Gets the content key.</summary>
    public static string GetContent(DependencyObject target) => (string)target.GetValue(ContentProperty);
    /// <summary>Sets the content key.</summary>
    public static void SetContent(DependencyObject target, string value) => target.SetValue(ContentProperty, value);
    /// <summary>Gets the header key.</summary>
    public static string GetHeader(DependencyObject target) => (string)target.GetValue(HeaderProperty);
    /// <summary>Sets the header key.</summary>
    public static void SetHeader(DependencyObject target, string value) => target.SetValue(HeaderProperty, value);
    /// <summary>Gets the ToggleSwitch "on" label key.</summary>
    public static string GetOnContent(DependencyObject target) => (string)target.GetValue(OnContentProperty);
    /// <summary>Sets the ToggleSwitch "on" label key.</summary>
    public static void SetOnContent(DependencyObject target, string value) => target.SetValue(OnContentProperty, value);
    /// <summary>Gets the ToggleSwitch "off" label key.</summary>
    public static string GetOffContent(DependencyObject target) => (string)target.GetValue(OffContentProperty);
    /// <summary>Sets the ToggleSwitch "off" label key.</summary>
    public static void SetOffContent(DependencyObject target, string value) => target.SetValue(OffContentProperty, value);
    /// <summary>Gets the placeholder key.</summary>
    public static string GetPlaceholder(DependencyObject target) => (string)target.GetValue(PlaceholderProperty);
    /// <summary>Sets the placeholder key.</summary>
    public static void SetPlaceholder(DependencyObject target, string value) => target.SetValue(PlaceholderProperty, value);
    /// <summary>Gets the tooltip key.</summary>
    public static string GetToolTip(DependencyObject target) => (string)target.GetValue(ToolTipProperty);
    /// <summary>Sets the tooltip key.</summary>
    public static void SetToolTip(DependencyObject target, string value) => target.SetValue(ToolTipProperty, value);
    /// <summary>Gets the accessible name key.</summary>
    public static string GetName(DependencyObject target) => (string)target.GetValue(NameProperty);
    /// <summary>Sets the accessible name key.</summary>
    public static void SetName(DependencyObject target, string value) => target.SetValue(NameProperty, value);
}
