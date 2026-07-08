using System;
using System.IO;
using System.Windows;

namespace BetterScreenShot
{
    public partial class ScreenshotToastWindow : Window
    {
        private static ScreenshotToastWindow? currentToast;
        private readonly string filePath;

        public ScreenshotToastWindow(string filePath, System.Drawing.Rectangle captureBounds)
        {
            InitializeComponent();

            this.filePath = filePath;
            PreviewImage.Source = ScreenshotFileService.LoadBitmap(filePath);
            PathText.Text = filePath;

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
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(filePath))
            {
                System.Windows.MessageBox.Show("The screenshot file no longer exists.", "Open Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            var viewer = new ScreenshotViewerWindow(filePath);
            viewer.Show();
            viewer.Activate();
            Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not delete the screenshot. {ex.Message}", "Delete Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
