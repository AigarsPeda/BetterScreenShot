using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace BetterScreenShot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private StatusTone _currentStatusTone;
        private string _currentStatusMessage = "Ready.";
        private bool isCapturing;
        private bool isClosed;
        private AppTheme? currentTheme;

        public MainWindow() : this(CaptureShortcutSettingsStore.Default) { }

        internal MainWindow(CaptureShortcutSettingsStore shortcutStore)
        {
            this.shortcutStore = shortcutStore;
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed += ThemePreference_Changed;
            ApplyThemeFromSystem();
            LoadCaptureShortcuts();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!isClosed && !isCapturing)
                    {
                        MonitorSelectionOverlayForm.WarmUpOverlays();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Overlay warm-up failed: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ThemeIconService.ApplyTitleBarTheme(this, currentTheme ?? AppTheme.Light);
            InitializeCaptureShortcuts();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            isClosed = true;
            globalShortcuts?.Dispose();
            MonitorSelectionOverlayForm.Shutdown();
            ScreenshotToastWindow.CloseCurrent();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed -= ThemePreference_Changed;
            Loaded -= MainWindow_Loaded;
            SourceInitialized -= MainWindow_SourceInitialized;
            Closed -= MainWindow_Closed;
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyThemeFromSystem));
        }

        private void ThemePreference_Changed() => Dispatcher.BeginInvoke(new Action(ApplyThemeFromSystem));

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            try { ThemePreferenceService.Toggle(); }
            catch (Exception error)
            {
                AppDialog.ShowError(this, "Could not change theme", "Try changing the theme again.", error.Message);
            }
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureAsync(selectArea: false);
        }

        private async void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureAsync(selectArea: true);
        }

        private async void ScrollingCaptureButton_Click(object sender, RoutedEventArgs e)
            => await CaptureScrollingAsync();

        private async void RecordAreaButton_Click(object sender, RoutedEventArgs e)
            => await RecordAreaAsync();

        internal async Task RecordAreaAsync()
        {
            if (isCapturing || isClosed) return;
            isCapturing = true;
            CaptureButton.IsEnabled = false;
            SelectAreaButton.IsEnabled = false;
            ScrollingCaptureButton.IsEnabled = false;
            RecordAreaButton.IsEnabled = false;
            OpenImageButton.IsEnabled = false;
            ShortcutsButton.IsEnabled = false;
            string? pendingFile = null;
            CaptureAreaOutlineWindow? outline = null;
            try
            {
                SetStatus(StatusTone.Info, "Drag to select the area to record.");
                ScreenshotToastWindow.HideCurrent();
                Hide();
                var bounds = await MonitorSelectionOverlayForm.SelectAreaAsync();
                if (isClosed) return;
                if (bounds is null)
                {
                    SetStatus(StatusTone.Info, "Area recording cancelled.");
                    return;
                }
                outline = new CaptureAreaOutlineWindow(bounds.Value);
                outline.Show();
                if (!outline.CanExcludeFromCapture || !outline.IsClickThrough)
                    throw new InvalidOperationException("Windows could not display the capture border without including it in the recording.");
                var controls = new AreaRecordingWindow(bounds.Value);
                controls.Show();
                var recording = await controls.Completion;
                if (isClosed) return;
                if (recording is null)
                {
                    SetStatus(StatusTone.Info, "Area recording cancelled.");
                    return;
                }
                pendingFile = recording.Value.FilePath;
                ShowLauncher();
                ScreenshotToastWindow.ShowRecordingToast(pendingFile, bounds.Value, recording.Value.Preview, recording.Value.Format);
                pendingFile = null;
                SetStatus(StatusTone.Success, "Recording ready. Use the popup to save or copy it.");
            }
            catch (Exception error)
            {
                if (!isClosed) SetStatus(StatusTone.Error, $"Area recording failed: {error.Message}");
            }
            finally
            {
                outline?.Close();
                if (pendingFile is not null) ScreenshotFileService.TryDelete(pendingFile);
                isCapturing = false;
                if (!isClosed)
                {
                    CaptureButton.IsEnabled = true;
                    SelectAreaButton.IsEnabled = true;
                    ScrollingCaptureButton.IsEnabled = true;
                    RecordAreaButton.IsEnabled = true;
                    OpenImageButton.IsEnabled = true;
                    ShortcutsButton.IsEnabled = true;
                    ShowLauncher();
                    ScreenshotToastWindow.RestoreCurrent();
                }
            }
        }

        internal async Task CaptureScrollingAsync()
        {
            if (isCapturing || isClosed) return;
            isCapturing = true;
            CaptureButton.IsEnabled = false;
            SelectAreaButton.IsEnabled = false;
            ScrollingCaptureButton.IsEnabled = false;
            RecordAreaButton.IsEnabled = false;
            OpenImageButton.IsEnabled = false;
            ShortcutsButton.IsEnabled = false;
            string? pendingFile = null;
            CaptureAreaOutlineWindow? outline = null;
            try
            {
                SetStatus(StatusTone.Info, "Drag to select the area to scroll.");
                ScreenshotToastWindow.HideCurrent();
                Hide();
                var bounds = await MonitorSelectionOverlayForm.SelectAreaAsync();
                if (isClosed) return;
                if (bounds is null)
                {
                    SetStatus(StatusTone.Info, "Scrolling capture cancelled.");
                    return;
                }
                outline = new CaptureAreaOutlineWindow(bounds.Value);
                outline.Show();
                if (!outline.CanExcludeFromCapture || !outline.IsClickThrough)
                    throw new InvalidOperationException("Windows could not display the capture border without including it in the image.");
                var controls = new ScrollingCaptureWindow(bounds.Value);
                controls.Show();
                using var image = await controls.Completion;
                if (isClosed) return;
                if (image is null)
                {
                    SetStatus(StatusTone.Info, "Scrolling capture cancelled.");
                    return;
                }
                var capture = await Task.Run(() => ScreenshotCaptureService.SaveScrollingCapture(image));
                pendingFile = capture.FilePath;
                if (isClosed) return;
                ShowLauncher();
                ScreenshotToastWindow.ShowToast(pendingFile, bounds.Value, capture.Preview, previewIsThumbnail: true);
                pendingFile = null;
                SetStatus(controls.MayHaveGaps ? StatusTone.Info : StatusTone.Success,
                    controls.MayHaveGaps
                        ? "Long screenshot captured, but some scrolls may be missing. Review it before saving."
                        : "Long screenshot captured. Use the popup to save or edit it.");
            }
            catch (Exception error)
            {
                if (!isClosed) SetStatus(StatusTone.Error, $"Scrolling capture failed: {error.Message}");
            }
            finally
            {
                outline?.Close();
                if (pendingFile is not null) ScreenshotFileService.TryDelete(pendingFile);
                isCapturing = false;
                if (!isClosed)
                {
                    CaptureButton.IsEnabled = true;
                    SelectAreaButton.IsEnabled = true;
                    ScrollingCaptureButton.IsEnabled = true;
                    RecordAreaButton.IsEnabled = true;
                    OpenImageButton.IsEnabled = true;
                    ShortcutsButton.IsEnabled = true;
                    ShowLauncher();
                    ScreenshotToastWindow.RestoreCurrent();
                }
            }
        }

        private void OpenImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true) OpenImage(dialog.FileName);
        }

        private void Window_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = ImportedImageService.TryGetDroppedImage(e.Data, out _) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (ImportedImageService.TryGetDroppedImage(e.Data, out var path)) OpenImage(path!);
        }

        internal void OpenImage(string filePath)
        {
            try
            {
                ImportedImageService.OpenEditor(filePath);
                SetStatus(StatusTone.Info, "Image opened for editing.");
            }
            catch (Exception error)
            {
                if (!IsVisible) ShowLauncher();
                AppDialog.ShowError(this, "Could not open image", "Choose a PNG, JPEG, BMP, GIF, or TIFF image and try again.", error.Message);
            }
        }

        private async Task CaptureAsync(bool selectArea, bool fromShortcut = false)
        {
            if (isCapturing || isClosed)
            {
                return;
            }

            isCapturing = true;
            CaptureButton.IsEnabled = false;
            SelectAreaButton.IsEnabled = false;
            ScrollingCaptureButton.IsEnabled = false;
            RecordAreaButton.IsEnabled = false;
            OpenImageButton.IsEnabled = false;
            ShortcutsButton.IsEnabled = false;
            string? pendingFile = null;

            try
            {
                SetStatus(StatusTone.Info, selectArea ? "Drag to select the area you want." : "Choose a monitor to capture.");
                ScreenshotToastWindow.HideCurrent();
                Hide();
                var bounds = selectArea
                    ? await MonitorSelectionOverlayForm.SelectAreaAsync()
                    : await MonitorSelectionOverlayForm.SelectMonitorAsync();

                if (isClosed)
                {
                    return;
                }

                if (bounds is null)
                {
                    SetStatus(StatusTone.Info, selectArea ? "Area selection cancelled." : "Full screen capture cancelled.");
                    return;
                }

                // Wait for the compositor, rather than guessing with a fixed delay.
                DesktopWindowService.FlushPendingUpdates();
                var capture = await Task.Run(() => ScreenshotCaptureService.Capture(bounds.Value));
                pendingFile = capture.FilePath;
                if (isClosed)
                {
                    return;
                }

                if (!fromShortcut) ShowLauncher();
                ScreenshotToastWindow.ShowToast(pendingFile, bounds.Value, capture.Preview);
                pendingFile = null; // The toast now owns the temporary file.
                SetStatus(StatusTone.Success, "Screenshot captured. Use the popup to save, copy, or discard it.");
            }
            catch (Exception ex)
            {
                if (!isClosed)
                {
                    SetStatus(StatusTone.Error, $"Error: {ex.Message}");
                }
            }
            finally
            {
                if (pendingFile is not null)
                {
                    ScreenshotFileService.TryDelete(pendingFile);
                }

                isCapturing = false;
                if (!isClosed)
                {
                    CaptureButton.IsEnabled = true;
                    SelectAreaButton.IsEnabled = true;
                    ScrollingCaptureButton.IsEnabled = true;
                    RecordAreaButton.IsEnabled = true;
                    OpenImageButton.IsEnabled = true;
                    ShortcutsButton.IsEnabled = true;
                    if (!fromShortcut) ShowLauncher();
                    ScreenshotToastWindow.RestoreCurrent();
                }
            }
        }

        private void SetStatus(StatusTone tone, string message)
        {
            _currentStatusTone = tone;
            _currentStatusMessage = message;
            StatusTextBlock.Text = message;

            switch (tone)
            {
                case StatusTone.Success:
                    ApplyStatusColors("StatusSuccessBrush", "StatusSuccessTextBrush");
                    break;
                case StatusTone.Info:
                    ApplyStatusColors("StatusInfoBrush", "StatusInfoTextBrush");
                    break;
                case StatusTone.Error:
                    ApplyStatusColors("StatusErrorBrush", "StatusErrorTextBrush");
                    break;
                default:
                    ApplyStatusColors("StatusNeutralBrush", "StatusNeutralTextBrush");
                    break;
            }
        }

        private void ApplyStatusColors(string backgroundKey, string foregroundKey)
        {
            var background = (System.Windows.Media.Brush)FindResource(backgroundKey);
            var foreground = (System.Windows.Media.Brush)FindResource(foregroundKey);
            StatusBadge.Background = background;
            StatusDot.Fill = _currentStatusTone == StatusTone.Neutral
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : foreground;
            StatusTextBlock.Foreground = foreground;
        }

        private void ApplyThemeFromSystem()
        {
            var theme = ThemePreferenceService.CurrentTheme;
            var switchLabel = theme == AppTheme.Dark ? "Switch to light theme" : "Switch to dark theme";
            ThemeButton.ToolTip = switchLabel;
            ThemeButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, switchLabel);
            ThemeGlyphPath.Data = ThemeIconService.ThemeGlyph(theme);
            var animateIcon = currentTheme.HasValue && IsLoaded;
            currentTheme = theme;
            ApplyTheme(theme);
            Icon = ThemeIconService.CreateScanIcon(theme);
            SetStatus(_currentStatusTone, _currentStatusMessage);
            ThemeIconService.ApplyTitleBarTheme(this, theme);
            if (animateIcon) AnimateThemeGlyph();
        }

        private void AnimateThemeGlyph()
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ThemeGlyphRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-35, 0, duration) { EasingFunction = ease });
            var scale = new DoubleAnimation(.7, 1, duration) { EasingFunction = ease };
            ThemeGlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            ThemeGlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            ThemeGlyph.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.3, 1, duration) { EasingFunction = ease });
        }

        private void ApplyTheme(AppTheme theme)
        {
            ThemeIconService.ApplyToolTipTheme(Resources, theme);
            if (SystemParameters.HighContrast)
            {
                foreach (var key in new[] { "PageBackgroundBrush", "SurfaceBrush", "StatusNeutralBrush", "StatusSuccessBrush", "StatusInfoBrush", "StatusErrorBrush" }) Resources[key] = System.Windows.SystemColors.WindowBrush;
                foreach (var key in new[] { "PrimaryTextBrush", "SecondaryTextBrush", "AccentBrush", "FocusBrush", "DividerBrush", "StatusNeutralTextBrush", "StatusSuccessTextBrush", "StatusInfoTextBrush", "StatusErrorTextBrush" }) Resources[key] = System.Windows.SystemColors.WindowTextBrush;
                Resources["SurfaceHoverBrush"] = Resources["SurfacePressedBrush"] = System.Windows.SystemColors.ControlBrush;
                return;
            }
            if (theme == AppTheme.Dark)
            {
                SetBrushColor("PageBackgroundBrush", "#171922");
                SetBrushColor("SurfaceBrush", "#252833");
                SetBrushColor("DividerBrush", "#3D4151");
                SetBrushColor("SurfaceHoverBrush", "#343849");
                SetBrushColor("SurfacePressedBrush", "#403E57");
                SetBrushColor("FocusBrush", "#B9B5FF");
                SetBrushColor("PrimaryTextBrush", "#F0F0F6");
                SetBrushColor("SecondaryTextBrush", "#B1B5C4");
                SetBrushColor("AccentBrush", "#B9B5FF");
                SetBrushColor("StatusNeutralBrush", "Transparent");
                SetBrushColor("StatusNeutralTextBrush", "#B1B5C4");
                SetBrushColor("StatusSuccessBrush", "Transparent");
                SetBrushColor("StatusSuccessTextBrush", "#83D5A8");
                SetBrushColor("StatusInfoBrush", "Transparent");
                SetBrushColor("StatusInfoTextBrush", "#B9B5FF");
                SetBrushColor("StatusErrorBrush", "Transparent");
                SetBrushColor("StatusErrorTextBrush", "#F1A3A9");
                return;
            }

            SetBrushColor("PageBackgroundBrush", "#F7F7F3");
            SetBrushColor("SurfaceBrush", "#FFFFFF");
            SetBrushColor("DividerBrush", "#E3E3EA");
            SetBrushColor("SurfaceHoverBrush", "#F0F0F8");
            SetBrushColor("SurfacePressedBrush", "#E8E7F5");
            SetBrushColor("FocusBrush", "#625FC6");
            SetBrushColor("PrimaryTextBrush", "#252735");
            SetBrushColor("SecondaryTextBrush", "#636978");
            SetBrushColor("AccentBrush", "#625FC6");
            SetBrushColor("StatusNeutralBrush", "Transparent");
            SetBrushColor("StatusNeutralTextBrush", "#636978");
            SetBrushColor("StatusSuccessBrush", "Transparent");
            SetBrushColor("StatusSuccessTextBrush", "#2F7D5A");
            SetBrushColor("StatusInfoBrush", "Transparent");
            SetBrushColor("StatusInfoTextBrush", "#514CB6");
            SetBrushColor("StatusErrorBrush", "Transparent");
            SetBrushColor("StatusErrorTextBrush", "#B43E45");
        }

        private void SetBrushColor(string resourceKey, string colorHex)
        {
            if (Resources.Contains(resourceKey))
            {
                Resources[resourceKey] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(colorHex));
            }
        }


        private enum StatusTone
        {
            Neutral,
            Info,
            Success,
            Error
        }

    }
}
