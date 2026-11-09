using System.Windows;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    // Opening content or the last image successfully retained by save/copy.
    private DocumentState retainedState = new([], false);
    private bool documentHasUnsavedChanges;
    private bool allowDiscardClose;
    private bool savingToClose;
    internal Func<UnsavedChoice>? ConfirmUnsavedChanges { get; set; }
    internal Func<string?>? SelectSaveCopyPath { get; set; }
    internal bool HasUnsavedChanges => documentHasUnsavedChanges ||
        InlineText.IsVisible && !string.IsNullOrWhiteSpace(InlineText.Text) &&
        (editingTextIndex < 0 || InlineText.Text != Annotations.Document.Items[editingTextIndex].Text);

    private void InitializeUnsavedState()
    {
        retainedState = CaptureDocumentState();
        Annotations.Document.Changed += DocumentEdited;
        UpdateDocumentTitle();
    }

    private void DocumentEdited(AnnotationChange change, int index)
    {
        RefreshUnsavedState();
    }

    private DocumentState CaptureDocumentState() => new(
        Annotations.Document.Items.Select(item => item.Clone()).ToArray(), Annotations.Document.SketchEnabled);

    private void RefreshUnsavedState()
    {
        documentHasUnsavedChanges = !retainedState.Matches(Annotations.Document);
        UpdateDocumentTitle();
        SetEditorStatus(HasUnsavedChanges ? "Unsaved changes" : "");
    }

    private sealed record DocumentState(ImageAnnotation[] Items, bool SketchEnabled)
    {
        private static bool UsesSketch(AnnotationTool tool) => tool is AnnotationTool.Line or AnnotationTool.Arrow
            or AnnotationTool.NumberedArrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse;

        internal bool Matches(AnnotationDocument document)
        {
            if (Items.Length != document.Items.Count) return false;
            if (SketchEnabled != document.SketchEnabled && Items.Any(item => UsesSketch(item.Tool))) return false;
            for (var i = 0; i < Items.Length; i++)
            {
                var before = Items[i]; var after = document.Items[i];
                if (before.Tool != after.Tool || before.Color != after.Color || !before.Points.SequenceEqual(after.Points)) return false;
                if (before.Tool == AnnotationTool.Text)
                {
                    if (before.Text != after.Text || before.FontSize != after.FontSize) return false;
                }
                else if (before.Width != after.Width || before.Tool == AnnotationTool.NumberedArrow && before.Number != after.Number) return false;
                if (before.Tool is AnnotationTool.Arrow or AnnotationTool.NumberedArrow && before.ArrowMetricScale != after.ArrowMetricScale) return false;
                if (SketchEnabled && UsesSketch(before.Tool) && before.SketchSeed != after.SketchSeed) return false;
            }
            return true;
        }
    }

    private void UpdateDocumentTitle()
    {
        Title = "Screenshot editor" + (screenshot.IsImported ? " · " + System.IO.Path.GetFileName(screenshot.OriginalPath) : "")
            + (HasUnsavedChanges ? " · Unsaved changes" : "");
    }

    internal void DiscardAndClose()
    {
        allowDiscardClose = true;
        Close();
    }

    private async Task SaveAndCloseAsync()
    {
        savingToClose = true;
        try
        {
            if (await SaveCopyAsync() && !HasUnsavedChanges) Close();
        }
        finally { savingToClose = false; }
    }
}
