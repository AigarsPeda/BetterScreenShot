using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;

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
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(SelectionOverlayWindow.WarmUp), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var selectedMonitorBounds = SelectionOverlayWindow.SelectMonitor(() =>
                {
                    Dispatcher.Invoke(Hide);
                });

                Show();
                Activate();

                if (selectedMonitorBounds is null)
                {
                    StatusText.Text = "Full screen capture cancelled.";
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
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private async void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var selectedCapture = SelectionOverlayWindow.SelectArea(() =>
                {
                    Dispatcher.Invoke(Hide);
                });

                Show();
                Activate();

                if (selectedCapture is null)
                {
                    StatusText.Text = "Selection cancelled.";
                    return;
                }

                await Task.Delay(150);
                Activate();

                using (selectedCapture.CapturedBitmap)
                {
                    var filePath = ScreenshotCaptureService.SaveBitmapToTemporaryFile(selectedCapture.CapturedBitmap);
                    ScreenshotToastWindow.ShowToast(filePath, selectedCapture.SourceScreenBounds);
                }

                StatusText.Text = "Screenshot captured. Use the popup to open, save, or discard it.";
            }
            catch (Exception ex)
            {
                if (!IsVisible)
                {
                    Show();
                }

                Activate();
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void HandleCapture(Rectangle bounds)
        {
            var filePath = ScreenshotCaptureService.CaptureToTemporaryFile(bounds);
            ScreenshotToastWindow.ShowToast(filePath, bounds);
            StatusText.Text = "Screenshot captured. Use the popup to open, save, or discard it.";
        }
    }
}
