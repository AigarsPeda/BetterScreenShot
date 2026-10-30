using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Cursors = System.Windows.Input.Cursors;

namespace BetterScreenShot;

public sealed class AnnotationSurface : FrameworkElement
{
    private readonly VisualCollection visuals;
    private readonly DrawingVisual preview = new();
    private readonly DrawingVisual selectionVisual = new();
    private ImageAnnotation? draft;
    private ImageAnnotation? original;
    private Point start;
    private int resizeCorner = -1;
    private int activeLineHandle = -1;
    private double sampleDistance;
    private AnnotationTool tool;
    internal AnnotationDocument Document { get; } = new();
    internal int SelectedIndex { get; private set; } = -1;
    internal ImageAnnotation? Selected => SelectedIndex >= 0 && SelectedIndex < Document.Items.Count ? Document.Items[SelectedIndex] : null;
    internal Color InkColor { get; set; } = Colors.Red;
    internal double InkWidth { get; set; } = 4;
    internal double TextSize { get; set; } = 28;
    internal double DisplayScale { get; set; } = 1;
    internal event Action? SelectionChanged;
    internal event Action<Point>? TextRequested;
    internal event Action? ContextRequested;

    internal AnnotationTool Tool
    {
        get => tool;
        set
        {
            CancelGesture();
            tool = value;
            Select(-1);
            Cursor = value switch { AnnotationTool.Select => Cursors.Arrow, AnnotationTool.Hand => Cursors.Hand, AnnotationTool.Text => Cursors.IBeam, _ => Cursors.Cross };
        }
    }

    public AnnotationSurface()
    {
        visuals = new VisualCollection(this);
        Focusable = true;
        ClipToBounds = true;
        Document.Changed += Rebuild;
        LostMouseCapture += (_, _) => CancelGesture();
    }

