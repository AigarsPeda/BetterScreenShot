using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

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
        }

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var primaryScreen = Forms.Screen.PrimaryScreen;

                if (primaryScreen is null)
                {
                    StatusText.Text = "Error: No primary screen was found.";
                    return;
                }

                HandleCapture(primaryScreen.Bounds);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private async void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Hide();

                var selectedCapture = SelectionOverlayWindow.SelectArea();

                if (selectedCapture is null)
                {
                    StatusText.Text = "Selection cancelled.";
                    Show();
                    Activate();
                    return;
                }

                await Task.Delay(150);

                Show();
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
                Show();
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
