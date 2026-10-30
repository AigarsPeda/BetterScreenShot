using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Point = System.Windows.Point;
using MenuItem = System.Windows.Controls.MenuItem;

internal static class AnnotationContextMenuTests
{
    public static void Run(Action<string, Action> check, string directory)
    {
        check("Right-click targets the top annotation and clears selection on empty space", () =>
        {
            var surface = new AnnotationSurface { Width = 300, Height = 200 };
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Points = [new(10, 20), new(90, 20)] });
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Points = [new(10, 10), new(100, 80)] });
            surface.Select(0);
            Require(surface.SelectForContextMenu(new Point(50, 20)) && surface.SelectedIndex == 1);
            Require(!surface.SelectForContextMenu(new Point(280, 180)) && surface.Selected is null);
            Require(surface.Document.Items.Count == 2);
        });
        check("Duplicate preserves editable geometry and style, fits the image, and supports undo", () =>
        {
            var surface = new AnnotationSurface { Width = 300, Height = 200, DisplayScale = 2 };
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Color = Colors.Purple, Width = 7,
                Points = [new(180, 130), new(230, 100), new(300, 200)] });
            surface.Select(0);
            var original = surface.Selected!.Clone();
            surface.DuplicateSelected();
            var copy = surface.Selected!;
            Require(surface.Document.Items.Count == 2 && surface.SelectedIndex == 1);
            Require(copy.Points.Count == 3 && copy.Color == original.Color && copy.Width == original.Width && copy.SketchSeed == original.SketchSeed);
            Require(copy.Points[0] != original.Points[0] && copy.Bounds.Right <= surface.Width && copy.Bounds.Bottom <= surface.Height);
            Require(surface.Document.Items[0].Points.SequenceEqual(original.Points));
            surface.Document.Undo();
            Require(surface.Document.Items.Count == 1);
            surface.Document.Redo();
            Require(surface.Document.Items.Count == 2 && surface.Document.Items[1].Points.SequenceEqual(copy.Points));
        });
        check("Context menu quick actions edit the intended annotation and preserve undo", () =>
        {
            var pixels = Enumerable.Repeat((byte)255, 300 * 200 * 4).ToArray();
            var image = BitmapSource.Create(300, 200, 96, 96, PixelFormats.Bgr32, null, pixels, 300 * 4);
            image.Freeze();
            var path = Path.Combine(directory, "context-menu.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(path)) encoder.Save(output);
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(path), image);
            try
            {
                window.Show();
                var surface = (AnnotationSurface)window.FindName("Annotations");
                Require(window.CreateAnnotationMenu() is null);
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Points = [new(20, 80), new(100, 20), new(180, 80)] });
                surface.Select(0);
                MenuItem Action(System.Windows.Controls.ContextMenu menu, string label) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, label));
                void Click(MenuItem item)
                {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }
                var menu = window.CreateAnnotationMenu()!;
                Require(menu.Items.OfType<MenuItem>().Select(item => item.Header).SequenceEqual(new[] { "Duplicate", "Change color…", "Straighten", "Delete" }));
                Click(Action(menu, "Straighten"));
                Require(surface.Selected!.Points.Count == 2);
                surface.Document.Undo();
                Require(surface.Selected!.Points.Count == 3);
                Click(Action(window.CreateAnnotationMenu()!, "Duplicate"));
                Require(surface.Document.Items.Count == 2 && surface.SelectedIndex == 1);
                Click(Action(window.CreateAnnotationMenu()!, "Delete"));
                Require(surface.Document.Items.Count == 1);
                surface.Document.Undo();
                Require(surface.Document.Items.Count == 2);
                surface.Select(0);
                var stale = window.CreateAnnotationMenu()!;
                surface.Select(1);
                Click(Action(stale, "Delete"));
                Require(surface.Document.Items.Count == 2);
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Text, Points = [new(30, 30)], Text = "Edit me" });
                surface.Select(2);
                menu = window.CreateAnnotationMenu()!;
                Require(!menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Straighten")));
                Click(Action(menu, "Edit text…"));
                var text = (System.Windows.Controls.TextBox)window.FindName("InlineText");
                Require(text.Visibility == Visibility.Visible && text.Text == "Edit me" && text.IsKeyboardFocused);
            }
            finally { window.Close(); }
        });
    }

    public static int Preview()
    {
        var application = new System.Windows.Application();
        var pixels = Enumerable.Repeat((byte)255, 300 * 200 * 4).ToArray();
        var image = BitmapSource.Create(300, 200, 96, 96, PixelFormats.Bgr32, null, pixels, 1200);
        image.Freeze();
        var folder = Path.Combine(Environment.CurrentDirectory, "obj", "context-menu-preview");
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(folder, "unused-source.png")), image);
            ThemeIconService.ApplyViewerTheme(window.Resources, theme);
            var surface = (AnnotationSurface)window.FindName("Annotations");
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Points = [new(20, 80), new(100, 20), new(180, 80)] });
            surface.Select(0);
            var menu = window.CreateAnnotationMenu()!;
            menu.Visibility = Visibility.Visible;
            menu.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            var rendered = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rendered.Render(menu);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            var path = Path.Combine(folder, $"context-menu-{theme.ToString().ToLowerInvariant()}.png");
            using (var output = File.Create(path)) encoder.Save(output);
            Console.WriteLine(path);
            window.Close();
        }
        application.Shutdown();
        return 0;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Annotation context menu assertion failed.");
    }
}
