namespace BetterScreenShot
{
    internal sealed class TemporaryScreenshot : IDisposable
    {
        public TemporaryScreenshot(string filePath, string? originalPath = null)
        {
            FilePath = filePath;
            OriginalPath = originalPath;
        }

        public string FilePath { get; }
        public string? OriginalPath { get; }
        public bool IsImported => OriginalPath is not null;

        public void Dispose()
        {
            ScreenshotFileService.TryDelete(FilePath);
        }
    }
}
