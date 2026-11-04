using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;

internal static class ScrollingCaptureTests
{
    internal static int Stress()
    {
        const int width = 990, viewport = 1000, shift = 750, steps = 39;
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        using var stitcher = new ScrollingStitcher(PatternFrame(width, viewport, 0));
        var path = stitcher.SpoolPath;
        for (var index = 1; index <= steps; index++)
            if (stitcher.Add(PatternFrame(width, viewport, index * shift)) != ScrollFrameResult.Added)
                throw new InvalidOperationException($"Near-limit frame {index} did not stitch.");
        var retained = GC.GetTotalMemory(forceFullCollection: true) - baseline;
        var expectedBytes = (long)width * stitcher.Height * 4;
        Console.WriteLine($"Captured {width}×{stitcher.Height:N0}: "
            + $"{stitcher.SpoolBytes / 1_000_000d:F1} MB in the temporary file, "
            + $"{retained / 1_000_000d:F1} MB additional managed memory.");
        if (stitcher.SpoolBytes != expectedBytes || retained >= 40_000_000)
            throw new InvalidOperationException("Near-limit capture retained too many pixels in memory.");
        stitcher.Dispose();
        if (File.Exists(path)) throw new InvalidOperationException("The temporary capture file was not deleted.");
        return 0;
    }

    internal static int Benchmark()
    {
        const int width = 1200, viewport = 800, steps = 18, scroll = 55;
        var source = new byte[width * (viewport + steps * scroll) * 4];
        new Random(17).NextBytes(source);
        var frames = Enumerable.Range(0, steps + 1)
            .Select(index => Frame(source, width, index * scroll, viewport)).ToArray();
        RunOnce();
        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var trial = 0; trial < 3; trial++) RunOnce();
        clock.Stop();
        Console.WriteLine($"Three 18-step 1200×800 captures: {clock.ElapsedMilliseconds} ms, "
            + $"{(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1_000_000d:F2} MB allocated");
        return 0;

