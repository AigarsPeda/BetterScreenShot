using System.Diagnostics;
using System.Reflection;
using BetterScreenShot;
using Forms = System.Windows.Forms;

internal static class OverlayBenchmark
{
    internal static int Probe()
    {
        var screen = Forms.Screen.PrimaryScreen!;
        using var backdrop = new Forms.Form
        {
            FormBorderStyle = Forms.FormBorderStyle.None,
            StartPosition = Forms.FormStartPosition.Manual,
            Bounds = screen.Bounds,
            BackColor = Color.White,
            TopMost = true,
            ShowInTaskbar = false
        };
        backdrop.Show();
        var type = typeof(MonitorSelectionOverlayForm);
        var overlaysField = type.GetField("cachedOverlays", BindingFlags.NonPublic | BindingFlags.Static)!;
        var startField = type.GetField("dragStart", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var currentField = type.GetField("dragCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var update = type.GetMethod("UpdateSelectionPresentation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var selection = MonitorSelectionOverlayForm.SelectAreaAsync();
        try
        {
            var overlay = ((List<MonitorSelectionOverlayForm>)overlaysField.GetValue(null)!)
                .Single(item => item.Bounds == screen.Bounds);
            var start = new System.Drawing.Point(80, 80);
            var firstEnd = new System.Drawing.Point(330, 230);
            var secondEnd = new System.Drawing.Point(290, 200);
            var first = Rectangle.FromLTRB(start.X, start.Y, firstEnd.X, firstEnd.Y);
            var second = Rectangle.FromLTRB(start.X, start.Y, secondEnd.X, secondEnd.Y);
            startField.SetValue(overlay, start);
            currentField.SetValue(overlay, firstEnd);
            update.Invoke(overlay, [null, first]);
            overlay.Update();
            DesktopWindowService.FlushPendingUpdates();
            var oldEdge = overlay.PointToScreen(new System.Drawing.Point(329, 150));
            var firstColor = ReadPixel(oldEdge);
            Console.WriteLine($"Old edge after first paint: {firstColor}");
            currentField.SetValue(overlay, secondEnd);
            update.Invoke(overlay, [first, second]);
            DesktopWindowService.FlushPendingUpdates();
            var immediateColor = ReadPixel(oldEdge);
            Console.WriteLine($"Old edge before next paint: {immediateColor}");
            overlay.Update();
            DesktopWindowService.FlushPendingUpdates();
            var settledColor = ReadPixel(oldEdge);
            Console.WriteLine($"Old edge after next paint: {settledColor}");
            if (firstColor.ToArgb() == settledColor.ToArgb() || immediateColor.ToArgb() != settledColor.ToArgb())
                throw new InvalidOperationException("The previous selection border remained visible until a later paint.");
            return 0;
        }
        finally
        {
            MonitorSelectionOverlayForm.CancelActiveSelection();
            _ = selection.GetAwaiter().GetResult();
            backdrop.Close();
        }

        static Color ReadPixel(System.Drawing.Point point)
        {
            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(point, System.Drawing.Point.Empty, new Size(1, 1));
            return bitmap.GetPixel(0, 0);
        }
    }

    internal static int Run(bool repeatedPositions = false)
    {
        const int moves = 120;
        var type = typeof(MonitorSelectionOverlayForm);
        var overlaysField = type.GetField("cachedOverlays", BindingFlags.NonPublic | BindingFlags.Static)!;
        var startField = type.GetField("dragStart", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var currentField = type.GetField("dragCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var selectionMethod = type.GetMethod("UpdateSelectionPresentation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var samples = new List<(double WallMs, double CpuMs, long Allocated, double MedianMoveMs, double P95MoveMs)>();
        var screen = Forms.Screen.PrimaryScreen!;

        try
        {
            MonitorSelectionOverlayForm.WarmUpOverlays();
            RunOnce(); // Warm up handles and paint paths.
            for (var trial = 0; trial < 3; trial++)
                samples.Add(RunOnce());
        }
        finally
        {
            MonitorSelectionOverlayForm.CancelActiveSelection();
        }

        Console.WriteLine($"Overlay area drag: {moves} moves, {screen.Bounds.Width}x{screen.Bounds.Height} primary monitor");
        if (repeatedPositions) Console.WriteLine("Repeated-position workload: four updates per position, 30 distinct positions.");
        for (var trial = 0; trial < samples.Count; trial++)
        {
            var sample = samples[trial];
            Console.WriteLine($"Trial {trial + 1}: wall {sample.WallMs:F1} ms, CPU {sample.CpuMs:F1} ms, "
                + $"allocated {sample.Allocated / 1_000_000d:F2} MB, "
                + $"move median {sample.MedianMoveMs:F2} ms, p95 {sample.P95MoveMs:F2} ms");
        }
        return 0;

        (double WallMs, double CpuMs, long Allocated, double MedianMoveMs, double P95MoveMs) RunOnce()
        {
            var selection = MonitorSelectionOverlayForm.SelectAreaAsync();
            try
            {
                var overlays = (List<MonitorSelectionOverlayForm>)overlaysField.GetValue(null)!;
                var overlay = overlays.Single(item => item.Bounds == screen.Bounds);
                var start = new System.Drawing.Point(80, 80);
                startField.SetValue(overlay, start);
                currentField.SetValue(overlay, start);
                var previous = Rectangle.Empty;
                var latencies = new double[moves];
                var process = Process.GetCurrentProcess();
                var cpuBefore = process.TotalProcessorTime;
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var wall = Stopwatch.StartNew();
                for (var move = 0; move < moves; move++)
                {
                    var step = (repeatedPositions ? move / 4 : move) % 40;
                    var end = new System.Drawing.Point(330 + step * 4, 230 + step * 3);
                    var current = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
                    var tick = Stopwatch.GetTimestamp();
                    currentField.SetValue(overlay, end);
                    selectionMethod.Invoke(overlay, [move == 0 ? null : previous, current]);
                    overlay.Update();
                    latencies[move] = Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
                    previous = current;
                }
                wall.Stop();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                var cpu = process.TotalProcessorTime - cpuBefore;
                Array.Sort(latencies);
                return (wall.Elapsed.TotalMilliseconds, cpu.TotalMilliseconds, allocated,
                    latencies[moves / 2], latencies[(int)Math.Ceiling(moves * 0.95) - 1]);
            }
            finally
            {
                MonitorSelectionOverlayForm.CancelActiveSelection();
                _ = selection.GetAwaiter().GetResult();
            }
        }
    }
}
