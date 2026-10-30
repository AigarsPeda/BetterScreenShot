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

        private static string SaveBitmapToTemporaryFile(Bitmap bitmap)
        {
            var screenshotsFolder = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
            Directory.CreateDirectory(screenshotsFolder);

            var fileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}.png";
            var filePath = Path.Combine(screenshotsFolder, fileName);

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
    }
}
