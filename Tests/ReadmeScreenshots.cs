using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Brushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

internal static class ReadmeScreenshots
{
    // Documentation fixtures use sample content and isolated preferences, never the user's desktop.
    public static int Generate()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var output = Path.Combine(Environment.CurrentDirectory, "docs", "screenshots");
        var fixtures = Path.Combine(Environment.CurrentDirectory, "obj", "readme-demo");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(fixtures);
        var sample = AnnotationTests.CreateDemoImage();

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var suffix = theme.ToString().ToLowerInvariant();
            var launcher = new MainWindow(new CaptureShortcutSettingsStore(Path.Combine(fixtures, "shortcuts.json")));
            typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(launcher, [theme]);
            ((System.Windows.Shapes.Path)launcher.FindName("ThemeGlyphPath")).Data = ThemeIconService.ThemeGlyph(theme);
            RenderOffscreen(launcher, 520, 390, Path.Combine(output, $"launcher-{suffix}.png"), theme,
                "BetterScreenShot");
            launcher.Close();

            var toastSource = WriteFixture(fixtures, $"toast-{suffix}", sample);
            var toast = new ScreenshotToastWindow(toastSource, System.Windows.Forms.Screen.PrimaryScreen!.Bounds, sample);
            typeof(ScreenshotToastWindow).GetMethod("ApplyToastTheme", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [typeof(AppTheme)], null)!.Invoke(toast, [theme]);
            RenderOffscreen(toast, 310, 215, Path.Combine(output, $"preview-{suffix}.png"), theme);
            toast.Close();

            var editorSource = WriteFixture(fixtures, $"editor-{suffix}", sample);
            var editor = new ScreenshotViewerWindow(new TemporaryScreenshot(editorSource), sample);
            ThemeIconService.ApplyViewerTheme(editor.Resources, theme);
            var surface = (AnnotationSurface)editor.FindName("Annotations");
            AddDemoAnnotations(surface);
            RenderOffscreen(editor, 1360, 800, Path.Combine(output, $"editor-{suffix}.png"), theme,
                "Screenshot Editor",
                afterLayout: () => surface.Select(2));
            editor.Close();

