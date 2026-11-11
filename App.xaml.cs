using System.Windows;

namespace BetterScreenShot;

public partial class App : System.Windows.Application
{
    private BackgroundInstance? instance;
    private TrayController? tray;
    private MainWindow? launcher;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--register-file-associations"))
        {
            try
            {
                FileAssociationRegistration.Register(Environment.ProcessPath!);
                Shutdown();
            }
            catch (Exception error)
            {
                System.Windows.MessageBox.Show(error.Message, "Could not register BetterScreenShot", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        if (await ApplicationInstaller.HandleLaunchAsync(e.Args))
        {
            Shutdown();
            return;
        }
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
            () => Dispatcher.BeginInvoke(new Action(ExitApplication)),
            openImages: paths => Dispatcher.BeginInvoke(new Action(() => OpenImages(paths))));
        if (e.Args.Contains("--exit"))
        {
            if (!instance.IsPrimary) instance.RequestExit();
            Shutdown();
            return;
        }
        if (!instance.IsPrimary)
        {
            try
            {
                var paths = ImportedImageService.GetLaunchPaths(e.Args);
                if (paths.Length > 0) await instance.RequestOpenImagesAsync(paths);
                else if (!e.Args.Contains("--background")) instance.RequestShow();
            }
            catch (Exception error)
            {
                System.Windows.MessageBox.Show(error.Message, "Could not open image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        launcher = new MainWindow { Title = "BetterScreenShot" };
        MainWindow = launcher;
        launcher.EnableBackgroundMode();
        try
        {
            var paths = ImportedImageService.GetLaunchPaths(e.Args);
            if (paths.Length > 0)
            {
                OpenImages(paths);
                // Show the requested editor before initializing the tray menu.
                _ = Dispatcher.BeginInvoke(new Action(StartBackgroundServices), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else
            {
                StartBackgroundServices();
                if (!e.Args.Contains("--background")) launcher.ShowLauncher();
            }
        }
        catch (Exception error)
        {
            StartBackgroundServices();
            launcher.ShowLauncher();
            AppDialog.ShowError(launcher, "Could not open image", "The image path is invalid.", error.Message);
        }
    }

    private void StartBackgroundServices()
    {
        if (tray is not null || launcher is null || Dispatcher.HasShutdownStarted) return;
        tray = new TrayController(launcher, ExitApplication);
    }

    private void OpenImages(string[] paths)
    {
        foreach (var path in paths) launcher?.OpenImage(path);
    }

    private void ExitApplication()
    {
        if (launcher is not null && !launcher.ExitBackgroundMode()) return;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
