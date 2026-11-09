using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private readonly List<ToggleButton> expandedColorSwatches = [];

    private void InitializeColorPicker()
    {
        Unloaded += (_, _) => ColorPickerPopup.IsOpen = false;
        foreach (var (name, hex) in new[] { ("Red", "FF453A"), ("Green", "30D158"), ("Blue", "0A84FF"), ("Purple", "BF5AF2"), ("Black", "111111") })
        {
            TryParseHexColor(hex, out var color);
            var swatch = CreateColorSwatch(color, name + " stroke", 24);
            swatch.Margin = new Thickness(1, 0, 1, 0);
            swatch.Click += (_, _) => ChooseColor(color);
            ColorSwatches.Children.Insert(ColorSwatches.Children.Count - 2, swatch);
            colorSwatches.Add(swatch);
        }
        foreach (var hex in new[] { "111111", "495057", "ADB5BD", "E9ECEF", "FFFFFF", "15AABF", "0A84FF", "7950F2", "BF5AF2", "F06595", "30D158", "12B886", "FF9F0A", "FF6B35", "FF453A" })
        {
            TryParseHexColor(hex, out var color);
            var swatch = CreateColorSwatch(color, "#" + hex, 30);
            swatch.Click += (_, _) => { ChooseColor(color); BuildColorShades(color); };
            ExpandedColorSwatches.Children.Add(swatch);
            expandedColorSwatches.Add(swatch);
        }
    }

    private ToggleButton CreateColorSwatch(Color color, string name, double size)
    {
        var swatch = new ToggleButton
        {
            Width = size, Height = size, Margin = new Thickness(2), Tag = color,
            Style = (Style)FindResource("ColorSwatchStyle"), Background = new SolidColorBrush(color), ToolTip = name,
            Foreground = color.R * .299 + color.G * .587 + color.B * .114 > 145 ? Brushes.Black : Brushes.White
        };
        System.Windows.Automation.AutomationProperties.SetName(swatch, name);
        return swatch;
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e) => ChooseCustomColor();

    private void ChooseCustomColor()
    {
        propertiesOpen = true;
        UpdateEditorLayout();
        UpdateColorPicker(Annotations.InkColor);
        BuildColorShades(Annotations.InkColor);
        ColorPickerPopup.IsOpen = true;
    }

    private void UpdateColorPicker(Color color)
    {
        CurrentColorSwatch.Background = new SolidColorBrush(color);
        foreach (var swatch in colorSwatches.Concat(expandedColorSwatches).Concat(ColorShades.Children.OfType<ToggleButton>()))
        {
            swatch.IsChecked = Equals(swatch.Tag, color);
            swatch.Content = swatch.IsChecked == true ? "✓" : "";
        }
        ColorHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ColorPickerButton.ToolTip = $"More colors · {ColorHex.Text}";
    }

    private void BuildColorShades(Color color)
    {
        ColorShades.Children.Clear();
        foreach (var amount in new[] { -.6, -.3, 0, .3, .6 })
        {
            byte Mix(byte channel) => (byte)Math.Round(amount < 0 ? channel * (1 + amount) : channel + (255 - channel) * amount);
            var shade = Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));
            var swatch = CreateColorSwatch(shade, $"#{shade.R:X2}{shade.G:X2}{shade.B:X2}", 30);
            swatch.IsChecked = shade == Annotations.InkColor;
            swatch.Content = swatch.IsChecked == true ? "✓" : "";
            swatch.Click += (_, _) => ChooseColor(shade);
            ColorShades.Children.Add(swatch);
        }
    }

    internal static bool TryParseHexColor(string text, out Color color)
    {
        var hex = text.Trim().TrimStart('#');
        uint value = 0;
        var valid = hex.Length == 6 && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return valid;
    }

    private void ColorHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ApplyHexColorButton is null || ColorHexError is null) return;
        var valid = TryParseHexColor(ColorHex.Text, out _);
        ApplyHexColorButton.IsEnabled = valid;
        ColorHexError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyHexColor_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseHexColor(ColorHex.Text, out var color)) return;
        ChooseColor(color);
        BuildColorShades(color);
    }

    private void ColorPicker_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ColorPickerPopup.IsOpen = false; ColorPickerButton.Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter && ColorHex.IsKeyboardFocusWithin) { ApplyHexColor_Click(sender, e); e.Handled = true; }
    }
}
