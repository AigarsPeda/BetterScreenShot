using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfSize = System.Windows.Size;

internal static class CaptureUiPreview
{
    public static int Render()
    {
        var app = new System.Windows.Application();
        var directory = Path.Combine(Environment.CurrentDirectory, "obj", "capture-ui-preview");
        Directory.CreateDirectory(directory);
        var image = AnnotationTests.CreateDemoImage();

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var suffix = theme.ToString().ToLowerInvariant();
            var launcher = new MainWindow(new CaptureShortcutSettingsStore(Path.Combine(directory, $"shortcuts-{suffix}.json")));
            typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(launcher, new object[] { theme });
            var neutral = (MediaBrush)launcher.FindResource("StatusNeutralTextBrush");
            ((TextBlock)launcher.FindName("StatusTextBlock")).Foreground = neutral;
            ((System.Windows.Shapes.Ellipse)launcher.FindName("StatusDot")).Fill =
                (MediaBrush)launcher.FindResource("AccentBrush");
            SaveWindow(launcher, 520, 320, Path.Combine(directory, $"launcher-{suffix}.png"));
            launcher.Close();

            var fixturePath = Path.Combine(directory, $"sample-{suffix}.png");
            SavePng(fixturePath, image);
            var toast = new ScreenshotToastWindow(fixturePath, System.Windows.Forms.Screen.PrimaryScreen!.Bounds, image);
            typeof(ScreenshotToastWindow).GetMethod("ApplyToastTheme", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(AppTheme) }, null)!
                .Invoke(toast, new object[] { theme });
            SaveWindow(toast, 310, 215, Path.Combine(directory, $"toast-{suffix}.png"),
                theme == AppTheme.Dark ? MediaColor.FromRgb(52, 58, 66) : MediaColor.FromRgb(220, 225, 224));
            toast.Close();
        }

        app.Shutdown();
        Console.WriteLine(directory);
        return 0;
    }

    private static void SaveWindow(Window window, int width, int height, string path, MediaColor? backdrop = null)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new WpfSize(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var margin = backdrop is null ? 0 : 20;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (backdrop is MediaColor color)
            {
                drawing.DrawRectangle(new SolidColorBrush(color), null,
                    new Rect(0, 0, width + margin * 2, height + margin * 2));
            }
            else
            {
                drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            }
            drawing.DrawRectangle(new VisualBrush(content), null,
                new Rect(margin, margin, width, height));
        }

        var bitmap = new RenderTargetBitmap(width + margin * 2, height + margin * 2,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(path, bitmap);
    }

    private static void SavePng(string path, BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
