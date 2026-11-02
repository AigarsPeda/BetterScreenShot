using System.Runtime.InteropServices;
using System.Windows;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;

namespace BetterScreenShot;

public partial class CaptureShortcutsWindow : Window
{
    private sealed record NativeOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    private readonly Func<CaptureShortcutSettings, EditorSettings, string?> save;
    private readonly Dictionary<AnnotationTool, ComboBox> editorKeyPickers = [];
    private bool? recordingArea;
    private ShortcutRecordingHook? recordingHook;
    private AppTheme currentTheme;
    internal CaptureShortcutSettings Draft { get; private set; }
    internal EditorSettings EditorDraft { get; private set; }

    internal CaptureShortcutsWindow(CaptureShortcutSettings settings, Func<CaptureShortcutSettings, string?> save)
        : this(settings, (capture, _) => save(capture)) { }

    internal CaptureShortcutsWindow(CaptureShortcutSettings settings, Func<CaptureShortcutSettings, EditorSettings, string?> save)
    {
        InitializeComponent();
        this.save = save;
        Draft = settings;
        EditorDraft = EditorSettingsService.Current;
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
        InitializeEditorSettings();
    }

    private void InitializeEditorSettings()
    {
        foreach (var tool in EditorSettings.Tools)
        {
            DefaultToolPicker.Items.Add(new ComboBoxItem { Content = tool.ToString(), Tag = tool });
            var panel = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 10, 12) };
            panel.Children.Add(new TextBlock { Text = tool.ToString(), Margin = new Thickness(0, 0, 0, 5) });
            var picker = new ComboBox();
            System.Windows.Automation.AutomationProperties.SetName(picker, $"{tool} shortcut");
            foreach (var key in Enumerable.Range((int)Key.A, 26).Select(value => (Key)value).Where(key => key != Key.E))
                picker.Items.Add(new ComboBoxItem { Content = key.ToString(), Tag = key });
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(item => (Key)item.Tag == EditorDraft.ToolKeys[tool]);
            editorKeyPickers.Add(tool, picker);
            panel.Children.Add(picker);
            EditorShortcutPanel.Children.Add(panel);
        }
        DefaultToolPicker.SelectedItem = DefaultToolPicker.Items.Cast<ComboBoxItem>().Single(item => (AnnotationTool)item.Tag == EditorDraft.DefaultTool);
        RememberToolCheck.IsChecked = EditorDraft.RememberLastTool;
        (string Name, string Hex)[] colors = [("Red", "#FF453A"), ("Orange", "#FF9F0A"), ("Yellow", "#FFD60A"),
            ("Green", "#30D158"), ("Blue", "#0A84FF"), ("Purple", "#BF5AF2"), ("White", "#FFFFFF"), ("Black", "#111111")];
        foreach (var (name, hex) in colors) DefaultColorPicker.Items.Add(new ComboBoxItem { Content = name, Tag = hex });
        if (!colors.Any(color => color.Hex.Equals(EditorDraft.Color, StringComparison.OrdinalIgnoreCase)))
            DefaultColorPicker.Items.Add(new ComboBoxItem { Content = EditorDraft.Color, Tag = EditorDraft.Color });
        DefaultColorPicker.SelectedItem = DefaultColorPicker.Items.Cast<ComboBoxItem>().Single(item =>
            ((string)item.Tag).Equals(EditorDraft.Color, StringComparison.OrdinalIgnoreCase));
        DefaultStrokePicker.SelectedIndex = EditorDraft.StrokeWidth switch { 2 => 0, 7 => 2, _ => 1 };
        DefaultTextSizePicker.SelectedIndex = EditorDraft.TextSize switch { 16 => 0, 24 => 1, 40 => 3, 64 => 4, _ => 2 };
        DefaultSketchCheck.IsChecked = EditorDraft.SketchEnabled;
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        var hex = (string)((ComboBoxItem)DefaultColorPicker.SelectedItem).Tag;
        var current = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        using var picker = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        if (picker.ShowDialog(new NativeOwner(new WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
        hex = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        var item = DefaultColorPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(option => Equals(option.Tag, hex));
        if (item is null) DefaultColorPicker.Items.Add(item = new ComboBoxItem { Content = hex, Tag = hex });
        DefaultColorPicker.SelectedItem = item;
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
        EditorDraft = EditorDraft with
        {
            DefaultTool = (AnnotationTool)((ComboBoxItem)DefaultToolPicker.SelectedItem).Tag,
            RememberLastTool = RememberToolCheck.IsChecked == true,
            Color = (string)((ComboBoxItem)DefaultColorPicker.SelectedItem).Tag,
            StrokeWidth = double.Parse((string)((ComboBoxItem)DefaultStrokePicker.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture),
            TextSize = double.Parse((string)((ComboBoxItem)DefaultTextSizePicker.SelectedItem).Content, System.Globalization.CultureInfo.InvariantCulture),
            SketchEnabled = DefaultSketchCheck.IsChecked == true,
            ToolKeys = editorKeyPickers.ToDictionary(pair => pair.Key, pair => (Key)((ComboBoxItem)pair.Value.SelectedItem).Tag)
        };
        var error = Draft.ValidationError() ?? EditorDraft.ValidationError() ?? save(Draft, EditorDraft);
        ShowError(error);
        if (error is null) DialogResult = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
