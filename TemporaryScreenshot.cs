namespace BetterScreenShot
{
    internal sealed class TemporaryScreenshot : IDisposable
    {
        public TemporaryScreenshot(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        public void Dispose()
        {
            ScreenshotFileService.TryDelete(FilePath);
        }
    }
}
