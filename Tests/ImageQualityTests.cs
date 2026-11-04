using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;

internal static class ImageQualityTests
{
    internal static void Run(Action<string, Action> check, string directory)
    {
        check("Image quality defaults to Maximum and persists across restart", () =>
        {
            var path = Path.Combine(directory, "image-quality.json");
            var store = new EditorSettingsStore(path);
            Require(store.Load().JpegQuality == 100);
            foreach (var quality in new[] { 1, 72, 85, 95, 100 })
            {
                store.Save(new EditorSettings { JpegQuality = quality, Color = "#0A84FF" });
                var loaded = new EditorSettingsStore(path).Load();
                Require(loaded.JpegQuality == quality && loaded.Color == "#0A84FF");
            }
            // Existing preferences acquire the new default without losing their other values.
            var legacy = JsonNode.Parse(File.ReadAllText(path))!;
            legacy.AsObject().Remove("jpegQuality");
            File.WriteAllText(path, legacy.ToJsonString());
            Require(store.Load().JpegQuality == 100 && store.Load().Color == "#0A84FF");
            foreach (var quality in new[] { int.MinValue, 0, 101, int.MaxValue })
            {
                var invalid = new EditorSettings { JpegQuality = quality };
                Require(invalid.ValidationError() is not null);
                Throws<ArgumentException>(() => store.Save(invalid));
                Require(store.Load().Color == "#0A84FF");
            }
        });
        check("Higher JPEG quality reduces pixel error while preserving odd-sized images", () =>
        {
            var source = DetailImage(257, 129);
            var original = Pixels(source);
            var previousError = double.MaxValue;
            var previousSize = 0L;
            foreach (var quality in new[] { 1, 85, 95, 100 })
            {
                var path = Path.Combine(directory, $"quality-{quality}.jpg");
                ScreenshotFileService.Export("unused.png", path, source, jpegQuality: quality);
                var decoded = ScreenshotCaptureService.LoadFullImage(path);
                Require(decoded.PixelWidth == 257 && decoded.PixelHeight == 129);
                var actual = Pixels(decoded);
                double squaredError = 0;
                for (var i = 0; i < original.Length; i++)
                    if (i % 4 != 3) squaredError += Math.Pow(original[i] - actual[i], 2);
                var size = new FileInfo(path).Length;
                Require(squaredError < previousError && size > previousSize);
                previousError = squaredError;
                previousSize = size;
            }
        });
        check("PNG and BMP retain exact full-resolution pixels at every JPEG quality", () =>
        {
            var source = DetailImage(317, 181);
            var path = Path.Combine(directory, "quality-source.png");
            ScreenshotFileService.Export("unused.png", path, source, imageModified: true);
            foreach (var quality in new[] { 1, 85, 100 })
            foreach (var extension in new[] { ".png", ".bmp" })
            {
                var saved = Path.Combine(directory, $"lossless-{quality}{extension}");
                ScreenshotFileService.Export(path, saved, source, jpegQuality: quality);
                Require(Pixels(ScreenshotCaptureService.LoadFullImage(saved)).AsSpan().SequenceEqual(Pixels(source)));
                if (extension == ".png") Require(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(saved)));
            }
        });
        check("Invalid JPEG quality leaves saved files intact and creates no staging files", () =>
        {
            var path = Path.Combine(directory, "quality-existing.jpg");
            File.WriteAllText(path, "previous save");
            foreach (var quality in new[] { 0, 101, int.MinValue, int.MaxValue })
                Throws<ArgumentOutOfRangeException>(() => ScreenshotFileService.Export("unused.png", path, DetailImage(1, 1), jpegQuality: quality));
            Require(File.ReadAllText(path) == "previous save" && Directory.GetFiles(directory, ".*.tmp").Length == 0);
        });
        check("JPEG quality boundaries support one-pixel images and both JPEG extensions", () =>
        {
            foreach (var quality in new[] { 1, 100 })
            foreach (var extension in new[] { ".jpg", ".JPEG" })
            {
                var path = Path.Combine(directory, $"one-pixel-{quality}{extension}");
                ScreenshotFileService.Export("unused.png", path, DetailImage(1, 1), jpegQuality: quality);
                var decoded = ScreenshotCaptureService.LoadFullImage(path);
                Require(decoded.PixelWidth == 1 && decoded.PixelHeight == 1);
            }
        });
        check("Image clipboard offers lossless PNG and a full-resolution bitmap", () =>
        {
            foreach (var (width, height) in new[] { (1, 1), (319, 173), (1440, 120) })
            {
                var source = DetailImage(width, height);
                var preparation = ImageClipboardService.CreateDataObjectAsync(source);
                PumpUntil(() => preparation.IsCompleted, Dispatcher.CurrentDispatcher);
                var data = preparation.GetAwaiter().GetResult();
                Require(data.GetDataPresent("PNG", autoConvert: false) && data.ContainsImage());
                Require(data.GetImage().PixelWidth == width && data.GetImage().PixelHeight == height);
                Require(Pixels(data.GetImage()).AsSpan().SequenceEqual(Pixels(source)));
                using var png = (MemoryStream)data.GetData("PNG", autoConvert: false);
                Require(png.Position == 0);
                var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Require(Pixels(decoder.Frames[0]).AsSpan().SequenceEqual(Pixels(source)));
            }
            Throws<ArgumentNullException>(() => ImageClipboardService.CreateDataObject(null!));
        });
        check("Lossless clipboard PNG preserves transparency and high-DPI image pixels", () =>
        {
            const int width = 37, height = 19;
            var pixels = new byte[width * height * 4];
            new Random(71).NextBytes(pixels);
            var source = BitmapSource.Create(width, height, 192, 192, PixelFormats.Bgra32, null, pixels, width * 4);
            source.Freeze();
            var data = ImageClipboardService.CreateDataObject(source);
            using var png = (MemoryStream)data.GetData("PNG", autoConvert: false);
            var decoded = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Require(decoded.PixelWidth == width && decoded.PixelHeight == height);
            Require(Pixels(decoded).AsSpan().SequenceEqual(pixels));
            Require(Math.Abs(decoded.DpiX - 192) < 1);
        });
        check("Editor Copy keeps source detail at high DPI and zoom", () =>
        {
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                BitmapSource? copied = null;
                var source = DetailImage(317, 181, dpi);
                var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, $"copy-quality-{dpi}.png")), source, image => copied = image);
                var previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                try
                {
                    window.Show();
                    typeof(ScreenshotViewerWindow).GetMethod("ChangeZoom", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [0.8]);
                    var copy = window.CopyImageAsync();
                    PumpUntil(() => copy.IsCompleted, window.Dispatcher);
                    copy.GetAwaiter().GetResult();
                    Require(copied is not null && copied.PixelWidth == source.PixelWidth && copied.PixelHeight == source.PixelHeight);
                    Require(Pixels(copied!).AsSpan().SequenceEqual(Pixels(source)));
                }
                finally
                {
                    window.Close();
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                }
            }
        });
        check("Annotation rendering preserves image detail outside marks at high DPI", () =>
        {
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                var source = DetailImage(317, 181, dpi);
                var document = new AnnotationDocument { SketchEnabled = false };
                document.Add(new ImageAnnotation { Tool = AnnotationTool.Line, Color = Colors.Red, Width = 2,
                    Points = [new(5, 5), new(20, 5)] });
                var rendered = document.Render(source);
                var original = Pixels(source);
                var actual = Pixels(rendered);
                Require(rendered.PixelWidth == source.PixelWidth && rendered.PixelHeight == source.PixelHeight);
                Require(original.AsSpan(317 * 20 * 4).SequenceEqual(actual.AsSpan(317 * 20 * 4)));
            }
        });
        check("Scrolling Copy loads the original image rather than its small preview", () =>
        {
            var source = DetailImage(1900, 120);
            var path = Path.Combine(directory, "copy-scrolling-quality.png");
            ScreenshotFileService.Export("unused.png", path, source, imageModified: true);
            var thumbnail = DetailImage(512, 32);
            var toast = new ScreenshotToastWindow(path, new Rectangle(0, 0, 300, 120), thumbnail, previewIsThumbnail: true);
            try
            {
                var full = (BitmapSource)typeof(ScreenshotToastWindow).GetMethod("FullImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toast, null)!;
                var data = ImageClipboardService.CreateDataObject(full);
                using var png = (MemoryStream)data.GetData("PNG", autoConvert: false);
                var decoded = new PngBitmapDecoder(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Require(decoded.PixelWidth == 1900 && decoded.PixelHeight == 120 && Pixels(decoded).AsSpan().SequenceEqual(Pixels(source)));
            }
            finally { toast.Close(); }
        });
        check("Settings quality choices save the selected value and Cancel leaves preferences alone", () =>
        {
            var current = EditorSettingsService.Current;
            EditorSettings? saved = null;
            var window = new CaptureShortcutsWindow(CaptureShortcutSettings.Disabled, (_, editor) => { saved = editor; return "Keep open for this check."; });
            try
            {
                window.Show();
                var picker = (System.Windows.Controls.ComboBox)window.FindName("JpegQualityPicker");
                Require(int.Parse((string)((ComboBoxItem)picker.SelectedItem).Tag) == current.JpegQuality);
                foreach (var quality in new[] { 85, 95, 100 })
                {
                    picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, quality.ToString()));
                    ((System.Windows.Controls.Button)window.FindName("SaveShortcutsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Require(saved?.JpegQuality == quality && window.EditorDraft.JpegQuality == quality);
                }
                Require(EditorSettingsService.Current == current);
            }
            finally { window.Close(); }
            var cancelled = new CaptureShortcutsWindow(CaptureShortcutSettings.Disabled, (_, _) => throw new InvalidOperationException("Cancel saved preferences."));
            try
            {
                ((System.Windows.Controls.ComboBox)cancelled.FindName("JpegQualityPicker")).SelectedIndex = 0;
            }
            finally { cancelled.Close(); }
            Require(EditorSettingsService.Current == current);
        });
    }

    private static BitmapSource DetailImage(int width, int height, int dpi = 96)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(207);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var value = (byte)random.Next(256);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        var source = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }

    private static void PumpUntil(Func<bool> done, Dispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.Background);
            Dispatcher.PushFrame(frame);
        }
        Require(done());
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Image quality assertion failed.");
    }
}
