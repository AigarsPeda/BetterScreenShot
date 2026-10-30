using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Point = System.Windows.Point;

internal static class TextSelectionTests
{
    internal static void Run(Action<string, Action> check, string directory)
    {
        check("Image selection handles extend and shrink a sentence across two lines", () =>
        {
            using var session = new Session(directory);
            session.SelectWord(1);
            Require(session.Start.IsVisible && session.End.IsVisible, "A selected word should show both handles.");
            Require(session.Window.BeginOcrSelectionDrag(false, session.Edge(1, false)), "End handle should start dragging.");
            session.Window.DragOcrSelection(session.Center(5));
            session.Window.EndOcrSelectionDrag();
            Require(session.Box.SelectedText == "selected line" + Environment.NewLine + "Second selected line", session.Box.SelectedText);
            Require(((GeometryGroup)session.Highlight.Data).Children.Count == 5, "Both lines should be highlighted.");

            session.Window.BeginOcrSelectionDrag(true, session.Edge(1, true));
            session.Window.DragOcrSelection(session.Center(0));
            session.Window.EndOcrSelectionDrag();
            Require(session.Box.SelectedText == session.Result.Text, "Dragging the start backward should include the first word.");
            session.Window.BeginOcrSelectionDrag(false, session.Edge(5, false));
            session.Window.DragOcrSelection(session.Center(2));
            session.Window.EndOcrSelectionDrag();
            Require(session.Box.SelectedText == "First selected line", "Dragging back should shrink to the first line.");
            Require(session.Box.IsKeyboardFocused && ApplicationCommands.Copy.CanExecute(null, session.Box), "Ctrl+C should still copy the selected sentence.");
        });
        check("Selection handles can cross, snap through whitespace and cancel with Escape", () =>
        {
            using var session = new Session(directory);
            session.SelectWord(1);
            session.Window.BeginOcrSelectionDrag(true, session.Edge(1, true));
            session.Window.DragOcrSelection(session.Center(5));
            Require(session.Box.SelectedText.EndsWith("Second selected line", StringComparison.Ordinal), "Crossing the other handle should extend in the new direction.");
            session.Window.DragOcrSelection(session.Center(0));
            Require(session.Box.SelectedText == "First selected", "Dragging back over the anchor should reverse the selection.");
            var escape = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(session.Window), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            session.Window.RaiseEvent(escape);
            Require(escape.Handled && session.Box.SelectedText == "selected", "Escape should restore the range from before the drag.");
            session.Window.BeginOcrSelectionDrag(false, session.Edge(1, false));
            var target = session.Result.Words[2].Bounds;
            session.Window.DragOcrSelection(new Point(target.Right + 5, target.Top + target.Height / 2));
            session.Window.EndOcrSelectionDrag();
            Require(session.Box.SelectedText == "selected line", "Whitespace after a line should snap to its nearest word.");
        });
        check("Selection handles keep their display size through zoom and pan and do not enter image copies", () =>
        {
            BitmapSource? copied = null;
            using var session = new Session(directory, image => copied = image);
            session.SelectWord(1);
            var zoom = typeof(ScreenshotViewerWindow).GetMethod("ChangeZoom", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var previousZoom = 1d;
            foreach (var width in new[] { 620d, 1100d })
            foreach (var scale in new[] { 1d, 2d, 3d })
            {
                session.Window.Width = width;
                zoom.Invoke(session.Window, [scale - previousZoom]);
                previousZoom = scale;
                var pan = (TranslateTransform)session.Window.FindName("ImageTranslateTransform");
                pan.X = 25;
                pan.Y = -10;
                session.Window.UpdateLayout();
                var viewport = (FrameworkElement)session.Window.FindName("ImageViewport");
                foreach (var handle in new[] { session.Start, session.End })
                {
                    var bounds = handle.TransformToAncestor(viewport).TransformBounds(new Rect(handle.RenderSize));
                    Require(Math.Abs(bounds.Width - 20) < .1, $"Handle width changed to {bounds.Width} at zoom {scale}.");
                }
            }
            TextExtractionTests.Complete(session.Window.CopyImageAsync());
            Require(ReferenceEquals(copied, session.Image), "Copying the image should exclude selection highlights and handles.");
            Require(((AnnotationSurface)session.Window.FindName("Annotations")).Document.Items.Count == 0, "Selection dragging must not add annotations.");
        });
        check("Closing the text panel during a handle drag clears capture state and both handles", () =>
        {
            using var session = new Session(directory);
            session.SelectWord(1);
            session.Window.BeginOcrSelectionDrag(false, session.Edge(1, false));
            session.Window.DragOcrSelection(session.Center(5));
            TextExtractionTests.Complete(session.Window.ToggleTextPanelAsync());
            Require(session.Box.SelectionLength == 0 && session.Start.Visibility == Visibility.Collapsed && session.End.Visibility == Visibility.Collapsed,
                "Closing the panel should clear the text range and hide both handles.");
            session.Window.DragOcrSelection(session.Center(2));
            Require(session.Box.SelectionLength == 0, "Late drag events after closing must be ignored.");
            TextExtractionTests.Complete(session.Window.ToggleTextPanelAsync());
            session.SelectWord(3);
            Require(session.Box.SelectedText == "Second" && session.Start.Visibility == Visibility.Visible, "Selecting a new word after reopening should restore the handles.");
        });
    }

    private sealed class Session : IDisposable
    {
        private readonly SynchronizationContext? previousContext = SynchronizationContext.Current;
        internal BitmapSource Image { get; } = TextExtractionTests.Fixture("First selected line\nSecond selected line");
        internal TextExtractionResult Result { get; }
        internal ScreenshotViewerWindow Window { get; }
        internal System.Windows.Controls.TextBox Box { get; }
        internal System.Windows.Shapes.Path Highlight { get; }
        internal Thumb Start { get; }
        internal Thumb End { get; }

        internal Session(string directory, Action<BitmapSource>? copy = null)
        {
            Result = TextExtractionService.RecognizeAsync(Image, "eng").GetAwaiter().GetResult();
            Require(Result.Words.Count == 6, Result.Text);
            Window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "handles-" + Guid.NewGuid().ToString("N") + ".png")), Image, copy);
            var picker = (System.Windows.Controls.ComboBox)Window.FindName("OcrLanguagePicker");
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "eng");
            Window.Show();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
            Box = (System.Windows.Controls.TextBox)Window.FindName("ExtractedTextBox");
            Highlight = (System.Windows.Shapes.Path)Window.FindName("OcrSelectionHighlight");
            Start = (Thumb)Window.FindName("OcrSelectionStartHandle");
            End = (Thumb)Window.FindName("OcrSelectionEndHandle");
        }

        internal Point Center(int index) => new(Result.Words[index].Bounds.Left + Result.Words[index].Bounds.Width / 2,
            Result.Words[index].Bounds.Top + Result.Words[index].Bounds.Height / 2);
        internal Point Edge(int index, bool start) => new(start ? Result.Words[index].Bounds.Left : Result.Words[index].Bounds.Right, Center(index).Y);
        internal void SelectWord(int index)
        {
            TextExtractionTests.Complete(Window.SelectImageWordAsync(Center(index)));
            TextExtractionTests.Complete(Task.Delay(250));
            Window.UpdateLayout();
        }
        public void Dispose()
        {
            Window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
