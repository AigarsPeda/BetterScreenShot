using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

internal static class AnnotationTests
{
    public static void Run(Action<string, Action> check, string directory)
    {
        check("Annotation history preserves add, edit, delete and redo snapshots", () =>
        {
            var document = new AnnotationDocument();
            var item = Line();
            document.Add(item);
            item.Points[0] = new Point(999, 999);
            Require(document.Items[0].Points[0].X == 10);
            var changed = document.Items[0].Clone();
            changed.Color = Colors.Blue;
            changed.Points = [new Point(15, 20), new Point(90, 20)];
            document.Replace(0, document.Items[0], changed);
            document.Remove(0);
            document.Undo();
            Require(document.Items[0].Color == Colors.Blue);
            document.Undo();
            Require(document.Items[0].Color == Colors.Red && document.Items[0].Points[0].X == 10);
            document.Redo();
            Require(document.Items[0].Points[0].X == 15);
            document.Add(Line());
            Require(!document.CanRedo);
        });
        check("Nearly straight mouse strokes snap while intentional curves remain curved", () =>
        {
            var item = new ImageAnnotation { Tool = AnnotationTool.Pen,
                Points = [new(10, 20), new(30, 20.8), new(60, 19.5), new(110, 21)] };
            item.Straighten();
            Require(item.Points.Count == 2 && Math.Abs(item.Points[0].Y - item.Points[1].Y) < .001);
            item.Points = [new(10, 20), new(30, 60), new(60, 65), new(110, 21)];
            item.Straighten();
            Require(item.Points.Count == 4 && item.Geometry().IsFrozen);
            var endpoint = ImageAnnotation.SnapEnd(new Point(), new Point(100, 80), true);
            Require(Math.Abs(endpoint.X - endpoint.Y) < .001);
        });
        check("Every shape and text can be hit-tested with a zoom-adjusted tolerance", () =>
        {
            foreach (var tool in new[] { AnnotationTool.Line, AnnotationTool.Arrow, AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Text })
            {
                var item = new ImageAnnotation { Tool = tool, Points = [new(10, 10), new(90, 90)], Text = "Review" };
                var point = tool == AnnotationTool.Ellipse ? new Point(50, 10) : new Point(10, 10);
                Require(item.HitTest(point, 5));
                Require(!item.HitTest(new Point(500, 500), 5));
            }
        });
        check("Highlights blend with the source and export at original pixel dimensions", () =>
        {
            var document = new AnnotationDocument();
            document.Add(new ImageAnnotation { Tool = AnnotationTool.Highlight, Color = Colors.Yellow, Width = 12, Points = [new(10, 20), new(90, 20)] });
            var source = WhiteImage(100, 60);
            var output = document.Render(source);
            Require(output.IsFrozen && output.PixelWidth == 100 && output.PixelHeight == 60);
            var pixel = Pixel(output, 50, 20);
            Require(pixel.R > 245 && pixel.G > 245 && pixel.B is > 130 and < 210);
            Require(Pixel(source, 50, 20).B == 255);
        });
        check("Edited PNG, JPEG and BMP exports include annotation pixels", () =>
        {
            var document = new AnnotationDocument();
            document.Add(Line());
            var source = WhiteImage(100, 60);
            var sourcePath = Path.Combine(directory, "annotation-source.png");
            SavePng(sourcePath, source);
            var output = document.Render(source);
            foreach (var extension in new[] { ".png", ".jpg", ".bmp" })
            {
                var destination = Path.Combine(directory, "annotated" + extension);
                ScreenshotFileService.Export(sourcePath, destination, output, imageModified: true);
                var decoded = new BitmapImage();
                decoded.BeginInit();
                decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.UriSource = new Uri(destination);
                decoded.EndInit();
                var pixel = Pixel(decoded, 50, 20);
                Require(pixel.R > 200 && pixel.G < 70 && pixel.B < 70);
            }
        });
        check("Background export uses a frozen snapshot while the document remains editable", () =>
        {
            var document = new AnnotationDocument();
            document.Add(Line());
            var rendering = document.RenderAsync(WhiteImage(100, 60));
            var edited = document.Items[0].Clone();
            edited.Color = Colors.Blue;
            document.Replace(0, document.Items[0], edited);
            var output = rendering.GetAwaiter().GetResult();
            Require(output.IsFrozen && Pixel(output, 50, 20).R > 245 && Pixel(output, 50, 20).B < 10);
            Require(document.Items[0].Color == Colors.Blue);
        });
        check("Moving and resizing an annotation can be cancelled without changing history", () =>
        {
            var surface = new AnnotationSurface { Width = 300, Height = 200 };
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Points = [new(30, 30), new(100, 80)] });
            surface.Select(0);
            var original = typeof(AnnotationSurface).GetField("original", BindingFlags.Instance | BindingFlags.NonPublic)!;
            original.SetValue(surface, surface.Selected!.Clone());
            surface.UpdateGesture(new Point(10, 20));
            Require(surface.Selected.Bounds.Left == 40 && surface.Selected.Bounds.Top == 50);
            surface.CancelGesture();
            Require(surface.Selected.Bounds.Left == 30 && surface.Selected.Bounds.Top == 30);
            original.SetValue(surface, surface.Selected.Clone());
            typeof(AnnotationSurface).GetField("resizeCorner", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(surface, 2);
            surface.UpdateGesture(new Point(200, 150));
            Require(surface.Selected.Bounds.Width == 170 && surface.Selected.Bounds.Height == 120);
            surface.CancelGesture();
            Require(surface.Selected.Bounds.Width == 70);
            surface.Document.Undo();
            Require(surface.Document.Items.Count == 0);
            surface.Document.Add(Line());
            surface.Select(0);
            original.SetValue(surface, surface.Selected!.Clone());
            surface.UpdateGesture(new Point(120, 100));
            Require(surface.Selected.Points[0] == new Point(10, 20) && surface.Selected.Points[1] == new Point(120, 100));
            surface.CancelGesture();
        });
        check("Editor toolbar wraps at minimum width and inline text remains editable", () =>
        {
            var path = Path.Combine(directory, "editor-layout.png");
            var source = WhiteImage(1200, 700);
            SavePng(path, source);
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(path), source);
            try
            {
                window.Show();
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                foreach (var width in new[] { 620d, 1100d })
                {
                    ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                    window.Width = width;
                    window.Height = 720;
                    window.UpdateLayout();
                    var surface = (AnnotationSurface)window.FindName("Annotations");
                    Require(surface.DisplayScale > 0);
                    var remove = (System.Windows.Controls.Button)window.FindName("RemoveButton");
                    var position = remove.TranslatePoint(new Point(), window);
                    Require(position.X >= 0 && position.X + remove.ActualWidth < width);
                    Require(((FrameworkElement)window.FindName("ImageViewport")).ActualHeight > 300);
                }
                var beginText = typeof(ScreenshotViewerWindow).GetMethod("BeginText", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var commit = typeof(ScreenshotViewerWindow).GetMethod("CommitText", BindingFlags.Instance | BindingFlags.NonPublic)!;
                beginText.Invoke(window, [new Point(30, 50)]);
                var text = (System.Windows.Controls.TextBox)window.FindName("InlineText");
                text.Text = "Editable note";
                commit.Invoke(window, [false]);
                var annotations = (AnnotationSurface)window.FindName("Annotations");
                Require(annotations.Document.Items.Count == 1 && annotations.Selected?.Text == "Editable note");
                beginText.Invoke(window, [new Point(30, 50)]);
                text.Text = "Changed note";
                commit.Invoke(window, [false]);
                Require(annotations.Document.Items.Count == 1 && annotations.Selected?.Text == "Changed note");
                annotations.Document.Undo();
                Require(annotations.Document.Items[0].Text == "Editable note");
            }
            finally { window.Close(); }
        });
    }

    private static ImageAnnotation Line() => new() { Tool = AnnotationTool.Line, Color = Colors.Red, Width = 6, Points = [new(10, 20), new(90, 20)] };

    private static BitmapSource WhiteImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static void SavePng(string path, BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    public static int Benchmark()
    {
        var app = new System.Windows.Application();
        var surface = new AnnotationSurface { Width = 3840, Height = 2160 };
        for (var i = 0; i < 300; i++)
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Points = [new(i * 7 % 3500, i * 11 % 1900), new(i * 7 % 3500 + 200, i * 11 % 1900 + 100)] });
        var draft = new ImageAnnotation { Tool = AnnotationTool.Pen, Points = [new(10, 10), new(10, 10)] };
        typeof(AnnotationSurface).GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(surface, draft);
        var timings = new double[1500];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < timings.Length; i++)
        {
            var timer = Stopwatch.StartNew();
            surface.UpdateGesture(new Point(10 + i * 2, 300 + Math.Sin(i * .04) * 100));
            timings[i] = timer.Elapsed.TotalMilliseconds;
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(timings);
        Console.WriteLine($"4K editor, 300 existing annotations, 1500 stroke updates: median {timings[750]:F3} ms, p95 {timings[1425]:F3} ms, max {timings[^1]:F3} ms; {allocated / 1024d / 1024:F1} MB allocated.");
        var source = WhiteImage(3840, 2160);
        surface.Document.Add(draft);
        var exportTimes = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            var timer = Stopwatch.StartNew();
            _ = surface.Document.Render(source);
            if (i > 0) exportTimes.Add(timer.Elapsed.TotalMilliseconds);
        }
        exportTimes.Sort();
        Console.WriteLine($"4K composite, 301 annotations, 5 warm runs: median {exportTimes[2]:F2} ms. PNG encoding measured separately.");
        var outputImage = surface.Document.Render(source);
        var encodeTimes = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var timer = Stopwatch.StartNew();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(outputImage));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            encodeTimes.Add(timer.Elapsed.TotalMilliseconds);
        }
        encodeTimes.Sort();
        Console.WriteLine($"4K PNG encode, 5 runs: median {encodeTimes[2]:F2} ms, performed off the UI thread by Save As.");
        app.Shutdown();
        return 0;
    }

    public static int Demo(bool qa = false)
    {
        var app = new System.Windows.Application();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 740));
            context.DrawText(new FormattedText("Launch review", System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 44, Brushes.Black, 1), new Point(80, 70));
            context.DrawText(new FormattedText("A clear plan. A confident team.", System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 24, Brushes.SlateGray, 1), new Point(80, 145));
            for (var i = 0; i < 3; i++)
            {
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(244, 246, 250)), null, new Rect(80 + i * 355, 230, 330, 180), 16, 16);
                context.DrawText(new FormattedText(new[] { "01  Design", "02  Build", "03  Ship" }[i], System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 26, Brushes.Black, 1), new Point(105 + i * 355, 260));
            }
            context.DrawText(new FormattedText("Ready for the next step", System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 24, Brushes.Black, 1), new Point(80, 495));
            context.DrawRectangle(Brushes.Gainsboro, null, new Rect(80, 560, 1040, 2));
        }
        var image = new RenderTargetBitmap(1200, 740, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        var path = Path.Combine(Path.GetTempPath(), $"BetterScreenShot-editor-demo-{Guid.NewGuid():N}.png");
        SavePng(path, image);
        var window = new ScreenshotViewerWindow(new TemporaryScreenshot(path), image);
        if (qa)
        {
            window.Title = "Screenshot Editor - QA";
            var surface = (AnnotationSurface)window.FindName("Annotations");
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Width = 5,
                Color = Colors.Red, Points = [new(980, 170), new(800, 270)] });
            surface.Tool = AnnotationTool.Hand;
        }
        app.Run(window);
        return 0;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Annotation assertion failed.");
    }
}
