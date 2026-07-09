using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BetterScreenShot
{
    public partial class ScreenshotToastWindow : Window
    {
        private const double ScreenMargin = 16;
        private const int SlideDurationMs = 180;
        private static ScreenshotToastWindow? currentToast;
        private readonly string filePath;
        private double finalLeft;
        private double hiddenLeft;
        private bool isAnimatingClose;
        private bool allowImmediateClose;
        private bool preserveTemporaryFileForViewer;

        public ScreenshotToastWindow(string filePath, System.Drawing.Rectangle captureBounds)
        {
            InitializeComponent();

            this.filePath = filePath;
            PreviewImage.Source = ScreenshotFileService.LoadBitmap(filePath);

            Loaded += (_, _) => OnToastLoaded(captureBounds);
            Closing += ScreenshotToastWindow_Closing;
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
            currentToast?.CloseImmediately();
            currentToast = new ScreenshotToastWindow(filePath, captureBounds);
            currentToast.Show();
        }

        private void OnToastLoaded(System.Drawing.Rectangle captureBounds)
        {
            PositionWindow(captureBounds);
            Left = hiddenLeft;
            Opacity = 0;
            BeginSlideAnimation(hiddenLeft, finalLeft, 0, 1, null);
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

            finalLeft = Math.Max(workAreaLeft + ScreenMargin, workAreaRight - ActualWidth - ScreenMargin);
            hiddenLeft = workAreaRight + 24;
            Left = finalLeft;
            Top = Math.Max(workAreaTop + ScreenMargin, workAreaBottom - ActualHeight - ScreenMargin);
        }

        private void ScreenshotToastWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (allowImmediateClose || isAnimatingClose || !IsLoaded)
            {
                return;
            }

            e.Cancel = true;
            isAnimatingClose = true;
            BeginSlideAnimation(finalLeft, hiddenLeft, Opacity, 0, CloseImmediately);
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

            BeginAnimation(LeftProperty, leftAnimation, HandoffBehavior.SnapshotAndReplace);
            BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        }

        private void CloseImmediately()
        {
            allowImmediateClose = true;
            BeginAnimation(LeftProperty, null);
            BeginAnimation(OpacityProperty, null);
            Close();
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

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                preserveTemporaryFileForViewer = true;

                var viewerWindow = new ScreenshotViewerWindow(filePath);
                viewerWindow.Show();
                viewerWindow.Activate();

                Close();
            }
            catch (Exception ex)
            {
                preserveTemporaryFileForViewer = false;
                System.Windows.MessageBox.Show($"Could not open the screenshot preview. {ex.Message}", "Open Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!preserveTemporaryFileForViewer)
                {
                    ScreenshotFileService.DeleteIfExists(filePath);
                }

                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not discard the screenshot. {ex.Message}", "Discard Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