    protected override int VisualChildrenCount => visuals.Count;
    protected override Visual GetVisualChild(int index) => visuals[index];
    protected override void OnRender(DrawingContext context) => context.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));

    private void Rebuild()
    {
        visuals.Clear();
        foreach (var item in Document.Items)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) item.Draw(context, Document.SketchEnabled);
            visuals.Add(visual);
        }
        visuals.Add(preview);
        visuals.Add(selectionVisual);
        if (SelectedIndex >= Document.Items.Count) SelectedIndex = -1;
        DrawSelection();
        SelectionChanged?.Invoke();
    }

    internal void Select(int index)
    {
        SelectedIndex = index;
        DrawSelection();
        SelectionChanged?.Invoke();
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, Width), Math.Clamp(point.Y, 0, Height));
    private double HandleSize => 7 / Math.Max(.05, DisplayScale);

    private Point[] Corners(Rect bounds) => [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];

    internal int AnnotationAt(Point point)
    {
        for (var i = Document.Items.Count - 1; i >= 0; i--)
            if (Document.Items[i].HitTest(point, 6 / Math.Max(.05, DisplayScale))) return i;
        return -1;
    }

    internal bool SelectForContextMenu(Point point)
    {
        CancelGesture();
        Focus();
        Select(AnnotationAt(point));
        return Selected is not null;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (SelectForContextMenu(e.GetPosition(this))) ContextRequested?.Invoke();
        e.Handled = true;
    }

    internal int LineHandleAt(Point point)
    {
        if (Selected is not { CanBend: true } selected) return -1;
        Point[] handles = [selected.Points[0], selected.BendPoint, selected.Points[^1]];
        var closest = -1;
        var distance = Math.Pow(HandleSize * 1.5, 2);
        for (var i = 0; i < handles.Length; i++)
        {
            var candidate = (handles[i] - point).LengthSquared;
            if (candidate <= distance) { closest = i; distance = candidate; }
        }
        return closest;
    }

    internal void DrawSelection()
    {
        using var context = selectionVisual.RenderOpen();
        if (Selected is not { } selected) return;
        var pen = new Pen(System.Windows.Media.Brushes.DodgerBlue, 1 / Math.Max(.05, DisplayScale));
        if (selected.CanBend)
        {
            var radius = HandleSize * .65;
            context.DrawEllipse(System.Windows.Media.Brushes.White, pen, selected.Points[0], radius, radius);
            context.DrawEllipse(System.Windows.Media.Brushes.White, pen, selected.Points[^1], radius, radius);
            context.DrawEllipse(System.Windows.Media.Brushes.MediumPurple, pen, selected.BendPoint, radius, radius);
            return;
        }
        var bounds = selected.Bounds;
        bounds.Inflate(HandleSize / 2, HandleSize / 2);
        context.DrawRectangle(null, pen, bounds);
        foreach (var corner in Corners(selected.Bounds))
            context.DrawRoundedRectangle(System.Windows.Media.Brushes.White, pen,
                new Rect(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize), HandleSize / 5, HandleSize / 5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Keyboard.IsKeyDown(Key.Space)) return;
        var point = Clamp(e.GetPosition(this));
        var hit = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? -1 : AnnotationAt(point);
        var lineHandle = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? -1 : LineHandleAt(point);
        var corner = Selected is { CanBend: false } current && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            ? Array.FindIndex(Corners(current.Bounds), p => (p - point).Length < HandleSize * 1.5) : -1;
        if (Tool == AnnotationTool.Hand && hit < 0 && corner < 0 && lineHandle < 0) { Select(-1); return; }
        Focus();
        e.Handled = true;
        start = point;
        activeLineHandle = lineHandle;
        if (Tool == AnnotationTool.Select || hit >= 0 || corner >= 0 || lineHandle >= 0)
        {
            resizeCorner = corner;
            if (resizeCorner < 0 && activeLineHandle < 0)
            {
                Select(hit);
            }
            if (Selected is not { } item) return;
            if (e.ClickCount == 2 && item.Tool == AnnotationTool.Text) { TextRequested?.Invoke(item.Points[0]); return; }
            original = item.Clone();
        }
        else if (Tool == AnnotationTool.Text)
        {
            Select(-1);
            TextRequested?.Invoke(start);
            return;
        }
        else
        {
            Select(-1);
            draft = new ImageAnnotation
            {
                Tool = Tool, Points = [start, start], Color = InkColor,
                Width = Tool == AnnotationTool.Highlight ? InkWidth * 5 : InkWidth
            };
            sampleDistance = 1.5 / Math.Max(.05, DisplayScale);
        }
        CaptureMouse();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured)
        {
            Cursor = Keyboard.IsKeyDown(Key.Space) ? Cursors.Hand
                : LineHandleAt(e.GetPosition(this)) >= 0 ? Cursors.Cross
                : AnnotationAt(e.GetPosition(this)) >= 0 ? Cursors.SizeAll
                : Tool switch { AnnotationTool.Select => Cursors.Arrow, AnnotationTool.Hand => Cursors.Hand, AnnotationTool.Text => Cursors.IBeam, _ => Cursors.Cross };
            return;
        }
        UpdateGesture(Clamp(e.GetPosition(this)));
        e.Handled = true;
    }

    internal void UpdateGesture(Point point)
    {
        if (original is not null && Selected is { } selected)
        {
            var oldBounds = original.Bounds;
            if (activeLineHandle >= 0)
            {
                var delta = point - start;
                selected.Points = [.. original.Points];
                if (activeLineHandle == 1)
                {
                    if (delta.Length > .5 / Math.Max(.05, DisplayScale))
                        selected.SetBendPoint(Clamp(original.BendPoint + delta));
                }
                else
                {
                    var endpoint = activeLineHandle == 0 ? 0 : original.Points.Count - 1;
                    selected.Points[endpoint] = Clamp(original.Points[endpoint] + delta);
                }
            }
            else if (resizeCorner >= 0 && original.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
            {
                var handle = Corners(oldBounds)[resizeCorner];
                var endpoint = (original.Points[0] - handle).Length < (original.Points[^1] - handle).Length ? 0 : original.Points.Count - 1;
                selected.Points = [.. original.Points];
                selected.Points[endpoint] = point;
            }
            else if (resizeCorner >= 0)
            {
                var anchor = Corners(oldBounds)[(resizeCorner + 2) % 4];
                var target = new Rect(anchor, point);
                var sx = Math.Max(4, target.Width) / Math.Max(1, oldBounds.Width);
                var sy = Math.Max(4, target.Height) / Math.Max(1, oldBounds.Height);
                selected.Points = original.Points.Select(p => new Point(target.Left + (p.X - oldBounds.Left) * sx, target.Top + (p.Y - oldBounds.Top) * sy)).ToList();
                if (selected.Tool == AnnotationTool.Text) selected.FontSize = Math.Clamp(original.FontSize * Math.Min(sx, sy), 8, 200);
            }
            else
            {
                var delta = point - start;
                delta.X = Math.Clamp(delta.X, -oldBounds.Left, Math.Max(-oldBounds.Left, Width - oldBounds.Right));
                delta.Y = Math.Clamp(delta.Y, -oldBounds.Top, Math.Max(-oldBounds.Top, Height - oldBounds.Bottom));
                selected.Points = original.Points.Select(p => p + delta).ToList();
            }
            using (var context = ((DrawingVisual)visuals[SelectedIndex]).RenderOpen()) selected.Draw(context, Document.SketchEnabled);
            DrawSelection();
        }
        else if (draft is not null)
        {
            if (draft.Tool is AnnotationTool.Pen or AnnotationTool.Highlight)
            {
                if ((draft.Points[^1] - point).Length < sampleDistance) return;
                if (draft.Points.Count >= 4096)
                {
                    draft.Points = draft.Points.Where((_, i) => i % 2 == 0).ToList();
                    sampleDistance *= 2;
                }
                draft.Points.Add(point);
            }
            else
            {
                var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (draft.Tool is AnnotationTool.Line or AnnotationTool.Arrow) point = ImageAnnotation.SnapEnd(start, point, shift);
                else if (shift)
                {
                    var delta = point - start;
                    var side = Math.Min(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)),
                        Math.Min(delta.X < 0 ? start.X : Width - start.X, delta.Y < 0 ? start.Y : Height - start.Y));
                    point = start + new Vector(delta.X < 0 ? -side : side, delta.Y < 0 ? -side : side);
                }
                draft.Points[^1] = Clamp(point);
            }
            using var context = preview.RenderOpen();
            draft.Draw(context, Document.SketchEnabled);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        UpdateGesture(Clamp(e.GetPosition(this)));
        if (draft is { } completed)
        {
            draft = null;
            using (preview.RenderOpen()) { }
            if (completed.Bounds.Width > 2 || completed.Bounds.Height > 2)
            {
                completed.Straighten();
                Document.Add(completed);
                Select(Document.Items.Count - 1);
            }
        }
        else if (original is { } before && Selected is { } after)
        {
            original = null;
            if (!before.Points.SequenceEqual(after.Points) || before.FontSize != after.FontSize)
                Document.Replace(SelectedIndex, before, after);
        }
        original = null;
        activeLineHandle = -1;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    internal void CancelGesture()
    {
        draft = null;
        using (preview.RenderOpen()) { }
        if (original is { } before && Selected is { } after)
        {
            after.Points = before.Points;
            after.FontSize = before.FontSize;
        }
        original = null;
        activeLineHandle = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Rebuild();
    }

    internal void ChangeSelected(Action<ImageAnnotation> change)
    {
        if (Selected is not { } selected) return;
        var before = selected.Clone();
        var after = selected.Clone();
        change(after);
        Document.Replace(SelectedIndex, before, after);
    }

    internal void DeleteSelected()
    {
        if (SelectedIndex < 0) return;
        var index = SelectedIndex;
        Select(-1);
        Document.Remove(index);
    }

    internal void DuplicateSelected()
    {
        if (Selected is not { } selected) return;
        var copy = selected.Clone();
        var bounds = copy.Bounds;
        var offset = 12 / Math.Max(.05, DisplayScale);
        var delta = new Vector(bounds.Right + offset <= Width ? offset : -Math.Min(offset, Math.Max(0, bounds.Left)),
            bounds.Bottom + offset <= Height ? offset : -Math.Min(offset, Math.Max(0, bounds.Top)));
        copy.Points = copy.Points.Select(point => point + delta).ToList();
        Document.Add(copy);
        Select(Document.Items.Count - 1);
    }
}
