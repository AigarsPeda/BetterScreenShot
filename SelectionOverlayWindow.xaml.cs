using System.Drawing;
using System.Threading.Tasks;
using System.Windows;

namespace BetterScreenShot
{
    public partial class SelectionOverlayWindow : Window
    {
        public SelectionOverlayWindow()
        {
            InitializeComponent();
        }

        public static void WarmUp() => MonitorSelectionOverlayForm.WarmUpOverlays();

        public static Task<Rectangle?> SelectAreaAsync() => MonitorSelectionOverlayForm.SelectAreaAsync();

        public static Task<Rectangle?> SelectMonitorAsync() => MonitorSelectionOverlayForm.SelectMonitorAsync();

        public static void Shutdown() => MonitorSelectionOverlayForm.Shutdown();
    }
}
