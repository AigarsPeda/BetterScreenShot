using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace BetterScreenShot;

internal enum AnnotationTool { Select, Hand, Pen, Highlight, Line, Arrow, Rectangle, Ellipse, Text }

internal sealed class ImageAnnotation
{
    public AnnotationTool Tool { get; init; }
    public List<Point> Points { get; set; } = [];
    public Color Color { get; set; } = Colors.Red;
    public double Width { get; set; } = 4;
    public double FontSize { get; set; } = 28;
    public string Text { get; set; } = "Text";
    public int SketchSeed { get; init; } = Random.Shared.Next();
    public bool CanBend => Tool is AnnotationTool.Line or AnnotationTool.Arrow && Points.Count >= 2;
    public Point BendPoint => Points.Count == 3 ? Points[1] : new Point((Points[0].X + Points[^1].X) / 2, (Points[0].Y + Points[^1].Y) / 2);
    private Point CurveControl => new(2 * BendPoint.X - (Points[0].X + Points[^1].X) / 2,
        2 * BendPoint.Y - (Points[0].Y + Points[^1].Y) / 2);

    public ImageAnnotation Clone() => new()
    {
        Tool = Tool, Points = [.. Points], Color = Color, Width = Width, FontSize = FontSize, Text = Text, SketchSeed = SketchSeed
    };

