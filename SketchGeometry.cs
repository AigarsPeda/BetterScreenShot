using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace BetterScreenShot;

internal static class SketchGeometry
{
    public static StreamGeometry Create(Geometry precise, int seed, double width)
    {
        // A stable seed keeps the stroke still during selection, repainting, and export.
        var random = new Random(seed);
        var rough = new StreamGeometry();
        using (var context = rough.Open())
        {
            foreach (var figure in PathGeometry.CreateFromGeometry(precise).Figures)
            {
                var previous = figure.StartPoint;
                context.BeginFigure(previous, false, figure.IsClosed);
                void DrawCurve(Point first, Point second, Point end)
                {
                    var delta = end - previous;
                    var length = delta.Length;
                    var amount = Math.Min(Math.Clamp(width * .22 + .6, .7, 2), length * .08);
                    var normal = length > 0 ? new Vector(-delta.Y / length, delta.X / length) : new Vector();
                    first += normal * ((random.NextDouble() * 2 - 1) * amount * 2);
                    second += normal * ((random.NextDouble() * 2 - 1) * amount * 2);
                    context.BezierTo(first, second, end, true, true);
                    previous = end;
                }
                void DrawEdge(Point end) => DrawCurve(previous + (end - previous) / 3, previous + (end - previous) * (2d / 3), end);
                void DrawQuadratic(Point control, Point end) => DrawCurve(previous + (control - previous) * (2d / 3), end + (control - end) * (2d / 3), end);
                foreach (var segment in figure.Segments)
                {
                    if (segment is PolyLineSegment polyline)
                        foreach (var point in polyline.Points) DrawEdge(point);
                    else if (segment is LineSegment line) DrawEdge(line.Point);
                    else if (segment is BezierSegment curve) DrawCurve(curve.Point1, curve.Point2, curve.Point3);
                    else if (segment is PolyBezierSegment curves)
                        for (var i = 0; i < curves.Points.Count; i += 3) DrawCurve(curves.Points[i], curves.Points[i + 1], curves.Points[i + 2]);
                    else if (segment is QuadraticBezierSegment quadratic) DrawQuadratic(quadratic.Point1, quadratic.Point2);
                    else if (segment is PolyQuadraticBezierSegment quadratics)
                        for (var i = 0; i < quadratics.Points.Count; i += 2) DrawQuadratic(quadratics.Points[i], quadratics.Points[i + 1]);
                }
                if (figure.IsClosed) DrawEdge(figure.StartPoint);
            }
        }
        rough.Freeze();
        return rough;
    }
}
