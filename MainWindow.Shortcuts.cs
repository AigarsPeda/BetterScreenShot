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
        var areaShortcuts = new List<string>();
        if (shortcutSettings.Area.IsEnabled && shortcutSettings.Area != WindowsSnipShortcutHook.Shortcut)
            areaShortcuts.Add(shortcutSettings.Area.DisplayText);
        if (shortcutSettings.UseWindowsSnipShortcut) areaShortcuts.Add("Shift+Win+S");
        AreaShortcutLabel.Text = areaShortcuts.Count > 0 ? string.Join(", ", areaShortcuts) : "Shortcut not set";
    }

    private void ShortcutsButton_Click(object sender, RoutedEventArgs e)
        => ShowSettings(this);

    internal void ShowSettings(Window owner)
    {
        if (isCapturing || globalShortcuts is null) return;
        configuringShortcuts = true;
        // Release the current shortcuts so their keystrokes reach the recorder.
        globalShortcuts.TryApply(CaptureShortcutSettings.Disabled, out _);
        try
        {
            var dialog = new CaptureShortcutsWindow(shortcutSettings, SaveSettings) { Owner = owner };
            if (dialog.ShowDialog() == true) SetStatus(StatusTone.Success, "Settings saved.");
        }
        finally
        {
            configuringShortcuts = false;
            if (!isClosed && !globalShortcuts.TryApply(shortcutSettings, out var error)) SetStatus(StatusTone.Error, error!);
        }
    }

    private string? SaveSettings(CaptureShortcutSettings settings, EditorSettings editorSettings)
    {
        if (!globalShortcuts!.TryApply(settings, out var error)) return error;
        var shortcutsSaved = false;
        try
        {
            shortcutStore.Save(settings);
            shortcutsSaved = true;
            EditorSettingsService.Save(editorSettings);
            shortcutSettings = settings;
            UpdateShortcutLabels();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (shortcutsSaved)
            {
                try { shortcutStore.Save(shortcutSettings); }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not restore capture shortcuts: {rollbackError.Message}");
                }
            }
            globalShortcuts.TryApply(shortcutSettings, out _);
            return $"Could not save settings. {ex.Message}";
        }
    }
}
