using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace BetterScreenShot
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new nint(-4);

        protected override void OnStartup(StartupEventArgs e)
        {
            TryEnablePerMonitorDpiAwareness();
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        private static void TryEnablePerMonitorDpiAwareness()
        {
            try
            {
                SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(nint dpiContext);
    }
}
