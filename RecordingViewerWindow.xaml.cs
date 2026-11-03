using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterScreenShot;

public partial class RecordingViewerWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const double HorizontalWindowPadding = 32;
    private const double VerticalWindowPadding = 176;
    private readonly TemporaryScreenshot recording;
    private readonly RecordingFormat format;
    private readonly DispatcherTimer gifTimer = new();
    private readonly DispatcherTimer videoTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Task<SixLabors.ImageSharp.Image<Bgra32>>? gifLoadTask;
    private SixLabors.ImageSharp.Image<Bgra32>? gif;
    private WriteableBitmap? gifBitmap;
    private byte[]? gifPixels;
    private int gifFrame;
    private bool isPlaying;
    private bool isClosed;
    private bool isUpdatingTimeline;

    internal RecordingViewerWindow(TemporaryScreenshot recording, RecordingFormat format, BitmapSource poster)
    {
        InitializeComponent();
        this.recording = recording;
        this.format = format;
        PosterImage.Source = poster;
        var initialSize = CalculateWindowSize(poster.PixelWidth, poster.PixelHeight, SystemParameters.WorkArea);
        Width = initialSize.Width;
        Height = initialSize.Height;
        Title = format == RecordingFormat.Video ? "Video recording" : "Animated GIF";
        ApplyTheme();
        gifTimer.Tick += (_, _) => ShowGifFrame((gifFrame + 1) % gif!.Frames.Count);
        videoTimer.Tick += (_, _) => UpdateVideoTimeline();
        Loaded += RecordingViewerWindow_Loaded;
        SourceInitialized += RecordingViewerWindow_SourceInitialized;
        Closed += RecordingViewerWindow_Closed;
        ThemePreferenceService.Changed += ThemePreference_Changed;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    internal static System.Windows.Size CalculateWindowSize(int pixelWidth, int pixelHeight, Rect workArea)
    {
        if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var maxWidth = Math.Max(420, Math.Min(1100, workArea.Width - 64));
        var maxHeight = Math.Max(300, Math.Min(780, workArea.Height - 64));
        var aspect = (double)pixelWidth / pixelHeight;
        var width = Math.Clamp(pixelWidth + HorizontalWindowPadding, 420, maxWidth);
        var height = (width - HorizontalWindowPadding) / aspect + VerticalWindowPadding;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = Math.Clamp((height - VerticalWindowPadding) * aspect + HorizontalWindowPadding, 420, maxWidth);
        }
        return new System.Windows.Size(width, Math.Clamp(height, 300, maxHeight));
    }

    private void ApplyTheme()
    {
        var theme = ThemePreferenceService.CurrentTheme;
        ThemeIconService.ApplyViewerTheme(Resources, theme);
        Icon = ThemeIconService.CreateScanIcon(theme);
        ApplyTitleBarTheme(theme);
    }

    private void RecordingViewerWindow_SourceInitialized(object? sender, EventArgs e) =>
        ApplyTitleBarTheme(ThemePreferenceService.CurrentTheme);

    private void ApplyTitleBarTheme(AppTheme theme)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var darkModeEnabled = theme == AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkModeEnabled, sizeof(int));
    }
    private void ThemePreference_Changed() => Dispatcher.BeginInvoke(new Action(ApplyTheme));
    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            ThemePreference_Changed();
    }

    private async void RecordingViewerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= RecordingViewerWindow_Loaded;
        if (format == RecordingFormat.Video)
        {
            VideoPlayer.Visibility = Visibility.Visible;
            VideoPlayer.Source = new Uri(recording.FilePath);
            VideoPlayer.Play();
            return;
        }

        gifLoadTask = Task.Run(() => SixLabors.ImageSharp.Image.Load<Bgra32>(recording.FilePath));
        try
        {
            var loaded = await gifLoadTask;
            if (isClosed) return;
            gif = loaded;
            gifPixels = new byte[checked(gif.Width * gif.Height * 4)];
            gifBitmap = new WriteableBitmap(gif.Width, gif.Height, 96, 96, PixelFormats.Bgra32, null);
            GifImage.Source = gifBitmap;
            GifImage.Visibility = Visibility.Visible;
            PosterImage.Visibility = Visibility.Collapsed;
            PlaybackStatus.Visibility = Visibility.Collapsed;
            ShowGifFrame(0);
            if (gif.Frames.Count > 1)
            {
                PlayPauseButton.IsEnabled = true;
                StopButton.IsEnabled = true;
                PositionSlider.Maximum = gif.Frames.Count - 1;
                PositionSlider.IsEnabled = true;
                SetPlaying(true);
                gifTimer.Start();
            }
        }
        catch (Exception error)
        {
            if (!isClosed) PlaybackStatus.Text = $"Could not display GIF: {error.Message}";
        }
    }

    private void ShowGifFrame(int index)
    {
        var frame = gif!.Frames[index];
        frame.CopyPixelDataTo(gifPixels!);
        gifBitmap!.WritePixels(new Int32Rect(0, 0, gif.Width, gif.Height), gifPixels!, gif.Width * 4, 0);
        gifFrame = index;
        gifTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, frame.Metadata.GetGifMetadata().FrameDelay * 10));
        isUpdatingTimeline = true;
        PositionSlider.Value = index;
        TimeText.Text = $"Frame {index + 1} / {gif.Frames.Count}";
        isUpdatingTimeline = false;
    }

    private void Video_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (isClosed) return;
        PosterImage.Visibility = Visibility.Collapsed;
        PlaybackStatus.Visibility = Visibility.Collapsed;
        PlayPauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        SetPlaying(true);
        videoTimer.Start();
        UpdateVideoTimeline();
    }

    private void Video_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!isClosed) StopPlayback();
    }

    private void Video_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (isClosed) return;
        PlayPauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        PositionSlider.IsEnabled = false;
        videoTimer.Stop();
        PlaybackStatus.Text = $"Could not play video: {e.ErrorException.Message}";
        PlaybackStatus.Visibility = Visibility.Visible;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (format == RecordingFormat.Video)
        {
            if (isPlaying) VideoPlayer.Pause();
            else VideoPlayer.Play();
        }
        else
        {
            if (isPlaying) gifTimer.Stop();
            else gifTimer.Start();
        }
        SetPlaying(!isPlaying);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        if (format == RecordingFormat.Video)
        {
            VideoPlayer.Pause();
            VideoPlayer.Position = TimeSpan.Zero;
            UpdateVideoTimeline();
        }
        else
        {
            gifTimer.Stop();
            ShowGifFrame(0);
        }
        SetPlaying(false);
    }

    private void SetPlaying(bool playing)
    {
        isPlaying = playing;
        PlayPauseButton.Content = isPlaying ? "Pause" : "Play";
        PlayPauseButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            isPlaying ? "Pause recording" : "Play recording");
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PositionSlider is null || isUpdatingTimeline || !PositionSlider.IsEnabled) return;
        if (format == RecordingFormat.Video)
        {
            VideoPlayer.Position = TimeSpan.FromMilliseconds(PositionSlider.Value);
            UpdateVideoTimeline();
        }
        else if (gif is not null)
        {
            ShowGifFrame(Math.Clamp((int)Math.Round(PositionSlider.Value), 0, gif.Frames.Count - 1));
        }
    }

    private void UpdateVideoTimeline()
    {
        if (!PositionSlider.IsEnabled && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = VideoPlayer.NaturalDuration.TimeSpan.TotalMilliseconds;
            PositionSlider.IsEnabled = PositionSlider.Maximum > 0;
        }
        if (!PositionSlider.IsEnabled) return;
        var position = VideoPlayer.Position;
        isUpdatingTimeline = true;
        PositionSlider.Value = Math.Clamp(position.TotalMilliseconds, 0, PositionSlider.Maximum);
        TimeText.Text = $"{FormatTime(position)} / {FormatTime(VideoPlayer.NaturalDuration.TimeSpan)}";
        isUpdatingTimeline = false;
    }

    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(RecordingScale.ScaleX / 1.25);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(RecordingScale.ScaleX * 1.25);
    private void Fit_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void SetZoom(double zoom)
    {
        RecordingScale.ScaleX = RecordingScale.ScaleY = Math.Clamp(zoom, 0.5, 3);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        string? copy = null;
        try
        {
            copy = ScreenshotFileService.CopyRecordingForClipboard(recording.FilePath);
            System.Windows.Clipboard.SetFileDropList(new StringCollection { copy });
        }
        catch (Exception error)
        {
            if (copy is not null) ScreenshotFileService.TryDelete(copy);
            AppDialog.ShowError(this, "Could not copy recording", "Try copying the recording again.", error.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { ScreenshotFileService.SaveRecordingAs(recording.FilePath, this); }
        catch (Exception error)
        {
            AppDialog.ShowError(this, "Could not save recording", "Choose another location and try again.", error.Message);
        }
    }

    private void RecordingViewerWindow_Closed(object? sender, EventArgs e)
    {
        isClosed = true;
        gifTimer.Stop();
        videoTimer.Stop();
        VideoPlayer.Close();
        ThemePreferenceService.Changed -= ThemePreference_Changed;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        Closed -= RecordingViewerWindow_Closed;
        SourceInitialized -= RecordingViewerWindow_SourceInitialized;
        if (gif is not null) gif.Dispose();
        else if (gifLoadTask is { IsCompletedSuccessfully: true }) gifLoadTask.Result.Dispose();
        else if (gifLoadTask is { IsCompleted: false } loading)
        {
            _ = loading.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully) task.Result.Dispose();
                recording.Dispose();
            }, TaskScheduler.Default);
            return;
        }
        recording.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
