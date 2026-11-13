using System.Windows;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly UpdateService updates;
    private readonly CancellationTokenSource updateCancellation = new();
    private readonly DispatcherTimer updateButtonTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool checkingUpdates;
    private bool isClosed;
    internal CaptureShortcutSettings Draft { get; private set; }
    internal EditorSettings EditorDraft { get; private set; }

    internal CaptureShortcutsWindow(CaptureShortcutSettings settings, Func<CaptureShortcutSettings, string?> save)
        : this(settings, (capture, _) => save(capture)) { }

    internal CaptureShortcutsWindow(CaptureShortcutSettings settings, Func<CaptureShortcutSettings, EditorSettings, string?> save,
        UpdateService? updateService = null)
    {
        InitializeComponent();
        this.save = save;
        updates = updateService ?? UpdateService.Shared;
        CurrentVersionText.Text = $"Version {UpdateService.CurrentVersion.ToString(3)}";
        updateButtonTimer.Tick += (_, _) => RefreshUpdateButton();
        RefreshUpdateButton();
        Draft = settings;
        WindowsSnipShortcutCheck.IsChecked = settings.UseWindowsSnipShortcut;
        EditorDraft = EditorSettingsService.Current;
        currentTheme = ThemePreferenceService.CurrentTheme;
        ApplyTheme(currentTheme);
        SourceInitialized += (_, _) => ThemeIconService.ApplyTitleBarTheme(this, currentTheme);
        Deactivated += (_, _) => StopRecording();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        ThemePreferenceService.Changed += ThemePreference_Changed;
        Closed += (_, _) =>
        {
            isClosed = true;
            updateButtonTimer.Stop();
            updateCancellation.Cancel();
            updateCancellation.Dispose();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed -= ThemePreference_Changed;
            recordingHook?.Dispose();
            recordingHook = null;
        };
        UpdateLabels();
        InitializeEditorSettings();
        SelectCategory("Capture");
    }

    private void RefreshUpdateButton()
    {
        var seconds = (int)Math.Ceiling(updates.ManualCheckRetryAfter.TotalSeconds);
        CheckVersionButton.IsEnabled = !checkingUpdates && seconds == 0;
        CheckVersionButton.Content = checkingUpdates ? "Checking…"
            : seconds > 0 ? $"Check again in {seconds}s" : "Check for new version";
        if (seconds > 0) updateButtonTimer.Start();
        else updateButtonTimer.Stop();
    }

    private async void CheckVersion_Click(object sender, RoutedEventArgs e)
    {
        if (isClosed || checkingUpdates || updates.ManualCheckRetryAfter > TimeSpan.Zero) return;
        checkingUpdates = true;
        RefreshUpdateButton();
        UpdateDownloadLink.Visibility = Visibility.Collapsed;
        UpdateCheckStatus.Visibility = Visibility.Visible;
        UpdateCheckStatus.Text = "Checking for a new version…";
        try
        {
            var release = await updates.CheckManualAsync(updateCancellation.Token);
            if (isClosed) return;
            UpdateCheckStatus.Text = release is null
                ? $"You have the latest version ({UpdateService.CurrentVersion.ToString(3)})."
                : $"Version {release.DisplayVersion} is available.";
            UpdateDownloadLink.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) when (isClosed) { }
        catch (UpdateCheckThrottledException)
        {
            if (!isClosed) UpdateCheckStatus.Text = "Please wait before checking again.";
        }
        catch (Exception)
        {
            if (!isClosed) UpdateCheckStatus.Text = "Could not check for updates. Check your connection and try again.";
        }
        finally
        {
            checkingUpdates = false;
            if (!isClosed) RefreshUpdateButton();
        }
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateService.DownloadUrl.AbsoluteUri)
                { UseShellExecute = true });
        }
        catch (Exception)
        {
            UpdateCheckStatus.Text = "Could not open the download. Visit www.aigarspeda.com to download the latest version.";
        }
    }

    private void InitializeEditorSettings()
    {
        foreach (var seconds in new[] { 5, 10, 15, 30, 60 }.Append(EditorDraft.CopyDismissSeconds).Distinct().Order())
            CopyDismissPicker.Items.Add(new ComboBoxItem { Content = $"{seconds} seconds", Tag = seconds });
        CopyDismissPicker.SelectedItem = CopyDismissPicker.Items.Cast<ComboBoxItem>().Single(item => (int)item.Tag == EditorDraft.CopyDismissSeconds);
        var quality = EditorDraft.JpegQuality.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!JpegQualityPicker.Items.Cast<ComboBoxItem>().Any(item => Equals(item.Tag, quality)))
            JpegQualityPicker.Items.Add(new ComboBoxItem { Content = $"Custom ({quality})", Tag = quality });
        JpegQualityPicker.SelectedItem = JpegQualityPicker.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, quality));
        foreach (var tool in EditorSettings.Tools)
        {
            DefaultToolPicker.Items.Add(new ComboBoxItem { Content = ScreenshotViewerWindow.ToolLabel(tool), Tag = tool });
            var panel = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 10, 12) };
            panel.Children.Add(new TextBlock { Text = ScreenshotViewerWindow.ToolLabel(tool), Margin = new Thickness(0, 0, 0, 5) });
            var picker = new ComboBox();
            System.Windows.Automation.AutomationProperties.SetName(picker, $"{ScreenshotViewerWindow.ToolLabel(tool)} shortcut");
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
            currentTheme = theme;
            ApplyTheme(theme);
            ThemeIconService.ApplyTitleBarTheme(this, theme);
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
        if (error is null)
        {
            CaptureValidationText.Visibility = EditorValidationText.Visibility = Visibility.Collapsed;
            foreach (var picker in editorKeyPickers.Values) System.Windows.Automation.AutomationProperties.SetHelpText(picker, "");
            System.Windows.Automation.AutomationProperties.SetHelpText(FullScreenShortcutButton, "");
            System.Windows.Automation.AutomationProperties.SetHelpText(AreaShortcutButton, "");
        }
    }

    private void SaveShortcuts_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        Draft = Draft with { UseWindowsSnipShortcut = WindowsSnipShortcutCheck.IsChecked == true };
        EditorDraft = EditorDraft with
        {
            DefaultTool = (AnnotationTool)((ComboBoxItem)DefaultToolPicker.SelectedItem).Tag,
            RememberLastTool = RememberToolCheck.IsChecked == true,
            Color = (string)((ComboBoxItem)DefaultColorPicker.SelectedItem).Tag,
            StrokeWidth = double.Parse((string)((ComboBoxItem)DefaultStrokePicker.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture),
            TextSize = double.Parse((string)((ComboBoxItem)DefaultTextSizePicker.SelectedItem).Content, System.Globalization.CultureInfo.InvariantCulture),
            SketchEnabled = DefaultSketchCheck.IsChecked == true,
            JpegQuality = int.Parse((string)((ComboBoxItem)JpegQualityPicker.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture),
            CopyDismissSeconds = (int)((ComboBoxItem)CopyDismissPicker.SelectedItem).Tag,
            ToolKeys = editorKeyPickers.ToDictionary(pair => pair.Key, pair => (Key)((ComboBoxItem)pair.Value.SelectedItem).Tag)
        };
        var error = Draft.ValidationError() ?? EditorDraft.ValidationError() ?? save(Draft, EditorDraft);
        ShowError(error);
        if (error is not null)
        {
            var duplicates = editorKeyPickers.Values.GroupBy(p => ((ComboBoxItem)p.SelectedItem).Tag).FirstOrDefault(g => g.Count() > 1);
            System.Windows.Controls.Control target;
            if (Draft.ValidationError() is null && duplicates is not null)
            {
                SelectCategory("Editor");
                EditorValidationText.Text = error;
                EditorValidationText.Visibility = Visibility.Visible;
                target = duplicates.First();
            }
            else
            {
                SelectCategory("Capture");
                CaptureValidationText.Text = error;
                CaptureValidationText.Visibility = Visibility.Visible;
                target = error.Contains("Full Screen", StringComparison.OrdinalIgnoreCase) || Draft.FullScreen.ValidationError() is not null
                    ? FullScreenShortcutButton : AreaShortcutButton;
            }
            System.Windows.Automation.AutomationProperties.SetHelpText(target, error);
            target.BringIntoView();
            target.Focus();
        }
        if (error is null) DialogResult = true;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        SelectCategory((string)((Button)sender).Tag);
    }

    private void SelectCategory(string category)
    {
        foreach (var panel in new[] { CaptureCategory, EditorCategory, ExportCategory, AboutCategory })
            panel.Visibility = panel.Name == category + "Category" ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button button in CategoryButtons.Children)
        {
            button.SetResourceReference(BackgroundProperty, Equals(button.Tag, category) ? "ViewerSelectedBrush" : "ViewerSurfaceBrush");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, Equals(button.Tag, category) ? "Selected" : "");
        }
    }

}
