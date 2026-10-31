using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;

namespace BetterScreenShot;

public partial class CaptureShortcutsWindow : Window
{
    private readonly Func<CaptureShortcutSettings, string?> save;
    private bool? recordingArea;
    private ShortcutRecordingHook? recordingHook;
    private AppTheme currentTheme;
    internal CaptureShortcutSettings Draft { get; private set; }

    internal CaptureShortcutsWindow(CaptureShortcutSettings settings, Func<CaptureShortcutSettings, string?> save)
    {
        InitializeComponent();
        this.save = save;
        Draft = settings;
        currentTheme = ThemePreferenceService.CurrentTheme;
        ApplyTheme(currentTheme);
        SourceInitialized += (_, _) => ApplyWindowChromeTheme(currentTheme);
        Deactivated += (_, _) => StopRecording();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        ThemePreferenceService.Changed += ThemePreference_Changed;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed -= ThemePreference_Changed;
            recordingHook?.Dispose();
            recordingHook = null;
        };
        UpdateLabels();
    }

    private void ApplyTheme(AppTheme theme)
    {
        ThemeIconService.ApplyViewerTheme(Resources, theme);
        Resources["ShortcutErrorBrush"] = new SolidColorBrush(theme == AppTheme.Dark ? Colors.LightPink : Colors.Firebrick);
        Icon = ThemeIconService.CreateScanIcon(theme);
    }

    private void ApplyWindowChromeTheme(AppTheme theme)
    {
        var dark = theme == AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        ThemePreference_Changed();
    }

    private void ThemePreference_Changed()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var theme = ThemePreferenceService.CurrentTheme;
            if (theme == currentTheme) return;
            currentTheme = theme;
            ApplyTheme(theme);
            ApplyWindowChromeTheme(theme);
        }));
    }

    private void UpdateLabels()
    {
        FullScreenShortcutButton.Content = recordingArea == false ? "Press shortcut…" : Draft.FullScreen.IsEnabled ? Draft.FullScreen.DisplayText : "Click to set";
        AreaShortcutButton.Content = recordingArea == true ? "Press shortcut…" : Draft.Area.IsEnabled ? Draft.Area.DisplayText : "Click to set";
    }

    private void RecordShortcut_Click(object sender, RoutedEventArgs e)
    {
        recordingHook?.Dispose();
        recordingArea = Equals(((Button)sender).Tag, "Area");
        ShowError(null);
        try
        {
            recordingHook = new ShortcutRecordingHook(new WindowInteropHelper(this).Handle, Dispatcher,
                shortcut => RecordKey(shortcut.Key, shortcut.Modifiers));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            recordingArea = null;
            recordingHook = null;
            ShowError("Could not protect shortcut recording. Close this dialog and try again.");
        }
        UpdateLabels();
    }

    internal void SetShortcut(bool area, CaptureShortcut shortcut)
    {
        Draft = area ? Draft with { Area = shortcut } : Draft with { FullScreen = shortcut };
        StopRecording();
        ShowError(null);
        UpdateLabels();
    }

    private void Shortcut_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (recordingArea is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = key != Key.Tab || Keyboard.Modifiers != ModifierKeys.None;
        RecordKey(key, Keyboard.Modifiers);
    }

    private void RecordKey(Key key, ModifierKeys modifiers)
    {
        if (recordingArea is not { } area) return;
        if (key == Key.Escape || key == Key.Tab && modifiers == ModifierKeys.None)
        {
            StopRecording();
            return;
        }
        if (CaptureShortcut.IsModifier(key) || key == Key.None) return;
        var shortcut = key is Key.Delete or Key.Back && modifiers == ModifierKeys.None ? new CaptureShortcut() : new CaptureShortcut(key, modifiers);
        if (shortcut.ValidationError() is { } error) { ShowError(error); return; }
        SetShortcut(area, shortcut);
    }

    private void Shortcut_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => StopRecording();

    private void StopRecording()
    {
        recordingArea = null;
        recordingHook?.Stop();
        UpdateLabels();
    }

    private void ClearShortcut_Click(object sender, RoutedEventArgs e) => SetShortcut(Equals(((Button)sender).Tag, "Area"), new CaptureShortcut());

    private void ShowError(string? error)
    {
        ShortcutError.Text = error ?? "";
        ShortcutError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveShortcuts_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        var error = Draft.ValidationError() ?? save(Draft);
        ShowError(error);
        if (error is null) DialogResult = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
