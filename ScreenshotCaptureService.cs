using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterScreenShot
{
    internal static class ScreenshotCaptureService
    {
        public static (string FilePath, BitmapSource Preview) Capture(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bounds), "Capture dimensions must be positive.");
            }

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }
            return SaveCapture(bitmap);
        }

        internal static (string FilePath, BitmapSource Preview) SaveCapture(Bitmap bitmap)
        {
            var pixels = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            BitmapSource preview;
            try
            {
                // BitmapSource.Create copies the pixels, so the GDI bitmap can be released.
                preview = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32, null,
                    pixels.Scan0, checked(pixels.Stride * bitmap.Height), pixels.Stride);
                preview.Freeze();
            }
            finally
            {
                bitmap.UnlockBits(pixels);
            }

            return (SaveBitmapToTemporaryFile(bitmap), preview);
        }

        internal static (string FilePath, BitmapSource Preview) SaveScrollingCapture(ScrollingStitcher stitcher)
        {
            var filePath = NewTemporaryFilePath();
            try
            {
                using (var output = File.Create(filePath))
                    PngStreamWriter.Write(output, stitcher);
                return (filePath, CreateToastPreview(stitcher));
            }
            catch
            {
                ScreenshotFileService.TryDelete(filePath);
                throw;
            }
        }

        internal static BitmapSource LoadFullImage(string filePath)
        {
            using var input = File.OpenRead(filePath);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = input;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private static BitmapSource CreateToastPreview(ScrollingStitcher stitcher)
        {
            // The toast crops tall images to a short center slice. Keep only that slice in memory.
            var visibleHeight = Math.Min(stitcher.Height, Math.Max(1, (int)Math.Ceiling(stitcher.Width * 0.52)));
            var top = (stitcher.Height - visibleHeight) / 2;
            var width = Math.Min(stitcher.Width, 512);
            var height = Math.Max(1, Math.Min(256, (int)Math.Round((double)visibleHeight * width / stitcher.Width)));
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var source = new byte[stitcher.Width * 4];
            for (var y = 0; y < height; y++)
            {
                stitcher.CopyRow(top + (int)((long)y * visibleHeight / height), source);
                for (var x = 0; x < width; x++)
                {
                    var sourcePixel = (int)((long)x * stitcher.Width / width) * 4;
                    var targetPixel = y * stride + x * 4;
                    pixels[targetPixel] = source[sourcePixel];
                    pixels[targetPixel + 1] = source[sourcePixel + 1];
                    pixels[targetPixel + 2] = source[sourcePixel + 2];
                }
            }
            var preview = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            preview.Freeze();
            return preview;
        }

        private static string SaveBitmapToTemporaryFile(Bitmap bitmap)
        {
            var filePath = NewTemporaryFilePath();

            try
            {
                bitmap.Save(filePath, ImageFormat.Png);
                return filePath;
            }
            catch
            {
                ScreenshotFileService.TryDelete(filePath);
                throw;
            }
        }

        private static string NewTemporaryFilePath()
        {
            var folder = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}.png");
        }
    }
}
