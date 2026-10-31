using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;
using Brushes = System.Windows.Media.Brushes;

internal static class ReadmeScreenshots
{
    // Documentation fixtures use sample content and isolated preferences, never the user's desktop.
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
