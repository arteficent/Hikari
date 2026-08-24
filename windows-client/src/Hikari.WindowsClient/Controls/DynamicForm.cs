using Hikari.WindowsClient.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hikari.WindowsClient.Controls;

/// <summary>
/// Renders an <see cref="IReadOnlyList{FormField}"/> into real controls and reads
/// the values back out. Used by both the upload form and the server-side filter
/// panel, so a new content plugin never needs its own XAML.
/// </summary>
public sealed class DynamicForm : StackPanel
{
    private readonly Dictionary<string, FrameworkElement> _inputs = new(StringComparer.Ordinal);
    private IReadOnlyList<FormField> _fields = [];

    public DynamicForm()
    {
        Spacing = 10;
    }

    public event EventHandler? ValueChanged;

    public IReadOnlyList<FormField> Fields => _fields;

    public void Render(IReadOnlyList<FormField> fields)
    {
        _fields = fields;
        _inputs.Clear();
        Children.Clear();

        foreach (var field in fields)
        {
            var control = (FrameworkElement)(field.Kind switch
            {
                FormFieldKind.Dropdown => BuildDropdown(field),
                FormFieldKind.Date => BuildDate(field),
                _ => (FrameworkElement)BuildText(field),
            });

            _inputs[field.Key] = control;
            Children.Add(control);
        }
    }

    /// <summary>Current values, with blank entries omitted.</summary>
    public Dictionary<string, string> GetValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, control) in _inputs)
        {
            var value = control switch
            {
                TextBox text => text.Text?.Trim() ?? string.Empty,
                ComboBox combo => combo.SelectedValue as string ?? string.Empty,
                CalendarDatePicker date => date.Date?.ToString("yyyy-MM-dd") ?? string.Empty,
                _ => string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
        }

        return values;
    }

    public void SetValues(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null) return;

        foreach (var (key, value) in values)
        {
            SetValue(key, value);
        }
    }

    public void SetValue(string key, string? value)
    {
        if (!_inputs.TryGetValue(key, out var control)) return;

        switch (control)
        {
            case TextBox text:
                text.Text = value ?? string.Empty;
                break;
            case ComboBox combo:
                combo.SelectedValue = value;
                break;
            case CalendarDatePicker date:
                date.Date = DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
                break;
        }
    }

    public string? GetValue(string key) => GetValues().GetValueOrDefault(key);

    /// <summary>Fills a field only when it is currently empty, for metadata auto-fill.</summary>
    public void FillIfEmpty(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!string.IsNullOrWhiteSpace(GetValue(key))) return;
        SetValue(key, value);
    }

    public void Clear()
    {
        foreach (var (_, control) in _inputs)
        {
            switch (control)
            {
                case TextBox text:
                    text.Text = string.Empty;
                    break;
                case ComboBox combo:
                    combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
                    break;
                case CalendarDatePicker date:
                    date.Date = null;
                    break;
            }
        }
    }

    private TextBox BuildText(FormField field)
    {
        var box = new TextBox
        {
            Header = Header(field),
            PlaceholderText = field.Placeholder ?? string.Empty,
            Text = field.DefaultValue ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        return box;
    }

    private ComboBox BuildDropdown(FormField field)
    {
        var combo = new ComboBox
        {
            Header = Header(field),
            ItemsSource = field.Options,
            DisplayMemberPath = nameof(FormOption.Label),
            SelectedValuePath = nameof(FormOption.Value),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (field.DefaultValue is not null) combo.SelectedValue = field.DefaultValue;
        combo.SelectionChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        return combo;
    }

    private CalendarDatePicker BuildDate(FormField field)
    {
        var picker = new CalendarDatePicker
        {
            Header = Header(field),
            PlaceholderText = field.Placeholder ?? "Pick a date",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (DateTimeOffset.TryParse(field.DefaultValue, out var parsed)) picker.Date = parsed;
        picker.DateChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        return picker;
    }

    private static string Header(FormField field) => field.Required ? field.Label + " *" : field.Label;
}
