using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace BetterScreenShot
{
    internal static class ScreenshotFileService
    {
        public static BitmapImage LoadBitmap(string filePath)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(filePath);
            image.EndInit();
            image.Freeze();
            return image;
        }

        public static void CopyToClipboard(string sourceFilePath)
        {
            System.Windows.Clipboard.SetImage(LoadBitmap(sourceFilePath));
        }

        public static string? SaveCopyAs(string sourceFilePath)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Screenshot As",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
                FileName = Path.GetFileName(sourceFilePath),
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            File.Copy(sourceFilePath, dialog.FileName, true);
            return dialog.FileName;
        }

        public static void DeleteIfExists(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
