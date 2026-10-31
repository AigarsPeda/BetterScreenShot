using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace BetterScreenShot
{
    internal static class ScreenshotFileService
    {
        public static string? SaveCopyAs(string sourceFilePath, BitmapSource image, System.Windows.Window owner)
        {
            var destination = ChooseSavePath(sourceFilePath, owner);
            if (destination is not null) Export(sourceFilePath, destination, image);
            return destination;
        }

        public static string? ChooseSavePath(string sourceFilePath, System.Windows.Window owner, string? originalPath = null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = originalPath is null ? "Save Screenshot As" : "Save Edited Image As",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
                FileName = originalPath is null ? Path.GetFileNameWithoutExtension(sourceFilePath) : Path.GetFileNameWithoutExtension(originalPath) + "_edited",
                InitialDirectory = originalPath is null ? null : Path.GetDirectoryName(originalPath),
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return null;
            }

            return dialog.FileName;
        }

        public static void Export(string sourceFilePath, string destinationPath, BitmapSource image, bool imageModified = false)
        {
            if (string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Choose a destination other than the temporary screenshot file.");
            }

            var extension = Path.GetExtension(destinationPath).ToLowerInvariant();
            BitmapEncoder? encoder = extension switch
            {
                ".png" => imageModified ? new PngBitmapEncoder() : null,
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                _ => throw new ArgumentException("Choose a PNG, JPEG, or BMP file extension.", nameof(destinationPath))
            };

            // Publish a complete file so a failed encode cannot damage an existing save.
            var stagingPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!, $".{Guid.NewGuid():N}.tmp");
            try
            {
                if (encoder is null)
                {
                    File.Copy(sourceFilePath, stagingPath);
                }
                else
                {
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var output = File.Create(stagingPath);
                    encoder.Save(output);
                }

                File.Move(stagingPath, destinationPath, overwrite: true);
            }
            finally
            {
                TryDelete(stagingPath);
            }
        }

        public static void TryDelete(string filePath)
        {
            try
            {
                DeleteIfExists(filePath);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Could not remove temporary screenshot: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Could not remove temporary screenshot: {ex.Message}");
            }
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
