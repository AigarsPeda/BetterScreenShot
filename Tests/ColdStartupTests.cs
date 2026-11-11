using System.IO;
using System.Reflection;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;

internal static class ColdStartupTests
{
    internal static void Run(Action<string, Action> check, string directory)
    {
        Directory.CreateDirectory(directory);
        check("Opening an image and populating OCR languages does not load OCR assets", () =>
        {
            Require(!AssetsLoaded());
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var path = Path.Combine(directory, "startup attēls.png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            var editor = ImportedImageService.OpenEditor(path);
            try { editor.WaitForImage(); Require(editor.IsVisible && !AssetsLoaded()); }
            finally { editor.CloseForTest(); }
        });
        check("Closing before idle cancels capture warmup without recreating native overlays", () =>
        {
            ClearOverlays();
            var launcher = CreateLauncher();
            launcher.EnableBackgroundMode();
            Require(Overlays() is null && !launcher.IsVisible);
            Require(launcher.ExitBackgroundMode());
            DrainIdle();
            Require(Overlays() is null);
        });
        check("Idle startup prepares hidden capture overlays without loading OCR assets", () =>
        {
            ClearOverlays();
            var launcher = CreateLauncher();
            try
            {
                launcher.EnableBackgroundMode();
                Require(Overlays() is null);
                DrainIdle();
                Require(Overlays() is { Count: > 0 } overlays && overlays.All(window => !window.Visible));
                Require(!launcher.IsVisible && !AssetsLoaded());
            }
            finally { launcher.ExitBackgroundMode(); ClearOverlays(); }
        });

        MainWindow CreateLauncher() => new(new CaptureShortcutSettingsStore(Path.Combine(directory, "shortcuts.json")));
    }

    private static bool AssetsLoaded() => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "BetterScreenShot.OcrAssets");
    private static List<MonitorSelectionOverlayForm>? Overlays() =>
        (List<MonitorSelectionOverlayForm>?)typeof(MonitorSelectionOverlayForm).GetField("cachedOverlays", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
    private static void ClearOverlays() => typeof(MonitorSelectionOverlayForm)
        .GetMethod("DisposeCachedOverlays", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    private static void DrainIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.SystemIdle);
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Cold startup assertion failed.");
    }
}