        void RunOnce()
        {
            using var stitcher = new ScrollingStitcher(frames[0]);
            for (var index = 1; index < frames.Length; index++)
                if (stitcher.Add(frames[index]) != ScrollFrameResult.Added)
                    throw new InvalidOperationException($"Benchmark frame {index} did not stitch.");
        }
    }

    internal static void Run(Action<string, Action> check)
    {
        check("Scrolling launcher flow hands the finished image to the preview", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new Rectangle(screen.Left + 30, screen.Top + 30, 120, 100);
            var launcher = new MainWindow(new CaptureShortcutSettingsStore(Path.Combine(Path.GetTempPath(), $"scroll-{Guid.NewGuid():N}.json")));
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(launcher.Dispatcher));
            try
            {
                var capture = launcher.CaptureScrollingAsync();
                var session = (CaptureSelectionSession)typeof(MonitorSelectionOverlayForm)
                    .GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                session.Complete(bounds);
                PumpUntil(() => System.Windows.Application.Current.Windows.OfType<ScrollingCaptureWindow>().Any(window => window.IsVisible), launcher.Dispatcher);
                var controls = System.Windows.Application.Current.Windows.OfType<ScrollingCaptureWindow>().Single(window => window.IsVisible);
                ((System.Windows.Controls.Button)controls.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(() => ((System.Windows.Controls.Button)controls.FindName("StopButton")).IsEnabled, launcher.Dispatcher);
                var spoolPath = controls.SpoolPath!;
                ((System.Windows.Controls.Button)controls.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(() => capture.IsCompleted, launcher.Dispatcher);
                capture.GetAwaiter().GetResult();
                Require(!File.Exists(spoolPath));
                var toast = System.Windows.Application.Current.Windows.OfType<ScreenshotToastWindow>()
                    .Single(window => window.IsVisible);
                var thumbnail = (BitmapSource)((System.Windows.Controls.Image)toast.FindName("PreviewImage")).Source;
                Require(thumbnail.PixelWidth <= 512 && thumbnail.PixelHeight < bounds.Height && launcher.IsVisible);
                var viewer = toast.OpenViewer();
                try
                {
                    var fullImage = (BitmapSource)((System.Windows.Controls.Image)viewer.FindName("ViewerImage")).Source;
                    Require(fullImage.PixelWidth == bounds.Width && fullImage.PixelHeight == bounds.Height);
                }
                finally { viewer.Close(); }
            }
            finally
            {
                MonitorSelectionOverlayForm.CancelActiveSelection();
                ScreenshotToastWindow.CloseCurrent();
                launcher.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        check("Scrolling controls start and stop a selected screen capture", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new Rectangle(screen.Left + 20, screen.Top + 20, 120, 100);
            var window = new ScrollingCaptureWindow(bounds);
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                window.Show();
                var start = (System.Windows.Controls.Button)window.FindName("StartButton");
                var stop = (System.Windows.Controls.Button)window.FindName("StopButton");
                Require(start.IsEnabled && !stop.IsEnabled);
                start.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(() => stop.IsEnabled, window.Dispatcher);
                stop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(() => window.Completion.IsCompleted, window.Dispatcher);
                using var image = window.Completion.GetAwaiter().GetResult();
                Require(image is not null && image.Width == bounds.Width && image.Height == bounds.Height);
            }
            finally
            {
                window.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        check("Cancelling before Start returns no screenshot", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var window = new ScrollingCaptureWindow(new Rectangle(screen.Left + 20, screen.Top + 20, 120, 100));
            window.Show();
            ((System.Windows.Controls.Button)window.FindName("CancelButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Require(window.Completion.IsCompleted && window.Completion.GetAwaiter().GetResult() is null);
        });
        check("Cancelling an active capture deletes its temporary rows", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var window = new ScrollingCaptureWindow(new Rectangle(screen.Left + 20, screen.Top + 20, 120, 100));
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                window.Show();
                ((System.Windows.Controls.Button)window.FindName("StartButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntil(() => window.SpoolPath is not null, window.Dispatcher);
                var path = window.SpoolPath!;
                ((System.Windows.Controls.Button)window.FindName("CancelButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(window.Completion.IsCompleted && window.Completion.GetAwaiter().GetResult() is null
                    && !File.Exists(path));
            }
            finally
            {
                window.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        check("The selection outline stays visible without entering captured pixels", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new Rectangle(screen.Left + 40, screen.Top + 40, 160, 120);
            var backdrop = new Window
            {
                Width = 160, Height = 120, WindowStyle = WindowStyle.None,
                ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 67, 120))
            };
            CaptureAreaOutlineWindow? outline = null;
            try
            {
                backdrop.Show();
                DesktopWindowService.PositionCaptureOutline(backdrop, bounds);
                DesktopWindowService.FlushPendingUpdates();
                var before = ScrollFrame.Capture(bounds);
                outline = new CaptureAreaOutlineWindow(bounds);
                outline.Show();
                DesktopWindowService.FlushPendingUpdates();
                Require(outline.IsVisible && outline.CanExcludeFromCapture && outline.IsClickThrough);
                Require(GetWindowRect(new System.Windows.Interop.WindowInteropHelper(outline).Handle, out var actualBounds)
                    && actualBounds.Left == bounds.Left && actualBounds.Top == bounds.Top
                    && actualBounds.Right == bounds.Right && actualBounds.Bottom == bounds.Bottom);
                Require(ScrollFrame.Capture(bounds).Pixels.SequenceEqual(before.Pixels));
            }
            finally
            {
                outline?.Close();
                backdrop.Close();
            }
        });
        check("Scrolling frames stitch into one image without duplicate rows", () =>
        {
            const int width = 180, viewport = 220, fullHeight = 670;
            var source = new byte[width * fullHeight * 4];
            new Random(42).NextBytes(source);
            using var stitcher = new ScrollingStitcher(Frame(source, width, 0, viewport));
            Require(stitcher.Add(Frame(source, width, 0, viewport)) == ScrollFrameResult.Unchanged);
            Require(stitcher.Add(Frame(source, width, 110, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, 250, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, 450, viewport)) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Height == 470 && stitcher.AddedFrames == 2);
            using var image = stitcher.BuildBitmap();
            var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                var actual = new byte[width * image.Height * 4];
                Marshal.Copy(data.Scan0, actual, 0, actual.Length);
                Require(actual.SequenceEqual(source.AsSpan(0, actual.Length).ToArray()));
            }
            finally { image.UnlockBits(data); }
        });
        check("Scrolling alignment works on mostly white page content", () =>
        {
            const int width = 220, viewport = 240, fullHeight = 720;
            var source = Enumerable.Repeat((byte)255, width * fullHeight * 4).ToArray();
            for (var line = 0; line < 36; line++)
            for (var y = line * 19 + 3; y < line * 19 + 8; y++)
            for (var x = 12; x < 60 + line * 37 % 145; x++)
            {
                var pixel = (y * width + x) * 4;
                source[pixel] = source[pixel + 1] = source[pixel + 2] = 38;
            }
            using var stitcher = new ScrollingStitcher(Frame(source, width, 0, viewport));
            Require(stitcher.Add(Frame(source, width, 102, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, 205, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Height == 445);
        });
        check("Floating controls sit beside the selected area", () =>
        {
            var workArea = new Rectangle(0, 0, 1200, 800);
            var size = new System.Drawing.Size(360, 112);
            Require(CaptureGeometry.FloatingControlsPosition(workArea, new Rectangle(300, 100, 500, 450), size, 10)
                == new System.Drawing.Point(810, 100));
            Require(CaptureGeometry.FloatingControlsPosition(workArea, new Rectangle(800, 100, 350, 450), size, 10)
                == new System.Drawing.Point(430, 100));
            Require(CaptureGeometry.FloatingControlsPosition(workArea, new Rectangle(10, 10, 1180, 600), size, 10)
                == new System.Drawing.Point(10, 620));
        });
        check("Repeated buttons do not create false stitching offsets", () =>
        {
            const int width = 300, viewport = 270, fullHeight = 900;
            var source = Enumerable.Repeat((byte)25, width * fullHeight * 4).ToArray();
            for (var block = 0; block < 18; block++)
            {
                var top = block * 50;
                for (var y = top + 8; y < top + 23; y++)
                for (var x = 15; x < 100; x++) SetPixel(source, width, x, y, 98, 87, 193);
                for (var y = top + 31; y < top + 34; y++)
                for (var x = 15; x < 50 + block * 11 % 180; x++) SetPixel(source, width, x, y, 207, 207, 209);
                for (var y = top + 38; y < top + 40; y++)
                for (var x = 15; x < 80 + block * 17 % 150; x++) SetPixel(source, width, x, y, 116, 116, 122);
            }
            using var stitcher = new ScrollingStitcher(Frame(source, width, 0, viewport));
            Require(stitcher.Add(Frame(source, width, 75, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, 155, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, 235, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Height == 505);
            using var image = stitcher.BuildBitmap();
            var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                var actual = new byte[width * image.Height * 4];
                Marshal.Copy(data.Scan0, actual, 0, actual.Length);
                Require(actual.SequenceEqual(source.AsSpan(0, actual.Length).ToArray()));
            }
            finally { image.UnlockBits(data); }
        });
        check("Indistinguishable repeated rows are rejected instead of stitched", () =>
        {
            const int width = 160, viewport = 240, fullHeight = 500;
            var source = Enumerable.Repeat((byte)25, width * fullHeight * 4).ToArray();
            for (var top = 0; top < fullHeight; top += 40)
            for (var y = top + 5; y < Math.Min(top + 17, fullHeight); y++)
            for (var x = 15; x < 90; x++) SetPixel(source, width, x, y, 98, 87, 193);
            using var stitcher = new ScrollingStitcher(Frame(source, width, 0, viewport));
            Require(stitcher.Add(Frame(source, width, 25, viewport)) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Height == viewport);
        });
        check("A fixed message box appears only once at the end", () =>
        {
            const int width = 220, viewport = 280, footerHeight = 60, contentHeight = viewport - footerHeight;
            var content = new byte[width * 700 * 4];
            new Random(24).NextBytes(content);
            var first = FrameWithFooter(content, width, 0, viewport, footerHeight);
            using var stitcher = new ScrollingStitcher(first);
            Require(stitcher.Add(FrameWithFooter(content, width, 70, viewport, footerHeight)) == ScrollFrameResult.Added);
            Require(stitcher.Add(FrameWithFooter(content, width, 150, viewport, footerHeight, changedFooter: true)) == ScrollFrameResult.Added);
            Require(stitcher.Height == viewport + 150);
            using var image = stitcher.BuildBitmap();
            var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                var actual = new byte[width * image.Height * 4];
                Marshal.Copy(data.Scan0, actual, 0, actual.Length);
                Require(actual.AsSpan(0, width * (contentHeight + 150) * 4)
                    .SequenceEqual(content.AsSpan(0, width * (contentHeight + 150) * 4)));
                var finalFrame = FrameWithFooter(content, width, 150, viewport, footerHeight, changedFooter: true);
                Require(actual.AsSpan(width * (contentHeight + 150) * 4)
                    .SequenceEqual(finalFrame.Pixels.AsSpan(width * contentHeight * 4)));
            }
            finally { image.UnlockBits(data); }
        });
        check("A skipped fast scroll can be recovered without duplicating content", () =>
        {
            const int width = 120, viewport = 220;
            var source = new byte[width * 800 * 4];
            new Random(91).NextBytes(source);
            using var stitcher = new ScrollingStitcher(Frame(source, width, 0, viewport));
            Require(stitcher.Add(Frame(source, width, 100, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, 500, viewport)) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Add(Frame(source, width, 70, viewport)) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Add(Frame(source, width, 135, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Height == viewport + 135);
            using var image = stitcher.BuildBitmap();
            Require(BitmapPixels(image).AsSpan().SequenceEqual(source.AsSpan(0, width * image.Height * 4)));
        });
        check("Streaming save keeps a large preview bounded and exports full size", () =>
        {
            const int width = 1500, height = 4000;
            var pixels = new byte[width * height * 4];
            using var stitcher = new ScrollingStitcher(new ScrollFrame(width, height, pixels));
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var capture = ScreenshotCaptureService.SaveScrollingCapture(stitcher);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            using var temporary = new TemporaryScreenshot(capture.FilePath);
            Console.WriteLine($"Streaming save allocated {allocated / 1_000_000d:F2} MB for a {width}×{height} image.");
            Require(allocated < 10_000_000 && capture.Preview.PixelWidth <= 512 && capture.Preview.PixelHeight <= 256);
            var full = ScreenshotCaptureService.LoadFullImage(capture.FilePath);
            Require(full.PixelWidth == width && full.PixelHeight == height);
            var folder = Path.Combine(Environment.CurrentDirectory, "obj", "scroll-export-test");
            Directory.CreateDirectory(folder);
            var png = Path.Combine(folder, "full.png");
            var bmp = Path.Combine(folder, "full.bmp");
            var jpeg = Path.Combine(folder, "full.jpg");
            ScreenshotFileService.Export(capture.FilePath, png, capture.Preview);
            ScreenshotFileService.Export(capture.FilePath, bmp, full);
            ScreenshotFileService.Export(capture.FilePath, jpeg, full);
            Require(File.ReadAllBytes(png).SequenceEqual(File.ReadAllBytes(capture.FilePath)));
            Require(ScreenshotCaptureService.LoadFullImage(bmp).PixelWidth == width);
            Require(ScreenshotCaptureService.LoadFullImage(jpeg).PixelHeight == height);
            File.Delete(png);
            File.Delete(bmp);
            File.Delete(jpeg);
        });
        check("Streaming PNG preserves pixels across multiple data chunks", () =>
        {
            const int width = 320, height = 240;
            var source = new byte[width * height * 4];
            new Random(41).NextBytes(source);
            using var stitcher = new ScrollingStitcher(new ScrollFrame(width, height, source));
            var capture = ScreenshotCaptureService.SaveScrollingCapture(stitcher);
            using var temporary = new TemporaryScreenshot(capture.FilePath);
            Require(new FileInfo(capture.FilePath).Length > 128_000);
            var decoded = new FormatConvertedBitmap(ScreenshotCaptureService.LoadFullImage(capture.FilePath), PixelFormats.Bgr32, null, 0);
            var actual = new byte[source.Length];
            decoded.CopyPixels(actual, width * 4, 0);
            Require(actual.Where((_, index) => index % 4 != 3)
                .SequenceEqual(source.Where((_, index) => index % 4 != 3)));
        });
        check("Streaming PNG handles a one-pixel capture", () =>
        {
            using var stitcher = new ScrollingStitcher(new ScrollFrame(1, 1, [29, 71, 193, 0]));
            var capture = ScreenshotCaptureService.SaveScrollingCapture(stitcher);
            using var temporary = new TemporaryScreenshot(capture.FilePath);
            var decoded = new FormatConvertedBitmap(ScreenshotCaptureService.LoadFullImage(capture.FilePath), PixelFormats.Bgr32, null, 0);
            var pixel = new byte[4];
            decoded.CopyPixels(pixel, 4, 0);
            Require(pixel[0] == 29 && pixel[1] == 71 && pixel[2] == 193);
        });
        check("Live screen scrolling stitches speed changes and pauses exactly", LiveScreenCapture);
        HorizontalScrollingCaptureTests.Run(check);
    }

    private static void LiveScreenCapture()
    {
        const int width = 360, viewport = 360, header = 30, footer = 65;
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var bounds = new Rectangle(screen.Left + 80, screen.Top + 80, width, viewport);
        using var content = new Bitmap(width, 1200, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(content))
        using (var font = new Font("Segoe UI", 9))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(28, 29, 35));
            for (var entry = 0; entry < 18; entry++)
            {
                var top = entry * 62;
                graphics.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(48, 49, 58)), 12, top + 4, 330, 49);
                graphics.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(104, 85, 199)), 18, top + 11, 86, 15);
                graphics.DrawString($"Message {entry + 1}: detail {entry * 71 + 23}", font,
                    System.Drawing.Brushes.White, 18, top + 30);
            }
        }
        using var form = new LiveScrollForm(content, header, footer)
        {
            Bounds = bounds,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual
        };
        CaptureAreaOutlineWindow? outline = null;
        ScrollingCaptureWindow? controls = null;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            form.Show();
            form.Update();
            DesktopWindowService.FlushPendingUpdates();
            var frames = new List<(int Offset, ScrollFrame Image)> { (0, ScrollFrame.Capture(bounds)) };
            outline = new CaptureAreaOutlineWindow(bounds);
            outline.Show();
            controls = new ScrollingCaptureWindow(bounds);
            controls.Show();
            Require(outline.CanExcludeFromCapture && outline.IsClickThrough);
            ((System.Windows.Controls.Button)controls.FindName("StartButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var stop = (System.Windows.Controls.Button)controls.FindName("StopButton");
            PumpUntil(() => stop.IsEnabled, controls.Dispatcher);
            var sequence = new (int Offset, int Delay)[]
            {
                (0, 280), (1, 120), (4, 100), (28, 100), (85, 80),
                (142, 40), (205, 40), (260, 250), (310, 100), (400, 80),
                (470, 350), (520, 80), (615, 100)
            };
            foreach (var (offset, delay) in sequence)
            {
                form.Offset = offset;
                form.Invalidate();
                form.Update();
                DesktopWindowService.FlushPendingUpdates();
                frames.Add((offset, ScrollFrame.Capture(bounds)));
                PumpFor(delay, controls.Dispatcher);
            }
            stop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => controls.Completion.IsCompleted, controls.Dispatcher);
            using var image = controls.Completion.GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Live scrolling capture was cancelled.");
            var expected = new byte[width * (viewport + sequence[^1].Offset) * 4];
            var stride = width * 4;
            var contentEnd = viewport - footer;
            Buffer.BlockCopy(frames[0].Image.Pixels, 0, expected, 0, contentEnd * stride);
            var written = contentEnd * stride;
            for (var index = 1; index < frames.Count; index++)
            {
                var moved = frames[index].Offset - frames[index - 1].Offset;
                Buffer.BlockCopy(frames[index].Image.Pixels, (contentEnd - moved) * stride,
                    expected, written, moved * stride);
                written += moved * stride;
            }
            Buffer.BlockCopy(frames[^1].Image.Pixels, contentEnd * stride, expected, written, footer * stride);
            var capture = ScreenshotCaptureService.SaveScrollingCapture(image);
            using var temporary = new TemporaryScreenshot(capture.FilePath);
            var decoded = new FormatConvertedBitmap(ScreenshotCaptureService.LoadFullImage(capture.FilePath), PixelFormats.Bgr32, null, 0);
            var actual = new byte[width * image.Height * 4];
            decoded.CopyPixels(actual, width * 4, 0);
            var folder = Path.Combine(Environment.CurrentDirectory, "obj", "scroll-live");
            Directory.CreateDirectory(folder);
            File.Copy(capture.FilePath, Path.Combine(folder, "actual.png"), overwrite: true);
            using (var reference = BitmapFromPixels(width, viewport + sequence[^1].Offset, expected))
                reference.Save(Path.Combine(folder, "expected.png"), ImageFormat.Png);
            if (!actual.Where((_, index) => index % 4 != 3)
                .SequenceEqual(expected.Where((_, index) => index % 4 != 3)))
            {
                var firstDifferentByte = actual.Zip(expected)
                    .TakeWhile((pair, index) => index % 4 == 3 || pair.First == pair.Second).Count();
                throw new InvalidOperationException($"Live scrolling differs at output row {firstDifferentByte / stride}. Actual {image.Width} × {image.Height}; expected {width} × {viewport + sequence[^1].Offset}. See obj/scroll-live.");
            }
        }
        finally
        {
            controls?.Close();
            outline?.Close();
            form.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    internal static int Preview()
    {
        var application = new System.Windows.Application();
        var folder = Path.Combine(Environment.CurrentDirectory, "obj", "scroll-preview");
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        foreach (var direction in Enum.GetValues<ScrollDirection>())
        {
            var window = new ScrollingCaptureWindow(new Rectangle(0, 0, 300, 300));
            ((System.Windows.Controls.ComboBox)window.FindName("DirectionPicker")).SelectedIndex = (int)direction;
            ThemeIconService.ApplyViewerTheme(window.Resources, theme);
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle((System.Windows.Media.Brush)window.FindResource("ViewerPageBackgroundBrush"), null, new Rect(0, 0, window.Width, window.Height));
                context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, window.Width, window.Height));
            }
            var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var suffix = direction == ScrollDirection.Horizontal ? "horizontal-" : "";
            var path = Path.Combine(folder, $"controls-{suffix}{theme.ToString().ToLowerInvariant()}.png");
            using (var output = File.Create(path)) encoder.Save(output);
            Console.WriteLine(path);
            window.Close();
        }
        application.Shutdown();
        return 0;
    }

    private static ScrollFrame Frame(byte[] source, int width, int startRow, int height)
    {
        var pixels = new byte[width * height * 4];
        Buffer.BlockCopy(source, startRow * width * 4, pixels, 0, pixels.Length);
        return new ScrollFrame(width, height, pixels);
    }

    private static ScrollFrame PatternFrame(int width, int height, int startRow)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var value = unchecked((uint)(startRow + y) * 2654435761u);
            for (var x = 0; x < width; x++)
            {
                value = unchecked(value * 1664525u + 1013904223u);
                var pixel = (y * width + x) * 4;
                pixels[pixel] = (byte)value;
                pixels[pixel + 1] = (byte)(value >> 8);
                pixels[pixel + 2] = (byte)(value >> 16);
            }
        }
        return new ScrollFrame(width, height, pixels);
    }

    private static ScrollFrame FrameWithFooter(byte[] source, int width, int startRow, int height, int footerHeight,
        bool changedFooter = false)
    {
        var frame = Frame(source, width, startRow, height);
        for (var y = height - footerHeight; y < height; y++)
        for (var x = 0; x < width; x++)
            SetPixel(frame.Pixels, width, x, y, 28, 28, 34);
        for (var y = height - footerHeight + 12; y < height - footerHeight + 30; y++)
        for (var x = 12; x < 140; x++)
            SetPixel(frame.Pixels, width, x, y, changedFooter ? (byte)90 : (byte)75, 40, 95);
        for (var y = height - 18; y < height - 15; y++)
        for (var x = 15; x < 110; x++)
            SetPixel(frame.Pixels, width, x, y, 190, 190, 195);
        return frame;
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, byte blue, byte green, byte red)
    {
        var offset = (y * width + x) * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
    }

    private static byte[] BitmapPixels(Bitmap image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { image.UnlockBits(data); }
        return pixels;
    }

    private static Bitmap BitmapFromPixels(int width, int height, byte[] pixels)
    {
        var image = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
        finally { image.UnlockBits(data); }
        return image;
    }

    private static void PumpFor(int milliseconds, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class LiveScrollForm : System.Windows.Forms.Form
    {
        private readonly Bitmap content;
        private readonly int header;
        private readonly int footer;
        internal int Offset { get; set; }

        internal LiveScrollForm(Bitmap content, int header, int footer)
        {
            this.content = content;
            this.header = header;
            this.footer = footer;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            e.Graphics.Clear(System.Drawing.Color.FromArgb(28, 29, 35));
            var body = e.Graphics.Save();
            e.Graphics.SetClip(new Rectangle(0, header, ClientSize.Width, ClientSize.Height - header - footer));
            e.Graphics.DrawImageUnscaled(content, 0, header - Offset);
            e.Graphics.Restore(body);
            using var headerBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(38, 39, 48));
            using var footerBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(25, 26, 31));
            using var accentBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(87, 43, 108));
            using var inputBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(60, 61, 71));
            e.Graphics.FillRectangle(headerBrush,
                0, 0, ClientSize.Width, header);
            e.Graphics.FillRectangle(footerBrush,
                0, ClientSize.Height - footer, ClientSize.Width, footer);
            e.Graphics.FillRectangle(accentBrush,
                12, ClientSize.Height - footer + 12, ClientSize.Width - 24, 18);
            e.Graphics.FillRectangle(inputBrush,
                12, ClientSize.Height - footer + 37, ClientSize.Width - 24, 19);
        }
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

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Scrolling capture assertion failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
}
