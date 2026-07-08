using System.Drawing;
using System.Windows;

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
