using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using BetterScreenShot;

internal static class RecordingBenchmark
{
    private sealed record Workload(string Name, RecordingFormat Format, VideoQuality Quality,
        int Width, int Height, int Frames);

    private sealed record Sample(string Workload, int Run, double WallMs, double CpuMs,
        long AllocatedBytes, long FileBytes, int Frames);

    internal static int Run()
    {
        var workloads = new[]
        {
            new Workload("Video High 720p", RecordingFormat.Video, VideoQuality.High, 1280, 720, 60),
            new Workload("GIF 640x360", RecordingFormat.Gif, VideoQuality.High, 640, 360, 24)
        };
        var samples = new List<Sample>();
        foreach (var workload in workloads)
        {
            var source = Pattern(workload.Width, workload.Height);
            RunOnce(workload, source, 3, -1);
            for (var run = 0; run < 3; run++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                samples.Add(RunOnce(workload, source, workload.Frames, run));
            }
            var group = samples.Where(sample => sample.Workload == workload.Name).ToArray();
            double Median(Func<Sample, double> value) => group.Select(value).Order().ElementAt(1);
            Console.WriteLine(FormattableString.Invariant($"{workload.Name}: wall {Median(s => s.WallMs):F0} ms, CPU {Median(s => s.CpuMs):F0} ms, alloc {Median(s => s.AllocatedBytes) / 1_000_000:F1} MB, output {Median(s => s.FileBytes) / 1_000_000:F2} MB ({workload.Frames} frames, median of 3)"));
        }

        var report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            Workloads = workloads,
            Samples = samples
        };
        var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "performance");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"recording-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(path);
        return 0;
    }

    private static Sample RunOnce(Workload workload, byte[] source, int frames, int run)
    {
        using var stop = new CancellationTokenSource();
        var captured = 0;
        byte[] Capture(Rectangle _, int width, int height)
        {
            if (width != workload.Width || height != workload.Height)
                throw new InvalidOperationException("Benchmark capture dimensions changed.");
            var pixels = (byte[])source.Clone();
            var x = captured * 13 % width;
            for (var y = 0; y < height; y++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
            }
            if (++captured == frames) stop.Cancel();
            return pixels;
        }

        using var process = Process.GetCurrentProcess();
        var cpuStarted = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        var result = AreaRecordingService.Record(new Rectangle(0, 0, workload.Width, workload.Height),
            workload.Format, stop.Token, CancellationToken.None, Capture, workload.Quality);
        try
        {
            var wallMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var cpuMs = (process.TotalProcessorTime - cpuStarted).TotalMilliseconds;
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            if (captured != frames) throw new InvalidOperationException("Benchmark recorded the wrong frame count.");
            var fileBytes = new FileInfo(result.FilePath).Length;
            if (fileBytes == 0) throw new InvalidOperationException("Benchmark produced an empty recording.");
            return new Sample(workload.Name, run, wallMs, cpuMs, allocatedBytes, fileBytes, captured);
        }
        finally { ScreenshotFileService.TryDelete(result.FilePath); }
    }

    private static byte[] Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            pixels[offset] = (byte)(x / 16 % 16 * 12);
            pixels[offset + 1] = (byte)(y / 16 % 16 * 12);
            pixels[offset + 2] = (byte)((x + y) / 32 % 16 * 12);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }
}
