using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;
using Point = System.Windows.Point;

internal static class AnnotationRenderingTests
{
    internal static void Run(Action<string, Action> check)
    {
        check("Incremental annotation visuals match exports through edits, history and style changes", () =>
        {
            var surface = new AnnotationSurface { Width = 240, Height = 160 };
            foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Arrow, AnnotationTool.Text })
            {
                surface.Document.Add(new ImageAnnotation
                {
                    Tool = tool, Points = [new(20, 30), new(180, 110)], Text = "Note", SketchSeed = 42
                });
                RequireMatchingPixels(surface);
            }
            var firstVisual = VisualTreeHelper.GetChild(surface, 0);
            var lastVisual = VisualTreeHelper.GetChild(surface, 3);
            var changed = surface.Document.Items[1].Clone();
            changed.Color = Colors.Blue;
            surface.Document.Replace(1, surface.Document.Items[1], changed);
            RequireMatchingPixels(surface);
            Require(ReferenceEquals(firstVisual, VisualTreeHelper.GetChild(surface, 0)));
            Require(ReferenceEquals(lastVisual, VisualTreeHelper.GetChild(surface, 3)));

            surface.Document.Remove(1);
            RequireMatchingPixels(surface);
            Require(ReferenceEquals(lastVisual, VisualTreeHelper.GetChild(surface, 2)));
            surface.Document.Undo();
            RequireMatchingPixels(surface);
            surface.Document.Undo();
            RequireMatchingPixels(surface);
            surface.Document.Redo();
            RequireMatchingPixels(surface);
            surface.Document.Redo();
            RequireMatchingPixels(surface);
            surface.Document.SketchEnabled = false;
            RequireMatchingPixels(surface);
            surface.Document.SketchEnabled = true;
            RequireMatchingPixels(surface);
            while (surface.Document.Items.Count > 0)
            {
                surface.Document.Remove(0);
                RequireMatchingPixels(surface);
            }
        });
        check("Cancelling a gesture restores rendered pixels and keeps unrelated visuals", () =>
        {
            var surface = new AnnotationSurface { Width = 240, Height = 160 };
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Text, Points = [new(20, 30)], Text = "Note" });
            surface.Document.Add(new ImageAnnotation { Tool = AnnotationTool.Arrow, Points = [new(10, 120), new(200, 120)] });
            var untouched = VisualTreeHelper.GetChild(surface, 1);
            surface.Select(0);
            var before = surface.Selected!.Clone();
            typeof(AnnotationSurface).GetField("original", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(surface, before);
            surface.UpdateGesture(new Point(30, 20));
            Require(surface.Selected.Points[0] != before.Points[0]);
            surface.CancelGesture();
            Require(surface.Selected.Points.SequenceEqual(before.Points));
            surface.Select(-1);
            RequireMatchingPixels(surface);
            surface.CancelGesture();
            Require(ReferenceEquals(untouched, VisualTreeHelper.GetChild(surface, 1)));
            RequireMatchingPixels(surface);
        });
    }

    private static void RequireMatchingPixels(AnnotationSurface surface)
    {
        var width = (int)surface.Width;
        var height = (int)surface.Height;
        surface.Measure(new System.Windows.Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        var actual = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        actual.Render(surface);
        var background = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, new byte[width * height * 4], width * 4);
        var expected = surface.Document.Render(background);
        var actualPixels = new byte[width * height * 4];
        var expectedPixels = new byte[actualPixels.Length];
        actual.CopyPixels(actualPixels, width * 4, 0);
        expected.CopyPixels(expectedPixels, width * 4, 0);
        Require(actualPixels.SequenceEqual(expectedPixels));
        Require(VisualTreeHelper.GetChildrenCount(surface) == surface.Document.Items.Count + 2);
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Annotation visual did not match the document.");
    }
}
