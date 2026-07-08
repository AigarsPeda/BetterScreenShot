using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BetterScreenShot
{
    internal static class ScreenshotCaptureService
    {
        public static string CaptureAndSave(Rectangle bounds)
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

            var screenshotsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "BetterScreenShot");

            Directory.CreateDirectory(screenshotsFolder);

            var fileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var filePath = Path.Combine(screenshotsFolder, fileName);

            bitmap.Save(filePath, ImageFormat.Png);
            return filePath;
        }
    }
}
