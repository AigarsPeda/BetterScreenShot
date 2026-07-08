using System.IO;
using System.Windows;

namespace BetterScreenShot
{
    public partial class ScreenshotViewerWindow : Window
    {
        private readonly string filePath;

        public ScreenshotViewerWindow(string filePath)
        {
            InitializeComponent();
            this.filePath = filePath;

            if (!File.Exists(filePath))
            {
                System.Windows.MessageBox.Show("The screenshot file no longer exists.", "Screenshot Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            ViewerImage.Source = ScreenshotFileService.LoadBitmap(filePath);
            FilePathText.Text = filePath;
        }

        private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var savedPath = ScreenshotFileService.SaveCopyAs(filePath);

                if (savedPath is null)
                {
                    return;
                }

                System.Windows.MessageBox.Show($"Saved copy to:\n{savedPath}", "Save Screenshot", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not save a copy. {ex.Message}", "Save Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
