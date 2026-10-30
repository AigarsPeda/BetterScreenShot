using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;

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
            var image = AnnotationTests.CreateDemoImage();
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
            else throw new ArgumentException("Choose --readme-capture, --readme-preview, or --readme-editor.", nameof(view));
        }
        app.Run(window);
        return 0;
    }
}
