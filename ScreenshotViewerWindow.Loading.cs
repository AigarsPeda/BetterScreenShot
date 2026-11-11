using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private CancellationTokenSource? imageLoadCancellation;
    internal bool IsImageReady { get; private set; } = true;
    internal string? ImageLoadError { get; private set; }
    internal Task ImageLoadTask { get; private set; } = Task.CompletedTask;
    internal static BitmapSource LoadingPlaceholder { get; } = CreateLoadingPlaceholder();

    private static BitmapSource CreateLoadingPlaceholder()
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        image.Freeze();
        return image;
    }

    internal void BeginImageLoading(string path, Task<BitmapSource> pending, CancellationTokenSource cancellation)
    {
        imageLoadCancellation = cancellation;
        IsImageReady = false;
        EditorContent.IsEnabled = false;
        ImageLoadingOverlay.Visibility = Visibility.Visible;
        ImageLoadingName.Text = Path.GetFileName(path);
        ImageLoadingProgress.IsIndeterminate = true;
        UpdateDocumentTitle();
        ImageLoadTask = FinishImageLoadAsync(pending, cancellation.Token);
    }

    private async Task FinishImageLoadAsync(Task<BitmapSource> pending,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await pending.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await UpdateLoadingUiAsync(() =>
            {
                ViewerImage.Source = image;
                ImageStage.Width = Annotations.Width = image.PixelWidth;
                ImageStage.Height = Annotations.Height = image.PixelHeight;
                IsImageReady = true;
                ImageLoadingProgress.IsIndeterminate = false;
                ImageLoadingOverlay.Visibility = Visibility.Collapsed;
                EditorContent.IsEnabled = true;
                UpdateZoomState();
                UpdateDocumentTitle();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (cancellationToken.IsCancellationRequested) return;
            await UpdateLoadingUiAsync(() =>
            {
                ImageLoadError = error.Message;
                ImageLoadingHeading.Text = "Couldn't open image";
                ImageLoadingDetail.Text = "The file may be unavailable, damaged, or unsupported.";
                ImageLoadingDetail.Visibility = Visibility.Visible;
                ImageLoadingProgress.IsIndeterminate = false;
                ImageLoadingProgress.Visibility = Visibility.Collapsed;
                ImageLoadingClose.Content = "Close";
                UpdateDocumentTitle();
            }).ConfigureAwait(false);
        }
    }

    private async Task UpdateLoadingUiAsync(Action update)
    {
        if (Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(() => { if (!isClosing) update(); }); }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }

    private void CancelImageLoading_Click(object sender, RoutedEventArgs e) => Close();
}
