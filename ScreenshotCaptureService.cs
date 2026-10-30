using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BetterScreenShot
{
    internal static class ScreenshotCaptureService
    {
        public static string CaptureToTemporaryFile(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bounds), "Capture dimensions must be positive.");
            }

            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            return SaveBitmapToTemporaryFile(bitmap);
        }

        public static string SaveBitmapToTemporaryFile(Bitmap bitmap)
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
