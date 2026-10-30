using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BetterScreenShot
{
    internal static class DesktopWindowService
    {
        private const uint NoSize = 0x0001;
        private const uint NoZOrder = 0x0004;
        private const uint NoActivate = 0x0010;

        public static void FlushPendingUpdates()
        {
            Marshal.ThrowExceptionForHR(DwmFlush());
        }

        public static void PositionToast(System.Windows.Window window, Rectangle captureBounds, int margin)
        {
            var workArea = System.Windows.Forms.Screen.FromRectangle(captureBounds).WorkingArea;
            var handle = new WindowInteropHelper(window).EnsureHandle();

            // Move to the target monitor first so WM_DPICHANGED selects its scale.
            SetPosition(handle, workArea.Left, workArea.Top, 0, 0, NoSize);
            var scale = GetDpiForWindow(handle) / 96d;
            var size = new Size((int)Math.Round(window.ActualWidth * scale), (int)Math.Round(window.ActualHeight * scale));
            var position = CaptureGeometry.ToastPosition(workArea, size, (int)Math.Round(margin * scale));
            SetPosition(handle, position.X, position.Y, size.Width, size.Height, 0);
        }

        private static void SetPosition(IntPtr handle, int x, int y, int width, int height, uint flags)
        {
            if (!SetWindowPos(handle, IntPtr.Zero, x, y, width, height, flags | NoZOrder | NoActivate))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
