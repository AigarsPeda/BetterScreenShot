using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace BetterScreenShot;

public partial class CaptureShortcutsWindow : Window
{
    private readonly Func<CaptureShortcutSettings, string?> save;
    private bool? recordingArea;
    internal CaptureShortcutSettings Draft { get; private set; }

    internal CaptureShortcutsWindow(CaptureShortcutSettings settings, Func<CaptureShortcutSettings, string?> save)
    {
        InitializeComponent();
        this.save = save;
        Draft = settings;
        var theme = ThemeIconService.DetectWindowsTheme();
        ThemeIconService.ApplyViewerTheme(Resources, theme);
        Resources["ShortcutErrorBrush"] = new SolidColorBrush(theme == AppTheme.Dark ? Colors.LightPink : Colors.Firebrick);
        Icon = ThemeIconService.CreateScanIcon(theme);
        SourceInitialized += (_, _) =>
        {
            var dark = theme == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        FullScreenShortcutButton.Content = recordingArea == false ? "Press shortcut…" : Draft.FullScreen.IsEnabled ? Draft.FullScreen.DisplayText : "Click to set";
        AreaShortcutButton.Content = recordingArea == true ? "Press shortcut…" : Draft.Area.IsEnabled ? Draft.Area.DisplayText : "Click to set";
    }

    private void RecordShortcut_Click(object sender, RoutedEventArgs e)
    {
        recordingArea = Equals(((Button)sender).Tag, "Area");
        ShowError(null);
        UpdateLabels();
    }

    internal void SetShortcut(bool area, CaptureShortcut shortcut)
    {
        Draft = area ? Draft with { Area = shortcut } : Draft with { FullScreen = shortcut };
        recordingArea = null;
        ShowError(null);
        UpdateLabels();
    }

    private void Shortcut_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (recordingArea is not { } area) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Escape || key == Key.Tab && modifiers == ModifierKeys.None)
        {
            recordingArea = null;
            UpdateLabels();
            e.Handled = key == Key.Escape;
            return;
        }
        e.Handled = true;
        if (CaptureShortcut.IsModifier(key)) return;
        var shortcut = key is Key.Delete or Key.Back && modifiers == ModifierKeys.None ? new CaptureShortcut() : new CaptureShortcut(key, modifiers);
        if (shortcut.ValidationError() is { } error) { ShowError(error); return; }
        SetShortcut(area, shortcut);
    }

    private void Shortcut_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        recordingArea = null;
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
        recordingArea = null;
        UpdateLabels();
        var error = Draft.ValidationError() ?? save(Draft);
        ShowError(error);
        if (error is null) DialogResult = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
