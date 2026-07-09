using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            SetStatus(StatusTone.Neutral, _currentStatusMessage);
            ApplyThemeFromSystem();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(SelectionOverlayWindow.WarmUp), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ApplyThemeFromSystem();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
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
            try
            {
                SetStatus(StatusTone.Info, "Choose a monitor to capture.");
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var selectedMonitorBounds = SelectionOverlayWindow.SelectMonitor(() =>
                {
                    Dispatcher.Invoke(Hide);
                });

                Show();
                Activate();

                if (selectedMonitorBounds is null)
                {
                    SetStatus(StatusTone.Info, "Full screen capture cancelled.");
                    return;
                }

                await Task.Delay(150);
                Activate();
                HandleCapture(selectedMonitorBounds.Value);
            }
            catch (Exception ex)
            {
                if (!IsVisible)
                {
                    Show();
                }

                Activate();
                SetStatus(StatusTone.Error, $"Error: {ex.Message}");
            }
        }

        private async void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus(StatusTone.Info, "Drag to select the area you want.");
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var selectedCapture = SelectionOverlayWindow.SelectArea(() =>
                {
                    Dispatcher.Invoke(Hide);
                });

                Show();
                Activate();

                if (selectedCapture is null)
                {
                    SetStatus(StatusTone.Info, "Area selection cancelled.");
                    return;
                }

                await Task.Delay(150);
                Activate();

                using (selectedCapture.CapturedBitmap)
                {
                    var filePath = ScreenshotCaptureService.SaveBitmapToTemporaryFile(selectedCapture.CapturedBitmap);
                    ScreenshotToastWindow.ShowToast(filePath, selectedCapture.SourceScreenBounds);
                }

                SetStatus(StatusTone.Success, "Screenshot captured. Use the popup to save, copy, or discard it.");
            }
            catch (Exception ex)
            {
                if (!IsVisible)
                {
                    Show();
                }

                Activate();
                SetStatus(StatusTone.Error, $"Error: {ex.Message}");
            }
        }

        private void HandleCapture(Rectangle bounds)
        {
            var filePath = ScreenshotCaptureService.CaptureToTemporaryFile(bounds);
            ScreenshotToastWindow.ShowToast(filePath, bounds);
            SetStatus(StatusTone.Success, "Screenshot captured. Use the popup to save, copy, or discard it.");
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
