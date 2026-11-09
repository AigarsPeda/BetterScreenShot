using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;

internal static class HorizontalScrollingCaptureTests
{
    internal static void Run(Action<string, Action> check)
    {
        check("Horizontal scrolling preserves every pixel and its orientation", () =>
        {
            const int width = 800, height = 137, viewport = 240;
            var source = Noise(width, height);
            using var stitcher = new ScrollingStitcher(Frame(source, width, height, 0, viewport), ScrollDirection.Horizontal);
            Require(stitcher.Add(Frame(source, width, height, 0, viewport)) == ScrollFrameResult.Unchanged);
            foreach (var offset in new[] { 3, 80, 185, 300, 420, 560 })
                Require(stitcher.Add(Frame(source, width, height, offset, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Width == width && stitcher.Height == height && stitcher.AddedFrames == 6);
            AssertPixels(stitcher, source);
            // Exercise cached output rows in a different order after sequential export.
            var row = new byte[width * 4];
            foreach (var y in new[] { height - 1, 0, height / 2 })
            {
                stitcher.CopyRow(y, row);
                Require(row.AsSpan().SequenceEqual(source.AsSpan(y * width * 4, row.Length)));
            }
        });
        check("Horizontal capture recovers from gaps, reverse movement and a wrong axis", () =>
        {
            const int width = 1000, height = 100, viewport = 220;
            var source = Noise(width, height);
            using var stitcher = new ScrollingStitcher(Frame(source, width, height, 0, viewport), ScrollDirection.Horizontal);
            Require(stitcher.Add(Frame(source, width, height, 100, viewport)) == ScrollFrameResult.Added);
            var savedBytes = stitcher.SpoolBytes;
            Require(stitcher.Add(Frame(source, width, height, 600, viewport)) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Add(Frame(source, width, height, 50, viewport)) == ScrollFrameResult.NoOverlap);
            var wrongAxis = Frame(source, width, height, 100, viewport);
            Buffer.BlockCopy(wrongAxis.Pixels, viewport * 4, wrongAxis.Pixels, 0, (height - 1) * viewport * 4);
            Require(stitcher.Add(wrongAxis) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Width == 320 && stitcher.SpoolBytes == savedBytes && stitcher.AddedFrames == 1);
            // Populate the output cache, then verify that a successful append invalidates it.
            stitcher.CopyRow(0, new byte[stitcher.Width * 4]);
            Require(stitcher.Add(Frame(source, width, height, 135, viewport)) == ScrollFrameResult.Added);
            AssertPixels(stitcher, Frame(source, width, height, 0, 355).Pixels);
        });
        check("Horizontal scrolling rejects ambiguous repeated columns", () =>
        {
            const int width = 500, height = 100, viewport = 240;
            var source = new byte[width * height * 4];
            for (var y = 15; y < 90; y++)
            for (var x = 0; x < width; x++)
                if (x % 40 is >= 5 and < 17)
                    source[(y * width + x) * 4] = 193;
            using var stitcher = new ScrollingStitcher(Frame(source, width, height, 0, viewport), ScrollDirection.Horizontal);
            Require(stitcher.Add(Frame(source, width, height, 25, viewport)) == ScrollFrameResult.NoOverlap);
            Require(stitcher.Width == viewport && stitcher.AddedFrames == 0);
        });
        check("Horizontal scrolling aligns sparse content on a white background", () =>
        {
            const int width = 720, height = 220, viewport = 240;
            var source = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
            for (var column = 0; column < 36; column++)
            for (var x = column * 19 + 3; x < column * 19 + 8; x++)
            for (var y = 12; y < 60 + column * 37 % 145; y++)
                source[(y * width + x) * 4] = source[(y * width + x) * 4 + 1] = source[(y * width + x) * 4 + 2] = 38;
            using var stitcher = new ScrollingStitcher(Frame(source, width, height, 0, viewport), ScrollDirection.Horizontal);
            Require(stitcher.Add(Frame(source, width, height, 102, viewport)) == ScrollFrameResult.Added);
            Require(stitcher.Add(Frame(source, width, height, 205, viewport)) == ScrollFrameResult.Added);
            AssertPixels(stitcher, Frame(source, width, height, 0, 445).Pixels);
        });
        check("A fixed right sidebar appears once using its latest pixels", () =>
        {
            const int width = 700, height = 180, viewport = 280, sidebar = 60;
            var source = Noise(width, height);
            ScrollFrame WithSidebar(int offset, byte color = 75)
            {
                var frame = Frame(source, width, height, offset, viewport);
                for (var y = 0; y < height; y++)
                for (var x = viewport - sidebar; x < viewport; x++)
                {
                    var pixel = (y * viewport + x) * 4;
                    frame.Pixels[pixel] = (byte)(color + x % 17);
                    frame.Pixels[pixel + 1] = (byte)(y % 101);
                    frame.Pixels[pixel + 2] = (byte)(x % 79);
                    frame.Pixels[pixel + 3] = 0;
                }
                return frame;
            }
            using var stitcher = new ScrollingStitcher(WithSidebar(0), ScrollDirection.Horizontal);
            Require(stitcher.Add(WithSidebar(70)) == ScrollFrameResult.Added);
            var final = WithSidebar(150, 90);
            Require(stitcher.Add(final) == ScrollFrameResult.Added);
            Require(stitcher.Width == viewport + 150 && stitcher.Height == height);
            var expected = Frame(source, width, height, 0, stitcher.Width).Pixels;
            for (var y = 0; y < height; y++)
                final.Pixels.AsSpan((y * viewport + viewport - sidebar) * 4, sidebar * 4)
                    .CopyTo(expected.AsSpan((y * stitcher.Width + stitcher.Width - sidebar) * 4));
            AssertPixels(stitcher, expected);
            AssertPng(stitcher, expected);
            Require(stitcher.SpoolBytes == (long)(stitcher.Width - sidebar) * height * 4);
        });
        check("Horizontal PNG export and its bounded preview preserve wide images", () =>
        {
            const int width = 900, height = 123, viewport = 300;
            var source = Noise(width, height);
            using var stitcher = new ScrollingStitcher(Frame(source, width, height, 0, viewport), ScrollDirection.Horizontal);
            for (var offset = 150; offset <= 600; offset += 150)
                Require(stitcher.Add(Frame(source, width, height, offset, viewport)) == ScrollFrameResult.Added);
            AssertPng(stitcher, source);
        });
        check("Horizontal output streams multiple row batches with bounded allocations", () =>
        {
            const int width = 2200, height = 2100;
            var source = Noise(width, height);
            using var stitcher = new ScrollingStitcher(new ScrollFrame(width, height, source), ScrollDirection.Horizontal);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var capture = ScreenshotCaptureService.SaveScrollingCapture(stitcher);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            using var temporary = new TemporaryScreenshot(capture.FilePath);
            Require(allocated < 8_000_000 && capture.Preview.PixelWidth <= 512 && capture.Preview.PixelHeight <= 256);
            AssertDecoded(capture.FilePath, width, height, source);
            // Cross batch boundaries and revisit the first and last rows.
            var row = new byte[width * 4];
            foreach (var y in new[] { height - 1, 0, 453, 454, 909, height - 1 })
            {
                stitcher.CopyRow(y, row);
                Require(row.AsSpan().SequenceEqual(source.AsSpan(y * row.Length, row.Length)));
            }
            Console.WriteLine($"Horizontal streaming save allocated {allocated / 1_000_000d:F2} MB for {width}×{height}.");
        });
        check("Horizontal capture handles one pixel, tiny viewports and unchanged flat images", () =>
        {
            foreach (var (width, height) in new[] { (1, 1), (2, 40), (40, 1), (32, 15), (240, 100) })
            {
                var source = new byte[width * height * 4];
                using var stitcher = new ScrollingStitcher(new ScrollFrame(width, height, source), ScrollDirection.Horizontal);
                Require(stitcher.Add(new ScrollFrame(width, height, source)) == ScrollFrameResult.Unchanged);
                AssertPixels(stitcher, source);
                AssertPng(stitcher, source);
                if (width <= 32)
                    Require(stitcher.Add(new ScrollFrame(width, height, Noise(width, height))) == ScrollFrameResult.NoOverlap);
            }
        });
        check("Scrolling capture validates malformed frames and row requests for both directions", () =>
        {
            foreach (var direction in Enum.GetValues<ScrollDirection>())
            {
                foreach (var invalid in new[] { new ScrollFrame(0, 1, []), new ScrollFrame(-1, 1, []),
                    new ScrollFrame(1, 0, []), new ScrollFrame(2, 2, new byte[15]), new ScrollFrame(1, 1, null!) })
                    Throws<ArgumentException>(() => new ScrollingStitcher(invalid, direction));
                Throws<ArgumentNullException>(() => new ScrollingStitcher(null!, direction));
                using var stitcher = new ScrollingStitcher(new ScrollFrame(120, 100, Noise(120, 100)), direction);
                Throws<ArgumentException>(() => stitcher.Add(new ScrollFrame(120, 100, new byte[1])));
                Throws<ArgumentException>(() => stitcher.Add(new ScrollFrame(100, 120, Noise(100, 120))));
                Throws<ArgumentNullException>(() => stitcher.Add(null!));
                Throws<ArgumentOutOfRangeException>(() => stitcher.CopyRow(-1, new byte[480]));
                Throws<ArgumentOutOfRangeException>(() => stitcher.CopyRow(100, new byte[480]));
                Throws<ArgumentException>(() => stitcher.CopyRow(0, new byte[479]));
                Require(stitcher.Width == 120 && stitcher.Height == 100 && stitcher.AddedFrames == 0);
            }
            Throws<ArgumentOutOfRangeException>(() => new ScrollingStitcher(new ScrollFrame(1, 1, new byte[4]), (ScrollDirection)99));
            Throws<InvalidOperationException>(() => new ScrollingStitcher(new ScrollFrame(6000, 5001, new byte[6000 * 5001 * 4]), ScrollDirection.Horizontal));
        });
        check("Horizontal capture enforces the length limit without changing accepted content", () =>
        {
            const int viewport = 1000, height = 20;
            using var stitcher = new ScrollingStitcher(PatternFrame(0, viewport, height), ScrollDirection.Horizontal);
            for (var offset = 750; offset <= 30_750; offset += 750)
                Require(stitcher.Add(PatternFrame(offset, viewport, height)) == ScrollFrameResult.Added);
            Require(stitcher.Add(PatternFrame(31_000, viewport, height)) == ScrollFrameResult.Added);
            var bytes = stitcher.SpoolBytes;
            Require(stitcher.Width == 32_000);
            Require(stitcher.Add(PatternFrame(31_003, viewport, height)) == ScrollFrameResult.LimitReached);
            Require(stitcher.Width == 32_000 && stitcher.Height == height && stitcher.SpoolBytes == bytes);
        });
        check("Horizontal capture enforces the pixel limit and deletes its spool", () =>
        {
            const int viewport = 1000, height = 990;
            var baseline = GC.GetTotalMemory(forceFullCollection: true);
            using var stitcher = new ScrollingStitcher(PatternFrame(0, viewport, height), ScrollDirection.Horizontal);
            var path = stitcher.SpoolPath;
            for (var offset = 750; offset <= 29_250; offset += 750)
                Require(stitcher.Add(PatternFrame(offset, viewport, height)) == ScrollFrameResult.Added);
            var retained = GC.GetTotalMemory(forceFullCollection: true) - baseline;
            var bytes = stitcher.SpoolBytes;
            Require(stitcher.Add(PatternFrame(29_400, viewport, height)) == ScrollFrameResult.LimitReached);
            Require(stitcher.Width == 30_250 && stitcher.SpoolBytes == bytes && bytes == 30_250L * height * 4);
            Require(retained < 40_000_000);
            stitcher.Dispose();
            Require(!File.Exists(path));
        });
        check("Scrolling direction defaults to vertical and updates instructions before Start", DirectionSelection);
        check("Horizontal controls lock the direction and complete an unscrolled capture", HorizontalControls);
        check("Cancelling horizontal capture deletes its temporary columns", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var window = new ScrollingCaptureWindow(new Rectangle(screen.Left + 20, screen.Top + 20, 150, 100));
            WithContext(window, () =>
            {
                window.ShowForTest();
                ((System.Windows.Controls.ComboBox)window.FindName("DirectionPicker")).SelectedIndex = 1;
                Click(window, "StartButton");
                PumpUntil(() => window.SpoolPath is not null, window.Dispatcher);
                var path = window.SpoolPath!;
                Click(window, "CancelButton");
                Require(window.Completion.GetAwaiter().GetResult() is null && !File.Exists(path));
            });
        });
        check("Failed scrolling startup restores the direction selector and Start", FailedStart);
        check("Live horizontal capture stitches screen pixels and exports the final image", LiveCapture);
        check("Scrolling direction and actions align with the recording toolbar in both themes", ToolbarLayout);
        check("Scrolling direction menu stays on screen near the bottom edge", DirectionMenu);
    }

    private static void ToolbarLayout()
    {
        var bounds = new Rectangle(0, 0, 120, 100);
        var scrolling = new ScrollingCaptureWindow(bounds);
        var recording = new AreaRecordingWindow(bounds);
        try
        {
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            foreach (var direction in new[] { 0, 1 })
            {
                ThemeIconService.ApplyViewerTheme(scrolling.Resources, theme);
                ThemeIconService.ApplyViewerTheme(recording.Resources, theme);
                ((System.Windows.Controls.ComboBox)scrolling.FindName("DirectionPicker")).SelectedIndex = direction;
                Arrange(scrolling);
                Arrange(recording);
                Match("DirectionPicker", "VideoQualityPicker");
                Match("StartButton", "VideoButton");
                Match("StopButton", "GifButton");
                Match("CancelButton", "CancelButton");
                var label = (FrameworkElement)scrolling.FindName("DirectionLabel");
                var picker = (FrameworkElement)scrolling.FindName("DirectionPicker");
                var actions = (FrameworkElement)scrolling.FindName("ActionsPanel");
                var status = (FrameworkElement)scrolling.FindName("CaptureStatus");
                var root = (FrameworkElement)scrolling.Content;
                Require(label.TranslatePoint(new System.Windows.Point(label.ActualWidth, 0), root).X
                    < picker.TranslatePoint(new System.Windows.Point(), root).X);
                var pickerBottom = picker.TranslatePoint(new System.Windows.Point(0, picker.ActualHeight), root).Y;
                var actionsTop = actions.TranslatePoint(new System.Windows.Point(), root).Y;
                var actionsBottom = actions.TranslatePoint(new System.Windows.Point(0, actions.ActualHeight), root).Y;
                var statusTop = status.TranslatePoint(new System.Windows.Point(), root).Y;
                var statusBottom = status.TranslatePoint(new System.Windows.Point(0, status.ActualHeight), root).Y;
                Require(pickerBottom + 4 <= actionsTop && actionsBottom + 4 <= statusTop && statusBottom <= scrolling.Height - 10);
            }
        }
        finally { scrolling.Close(); recording.Close(); }

        static void Arrange(Window window)
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
        }
        void Match(string scrollingName, string recordingName)
        {
            var actual = (FrameworkElement)scrolling.FindName(scrollingName);
            var reference = (FrameworkElement)recording.FindName(recordingName);
            var point = actual.TranslatePoint(new System.Windows.Point(), (FrameworkElement)scrolling.Content);
            var referencePoint = reference.TranslatePoint(new System.Windows.Point(), (FrameworkElement)recording.Content);
            Require(Math.Abs(point.X - referencePoint.X) < 1 && Math.Abs(point.Y - referencePoint.Y) < 1);
            Require(Math.Abs(actual.ActualWidth - reference.ActualWidth) < 1 && Math.Abs(actual.ActualHeight - reference.ActualHeight) < 1);
        }
    }

