using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Point = System.Windows.Point;

internal static class UxReviewTests
{
    internal static void Run(Action<string, Action> check, string directory)
    {
        Directory.CreateDirectory(directory);
        var image = AnnotationTests.CreateDemoImage();
        ScreenshotViewerWindow Editor(string name)
        {
            var path = Path.Combine(directory, name + ".png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
            return new ScreenshotViewerWindow(new TemporaryScreenshot(path), image, _ => { });
        }

        check("Selecting an annotation with Pan active keeps the image in place at fit", () =>
        {
            var window = Editor("pan-at-fit");
            try
            {
                Choose(window, AnnotationTool.Hand);
                window.Show();
                var surface = (AnnotationSurface)window.FindName("Annotations");
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Line, Points = [new(50, 50), new(150, 150)] });
                var stage = (FrameworkElement)window.FindName("ImageStage");
                var options = (FrameworkElement)window.FindName("AnnotationOptionsTray");
                Rect ImageBounds() => stage.TransformToAncestor(window).TransformBounds(new Rect(stage.RenderSize));
                var before = ImageBounds();
                surface.Select(0);
                window.UpdateLayout();
                Require(options.IsVisible, "Selecting a mark in Pan shows its properties");
                Require(ImageBounds() == before, "Selecting the mark must not shrink or move the image before the drag");
                surface.Select(-1);
                window.UpdateLayout();
                Require(!options.IsVisible && ImageBounds() == before, "Returning to the canvas keeps the same view");
            }
            finally { window.DiscardAndClose(); }
        });

