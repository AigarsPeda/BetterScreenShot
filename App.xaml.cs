using System;
using System.Windows;

namespace BetterScreenShot
{
    /// <summary>
    /// Interaction logic for App.xaml 1
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }
}
