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
