namespace Hikari.WindowsClient.Content;

public enum FormFieldKind
{
    Text,
    Dropdown,
    Date,
}

public sealed record FormOption(string Value, string Label);

/// <summary>
/// Declarative description of one input a plugin needs, used for both the upload
/// form and the server-side filter panel.
///
/// The android client renders these inline as Compose <c>@Composable</c> members of
/// the plugin. On Windows the plugin instead <i>declares</i> its fields and a single
/// generic control renders them, which keeps plugins free of XAML and means adding
/// a content type never requires touching the UI layer.
/// </summary>
public sealed record FormField(
    string Key,
    string Label,
    FormFieldKind Kind = FormFieldKind.Text,
    IReadOnlyList<FormOption>? Options = null,
    bool Required = false,
    string? DefaultValue = null,
    string? Placeholder = null)
{
    public static FormField Text(string key, string label, bool required = false, string? placeholder = null) =>
        new(key, label, FormFieldKind.Text, Required: required, Placeholder: placeholder);

    public static FormField Date(string key, string label) =>
        new(key, label, FormFieldKind.Date, Placeholder: "YYYY-MM-DD");

    public static FormField Dropdown(
        string key, string label, IReadOnlyList<FormOption> options, string? defaultValue = null, bool required = false) =>
        new(key, label, FormFieldKind.Dropdown, options, required, defaultValue ?? options.FirstOrDefault()?.Value);
}
