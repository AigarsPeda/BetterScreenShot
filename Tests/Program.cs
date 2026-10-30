using System.Diagnostics;
using System.Drawing;
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

            Check("PNG is copied without re-encoding", () =>
            {
                var output = Path.Combine(directory, "copy.png");
                ScreenshotFileService.Export(source, output);
                Assert(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(output)));
            });
            foreach (var extension in new[] { ".jpg", ".jpeg", ".bmp" })
            {
                Check($"{extension} has the correct format and dimensions", () =>
                {
                    var output = Path.Combine(directory, "converted" + extension);
                    ScreenshotFileService.Export(source, output);
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
                Throws<ArgumentException>(() => ScreenshotFileService.Export(source, output));
                Assert(File.ReadAllText(output) == "original");
            });
            Check("Saving onto the temporary source is rejected", () =>
            {
                Throws<IOException>(() => ScreenshotFileService.Export(source, source));
                Assert(ScreenshotFileService.LoadBitmap(source).PixelWidth == 32);
            });
            Check("Failed overwrite preserves the destination and removes staging files", () =>
            {
                var output = Path.Combine(directory, "locked.jpg");
                File.WriteAllText(output, "original");
                using (var locked = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var error = Throws<Exception>(() => ScreenshotFileService.Export(source, output));
                    Assert(error is IOException or UnauthorizedAccessException);
                }
                Assert(File.ReadAllText(output) == "original");
                Assert(Directory.GetFiles(directory, "*.tmp").Length == 0);
            });
            Check("Cached image survives source deletion and can still be exported", () =>
            {
                var copy = Path.Combine(directory, "cached.png");
                File.Copy(source, copy);
                var image = ScreenshotFileService.LoadBitmap(copy);
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
            Check("Worker-thread decoding produces a frozen image usable on the UI thread", () =>
            {
                var image = Task.Run(() => ScreenshotFileService.LoadBitmap(source)).GetAwaiter().GetResult();
                Assert(image.IsFrozen && image.PixelWidth == 32 && image.PixelHeight == 24);
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
                Throws<ArgumentOutOfRangeException>(() => ScreenshotCaptureService.CaptureToTemporaryFile(Rectangle.Empty));
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
                var toast = new ScreenshotToastWindow(copy, new Rectangle(0, 0, 32, 24));
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
                    ScreenshotToastWindow.ShowToast(first, bounds);
                    ScreenshotToastWindow.ShowToast(second, bounds);
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
                var image = ScreenshotFileService.LoadBitmap(copy);
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
            application.Shutdown();
            MonitorSelectionOverlayForm.Shutdown();

            Console.WriteLine($"{passed} passed, {failed} failed.");
            if (args.Contains("--benchmark"))
            {
                BenchmarkCapture();
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
