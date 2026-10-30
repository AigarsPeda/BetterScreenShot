using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using Point = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace BetterScreenShot
{
    public partial class ScreenshotViewerWindow : Window
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const double MinZoom = 1.0;
        private const double MaxZoom = 5.0;
        private const double ZoomStep = 0.2;

        private readonly TemporaryScreenshot screenshot;
        private bool isDragging;
        private Point lastDragPoint;
        private double currentZoom = 1.0;
        private AppTheme? currentTheme;

        internal ScreenshotViewerWindow(TemporaryScreenshot screenshot, BitmapSource image)
        {
            InitializeComponent();
            this.screenshot = screenshot;
            ViewerImage.Source = image;
            InitializeEditor(image);
            ApplyThemeFromSystem();
            UpdateZoomState();
            Closed += ScreenshotViewerWindow_Closed;
            SourceInitialized += ScreenshotViewerWindow_SourceInitialized;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        private void ScreenshotViewerWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ApplyTitleBarTheme(currentTheme ?? AppTheme.Light);
        }

        private void ApplyThemeFromSystem()
        {
            var theme = ThemeIconService.DetectWindowsTheme();
            if (currentTheme == theme)
            {
                return;
            }
            currentTheme = theme;
            ThemeIconService.ApplyViewerTheme(Resources, theme);
            Icon = ThemeIconService.CreateScanIcon(theme);
            ApplyTitleBarTheme(theme);
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

        private void UpdateZoomState()
        {
            ImageScaleTransform.ScaleX = currentZoom;
            ImageScaleTransform.ScaleY = currentZoom;

            if (currentZoom <= MinZoom)
            {
                ImageTranslateTransform.X = 0;
                ImageTranslateTransform.Y = 0;
            }

            UpdateDisplayScale();
        }

        private void ChangeZoom(double delta)
        {
            currentZoom = Math.Clamp(Math.Round(currentZoom + delta, 2), MinZoom, MaxZoom);
            UpdateZoomState();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeZoom(-ZoomStep);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeZoom(ZoomStep);
        }

        private void ImageViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ChangeZoom(e.Delta > 0 ? ZoomStep : -ZoomStep);
            e.Handled = true;
        }

        private void ImageViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Annotations.Tool != AnnotationTool.Hand && !Keyboard.IsKeyDown(Key.Space))
            {
                return;
            }

            isDragging = true;
            lastDragPoint = e.GetPosition(ImageViewport);
            ImageViewport.CaptureMouse();
            ImageViewport.Cursor = WpfCursors.Hand;
            e.Handled = true;
        }

        private void ImageViewport_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }

            var currentPoint = e.GetPosition(ImageViewport);
            var delta = currentPoint - lastDragPoint;
            lastDragPoint = currentPoint;

            ImageTranslateTransform.X += delta.X;
            ImageTranslateTransform.Y += delta.Y;
            e.Handled = true;
        }

        private void ImageViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            ImageViewport.ReleaseMouseCapture();
            ImageViewport.Cursor = WpfCursors.Arrow;
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

        private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveButton.IsEnabled) return;
            CommitText();
            Annotations.CancelGesture();
            try
            {
                var savedPath = ScreenshotFileService.ChooseSavePath(screenshot.FilePath, this);
                if (savedPath is null)
                {
                    return;
                }

                SaveButton.IsEnabled = false;
                SaveButton.Content = "Saving…";
                var modified = Annotations.Document.Items.Count > 0;
                var output = modified ? await Annotations.Document.RenderAsync((BitmapSource)ViewerImage.Source) : (BitmapSource)ViewerImage.Source;
                // Encoding the frozen image also lets an export finish if the editor closes.
                await Task.Run(() => ScreenshotFileService.Export(screenshot.FilePath, savedPath, output, imageModified: true));
                if (!IsLoaded) return;
                AppDialog.ShowSaved(this, savedPath);
            }
            catch (Exception ex)
            {
                if (!IsLoaded) return;
                AppDialog.ShowError(this, "Could not save screenshot", "Choose another location and try again.", ex.Message);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = "Save As…";
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDialog.ConfirmDelete(this))
            {
                return;
            }

            try
            {
                ScreenshotFileService.DeleteIfExists(screenshot.FilePath);
                Close();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, "Could not delete screenshot", "The screenshot is still open. Try deleting it again.", ex.Message);
            }
        }

        private void ScreenshotViewerWindow_Closed(object? sender, EventArgs e)
        {
            EndDrag();
            Annotations.CancelGesture();
            screenshot.Dispose();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SourceInitialized -= ScreenshotViewerWindow_SourceInitialized;
            Closed -= ScreenshotViewerWindow_Closed;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    }
}
