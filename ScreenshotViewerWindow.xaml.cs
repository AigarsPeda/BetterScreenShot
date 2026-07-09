using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
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

        private readonly string filePath;
        private bool shouldDeleteTemporaryFile = true;
        private bool isDragging;
        private Point lastDragPoint;
        private double currentZoom = 1.0;

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
            Closed += ScreenshotViewerWindow_Closed;
            Closed += ScreenshotViewerWindow_OnClosed;
            SourceInitialized += ScreenshotViewerWindow_SourceInitialized;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ApplyThemeFromSystem();
            UpdateZoomState();
        }

        private void ScreenshotViewerWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ApplyThemeFromSystem();
        }

        private void ApplyThemeFromSystem()
        {
            var theme = ThemeIconService.DetectWindowsTheme();
            ApplyTheme(theme);
            Icon = ThemeIconService.CreateScanIcon(theme);
            ApplyTitleBarTheme(theme);
        }

        private void ApplyTheme(AppTheme theme)
        {
            if (theme == AppTheme.Dark)
            {
                SetBrushColor("ViewerPageBackgroundBrush", "#0F172A");
                SetBrushColor("ViewerSurfaceBrush", "#111827");
                SetBrushColor("ViewerSurfaceBorderBrush", "#243041");
                SetBrushColor("ViewerSurfaceHoverBrush", "#172134");
                SetBrushColor("ViewerSurfacePressedBrush", "#1C2940");
                SetBrushColor("ViewerPrimaryTextBrush", "#E5EEF9");
                SetBrushColor("ViewerSecondaryTextBrush", "#94A3B8");
                SetBrushColor("ViewerCanvasBrush", "#0B1220");
                return;
            }

            SetBrushColor("ViewerPageBackgroundBrush", "#F7F9FC");
            SetBrushColor("ViewerSurfaceBrush", "#FFFFFF");
            SetBrushColor("ViewerSurfaceBorderBrush", "#D8E1EC");
            SetBrushColor("ViewerSurfaceHoverBrush", "#F5F9FD");
            SetBrushColor("ViewerSurfacePressedBrush", "#EEF4FB");
            SetBrushColor("ViewerPrimaryTextBrush", "#111827");
            SetBrushColor("ViewerSecondaryTextBrush", "#415063");
            SetBrushColor("ViewerCanvasBrush", "#EDEFF4");
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

        private void UpdateZoomState()
        {
            ImageScaleTransform.ScaleX = currentZoom;
            ImageScaleTransform.ScaleY = currentZoom;

            if (currentZoom <= MinZoom)
            {
                ImageTranslateTransform.X = 0;
                ImageTranslateTransform.Y = 0;
            }

            ImageViewport.Cursor = currentZoom > MinZoom ? WpfCursors.SizeAll : WpfCursors.Arrow;
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
            if (currentZoom <= MinZoom)
            {
                return;
            }

            isDragging = true;
            lastDragPoint = e.GetPosition(ImageViewport);
            ImageViewport.CaptureMouse();
            ImageViewport.Cursor = WpfCursors.Hand;
        }

        private void ImageViewport_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (!isDragging || currentZoom <= MinZoom)
            {
                return;
            }

            var currentPoint = e.GetPosition(ImageViewport);
            var delta = currentPoint - lastDragPoint;
            lastDragPoint = currentPoint;

            ImageTranslateTransform.X += delta.X;
            ImageTranslateTransform.Y += delta.Y;
        }

        private void ImageViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag();
        }

        private void ImageViewport_MouseLeave(object sender, WpfMouseEventArgs e)
        {
            if (isDragging && e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
            }
        }

        private void EndDrag()
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            ImageViewport.ReleaseMouseCapture();
            ImageViewport.Cursor = currentZoom > MinZoom ? WpfCursors.SizeAll : WpfCursors.Arrow;
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
            catch (Exception ex)
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
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not delete the screenshot. {ex.Message}", "Delete Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScreenshotViewerWindow_Closed(object? sender, EventArgs e)
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

        private void ScreenshotViewerWindow_OnClosed(object? sender, EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SourceInitialized -= ScreenshotViewerWindow_SourceInitialized;
            Closed -= ScreenshotViewerWindow_OnClosed;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    }
}
