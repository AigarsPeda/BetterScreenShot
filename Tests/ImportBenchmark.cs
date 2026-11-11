using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;

internal static class ImportBenchmark
{
    internal static int Run(string output)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BetterScreenShot-import-bench-" + Guid.NewGuid().ToString("N")));
        try
        {
            var pixels = new byte[3840 * 2160 * 4];
            new Random(42).NextBytes(pixels);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var image = BitmapSource.Create(3840, 2160, 96, 96, PixelFormats.Bgra32, null, pixels, 3840 * 4);
            var results = new List<object>();
            foreach (var extension in new[] { ".png", ".jpg" })
            {
                var path = Path.Combine(root.FullName, "image" + extension);
                BitmapEncoder encoder = extension == ".png" ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(path)) encoder.Save(stream);
                var samples = new List<object>();
                for (var i = -1; i < 5; i++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var timer = Stopwatch.StartNew();
                    var imported = ImportedImageService.Load(path);
                    timer.Stop();
                    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    using (imported.WorkingFile)
                    {
                        if (i >= 0) samples.Add(new { Ms = timer.Elapsed.TotalMilliseconds, AllocatedBytes = bytes,
                            TemporaryBytes = File.Exists(imported.WorkingFile.FilePath) ? new FileInfo(imported.WorkingFile.FilePath).Length : 0 });
                    }
                }
                results.Add(new { Extension = extension, SourceBytes = new FileInfo(path).Length, Samples = samples });
            }
            File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        finally { root.Delete(true); }
    }
}
