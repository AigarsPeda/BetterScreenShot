using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private readonly Dictionary<string, TextExtractionResult> extractedText = [];
    private TextExtractionResult? currentTextResult;
    private Point? pendingWordSelection;
    private CancellationTokenSource? textExtractionCancellation;
    private string? extractingLanguage;
    private bool copyingExtractedText;
    private bool textPanelOpen;
    private int textPanelTransition;

    private async void ExtractTextButton_Click(object sender, RoutedEventArgs e) => await ToggleTextPanelAsync();

    internal async Task ToggleTextPanelAsync()
    {
        if (textPanelOpen)
        {
            CloseTextPanel();
            return;
        }
        pendingWordSelection = null;
        ChooseTool(AnnotationTool.Select);
        await ExtractTextAsync();
    }

    private async void OcrLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (textPanelOpen)
        {
            pendingWordSelection = null;
            await ExtractTextAsync();
        }
    }

    internal async Task ExtractTextAsync()
    {
        if (isClosing) return;
        CommitText();
        Annotations.CancelGesture();
        SetTextPanelOpen(true);
        var language = (string)((ComboBoxItem)OcrLanguagePicker.SelectedItem).Tag;
        if (textExtractionCancellation is not null && extractingLanguage == language) return;
        CancelTextExtraction();
        if (extractedText.TryGetValue(language, out var cached))
        {
            ShowExtractedText(cached);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        textExtractionCancellation = cancellation;
        extractingLanguage = language;
        ExtractedTextBox.Clear();
        currentTextResult = null;
        OcrSelectionHighlight.Visibility = Visibility.Collapsed;
        CopyExtractedTextButton.IsEnabled = false;
        SetOcrStatus(language == TextExtractionService.AutomaticLanguage ? "Recognizing text automatically…" : "Recognizing text…");
        try
        {
            var text = await TextExtractionService.RecognizeAsync((BitmapSource)ViewerImage.Source, language, cancellation.Token);
            if (isClosing || cancellation.IsCancellationRequested) return;
            extractedText[language] = text;
            ShowExtractedText(text);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (isClosing || cancellation.IsCancellationRequested) return;
            pendingWordSelection = null;
            SetOcrStatus("Could not extract text. " + error.Message);
        }
        finally
        {
            if (ReferenceEquals(textExtractionCancellation, cancellation))
            {
                textExtractionCancellation = null;
                extractingLanguage = null;
            }
        }
    }

    private void ShowExtractedText(TextExtractionResult result)
    {
        currentTextResult = result;
        var text = result.Text;
        ExtractedTextBox.Text = text;
        CopyExtractedTextButton.IsEnabled = !string.IsNullOrWhiteSpace(text);
        SetOcrStatus(string.IsNullOrWhiteSpace(text)
            ? "No text found. Try a closer capture or choose a language in Advanced options."
            : null);
        if (pendingWordSelection is { } point)
        {
            pendingWordSelection = null;
            SelectRecognizedWord(point);
        }
    }

    private async void ImageStage_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Annotations.Tool != AnnotationTool.Select || Keyboard.IsKeyDown(Key.Space) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || e.OriginalSource is System.Windows.Controls.TextBox) return;
        var point = e.GetPosition(ImageStage);
        if (e.ClickCount == 2 && Annotations.AnnotationAt(point) < 0)
        {
            e.Handled = true;
            await SelectImageWordAsync(point);
        }
        else
        {
            pendingWordSelection = null;
            ExtractedTextBox.Select(0, 0);
            OcrSelectionHighlight.Visibility = Visibility.Collapsed;
        }
    }

    internal async Task SelectImageWordAsync(Point point)
    {
        pendingWordSelection = point;
        await ExtractTextAsync();
    }

    private void SelectRecognizedWord(Point point)
    {
        var word = currentTextResult?.Words.FirstOrDefault(item => item.Bounds.Contains(point));
        if (word is null)
        {
            ExtractedTextBox.Select(0, 0);
            OcrSelectionHighlight.Visibility = Visibility.Collapsed;
            SetOcrStatus("No text recognized here.");
            return;
        }
        ExtractedTextBox.Focus();
        ExtractedTextBox.Select(word.TextStart, word.Text.Length);
        ExtractedTextBox.UpdateLayout();
        var line = ExtractedTextBox.GetLineIndexFromCharacterIndex(word.TextStart);
        if (line >= 0) ExtractedTextBox.ScrollToLine(line);
        SetOcrStatus(null);
    }

    private void ExtractedText_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (OcrSelectionHighlight is null) return;
        var start = ExtractedTextBox.SelectionStart;
        var end = start + ExtractedTextBox.SelectionLength;
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (end > start && currentTextResult is { } result)
        {
            foreach (var word in result.Words)
                if (word.TextStart < end && word.TextStart + word.Text.Length > start)
                    geometry.Children.Add(new RectangleGeometry(word.Bounds));
        }
        geometry.Freeze();
        OcrSelectionHighlight.Data = geometry;
        OcrSelectionHighlight.StrokeThickness = 1.5 / Math.Max(.01, Annotations.DisplayScale);
        OcrSelectionHighlight.Visibility = geometry.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (geometry.Children.Count > 0)
        {
            SetOcrStatus(null);
            RevealSelectedText();
        }
    }

    private void RevealSelectedText()
    {
        if (!IsLoaded || OcrSelectionHighlight.Visibility != Visibility.Visible || ImageViewport.ActualWidth <= 40 || ImageViewport.ActualHeight <= 40) return;
        ImageViewport.UpdateLayout();
        var bounds = ImageStage.TransformToAncestor(ImageViewport).TransformBounds(OcrSelectionHighlight.Data.Bounds);
        const double margin = 16;
        if (bounds.Width > ImageViewport.ActualWidth - margin * 2 || bounds.Height > ImageViewport.ActualHeight - margin * 2)
        {
            currentZoom = MinZoom;
            UpdateZoomState();
            ImageViewport.UpdateLayout();
            bounds = ImageStage.TransformToAncestor(ImageViewport).TransformBounds(OcrSelectionHighlight.Data.Bounds);
        }
        ImageTranslateTransform.X += bounds.Left < margin ? margin - bounds.Left
            : bounds.Right > ImageViewport.ActualWidth - margin ? ImageViewport.ActualWidth - margin - bounds.Right : 0;
        ImageTranslateTransform.Y += bounds.Top < margin ? margin - bounds.Top
            : bounds.Bottom > ImageViewport.ActualHeight - margin ? ImageViewport.ActualHeight - margin - bounds.Bottom : 0;
    }

    private void SetOcrStatus(string? message)
    {
        OcrStatus.Text = message ?? "";
        OcrStatus.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void SetTextPanelOpen(bool open)
    {
        if (textPanelOpen == open) return;
        textPanelOpen = open;
        ExtractTextButton.ToolTip = open ? "Hide extracted text (Ctrl+Shift+T)" : "Extract text from the original screenshot (Ctrl+Shift+T)";
        var transition = ++textPanelTransition;
        var fromWidth = ExtractedTextHost.Width;
        var fromSlide = ExtractedTextSlide.X;
        var targetWidth = open ? 312d : 0;
        var targetSlide = open ? 0 : 312d;
        ExtractedTextHost.BeginAnimation(WidthProperty, null);
        ExtractedTextSlide.BeginAnimation(TranslateTransform.XProperty, null);
        ExtractedTextHost.Width = targetWidth;
        ExtractedTextSlide.X = targetSlide;
        ExtractedTextHost.Visibility = Visibility.Visible;
        ExtractedTextHost.IsHitTestVisible = open;

        void Finish()
        {
            if (textPanelTransition != transition || isClosing) return;
            ExtractedTextHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) RevealSelectedText();
        }

        if (!IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            Finish();
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var width = new DoubleAnimation(fromWidth, targetWidth, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var slide = new DoubleAnimation(fromSlide, targetSlide, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        width.Completed += (_, _) => Finish();
        ExtractedTextHost.BeginAnimation(WidthProperty, width, HandoffBehavior.SnapshotAndReplace);
        ExtractedTextSlide.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }

    private void CancelTextExtraction()
    {
        textExtractionCancellation?.Cancel();
        textExtractionCancellation = null;
        extractingLanguage = null;
    }

    private void CloseExtractedText_Click(object sender, RoutedEventArgs e) => CloseTextPanel();

    private void CloseTextPanel()
    {
        CancelTextExtraction();
        pendingWordSelection = null;
        ExtractedTextBox.Select(0, 0);
        OcrSelectionHighlight.Visibility = Visibility.Collapsed;
        SetTextPanelOpen(false);
    }

    private async void CopyExtractedText_Click(object sender, RoutedEventArgs e)
    {
        if (copyingExtractedText || string.IsNullOrWhiteSpace(ExtractedTextBox.Text)) return;
        copyingExtractedText = true;
        var text = ExtractedTextBox.Text;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                if (isClosing) return;
                try { System.Windows.Clipboard.SetText(text); break; }
                catch (COMException error) when (error.ErrorCode == unchecked((int)0x800401D0) && attempt < 4)
                {
                    await Task.Delay(75);
                }
            }
            SetOcrStatus(null);
        }
        catch (Exception error)
        {
            if (!isClosing) SetOcrStatus("Could not copy text. " + error.Message);
        }
        finally { copyingExtractedText = false; }
    }
}
