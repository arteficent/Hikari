using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Hikari.WindowsClient.Themes;

/// <summary>
/// One of Hikari's washi-paper palettes. Ports
/// <c>android-client/app/src/ui/theme/Color.kt</c> + <c>Theme.kt</c> to WinUI.
/// </summary>
public sealed record HikariTheme(
    string Name,
    ElementTheme Base,
    Color Primary,
    Color PrimaryDim,
    Color Container,
    Color Accent,
    Color Background,
    Color BackgroundAlt,
    Color Surface,
    Color Border,
    Color Text,
    Color TextSoft)
{
    // Washi-paper base tones, shared by the three light themes.
    private static readonly Color WashiCream = Rgb(0xFAF3E8);
    private static readonly Color WashiIvory = Rgb(0xFFF8EF);
    private static readonly Color WashiEdge = Rgb(0xE8DFD0);
    private static readonly Color WashiText = Rgb(0x2E2420);
    private static readonly Color WashiTextSoft = Rgb(0x5C4F45);
    private static readonly Color GoldLeaf = Rgb(0xCEA84C);

    public static readonly HikariTheme Wisteria = new(
        "Wisteria", ElementTheme.Light,
        Primary: Rgb(0x6B4C8A), PrimaryDim: Rgb(0x4A2D6B), Container: Rgb(0xE8DCF4), Accent: GoldLeaf,
        Background: WashiCream, BackgroundAlt: Rgb(0xF3E9F7), Surface: WashiIvory, Border: WashiEdge,
        Text: WashiText, TextSoft: WashiTextSoft);

    public static readonly HikariTheme Sakura = new(
        "Sakura", ElementTheme.Light,
        Primary: Rgb(0xC47A8A), PrimaryDim: Rgb(0x8A4555), Container: Rgb(0xF5DDE3), Accent: GoldLeaf,
        Background: WashiCream, BackgroundAlt: Rgb(0xFBEDF1), Surface: WashiIvory, Border: WashiEdge,
        Text: WashiText, TextSoft: WashiTextSoft);

    public static readonly HikariTheme Gold = new(
        "Gold", ElementTheme.Light,
        Primary: Rgb(0xB08830), PrimaryDim: Rgb(0x7A5F20), Container: Rgb(0xF2E6C4), Accent: Rgb(0xC49B40),
        Background: WashiCream, BackgroundAlt: Rgb(0xF8F0DC), Surface: WashiIvory, Border: WashiEdge,
        Text: WashiText, TextSoft: WashiTextSoft);

    public static readonly HikariTheme Celestial = new(
        "Celestial", ElementTheme.Dark,
        Primary: Rgb(0xD4A843), PrimaryDim: Rgb(0xA88430), Container: Rgb(0x1A1A28), Accent: Rgb(0xE8D48C),
        Background: Rgb(0x050508), BackgroundAlt: Rgb(0x0A0A14), Surface: Rgb(0x0C0C12), Border: Rgb(0x1A1A28),
        Text: Rgb(0xE8E4F0), TextSoft: Rgb(0x9090A8));

    public static readonly IReadOnlyList<HikariTheme> All = [Wisteria, Sakura, Gold, Celestial];

    public static HikariTheme FromName(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Wisteria;

    private static Color Rgb(uint hex) =>
        Color.FromArgb(0xFF, (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    public static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}

/// <summary>
/// Applies a <see cref="HikariTheme"/> by mutating the shared
/// <see cref="SolidColorBrush"/> resources declared in App.xaml, so every open
/// page repaints without being rebuilt.
/// </summary>
public static class ThemeManager
{
    public static HikariTheme Current { get; private set; } = HikariTheme.Wisteria;

    public static event EventHandler<HikariTheme>? ThemeChanged;

    public static void Apply(HikariTheme theme)
    {
        Current = theme;

        Set("HikariPrimaryBrush", theme.Primary);
        Set("HikariPrimaryDimBrush", theme.PrimaryDim);
        Set("HikariContainerBrush", theme.Container);
        Set("HikariAccentBrush", theme.Accent);
        Set("HikariBackgroundBrush", theme.Background);
        Set("HikariBackgroundAltBrush", theme.BackgroundAlt);
        Set("HikariSurfaceBrush", theme.Surface);
        Set("HikariBorderBrush", theme.Border);
        Set("HikariTextBrush", theme.Text);
        Set("HikariTextSoftBrush", theme.TextSoft);
        Set("HikariPrimarySoftBrush", HikariTheme.WithAlpha(theme.Primary, 0x24));
        Set("HikariScrimBrush", HikariTheme.WithAlpha(theme.Text, 0x14));

        if (Application.Current.Resources.TryGetValue("HikariAccentGradient", out var gradient) &&
            gradient is LinearGradientBrush linear && linear.GradientStops.Count >= 2)
        {
            linear.GradientStops[0].Color = theme.Primary;
            linear.GradientStops[1].Color = theme.Accent;
        }

        ApplySystemAccent(theme);

        ThemeChanged?.Invoke(null, theme);
    }

    /// <summary>
    /// WinUI controls (accent buttons, hyperlinks, checkboxes, sliders, selection
    /// highlight) default to the <em>operating system</em> accent colour, which would
    /// leave the app looking half-themed. Override the system brush keys with direct
    /// app-level entries so the Hikari palette wins everywhere.
    /// </summary>
    private static void ApplySystemAccent(HikariTheme theme)
    {
        var primary = theme.Primary;
        var hover = HikariTheme.WithAlpha(primary, 0xE6);
        var pressed = HikariTheme.WithAlpha(primary, 0xCC);
        var onPrimary = ReadableOn(primary);

        foreach (var key in AccentDefaultKeys) SetOverride(key, primary);
        foreach (var key in AccentHoverKeys) SetOverride(key, hover);
        foreach (var key in AccentPressedKeys) SetOverride(key, pressed);

        SetOverride("TextOnAccentFillColorPrimaryBrush", onPrimary);
        SetOverride("TextOnAccentFillColorSecondaryBrush", HikariTheme.WithAlpha(onPrimary, 0xC8));
        SetOverride("TextOnAccentFillColorSelectedTextBrush", onPrimary);

        // Hyperlinks and other "accent text" read better in the dim tone on paper.
        SetOverride("AccentTextFillColorPrimaryBrush", theme.PrimaryDim);
        SetOverride("AccentTextFillColorSecondaryBrush", theme.PrimaryDim);
        SetOverride("AccentTextFillColorTertiaryBrush", primary);
        SetOverride("HyperlinkButtonForeground", theme.PrimaryDim);
        SetOverride("HyperlinkButtonForegroundPointerOver", primary);
        SetOverride("HyperlinkButtonForegroundPressed", primary);

        SetOverride("TextControlSelectionHighlightColor", HikariTheme.WithAlpha(primary, 0x99));
        SetOverride("SystemControlFocusVisualPrimaryBrush", theme.Text);
    }

    private static readonly string[] AccentDefaultKeys =
    [
        "AccentFillColorDefaultBrush",
        "AccentFillColorSelectedTextBackgroundBrush",
        "SystemControlHighlightAccentBrush",
        "SystemControlBackgroundAccentBrush",
        "AccentButtonBackground",
        "TextControlBorderBrushFocused",
        "AutoSuggestBoxTextBoxBorderBrushFocused",
        "ComboBoxBorderBrushFocused",
        "CalendarDatePickerBorderBrushFocused",
        "CheckBoxCheckBackgroundFillChecked",
        "CheckBoxCheckBackgroundStrokeChecked",
        "RadioButtonOuterEllipseCheckedFill",
        "RadioButtonOuterEllipseCheckedStroke",
        "ToggleSwitchFillOn",
        "ToggleSwitchStrokeOn",
        "SliderTrackValueFill",
        "SliderThumbBackground",
        "ProgressBarForeground",
        "ProgressRingForegroundThemeBrush",
        "ListViewItemBackgroundSelected",
        "TabViewItemHeaderBackgroundSelected",
    ];

    private static readonly string[] AccentHoverKeys =
    [
        "AccentFillColorSecondaryBrush",
        "AccentButtonBackgroundPointerOver",
        "CheckBoxCheckBackgroundFillCheckedPointerOver",
        "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
        "RadioButtonOuterEllipseCheckedFillPointerOver",
        "ToggleSwitchFillOnPointerOver",
        "SliderTrackValueFillPointerOver",
        "ListViewItemBackgroundSelectedPointerOver",
    ];

    private static readonly string[] AccentPressedKeys =
    [
        "AccentFillColorTertiaryBrush",
        "AccentButtonBackgroundPressed",
        "CheckBoxCheckBackgroundFillCheckedPressed",
        "CheckBoxCheckBackgroundStrokeCheckedPressed",
        "RadioButtonOuterEllipseCheckedFillPressed",
        "ToggleSwitchFillOnPressed",
        "SliderTrackValueFillPressed",
        "ListViewItemBackgroundSelectedPressed",
    ];

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    private static Color ReadableOn(Color background)
    {
        var luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
        return luminance > 0.6 ? Color.FromArgb(0xFF, 0x14, 0x12, 0x10) : Colors.White;
    }

    /// <summary>
    /// Always writes a brush we own as a direct entry on Application.Resources —
    /// direct entries take priority over the merged XamlControlsResources dictionary.
    /// </summary>
    private static void SetOverride(string key, Color color)
    {
        if (SystemOverrides.TryGetValue(key, out var existing))
        {
            existing.Color = color;
            return;
        }

        var brush = new SolidColorBrush(color);
        SystemOverrides[key] = brush;
        Application.Current.Resources[key] = brush;
    }

    private static readonly Dictionary<string, SolidColorBrush> SystemOverrides = new(StringComparer.Ordinal);

    private static void Set(string key, Color color)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) &&
            resource is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}
