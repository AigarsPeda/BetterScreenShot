using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace BetterScreenShot
{
    public partial class ScreenshotViewerWindow : Window
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

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
            Closed += ScreenshotViewerWindow_Closed;
            Closed += ScreenshotViewerWindow_OnClosed;
            SourceInitialized += ScreenshotViewerWindow_SourceInitialized;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ApplyThemeFromSystem();
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