    public FormattedText FormatText() => new(Text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), FontSize, new SolidColorBrush(Color), 1);

    public Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            if (Tool == AnnotationTool.Text)
            {
                var text = FormatText();
                return new Rect(Points[0], new System.Windows.Size(Math.Max(8, text.WidthIncludingTrailingWhitespace), text.Height));
            }
            if (CanBend && Points.Count == 3) return Geometry().Bounds;
            var bounds = new Rect(Points[0], Points[^1]);
            foreach (var point in Points) bounds.Union(point);
            return bounds;
        }
    }

    public Geometry Geometry()
    {
        if (Tool == AnnotationTool.Rectangle) return new RectangleGeometry(Bounds);
        if (Tool == AnnotationTool.Ellipse) return new EllipseGeometry(Bounds);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            if (Points.Count == 0) return geometry;
            path.BeginFigure(Points[0], false, false);
            if (CanBend && Points.Count == 3)
            {
                // The curve passes through the handle, rather than merely aiming toward it.
                path.QuadraticBezierTo(CurveControl, Points[^1], true, true);
            }
            else if (Tool is AnnotationTool.Pen or AnnotationTool.Highlight && Points.Count > 2)
            {
                for (var i = 1; i < Points.Count - 1; i++)
                {
                    var middle = new Point((Points[i].X + Points[i + 1].X) / 2, (Points[i].Y + Points[i + 1].Y) / 2);
                    path.QuadraticBezierTo(Points[i], middle, true, true);
                }
                path.LineTo(Points[^1], true, true);
            }
            else path.LineTo(Points[^1], true, false);

            if (Tool == AnnotationTool.Arrow && Points.Count >= 2)
            {
                var direction = Points[^1] - (Points.Count == 3 ? CurveControl : Points[0]);
                if (direction.Length < .001) direction = Points[^1] - Points[0];
                if (direction.Length > 0)
                {
                    var length = Math.Min(direction.Length * .45, Math.Max(14, Width * 4));
                    direction.Normalize();
                    var normal = new Vector(-direction.Y, direction.X);
                    path.BeginFigure(Points[^1] - direction * length + normal * length * .5, false, false);
                    path.LineTo(Points[^1], true, false);
                    path.LineTo(Points[^1] - direction * length - normal * length * .5, true, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public void Draw(DrawingContext context, bool sketchEnabled = true)
    {
        if (Points.Count == 0) return;
        if (Tool == AnnotationTool.Text) { context.DrawText(FormatText(), Points[0]); return; }
        var brush = new SolidColorBrush(Color);
        var pen = new Pen(brush, Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (Tool == AnnotationTool.Highlight) context.PushOpacity(.32);
        var geometry = Geometry();
        if (sketchEnabled && Tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse)
        {
            pen.Thickness = Width * .8;
            context.DrawGeometry(null, pen, SketchGeometry.Create(geometry, SketchSeed, Width));
            pen = pen.Clone();
            pen.Thickness = Width * .45;
            // Brush opacity avoids a full offscreen compositing layer for every outline.
            pen.Brush = new SolidColorBrush(Color) { Opacity = .55 };
            context.DrawGeometry(null, pen, SketchGeometry.Create(geometry, SketchSeed ^ 0x59A7, Width));
        }
        else context.DrawGeometry(null, pen, geometry);
        if (Tool == AnnotationTool.Highlight) context.Pop();
    }

    public void SetBendPoint(Point point)
    {
        if (!CanBend) return;
        Points = [Points[0], point, Points[^1]];
    }

    public bool HitTest(Point point, double tolerance)
    {
        var bounds = Bounds;
        bounds.Inflate(Math.Max(tolerance, Width), Math.Max(tolerance, Width));
        if (!bounds.Contains(point)) return false;
        var geometry = Geometry();
        return Tool == AnnotationTool.Text || Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse && geometry.FillContains(point)
            || geometry.StrokeContains(new Pen(System.Windows.Media.Brushes.Black, Width + tolerance * 2), point);
    }

    public void Straighten()
    {
        if (Tool is not (AnnotationTool.Pen or AnnotationTool.Highlight) || Points.Count < 3) return;
        var delta = Points[^1] - Points[0];
        if (delta.Length < 8) return;
        var distance = delta.Length;
        var origin = Points[0];
        // Only tidy strokes that were already intended to be straight.
        if (Points.Any(p => Math.Abs(Vector.CrossProduct(p - origin, delta)) / distance > Math.Min(8, distance * .035))) return;
        Points = [origin, SnapEnd(origin, Points[^1], false)];
    }

    public static Point SnapEnd(Point start, Point end, bool constrain)
    {
        var delta = end - start;
        if (delta.Length < 1) return end;
        var angle = Math.Atan2(delta.Y, delta.X);
        var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        return constrain || Math.Abs(angle - snapped) < Math.PI / 36
            ? start + new Vector(Math.Cos(snapped), Math.Sin(snapped)) * delta.Length : end;
    }
}

internal sealed class AnnotationDocument
{
    private sealed record Edit(int Index, ImageAnnotation? Before, ImageAnnotation? After);
    private readonly List<ImageAnnotation> items = [];
    private readonly Stack<Edit> undo = new();
    private readonly Stack<Edit> redo = new();
    private bool sketchEnabled = true;
    public bool SketchEnabled
    {
        get => sketchEnabled;
        set
        {
            if (sketchEnabled == value) return;
            sketchEnabled = value;
            Changed?.Invoke();
        }
    }
    public IReadOnlyList<ImageAnnotation> Items => items;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;

    public void Add(ImageAnnotation item) => Apply(new Edit(items.Count, null, item.Clone()));
    public void Remove(int index) => Apply(new Edit(index, items[index].Clone(), null));
    public void Replace(int index, ImageAnnotation before, ImageAnnotation after) => Apply(new Edit(index, before.Clone(), after.Clone()));

    private void Apply(Edit edit)
    {
        Set(edit.Index, edit.Before, edit.After);
        undo.Push(edit);
        redo.Clear();
        Changed?.Invoke();
    }

    private void Set(int index, ImageAnnotation? before, ImageAnnotation? after)
    {
        if (after is null) items.RemoveAt(index);
        else if (before is null) items.Insert(index, after.Clone());
        else items[index] = after.Clone();
    }

    public void Undo()
    {
        if (!undo.TryPop(out var edit)) return;
        Set(edit.Index, edit.After, edit.Before);
        redo.Push(edit);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!redo.TryPop(out var edit)) return;
        Set(edit.Index, edit.Before, edit.After);
        undo.Push(edit);
        Changed?.Invoke();
    }

    public BitmapSource Render(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.PushClip(new RectangleGeometry(new Rect(0, 0, source.PixelWidth, source.PixelHeight)));
            foreach (var item in items) item.Draw(context, SketchEnabled);
            context.Pop();
        }
        var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public Task<BitmapSource> RenderAsync(BitmapSource source)
    {
        // Capture an independent version so editing can continue during export.
        var snapshot = items.Select(item => item.Clone()).ToArray();
        var sketch = SketchEnabled;
        var completion = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var document = new AnnotationDocument { SketchEnabled = sketch };
                document.items.AddRange(snapshot);
                completion.SetResult(document.Render(source));
            }
            catch (Exception error) { completion.SetException(error); }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "Screenshot export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
