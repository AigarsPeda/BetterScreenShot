using System.IO;
using BetterScreenShot;

internal static class OcrAssetTests
{
    internal static void Run(Action<string, Action> check, string directory)
    {
        check("OCR assets are streamed, reused when valid and repaired when corrupt", () =>
        {
            var path = Path.Combine(directory, "ocr-asset.bin");
            byte[] content = [1, 2, 3, 4, 5];
            void Extract()
            {
                using var resource = new MemoryStream(content);
                TextExtractionService.ExtractAsset(resource, path);
                Require(File.ReadAllBytes(path).SequenceEqual(content));
            }
            Extract();
            // A valid cached asset must not be replaced, including while it is in use.
            using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Extract();
            File.WriteAllBytes(path, [5, 4, 3, 2, 1]);
            Extract();
            File.WriteAllBytes(path, [1, 2]);
            Extract();
        });
        check("Failed OCR asset replacement preserves the file and cleans up staging data", () =>
        {
            var path = Path.Combine(directory, "locked-ocr-asset.bin");
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, original);
            using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var resource = new MemoryStream(new byte[] { 3, 2, 1 });
            var failed = false;
            try { TextExtractionService.ExtractAsset(resource, path); }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }
            Require(failed && File.ReadAllBytes(path).SequenceEqual(original));
            Require(Directory.GetFiles(directory, "locked-ocr-asset.bin.*.tmp").Length == 0);
        });
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("OCR asset cache assertion failed.");
    }
}
