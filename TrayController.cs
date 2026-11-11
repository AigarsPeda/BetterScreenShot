using System.Runtime.InteropServices;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterScreenShot;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Icon icon;
    private readonly CancellationTokenSource updateCancellation = new();
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromDays(1) };
    private readonly Forms.ToolStripMenuItem downloadUpdate = new("Download update…") { Visible = false };
    private readonly Forms.ToolStripMenuItem checkForUpdates = new("Check for updates…");
    private readonly UpdateService updates;
    private AvailableUpdate? availableUpdate;
    private Version? notifiedVersion;
    private bool checkingUpdates;
    private bool downloadingUpdate;
    private bool disposed;

    public TrayController(MainWindow launcher, Action exit)
    {
        icon = CreateIcon();
        updates = UpdateService.Shared;
        menu.Items.Add("Open BetterScreenShot", null, (_, _) => launcher.ShowLauncher());
        menu.Items.Add("Settings…", null, (_, _) => launcher.ShowSettingsFromTray());
        checkForUpdates.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        menu.Items.Add(checkForUpdates);
        downloadUpdate.Click += async (_, _) => await DownloadUpdateAsync();
        menu.Items.Add(downloadUpdate);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Capture full screen", null, async (_, _) => await launcher.CaptureFromShortcutAsync(false));
        menu.Items.Add("Select area", null, async (_, _) => await launcher.CaptureFromShortcutAsync(true));
        menu.Items.Add("Scrolling capture", null, async (_, _) => await launcher.CaptureScrollingAsync());
        menu.Items.Add("Record area", null, async (_, _) => await launcher.RecordAreaAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startup.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(Environment.ProcessPath!, startup.Checked); }
            catch (Exception error)
            {
                launcher.ShowLauncher();
                AppDialog.ShowError(launcher, "Could not change startup", "Try again from the tray menu.", error.Message);
            }
        };
        menu.Opening += (_, _) =>
        {
            startup.Checked = StartupRegistration.IsEnabled(Environment.ProcessPath!);
            checkForUpdates.Enabled = !checkingUpdates && updates.ManualCheckRetryAfter == TimeSpan.Zero;
        };
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (_, _) => exit());
        notifyIcon = new Forms.NotifyIcon { Icon = icon, Text = "BetterScreenShot", ContextMenuStrip = menu, Visible = true };
        notifyIcon.DoubleClick += (_, _) => launcher.ShowLauncher();
        updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);
        updateTimer.Start();
        // Network initialization and its JIT work must not delay the first window.
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => _ = CheckForUpdatesAsync(manual: false)),
            DispatcherPriority.ApplicationIdle);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (disposed || checkingUpdates) return;
        checkingUpdates = true;
        try
        {
            availableUpdate = manual
                ? await updates.CheckManualAsync(updateCancellation.Token)
                : await updates.CheckAsync(updateCancellation.Token);
            if (disposed) return;
            downloadUpdate.Visible = availableUpdate is not null;
            if (availableUpdate is { } release)
            {
                downloadUpdate.Text = $"Download version {release.DisplayVersion}…";
                if (manual || notifiedVersion != release.Version)
                {
                    notifyIcon.ShowBalloonTip(10000, "BetterScreenShot update available",
                        $"Version {release.DisplayVersion} is available. Use the tray menu to download it.", Forms.ToolTipIcon.Info);
                    notifiedVersion = release.Version;
                }
            }
            else if (manual)
                notifyIcon.ShowBalloonTip(6000, "BetterScreenShot is up to date",
                    $"You have version {UpdateService.CurrentVersion.ToString(3)}.", Forms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (UpdateCheckThrottledException) { }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {error.Message}");
            if (manual && !disposed)
                notifyIcon.ShowBalloonTip(7000, "Could not check for updates",
                    "Check your connection and try again.", Forms.ToolTipIcon.Warning);
        }
        finally { checkingUpdates = false; }
    }

    private async Task DownloadUpdateAsync()
    {
        if (disposed || availableUpdate is not { } release || downloadingUpdate) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Download BetterScreenShot",
            FileName = $"BetterScreenShot-{release.DisplayVersion}.exe",
            Filter = "Windows application (*.exe)|*.exe",
            DefaultExt = ".exe",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        downloadingUpdate = true;
        downloadUpdate.Enabled = false;
        downloadUpdate.Text = $"Downloading version {release.DisplayVersion}…";
        try
        {
            notifyIcon.ShowBalloonTip(4000, "Downloading BetterScreenShot",
                "The installer is being saved to the folder you chose.", Forms.ToolTipIcon.Info);
            await updates.DownloadAsync(release, dialog.FileName, updateCancellation.Token);
            if (disposed) return;
            notifyIcon.ShowBalloonTip(8000, "BetterScreenShot downloaded",
                $"Open {System.IO.Path.GetFileName(dialog.FileName)} to install or update automatically.", Forms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Update download failed: {error.Message}");
            if (!disposed)
                notifyIcon.ShowBalloonTip(8000, "Could not download BetterScreenShot",
                    "The download was not saved. Try again from the tray menu.", Forms.ToolTipIcon.Warning);
        }
        finally
        {
            downloadingUpdate = false;
            if (!disposed)
            {
                downloadUpdate.Text = $"Download version {release.DisplayVersion}…";
                downloadUpdate.Enabled = true;
            }
        }
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Color.FromArgb(122, 162, 255), 3))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.DrawLines(pen, new Point[] { new(4, 12), new(4, 4), new(12, 4) });
            graphics.DrawLines(pen, new Point[] { new(20, 4), new(28, 4), new(28, 12) });
            graphics.DrawLines(pen, new Point[] { new(28, 20), new(28, 28), new(20, 28) });
            graphics.DrawLines(pen, new Point[] { new(12, 28), new(4, 28), new(4, 20) });
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        disposed = true;
        updateTimer.Stop();
        updateCancellation.Cancel();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
        updateCancellation.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
