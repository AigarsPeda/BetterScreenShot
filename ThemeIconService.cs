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
        private static ImageSource? lightScanIcon;
        private static ImageSource? darkScanIcon;

        private static readonly (string Key, string Light, string Dark)[] ViewerPalette =
        [
            ("ViewerPageBackgroundBrush", "#F7F7F3", "#171922"),
            ("ViewerSurfaceBrush", "#FFFFFF", "#252833"),
            ("ViewerSurfaceBorderBrush", "#E3E3EA", "#3D4151"),
            ("ViewerSurfaceHoverBrush", "#F0F0F8", "#343849"),
            ("ViewerSurfacePressedBrush", "#E8E7F7", "#444963"),
            ("ViewerPrimaryTextBrush", "#252735", "#F0F0F6"),
            ("ViewerSecondaryTextBrush", "#636978", "#B1B5C4"),
            ("ViewerCanvasBrush", "#FBFBF8", "#1B1D27"),
            ("ViewerAccentBrush", "#625FC6", "#B9B5FF"),
            ("ViewerAccentTextBrush", "#FFFFFF", "#222345"),
            ("ViewerSelectedBrush", "#ECEBFF", "#403E6B"),
            ("ViewerSelectionFillBrush", "#55625FC6", "#66B9B5FF")
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
            if (theme == AppTheme.Dark)
            {
                return darkScanIcon ??= RenderScanIcon(theme);
            }

            return lightScanIcon ??= RenderScanIcon(theme);
        }

        private static ImageSource RenderScanIcon(AppTheme theme)
        {
            var strokeColor = theme == AppTheme.Dark
                ? System.Windows.Media.Color.FromRgb(185, 181, 255)
                : System.Windows.Media.Color.FromRgb(98, 95, 198);
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
