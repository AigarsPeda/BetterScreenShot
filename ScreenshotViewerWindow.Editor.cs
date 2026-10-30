using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ColorConverter = System.Windows.Media.ColorConverter;
using MenuItem = System.Windows.Controls.MenuItem;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private sealed record NativeOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    private bool syncingControls;
    private int editingTextIndex = -1;
    private Point textPosition;
    private readonly List<ToggleButton> toolButtons = [];
    private readonly List<MenuItem> colorItems = [];

    private void InitializeEditor(BitmapSource image)
    {
        ImageStage.Width = Annotations.Width = image.PixelWidth;
        ImageStage.Height = Annotations.Height = image.PixelHeight;
        ImageDetails.Text = $"{image.PixelWidth} × {image.PixelHeight} pixels";
        OcrLanguagePicker.Items.Add(new ComboBoxItem { Content = "Auto", Tag = TextExtractionService.AutomaticLanguage });
        foreach (var language in TextExtractionService.Languages)
            OcrLanguagePicker.Items.Add(new ComboBoxItem { Content = language.Value, Tag = language.Key });
        OcrLanguagePicker.Items.Add(new ComboBoxItem { Content = "English + Latvian", Tag = "eng+lav" });
        OcrLanguagePicker.SelectedIndex = 0;
        (AnnotationTool Tool, string Label, string Key, string Icon)[] tools =
        [
            (AnnotationTool.Select, "Select", "V", "M5 3l14 9-7 1-3 7z"),
            (AnnotationTool.Hand, "Pan", "H", "M7 12V7a2 2 0 0 1 4 0v4-7a2 2 0 0 1 4 0v7-5a2 2 0 0 1 4 0v6-2a2 2 0 0 1 4 0v5q0 7-7 7h-2q-3 0-5-3l-4-6q-1-3 2-2l3 3"),
            (AnnotationTool.Pen, "Pen", "P", "M4 17l-1 4 4-1L20 7l-3-3z M14 7l3 3"),
            (AnnotationTool.Highlight, "Highlight", "M", "M5 14l8-10 6 5-8 10z M5 14l6 5 M3 21h11"),
            (AnnotationTool.Line, "Underline / line", "L", "M4 17h16 M7 4v5a5 5 0 0 0 10 0V4"),
            (AnnotationTool.Arrow, "Arrow", "A", "M4 20L20 4 M9 4h11v11"),
            (AnnotationTool.Rectangle, "Rectangle", "R", "M4 5h16v14H4z"),
            (AnnotationTool.Ellipse, "Circle / oval", "O", "M20 12a8 8 0 1 1-16 0 8 8 0 0 1 16 0"),
            (AnnotationTool.Text, "Text", "T", "M4 5h16 M12 5v15 M8 20h8")
        ];
        foreach (var (tool, label, key, icon) in tools)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon), Width = 20, Height = 20, Stretch = Stretch.Uniform,
                StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            };
            path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ViewerPrimaryTextBrush");
            var button = new ToggleButton { Style = (Style)FindResource("ToolStyle"), Tag = tool, Content = path, ToolTip = $"{label} ({key})" };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => ChooseTool(tool);
            toolButtons.Add(button);
            ToolsPanel.Children.Add(button);
        }

        (string Name, string Hex)[] colors = [("Red", "#FF453A"), ("Orange", "#FF9F0A"), ("Yellow", "#FFD60A"),
            ("Green", "#30D158"), ("Blue", "#0A84FF"), ("Purple", "#BF5AF2"), ("White", "#FFFFFF"), ("Black", "#111111")];
        foreach (var (name, hex) in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            header.Children.Add(new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0),
                Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Colors.Gray), StrokeThickness = .5 });
            header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header, Tag = color, IsCheckable = true, Style = (Style)FindResource("AnnotationMenuItemStyle") };
            AutomationProperties.SetName(item, name);
            item.Click += (_, _) => ChooseColor(color);
            ColorPaletteMenu.Items.Add(item);
            colorItems.Add(item);
        }
        var custom = new MenuItem { Header = "Custom color…", Style = (Style)FindResource("AnnotationMenuItemStyle") };
        AutomationProperties.SetName(custom, "Custom color");
        custom.Click += (_, _) => { ColorPaletteMenu.IsOpen = false; ChooseCustomColor(); };
        ColorPaletteMenu.Items.Add(custom);
        Annotations.SelectionChanged += SyncEditor;
        Annotations.TextRequested += BeginText;
        Annotations.ContextRequested += ShowAnnotationMenu;
        Annotations.Document.Changed += SyncEditor;
        ChooseColor((Color)ColorConverter.ConvertFromString("#FF453A"));
        ChooseTool(AnnotationTool.Select);
    }

    private void ChooseTool(AnnotationTool tool)
    {
        CommitText();
        Annotations.Tool = tool;
        foreach (var button in toolButtons) button.IsChecked = Equals(button.Tag, tool);
        SyncEditor();
    }

    private void ChooseColor(Color color)
    {
        Annotations.InkColor = color;
        if (InlineText.IsVisible) InlineText.Foreground = new SolidColorBrush(color);
        else Annotations.ChangeSelected(item => item.Color = color);
        UpdateColorPicker(color);
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPaletteMenu.PlacementTarget = ColorPickerButton;
        ColorPaletteMenu.IsOpen = true;
    }

    private void UpdateColorPicker(Color color)
    {
        CurrentColorSwatch.Fill = new SolidColorBrush(color);
        foreach (var item in colorItems)
        {
            item.IsChecked = Equals(item.Tag, color);
            item.InputGestureText = item.IsChecked ? "✓" : "";
        }
        var name = colorItems.FirstOrDefault(item => Equals(item.Tag, color)) is { } selected
            ? AutomationProperties.GetName(selected) : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ColorPickerButton.ToolTip = $"Annotation color: {name}";
    }

    private void SketchToggle_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        Annotations.CancelGesture();
        Annotations.Document.SketchEnabled = SketchToggle.IsChecked == true;
    }

    private void SyncEditor()
    {
        syncingControls = true;
        var selected = Annotations.Selected;
        if (selected is not null)
        {
            Annotations.InkColor = selected.Color;
            UpdateColorPicker(selected.Color);
            var width = selected.Tool == AnnotationTool.Highlight ? selected.Width / 5 : selected.Width;
            StrokeWidthPicker.SelectedIndex = width <= 2 ? 0 : width >= 7 ? 2 : 1;
            if (selected.Tool == AnnotationTool.Text)
                FontSizePicker.Text = selected.FontSize.ToString("0");
        }
        UndoButton.IsEnabled = Annotations.Document.CanUndo;
        RedoButton.IsEnabled = Annotations.Document.CanRedo;
        RemoveButton.IsEnabled = selected is not null;
        syncingControls = false;
    }

    private void StrokeWidth_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncingControls || Annotations is null || StrokeWidthPicker.SelectedItem is not ComboBoxItem item) return;
        Annotations.InkWidth = double.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture);
        Annotations.ChangeSelected(annotation => annotation.Width = Annotations.InkWidth * (annotation.Tool == AnnotationTool.Highlight ? 5 : 1));
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncingControls || Annotations is null || FontSizePicker.SelectedItem is not ComboBoxItem item) return;
        Annotations.TextSize = double.Parse((string)item.Content, System.Globalization.CultureInfo.InvariantCulture);
        if (InlineText.IsVisible) InlineText.FontSize = Annotations.TextSize;
        else if (Annotations.Selected?.Tool == AnnotationTool.Text) Annotations.ChangeSelected(annotation => annotation.FontSize = Annotations.TextSize);
    }

    private void BeginText(Point position)
    {
        CommitText();
        editingTextIndex = Annotations.Selected?.Tool == AnnotationTool.Text ? Annotations.SelectedIndex : -1;
        var existing = editingTextIndex >= 0 ? Annotations.Selected : null;
        textPosition = position;
        Canvas.SetLeft(InlineText, position.X);
        Canvas.SetTop(InlineText, position.Y);
        InlineText.MaxWidth = Math.Max(40, ImageStage.Width - position.X);
        InlineText.MinWidth = Math.Min(80, InlineText.MaxWidth);
        InlineText.FontSize = existing?.FontSize ?? Annotations.TextSize;
        InlineText.Foreground = new SolidColorBrush(existing?.Color ?? Annotations.InkColor);
        InlineText.Text = existing?.Text ?? "";
        InlineText.Visibility = Visibility.Visible;
        InlineText.Focus();
        InlineText.SelectAll();
    }

    private void CommitText(bool cancel = false)
    {
        if (InlineText.Visibility != Visibility.Visible) return;
        var value = InlineText.Text;
        InlineText.Visibility = Visibility.Collapsed;
        if (!cancel && !string.IsNullOrWhiteSpace(value))
        {
            var item = new ImageAnnotation { Tool = AnnotationTool.Text, Points = [textPosition], Text = value,
                Color = ((SolidColorBrush)InlineText.Foreground).Color, FontSize = InlineText.FontSize };
            if (editingTextIndex >= 0)
                Annotations.Document.Replace(editingTextIndex, Annotations.Document.Items[editingTextIndex], item);
            else
            {
                Annotations.Document.Add(item);
                editingTextIndex = Annotations.Document.Items.Count - 1;
            }
            Annotations.Select(editingTextIndex);
        }
        editingTextIndex = -1;
    }

    private void InlineText_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitText();
    private void InlineText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitText(e.Key == Key.Escape);
            Annotations.Focus();
            e.Handled = true;
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) { CommitText(); Annotations.CancelGesture(); Annotations.Select(-1); Annotations.Document.Undo(); }
    private void RedoButton_Click(object sender, RoutedEventArgs e) { CommitText(); Annotations.CancelGesture(); Annotations.Select(-1); Annotations.Document.Redo(); }
    private void RemoveButton_Click(object sender, RoutedEventArgs e) { CommitText(); Annotations.CancelGesture(); Annotations.DeleteSelected(); }
    private void MoreButton_Click(object sender, RoutedEventArgs e) { var button = (Button)sender; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; }
    private void FitButton_Click(object sender, RoutedEventArgs e) { currentZoom = 1; UpdateZoomState(); }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDisplayScale();
    private void Viewport_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) { if (e.OriginalSource == ImageViewport) EndDrag(); }

    private void UpdateDisplayScale()
    {
        if (Annotations is null || ImageStage.Width <= 0) return;
        Annotations.DisplayScale = Math.Max(.01, Math.Min(Math.Max(1, ImageViewport.ActualWidth - 40) / ImageStage.Width,
            Math.Max(1, ImageViewport.ActualHeight - 40) / ImageStage.Height) * currentZoom);
        Annotations.DrawSelection();
        OcrSelectionHighlight.StrokeThickness = 1.5 / Annotations.DisplayScale;
        FitButton.Content = $"{Annotations.DisplayScale * 100:0}%";
    }

    private void Editor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.T when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): ExtractTextButton_Click(sender, e); break;
                case Key.Z when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): RedoButton_Click(sender, e); break;
                case Key.Z: UndoButton_Click(sender, e); break;
                case Key.Y: RedoButton_Click(sender, e); break;
                case Key.D: CommitText(); Annotations.CancelGesture(); Annotations.DuplicateSelected(); break;
                case Key.C: CopyButton_Click(sender, e); break;
                case Key.S: SaveAsButton_Click(sender, e); break;
                case Key.D0: FitButton_Click(sender, e); break;
                case Key.Add: case Key.OemPlus: ChangeZoom(ZoomStep); break;
                case Key.Subtract: case Key.OemMinus: ChangeZoom(-ZoomStep); break;
                default: return;
            }
        }
        else
        {
            var tool = e.Key switch { Key.V => AnnotationTool.Select, Key.H => AnnotationTool.Hand, Key.P => AnnotationTool.Pen,
                Key.M => AnnotationTool.Highlight, Key.L => AnnotationTool.Line, Key.A => AnnotationTool.Arrow, Key.R => AnnotationTool.Rectangle,
                Key.O => AnnotationTool.Ellipse, Key.T => AnnotationTool.Text, _ => (AnnotationTool?)null };
            if (tool is not null) ChooseTool(tool.Value);
            else if (e.Key is Key.Delete or Key.Back) RemoveButton_Click(sender, e);
            else if (e.Key == Key.Escape) { Annotations.CancelGesture(); Annotations.Select(-1); }
            else if (e.Key == Key.Space) { Annotations.Cursor = System.Windows.Input.Cursors.Hand; }
            else return;
        }
        e.Handled = true;
    }

    private void Editor_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Space && !InlineText.IsVisible)
            Annotations.Cursor = Annotations.Tool switch { AnnotationTool.Select => System.Windows.Input.Cursors.Arrow, AnnotationTool.Hand => System.Windows.Input.Cursors.Hand,
                AnnotationTool.Text => System.Windows.Input.Cursors.IBeam, _ => System.Windows.Input.Cursors.Cross };
    }
}