        check("Properties clear the fitted image and float over zoom, with responsive tools and OCR", () =>
        {
            var window = Editor("layout");
            try
            {
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                foreach (var width in new[] { 1100d, 780d, 750d, 620d })
                {
                    window.Width = width; window.Height = width == 620 ? 450 : 680;
                    ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                    Choose(window, AnnotationTool.Arrow);
                    var opening = !window.IsVisible;
                    if (opening) window.Show();
                    window.UpdateLayout();
                    var options = (FrameworkElement)window.FindName("AnnotationOptionsTray");
                    var dock = (FrameworkElement)window.FindName("ToolDock");
                    var copy = (FrameworkElement)window.FindName("CopyButton");
                    var viewport = (FrameworkElement)window.FindName("ImageViewport");
                    var compact = ((FrameworkElement)window.Content).ActualWidth - 24 < 760;
                    Require(options.IsVisible != compact, "Contextual visibility");
                    if (width >= 780) Require(Math.Abs(dock.TranslatePoint(new Point(0, dock.ActualHeight / 2), window).Y - copy.TranslatePoint(new Point(0, copy.ActualHeight / 2), window).Y) < 8, "One-row toolbar");
                    Require(viewport.ActualHeight > 150, "Usable image viewport");
                    if (compact) Click(window, "PropertiesButton");
                    if (!opening) Click(window, "FitButton");
                    window.UpdateLayout();
                    var bounds = WindowBounds(options);
                    Require(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom < window.ActualHeight - 40, "Floating card bounds");
                    var canvas = (FrameworkElement)window.Content;
                    Require(Math.Abs(options.ActualHeight + options.Margin.Top + options.Margin.Bottom - canvas.ActualHeight) < 2, "Properties fill the available height");
                    var stage = (FrameworkElement)window.FindName("ImageStage");
                    Rect WindowBounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
                    var imageBounds = WindowBounds(stage);
                    Require(imageBounds.Left > bounds.Right, "The fitted image clears the properties panel");
                    Require(imageBounds.Right <= WindowBounds(viewport).Right + 1, "The fitted image stays inside the viewport");
                    Require(viewport.RenderSize == canvas.RenderSize, "The image can use the full canvas while properties are visible");
                    Click(window, "PropertiesButton"); window.UpdateLayout();
                    Require(WindowBounds(stage) == imageBounds, "Hiding properties leaves the fitted image in place");
                    Click(window, "FitButton"); window.UpdateLayout();
                    var fullImageBounds = WindowBounds(stage);
                    Require(fullImageBounds.Width > imageBounds.Width, "Fit uses the available space with properties closed");
                    Click(window, "PropertiesButton"); window.UpdateLayout();
                    Require(WindowBounds(stage) == fullImageBounds, "Opening properties leaves the fitted image in place");
                    Click(window, "FitButton"); window.UpdateLayout();
                    Require(WindowBounds(stage) == imageBounds, "Fit clears the open panel");
                    Render(window, Path.Combine(directory, $"editor-{theme}-{width}.png"));
                    Click(window, "ZoomInButton"); window.UpdateLayout();
                    Require(Math.Abs(WindowBounds(stage).Width - imageBounds.Width * 1.2) < 2, "The first zoom step scales the fitted image without jumping");
                    for (var step = 0; step < 6; step++) Click(window, "ZoomInButton");
                    window.UpdateLayout();
                    var zoomedBounds = WindowBounds(stage);
                    Require(zoomedBounds.Left < WindowBounds(viewport).Left, "Zoom reaches the left edge of the canvas");
                    if (width == 1100) Render(window, Path.Combine(directory, $"editor-{theme}-zoomed.png"));
                    Click(window, "PropertiesButton"); window.UpdateLayout();
                    Require(WindowBounds(stage) == zoomedBounds, "Closing properties leaves the zoomed image in place");
                    Click(window, "PropertiesButton"); window.UpdateLayout();
                    Require(WindowBounds(stage) == zoomedBounds, "Opening properties leaves the zoomed image in place");
                    Click(window, "FitButton"); window.UpdateLayout();
                    Require(WindowBounds(stage) == imageBounds, "Fit restores the image beside properties");
                    window.SetTextPanelOpen(true);
                    Pump(Task.Delay(250), window);
                    window.UpdateLayout();
                    var panel = (FrameworkElement)window.FindName("ExtractedTextPanel");
                    Require(panel.ActualWidth >= 280 && panel.ActualHeight > 150, "Text workspace remains usable");
                    if (compact) Require(!((FrameworkElement)window.FindName("CanvasHost")).IsVisible, "Compact OCR replaces canvas");
                    window.SetTextPanelOpen(false);
                    Pump(Task.Delay(250), window);
                    Choose(window, AnnotationTool.Select);
                    Require(!options.IsVisible, "Select hides properties");
                }
            }
            finally { window.DiscardAndClose(); }
        });
        check("Unsaved close keeps work, cancelled save stays open, and discard closes", () =>
        {
            var window = Editor("close");
            try
            {
                window.Show();
                ((AnnotationSurface)window.FindName("Annotations")).Document.Add(new ImageAnnotation { Tool = AnnotationTool.Line, Points = [new(5, 5), new(50, 50)] });
                window.ConfirmUnsavedChanges = () => UnsavedChoice.KeepEditing;
                window.Close(); Require(window.IsLoaded && window.HasUnsavedChanges, "Keep editing");
                window.SelectSaveCopyPath = () => null;
                window.ConfirmUnsavedChanges = () => UnsavedChoice.Save;
                window.Close(); Require(window.IsLoaded && window.HasUnsavedChanges, "Cancelled save");
                window.ConfirmUnsavedChanges = () => UnsavedChoice.Discard;
                window.Close(); Require(!System.Windows.Application.Current.Windows.OfType<ScreenshotViewerWindow>().Contains(window), "Confirmed discard");
            }
            finally { window.DiscardAndClose(); }
        });
        check("Untouched captures, preferences, and undone edits close without prompting", () =>
        {
            foreach (var mode in new[] { "untouched", "undo", "delete" })
            {
                var window = Editor("clean-" + mode);
                var prompts = 0;
                try
                {
                    window.Show();
                    window.ConfirmUnsavedChanges = () => { prompts++; return UnsavedChoice.KeepEditing; };
                    var document = ((AnnotationSurface)window.FindName("Annotations")).Document;
                    Require(!window.HasUnsavedChanges, "Fresh capture is unedited");
                    Choose(window, AnnotationTool.Rectangle);
                    document.SketchEnabled = !document.SketchEnabled;
                    Require(!window.HasUnsavedChanges, "Tool defaults do not edit an empty image");
                    if (mode != "untouched")
                    {
                        document.Add(new ImageAnnotation { Tool = AnnotationTool.Line, Points = [new(5, 5), new(50, 50)] });
                        Require(window.HasUnsavedChanges, "Drawing marks the image changed");
                        if (mode == "undo") document.Undo(); else document.Remove(0);
                        Require(!window.HasUnsavedChanges, "Returning to original content clears changes");
                    }
                    window.Close();
                    Require(prompts == 0 && !System.Windows.Application.Current.Windows.OfType<ScreenshotViewerWindow>().Contains(window), "Clean image closes directly");
                }
                finally { window.DiscardAndClose(); }
            }
        });
        check("Color popup opens directly, applies shades and hex colors, and preserves undo", () =>
        {
            var window = Editor("color-picker");
            try
            {
                window.Show(); Choose(window, AnnotationTool.Arrow);
                var surface = (AnnotationSurface)window.FindName("Annotations");
                surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Color = Colors.Red, Points = [new(20, 20), new(120, 120)] });
                surface.Select(0);
                Click(window, "ColorPickerButton"); window.UpdateLayout();
                var popup = (Popup)window.FindName("ColorPickerPopup");
                Require(popup.IsOpen, "Picker opens directly");
                var row = (Panel)window.FindName("ColorSwatches");
                Require(ReferenceEquals(row.Children[row.Children.Count - 1], window.FindName("ColorPickerButton")), "More colors is last");
                var hex = (System.Windows.Controls.TextBox)window.FindName("ColorHex");
                hex.Text = "#A855F7"; Click(window, "ApplyHexColorButton");
                Require(surface.Selected!.Color == System.Windows.Media.Color.FromRgb(168, 85, 247), "Hex changes selection");
                hex.Text = "badhex";
                Require(!((Button)window.FindName("ApplyHexColorButton")).IsEnabled && surface.Selected.Color == System.Windows.Media.Color.FromRgb(168, 85, 247), "Invalid hex leaves color intact");
                var shade = ((Panel)window.FindName("ColorShades")).Children.OfType<ToggleButton>().First();
                shade.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(surface.Selected.Color == (System.Windows.Media.Color)shade.Tag, "Shade applies");
                surface.Document.Undo(); Require(surface.Selected.Color == System.Windows.Media.Color.FromRgb(168, 85, 247), "Color changes undo");
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                    var card = (FrameworkElement)window.FindName("ColorPickerSurface");
                    card.UpdateLayout();
                    RenderElement(card, Path.Combine(directory, $"color-picker-{theme}.png"));
                }
                popup.IsOpen = false;
            }
            finally { window.DiscardAndClose(); }
        });
        check("Tooltips use the app theme inside their popup", () =>
        {
            var window = Editor("tooltip");
            var tooltip = new System.Windows.Controls.ToolTip { Content = "Copy image (Ctrl+C)" };
            try
            {
                window.Show();
                var button = (Button)window.FindName("CopyButton");
                button.ToolTip = tooltip;
                tooltip.PlacementTarget = button;
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                    tooltip.IsOpen = true; tooltip.UpdateLayout();
                    Require(tooltip.Template.FindName("ToolTipSurface", tooltip) is Border, "Custom tooltip template");
                    var expected = theme == AppTheme.Dark ? System.Windows.Media.Color.FromRgb(37, 40, 51) : Colors.White;
                    Require(((SolidColorBrush)tooltip.Background).Color == expected, "Popup resolves themed brushes");
                    tooltip.IsOpen = false;
                }
            }
            finally { tooltip.IsOpen = false; window.DiscardAndClose(); }
        });
        check("Close dialog shows all three actions without clipping", () =>
        {
            var window = Editor("dialog");
            try
            {
                window.Show();
                var dialog = AppDialog.CreateUnsavedDialog(window);
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    foreach (var name in new[] { "SaveActionButton", "CancelActionButton", "PrimaryActionButton" })
                    {
                        var button = (FrameworkElement)dialog.FindName(name);
                        var bounds = button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                        Require(button.IsVisible && bounds.Left >= 20 && bounds.Right <= dialog.ActualWidth - 20, name + " fits");
                    }
                    Pump(Task.Delay(300), dialog);
                    Render(dialog, Path.Combine(directory, "close-dialog.png"));
                }
                finally { dialog.Close(); }
            }
            finally { window.DiscardAndClose(); }
        });
        check("Successful copy allows closing, while later edits still require a decision", () =>
        {
            var window = Editor("copy-close");
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                window.Show();
                var document = ((AnnotationSurface)window.FindName("Annotations")).Document;
                document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Points = [new(10, 10), new(100, 100)] });
                Pump(window.CopyImageAsync(), window);
                Require(!window.HasUnsavedChanges, "Successfully copied edits are retained");
                var prompts = 0;
                window.ConfirmUnsavedChanges = () => { prompts++; return UnsavedChoice.KeepEditing; };
                document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Points = [new(20, 20), new(80, 80)] });
                window.Close();
                Require(prompts == 1 && window.IsLoaded, "Later changes prompt");
                document.Undo();
                window.Close();
                Require(prompts == 1 && !System.Windows.Application.Current.Windows.OfType<ScreenshotViewerWindow>().Contains(window), "Returning to copied content closes without another prompt");
            }
            finally { window.DiscardAndClose(); SynchronizationContext.SetSynchronizationContext(previous); }
        });
        check("Copy and save establish retained snapshots without resizing feedback", () =>
        {
            var window = Editor("save");
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                window.Show();
                var document = ((AnnotationSurface)window.FindName("Annotations")).Document;
                document.Add(new ImageAnnotation { Tool = AnnotationTool.Line, Points = [new(5, 5), new(50, 50)] });
                var copyButton = (Button)window.FindName("CopyButton");
                window.UpdateLayout();
                var copyBounds = copyButton.TransformToAncestor(window).TransformBounds(new Rect(copyButton.RenderSize));
                Pump(window.CopyImageAsync(), window);
                Require(!window.HasUnsavedChanges && ((TextBlock)window.FindName("EditorStatus")).Text == "Copied image", "Copy status");
                window.UpdateLayout();
                Require(ActionButtonFeedback.GetIsActive(copyButton) && ActionButtonFeedback.GetMessage(copyButton) == "✓"
                    && System.Windows.Automation.AutomationProperties.GetItemStatus(copyButton) == "Copied", "Copy animates successful feedback with accessible status");
                Require(copyButton.TransformToAncestor(window).TransformBounds(new Rect(copyButton.RenderSize)) == copyBounds, "Copy confirmation preserves layout");
                window.SelectSaveCopyPath = () => Path.Combine(directory, "saved-copy.png");
                var saveButton = (Button)window.FindName("SaveButton");
                window.UpdateLayout();
                var originalBounds = saveButton.TransformToAncestor(window).TransformBounds(new Rect(saveButton.RenderSize));
                var save = window.SaveCopyAsync(); Pump(save, window);
                Require(save.Result && !window.HasUnsavedChanges && File.Exists(Path.Combine(directory, "saved-copy.png")), "Saved copy");
                window.UpdateLayout();
                Require(ActionButtonFeedback.GetIsActive(saveButton), "Successful save shows button confirmation");
                Require(saveButton.TransformToAncestor(window).TransformBounds(new Rect(saveButton.RenderSize)) == originalBounds, "Saved label does not move or resize button");
                var label = (ContentPresenter)saveButton.Template.FindName("ActionContent", saveButton);
                Require(label.Content?.ToString() == "✓" && System.Windows.Automation.AutomationProperties.GetItemStatus(saveButton) == "Saved", "Saved checkmark and accessible status are present");
                Pump(Task.Delay(350), window);
                Render(window, Path.Combine(directory, "save-button-confirmation.png"));
                Pump(Task.Delay(2000), window);
                Require(ActionButtonFeedback.GetIsActive(saveButton), "Confirmation remains still and readable");
                Pump(Task.Delay(1500), window);
                window.UpdateLayout();
                Require(!ActionButtonFeedback.GetIsActive(saveButton) && label.Content is System.Windows.Shapes.Path && ReferenceEquals(label.Content, saveButton.Content), "Save icon restores automatically");
                Require(saveButton.TransformToAncestor(window).TransformBounds(new Rect(saveButton.RenderSize)) == originalBounds, "Restoring label preserves layout");
                window.SelectSaveCopyPath = () => null;
                var cancel = window.SaveCopyAsync(); Pump(cancel, window);
                Require(!cancel.Result && !ActionButtonFeedback.GetIsActive(saveButton), "Cancelled save has no success state");
                var before = document.Items[0].Clone(); var after = before.Clone(); after.Width += 2;
                document.Replace(0, before, after);
                Require(window.HasUnsavedChanges, "Change after saving is dirty");
                document.Undo(); Require(!window.HasUnsavedChanges, "Undo returns to saved content");
                document.Redo(); Require(window.HasUnsavedChanges, "Redo restores changed content");
                window.SelectSaveCopyPath = () => Path.Combine(directory, "saved-copy.png");
                var concurrentSave = window.SaveCopyAsync();
                document.Add(new ImageAnnotation { Tool = AnnotationTool.Rectangle, Points = [new(10, 10), new(80, 80)] });
                Pump(concurrentSave, window);
                Require(concurrentSave.Result && window.HasUnsavedChanges, "Edits during save remain unsaved");
                document.Undo(); Require(!window.HasUnsavedChanges, "Undo reaches the exact exported snapshot");
                ((AnnotationSurface)window.FindName("Annotations")).Document.Add(new ImageAnnotation { Tool = AnnotationTool.Line, Points = [new(5, 5), new(50, 50)], Width = 3, Color = Colors.Red });
                Require(window.HasUnsavedChanges, "New edit marks dirty");
            }
            finally { window.DiscardAndClose(); SynchronizationContext.SetSynchronizationContext(previous); }
        });
        check("Preview and recording save feedback preserves button bounds and original content", () =>
        {
            var previewPath = Path.Combine(directory, "feedback-preview.png");
            var recordingPath = Path.Combine(directory, "feedback-recording.png");
            File.Copy(Path.Combine(directory, "saved-copy.png"), previewPath, true);
            File.Copy(previewPath, recordingPath, true);
            Window[] windows = [new ScreenshotToastWindow(previewPath, new System.Drawing.Rectangle(0, 0, 100, 100), image),
                new RecordingViewerWindow(new TemporaryScreenshot(recordingPath), RecordingFormat.Gif, image)];
            try
            {
                foreach (var window in windows)
                {
                    window.Show(); window.UpdateLayout();
                    var button = (Button)window.FindName("SaveButton");
                    var size = button.RenderSize;
                    var content = button.Content;
                    ActionButtonFeedback.Show(button); window.UpdateLayout();
                    var label = (ContentPresenter)button.Template.FindName("ActionContent", button);
                    Require(label.Content?.ToString() == "✓ Saved" && button.RenderSize == size, "Confirmation fits original button");
                    ActionButtonFeedback.Reset(button); window.UpdateLayout();
                    Require(ReferenceEquals(label.Content, content) && button.RenderSize == size, "Original icon and label restore without resizing");
                }
            }
            finally
            {
                foreach (var window in windows) window.CloseForTest();
                Pump(Task.Delay(250), windows[0]); // Let the preview's closing animation finish.
            }
        });
        check("New captures preserve previous previews and allow navigation", () =>
        {
            var first = Path.Combine(directory, "pending-first.png"); var second = Path.Combine(directory, "pending-second.png");
            File.Copy(Path.Combine(directory, "saved-copy.png"), first, true); File.Copy(first, second, true);
            try
            {
                ScreenshotToastWindow.ShowToast(first, new System.Drawing.Rectangle(0, 0, 100, 100), image);
                ScreenshotToastWindow.ShowToast(second, new System.Drawing.Rectangle(0, 0, 100, 100), image);
                Require(ScreenshotToastWindow.PendingCount == 2 && File.Exists(first) && File.Exists(second), "Both captures retained");
                var toast = System.Windows.Application.Current.Windows.OfType<ScreenshotToastWindow>().Single(w => w.IsVisible);
                typeof(ScreenshotToastWindow).GetMethod("PreviousCapture_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toast, [toast, new RoutedEventArgs()]);
                Require(!toast.IsVisible && System.Windows.Application.Current.Windows.OfType<ScreenshotToastWindow>().Count(w => w.IsVisible) == 1, "Previous capture restored");
            }
            finally { ScreenshotToastWindow.CloseCurrent(); }
        });
        check("Copy countdown preferences persist and older settings keep the ten-second default", () =>
        {
            var path = Path.Combine(directory, "countdown-settings.json");
            var store = new EditorSettingsStore(path);
            store.Save(new EditorSettings { CopyDismissSeconds = 30, Color = "#0A84FF" });
            Require(new EditorSettingsStore(path).Load().CopyDismissSeconds == 30, "Saved delay survives reload");
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            legacy.AsObject().Remove("copyDismissSeconds");
            File.WriteAllText(path, legacy.ToJsonString());
            Require(store.Load().CopyDismissSeconds == 10 && store.Load().Color == "#0A84FF", "Migration preserves existing preferences");
            Require((new EditorSettings { CopyDismissSeconds = 0 }).ValidationError() is not null, "Zero is rejected");
            Require((new EditorSettings { CopyDismissSeconds = 301 }).ValidationError() is not null, "Excessive delays are rejected");
        });
        check("Copy countdown renders in both themes, restarts, and closes the preview", () =>
        {
            var path = Path.Combine(directory, "countdown-preview.png");
            File.Copy(Path.Combine(directory, "saved-copy.png"), path, true);
            ScreenshotToastWindow.ShowToast(path, new System.Drawing.Rectangle(0, 0, 100, 100), image);
            var toast = System.Windows.Application.Current.Windows.OfType<ScreenshotToastWindow>().Single(w => w.IsVisible);
            var start = typeof(ScreenshotToastWindow).GetMethod("StartCopyCountdown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var badge = (Border)toast.FindName("CountdownBadge");
            var label = (TextBlock)toast.FindName("CountdownText");
            var seconds = EditorSettingsService.Current.CopyDismissSeconds;
            try
            {
                Require(badge.Visibility == Visibility.Collapsed, "No countdown before copying");
                start.Invoke(toast, null);
                Require(label.Text == $"Closes in {seconds}s", "Countdown uses the saved preference");
                Pump(Task.Delay(1150), toast);
                Require(label.Text != $"Closes in {seconds}s", "Remaining seconds decrease");
                start.Invoke(toast, null);
                Require(label.Text == $"Closes in {seconds}s", "Copy again restarts the countdown");
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    typeof(ScreenshotToastWindow).GetMethod("ApplyToastTheme", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, [typeof(AppTheme)], null)!.Invoke(toast, [theme]);
                    toast.UpdateLayout();
                    Render(toast, Path.Combine(directory, $"countdown-{theme}.png"));
                }
                var closed = new TaskCompletionSource();
                toast.Closed += (_, _) => closed.TrySetResult();
                Pump(closed.Task.WaitAsync(TimeSpan.FromSeconds(seconds + 3)), toast);
                Require(!File.Exists(path) && ScreenshotToastWindow.PendingCount == 0, "Expiry releases the preview and temporary file");
            }
            finally { ScreenshotToastWindow.CloseCurrent(); }
        });
        check("Capture settings expose and save the preview delay", () =>
        {
            EditorSettings? saved = null;
            var window = new CaptureShortcutsWindow(CaptureShortcutSettings.Disabled,
                (_, settings) => { saved = settings; return "Keep open for this check."; });
            try
            {
                window.Show();
                var picker = (System.Windows.Controls.ComboBox)window.FindName("CopyDismissPicker");
                Require((int)((ComboBoxItem)picker.SelectedItem).Tag == EditorSettingsService.Current.CopyDismissSeconds, "Current preference selected");
                foreach (var width in new[] { 620d, 520d })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    Render(window, Path.Combine(directory, $"countdown-settings-{width}.png"));
                }
                foreach (var seconds in new[] { 5, 10, 15, 30, 60 })
                {
                    picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(item => (int)item.Tag == seconds);
                    Click(window, "SaveShortcutsButton");
                    Require(saved?.CopyDismissSeconds == seconds, "Save callback receives selected delay");
                }
            }
            finally { window.Close(); }
        });
        check("Closing an unsaved capture notification needs no confirmation", () =>
        {
            var path = Path.Combine(directory, "dismiss-notification.png");
            File.Copy(Path.Combine(directory, "saved-copy.png"), path, true);
            var dialogSeen = false;
            var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            guard.Tick += (_, _) =>
            {
                foreach (var dialog in System.Windows.Application.Current.Windows.OfType<AppDialog>().ToArray())
                { dialogSeen = true; dialog.Close(); }
            };
            try
            {
                ScreenshotToastWindow.ShowToast(path, new System.Drawing.Rectangle(0, 0, 100, 100), image);
                var toast = System.Windows.Application.Current.Windows.OfType<ScreenshotToastWindow>().Single(w => w.IsVisible);
                guard.Start();
                var closed = new TaskCompletionSource();
                toast.Closed += (_, _) => closed.TrySetResult();
                Click(toast, "DiscardButton");
                Pump(closed.Task.WaitAsync(TimeSpan.FromSeconds(3)), toast);
                Require(!dialogSeen && ScreenshotToastWindow.PendingCount == 0 && !File.Exists(path), "Notification dismisses and releases its temporary capture directly");
            }
            finally { guard.Stop(); ScreenshotToastWindow.CloseCurrent(); }
        });
    }

    private static void Choose(ScreenshotViewerWindow window, AnnotationTool tool) => typeof(ScreenshotViewerWindow).GetMethod("ChooseTool", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [tool]);
    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Require(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    private static void Pump(Task task, Window window)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static void Render(Window window, string path)
        => RenderElement((FrameworkElement)window.Content, path);

    private static void RenderElement(FrameworkElement content, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right),
            (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