    private static void DirectionMenu()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        foreach (var direction in new[] { 0, 1 })
        {
            var window = new ScrollingCaptureWindow(new Rectangle(screen.Left + 20, screen.Top + 20, 120, 100));
            try
            {
                ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                window.ShowForTest();
                window.Top = SystemParameters.WorkArea.Bottom - window.ActualHeight - 8;
                var picker = (System.Windows.Controls.ComboBox)window.FindName("DirectionPicker");
                picker.SelectedIndex = direction;
                picker.IsDropDownOpen = true;
                var popup = (System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup", picker);
                var last = (ComboBoxItem)picker.Items[1];
                PumpUntil(() => popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 0 }
                    && last.IsVisible && last.ActualHeight > 0, window.Dispatcher);
                var popupTop = popup.Child.PointToScreen(new System.Windows.Point()).Y;
                var popupBottom = popup.Child.PointToScreen(new System.Windows.Point(0, ((FrameworkElement)popup.Child).ActualHeight)).Y;
                var pickerTop = picker.PointToScreen(new System.Windows.Point()).Y;
                Require(popupTop >= screen.Top && popupBottom <= pickerTop + 1);
                Require(last.PointToScreen(new System.Windows.Point(0, last.ActualHeight)).Y <= popupBottom + 1);
                picker.IsDropDownOpen = false;
            }
            finally { window.Close(); }
        }
    }

    private static void DirectionSelection()
    {
        var window = new ScrollingCaptureWindow(new Rectangle(0, 0, 120, 100));
        try
        {
            var picker = (System.Windows.Controls.ComboBox)window.FindName("DirectionPicker");
            var status = (TextBlock)window.FindName("CaptureStatus");
            Require(picker.SelectedIndex == 0 && picker.IsEnabled && status.Text.Contains("downward"));
            picker.SelectedIndex = 1;
            Require(status.Text.Contains("rightward"));
            picker.SelectedIndex = 0;
            Require(status.Text.Contains("downward"));
        }
        finally { window.Close(); }
    }

    private static void HorizontalControls()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var bounds = new Rectangle(screen.Left + 20, screen.Top + 20, 150, 100);
        var window = new ScrollingCaptureWindow(bounds);
        WithContext(window, () =>
        {
            window.ShowForTest();
            var picker = (System.Windows.Controls.ComboBox)window.FindName("DirectionPicker");
            picker.SelectedIndex = 1;
            Click(window, "StartButton");
            Require(!picker.IsEnabled);
            PumpUntil(() => ((System.Windows.Controls.Button)window.FindName("StopButton")).IsEnabled, window.Dispatcher);
            Require(((TextBlock)window.FindName("CaptureStatus")).Text.Contains("rightward"));
            Click(window, "StopButton");
            PumpUntil(() => window.Completion.IsCompleted, window.Dispatcher);
            using var result = window.Completion.GetAwaiter().GetResult();
            Require(result is { Direction: ScrollDirection.Horizontal, Width: 150, Height: 100 });
        });
    }

    private static void FailedStart()
    {
        var window = new ScrollingCaptureWindow(new Rectangle(0, 0, 6000, 6000));
        WithContext(window, () =>
        {
            window.ShowForTest();
            Click(window, "StartButton");
            Require(((System.Windows.Controls.Button)window.FindName("StartButton")).IsEnabled);
            Require(((System.Windows.Controls.ComboBox)window.FindName("DirectionPicker")).IsEnabled);
            Require(((TextBlock)window.FindName("CaptureStatus")).Text.Contains("too large"));
            Require(window.SpoolPath is null);
        });
    }

    private static void LiveCapture()
    {
        const int width = 740, height = 130, viewport = 280;
        var source = Noise(width, height);
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try { Marshal.Copy(source, 0, data.Scan0, source.Length); }
        finally { bitmap.UnlockBits(data); }
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var bounds = new Rectangle(area.Left + 60, area.Top + 60, viewport, height);
        using var form = new HorizontalForm(bitmap) { Bounds = bounds, StartPosition = System.Windows.Forms.FormStartPosition.Manual };
        var window = new ScrollingCaptureWindow(bounds);
        WithContext(window, () =>
        {
            form.Show();
            form.Update();
            DesktopWindowService.FlushPendingUpdates();
            window.ShowForTest();
            ((System.Windows.Controls.ComboBox)window.FindName("DirectionPicker")).SelectedIndex = 1;
            Click(window, "StartButton");
            PumpUntil(() => ((System.Windows.Controls.Button)window.FindName("StopButton")).IsEnabled, window.Dispatcher);
            var captured = (ScrollingStitcher)typeof(ScrollingCaptureWindow)
                .GetField("stitcher", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            foreach (var offset in new[] { 0, 75, 160, 300, 460 })
            {
                form.Offset = offset;
                form.Refresh();
                DesktopWindowService.FlushPendingUpdates();
                PumpUntil(() => captured.Width == viewport + offset, window.Dispatcher);
            }
            Click(window, "StopButton");
            PumpUntil(() => window.Completion.IsCompleted, window.Dispatcher);
            using var result = window.Completion.GetAwaiter().GetResult()!;
            Require(result.Width == width && result.Height == height && !window.MayHaveGaps);
            AssertPng(result, source);
        });
    }

    private sealed class HorizontalForm : System.Windows.Forms.Form
    {
        private readonly Bitmap bitmap;
        internal int Offset { get; set; }
        internal HorizontalForm(Bitmap bitmap)
        {
            this.bitmap = bitmap;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
        }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) => e.Graphics.DrawImageUnscaled(bitmap, -Offset, 0);
    }

    private static ScrollFrame Frame(byte[] source, int width, int height, int left, int viewport)
    {
        var pixels = new byte[viewport * height * 4];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(source, (y * width + left) * 4, pixels, y * viewport * 4, viewport * 4);
        return new ScrollFrame(viewport, height, pixels);
    }

    private static byte[] Noise(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        new Random(173).NextBytes(pixels);
        return pixels;
    }

    private static ScrollFrame PatternFrame(int left, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var x = 0; x < width; x++)
        {
            var value = unchecked((uint)(left + x) * 2654435761u);
            for (var y = 0; y < height; y++)
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

    private static void AssertPixels(ScrollingStitcher stitcher, byte[] expected)
    {
        using var bitmap = stitcher.BuildBitmap();
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var actual = new byte[stitcher.Width * stitcher.Height * 4];
            Marshal.Copy(data.Scan0, actual, 0, actual.Length);
            Require(actual.AsSpan().SequenceEqual(expected));
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static void AssertPng(ScrollingStitcher stitcher, byte[] expected)
    {
        var capture = ScreenshotCaptureService.SaveScrollingCapture(stitcher);
        using var temporary = new TemporaryScreenshot(capture.FilePath);
        Require(capture.Preview.PixelWidth <= 512 && capture.Preview.PixelHeight <= 256);
        AssertDecoded(capture.FilePath, stitcher.Width, stitcher.Height, expected);
    }

    private static void AssertDecoded(string path, int width, int height, byte[] expected)
    {
        var decoded = new FormatConvertedBitmap(ScreenshotCaptureService.LoadFullImage(path), PixelFormats.Bgr32, null, 0);
        Require(decoded.PixelWidth == width && decoded.PixelHeight == height);
        var actual = new byte[width * height * 4];
        decoded.CopyPixels(actual, width * 4, 0);
        for (var i = 0; i < actual.Length; i++)
            if (i % 4 != 3 && actual[i] != expected[i])
                throw new InvalidOperationException($"Horizontal output differs at pixel {i / 4 % width}, {i / 4 / width}.");
    }

    private static void WithContext(Window window, Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try { action(); }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void Click(Window window, string name) => ((System.Windows.Controls.Button)window.FindName(name))
        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

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
        if (!condition) throw new InvalidOperationException("Horizontal scrolling capture assertion failed.");
    }
}
