using BetterScreenShot;
using System.Windows;

internal static class WindowTestCleanup
{
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
