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

    internal void ShowShortcutSettings()
    {
        ShowLauncher();
        ShortcutsButton_Click(this, new RoutedEventArgs());
    }

    internal Task CaptureFromShortcutAsync(bool area) => CaptureAsync(area, fromShortcut: true);

    internal void ExitBackgroundMode()
    {
        keepRunningInBackground = false;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (keepRunningInBackground)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
