using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using BetterScreenShot;

internal static class Program
{
    private static int passed;
    private static int failed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault(arg => arg.StartsWith("--readme-", StringComparison.Ordinal)) is { } readmeView)
            return ReadmeScreenshots.Demo(readmeView);
        if (args.Contains("--shortcuts-demo")) return CaptureShortcutTests.Demo();
        if (args.Contains("--shortcut-recorder-demo")) return CaptureShortcutTests.RecorderDemo();
        if (args.Contains("--shortcuts-preview")) return CaptureShortcutTests.Preview();
        if (args.Contains("--context-menu-preview")) return AnnotationContextMenuTests.Preview();
        if (args.Contains("--editor-demo")) return AnnotationTests.Demo();
        if (args.Contains("--editor-qa")) return AnnotationTests.Demo(qa: true);
        if (args.Contains("--editor-benchmark")) return AnnotationTests.Benchmark();
        if (args.Contains("--editor-toolbar-preview")) return AnnotationTests.ToolbarPreview();
        var fixturesRoot = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var directory = Path.Combine(fixturesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.png");
            using (var bitmap = new Bitmap(32, 24))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.CornflowerBlue);
                bitmap.Save(source, ImageFormat.Png);
            }

            var fixtureImage = LoadBitmap(source);
            Check("PNG is copied without re-encoding", () =>
            {
                var output = Path.Combine(directory, "copy.png");
                ScreenshotFileService.Export(source, output, fixtureImage);
                Assert(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(output)));
            });
            foreach (var extension in new[] { ".jpg", ".jpeg", ".bmp" })
            {
                Check($"{extension} has the correct format and dimensions", () =>
                {
                    var output = Path.Combine(directory, "converted" + extension);
                    ScreenshotFileService.Export(source, output, fixtureImage);
                    using var stream = File.OpenRead(output);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Assert(extension == ".bmp" ? decoder is BmpBitmapDecoder : decoder is JpegBitmapDecoder);
                    Assert(decoder.Frames[0].PixelWidth == 32 && decoder.Frames[0].PixelHeight == 24);
                });
            }

            Check("Unsupported formats preserve an existing file", () =>
            {
                var output = Path.Combine(directory, "existing.gif");
                File.WriteAllText(output, "original");
                Throws<ArgumentException>(() => ScreenshotFileService.Export(source, output, fixtureImage));
                Assert(File.ReadAllText(output) == "original");
            });
            Check("Saving onto the temporary source is rejected", () =>
            {
                Throws<IOException>(() => ScreenshotFileService.Export(source, source, fixtureImage));
                Assert(LoadBitmap(source).PixelWidth == 32);
            });
            Check("Failed overwrite preserves the destination and removes staging files", () =>
            {
                var output = Path.Combine(directory, "locked.jpg");
                File.WriteAllText(output, "original");
                using (var locked = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var error = Throws<Exception>(() => ScreenshotFileService.Export(source, output, fixtureImage));
                    Assert(error is IOException or UnauthorizedAccessException);
                }
                Assert(File.ReadAllText(output) == "original");
                Assert(Directory.GetFiles(directory, "*.tmp").Length == 0);
            });
            Check("Cached image survives source deletion and can still be exported", () =>
            {
                var copy = Path.Combine(directory, "cached.png");
                File.Copy(source, copy);
                var image = LoadBitmap(copy);
                Assert(image.IsFrozen);
                File.Delete(copy);
                ScreenshotFileService.Export(copy, Path.Combine(directory, "cached.jpg"), image);
            });
            Check("Temporary file disposal is safe to repeat", () =>
            {
                var copy = Path.Combine(directory, "temporary.png");
                File.Copy(source, copy);
                var screenshot = new TemporaryScreenshot(copy);
                screenshot.Dispose();
                screenshot.Dispose();
                Assert(!File.Exists(copy));
            });
            Check("Worker capture copies pixels and releases the source bitmap", () =>
            {
                var capture = Task.Run(() =>
                {
                    using var bitmap = new Bitmap(3, 2, PixelFormat.Format32bppRgb);
                    bitmap.SetPixel(0, 0, Color.Red);
                    bitmap.SetPixel(2, 1, Color.Blue);
                    return ScreenshotCaptureService.SaveCapture(bitmap);
                }).GetAwaiter().GetResult();
                using var temporaryFile = new TemporaryScreenshot(capture.FilePath);
                Assert(capture.Preview.IsFrozen && capture.Preview.PixelWidth == 3 && capture.Preview.PixelHeight == 2);
                var pixels = new byte[24];
                capture.Preview.CopyPixels(pixels, 12, 0);
                Assert(pixels[0] == 0 && pixels[1] == 0 && pixels[2] == 255);
                Assert(pixels[20] == 255 && pixels[21] == 0 && pixels[22] == 0);
                var savedImage = LoadBitmap(capture.FilePath);
                Assert(savedImage.PixelWidth == 3);
                var savedPixels = new byte[24];
                var converted = new FormatConvertedBitmap(savedImage, System.Windows.Media.PixelFormats.Bgr32, null, 0);
                converted.CopyPixels(savedPixels, 12, 0);
                Assert(pixels.Where((_, index) => index % 4 != 3).SequenceEqual(savedPixels.Where((_, index) => index % 4 != 3)));
            });
            Check("Scan icons are frozen and reused for each theme", () =>
            {
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    var icon = ThemeIconService.CreateScanIcon(theme);
                    Assert(icon.IsFrozen && ReferenceEquals(icon, ThemeIconService.CreateScanIcon(theme)));
                }
                Assert(!ReferenceEquals(ThemeIconService.CreateScanIcon(AppTheme.Light), ThemeIconService.CreateScanIcon(AppTheme.Dark)));
            });
            Check("Camera cursor loads from the embedded asset without companion files", () =>
            {
                var factory = typeof(MonitorSelectionOverlayForm).GetMethod("CreateMonitorSelectionCursor",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                using var cursor = (System.Windows.Forms.Cursor)factory.Invoke(null, null)!;
                Assert(cursor.Handle != System.Windows.Forms.Cursors.Hand.Handle);
                Assert(cursor.Size == new System.Drawing.Size(32, 32));
            });
            Check("Selection success cleans up before completing", () =>
            {
                var cleanups = 0;
                using var session = new CaptureSelectionSession(() => cleanups++);
                var rectangle = new Rectangle(12, 34, 56, 78);
                session.Complete(rectangle);
                Assert(cleanups == 1 && session.Task.GetAwaiter().GetResult() == rectangle);
                session.Complete(null);
                Assert(cleanups == 1 && session.Task.Result == rectangle);
            });
            Check("Selection cancellation cleans up once", () =>
            {
                var cleanups = 0;
                var session = new CaptureSelectionSession(() => cleanups++);
                session.Dispose();
                session.Dispose();
                Assert(session.Task.Result is null && cleanups == 1);
            });
            Check("Selection failure reaches the caller after cleanup", () =>
            {
                var cleaned = false;
                using var session = new CaptureSelectionSession(() => cleaned = true);
                session.Fail(new IOException("capture failed"));
                Throws<IOException>(() => session.Task.GetAwaiter().GetResult());
                Assert(cleaned);
            });
            Check("Cleanup errors fault the selection instead of hanging it", () =>
            {
                using var session = new CaptureSelectionSession(() => throw new IOException("cleanup failed"));
                session.Complete(null);
                Assert(session.Task.IsCompleted);
                Throws<IOException>(() => session.Task.GetAwaiter().GetResult());
            });
            Check("Original and cleanup failures are both retained", () =>
            {
                using var session = new CaptureSelectionSession(() => throw new IOException("cleanup failed"));
                session.Fail(new InvalidOperationException("selection failed"));
                var error = Throws<AggregateException>(() => session.Task.GetAwaiter().GetResult());
                Assert(error.InnerExceptions.Count == 2);
            });
            foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
            {
                Check($"Selection maps correctly at {scale * 100}% with a negative monitor origin", () =>
                {
                    var screen = new Rectangle(-3840, -2160, 3840, 2160);
                    var client = new Size((int)(3840 / scale), (int)(2160 / scale));
                    var selection = new Rectangle(100, 100, 200, 200);
                    var expected = new Rectangle(-3840 + (int)(100 * scale), -2160 + (int)(100 * scale), (int)(200 * scale), (int)(200 * scale));
                    Assert(CaptureGeometry.ToScreenRectangle(selection, client, screen) == expected);
                });
            }
            Check("Drag beyond monitor edges is clipped", () =>
            {
                var screen = new Rectangle(-1920, 0, 1920, 1080);
                var selection = new Rectangle(-100, -100, 3000, 2000);
                Assert(CaptureGeometry.ToScreenRectangle(selection, screen.Size, screen) == screen);
            });
            Check("Invalid geometry and captures are rejected", () =>
            {
                Throws<ArgumentOutOfRangeException>(() => CaptureGeometry.ToScreenRectangle(new Rectangle(0, 0, 10, 10), Size.Empty, new Rectangle(0, 0, 100, 100)));
                Throws<ArgumentOutOfRangeException>(() => ScreenshotCaptureService.Capture(Rectangle.Empty));
            });
            Check("Toast positioning preserves physical monitor coordinates at mixed scales", () =>
            {
                foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
                {
                    var workArea = new Rectangle(-3840, -2160, 3840, 2100);
                    var size = new Size((int)(300 * scale), (int)(170 * scale));
                    var margin = (int)(16 * scale);
                    var position = CaptureGeometry.ToastPosition(workArea, size, margin);
                    Assert(position.X + size.Width == workArea.Right - margin);
                    Assert(position.Y + size.Height == workArea.Bottom - margin);
                }
            });

            var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            CaptureShortcutTests.Run(Check, directory);
            AnnotationTests.Run(Check, directory);
            OverlayPresentationTests.Run(Check);
            Check("Real area-selection cancellation releases all overlays", () =>
            {
                var selection = MonitorSelectionOverlayForm.SelectAreaAsync();
                Assert(!selection.IsCompleted);
                MonitorSelectionOverlayForm.CancelActiveSelection();
                Assert(selection.IsCompletedSuccessfully && selection.Result is null);
                Assert(!System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Any(form => form.Visible));
            });
            Check("Real monitor selection rejects overlapping sessions and can be reused", () =>
            {
                var selection = MonitorSelectionOverlayForm.SelectMonitorAsync();
                try
                {
                    Throws<InvalidOperationException>(() => MonitorSelectionOverlayForm.SelectAreaAsync());
                }
                finally
                {
                    MonitorSelectionOverlayForm.CancelActiveSelection();
                }
                Assert(selection.IsCompletedSuccessfully && selection.Result is null);
                Assert(!System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Any(form => form.Visible));
                var retry = MonitorSelectionOverlayForm.SelectMonitorAsync();
                MonitorSelectionOverlayForm.CancelActiveSelection();
                Assert(retry.IsCompletedSuccessfully && retry.Result is null);
            });
            Check("Closing an unopened toast releases its screenshot", () =>
            {
                var copy = Path.Combine(directory, "closed-toast.png");
                File.Copy(source, copy);
                var toast = new ScreenshotToastWindow(copy, new Rectangle(0, 0, 32, 24), fixtureImage);
                toast.Close();
                Assert(!File.Exists(copy));
            });
            Check("Replacing a toast deletes the previous temporary screenshot", () =>
            {
                var first = Path.Combine(directory, "first-toast.png");
                var second = Path.Combine(directory, "second-toast.png");
                File.Copy(source, first);
                File.Copy(source, second);
                var bounds = new Rectangle(0, 0, 32, 24);
                try
                {
                    ScreenshotToastWindow.ShowToast(first, bounds, fixtureImage);
                    ScreenshotToastWindow.ShowToast(second, bounds, fixtureImage);
                    Assert(!File.Exists(first) && File.Exists(second));
                }
                finally
                {
                    ScreenshotToastWindow.CloseCurrent();
                }
                Assert(!File.Exists(second));
            });
            Check("Opening a viewer transfers ownership until the viewer closes", () =>
            {
                var copy = Path.Combine(directory, "viewer-owned.png");
                File.Copy(source, copy);
                var image = LoadBitmap(copy);
                var toast = new ScreenshotToastWindow(copy, new Rectangle(0, 0, 32, 24), image);
                var viewer = toast.OpenViewer();
                try
                {
                    Assert(File.Exists(copy));
                    var viewerImage = (System.Windows.Controls.Image)viewer.FindName("ViewerImage");
                    Assert(ReferenceEquals(image, viewerImage.Source));
                }
                finally
                {
                    viewer.Close();
                }
                Assert(!File.Exists(copy));
            });
            BackgroundCaptureTests.Run(Check, directory);
            Check("Launcher status text wraps within a narrow window", () =>
            {
                var window = new MainWindow();
                try
                {
                    var status = (System.Windows.Controls.TextBlock)window.FindName("StatusTextBlock");
                    status.Text = "Screenshot captured. Use the popup to save, copy, or discard it.";
                    var content = (System.Windows.FrameworkElement)window.Content;
                    content.Measure(new System.Windows.Size(380, 300));
                    content.Arrange(new System.Windows.Rect(0, 0, 380, 300));
                    content.UpdateLayout();
                    var badge = (System.Windows.Controls.Border)window.FindName("StatusBadge");
                    Assert(status.ActualWidth > 0 && status.ActualWidth < badge.ActualWidth && status.ActualHeight > status.FontSize * 2);
                }
                finally { window.Close(); }
            });
            application.Shutdown();
            MonitorSelectionOverlayForm.Shutdown();

            Console.WriteLine($"{passed} passed, {failed} failed.");
            if (args.Contains("--benchmark"))
            {
                BenchmarkCapture();
                BenchmarkPreview(directory);
            }
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            // This directory contains only fixtures created by this run.
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(fixturesRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fixture directory is outside the test output directory.");
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BitmapImage LoadBitmap(string filePath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(filePath);
        image.EndInit();
        image.Freeze();
        return image;
    }
    private static void BenchmarkCapture()
    {
        var monitor = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var region = new Rectangle(monitor.Location, new Size(Math.Min(800, monitor.Width), Math.Min(600, monitor.Height)));
        void Capture(bool fullMonitor)
        {
            var bounds = fullMonitor ? monitor : region;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            if (fullMonitor)
            {
                using var crop = bitmap.Clone(new Rectangle(Point.Empty, region.Size), PixelFormat.Format32bppArgb);
            }
        }

        const int iterations = 20;
        Capture(true);
        Capture(false);
        var oldTime = TimeSpan.Zero;
        var newTime = TimeSpan.Zero;
        for (var i = 0; i < iterations; i++)
        {
            var timer = Stopwatch.StartNew();
            Capture(true);
            oldTime += timer.Elapsed;
            timer.Restart();
            Capture(false);
            newTime += timer.Elapsed;
        }
        Console.WriteLine($"Capture benchmark: monitor {monitor.Width}x{monitor.Height}, region {region.Width}x{region.Height}, {iterations} iterations.");
        Console.WriteLine($"Full monitor + crop: {oldTime.TotalMilliseconds / iterations:F2} ms; direct region: {newTime.TotalMilliseconds / iterations:F2} ms.");
        Console.WriteLine($"Pixel buffers: {(monitor.Width * (long)monitor.Height + region.Width * (long)region.Height) * 4:N0} bytes before; {region.Width * (long)region.Height * 4:N0} bytes after.");
    }

    private static void BenchmarkPreview(string directory)
    {
        using var bitmap = new Bitmap(1920, 1200, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(15, 23, 42));
            var random = new Random(42);
            for (var i = 0; i < 1500; i++)
            {
                using var brush = new SolidBrush(Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
                graphics.FillRectangle(brush, random.Next(bitmap.Width), random.Next(bitmap.Height), random.Next(10, 140), random.Next(10, 100));
            }
        }

        void SaveAndDecode()
        {
            var path = Path.Combine(directory, $"preview-{Guid.NewGuid():N}.png");
            try
            {
                bitmap.Save(path, ImageFormat.Png);
                _ = LoadBitmap(path);
            }
            finally { File.Delete(path); }
        }
        void CopyAndSave()
        {
            var capture = ScreenshotCaptureService.SaveCapture(bitmap);
            using var temporaryFile = new TemporaryScreenshot(capture.FilePath);
        }

        const int iterations = 10;
        SaveAndDecode();
        CopyAndSave();
        var oldTimes = new double[iterations];
        var newTimes = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var timer = Stopwatch.StartNew();
            SaveAndDecode();
            oldTimes[i] = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            CopyAndSave();
            newTimes[i] = timer.Elapsed.TotalMilliseconds;
        }
        Array.Sort(oldTimes);
        Array.Sort(newTimes);
        Console.WriteLine($"Preview benchmark: synthetic {bitmap.Width}x{bitmap.Height} image, {iterations} iterations.");
        Console.WriteLine($"Save + PNG decode median: {(oldTimes[4] + oldTimes[5]) / 2:F2} ms; copy pixels + save median: {(newTimes[4] + newTimes[5]) / 2:F2} ms.");
    }

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static void Assert(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
