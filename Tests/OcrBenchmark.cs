using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

internal static class OcrBenchmark
{
    internal static int Run()
    {
        var samples = new List<Sample>();
        var fixtures = new[]
        {
            ("Large text", TextExtractionTests.Fixture()),
            ("Small file list", OcrLayoutTests.FileListFixture()),
            ("1080p text", DesktopFixture()),
            ("Blank", TextExtractionTests.Fixture(""))
        };
        foreach (var (name, image) in fixtures)
        foreach (var language in new[] { TextExtractionService.AutomaticLanguage, "eng" })
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var timings = new ConcurrentQueue<OcrTiming>();
                using var process = Process.GetCurrentProcess();
                var cpuStarted = process.TotalProcessorTime;
                var started = Stopwatch.GetTimestamp();
                var result = TextExtractionService.RecognizeAsync(image, language, reportTiming: timings.Enqueue).GetAwaiter().GetResult();
                samples.Add(new Sample(name, language, iteration, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    (process.TotalProcessorTime - cpuStarted).TotalMilliseconds, result.Words.Count, timings.ToList()));
            }
            var group = samples.Where(sample => sample.Name == name && sample.Language == language).ToArray();
            double Median(Func<Sample, double> value) => group.Select(value).Order().ElementAt(1);
            var initMs = Median(s => s.Timings.Where(t => t.Stage == "init").Sum(t => t.Milliseconds));
            var recognitionMs = Median(s => s.Timings.Where(t => t.Stage == "recognize").Sum(t => t.Milliseconds));
            var passes = group[1].Timings.Count(t => t.Stage == "recognize");
            Console.WriteLine(FormattableString.Invariant($"{name,-16} {language,-4} wall={Median(s => s.WallMs):F0}ms CPU={Median(s => s.CpuMs):F0}ms init={initMs:F0}ms recognize={recognitionMs:F0}ms passes={passes} words={group[1].Words}"));
        }
        var path = Path.Combine(Environment.CurrentDirectory, "obj", "ocr-benchmark.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Environment.ProcessorCount, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(path);
        return 0;
    }

    internal static BitmapSource DesktopFixture()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1920, 1080));
            for (var row = 0; row < 16; row++)
                drawing.DrawText(new FormattedText($"Line {row + 1:D2}: Screenshot text recognition, filenames and selection handles.",
                    CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"),
                    18, Brushes.Black, 1), new Point(45, 40 + row * 58));
        }
        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static int Viewer()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(Path.GetTempPath(), "ocr-benchmark-unused.png")), DesktopFixture())
            { Title = "OCR responsiveness benchmark" };
        var previousContext = SynchronizationContext.Current;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        var gaps = new List<double>();
        try
        {
            window.Show();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            TextExtractionTests.Complete(Task.Delay(250));
            var lastTick = Stopwatch.GetTimestamp();
            timer.Tick += (_, _) =>
            {
                gaps.Add(Stopwatch.GetElapsedTime(lastTick).TotalMilliseconds);
                lastTick = Stopwatch.GetTimestamp();
            };
            timer.Start();
            var started = Stopwatch.GetTimestamp();
            TextExtractionTests.Complete(window.ExtractTextAsync());
            var recognitionMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            timer.Stop();
            TextExtractionTests.Complete(window.ToggleTextPanelAsync());
            TextExtractionTests.Complete(Task.Delay(250));
            started = Stopwatch.GetTimestamp();
            var reopen = window.ToggleTextPanelAsync();
            if (!reopen.IsCompleted) throw new InvalidOperationException("Cached reopening should not start background recognition.");
            reopen.GetAwaiter().GetResult();
            var cachedReopenDispatchMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var report = new
            {
                RecognitionMs = recognitionMs, CachedReopenDispatchMs = cachedReopenDispatchMs,
                HeartbeatTicks = gaps.Count, MaxHeartbeatGapMs = gaps.DefaultIfEmpty().Max(),
                P95HeartbeatGapMs = gaps.Order().ElementAtOrDefault((int)(gaps.Count * .95))
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            var path = Path.Combine(Environment.CurrentDirectory, "obj", "ocr-ui-benchmark.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            Console.WriteLine(json);
            return 0;
        }
        finally
        {
            timer.Stop();
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            app.Shutdown();
        }
    }

    private sealed record Sample(string Name, string Language, int Iteration, double WallMs, double CpuMs, int Words, List<OcrTiming> Timings);
}
