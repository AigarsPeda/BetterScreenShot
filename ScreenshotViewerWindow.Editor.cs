using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private bool syncingControls;
    private int editingTextIndex = -1;
    private int editingNumberIndex = -1;
    private Point textPosition;
    private readonly List<ToggleButton> toolButtons = [];

    private readonly List<ToggleButton> colorSwatches = [];

    private void InitializeEditor(BitmapSource image)
    {
        ImageStage.Width = Annotations.Width = image.PixelWidth;
        ImageStage.Height = Annotations.Height = image.PixelHeight;
        OcrLanguagePicker.Items.Add(new ComboBoxItem { Content = "Auto", Tag = TextExtractionService.AutomaticLanguage });
        foreach (var language in TextExtractionService.Languages)
            OcrLanguagePicker.Items.Add(new ComboBoxItem { Content = language.Value, Tag = language.Key });
        OcrLanguagePicker.Items.Add(new ComboBoxItem { Content = "English + Latvian", Tag = "eng+lav" });
        OcrLanguagePicker.SelectedIndex = 0;
        (AnnotationTool Tool, string Label, string Key, string Icon)[] tools =
        [
            (AnnotationTool.Select, "Select", "V", "M5 3l14 9-7 1-3 7z"),
            (AnnotationTool.Hand, "Pan", "H", "M8 13V6a2 2 0 0 1 4 0v5-1-6a2 2 0 0 1 4 0v6-1-4a2 2 0 0 1 4 0v4-1-2a2 2 0 0 1 4 0v4c0 5-3 8-8 8h-2c-3 0-5-1-7-4l-3-4a2 2 0 0 1 3-2z"),
            (AnnotationTool.Pen, "Pen", "P", "M12 19l7-7 3 3-7 7-4 1z M19 12l2-2a2 2 0 0 1 3 3l-2 2 M2 22h7"),
            (AnnotationTool.Highlight, "Highlight", "M", "M9 11l6-6 5 5-6 6z M4 20l5-9 5 5-9 5z M2 23h20"),
            (AnnotationTool.Line, "Line", "L", "M4 20 20 4"),
            (AnnotationTool.Arrow, "Arrow", "A", "M5 19 19 5 M9 5h10v10"),
            (AnnotationTool.NumberedArrow, "Numbered arrow", "N", "M3 20 13 10 M8 10h5v5 M19 2a3 3 0 1 0 0 6a3 3 0 0 0 0-6"),
            (AnnotationTool.Rectangle, "Rectangle", "R", "M5 4h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z"),
            (AnnotationTool.Ellipse, "Ellipse", "O", "M20 12a8 8 0 1 1-16 0 8 8 0 0 1 16 0"),
            (AnnotationTool.Text, "Text", "T", "M4 7V4h16v3 M12 4v16 M9 20h6")
        ];
        foreach (var (tool, label, _, icon) in tools)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon), Width = 18, Height = 18, Stretch = Stretch.Uniform,
                StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(ToggleButton.Foreground))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1)
            });
            var button = new ToggleButton { Style = (Style)FindResource("TopRowToolStyle"), Tag = tool, Content = path, ToolTip = $"{label} ({EditorSettingsService.Current.ToolKeys[tool]})" };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => ChooseTool(tool);
            toolButtons.Add(button);
            ToolsPanel.Children.Add(button);
        }

        InitializeColorPicker();
        Annotations.SelectionChanged += SyncEditor;
        Annotations.TextRequested += BeginText;
        Annotations.NumberRequested += BeginNumberEdit;
        Annotations.ContextRequested += ShowAnnotationMenu;
        var settings = EditorSettingsService.Current;
        syncingControls = true;
        StrokeWidthPicker.SelectedIndex = settings.StrokeWidth switch { 2 => 0, 7 => 2, _ => 1 };
        FontSizePicker.SelectedIndex = settings.TextSize switch { 16 => 0, 24 => 1, 40 => 3, 64 => 4, _ => 2 };
        syncingControls = false;
        Annotations.InkWidth = settings.StrokeWidth;
        Annotations.TextSize = settings.TextSize;
        SketchToggle.IsChecked = settings.SketchEnabled;
        Annotations.Document.SketchEnabled = settings.SketchEnabled;
        ChooseColor((Color)ColorConverter.ConvertFromString(settings.Color));
        ChooseTool(settings.OpeningTool);
    }

    private void ChooseTool(AnnotationTool tool)
    {
        CommitNumber();
        CommitText();
        CancelOcrSelectionDrag();
        ExtractedTextBox.Select(0, 0);
        propertiesOpen = null;
        if (textPanelOpen) CloseTextPanel();
        Annotations.Tool = tool;
        foreach (var button in toolButtons) button.IsChecked = Equals(button.Tag, tool);
        SyncEditor();
        EditorSettingsService.Remember(tool);
    }

    private void RefreshToolShortcutLabels()
    {
        foreach (var button in toolButtons)
        {
            var tool = (AnnotationTool)button.Tag;
            var key = EditorSettingsService.Current.ToolKeys[tool];
            button.ToolTip = $"{AutomationProperties.GetName(button)} ({key})";
        }
    }

    private void ChooseColor(Color color)
    {
        Annotations.InkColor = color;
        if (InlineText.IsVisible) InlineText.Foreground = new SolidColorBrush(color);
        else Annotations.ChangeSelected(item => item.Color = color);
        UpdateColorPicker(color);
    }

    private void SketchToggle_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        Annotations.CancelGesture();
        Annotations.Document.SketchEnabled = SketchToggle.IsChecked == true;
        PreciseToggle.IsChecked = !Annotations.Document.SketchEnabled;
    }

    private void PreciseToggle_Click(object sender, RoutedEventArgs e)
    {
        SketchToggle.IsChecked = false;
        SketchToggle_Click(sender, e);
    }

    private void SyncEditor()
    {
        syncingControls = true;
        SketchToggle.IsChecked = Annotations.Document.SketchEnabled;
        PreciseToggle.IsChecked = !Annotations.Document.SketchEnabled;
        var selected = Annotations.Selected;
        if (selected is not null)
        {
            Annotations.InkColor = selected.Color;
            UpdateColorPicker(selected.Color);
            var width = selected.Width / (selected.Tool switch
            {
                AnnotationTool.Highlight => 5,
                AnnotationTool.Arrow or AnnotationTool.NumberedArrow => selected.ArrowMetricScale,
                _ => 1
            });
            StrokeWidthPicker.SelectedIndex = width <= 2 ? 0 : width >= 7 ? 2 : 1;
            if (selected.Tool == AnnotationTool.Text)
                FontSizePicker.Text = selected.FontSize.ToString("0");
        }
        UndoButton.IsEnabled = Annotations.Document.CanUndo;
        RedoButton.IsEnabled = Annotations.Document.CanRedo;
        RemoveButton.IsEnabled = selected is not null;
        var activeTool = selected?.Tool ?? Annotations.Tool;
        var drawingTool = activeTool is not (AnnotationTool.Select or AnnotationTool.Hand);
        var strokeTool = activeTool is AnnotationTool.Pen or AnnotationTool.Highlight or AnnotationTool.Line
            or AnnotationTool.Arrow or AnnotationTool.NumberedArrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse;
        var sketchTool = activeTool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.NumberedArrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse;
        DeleteOptionsGroup.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        ColorOptionsGroup.Visibility = drawingTool ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptionsGroup.Visibility = strokeTool ? Visibility.Visible : Visibility.Collapsed;
        TextOptionsGroup.Visibility = activeTool == AnnotationTool.Text ? Visibility.Visible : Visibility.Collapsed;
        NumberOptionsGroup.Visibility = selected?.Tool == AnnotationTool.NumberedArrow ? Visibility.Visible : Visibility.Collapsed;
        if (!NumberPicker.IsKeyboardFocused) NumberPicker.Text = selected?.Tool == AnnotationTool.NumberedArrow ? selected.Number.ToString() : "";
        SketchOptionsGroup.Visibility = sketchTool ? Visibility.Visible : Visibility.Collapsed;
        hasProperties = drawingTool || selected is not null;
        PropertiesHeading.Text = ToolLabel(activeTool);
        UpdateEditorLayout();
        syncingControls = false;
    }

    private void StrokeWidth_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncingControls || Annotations is null || StrokeWidthPicker.SelectedItem is not ComboBoxItem item) return;
        Annotations.InkWidth = double.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture);
        Annotations.ChangeSelected(annotation => annotation.Width = annotation.Tool is AnnotationTool.Arrow or AnnotationTool.NumberedArrow
            ? Annotations.InkWidth * annotation.ArrowMetricScale : Annotations.WidthForTool(annotation.Tool));
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncingControls || Annotations is null || FontSizePicker.SelectedItem is not ComboBoxItem item) return;
        Annotations.TextSize = double.Parse((string)item.Content, System.Globalization.CultureInfo.InvariantCulture);
        if (InlineText.IsVisible) InlineText.FontSize = Annotations.TextSize;
        else if (Annotations.Selected?.Tool == AnnotationTool.Text) Annotations.ChangeSelected(annotation => annotation.FontSize = Annotations.TextSize);
    }

    private void BeginNumberEdit()
    {
        if (Annotations.Selected?.Tool != AnnotationTool.NumberedArrow) return;
        editingNumberIndex = Annotations.SelectedIndex;
        NumberPicker.Focus();
        NumberPicker.SelectAll();
    }

    private void NumberPicker_GotFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        editingNumberIndex = Annotations.Selected?.Tool == AnnotationTool.NumberedArrow ? Annotations.SelectedIndex : -1;

    private void CommitNumber()
    {
        if (editingNumberIndex < 0) return;
        var index = editingNumberIndex;
        editingNumberIndex = -1;
        if (index < Annotations.Document.Items.Count && Annotations.Document.Items[index].Tool == AnnotationTool.NumberedArrow
            && int.TryParse(NumberPicker.Text, out var number) && number > 0 && number != Annotations.Document.Items[index].Number)
        {
            var before = Annotations.Document.Items[index];
            var after = before.Clone();
            after.Number = number;
            Annotations.Document.Replace(index, before, after);
        }
        NumberPicker.Text = Annotations.Selected?.Tool == AnnotationTool.NumberedArrow ? Annotations.Selected.Number.ToString() : "";
        SyncEditor();
    }

    private void NumberPicker_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitNumber();

    private void NumberPicker_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            editingNumberIndex = -1;
            NumberPicker.Text = Annotations.Selected?.Number.ToString() ?? "";
            Annotations.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitNumber();
            Annotations.Focus();
            e.Handled = true;
        }
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
    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateImageFitMargin(refit: true);
        currentZoom = MinZoom;
        UpdateZoomState();
    }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ImageViewport.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height));
    private void ImageFit_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDisplayScale();
    private void Viewport_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) { if (e.OriginalSource == ImageViewport) EndDrag(); }

    private void UpdateDisplayScale()
    {
        if (Annotations is null || ImageStage.Width <= 0) return;
        Annotations.DisplayScale = Math.Max(.01, Math.Min(Math.Max(1, ImageFit.ActualWidth) / ImageStage.Width,
            Math.Max(1, ImageFit.ActualHeight) / ImageStage.Height) * currentZoom);
        // New arrows use screen-sized metrics; committed annotations retain their creation scale.
        Annotations.ArrowScale = 1 / Annotations.DisplayScale;
        RenderOptions.SetBitmapScalingMode(ViewerImage, Annotations.DisplayScale > 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Annotations.DrawSelection();
        OcrSelectionHighlight.StrokeThickness = 1.5 / Annotations.DisplayScale;
        UpdateOcrSelectionHandles();
        FitButton.Content = $"{Annotations.DisplayScale * 100:0}%";
    }

    private void Editor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ColorPickerPopup.IsOpen) return;
        if (e.Key == Key.Escape && ocrDragAnchor >= 0)
        {
            CancelOcrSelectionDrag();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && AnnotationOptionsTray.IsVisible)
        { HideProperties_Click(sender, e); e.Handled = true; return; }
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
            if (e.Key == Key.E) { ExtractTextButton_Click(sender, e); e.Handled = true; return; }
            var tool = EditorSettingsService.Current.ToolKeys.FirstOrDefault(pair => pair.Value == e.Key);
            if (tool.Value == e.Key) ChooseTool(tool.Key);
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
