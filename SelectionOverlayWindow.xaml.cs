using System.Drawing;
using System.Windows;
using System.Windows.Input;

namespace BetterScreenShot
{
    public partial class SelectionOverlayWindow : Window
    {
        public SelectionOverlayWindow()
        {
            InitializeComponent();
        }

        public static void WarmUp()
        {
            MonitorSelectionOverlayForm.WarmUpOverlays();
        }

        public static SelectedCaptureResult? SelectArea(System.Action? onOverlaysShown = null)
        {
            return MonitorSelectionOverlayForm.SelectArea(onOverlaysShown);
        }

        private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
        }

        private void OverlayCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
        }

        private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
        }
    }

    public sealed class SelectedCaptureResult
    {
        public SelectedCaptureResult(Bitmap capturedBitmap, Rectangle sourceScreenBounds)
        {
            CapturedBitmap = capturedBitmap;
            SourceScreenBounds = sourceScreenBounds;
        }

        public Bitmap CapturedBitmap { get; }

        public Rectangle SourceScreenBounds { get; }
    }
}
