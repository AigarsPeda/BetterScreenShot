using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace BetterScreenShot;

public partial class MainWindow
{
    private bool keepRunningInBackground;

    internal void EnableBackgroundMode()
    {
        keepRunningInBackground = true;
        // Register hotkeys on a hidden HWND; never show a window just to initialize it.
        new WindowInteropHelper(this).EnsureHandle();
        MonitorSelectionOverlayForm.WarmUpOverlays();
    }

    internal void ShowLauncher()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal void ShowSettingsFromTray()
    {
        ShowLauncher();
        ShowSettings(this);
    }

    internal Task CaptureFromShortcutAsync(bool area) => CaptureAsync(area, fromShortcut: true);

    internal bool ExitBackgroundMode()
    {
        keepRunningInBackground = false;
        Close();
        if (!isClosed) keepRunningInBackground = true;
        return isClosed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (keepRunningInBackground)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            if (!ScreenshotToastWindow.ConfirmPendingExit(this)) e.Cancel = true;
            if (!e.Cancel)
                foreach (var editor in System.Windows.Application.Current.Windows.OfType<ScreenshotViewerWindow>().ToArray())
                {
                    editor.Close();
                    if (System.Windows.Application.Current.Windows.OfType<ScreenshotViewerWindow>().Contains(editor)) { e.Cancel = true; break; }
                }
        }
        base.OnClosing(e);
    }
}
