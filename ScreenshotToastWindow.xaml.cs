using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace BetterScreenShot
{
    public partial class ScreenshotToastWindow : Window
    {
        private const double ScreenMargin = 16;
        private static ScreenshotToastWindow? currentToast;
        private readonly string filePath;

        public ScreenshotToastWindow(string filePath, System.Drawing.Rectangle captureBounds)
        {
            InitializeComponent();

            this.filePath = filePath;
            PreviewImage.Source = ScreenshotFileService.LoadBitmap(filePath);

            Loaded += (_, _) => PositionWindow(captureBounds);
            Closed += (_, _) =>
            {
                if (ReferenceEquals(currentToast, this))
                {
                    currentToast = null;
                }
            };
        }

        public static void ShowToast(string filePath, System.Drawing.Rectangle captureBounds)
        {
            currentToast?.Close();
            currentToast = new ScreenshotToastWindow(filePath, captureBounds);
            currentToast.Show();
        }

        private void PositionWindow(System.Drawing.Rectangle captureBounds)
        {
            var screen = System.Windows.Forms.Screen.FromRectangle(captureBounds);
            var workArea = screen.WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);

            var workAreaLeft = workArea.Left / dpi.DpiScaleX;
            var workAreaTop = workArea.Top / dpi.DpiScaleY;
            var workAreaRight = workArea.Right / dpi.DpiScaleX;
            var workAreaBottom = workArea.Bottom / dpi.DpiScaleY;

            Left = Math.Max(workAreaLeft + ScreenMargin, workAreaRight - ActualWidth - ScreenMargin);
            Top = Math.Max(workAreaTop + ScreenMargin, workAreaBottom - ActualHeight - ScreenMargin);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScreenshotFileService.CopyToClipboard(filePath);
                ScreenshotFileService.DeleteIfExists(filePath);
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not copy the screenshot. {ex.Message}", "Copy Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var savedPath = ScreenshotFileService.SaveCopyAs(filePath);

                if (savedPath is null)
                {
                    return;
                }

                ScreenshotFileService.DeleteIfExists(filePath);
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not save the screenshot. {ex.Message}", "Save Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScreenshotFileService.DeleteIfExists(filePath);
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not discard the screenshot. {ex.Message}", "Discard Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
