using System.IO;
using System.Windows.Media.Imaging;

namespace BetterScreenShot;

internal static class ImportedImageService
{
    private static readonly SemaphoreSlim decoding = new(1, 1);
    internal static IReadOnlyList<string> SupportedExtensions { get; } =
        Array.AsReadOnly(new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" });

    internal static string[] GetLaunchPaths(string[] args) => args
        .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
        .Select(Path.GetFullPath).ToArray();

    internal static bool TryGetDroppedImage(System.Windows.IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop) || data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: 1 } files)
            return false;
        if (!SupportedExtensions.Contains(Path.GetExtension(files[0]), StringComparer.OrdinalIgnoreCase)) return false;
        path = files[0];
        return true;
    }

    internal static ScreenshotViewerWindow OpenEditor(string filePath,
        Func<string, CancellationToken, Task<BitmapSource>>? load = null)
    {
        var working = CreateWorkingFile(filePath);
        var cancellation = new CancellationTokenSource();
        Task<BitmapSource>? pending = null;
        ScreenshotViewerWindow? editor = null;
        try
        {
            // Decode while WPF constructs and lays out the editor, not afterwards.
            pending = (load ?? DecodeAsync)(working.OriginalPath!, cancellation.Token);
            editor = new ScreenshotViewerWindow(working, ScreenshotViewerWindow.LoadingPlaceholder);
            editor.BeginImageLoading(working.OriginalPath!, pending, cancellation);
            ScreenshotToastWindow.HideCurrent();
            editor.Closed += (_, _) => ScreenshotToastWindow.RestoreCurrent();
            editor.Show();
            editor.Activate();
            return editor;
        }
        catch
        {
            if (editor is null) { cancellation.Cancel(); cancellation.Dispose(); }
            else editor.DiscardAndClose();
            if (pending is not null)
                _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            working.Dispose();
            ScreenshotToastWindow.RestoreCurrent();
            throw;
        }
    }

    internal static (TemporaryScreenshot WorkingFile, BitmapSource Image) Load(string filePath)
        => (CreateWorkingFile(filePath), Decode(filePath));

    internal static Task<BitmapSource> DecodeAsync(string filePath, CancellationToken cancellationToken)
        => DecodeQueuedAsync(() => Decode(filePath), cancellationToken);

    internal static async Task<BitmapSource> DecodeQueuedAsync(Func<BitmapSource> decode, CancellationToken cancellationToken)
    {
        // One native decoder at a time bounds peak temporary memory when many
        // images are opened. Window rendering remains on the independent UI thread.
        await decoding.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = decode();
                cancellationToken.ThrowIfCancellationRequested();
                return image;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { decoding.Release(); }
    }

    private static BitmapSource Decode(string filePath)
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
        return image;
    }

    private static TemporaryScreenshot CreateWorkingFile(string filePath)
    {
        var originalPath = Path.GetFullPath(filePath);
        var directory = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
        Directory.CreateDirectory(directory);
        var workingPath = Path.Combine(directory, $"import_{Guid.NewGuid():N}.png");
        // The editor owns the decoded pixels and encodes them only when saving.
        // Reserve a separate working path for save/cleanup metadata; never use the
        // original as the disposable working file, and avoid an unused PNG copy.
        return new TemporaryScreenshot(workingPath, originalPath);
    }
}
