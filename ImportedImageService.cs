using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BetterScreenShot;

internal static class ImportedImageService
{
    private static readonly HashSet<string> supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    internal static bool TryGetDroppedImage(System.Windows.IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop) || data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: 1 } files)
            return false;
        if (!supportedExtensions.Contains(Path.GetExtension(files[0]))) return false;
        path = files[0];
        return true;
    }

    internal static ScreenshotViewerWindow OpenEditor(string filePath)
    {
        var imported = Load(filePath);
        try
        {
            var editor = new ScreenshotViewerWindow(imported.WorkingFile, imported.Image);
            ScreenshotToastWindow.HideCurrent();
            editor.Closed += (_, _) => ScreenshotToastWindow.RestoreCurrent();
            editor.Show();
            editor.Activate();
            return editor;
        }
        catch
        {
            imported.WorkingFile.Dispose();
            ScreenshotToastWindow.RestoreCurrent();
            throw;
        }
    }

    internal static (TemporaryScreenshot WorkingFile, BitmapSource Image) Load(string filePath)
    {
        var originalPath = Path.GetFullPath(filePath);
        BitmapImage image;
        using (var input = File.OpenRead(originalPath))
        {
            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = input;
            image.EndInit();
            image.Freeze();
        }

        var directory = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
        Directory.CreateDirectory(directory);
        var workingPath = Path.Combine(directory, $"import_{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(workingPath)) encoder.Save(output);
            return (new TemporaryScreenshot(workingPath, originalPath), image);
        }
        catch
        {
            ScreenshotFileService.TryDelete(workingPath);
            throw;
        }
    }
}