            RenderTextView(theme, sample, fixtures, Path.Combine(output, $"text-recognition-{suffix}.png"),
                "clear plan", "confident team.");
            RenderTextView(theme, TextSelectionImage(), fixtures, Path.Combine(output, $"text-selection-{suffix}.png"),
                "Select the words", "next line.");
        }

        app.Shutdown();
        Console.WriteLine($"Generated 10 screenshots in {output}");
        return 0;
    }

    private static void AddDemoAnnotations(AnnotationSurface surface)
    {
        surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Highlight, Color = Colors.Yellow, Width = 20,
            Points = [new(80, 165), new(450, 165)] });
        surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Color = Colors.DodgerBlue, Width = 4,
            Points = [new(65, 215), new(425, 425)], SketchSeed = 42 });
        surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Color = Colors.Red, Width = 7,
            Points = [new(780, 120), new(670, 140), new(600, 245)], SketchSeed = 71 });
        surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Text, Color = Colors.MediumPurple, FontSize = 28,
            Text = "Ready to share", Points = [new(810, 495)] });
    }

    private static void RenderTextView(AppTheme theme, BitmapSource image, string fixtures, string output,
        string first, string last)
    {
        var source = WriteFixture(fixtures, Path.GetFileNameWithoutExtension(output), image);
        var editor = new ScreenshotViewerWindow(new TemporaryScreenshot(source), image)
        {
            Width = 1360, Height = 800, Left = -20000, Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false
        };
        try
        {
            editor.Show();
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(editor.Dispatcher));
                TextExtractionTests.Complete(editor.ExtractTextAsync());
                TextExtractionTests.Complete(Task.Delay(300));
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            var textBox = (System.Windows.Controls.TextBox)editor.FindName("ExtractedTextBox");
            var start = textBox.Text.IndexOf(first, StringComparison.Ordinal);
            var end = textBox.Text.IndexOf(last, StringComparison.Ordinal);
            if (start < 0 || end < start)
                throw new InvalidOperationException("The documentation passage was not recognized: " + textBox.Text);
            textBox.IsInactiveSelectionHighlightEnabled = true;
            textBox.Select(start, end + last.Length - start);
            textBox.Focus();
            ThemeIconService.ApplyViewerTheme(editor.Resources, theme);
            editor.UpdateLayout();
            RenderContent(editor, output, theme, "Screenshot Editor");
        }
        finally { editor.Close(); }
    }

    private static string WriteFixture(string folder, string name, BitmapSource image)
    {
        var path = Path.Combine(folder, name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static void RenderOffscreen(Window window, int width, int height, string output, AppTheme theme,
        string? title = null, Action? afterLayout = null)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        afterLayout?.Invoke();
        content.UpdateLayout();
        RenderContent(window, output, theme, title);
    }

    private static void RenderContent(Window window, string output, AppTheme theme, string? title = null)
    {
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        var margin = title is null ? 12 : 0;
        var captionHeight = title is null ? 0 : 32;
        var bounds = new Rect(margin, margin, width, height + captionHeight);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (title is not null)
            {
                drawing.PushClip(new RectangleGeometry(bounds, 10, 10));
                drawing.DrawRectangle(new SolidColorBrush(theme == AppTheme.Dark
                    ? MediaColor.FromRgb(32, 33, 36) : MediaColor.FromRgb(250, 250, 250)), null, bounds);
                DrawTitleBar(drawing, title, theme, width);
            }
            drawing.DrawRectangle(new VisualBrush(content), null,
                new Rect(margin, margin + captionHeight, width, height));
            if (title is not null) drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(width + margin * 2, height + captionHeight + margin * 2,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
        Console.WriteLine(output);
    }

    private static void DrawTitleBar(DrawingContext drawing, string title, AppTheme theme, int width)
    {
        var foreground = new SolidColorBrush(theme == AppTheme.Dark
            ? MediaColor.FromRgb(240, 240, 246) : MediaColor.FromRgb(37, 39, 53));
        drawing.DrawImage(ThemeIconService.CreateScanIcon(theme), new Rect(10, 8, 16, 16));
        drawing.DrawText(new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, foreground, 1), new WpfPoint(33, 8));
        var pen = new MediaPen(foreground, 1);
        drawing.DrawLine(pen, new WpfPoint(width - 112, 16), new WpfPoint(width - 100, 16));
        drawing.DrawRectangle(null, pen, new Rect(width - 73, 11, 9, 9));
        drawing.DrawLine(pen, new WpfPoint(width - 30, 11), new WpfPoint(width - 20, 21));
        drawing.DrawLine(pen, new WpfPoint(width - 20, 11), new WpfPoint(width - 30, 21));
    }

    public static int Demo(string view)
    {
        var app = new System.Windows.Application();
        var folder = Path.Combine(Environment.CurrentDirectory, "obj", "readme-demo");
        Directory.CreateDirectory(folder);
        Window window;
        if (view == "--readme-capture")
        {
            window = new MainWindow(new CaptureShortcutSettingsStore(Path.Combine(folder, "shortcuts.json")))
                { Title = "BetterScreenShot" };
        }
        else
        {
            var image = view == "--readme-text-selection" ? TextSelectionImage() : AnnotationTests.CreateDemoImage();
            var path = Path.Combine(folder, view + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(path)) encoder.Save(output);
            if (view == "--readme-preview")
            {
                window = new ScreenshotToastWindow(path, System.Windows.Forms.Screen.PrimaryScreen!.Bounds, image)
                    { Title = "Screenshot preview", ShowInTaskbar = true };
            }
            else if (view == "--readme-editor")
            {
                var editor = new ScreenshotViewerWindow(new TemporaryScreenshot(path), image) { Width = 1280, Height = 850 };
                var surface = (AnnotationSurface)editor.FindName("Annotations");
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Highlight, Color = Colors.Yellow, Width = 20,
                    Points = [new(80, 165), new(450, 165)] });
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Color = Colors.DodgerBlue, Width = 4,
                    Points = [new(65, 215), new(425, 425)], SketchSeed = 42 });
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Color = Colors.Red, Width = 4,
                    Points = [new(780, 120), new(670, 140), new(600, 245)], SketchSeed = 71 });
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Text, Color = Colors.MediumPurple, FontSize = 28,
                    Text = "Ready to share", Points = [new(810, 495)] });
                surface.Select(2);
                window = editor;
            }
            else if (view is "--readme-text-recognition" or "--readme-text-selection")
            {
                var editor = new ScreenshotViewerWindow(new TemporaryScreenshot(path), image) { Width = 1280, Height = 850 };
                editor.ContentRendered += async (_, _) =>
                {
                    await editor.ExtractTextAsync();
                    var textBox = (System.Windows.Controls.TextBox)editor.FindName("ExtractedTextBox");
                    var first = view == "--readme-text-selection" ? "Select the words" : "clear plan";
                    var last = view == "--readme-text-selection" ? "next line." : "confident team.";
                    var start = textBox.Text.IndexOf(first, StringComparison.Ordinal);
                    var end = textBox.Text.IndexOf(last, StringComparison.Ordinal);
                    if (start < 0 || end < start) throw new InvalidOperationException("The documentation passage was not recognized: " + textBox.Text);
                    textBox.Select(start, end + last.Length - start);
                    textBox.Focus();
                };
                window = editor;
            }
            else throw new ArgumentException("Choose --readme-capture, --readme-preview, --readme-editor, --readme-text-recognition, or --readme-text-selection.", nameof(view));
        }
        app.Run(window);
        return 0;
    }

    private static BitmapSource TextSelectionImage()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1000, 620));
            void Text(string text, double y, double size, System.Windows.Media.Brush brush) => drawing.DrawText(
                new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), size, brush, 1), new System.Windows.Point(60, y));
            Text("Meeting notes", 48, 42, Brushes.Black);
            Text("Keep the part you need", 110, 22, Brushes.SlateGray);
            drawing.DrawRectangle(Brushes.Gainsboro, null, new Rect(60, 158, 880, 2));
            Text("Select the words you need,", 190, 30, Brushes.Black);
            Text("even when they continue on the next line.", 238, 30, Brushes.Black);
            Text("Labdien! Rīga, Latvija.", 350, 28, Brushes.Black);
            Text("Copy a word or a whole sentence.", 405, 28, Brushes.Black);
            Text("Notes for the next meeting", 530, 22, Brushes.SlateGray);
        }
        var image = new RenderTargetBitmap(1000, 620, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }
}
