using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace BetterScreenShot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            SetStatus(StatusTone.Neutral, "Ready to capture.");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(SelectionOverlayWindow.WarmUp), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

        private enum StatusTone
        {
            Neutral,
            Info,
            Success,
            Error
        }
    }
}

