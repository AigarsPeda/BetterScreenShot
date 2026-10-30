using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal sealed partial class MonitorSelectionOverlayForm
    {
        private static Forms.Cursor CreateMonitorSelectionCursor()
        {
            try
            {
                using var asset = typeof(MonitorSelectionOverlayForm).Assembly.GetManifestResourceStream("BetterScreenShot.assets.camera.svg");
                if (asset is null)
                {
                    return Forms.Cursors.Hand;
                }

                using var reader = new StreamReader(asset);
                var svgContent = reader.ReadToEnd();
                var pathMatch = CameraPathRegex().Match(svgContent);
                var circleMatch = CameraCircleRegex().Match(svgContent);
                if (!pathMatch.Success || !circleMatch.Success)
                {
                    return Forms.Cursors.Hand;
                }

                const int cursorSize = 32;
                const double scale = cursorSize / 24d;
                var visual = new System.Windows.Media.DrawingVisual();

                using (var context = visual.RenderOpen())
                {
                    var outerPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 4)
                    {
                        StartLineCap = System.Windows.Media.PenLineCap.Round,
                        EndLineCap = System.Windows.Media.PenLineCap.Round,
                        LineJoin = System.Windows.Media.PenLineJoin.Round
                    };

                    var innerPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2.2);
                    innerPen.StartLineCap = System.Windows.Media.PenLineCap.Round;
                    innerPen.EndLineCap = System.Windows.Media.PenLineCap.Round;
                    innerPen.LineJoin = System.Windows.Media.PenLineJoin.Round;

                    context.PushTransform(new System.Windows.Media.ScaleTransform(scale, scale));

                    var bodyGeometry = System.Windows.Media.Geometry.Parse(pathMatch.Groups["value"].Value);
                    var centerX = double.Parse(circleMatch.Groups["cx"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var centerY = double.Parse(circleMatch.Groups["cy"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var radius = double.Parse(circleMatch.Groups["r"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var circleGeometry = new System.Windows.Media.EllipseGeometry(new System.Windows.Point(centerX, centerY), radius, radius);

                    context.DrawGeometry(null, outerPen, bodyGeometry);
                    context.DrawGeometry(null, outerPen, circleGeometry);
                    context.DrawGeometry(null, innerPen, bodyGeometry);
                    context.DrawGeometry(null, innerPen, circleGeometry);
                    context.Pop();
                }

                var bitmapSource = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    cursorSize,
                    cursorSize,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmapSource.Render(visual);

                using var stream = new MemoryStream();
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                encoder.Save(stream);
                stream.Position = 0;

                using var cursorBitmap = new Bitmap(stream);
                var colorBitmapHandle = cursorBitmap.GetHbitmap(Color.FromArgb(0));
                var maskBitmapHandle = IntPtr.Zero;

                try
                {
                    maskBitmapHandle = cursorBitmap.GetHbitmap(Color.FromArgb(0));
                    var iconInfo = new IconInfo
                    {
                        fIcon = false,
                        xHotspot = 6,
                        yHotspot = 6,
                        hbmMask = maskBitmapHandle,
                        hbmColor = colorBitmapHandle
                    };

                    var cursorHandle = CreateIconIndirect(ref iconInfo);
                    return cursorHandle != IntPtr.Zero ? new Forms.Cursor(cursorHandle) : Forms.Cursors.Hand;
                }
                finally
                {
                    DeleteObject(colorBitmapHandle);
                    if (maskBitmapHandle != IntPtr.Zero)
                    {
                        DeleteObject(maskBitmapHandle);
                    }
                }
            }
            catch
            {
                return Forms.Cursors.Hand;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyCursor(IntPtr cursor);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [GeneratedRegex("d=\"(?<value>[^\"]+)\"")]
        private static partial Regex CameraPathRegex();
        [GeneratedRegex("<circle[^>]*cx=\"(?<cx>[^\"]+)\"[^>]*cy=\"(?<cy>[^\"]+)\"[^>]*r=\"(?<r>[^\"]+)\"")]
        private static partial Regex CameraCircleRegex();
    }
}
