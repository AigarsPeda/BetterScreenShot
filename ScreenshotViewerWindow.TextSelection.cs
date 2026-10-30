using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private int firstSelectedOcrWord = -1;
    private int lastSelectedOcrWord = -1;
    private int ocrDragAnchor = -1;
    private int ocrSelectionBeforeDrag;
    private int ocrSelectionLengthBeforeDrag;
    private Vector ocrDragOffset;
    private Thumb? activeOcrHandle;

    private void UpdateOcrSelectionHandles()
    {
        if (OcrSelectionStartHandle is null) return;
        var visible = currentTextResult is not null && firstSelectedOcrWord >= 0 && lastSelectedOcrWord >= firstSelectedOcrWord;
        OcrSelectionStartHandle.Visibility = OcrSelectionEndHandle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var first = currentTextResult!.Words[firstSelectedOcrWord].Bounds;
        var last = currentTextResult.Words[lastSelectedOcrWord].Bounds;
        var scale = Math.Max(.01, Annotations.DisplayScale);
        foreach (var handle in new[] { OcrSelectionStartHandle, OcrSelectionEndHandle })
            handle.RenderTransform = new ScaleTransform(1 / scale, 1 / scale);
        OcrSelectionStartHandle.Height = first.Height * scale + 14;
        OcrSelectionEndHandle.Height = last.Height * scale + 14;
        Canvas.SetLeft(OcrSelectionStartHandle, first.Left - 10 / scale);
        Canvas.SetTop(OcrSelectionStartHandle, first.Top - 12 / scale);
        Canvas.SetLeft(OcrSelectionEndHandle, last.Right - 10 / scale);
        Canvas.SetTop(OcrSelectionEndHandle, last.Top - 2 / scale);
    }

    private void OcrSelectionHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        var handle = (Thumb)sender;
        if (BeginOcrSelectionDrag(ReferenceEquals(handle, OcrSelectionStartHandle), Mouse.GetPosition(ImageStage)))
            activeOcrHandle = handle;
        else handle.CancelDrag();
        e.Handled = true;
    }

    private void OcrSelectionHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        DragOcrSelection(Mouse.GetPosition(ImageStage));
        e.Handled = true;
    }

    private void OcrSelectionHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndOcrSelectionDrag(e.Canceled);
        e.Handled = true;
    }

    internal bool BeginOcrSelectionDrag(bool startHandle, Point pointer)
    {
        if (currentTextResult is null || firstSelectedOcrWord < 0 || lastSelectedOcrWord < firstSelectedOcrWord) return false;
        Annotations.CancelGesture();
        ocrSelectionBeforeDrag = ExtractedTextBox.SelectionStart;
        ocrSelectionLengthBeforeDrag = ExtractedTextBox.SelectionLength;
        ocrDragAnchor = startHandle ? lastSelectedOcrWord : firstSelectedOcrWord;
        var word = currentTextResult.Words[startHandle ? firstSelectedOcrWord : lastSelectedOcrWord].Bounds;
        ocrDragOffset = pointer - new Point(startHandle ? word.Left : word.Right, word.Top + word.Height / 2);
        return true;
    }

    internal void DragOcrSelection(Point pointer)
    {
        if (ocrDragAnchor < 0 || currentTextResult is null) return;
        var point = pointer - ocrDragOffset;
        var closest = -1;
        var distance = double.PositiveInfinity;
        for (var index = 0; index < currentTextResult.Words.Count; index++)
        {
            var bounds = currentTextResult.Words[index].Bounds;
            var x = Math.Max(0, Math.Max(bounds.Left - point.X, point.X - bounds.Right));
            var y = Math.Max(0, Math.Max(bounds.Top - point.Y, point.Y - bounds.Bottom));
            var candidate = x * x + y * y * 4;
            if (candidate >= distance) continue;
            closest = index;
            distance = candidate;
        }
        if (closest < 0) return;
        var first = currentTextResult.Words[Math.Min(ocrDragAnchor, closest)];
        var last = currentTextResult.Words[Math.Max(ocrDragAnchor, closest)];
        ExtractedTextBox.Select(first.TextStart, last.TextStart + last.Text.Length - first.TextStart);
        var line = ExtractedTextBox.GetLineIndexFromCharacterIndex(currentTextResult.Words[closest].TextStart);
        if (line >= 0) ExtractedTextBox.ScrollToLine(line);
    }

    internal void EndOcrSelectionDrag(bool canceled = false)
    {
        if (ocrDragAnchor < 0) return;
        ocrDragAnchor = -1;
        activeOcrHandle = null;
        if (canceled) ExtractedTextBox.Select(ocrSelectionBeforeDrag, ocrSelectionLengthBeforeDrag);
        if (!isClosing)
        {
            ExtractedTextBox.Focus();
            RevealSelectedText();
        }
    }

    private void CancelOcrSelectionDrag()
    {
        activeOcrHandle?.CancelDrag();
        EndOcrSelectionDrag(canceled: true);
    }
}
