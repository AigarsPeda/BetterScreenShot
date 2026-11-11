using BetterScreenShot;
using System.Windows;

internal static class WindowTestCleanup
{
    internal static void WaitForImage(this ScreenshotViewerWindow editor)
    {
        PumpUntil(() => editor.ImageLoadTask.IsCompleted);
        editor.ImageLoadTask.GetAwaiter().GetResult();
        if (!editor.IsImageReady) throw new InvalidOperationException(editor.ImageLoadError ?? "Image was not ready.");
    }

    internal static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false),
                System.Windows.Threading.DispatcherPriority.Background);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!done()) throw new TimeoutException("The UI operation did not finish.");
    }

    internal static void ShowForTest(this Window window)
    {
        if (window is AreaRecordingWindow recording) recording.ConfirmDiscardCapture = () => true;
        if (window is ScrollingCaptureWindow scrolling) scrolling.ConfirmDiscardCapture = () => true;
        window.Show();
    }
    // Tests own synthetic captures. Explicitly discard them during fixture cleanup.
    internal static void CloseForTest(this Window window)
    {
        if (window is ScreenshotViewerWindow editor) editor.DiscardAndClose();
        else window.Close();
    }
}
