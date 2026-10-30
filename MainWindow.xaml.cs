using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace BetterScreenShot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

        private StatusTone _currentStatusTone;
        private string _currentStatusMessage = "Ready to capture.";
        private bool isCapturing;
        private bool isClosed;
        private AppTheme? currentTheme;

        public MainWindow() : this(CaptureShortcutSettingsStore.Default) { }

        internal MainWindow(CaptureShortcutSettingsStore shortcutStore)
        {
            this.shortcutStore = shortcutStore;
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ApplyThemeFromSystem();
            LoadCaptureShortcuts();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!isClosed && !isCapturing)
                    {
                        MonitorSelectionOverlayForm.WarmUpOverlays();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Overlay warm-up failed: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ApplyTitleBarTheme(currentTheme ?? AppTheme.Light);
            InitializeCaptureShortcuts();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            isClosed = true;
            globalShortcuts?.Dispose();
            MonitorSelectionOverlayForm.Shutdown();
            ScreenshotToastWindow.CloseCurrent();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            Loaded -= MainWindow_Loaded;
            SourceInitialized -= MainWindow_SourceInitialized;
            Closed -= MainWindow_Closed;
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyThemeFromSystem));
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureAsync(selectArea: false);
        }

        private async void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureAsync(selectArea: true);
        }

        private async Task CaptureAsync(bool selectArea)
        {
            if (isCapturing || isClosed)
            {
                return;
            }

            isCapturing = true;
            CaptureButton.IsEnabled = false;
            SelectAreaButton.IsEnabled = false;
            ShortcutsButton.IsEnabled = false;
            string? pendingFile = null;

            try
            {
                SetStatus(StatusTone.Info, selectArea ? "Drag to select the area you want." : "Choose a monitor to capture.");
                ScreenshotToastWindow.HideCurrent();
                Hide();
                var bounds = selectArea
                    ? await MonitorSelectionOverlayForm.SelectAreaAsync()
                    : await MonitorSelectionOverlayForm.SelectMonitorAsync();

                if (isClosed)
                {
                    return;
                }

                if (bounds is null)
                {
                    SetStatus(StatusTone.Info, selectArea ? "Area selection cancelled." : "Full screen capture cancelled.");
                    return;
                }

                // Wait for the compositor, rather than guessing with a fixed delay.
                DesktopWindowService.FlushPendingUpdates();
                var capture = await Task.Run(() => ScreenshotCaptureService.Capture(bounds.Value));
                pendingFile = capture.FilePath;
                if (isClosed)
                {
                    return;
                }

                Show();
                Activate();
                ScreenshotToastWindow.ShowToast(pendingFile, bounds.Value, capture.Preview);
                pendingFile = null; // The toast now owns the temporary file.
                SetStatus(StatusTone.Success, "Screenshot captured. Use the popup to save, copy, or discard it.");
            }
            catch (Exception ex)
            {
                if (!isClosed)
                {
                    SetStatus(StatusTone.Error, $"Error: {ex.Message}");
                }
            }
            finally
            {
                if (pendingFile is not null)
                {
                    ScreenshotFileService.TryDelete(pendingFile);
                }

                isCapturing = false;
                if (!isClosed)
                {
                    CaptureButton.IsEnabled = true;
                    SelectAreaButton.IsEnabled = true;
                    ShortcutsButton.IsEnabled = true;
                    Show();
                    Activate();
                    ScreenshotToastWindow.RestoreCurrent();
                }
            }
        }

        private void SetStatus(StatusTone tone, string message)
        {
            _currentStatusTone = tone;
            _currentStatusMessage = message;
            StatusTextBlock.Text = message;

            switch (tone)
            {
                case StatusTone.Success:
                    ApplyStatusColors("StatusSuccessBrush", "StatusSuccessTextBrush");
                    break;
                case StatusTone.Info:
                    ApplyStatusColors("StatusInfoBrush", "StatusInfoTextBrush");
                    break;
                case StatusTone.Error:
                    ApplyStatusColors("StatusErrorBrush", "StatusErrorTextBrush");
                    break;
                default:
                    ApplyStatusColors("StatusNeutralBrush", "StatusNeutralTextBrush");
                    break;
            }
        }

        private void ApplyStatusColors(string backgroundKey, string foregroundKey)
        {
            var background = (System.Windows.Media.Brush)FindResource(backgroundKey);
            var foreground = (System.Windows.Media.Brush)FindResource(foregroundKey);
            StatusBadge.Background = background;
            StatusDot.Fill = foreground;
            StatusTextBlock.Foreground = foreground;
        }

        private void ApplyThemeFromSystem()
        {
            var theme = ThemeIconService.DetectWindowsTheme();
            if (currentTheme == theme)
            {
                return;
            }
            currentTheme = theme;
            ApplyTheme(theme);
            Icon = ThemeIconService.CreateScanIcon(theme);
            SetStatus(_currentStatusTone, _currentStatusMessage);
            ApplyTitleBarTheme(theme);
        }

        private void ApplyTheme(AppTheme theme)
        {
            if (theme == AppTheme.Dark)
            {
                SetBrushColor("PageBackgroundBrush", "#0F172A");
                SetBrushColor("SurfaceBrush", "#111827");
                SetBrushColor("SurfaceBorderBrush", "#243041");
                SetBrushColor("SurfaceHoverBrush", "#172134");
                SetBrushColor("SurfaceHoverBorderBrush", "#334155");
                SetBrushColor("SurfacePressedBrush", "#1C2940");
                SetBrushColor("SurfacePressedBorderBrush", "#3B4C63");
                SetBrushColor("PrimaryTextBrush", "#E5EEF9");
                SetBrushColor("SecondaryTextBrush", "#94A3B8");
                SetBrushColor("AccentBlueBrush", "#7AA2FF");
                SetBrushColor("AccentBlueSoftBrush", "#1C2C4A");
                SetBrushColor("AccentGreenBrush", "#5DD6A4");
                SetBrushColor("AccentGreenSoftBrush", "#163328");
                SetBrushColor("StatusNeutralBrush", "#162133");
                SetBrushColor("StatusNeutralTextBrush", "#C7D4E5");
                SetBrushColor("StatusSuccessBrush", "#163328");
                SetBrushColor("StatusSuccessTextBrush", "#83E1B9");
                SetBrushColor("StatusInfoBrush", "#1B2B49");
                SetBrushColor("StatusInfoTextBrush", "#9CB9FF");
                SetBrushColor("StatusErrorBrush", "#3A1D22");
                SetBrushColor("StatusErrorTextBrush", "#FFB4B4");
                return;
            }

            SetBrushColor("PageBackgroundBrush", "#F7F9FC");
            SetBrushColor("SurfaceBrush", "#FFFFFF");
            SetBrushColor("SurfaceBorderBrush", "#D8E1EC");
            SetBrushColor("SurfaceHoverBrush", "#FBFDFF");
            SetBrushColor("SurfaceHoverBorderBrush", "#C4D4E8");
            SetBrushColor("SurfacePressedBrush", "#F5F9FD");
            SetBrushColor("SurfacePressedBorderBrush", "#B7CCE6");
            SetBrushColor("PrimaryTextBrush", "#111827");
            SetBrushColor("SecondaryTextBrush", "#97A6BA");
            SetBrushColor("AccentBlueBrush", "#2563EB");
            SetBrushColor("AccentBlueSoftBrush", "#E8F0FF");
            SetBrushColor("AccentGreenBrush", "#0F9F6E");
            SetBrushColor("AccentGreenSoftBrush", "#E7F8F1");
            SetBrushColor("StatusNeutralBrush", "#EEF2F7");
            SetBrushColor("StatusNeutralTextBrush", "#415063");
            SetBrushColor("StatusSuccessBrush", "#E7F8F1");
            SetBrushColor("StatusSuccessTextBrush", "#0D7A55");
            SetBrushColor("StatusInfoBrush", "#EAF2FF");
            SetBrushColor("StatusInfoTextBrush", "#2454B8");
            SetBrushColor("StatusErrorBrush", "#FDECEC");
            SetBrushColor("StatusErrorTextBrush", "#B42318");
        }

        private void SetBrushColor(string resourceKey, string colorHex)
        {
            if (Resources.Contains(resourceKey))
            {
                Resources[resourceKey] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(colorHex));
            }
        }

        private void ApplyTitleBarTheme(AppTheme theme)
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
            {
                return;
            }

            var darkModeEnabled = theme == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(helper.Handle, DwmwaUseImmersiveDarkMode, ref darkModeEnabled, sizeof(int));
        }

        private enum StatusTone
        {
            Neutral,
            Info,
            Success,
            Error
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    }
}
