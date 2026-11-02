using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BetterScreenShot;

public partial class ScrollingCaptureWindow : Window
{
    private const uint ExcludeFromCapture = 0x00000011;
    private readonly Rectangle bounds;
    private readonly DispatcherTimer timer = new();
    private readonly TaskCompletionSource<Bitmap?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScrollingStitcher? stitcher;
    private Task? captureTask;
    private bool isStopping;
    private bool isClosed;
    private bool canExcludeControls;

    internal Task<Bitmap?> Completion => completion.Task;
    internal bool MayHaveGaps { get; private set; }

    internal ScrollingCaptureWindow(Rectangle bounds)
    {
        InitializeComponent();
        this.bounds = bounds;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((long)bounds.Width * bounds.Height / 10_000, 100, 350));
        ApplyTheme();
        SourceInitialized += (_, _) => canExcludeControls = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            && SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, ExcludeFromCapture);
        Loaded += (_, _) =>
        {
            DesktopWindowService.PositionFloatingControls(this, bounds, 10);
            if (!canExcludeControls)
            {
                StartButton.IsEnabled = false;
                CaptureStatus.Text = "Windows could not keep these controls out of the screenshot.";
            }
        };
        timer.Tick += (_, _) =>
        {
            if (captureTask is null || captureTask.IsCompleted) captureTask = CaptureStepAsync();
        };
        ThemePreferenceService.Changed += ThemePreference_Changed;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Closed += (_, _) =>
        {
            isClosed = true;
            timer.Stop();
            ThemePreferenceService.Changed -= ThemePreference_Changed;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            completion.TrySetResult(null);
        };
    }

    private void ApplyTheme() => ThemeIconService.ApplyViewerTheme(Resources, ThemePreferenceService.CurrentTheme);
    private void ThemePreference_Changed() => Dispatcher.BeginInvoke(new Action(ApplyTheme));
    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            ThemePreference_Changed();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        CaptureStatus.Text = "Starting capture…";
        try
        {
            if ((long)bounds.Width * bounds.Height > 30_000_000)
                throw new InvalidOperationException("The selected area is too large for a scrolling capture.");
            DesktopWindowService.FlushPendingUpdates();
            stitcher = new ScrollingStitcher(await Task.Run(() => ScrollFrame.Capture(bounds)));
            if (isClosed) return;
            StopButton.IsEnabled = true;
            CaptureStatus.Text = "Scroll downward. Press Stop capture when finished.";
            timer.Start();
        }
        catch (Exception error)
        {
            if (!isClosed) CaptureStatus.Text = $"Could not start capture: {error.Message}";
        }
    }

    private async Task CaptureStepAsync()
    {
        try
        {
            var result = await Task.Run(() => stitcher!.Add(ScrollFrame.Capture(bounds)));
            if (isClosed) return;
            if (result == ScrollFrameResult.NoOverlap) MayHaveGaps = true;
            if (result == ScrollFrameResult.Added) MayHaveGaps = false;
            CaptureStatus.Text = result switch
            {
                ScrollFrameResult.Added => $"Captured {stitcher!.Height:N0} pixels. Keep scrolling downward.",
                ScrollFrameResult.NoOverlap => "No overlap. Scroll back, then continue more slowly.",
                ScrollFrameResult.LimitReached => "Maximum image length reached. Press Stop capture.",
                _ => CaptureStatus.Text
            };
            if (result == ScrollFrameResult.LimitReached) timer.Stop();
        }
        catch (Exception error)
        {
            timer.Stop();
            MayHaveGaps = true;
            if (!isClosed) CaptureStatus.Text = $"Capture paused: {error.Message}";
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (isStopping || stitcher is null) return;
        isStopping = true;
        StopButton.IsEnabled = false;
        timer.Stop();
        CaptureStatus.Text = "Stitching image…";
        if (captureTask is not null) await captureTask;
        if (isClosed) return;
        await CaptureStepAsync();
        if (isClosed) return;
        try
        {
            var image = await Task.Run(stitcher.BuildBitmap);
            if (isClosed) { image.Dispose(); return; }
            completion.TrySetResult(image);
            Close();
        }
        catch (Exception error)
        {
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
