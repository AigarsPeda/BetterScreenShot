using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Brushes = System.Windows.Media.Brushes;

internal static class TextExtractionTests
{
    internal static int Demo(bool longText = false, bool selectionHandles = false)
    {
        var app = new System.Windows.Application();
        var image = selectionHandles ? Fixture("Select words on this line\nand continue on the next line.")
            : longText ? Fixture(string.Join(Environment.NewLine, Enumerable.Range(1, 22).Select(line => $"Line {line:D2} Select these words.")), height: 1400) : Fixture();
        var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(Path.GetTempPath(), "BetterScreenShot-ocr-demo-unused.png")), image)
            { Title = selectionHandles ? "Text selection handles" : "Text selection test" };
        app.Run(window);
        return 0;
    }

    internal static int Smoke()
    {
        var text = TextExtractionService.ExtractAsync(Fixture(), TextExtractionService.AutomaticLanguage).GetAwaiter().GetResult();
        Console.WriteLine(text);
        return text.Contains("Screenshot text 12345") ? 0 : 1;
    }

    internal static int Preview()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var directory = Path.Combine(Environment.CurrentDirectory, "obj", "ocr-preview");
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        foreach (var width in new[] { 620, 1100 })
        {
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "unused.png")), AnnotationTests.CreateDemoImage())
                { Width = width, Height = 740 };
            var previousContext = SynchronizationContext.Current;
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                Complete(window.ExtractTextAsync());
                Complete(Task.Delay(250));
                var extracted = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                var phrase = extracted.Text.IndexOf("clear plan", StringComparison.Ordinal);
                if (phrase >= 0) extracted.Select(phrase, "clear plan. A confident team.".Length);
                ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle((System.Windows.Media.Brush)window.FindResource("ViewerPageBackgroundBrush"), null, new Rect(content.RenderSize));
                    drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
                }
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.Combine(directory, $"ocr-{theme.ToString().ToLowerInvariant()}-{width}.png");
                using var output = File.Create(path);
                encoder.Save(output);
                Console.WriteLine(path);
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
        app.Shutdown();
        return 0;
    }

    internal static void Run(Action<string, Action> check, string directory)
    {
        OcrAssetTests.Run(check, directory);
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        OcrLayoutTests.Run(check, directory);
        TextSelectionTests.Run(check, directory);
        check("Tesseract recognizes English, dark backgrounds and Latvian diacritics from memory", () =>
        {
            foreach (var dark in new[] { false, true })
            {
                var text = TextExtractionService.ExtractAsync(Fixture(dark: dark), "eng").GetAwaiter().GetResult();
                Require(text.Contains("Screenshot text", StringComparison.OrdinalIgnoreCase), text);
                Require(text.Contains("12345"), text);
            }
            var latvian = TextExtractionService.ExtractAsync(Fixture("Labdien! Rīga, Latvija."), "lav").GetAwaiter().GetResult();
            Require(latvian.Contains("Rīga") && latvian.Contains("Latvija"), latvian);
        });
        check("Short UI text gets a sparse-layout retry without losing word positions", () =>
        {
            var passes = 0;
            var result = TextExtractionService.RecognizeAsync(Fixture("Hello"), "eng",
                reportTiming: timing => { if (timing.Stage == "recognize") Interlocked.Increment(ref passes); }).GetAwaiter().GetResult();
            Require(passes >= 2, "Short text should get a sparse-layout retry.");
            Require(result.Text.Contains("Hello", StringComparison.OrdinalIgnoreCase), result.Text);
            Require(result.Words.All(word => result.Text.Substring(word.TextStart, word.Text.Length) == word.Text),
                "Sparse-layout words must keep valid text offsets.");
        });
        check("Added OCR models recognize Lithuanian, Estonian, Swedish, Finnish and Russian", () =>
        {
            foreach (var (language, sample, expected) in new[]
            {
                ("lit", "Labas rytas! Lietuva.", "Lietuva"), ("est", "Tere! Eesti.", "Eesti"),
                ("swe", "Sverige är vackert.", "är"), ("fin", "Hyvää päivää! Suomi.", "Hyvää"),
                ("rus", "Привет! Россия.", "Россия")
            })
            {
                var result = TextExtractionService.RecognizeAsync(Fixture(sample), language).GetAwaiter().GetResult();
                Require(result.Text.Contains(expected, StringComparison.Ordinal), $"{language}: {result.Text}");
                Require(result.Words.Any(word => word.Text.Contains(expected, StringComparison.Ordinal)), "Words must retain Unicode text for image selection.");
            }
        });
        check("Auto recognizes all seven languages and mixed Latin and Cyrillic text", () =>
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var failures = new List<string>();
            foreach (var (sample, expected) in new[]
            {
                ("Screenshot text 12345", "Screenshot"), ("Labdien! Rīga, Latvija.", "Rīga"),
                ("Labas rytas! Lietuva.", "Lietuva"), ("Tere! Eesti.", "Eesti"),
                ("Sverige är vackert.", "är"), ("Hyvää päivää! Suomi.", "Hyvää"),
                ("Привет! Россия.", "Россия"), ("Hello! Rīga, Россия.", "Rīga")
            })
            {
                var result = TextExtractionService.RecognizeAsync(Fixture(sample), TextExtractionService.AutomaticLanguage).GetAwaiter().GetResult();
                if (!result.Text.Contains(expected, StringComparison.Ordinal)) failures.Add($"Auto: {sample} => {result.Text}");
                if (sample.StartsWith("Hello", StringComparison.Ordinal))
                    if (!result.Text.Contains("Hello") || !result.Text.Contains("Россия")) failures.Add("Auto should recognize both scripts in the same image. " + result.Text);
                foreach (var word in result.Words)
                    Require(result.Text.Substring(word.TextStart, word.Text.Length) == word.Text, "Auto must preserve word offsets for image highlighting.");
            }
            Console.WriteLine($"Auto recognition: eight synthetic samples completed in {elapsed.ElapsedMilliseconds} ms.");
            Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        });
        check("OCR returns no text for blank images and honors cancellation", () =>
        {
            Require(TextExtractionService.ExtractAsync(Fixture(""), "eng").GetAwaiter().GetResult() == "", "Blank image should have no text.");
            Require(TextExtractionService.ExtractAsync(Fixture(""), TextExtractionService.AutomaticLanguage).GetAwaiter().GetResult() == "", "Auto should return no text for blank images.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                TextExtractionService.ExtractAsync(Fixture(), "eng", cancellation.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
        });
        check("Auto preserves small multilingual text after choosing a working image size", () =>
        {
            foreach (var (sample, expected) in new[]
            {
                ("Labdien! Rīga, Latvija.", "Rīga"), ("Labas rytas! Lietuva.", "Lietuva"),
                ("Tere! Eesti.", "Eesti"), ("Sverige är vackert.", "är"),
                ("Hyvää päivää! Suomi.", "Hyvää"), ("Привет! Россия.", "Россия"),
                ("Hello! Rīga, Россия.", "Россия")
            })
            {
                var result = TextExtractionService.RecognizeAsync(Fixture(sample, fontSize: 16), TextExtractionService.AutomaticLanguage).GetAwaiter().GetResult();
                Require(result.Text.Contains(expected, StringComparison.Ordinal), $"{sample} => {result.Text}");
                if (sample.StartsWith("Hello", StringComparison.Ordinal))
                    Require(result.Text.Contains("Hello") && result.Text.Contains("Rīga"), "Mixed scripts and accents must survive the size probe: " + result.Text);
                foreach (var word in result.Words)
                    Require(result.Text.Substring(word.TextStart, word.Text.Length) == word.Text && word.Bounds.Height < 20,
                        "Small-text offsets and bounds must remain in original image coordinates.");
            }
        });
        check("Cancelled queued OCR requests do not start native work or disturb the active request", () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var image = Fixture();
            var active = TextExtractionService.RecognizeAsync(image, "eng", reportTiming: timing =>
            {
                if (timing.Stage != "assets") return;
                entered.Set();
                Require(release.Wait(TimeSpan.FromSeconds(10)), "Active OCR test was not released.");
            });
            try
            {
                Require(entered.Wait(TimeSpan.FromSeconds(10)), "Active OCR did not start.");
                using var cancellation = new CancellationTokenSource();
                var queuedCallbacks = 0;
                var queued = TextExtractionService.RecognizeAsync(image, "eng", cancellation.Token,
                    _ => Interlocked.Increment(ref queuedCallbacks));
                cancellation.Cancel();
                try
                {
                    queued.GetAwaiter().GetResult();
                    throw new InvalidOperationException("Queued OCR must honor cancellation.");
                }
                catch (OperationCanceledException) { }
                Require(queuedCallbacks == 0, "Cancelled queued OCR must not prepare assets or start an engine.");
            }
            finally { release.Set(); }
            Require(active.GetAwaiter().GetResult().Text.Contains("Screenshot text"), "The active request must finish normally.");
        });
        check("OCR word bounds and UTF-16 offsets identify repeated words and Latvian punctuation", () =>
        {
            foreach (var (text, language) in new[] { ("Screenshot word word 12345", "eng"), ("Labdien! Rīga, Latvija.", "lav") })
            {
                var image = Fixture(text);
                var result = TextExtractionService.RecognizeAsync(image, language).GetAwaiter().GetResult();
                Require(result.Words.Count >= 3, result.Text);
                foreach (var word in result.Words)
                {
                    Require(result.Text.Substring(word.TextStart, word.Text.Length) == word.Text, word.Text);
                    Require(new Rect(0, 0, image.PixelWidth, image.PixelHeight).Contains(word.Bounds), "Word bounds must use image pixels.");
                    Require(word.Bounds.Width > 0 && word.Bounds.Height > 0, "Word bounds must not be empty.");
                }
                if (language == "eng")
                {
                    var repeated = result.Words.Where(word => word.Text == "word").ToArray();
                    Require(repeated.Length == 2 && repeated[0].TextStart < repeated[1].TextStart && repeated[0].Bounds.Left < repeated[1].Bounds.Left,
                        "Repeated words must have distinct positions and text offsets.");
                }
            }
        });
        check("Selecting an image word starts OCR, highlights it and focuses just that word for Copy", () =>
        {
            var image = Fixture();
            var word = TextExtractionService.RecognizeAsync(image, "eng").GetAwaiter().GetResult().Words.Single(item => item.Text == "text");
            var previousContext = SynchronizationContext.Current;
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "word-selection.png")), image);
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                Complete(window.SelectImageWordAsync(new System.Windows.Point(word.Bounds.Left + word.Bounds.Width / 2, word.Bounds.Top + word.Bounds.Height / 2)));
                var textBox = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                var highlight = (System.Windows.Shapes.Path)window.FindName("OcrSelectionHighlight");
                Require(textBox.SelectedText == "text" && textBox.IsKeyboardFocused, "Ctrl+C should target only the selected word.");
                Require(highlight.Visibility == Visibility.Visible && highlight.Data.Bounds == word.Bounds, "Highlight must match the source word.");
                var surface = (AnnotationSurface)window.FindName("Annotations");
                Require(surface.Document.Items.Count == 0 && !surface.Document.CanUndo, "Text selection must not create an annotation.");
                textBox.Select(0, "Screenshot".Length);
                Require(highlight.Visibility == Visibility.Visible && highlight.Data.Bounds.Width != word.Bounds.Width, "Panel selection should also highlight its image word.");
                textBox.SelectAll();
                Require(highlight.Visibility == Visibility.Visible && ((GeometryGroup)highlight.Data).Children.Count == 3, "Selecting all text should highlight all three image words.");
                Complete(window.SelectImageWordAsync(new System.Windows.Point(800, 230)));
                Require(textBox.SelectionLength == 0 && highlight.Visibility == Visibility.Collapsed, "Double-clicking blank space should clear the word selection.");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
        check("Panel selections highlight every overlapping image word across lines and reveal zoomed selections", () =>
        {
            var previousContext = SynchronizationContext.Current;
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "range-selection.png")), Fixture("First selected line\nSecond selected line"));
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                Complete(window.ExtractTextAsync());
                Complete(Task.Delay(250));
                var textBox = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                var highlight = (System.Windows.Shapes.Path)window.FindName("OcrSelectionHighlight");
                var start = textBox.Text.IndexOf("selected", StringComparison.Ordinal);
                var end = textBox.Text.LastIndexOf("selected", StringComparison.Ordinal) + "selected".Length;
                Require(start >= 0 && end > start, textBox.Text);
                textBox.Select(start, end - start);
                var geometry = (GeometryGroup)highlight.Data;
                Require(geometry.Children.Count == 4 && geometry.Children.Select(item => item.Bounds.Top).Distinct().Count() == 2,
                    "The four selected words on both lines should be highlighted.");
                textBox.Select(start + 1, 2);
                Require(((GeometryGroup)highlight.Data).Children.Count == 1, "A partial word selection should highlight its image word.");
                var shift = (TranslateTransform)window.FindName("ImageTranslateTransform");
                var scale = (ScaleTransform)window.FindName("ImageScaleTransform");
                scale.ScaleX = scale.ScaleY = 3;
                shift.X = -2000;
                shift.Y = -1500;
                textBox.Select(start, "selected".Length);
                var stage = (FrameworkElement)window.FindName("ImageStage");
                var viewport = (FrameworkElement)window.FindName("ImageViewport");
                window.UpdateLayout();
                var visible = stage.TransformToAncestor(viewport).TransformBounds(highlight.Data.Bounds);
                Require(visible.Left >= 0 && visible.Top >= 0 && visible.Right <= viewport.ActualWidth && visible.Bottom <= viewport.ActualHeight,
                    "Selecting text in the panel should reveal a word panned outside the image viewport.");
                textBox.Select(5, 1);
                Require(highlight.Visibility == Visibility.Collapsed, "Selecting only a space should clear image highlights.");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
        check("Extract text toggles the panel during recognition and reopens cached results", () =>
        {
            var previousContext = SynchronizationContext.Current;
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "panel-toggle.png")), Fixture());
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                var button = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("ExtractTextButton");
                var host = (FrameworkElement)window.FindName("ExtractedTextHost");
                var textBox = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                var pending = window.ToggleTextPanelAsync();
                Require(button.IsEnabled && !pending.IsCompleted, "The toggle should remain clickable while Auto is recognizing text.");
                Require(button.IsChecked == true, "The toolbar icon should show when extracted text is open.");
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Complete(pending);
                Complete(Task.Delay(250));
                Require(host.Visibility == Visibility.Collapsed && textBox.Text == "", "A second click must close the panel and ignore the pending result.");
                Complete(window.ToggleTextPanelAsync());
                Require(textBox.Text.Contains("Screenshot text"), "Opening the panel again should recognize the image.");
                Complete(window.ToggleTextPanelAsync());
                Complete(Task.Delay(250));
                Require(host.Visibility == Visibility.Collapsed, "The toggle must close an already populated panel.");
                Require(window.ToggleTextPanelAsync().IsCompletedSuccessfully, "Reopening should reuse cached Auto recognition.");
                Complete(Task.Delay(250));
                Require(host.Visibility == Visibility.Visible && textBox.Text.Contains("Screenshot text"), "The cached text should reappear when reopened.");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
        check("Text panel transitions settle correctly when opening and closing are interrupted", () =>
        {
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "panel-animation.png")), Fixture());
            try
            {
                window.Show();
                var host = (FrameworkElement)window.FindName("ExtractedTextHost");
                Require(host.Visibility == Visibility.Collapsed && host.Width == 0, "Panel should start closed.");
                window.SetTextPanelOpen(true);
                Complete(Task.Delay(60));
                window.SetTextPanelOpen(false);
                Complete(Task.Delay(60));
                window.SetTextPanelOpen(true);
                Complete(Task.Delay(250));
                Require(host.Visibility == Visibility.Visible && Math.Abs(host.Width - 312) < .1, "Reopening must cancel the pending close transition.");
                window.SetTextPanelOpen(false);
                Complete(Task.Delay(250));
                Require(host.Visibility == Visibility.Collapsed && host.Width == 0, "Closing should reclaim all panel space.");
            }
            finally { window.Close(); }
        });
        check("The narrow OCR scrollbar supports paging and dragging without changing the theme", () =>
        {
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "scrollbar.png")), Fixture()) { Height = 500 };
            try
            {
                window.Show();
                window.SetTextPanelOpen(true);
                Complete(Task.Delay(250));
                var textBox = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                textBox.Text = string.Join(Environment.NewLine, Enumerable.Range(1, 100).Select(line => $"Scrollable sample line {line}"));
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                    window.UpdateLayout();
                    var viewer = Descendants<ScrollViewer>(textBox).Single();
                    var bar = Descendants<System.Windows.Controls.Primitives.ScrollBar>(viewer).Single(item => item.Orientation == System.Windows.Controls.Orientation.Vertical);
                    Require(Math.Abs(bar.ActualWidth - 8) < .1 && viewer.ScrollableHeight > 0,
                        $"The visible scrollbar must use the narrow style. Width: {bar.ActualWidth}, scrollable height: {viewer.ScrollableHeight}.");
                    viewer.ScrollToTop();
                    window.UpdateLayout();
                    System.Windows.Controls.Primitives.ScrollBar.PageDownCommand.Execute(null, bar);
                    window.UpdateLayout();
                    Require(viewer.VerticalOffset > 0, "Clicking the track must page down.");
                    var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
                    var before = viewer.VerticalOffset;
                    track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 20)
                        { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                    window.UpdateLayout();
                    Require(viewer.VerticalOffset > before, "Dragging the thumb must scroll the extracted text.");
                }
            }
            finally { window.Close(); }
        });
        check("Editor OCR uses the original image, caches results, switches language and keeps text copy separate", () =>
        {
            var previousContext = SynchronizationContext.Current;
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "ocr.png")), Fixture());
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                var surface = (AnnotationSurface)window.FindName("Annotations");
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Text, Text = "ADDED MARKUP", FontSize = 36, Points = [new(20, 180)] });
                var task = window.ExtractTextAsync();
                Require(((System.Windows.Controls.Primitives.ToggleButton)window.FindName("ExtractTextButton")).IsEnabled, "The panel toggle must remain available during recognition.");
                Require(!((System.Windows.Controls.Button)window.FindName("CopyExtractedTextButton")).IsEnabled, "Copy all should wait for the recognition result.");
                Require(window.ExtractTextAsync().IsCompletedSuccessfully, "Repeated shortcuts should not restart active recognition.");
                Complete(task);
                var textBox = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                Require(textBox.Text.Contains("Screenshot text") && !textBox.Text.Contains("ADDED MARKUP"), textBox.Text);
                Require(((System.Windows.Controls.Button)window.FindName("CopyExtractedTextButton")).IsEnabled, "Copy all should be enabled.");
                Require(((System.Windows.Controls.Button)window.FindName("CopyButton")).IsEnabled, "Image copy should remain available.");
                Require(window.ExtractTextAsync().IsCompletedSuccessfully, "Repeated extraction should use cached text.");
                var languages = (System.Windows.Controls.ComboBox)window.FindName("OcrLanguagePicker");
                var advanced = (Expander)window.FindName("OcrAdvancedOptions");
                Require(languages.Items.Count == 9, "The advanced picker should offer Auto, all seven languages and English + Latvian.");
                Require((string)((ComboBoxItem)languages.SelectedItem).Tag == TextExtractionService.AutomaticLanguage, "A new editor must default to Auto.");
                Require(!advanced.IsExpanded && !languages.IsVisible, "Language controls must be hidden until Advanced options is expanded.");
                advanced.IsExpanded = true;
                window.UpdateLayout();
                Require(languages.IsVisible, "Advanced options should reveal the manual language picker.");
                // Switch while hidden so the test can await the extraction directly.
                window.SetTextPanelOpen(false);
                languages.SelectedItem = languages.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "eng+lav");
                Complete(window.ExtractTextAsync());
                Require(textBox.Text.Contains("Screenshot text"), textBox.Text);
                languages.SelectedItem = languages.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == TextExtractionService.AutomaticLanguage);
                Require(window.ExtractTextAsync().IsCompletedSuccessfully, "Returning to Auto should reuse its cached result.");
                Require(textBox.Text.Contains("Screenshot text"), textBox.Text);
                textBox.Focus();
                textBox.Select(0, 10);
                Require(System.Windows.Input.ApplicationCommands.Copy.CanExecute(null, textBox), "Normal text selection should support Copy.");
                Require(surface.Document.Items.Count == 1 && surface.Document.CanUndo, "OCR should preserve editable annotations.");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
        check("Closing the editor during OCR ignores the pending result", () =>
        {
            var previousContext = SynchronizationContext.Current;
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "closing-ocr.png")), Fixture());
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                var pending = window.ExtractTextAsync();
                window.Close();
                Complete(pending);
                Require(((System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox")).Text == "", "Closed editor should ignore pending OCR.");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
    }

    internal static BitmapSource Fixture(string text = "Screenshot text 12345", bool dark = false, int height = 260, double fontSize = 42)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(dark ? Brushes.Black : Brushes.White, null, new Rect(0, 0, 850, height));
            drawing.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, dark ? Brushes.White : Brushes.Black, 1), new System.Windows.Point(30, 40));
        }
        var image = new RenderTargetBitmap(850, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    internal static void Complete(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var timeout = DateTime.UtcNow.AddSeconds(30);
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= timeout) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Require(task.IsCompleted, "OCR did not finish within 30 seconds.");
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
