using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Point = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace BetterScreenShot
{
    public partial class ScreenshotViewerWindow : Window
    {
        private const double MinZoom = 1.0;
        private const double MaxZoom = 5.0;
        private const double ZoomStep = 0.2;

        private readonly TemporaryScreenshot screenshot;
        private readonly Action<BitmapSource>? setClipboardImage;
        private bool isDragging;
        private bool isClosing;
        private Point lastDragPoint;
        private double currentZoom = 1.0;
        private AppTheme? currentTheme;

        internal ScreenshotViewerWindow(TemporaryScreenshot screenshot, BitmapSource image, Action<BitmapSource>? setClipboardImage = null)
        {
            InitializeComponent();
            this.screenshot = screenshot;
            this.setClipboardImage = setClipboardImage;
            if (screenshot.IsImported)
            {
                Title = "Image Editor — " + System.IO.Path.GetFileName(screenshot.OriginalPath);
                DiscardMenuItem.Header = "Close image…";
            }
            ViewerImage.Source = image;
            InitializeEditor(image);
            InitializeUnsavedState();
            ApplyThemeFromSystem();
            UpdateZoomState();
            Closed += ScreenshotViewerWindow_Closed;
            SourceInitialized += ScreenshotViewerWindow_SourceInitialized;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed += ThemePreference_Changed;
            EditorSettingsService.Changed += EditorSettings_Changed;
        }

        private void EditorSettings_Changed() => Dispatcher.BeginInvoke(new Action(RefreshToolShortcutLabels));

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow main) main.ShowSettings(this);
        }

        private void ScreenshotViewerWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ThemeIconService.ApplyTitleBarTheme(this, currentTheme ?? AppTheme.Light);
        }

        private void Editor_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = ImportedImageService.TryGetDroppedImage(e.Data, out _) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Editor_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (!ImportedImageService.TryGetDroppedImage(e.Data, out var path)) return;
            try { ImportedImageService.OpenEditor(path!); }
            catch (Exception error)
            {
                AppDialog.ShowError(this, "Could not open image", "Choose a PNG, JPEG, BMP, GIF, or TIFF image and try again.", error.Message);
            }
        }

        private void ApplyThemeFromSystem()
        {
            var theme = ThemePreferenceService.CurrentTheme;
            ThemeMenuItem.Header = theme == AppTheme.Dark ? "Switch to light theme" : "Switch to dark theme";
            ThemeMenuIcon.Data = ThemeIconService.ThemeGlyph(theme);
            FollowSystemThemeMenuItem.Visibility = ThemePreferenceService.FollowsSystem ? Visibility.Collapsed : Visibility.Visible;
            currentTheme = theme;
            ThemeIconService.ApplyViewerTheme(Resources, theme);
            Icon = ThemeIconService.CreateScanIcon(theme);
            ThemeIconService.ApplyTitleBarTheme(this, theme);
        }


        private void UpdateZoomState()
        {
            ImageScaleTransform.ScaleX = currentZoom;
            ImageScaleTransform.ScaleY = currentZoom;

            if (currentZoom <= MinZoom)
            {
                ImageTranslateTransform.X = 0;
                ImageTranslateTransform.Y = 0;
            }

            UpdateDisplayScale();
        }

        private void ChangeZoom(double delta)
        {
            currentZoom = Math.Clamp(Math.Round(currentZoom + delta, 2), MinZoom, MaxZoom);
            UpdateZoomState();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeZoom(-ZoomStep);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeZoom(ZoomStep);
        }

        private void ImageViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ChangeZoom(e.Delta > 0 ? ZoomStep : -ZoomStep);
            e.Handled = true;
        }

        private void ImageViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Annotations.Tool != AnnotationTool.Hand && !Keyboard.IsKeyDown(Key.Space))
            {
                return;
            }

            isDragging = true;
            lastDragPoint = e.GetPosition(ImageViewport);
            ImageViewport.CaptureMouse();
            ImageViewport.Cursor = WpfCursors.Hand;
            e.Handled = true;
        }

        private void ImageViewport_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }

            var currentPoint = e.GetPosition(ImageViewport);
            var delta = currentPoint - lastDragPoint;
            lastDragPoint = currentPoint;

            ImageTranslateTransform.X += delta.X;
            ImageTranslateTransform.Y += delta.Y;
            e.Handled = true;
        }

        private void ImageViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            ImageViewport.ReleaseMouseCapture();
            ImageViewport.Cursor = WpfCursors.Arrow;
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

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try { ThemePreferenceService.Toggle(); }
            catch (Exception error) { AppDialog.ShowError(this, "Could not change theme", "Try changing the theme again.", error.Message); }
        }

        private void FollowSystemThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try { ThemePreferenceService.Set(null); }
            catch (Exception error) { AppDialog.ShowError(this, "Could not change theme", "Try changing the theme again.", error.Message); }
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try { await CopyImageAsync(); }
            catch (Exception ex)
            {
                if (!IsLoaded) return;
                AppDialog.ShowError(this, "Could not copy screenshot", "Try copying the screenshot again.", ex.Message);
            }
        }

        internal async Task CopyImageAsync()
        {
            if (!CopyButton.IsEnabled) return;
            ActionButtonFeedback.Reset(CopyButton);
            var originalContent = CopyButton.Content;
            CopyButton.IsEnabled = false;
            CopyButton.Content = "…";
            try
            {
                CommitText();
                Annotations.CancelGesture();
                var stateBeingCopied = CaptureDocumentState();
                var source = (BitmapSource)ViewerImage.Source;
                var output = Annotations.Document.Items.Count > 0 ? await Annotations.Document.RenderAsync(source) : source;
                var clipboardData = setClipboardImage is null ? await ImageClipboardService.CreateDataObjectAsync(output) : null;
                for (var attempt = 0; ; attempt++)
                {
                    if (isClosing || !IsLoaded) return;
                    try
                    {
                        if (clipboardData is not null) System.Windows.Clipboard.SetDataObject(clipboardData, copy: true);
                        else setClipboardImage!(output);
                        retainedState = stateBeingCopied;
                        RefreshUnsavedState();
                        SetEditorStatus("Copied image");
                        ActionButtonFeedback.Show(CopyButton, "Copied", iconOnly: true);
                        break;
                    }
                    catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x800401D0) && attempt < 4)
                    {
                        // Clipboard managers and the destination app can briefly hold the clipboard.
                        await Task.Delay(75);
                    }
                }
            }
            finally
            {
                CopyButton.IsEnabled = true;
                CopyButton.Content = originalContent;
            }
        }

        private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
            => await SaveCopyAsync();

        internal async Task<bool> SaveCopyAsync()
        {
            if (!SaveButton.IsEnabled) return false;
            ActionButtonFeedback.Reset(SaveButton);
            var originalContent = SaveButton.Content;
            CommitText();
            Annotations.CancelGesture();
            try
            {
                var savedPath = SelectSaveCopyPath is null ? ScreenshotFileService.ChooseSavePath(screenshot.FilePath, this, screenshot.OriginalPath) : SelectSaveCopyPath();
                if (savedPath is null)
                {
                    return false;
                }

                SaveButton.IsEnabled = false;
                SaveButton.Content = "…";
                var stateBeingSaved = CaptureDocumentState();
                var modified = Annotations.Document.Items.Count > 0;
                var output = modified ? await Annotations.Document.RenderAsync((BitmapSource)ViewerImage.Source) : (BitmapSource)ViewerImage.Source;
                // Encoding the frozen image also lets an export finish if the editor closes.
                await Task.Run(() => ScreenshotFileService.Export(screenshot.FilePath, savedPath, output, imageModified: true));
                if (!IsLoaded) return false;
                retainedState = stateBeingSaved;
                RefreshUnsavedState();
                ActionButtonFeedback.Show(SaveButton, iconOnly: true);
                return true;
            }
            catch (Exception ex)
            {
                if (!IsLoaded) return false;
                AppDialog.ShowError(this, "Could not save screenshot", "Choose another location and try again.", ex.Message);
                return false;
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = originalContent;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDialog.ConfirmDelete(this, screenshot.IsImported))
            {
                return;
            }

            try
            {
                ScreenshotFileService.DeleteIfExists(screenshot.FilePath);
                DiscardAndClose();
            }
            catch (Exception ex)
            {
                AppDialog.ShowError(this, screenshot.IsImported ? "Could not close image" : "Could not delete screenshot",
                    screenshot.IsImported ? "Try closing the image again." : "The screenshot is still open. Try deleting it again.", ex.Message);
            }
        }

        private void ScreenshotViewerWindow_Closed(object? sender, EventArgs e)
        {
            EndDrag();
            Annotations.CancelGesture();
            screenshot.Dispose();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed -= ThemePreference_Changed;
            EditorSettingsService.Changed -= EditorSettings_Changed;
            SourceInitialized -= ScreenshotViewerWindow_SourceInitialized;
            Closed -= ScreenshotViewerWindow_Closed;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            CommitText();
            if (!allowDiscardClose && IsLoaded && HasUnsavedChanges)
            {
                var choice = savingToClose ? UnsavedChoice.KeepEditing : ConfirmUnsavedChanges?.Invoke() ?? AppDialog.ConfirmUnsaved(this);
                if (choice != UnsavedChoice.Discard)
                {
                    e.Cancel = true;
                    base.OnClosing(e);
                    if (choice == UnsavedChoice.Save) _ = SaveAndCloseAsync();
                    return;
                }
            }
            isClosing = true;
            base.OnClosing(e);
            if (e.Cancel) isClosing = false;
            else
            {
                CancelTextExtraction();
                CancelOcrSelectionDrag();
            }
        }

    }
}
