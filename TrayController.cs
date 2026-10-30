using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace BetterScreenShot;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Icon icon;

    public TrayController(MainWindow launcher, Action exit)
    {
        icon = CreateIcon();
        menu.Items.Add("Open BetterScreenShot", null, (_, _) => launcher.ShowLauncher());
        menu.Items.Add("Keyboard shortcuts…", null, (_, _) => launcher.ShowShortcutSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Capture full screen", null, async (_, _) => await launcher.CaptureFromShortcutAsync(false));
        menu.Items.Add("Select area", null, async (_, _) => await launcher.CaptureFromShortcutAsync(true));
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
        menu.Opening += (_, _) => startup.Checked = StartupRegistration.IsEnabled(Environment.ProcessPath!);
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (_, _) => exit());
        notifyIcon = new Forms.NotifyIcon { Icon = icon, Text = "BetterScreenShot", ContextMenuStrip = menu, Visible = true };
        notifyIcon.DoubleClick += (_, _) => launcher.ShowLauncher();
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
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
