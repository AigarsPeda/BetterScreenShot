using System.Windows;

namespace BetterScreenShot;

public partial class App : System.Windows.Application
{
    private BackgroundInstance? instance;
    private TrayController? tray;
    private MainWindow? launcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--uninstall"))
        {
            try
            {
                var script = System.IO.Path.Combine(AppContext.BaseDirectory, "Uninstall.ps1");
                if (!System.IO.File.Exists(script)) throw new System.IO.FileNotFoundException("The uninstaller is missing. Reinstall BetterScreenShot and try again.", script);
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-File");
                start.ArgumentList.Add(script);
                System.Diagnostics.Process.Start(start);
            }
            catch (Exception error)
            {
                System.Windows.MessageBox.Show(error.Message, "Could not uninstall BetterScreenShot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }
        instance = new BackgroundInstance(
            () => Dispatcher.BeginInvoke(new Action(() => launcher?.ShowLauncher())),
            () => Dispatcher.BeginInvoke(new Action(ExitApplication)));
        if (e.Args.Contains("--exit"))
        {
            if (!instance.IsPrimary) instance.RequestExit();
            Shutdown();
            return;
        }
        if (!instance.IsPrimary)
        {
            if (!e.Args.Contains("--background")) instance.RequestShow();
            Shutdown();
            return;
        }

        launcher = new MainWindow { Title = "BetterScreenShot" };
        MainWindow = launcher;
        launcher.EnableBackgroundMode();
        tray = new TrayController(launcher, ExitApplication);
        if (!e.Args.Contains("--background")) launcher.ShowLauncher();
    }

    private void ExitApplication()
    {
        launcher?.ExitBackgroundMode();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
