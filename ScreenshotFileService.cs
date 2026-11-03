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

        public static string? SaveRecordingAs(string sourceFilePath, System.Windows.Window owner)
        {
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (extension is not (".mp4" or ".gif"))
                throw new ArgumentException("Unsupported recording format.", nameof(sourceFilePath));
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Recording As",
                Filter = extension == ".mp4" ? "MP4 Video (*.mp4)|*.mp4" : "Animated GIF (*.gif)|*.gif",
                FileName = Path.GetFileNameWithoutExtension(sourceFilePath),
                DefaultExt = extension,
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(owner) != true) return null;
            var destination = Path.GetFullPath(dialog.FileName);
            if (!Path.GetExtension(destination).Equals(extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Choose a {extension} file name.");
            if (string.Equals(Path.GetFullPath(sourceFilePath), destination, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a destination other than the temporary recording file.");
            var staging = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(sourceFilePath, staging);
                File.Move(staging, destination, overwrite: true);
            }
            finally { TryDelete(staging); }
            return destination;
        }

        public static string CopyRecordingForClipboard(string sourceFilePath)
        {
            // File-drop clipboard entries need a file that outlives the popup.
            var folder = Path.Combine(Path.GetTempPath(), "BetterScreenShot", "Copied Recordings");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, copy);
            return copy;
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
