using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BetterScreenShot;

public partial class AreaRecordingWindow : Window
{
    private const uint ExcludeFromCapture = 0x00000011;
    private readonly Rectangle bounds;
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationTokenSource cancel = new();
    private readonly TaskCompletionSource<(string FilePath, BitmapSource Preview, RecordingFormat Format)?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<(string FilePath, BitmapSource Preview)>? recording;
    private RecordingFormat format;
    private bool isClosed;
    private bool canExcludeControls;

    internal Task<(string FilePath, BitmapSource Preview, RecordingFormat Format)?> Completion => completion.Task;

    internal AreaRecordingWindow(Rectangle bounds)
    {
        InitializeComponent();
        this.bounds = bounds;
        ApplyTheme();
        SourceInitialized += (_, _) => canExcludeControls = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            && SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, ExcludeFromCapture);
        Loaded += (_, _) =>
        {
            DesktopWindowService.PositionFloatingControls(this, bounds, 10);
            if (!canExcludeControls)
            {
                VideoButton.IsEnabled = false;
                GifButton.IsEnabled = false;
                CaptureStatus.Text = "Windows could not keep these controls out of the recording.";
            }
        };
        ThemePreferenceService.Changed += ThemePreference_Changed;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Closed += (_, _) =>
        {
            isClosed = true;
            ThemePreferenceService.Changed -= ThemePreference_Changed;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            if (completion.TrySetResult(null))
            {
                cancel.Cancel();
                if (recording is not null)
                    _ = recording.ContinueWith(task =>
                    {
                        if (task.IsCompletedSuccessfully) ScreenshotFileService.TryDelete(task.Result.FilePath);
                    }, TaskScheduler.Default);
            }
        };
    }

    private void ApplyTheme() => ThemeIconService.ApplyViewerTheme(Resources, ThemePreferenceService.CurrentTheme);
    private void ThemePreference_Changed() => Dispatcher.BeginInvoke(new Action(ApplyTheme));
    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            ThemePreference_Changed();
    }

    private void Video_Click(object sender, RoutedEventArgs e) => Start(RecordingFormat.Video);
    private void Gif_Click(object sender, RoutedEventArgs e) => Start(RecordingFormat.Gif);

    private void Start(RecordingFormat selectedFormat)
    {
        if (recording is not null || !canExcludeControls) return;
        format = selectedFormat;
        VideoButton.IsEnabled = false;
        GifButton.IsEnabled = false;
        CaptureStatus.Text = format == RecordingFormat.Video
            ? "Recording video. Press Stop when finished."
            : $"Recording GIF (up to {AreaRecordingService.GifSecondsLimit} seconds). Press Stop when finished.";
        try
        {
            DesktopWindowService.FlushPendingUpdates();
            recording = Task.Run(() => AreaRecordingService.Record(bounds, format, stop.Token, cancel.Token));
        }
        catch (Exception error)
        {
            VideoButton.IsEnabled = true;
            GifButton.IsEnabled = true;
            CaptureStatus.Text = $"Could not start recording: {error.Message}";
            return;
        }
        var selectedButton = format == RecordingFormat.Video ? VideoButton : GifButton;
        var selectedIndex = ActionsPanel.Children.IndexOf(selectedButton);
        StopButton.Width = selectedButton.ActualWidth;
        ActionsPanel.Children.Remove(StopButton);
        ActionsPanel.Children.Insert(selectedIndex, StopButton);
        selectedButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        StopButton.IsEnabled = true;
        RecordingIndicator.Visibility = Visibility.Visible;
        _ = recording.ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!isClosed && task.IsFaulted)
            {
                completion.TrySetException(task.Exception!.InnerException ?? task.Exception);
                Close();
            }
            else if (!isClosed && task.IsCompletedSuccessfully && !stop.IsCancellationRequested)
                _ = FinishAsync();
        })), TaskScheduler.Default);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await FinishAsync();

    private async Task FinishAsync()
    {
        if (recording is null || stop.IsCancellationRequested) return;
        stop.Cancel();
        StopButton.IsEnabled = false;
        RecordingIndicator.Visibility = Visibility.Collapsed;
        CaptureStatus.Text = "Finishing recording…";
        try
        {
            var result = await recording;
            if (isClosed) return;
            if (!completion.TrySetResult((result.FilePath, result.Preview, format)))
                ScreenshotFileService.TryDelete(result.FilePath);
            Close();
        }
        catch (Exception error)
        {
            if (isClosed) return;
            completion.TrySetException(error);
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ToolbarGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
