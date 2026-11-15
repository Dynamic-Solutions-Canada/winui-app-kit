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
/// Supported: TextBlock text, ContentControl content (buttons, navigation items), headers of
/// ToggleSwitch / ComboBox / TextBox, ToggleSwitch on/off labels, placeholder text, tooltips and the
/// accessible name.
/// </summary>
public static class Localize
{
    private sealed record Binding(WeakReference<DependencyObject> Target, DependencyProperty Property);

    private static readonly List<Binding> s_bindings = [];

    /// <summary>TextBlock.Text.</summary>
    public static readonly DependencyProperty TextProperty = Register("Text");

    /// <summary>ContentControl.Content (buttons, NavigationViewItem, ComboBoxItem).</summary>
    public static readonly DependencyProperty ContentProperty = Register("Content");

    /// <summary>Header of a ToggleSwitch, ComboBox or TextBox.</summary>
    public static readonly DependencyProperty HeaderProperty = Register("Header");

    /// <summary>ToggleSwitch.OnContent.</summary>
    public static readonly DependencyProperty OnContentProperty = Register("OnContent");

    /// <summary>ToggleSwitch.OffContent.</summary>
    public static readonly DependencyProperty OffContentProperty = Register("OffContent");

    /// <summary>TextBox / ComboBox placeholder text.</summary>
    public static readonly DependencyProperty PlaceholderProperty = Register("Placeholder");

    /// <summary>Tooltip.</summary>
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");

    /// <summary>Accessible name (AutomationProperties.Name).</summary>
    public static readonly DependencyProperty NameProperty = Register("Name");

    static Localize()
    {
        UiText.Changed += () =>
        {
            s_bindings.RemoveAll(binding => !binding.Target.TryGetTarget(out _));
            foreach (var binding in s_bindings.ToArray())
            {
                if (binding.Target.TryGetTarget(out var target)) Apply(target, binding.Property);
            }
        };
    }

    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(
        name, typeof(string), typeof(Localize), new PropertyMetadata(null, OnKeyChanged));

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        // Prune dead entries occasionally so long-lived apps that rebuild views don't accumulate them.
        if (s_bindings.Count > 0 && s_bindings.Count % 256 == 0)
            s_bindings.RemoveAll(binding => !binding.Target.TryGetTarget(out _));

        if (args.OldValue is null) s_bindings.Add(new Binding(new WeakReference<DependencyObject>(target), args.Property));
        Apply(target, args.Property);
    }

    /// <summary>Write the current text for the target's key into the matching control property.</summary>
    private static void Apply(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string key) return;
        string value = UiText.Get(key);

        if (property == TextProperty && target is TextBlock text) text.Text = value;
        else if (property == ContentProperty && target is ContentControl content) content.Content = value;
        else if (property == HeaderProperty) ApplyHeader(target, value);
        else if (property == OnContentProperty && target is ToggleSwitch on) on.OnContent = value;
        else if (property == OffContentProperty && target is ToggleSwitch off) off.OffContent = value;
        else if (property == PlaceholderProperty && target is TextBox box) box.PlaceholderText = value;
        else if (property == PlaceholderProperty && target is ComboBox combo) combo.PlaceholderText = value;
        else if (property == ToolTipProperty) ToolTipService.SetToolTip(target, value);
        else if (property == NameProperty) AutomationProperties.SetName(target, value);
    }

    private static void ApplyHeader(DependencyObject target, string value)
    {
        switch (target)
        {
            case ToggleSwitch toggle: toggle.Header = value; break;
            case ComboBox combo: combo.Header = value; break;
            case TextBox box: box.Header = value; break;
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
