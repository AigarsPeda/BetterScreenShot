using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal static class MonitorGeometry
    {
        private const int EnumCurrentSettings = -1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

        public static Rectangle GetBounds(Forms.Screen screen)
        {
            if (TryGetNativeMonitorBounds(screen.DeviceName, out var nativeBounds))
            {
                return nativeBounds;
            }

            return screen.Bounds;
        }

        private static bool TryGetNativeMonitorBounds(string deviceName, out Rectangle bounds)
        {
            var devMode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
            {
                bounds = default;
                return false;
            }

            bounds = new Rectangle(devMode.dmPositionX, devMode.dmPositionY, devMode.dmPelsWidth, devMode.dmPelsHeight);
            return true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            private const int DeviceNameLength = 32;
            private const int FormNameLength = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameLength)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FormNameLength)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
