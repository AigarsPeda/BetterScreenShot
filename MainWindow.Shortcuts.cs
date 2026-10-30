using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace BetterScreenShot;

public partial class MainWindow
{
    private readonly CaptureShortcutSettingsStore shortcutStore;
    private CaptureShortcutSettings shortcutSettings = CaptureShortcutSettings.Disabled;
    private GlobalCaptureShortcuts? globalShortcuts;
    private bool configuringShortcuts;

    private void LoadCaptureShortcuts()
    {
        shortcutSettings = shortcutStore.Load(out var error);
        UpdateShortcutLabels();
        if (error is not null) SetStatus(StatusTone.Error, error);
    }

    private void InitializeCaptureShortcuts()
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        globalShortcuts = new GlobalCaptureShortcuts(source, area =>
        {
            if (configuringShortcuts || isCapturing || isClosed) return;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!configuringShortcuts) await CaptureFromShortcutAsync(area);
            }));
        });
        if (!globalShortcuts.TryApply(shortcutSettings, out var error)) SetStatus(StatusTone.Error, error!);
    }

    private void UpdateShortcutLabels()
    {
        FullScreenShortcutLabel.Text = shortcutSettings.FullScreen.IsEnabled ? shortcutSettings.FullScreen.DisplayText : "Shortcut not set";
        AreaShortcutLabel.Text = shortcutSettings.Area.IsEnabled ? shortcutSettings.Area.DisplayText : "Shortcut not set";
    }

    private void ShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        if (isCapturing || globalShortcuts is null) return;
        configuringShortcuts = true;
        // Release the current shortcuts so their keystrokes reach the recorder.
        globalShortcuts.TryApply(CaptureShortcutSettings.Disabled, out _);
        try
        {
            var dialog = new CaptureShortcutsWindow(shortcutSettings, SaveCaptureShortcuts) { Owner = this };
            if (dialog.ShowDialog() == true) SetStatus(StatusTone.Success, "Capture shortcuts saved.");
        }
        finally
        {
            configuringShortcuts = false;
            if (!isClosed && !globalShortcuts.TryApply(shortcutSettings, out var error)) SetStatus(StatusTone.Error, error!);
        }
    }

    private string? SaveCaptureShortcuts(CaptureShortcutSettings settings)
    {
        if (!globalShortcuts!.TryApply(settings, out var error)) return error;
        try
        {
            shortcutStore.Save(settings);
            shortcutSettings = settings;
            UpdateShortcutLabels();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            globalShortcuts.TryApply(CaptureShortcutSettings.Disabled, out _);
            return $"Could not save shortcuts. {ex.Message}";
        }
    }
}
