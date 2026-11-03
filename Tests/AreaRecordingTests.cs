using System.IO;
using System.Reflection;
using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using SixLabors.ImageSharp;
using Application = System.Windows.Application;

internal static class AreaRecordingTests
{
    internal static void Run(Action<string, Action> check)
    {
        check("Selected video area produces an MP4 file", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 121, 101);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            var result = AreaRecordingService.Record(bounds, RecordingFormat.Video, stop.Token, CancellationToken.None);
            try
            {
                Require(result.Preview.PixelWidth == 122 && result.Preview.PixelHeight == 102);
                using var stream = File.OpenRead(result.FilePath);
                var header = new byte[12];
                Require(stream.Read(header) == header.Length &&
                    System.Text.Encoding.ASCII.GetString(header, 4, 4) == "ftyp" && stream.Length > 1000);
                Require(Mp4SampleCount(result.FilePath) > 1);
                var player = new System.Windows.Media.MediaPlayer();
                bool opened = false;
                string? error = null;
                player.MediaOpened += (_, _) => opened = true;
                player.MediaFailed += (_, args) => error = args.ErrorException.Message;
                player.Open(new Uri(result.FilePath));
                PumpUntil(() => opened || error is not null);
                Require(error is null && player.HasVideo &&
                    player.NaturalVideoWidth == 122 && player.NaturalVideoHeight == 102);
                player.Close();
            }
            finally { ScreenshotFileService.TryDelete(result.FilePath); }
        });
        check("Selected GIF area produces timed animation", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 121, 101);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var result = AreaRecordingService.Record(bounds, RecordingFormat.Gif, stop.Token, CancellationToken.None);
            try
            {
                using var stream = File.OpenRead(result.FilePath);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Require(decoder.Frames.Count > 1 && result.Preview.PixelWidth == bounds.Width);
                using var animation = SixLabors.ImageSharp.Image.Load(result.FilePath);
                Require(animation.Frames.Count > 1 &&
                    animation.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay > 0 &&
                    animation.Metadata.GetGifMetadata().RepeatCount == 0);
            }
            finally { ScreenshotFileService.TryDelete(result.FilePath); }
        });
        check("Immediate stop still writes one valid frame in each format", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 120, 100);
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            foreach (var format in new[] { RecordingFormat.Video, RecordingFormat.Gif })
            {
                var result = AreaRecordingService.Record(bounds, format, stop.Token, CancellationToken.None);
                try
                {
                    Require(File.Exists(result.FilePath) && new FileInfo(result.FilePath).Length > 100);
                    if (format == RecordingFormat.Gif)
                    {
                        using var stream = File.OpenRead(result.FilePath);
                        Require(new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad).Frames.Count == 1);
                    }
                    else
                    {
                        var bytes = File.ReadAllBytes(result.FilePath);
                        Require(System.Text.Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp" &&
                            Mp4SampleCount(result.FilePath) == 1);
                    }
                }
                finally { ScreenshotFileService.TryDelete(result.FilePath); }
            }
        });
        check("GIF output scales a large selected area", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 1001, 701);
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            var result = AreaRecordingService.Record(bounds, RecordingFormat.Gif, stop.Token, CancellationToken.None);
            try
            {
                Require(result.Preview.PixelWidth == 640 && result.Preview.PixelHeight == 448);
                using var stream = File.OpenRead(result.FilePath);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Require(decoder.Frames[0].PixelWidth == 640 && decoder.Frames[0].PixelHeight == 448);
            }
            finally { ScreenshotFileService.TryDelete(result.FilePath); }
        });
        check("GIF frames contain changes in the selected area", () =>
        {
            var bounds = new System.Drawing.Rectangle(0, 0, 120, 90);
            var captured = 0;
            byte[] NextFrame(System.Drawing.Rectangle _, int width, int height)
            {
                var red = Interlocked.Increment(ref captured) <= 2;
                var pixels = new byte[width * height * 4];
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index] = red ? (byte)0 : (byte)255;
                    pixels[index + 2] = red ? (byte)255 : (byte)0;
                    pixels[index + 3] = 255;
                }
                return pixels;
            }
            using var stop = new CancellationTokenSource();
            stop.CancelAfter(TimeSpan.FromMilliseconds(650));
            string? path = null;
            try
            {
                var result = AreaRecordingService.Record(bounds, RecordingFormat.Gif,
                    stop.Token, CancellationToken.None, NextFrame);
                path = result.FilePath;
                using var animation = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(path);
                Require(animation.Frames.Count >= 4);
                var first = animation.Frames[0][bounds.Width / 2, bounds.Height / 2];
                var last = animation.Frames[^1][bounds.Width / 2, bounds.Height / 2];
                Require(first.R > first.B + 100 && last.B > last.R + 100);
                Require(animation.Frames[0].Metadata.GetGifMetadata().FrameDelay == 12 &&
                    animation.Frames[1].Metadata.GetGifMetadata().FrameDelay == 13);
            }
            finally
            {
                if (path is not null) ScreenshotFileService.TryDelete(path);
            }
        });
        check("Cancelled recordings remove incomplete files", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 120, 100);
            foreach (var format in new[] { RecordingFormat.Video, RecordingFormat.Gif })
            {
                var before = RecordingFiles(format);
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                Throws<OperationCanceledException>(() => AreaRecordingService.Record(
                    bounds, format, CancellationToken.None, cancel.Token));
                Require(RecordingFiles(format).SetEquals(before));
            }
        });
        check("Capture errors remove incomplete recordings", () =>
        {
            var bounds = new System.Drawing.Rectangle(0, 0, 120, 100);
            foreach (var format in new[] { RecordingFormat.Video, RecordingFormat.Gif })
            {
                var before = RecordingFiles(format);
                Throws<InvalidOperationException>(() => AreaRecordingService.Record(
                    bounds, format, CancellationToken.None, CancellationToken.None,
                    (_, _, _) => throw new InvalidOperationException("Screen capture failed.")));
                Require(RecordingFiles(format).SetEquals(before));
            }
        });
        check("Invalid recording areas and formats are rejected", () =>
        {
            foreach (var format in new[] { RecordingFormat.Video, RecordingFormat.Gif })
            {
                Throws<ArgumentOutOfRangeException>(() => AreaRecordingService.Record(
                    new System.Drawing.Rectangle(0, 0, 0, 100), format, CancellationToken.None, CancellationToken.None));
                Throws<ArgumentOutOfRangeException>(() => AreaRecordingService.Record(
                    new System.Drawing.Rectangle(0, 0, 100, -1), format, CancellationToken.None, CancellationToken.None));
            }
            Throws<ArgumentOutOfRangeException>(() => AreaRecordingService.Record(
                new System.Drawing.Rectangle(0, 0, 100, 100), (RecordingFormat)99,
                CancellationToken.None, CancellationToken.None));
        });
        check("Recording duration formats seconds and elapsed minutes", () =>
        {
            Require(AreaRecordingWindow.FormatElapsed(TimeSpan.Zero) == "0:00");
            Require(AreaRecordingWindow.FormatElapsed(TimeSpan.FromSeconds(9)) == "0:09");
            Require(AreaRecordingWindow.FormatElapsed(TimeSpan.FromSeconds(60)) == "1:00");
            Require(AreaRecordingWindow.FormatElapsed(TimeSpan.FromSeconds(3661)) == "61:01");
        });
        check("Recording viewer fits wide, narrow and tall media to the screen", () =>
        {
            var desktop = new Rect(0, 0, 1920, 1080);
            var narrow = RecordingViewerWindow.CalculateWindowSize(830, 580, desktop);
            Require(narrow.Width == 862 && Math.Abs(narrow.Height - 756) < 0.01);
            var wide = RecordingViewerWindow.CalculateWindowSize(1920, 1080, desktop);
            Require(wide.Width == 1100 && wide.Height < 780);
            var tall = RecordingViewerWindow.CalculateWindowSize(320, 800, desktop);
            Require(tall.Width == 420 && tall.Height == 780);
            var compactScreen = RecordingViewerWindow.CalculateWindowSize(830, 580, new Rect(0, 0, 1366, 768));
            Require(compactScreen.Width < narrow.Width && compactScreen.Height == 704);
            Throws<ArgumentOutOfRangeException>(() => RecordingViewerWindow.CalculateWindowSize(0, 100, desktop));
            Throws<ArgumentOutOfRangeException>(() => RecordingViewerWindow.CalculateWindowSize(100, 0, desktop));
        });
        check("Recording popup offers view, save and copy without editing", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 40, 30);
            var preview = BitmapSource.Create(40, 30, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
                null, new byte[40 * 30 * 4], 40 * 4);
            var path = Path.Combine(Path.GetTempPath(), $"recording-{Guid.NewGuid():N}.gif");
            File.WriteAllBytes(path, [71, 73, 70]);
            var toast = new ScreenshotToastWindow(path, bounds, preview, recordingFormat: RecordingFormat.Gif);
            string? copiedPath = null;
            try
            {
                var view = (System.Windows.Controls.Button)toast.FindName("EditorButton");
                Require(view.Visibility == Visibility.Visible && (string)view.ToolTip == "View recording");
                Require(((System.Windows.FrameworkElement)toast.FindName("PlayGlyph")).Visibility == Visibility.Visible);
                Require(((System.Windows.FrameworkElement)toast.FindName("EditGlyph")).Visibility == Visibility.Collapsed);
                Require(((System.Windows.Controls.Button)toast.FindName("SaveButton")).IsEnabled);
                ((System.Windows.Controls.Button)toast.FindName("CopyButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(System.Windows.Clipboard.ContainsFileDropList());
                copiedPath = System.Windows.Clipboard.GetFileDropList()[0];
                Require(File.Exists(copiedPath) && File.ReadAllBytes(copiedPath).SequenceEqual(new byte[] { 71, 73, 70 }));
            }
            finally
            {
                toast.Close();
                TryClearClipboard();
                if (copiedPath is not null) ScreenshotFileService.TryDelete(copiedPath);
            }
            Require(!File.Exists(path));
        });
        check("GIF popup opens an animated read-only viewer with copy and save", () =>
        {
            var bounds = new System.Drawing.Rectangle(0, 0, 120, 90);
            var captured = 0;
            byte[] NextFrame(System.Drawing.Rectangle _, int width, int height)
            {
                var red = Interlocked.Increment(ref captured) <= 2;
                var pixels = new byte[width * height * 4];
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index] = red ? (byte)0 : (byte)255;
                    pixels[index + 2] = red ? (byte)255 : (byte)0;
                    pixels[index + 3] = 255;
                }
                return pixels;
            }
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            var result = AreaRecordingService.Record(bounds, RecordingFormat.Gif,
                stop.Token, CancellationToken.None, NextFrame);
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ScreenshotToastWindow? toast = null;
            RecordingViewerWindow? viewer = null;
            string? copy = null;
            try
            {
                toast = new ScreenshotToastWindow(result.FilePath, bounds, result.Preview,
                    recordingFormat: RecordingFormat.Gif);
                toast.Show();
                ((System.Windows.Controls.Button)toast.FindName("EditorButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                viewer = Application.Current.Windows.OfType<RecordingViewerWindow>().Single(window => window.IsVisible);
                Require(File.Exists(result.FilePath) &&
                    viewer.Width == 420 && viewer.Height < 780 && viewer.FindName("Annotations") is null &&
                    ((System.Windows.Controls.Button)viewer.FindName("CopyButton")).IsEnabled &&
                    ((System.Windows.Controls.Button)viewer.FindName("SaveButton")).IsEnabled);
                var image = (System.Windows.Controls.Image)viewer.FindName("GifImage");
                var playback = (System.Windows.Controls.Button)viewer.FindName("PlayPauseButton");
                PumpUntil(() => image.Source is WriteableBitmap && playback.IsEnabled);
                var stopButton = (System.Windows.Controls.Button)viewer.FindName("StopButton");
                var timeline = (System.Windows.Controls.Slider)viewer.FindName("PositionSlider");
                var bar = (System.Windows.FrameworkElement)viewer.FindName("PlaybackBar");
                var viewport = (System.Windows.FrameworkElement)viewer.FindName("RecordingViewport");
                Require(stopButton.IsEnabled && timeline.IsEnabled &&
                    Math.Abs(bar.ActualWidth - viewport.ActualWidth) < 1);
                var colors = new HashSet<int>();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (colors.Count < 2 && DateTime.UtcNow < deadline)
                {
                    var pixel = new byte[4];
                    ((WriteableBitmap)image.Source).CopyPixels(new Int32Rect(60, 45, 1, 1), pixel, 4, 0);
                    colors.Add(pixel[0] > pixel[2] ? 1 : 2);
                    PumpOnce();
                }
                Require(colors.Count == 2);
                playback.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require((string)playback.Content == "Play" && playback.ActualWidth >= 78);
                stopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require((string)playback.Content == "Play" && timeline.Value == 0);
                ((System.Windows.Controls.Button)viewer.FindName("CopyButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(System.Windows.Clipboard.ContainsFileDropList());
                copy = System.Windows.Clipboard.GetFileDropList()[0];
                Require(File.Exists(copy) && File.ReadAllBytes(copy).SequenceEqual(File.ReadAllBytes(result.FilePath)));
            }
            finally
            {
                viewer?.Close();
                toast?.Close();
                TryClearClipboard();
                if (copy is not null) ScreenshotFileService.TryDelete(copy);
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            Require(!File.Exists(result.FilePath));
        });
        check("Video popup opens a playable viewer and releases its file on close", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 120, 90);
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            var result = AreaRecordingService.Record(bounds, RecordingFormat.Video,
                stop.Token, CancellationToken.None);
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ScreenshotToastWindow? toast = null;
            RecordingViewerWindow? viewer = null;
            try
            {
                toast = new ScreenshotToastWindow(result.FilePath, bounds, result.Preview,
                    recordingFormat: RecordingFormat.Video);
                toast.Show();
                viewer = toast.OpenRecordingViewer();
                var player = (System.Windows.Controls.MediaElement)viewer.FindName("VideoPlayer");
                var playback = (System.Windows.Controls.Button)viewer.FindName("PlayPauseButton");
                PumpUntil(() => playback.IsEnabled);
                var stopButton = (System.Windows.Controls.Button)viewer.FindName("StopButton");
                var bar = (System.Windows.FrameworkElement)viewer.FindName("PlaybackBar");
                var viewport = (System.Windows.FrameworkElement)viewer.FindName("RecordingViewport");
                var toolbar = (System.Windows.FrameworkElement)viewer.FindName("ViewerToolbar");
                var stage = (System.Windows.FrameworkElement)viewer.FindName("RecordingStage");
                var zoomOut = (System.Windows.Controls.Button)viewer.FindName("ZoomOutButton");
                var toolbarBottom = zoomOut.TranslatePoint(new System.Windows.Point(0, zoomOut.ActualHeight), viewport).Y;
                var videoTop = stage.TranslatePoint(new System.Windows.Point(), viewport).Y;
                Require(player.Source?.LocalPath == result.FilePath &&
                    player.Visibility == Visibility.Visible && viewer.Width == 420 && viewer.Height < 780 &&
                    viewer.FindName("Annotations") is null && viewer.Icon is not null &&
                    stopButton.IsEnabled && Math.Abs(bar.ActualWidth - viewport.ActualWidth) < 1 &&
                    Math.Abs(toolbar.ActualWidth - viewport.ActualWidth) < 1 &&
                    player.Margin == new Thickness(0) && player.Stretch == System.Windows.Media.Stretch.Uniform &&
                    Math.Abs(videoTop - toolbarBottom - 8) < 1 &&
                    ((System.Windows.Controls.Button)viewer.FindName("CopyButton")).IsEnabled &&
                    ((System.Windows.Controls.Button)viewer.FindName("SaveButton")).IsEnabled);
                ((System.Windows.Controls.Button)viewer.FindName("ZoomInButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var scale = (System.Windows.Media.ScaleTransform)viewer.FindName("RecordingScale");
                Require(scale.ScaleX > 1 && scale.ScaleX == scale.ScaleY);
                ((System.Windows.Controls.Button)viewer.FindName("FitButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(scale.ScaleX == 1 && scale.ScaleY == 1);
                stopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require((string)playback.Content == "Play" && playback.ActualWidth >= 78);
                playback.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require((string)playback.Content == "Pause" && File.Exists(result.FilePath));
                stopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require((string)playback.Content == "Play");
            }
            finally
            {
                viewer?.Close();
                toast?.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            Require(!File.Exists(result.FilePath));
        });
        check("Video recording shows elapsed time and clears the red indicator", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 120, 100);
            var controls = new AreaRecordingWindow(bounds);
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(controls.Dispatcher));
            string? path = null;
            try
            {
                controls.Show();
                var indicator = (System.Windows.FrameworkElement)controls.FindName("RecordingIndicator");
                var duration = (System.Windows.Controls.TextBlock)controls.FindName("RecordingDuration");
                var video = (System.Windows.Controls.Button)controls.FindName("VideoButton");
                var gif = (System.Windows.Controls.Button)controls.FindName("GifButton");
                var stop = (System.Windows.Controls.Button)controls.FindName("StopButton");
                var cancelButton = (System.Windows.Controls.Button)controls.FindName("CancelButton");
                var actions = (System.Windows.Controls.StackPanel)controls.FindName("ActionsPanel");
                var videoSlot = actions.Children.IndexOf(video);
                var videoWidth = video.ActualWidth;
                var gifX = gif.TranslatePoint(new System.Windows.Point(), actions).X;
                var cancelX = cancelButton.TranslatePoint(new System.Windows.Point(), actions).X;
                Require(indicator.Visibility == Visibility.Collapsed && video.IsEnabled &&
                    stop.Visibility == Visibility.Collapsed && videoWidth > 0);
                video.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                controls.UpdateLayout();
                Require(indicator.Visibility == Visibility.Visible && stop.IsEnabled &&
                    duration.Text == "0:00" &&
                    video.Visibility == Visibility.Collapsed && stop.Visibility == Visibility.Visible &&
                    actions.Children.IndexOf(stop) == videoSlot && Math.Abs(stop.ActualWidth - videoWidth) < 1 &&
                    gif.Visibility == Visibility.Visible && !gif.IsEnabled &&
                    Math.Abs(gif.TranslatePoint(new System.Windows.Point(), actions).X - gifX) < 1 &&
                    Math.Abs(cancelButton.TranslatePoint(new System.Windows.Point(), actions).X - cancelX) < 1);
                var durationWidth = duration.ActualWidth;
                PumpUntil(() => duration.Text == "0:01");
                Require(Math.Abs(duration.ActualWidth - durationWidth) < 1 &&
                    Math.Abs(gif.TranslatePoint(new System.Windows.Point(), actions).X - gifX) < 1 &&
                    Math.Abs(cancelButton.TranslatePoint(new System.Windows.Point(), actions).X - cancelX) < 1);
                stop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(indicator.Visibility == Visibility.Collapsed && duration.Text == "0:01");
                PumpUntil(() => controls.Completion.IsCompleted);
                var result = controls.Completion.GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("Video recording was cancelled.");
                Require(result.Format == RecordingFormat.Video && File.Exists(result.FilePath));
                path = result.FilePath;
            }
            finally
            {
                controls.Close();
                if (path is not null) ScreenshotFileService.TryDelete(path);
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        check("Floating controls cancel either format without a result", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 120, 100);
            foreach (var format in new[] { RecordingFormat.Video, RecordingFormat.Gif })
            {
                var before = RecordingFiles(format);
                var controls = new AreaRecordingWindow(bounds);
                try
                {
                    controls.Show();
                    var start = (System.Windows.Controls.Button)controls.FindName(
                        format == RecordingFormat.Video ? "VideoButton" : "GifButton");
                    start.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Require(((System.Windows.FrameworkElement)controls.FindName("RecordingIndicator")).Visibility == Visibility.Visible);
                    Require(((System.Windows.Controls.TextBlock)controls.FindName("RecordingDuration")).Text == "0:00");
                    ((System.Windows.Controls.Button)controls.FindName("CancelButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Require(controls.Completion.IsCompletedSuccessfully && controls.Completion.Result is null);
                    var worker = (Task)typeof(AreaRecordingWindow)
                        .GetField("recording", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controls)!;
                    PumpUntil(() => worker.IsCompleted && RecordingFiles(format).SetEquals(before));
                }
                finally { controls.Close(); }
            }
        });
        check("Record area selection opens floating format choices and result popup", () =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var bounds = new System.Drawing.Rectangle(screen.Left + 20, screen.Top + 20, 100, 80);
            var launcher = new MainWindow(new CaptureShortcutSettingsStore(
                Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}.json")));
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(launcher.Dispatcher));
            try
            {
                var flow = launcher.RecordAreaAsync();
                var session = (CaptureSelectionSession)typeof(MonitorSelectionOverlayForm)
                    .GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                session.Complete(bounds);
                PumpUntil(() => Application.Current.Windows.OfType<AreaRecordingWindow>().Any(window => window.IsVisible));
                var controls = Application.Current.Windows.OfType<AreaRecordingWindow>().Single(window => window.IsVisible);
                var gif = (System.Windows.Controls.Button)controls.FindName("GifButton");
                var video = (System.Windows.Controls.Button)controls.FindName("VideoButton");
                var stop = (System.Windows.Controls.Button)controls.FindName("StopButton");
                var cancelButton = (System.Windows.Controls.Button)controls.FindName("CancelButton");
                var indicator = (System.Windows.FrameworkElement)controls.FindName("RecordingIndicator");
                var actions = (System.Windows.Controls.StackPanel)controls.FindName("ActionsPanel");
                var gifSlot = actions.Children.IndexOf(gif);
                var gifWidth = gif.ActualWidth;
                var videoX = video.TranslatePoint(new System.Windows.Point(), actions).X;
                var cancelX = cancelButton.TranslatePoint(new System.Windows.Point(), actions).X;
                Require(gif.IsEnabled && !stop.IsEnabled && stop.Visibility == Visibility.Collapsed && gifWidth > 0);
                Require(indicator.Visibility == Visibility.Collapsed);
                gif.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                controls.UpdateLayout();
                Require(stop.IsEnabled && gif.Visibility == Visibility.Collapsed &&
                    stop.Visibility == Visibility.Visible && actions.Children.IndexOf(stop) == gifSlot &&
                    Math.Abs(stop.ActualWidth - gifWidth) < 1 && video.Visibility == Visibility.Visible &&
                    !video.IsEnabled && indicator.Visibility == Visibility.Visible &&
                    Math.Abs(video.TranslatePoint(new System.Windows.Point(), actions).X - videoX) < 1 &&
                    Math.Abs(cancelButton.TranslatePoint(new System.Windows.Point(), actions).X - cancelX) < 1);
                Thread.Sleep(300);
                stop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(indicator.Visibility == Visibility.Collapsed);
                PumpUntil(() => flow.IsCompleted);
                flow.GetAwaiter().GetResult();
                var toast = Application.Current.Windows.OfType<ScreenshotToastWindow>().Single(window => window.IsVisible);
                Require(((System.Windows.Controls.Button)toast.FindName("EditorButton")).Visibility == Visibility.Visible);
                Require(launcher.IsVisible);
            }
            finally
            {
                MonitorSelectionOverlayForm.CancelActiveSelection();
                ScreenshotToastWindow.CloseCurrent();
                launcher.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Area recording assertion failed.");
    }

    private static HashSet<string> RecordingFiles(RecordingFormat format)
    {
        var folder = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, format == RecordingFormat.Video ? "recording_*.mp4" : "recording_*.gif")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static uint Mp4SampleCount(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var index = 4; index <= bytes.Length - 16; index++)
        {
            if (!bytes.AsSpan(index, 4).SequenceEqual("stsz"u8)) continue;
            var boxSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index - 4, 4));
            if (boxSize >= 20 && (long)index - 4 + boxSize <= bytes.Length)
                return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index + 12, 4));
        }
        throw new InvalidOperationException("MP4 sample table was not found.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.Background);
            Dispatcher.PushFrame(frame);
        }
        Require(done());
    }

    private static void PumpOnce()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }

    private static void TryClearClipboard()
    {
        try { System.Windows.Clipboard.Clear(); }
        catch (UnauthorizedAccessException) { }
    }
}
