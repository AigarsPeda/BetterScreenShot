using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BetterScreenShot
{
    public partial class ScreenshotToastWindow : Window
    {
        private const int ScreenMargin = 16;
        private const int SlideDurationMs = 180;
        private static ScreenshotToastWindow? currentToast;
        private readonly TemporaryScreenshot screenshot;
        private readonly bool previewIsThumbnail;
        private readonly RecordingFormat? recordingFormat;
        private bool isAnimatingClose;
        private bool allowImmediateClose;
        private bool preserveTemporaryFileForViewer;

        internal ScreenshotToastWindow(string filePath, System.Drawing.Rectangle captureBounds, BitmapSource image,
            bool previewIsThumbnail = false, RecordingFormat? recordingFormat = null)
        {
            InitializeComponent();
            ApplyToastTheme();

            screenshot = new TemporaryScreenshot(filePath);
            this.previewIsThumbnail = previewIsThumbnail;
            this.recordingFormat = recordingFormat;
            PreviewImage.Source = image;
            if (recordingFormat is { } format)
            {
                Title = "Recording ready";
                EditorButton.ToolTip = "View recording";
                EditorButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "View recording");
                EditGlyph.Visibility = Visibility.Collapsed;
                PlayGlyph.Visibility = Visibility.Visible;
                DiscardButton.ToolTip = "Discard recording";
                DiscardButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Discard recording");
                CopyButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
                    format == RecordingFormat.Video ? "Copy video file" : "Copy GIF file");
                SaveButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
                    format == RecordingFormat.Video ? "Save video" : "Save GIF");
            }

            Loaded += (_, _) => OnToastLoaded(captureBounds);
            Closing += ScreenshotToastWindow_Closing;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed += ThemePreference_Changed;
            Closed += (_, _) =>
            {
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                ThemePreferenceService.Changed -= ThemePreference_Changed;
                if (!preserveTemporaryFileForViewer)
                {
                    screenshot.Dispose();
                }

                if (ReferenceEquals(currentToast, this))
                {
                    currentToast = null;
                }
            };
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(new Action(ApplyToastTheme));
            }
        }

        private void ApplyToastTheme()
        {
            ApplyToastTheme(ThemePreferenceService.CurrentTheme);
        }

        private void ThemePreference_Changed() => Dispatcher.BeginInvoke(new Action(ApplyToastTheme));

        private void ApplyToastTheme(AppTheme theme)
        {
            var dark = theme == AppTheme.Dark;
            SetToastBrush("ToastSurfaceBrush", dark ? "#252833" : "#FFFFFF");
            SetToastBrush("ToastImageBrush", dark ? "#171922" : "#E3E3EA");
            SetToastBrush("ToastPrimaryTextBrush", dark ? "#F0F0F6" : "#252735");
            SetToastBrush("ToastSecondarySurfaceBrush", dark ? "#343849" : "#F0F0F8");
            SetToastBrush("ToastAccentBrush", dark ? "#B9B5FF" : "#625FC6");
            SetToastBrush("ToastAccentTextBrush", dark ? "#222345" : "#FFFFFF");
            SetToastBrush("ToastFocusBrush", dark ? "#B9B5FF" : "#625FC6");
        }

        private void SetToastBrush(string key, string hex)
        {
            Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }

        public static void ShowToast(string filePath, System.Drawing.Rectangle captureBounds, BitmapSource image,
            bool previewIsThumbnail = false)
        {
            ScreenshotToastWindow? toast = null;
            try
            {
                toast = new ScreenshotToastWindow(filePath, captureBounds, image, previewIsThumbnail);
                currentToast?.CloseImmediately();
                currentToast = toast;
                toast.Show();
            }
            catch
            {
                toast?.CloseImmediately();
                ScreenshotFileService.TryDelete(filePath);
                throw;
            }
        }

        internal static void ShowRecordingToast(string filePath, System.Drawing.Rectangle captureBounds,
            BitmapSource image, RecordingFormat format)
        {
            ScreenshotToastWindow? toast = null;
            try
            {
                toast = new ScreenshotToastWindow(filePath, captureBounds, image, recordingFormat: format);
                currentToast?.CloseImmediately();
                currentToast = toast;
                toast.Show();
            }
            catch
            {
                toast?.CloseImmediately();
                ScreenshotFileService.TryDelete(filePath);
                throw;
            }
        }

        public static void HideCurrent() => currentToast?.Hide();

        public static void CloseCurrent() => currentToast?.CloseImmediately();

        public static void RestoreCurrent()
        {
            if (currentToast is { IsVisible: false, isAnimatingClose: false })
            {
                currentToast.Show();
            }
        }

        private void OnToastLoaded(System.Drawing.Rectangle captureBounds)
        {
            DesktopWindowService.PositionToast(this, captureBounds, ScreenMargin);
            BeginSlideAnimation(24, 0, 0, 1, null);
        }

        private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PreviewImage.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 10, 10);
        }

        private void ScreenshotToastWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (allowImmediateClose || !IsLoaded || !IsVisible || System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            e.Cancel = true;
            if (isAnimatingClose)
            {
                return;
            }

            isAnimatingClose = true;
            IsHitTestVisible = false;
            BeginSlideAnimation(SlideTransform.X, 24, Opacity, 0, CloseImmediately);
        }

        private void BeginSlideAnimation(double fromLeft, double toLeft, double fromOpacity, double toOpacity, Action? completed)
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(SlideDurationMs);

            var leftAnimation = new DoubleAnimation
            {
                From = fromLeft,
                To = toLeft,
                Duration = duration,
                EasingFunction = easing
            };

            var opacityAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = duration,
                EasingFunction = easing
            };

            if (completed is not null)
            {
                leftAnimation.Completed += (_, _) => completed();
            }

            SlideTransform.BeginAnimation(TranslateTransform.XProperty, leftAnimation, HandoffBehavior.SnapshotAndReplace);
            BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        }

        private void CloseImmediately()
        {
            allowImmediateClose = true;
            SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, null);
            Close();
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CopyButton.IsEnabled) return;
            CopyButton.IsEnabled = false;
            try
            {
                if (recordingFormat is null)
                {
                    var data = await ImageClipboardService.CreateDataObjectAsync(FullImage());
                    if (!IsLoaded || isAnimatingClose) return;
                    System.Windows.Clipboard.SetDataObject(data, copy: true);
                }
                else
                {
                    var copy = ScreenshotFileService.CopyRecordingForClipboard(screenshot.FilePath);
                    try
                    {
                        var files = new StringCollection { copy };
                        System.Windows.Clipboard.SetFileDropList(files);
                    }
                    catch
                    {
                        ScreenshotFileService.TryDelete(copy);
                        throw;
                    }
                }
                Close();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, recordingFormat is null ? "Could not copy screenshot" : "Could not copy recording",
                    recordingFormat is null ? "Try copying the screenshot again." : "Try copying the recording again.", ex.Message);
            }
            finally { CopyButton.IsEnabled = true; }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? savedPath;
                if (recordingFormat is not null)
                {
                    savedPath = ScreenshotFileService.SaveRecordingAs(screenshot.FilePath, this);
                }
                else if (previewIsThumbnail)
                {
                    savedPath = ScreenshotFileService.ChooseSavePath(screenshot.FilePath, this);
                    if (savedPath is not null)
                    {
                        var image = System.IO.Path.GetExtension(savedPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                            ? (BitmapSource)PreviewImage.Source : FullImage();
                        ScreenshotFileService.Export(screenshot.FilePath, savedPath, image);
                    }
                }
                else
                {
                    savedPath = ScreenshotFileService.SaveCopyAs(screenshot.FilePath, (BitmapSource)PreviewImage.Source, this);
                }

                if (savedPath is null)
                {
                    return;
                }

                Close();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, recordingFormat is null ? "Could not save screenshot" : "Could not save recording",
                    "Choose another location and try again.", ex.Message);
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (recordingFormat is null) OpenViewer();
                else OpenRecordingViewer();
            }
            catch (Exception ex)
            {
                preserveTemporaryFileForViewer = false;
                AppDialog.ShowError(this, "Could not open preview", "Try opening the screenshot again.", ex.Message);
            }
        }

        internal ScreenshotViewerWindow OpenViewer()
        {
            var viewerWindow = new ScreenshotViewerWindow(screenshot, FullImage());
            viewerWindow.Show();
            viewerWindow.Activate();
            preserveTemporaryFileForViewer = true;
            Close();
            return viewerWindow;
        }

        internal RecordingViewerWindow OpenRecordingViewer()
        {
            if (recordingFormat is null) throw new InvalidOperationException("This popup does not contain a recording.");
            var viewerWindow = new RecordingViewerWindow(screenshot, recordingFormat.Value, (BitmapSource)PreviewImage.Source);
            viewerWindow.Show();
            viewerWindow.Activate();
            preserveTemporaryFileForViewer = true;
            Close();
            return viewerWindow;
        }

        private BitmapSource FullImage() => previewIsThumbnail
            ? ScreenshotCaptureService.LoadFullImage(screenshot.FilePath)
            : (BitmapSource)PreviewImage.Source;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, "Could not discard screenshot", "Try closing the preview again.", ex.Message);
            }
        }
    }
}
