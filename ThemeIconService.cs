using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterScreenShot
{
    internal enum AppTheme
    {
        Light,
        Dark
    }

    internal static class ThemeIconService
    {
        private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

        private static readonly (string Key, string Light, string Dark)[] ViewerPalette =
        [
            ("ViewerPageBackgroundBrush", "#F7F9FC", "#0F172A"),
            ("ViewerSurfaceBrush", "#FFFFFF", "#111827"),
            ("ViewerSurfaceBorderBrush", "#D8E1EC", "#243041"),
            ("ViewerSurfaceHoverBrush", "#F5F9FD", "#172134"),
            ("ViewerSurfacePressedBrush", "#EEF4FB", "#1C2940"),
            ("ViewerPrimaryTextBrush", "#111827", "#E5EEF9"),
            ("ViewerSecondaryTextBrush", "#415063", "#94A3B8"),
            ("ViewerCanvasBrush", "#EDEFF4", "#0B1220")
        ];

        public static void ApplyViewerTheme(ResourceDictionary resources, AppTheme theme)
        {
            foreach (var (key, light, dark) in ViewerPalette)
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme == AppTheme.Dark ? dark : light);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                resources[key] = brush;
            }
        }

        public static AppTheme DetectWindowsTheme()
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            var rawValue = personalizeKey?.GetValue(AppsUseLightThemeValueName);
            return rawValue is int appTheme && appTheme == 0 ? AppTheme.Dark : AppTheme.Light;
        }

        public static ImageSource CreateScanIcon(AppTheme theme)
        {
            var strokeColor = theme == AppTheme.Dark ? Colors.White : Colors.Black;
            var strokeBrush = new SolidColorBrush(strokeColor);
            strokeBrush.Freeze();

            var pen = new System.Windows.Media.Pen(strokeBrush, 2.25)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            var visual = new DrawingVisual();

            using (var drawingContext = visual.RenderOpen())
            {
                drawingContext.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, 64, 64));
                drawingContext.PushTransform(new ScaleTransform(64d / 24d, 64d / 24d));
                drawingContext.DrawGeometry(null, pen, CreateGeometry());
                drawingContext.Pop();
            }

            var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static Geometry CreateGeometry()
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                DrawOpenFigure(context, new System.Windows.Point(3, 7), new System.Windows.Point(3, 5), new System.Windows.Point(5, 3), new System.Windows.Point(7, 3));
                DrawOpenFigure(context, new System.Windows.Point(17, 3), new System.Windows.Point(19, 3), new System.Windows.Point(21, 5), new System.Windows.Point(21, 7));
                DrawOpenFigure(context, new System.Windows.Point(21, 17), new System.Windows.Point(21, 19), new System.Windows.Point(19, 21), new System.Windows.Point(17, 21));
                DrawOpenFigure(context, new System.Windows.Point(7, 21), new System.Windows.Point(5, 21), new System.Windows.Point(3, 19), new System.Windows.Point(3, 17));
            }

            geometry.Freeze();
            return geometry;
        }

        private static void DrawOpenFigure(StreamGeometryContext context, params System.Windows.Point[] points)
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);

            for (var index = 1; index < points.Length; index++)
            {
                context.LineTo(points[index], isStroked: true, isSmoothJoin: true);
            }
        }
    }
}
