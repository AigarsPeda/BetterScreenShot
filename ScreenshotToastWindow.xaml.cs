using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BetterScreenShot
{
    public partial class ScreenshotToastWindow : Window
    {
        private const int ScreenMargin = 16;
        private const int SlideDurationMs = 180;
        private static ScreenshotToastWindow? currentToast;
        private readonly TemporaryScreenshot screenshot;
        private bool isAnimatingClose;
        private bool allowImmediateClose;
        private bool preserveTemporaryFileForViewer;

        public ScreenshotToastWindow(string filePath, System.Drawing.Rectangle captureBounds, BitmapSource? image = null)
        {
            InitializeComponent();

            screenshot = new TemporaryScreenshot(filePath);
            PreviewImage.Source = image ?? ScreenshotFileService.LoadBitmap(filePath);

            Loaded += (_, _) => OnToastLoaded(captureBounds);
            Closing += ScreenshotToastWindow_Closing;
            Closed += (_, _) =>
            {
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

        public static void ShowToast(string filePath, System.Drawing.Rectangle captureBounds, BitmapSource? image = null)
        {
            ScreenshotToastWindow? toast = null;
            try
            {
                toast = new ScreenshotToastWindow(filePath, captureBounds, image);
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

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetImage((BitmapSource)PreviewImage.Source);
                Close();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, "Could not copy screenshot", "Try copying the screenshot again.", ex.Message);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var savedPath = ScreenshotFileService.SaveCopyAs(screenshot.FilePath, (BitmapSource)PreviewImage.Source, this);

                if (savedPath is null)
                {
                    return;
                }

                Close();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, "Could not save screenshot", "Choose another location and try again.", ex.Message);
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenViewer();
            }
            catch (Exception ex)
            {
                preserveTemporaryFileForViewer = false;
                AppDialog.ShowError(this, "Could not open preview", "Try opening the screenshot again.", ex.Message);
            }
        }

        internal ScreenshotViewerWindow OpenViewer()
        {
            var viewerWindow = new ScreenshotViewerWindow(screenshot, (BitmapSource)PreviewImage.Source);
            viewerWindow.Show();
            viewerWindow.Activate();
            preserveTemporaryFileForViewer = true;
            Close();
            return viewerWindow;
        }

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
