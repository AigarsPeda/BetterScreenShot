using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;

internal static class AsyncImageTests
{
    internal static int Preview(string output)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var pending = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = ImportedImageService.OpenEditor(Path.Combine(Path.GetTempPath(), "Summer holiday.png"), (_, _) => pending.Task);
        try
        {
            editor.Width = 900; editor.Height = 600;
            ThemeIconService.ApplyViewerTheme(editor.Resources, AppTheme.Dark);
            editor.UpdateLayout();
            var delay = Task.Delay(150);
            WindowTestCleanup.PumpUntil(() => delay.IsCompleted);
            var content = (FrameworkElement)editor.Content;
            var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(output);
            encoder.Save(stream);
        }
        finally
        {
            editor.CloseForTest(); pending.SetCanceled();
            WindowTestCleanup.PumpUntil(() => editor.ImageLoadTask.IsCompleted);
            app.Shutdown();
        }
        return 0;
    }

    internal static void Run(Action<string, Action> check, string directory)
    {
        Directory.CreateDirectory(directory);
        check("Loading editor is visible, responds to input, and cannot save placeholder pixels", () =>
        {
            var pending = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var editor = ImportedImageService.OpenEditor(Path.Combine(directory, "pending attēls.png"), (_, _) => pending.Task);
            try
            {
                Require(editor.IsVisible && !editor.IsImageReady && editor.Title.Contains("Loading image"));
                Require(!editor.SaveCopyAsync().GetAwaiter().GetResult());
                var inputRan = false;
                editor.Dispatcher.BeginInvoke(new Action(() => inputRan = true), System.Windows.Threading.DispatcherPriority.Input);
                WindowTestCleanup.PumpUntil(() => inputRan);
                Require(!editor.ImageLoadTask.IsCompleted);
                pending.SetResult(Fixture(31, 17));
                editor.WaitForImage();
                var displayed = (BitmapSource)((System.Windows.Controls.Image)editor.FindName("ViewerImage")).Source;
                Require(displayed.PixelWidth == 31 && displayed.PixelHeight == 17);
                Require(((FrameworkElement)editor.FindName("ImageLoadingOverlay")).Visibility == Visibility.Collapsed);
                Require(((FrameworkElement)editor.FindName("EditorContent")).IsEnabled && !editor.HasUnsavedChanges);
            }
            finally { pending.TrySetCanceled(); editor.CloseForTest(); }
        });
        foreach (var failLate in new[] { false, true })
        check("Closing while loading cancels safely with a late " + (failLate ? "failure" : "success"), () =>
        {
            var pending = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            var editor = ImportedImageService.OpenEditor(Path.Combine(directory, "closed.png"), (_, token) => { observed = token; return pending.Task; });
            editor.Close();
            Require(observed.IsCancellationRequested && !editor.IsVisible);
            if (failLate) pending.SetException(new IOException("Late file error"));
            else pending.SetResult(Fixture(5, 7));
            WindowTestCleanup.PumpUntil(() => editor.ImageLoadTask.IsCompleted);
            editor.ImageLoadTask.GetAwaiter().GetResult();
            Require(!editor.IsVisible && !editor.IsImageReady && editor.ImageLoadError is null);
        });
        check("Missing and corrupt images show an inline error without enabling save", () =>
        {
            var broken = Path.Combine(directory, "broken.png");
            File.WriteAllText(broken, "not an image");
            foreach (var path in new[] { broken, broken + ".missing" })
            {
                var editor = ImportedImageService.OpenEditor(path);
                try
                {
                    WindowTestCleanup.PumpUntil(() => editor.ImageLoadTask.IsCompleted);
                    Require(editor.IsVisible && !editor.IsImageReady && editor.ImageLoadError is not null);
                    Require(((TextBlock)editor.FindName("ImageLoadingHeading")).Text == "Couldn't open image");
                    Require(!((System.Windows.Controls.ProgressBar)editor.FindName("ImageLoadingProgress")).IsIndeterminate);
                    Require(!editor.SaveCopyAsync().GetAwaiter().GetResult());
                }
                finally { editor.CloseForTest(); }
            }
        });
        check("Parallel image requests retain their own result when completion order changes", () =>
        {
            var first = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var one = ImportedImageService.OpenEditor(Path.Combine(directory, "one.png"), (_, _) => first.Task);
            var two = ImportedImageService.OpenEditor(Path.Combine(directory, "two.png"), (_, _) => second.Task);
            try
            {
                second.SetResult(Fixture(40, 20)); two.WaitForImage();
                Require(!one.IsImageReady);
                first.SetResult(Fixture(20, 40)); one.WaitForImage();
                Require(((BitmapSource)((System.Windows.Controls.Image)one.FindName("ViewerImage")).Source).PixelWidth == 20);
                Require(((BitmapSource)((System.Windows.Controls.Image)two.FindName("ViewerImage")).Source).PixelWidth == 40);
            }
            finally { first.TrySetCanceled(); second.TrySetCanceled(); one.CloseForTest(); two.CloseForTest(); }
        });
        check("Queued decoder cancellation skips closed requests and releases the concurrency gate", () =>
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var queuedCancel = new CancellationTokenSource();
            using var runningCancel = new CancellationTokenSource();
            var image = Fixture(3, 3);
            var running = ImportedImageService.DecodeQueuedAsync(() =>
            {
                started.Set();
                if (!release.Wait(5000)) throw new TimeoutException();
                return image;
            }, runningCancel.Token);
            try
            {
                Require(started.Wait(3000));
                var called = false;
                var cancelled = ImportedImageService.DecodeQueuedAsync(() => { called = true; return image; }, queuedCancel.Token);
                var next = ImportedImageService.DecodeQueuedAsync(() => image, CancellationToken.None);
                Require(!next.IsCompleted);
                queuedCancel.Cancel();
                runningCancel.Cancel();
                release.Set();
                ExpectCancelled(cancelled); ExpectCancelled(running);
                Require(ReferenceEquals(next.GetAwaiter().GetResult(), image) && !called);
            }
            finally { release.Set(); }
        });
    }

    private static BitmapSource Fixture(int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        image.Freeze();
        return image;
    }
    private static void ExpectCancelled(Task task)
    {
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation.");
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Async image assertion failed.");
    }
}
