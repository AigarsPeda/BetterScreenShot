using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BetterScreenShot
{
    public partial class ScreenshotViewerWindow : Window
    {
        private readonly string filePath;
        private bool shouldDeleteTemporaryFile = true;

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
            FilePathText.Text = "Temporary screenshot. Save it if you want to keep it.";
            Closed += ScreenshotViewerWindow_Closed;
            Closed += ScreenshotViewerWindow_OnClosed;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ApplyThemeIcon();
        }

        private void ApplyThemeIcon()
        {
            Icon = ThemeIconService.CreateScanIcon(ThemeIconService.DetectWindowsTheme());
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyThemeIcon));
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

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Delete this screenshot?",
                "Delete Screenshot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                ScreenshotFileService.DeleteIfExists(filePath);
                shouldDeleteTemporaryFile = false;
                Close();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not delete the screenshot. {ex.Message}", "Delete Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScreenshotViewerWindow_Closed(object? sender, System.EventArgs e)
        {
            if (!shouldDeleteTemporaryFile)
            {
                return;
            }

            try
            {
                ScreenshotFileService.DeleteIfExists(filePath);
            }
            catch
            {
            }
        }

        private void ScreenshotViewerWindow_OnClosed(object? sender, System.EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            Closed -= ScreenshotViewerWindow_OnClosed;
        }
    }
}
