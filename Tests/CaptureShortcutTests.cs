using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterScreenShot;

internal static class CaptureShortcutTests
{
    private static readonly CaptureShortcutSettings Example = new()
    {
        FullScreen = new CaptureShortcut(Key.D1, ModifierKeys.Control | ModifierKeys.Shift),
        Area = new CaptureShortcut(Key.D2, ModifierKeys.Control | ModifierKeys.Shift)
    };

    public static void Run(Action<string, Action> check, string directory)
    {
        check("Shortcut validation rejects duplicates, reserved keys and accidental typing shortcuts", () =>
        {
            Require(Example.ValidationError() is null && Example.FullScreen.DisplayText == "Ctrl+Shift+1");
            Require(CaptureShortcutSettings.Disabled.ValidationError() is null);
            Require(new CaptureShortcut(Key.A).ValidationError() is not null);
            Require(new CaptureShortcut(Key.A, ModifierKeys.Shift).ValidationError() is not null);
            Require(new CaptureShortcut(Key.F12, ModifierKeys.Control).ValidationError() is not null);
            Require(new CaptureShortcut(Key.LeftCtrl, ModifierKeys.Control).ValidationError() is not null);
            Require(new CaptureShortcut(Key.F8).ValidationError() is null && new CaptureShortcut(Key.Snapshot).DisplayText == "Print Screen");
            Require((Example with { Area = Example.FullScreen }).ValidationError() is not null);
        });
        check("Shortcut settings survive restart and invalid files fall back to unassigned", () =>
        {
            var path = Path.Combine(directory, "shortcuts", "settings.json");
            var store = new CaptureShortcutSettingsStore(path);
            Require(store.Load(out var error) == CaptureShortcutSettings.Disabled && error is null);
            store.Save(Example);
            Require(new CaptureShortcutSettingsStore(path).Load(out error) == Example && error is null);
            File.WriteAllText(path, "{\"fullScreen\":null}");
            Require(store.Load(out error) == CaptureShortcutSettings.Disabled && error is not null);
            File.WriteAllText(path, "not json");
            Require(store.Load(out error) == CaptureShortcutSettings.Disabled && error is not null);
        });
        check("Failed shortcut saves preserve the previous settings and remove staging files", () =>
        {
            var path = Path.Combine(directory, "locked-shortcuts.json");
            var store = new CaptureShortcutSettingsStore(path);
            store.Save(Example);
            var failed = false;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { store.Save(CaptureShortcutSettings.Disabled); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            }
            Require(failed && store.Load(out _) == Example);
            Require(Directory.GetFiles(directory, "locked-shortcuts.json.*.tmp").Length == 0);
        });
        check("Conflicting hotkeys restore working bindings and stale messages are ignored", () =>
        {
            using var source = CreateSource();
            var registered = new Dictionary<int, CaptureShortcut>();
            var calls = new List<bool>();
            using var shortcuts = new GlobalCaptureShortcuts(source, calls.Add,
                (id, shortcut) => { if (shortcut.Key == Key.K) return false; registered.Add(id, shortcut); return true; },
                id => registered.Remove(id));
            Require(shortcuts.TryApply(Example, out _));
            var conflict = Example with { FullScreen = new CaptureShortcut(Key.J, ModifierKeys.Control), Area = new CaptureShortcut(Key.K, ModifierKeys.Control) };
            Require(!shortcuts.TryApply(conflict, out var error) && error!.Contains("Select Area"));
            Require(shortcuts.Current == Example && registered.Count == 2 && registered[GlobalCaptureShortcuts.FullScreenId] == Example.FullScreen);
            SendHotkey(source, GlobalCaptureShortcuts.FullScreenId, Example.FullScreen);
            SendHotkey(source, GlobalCaptureShortcuts.AreaId, Example.Area);
            SendHotkey(source, GlobalCaptureShortcuts.AreaId, conflict.Area);
            Require(calls.SequenceEqual(new[] { false, true }));
            Require(shortcuts.TryApply(CaptureShortcutSettings.Disabled, out _) && registered.Count == 0);
            SendHotkey(source, GlobalCaptureShortcuts.AreaId, Example.Area);
            Require(calls.Count == 2);
        });
        check("Native global hotkeys register, dispatch on a hidden window and release on close", () =>
        {
            using var source = CreateSource();
            var calls = new List<bool>();
            var shortcuts = new GlobalCaptureShortcuts(source, calls.Add);
            var settings = CaptureShortcutSettings.Disabled;
            try
            {
                for (var key = Key.F13; key <= Key.F24; key++)
                {
                    var candidate = settings with { FullScreen = new CaptureShortcut(key, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift) };
                    if (shortcuts.TryApply(candidate, out _)) { settings = candidate; break; }
                }
                Require(settings.FullScreen.IsEnabled);
                SendHotkey(source, GlobalCaptureShortcuts.FullScreenId, settings.FullScreen);
                Require(calls.SequenceEqual(new[] { false }));
            }
            finally { shortcuts.Dispose(); }
            using var replacement = new GlobalCaptureShortcuts(source, _ => { });
            Require(replacement.TryApply(settings, out _));
        });
        check("Shortcut dialog shows duplicate and registration errors without saving changes", () =>
        {
            var saves = 0;
            var dialog = new CaptureShortcutsWindow(Example, _ => { saves++; return "Shortcut already in use."; });
            try
            {
                dialog.Show();
                var save = (System.Windows.Controls.Button)dialog.FindName("SaveShortcutsButton");
                var error = (System.Windows.Controls.TextBlock)dialog.FindName("ShortcutError");
                dialog.SetShortcut(true, Example.FullScreen);
                save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(saves == 0 && error.IsVisible && error.Text.Contains("different shortcuts"));
                dialog.SetShortcut(true, Example.Area);
                save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(saves == 1 && error.Text == "Shortcut already in use." && dialog.IsVisible);
                dialog.SetShortcut(false, new CaptureShortcut());
                Require(!dialog.Draft.FullScreen.IsEnabled && dialog.Draft.Area == Example.Area);
            }
            finally { dialog.Close(); }
            Require(Example.FullScreen.IsEnabled);
        });
    }

    public static int Demo()
    {
        var application = new System.Windows.Application();
        var folder = Path.Combine(Environment.CurrentDirectory, "obj", "shortcut-qa");
        var store = new CaptureShortcutSettingsStore(Path.Combine(folder, "shortcuts.json"));
        var window = new MainWindow(store) { Title = "Capture shortcuts - QA" };
        application.Run(window);
        return 0;
    }

    public static int Preview()
    {
        var application = new System.Windows.Application();
        var folder = Path.Combine(Environment.CurrentDirectory, "obj", "shortcut-preview");
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var window = new CaptureShortcutsWindow(Example, _ => null);
            ThemeIconService.ApplyViewerTheme(window.Resources, theme);
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(544, double.PositiveInfinity));
            content.Arrange(new Rect(new System.Windows.Size(544, content.DesiredSize.Height)));
            content.UpdateLayout();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle((System.Windows.Media.Brush)window.FindResource("ViewerPageBackgroundBrush"), null, new Rect(content.RenderSize));
                context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
            }
            var bitmap = new RenderTargetBitmap(544, (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(folder, $"shortcuts-{theme.ToString().ToLowerInvariant()}.png");
            using (var output = File.Create(path)) encoder.Save(output);
            Console.WriteLine(path);
            window.Close();
        }
        application.Shutdown();
        return 0;
    }

    private static HwndSource CreateSource() => new(new HwndSourceParameters("Shortcut regression fixture") { WindowStyle = 0 });
    private static void SendHotkey(HwndSource source, int id, CaptureShortcut shortcut) => SendMessage(source.Handle, 0x0312, new IntPtr(id),
        new IntPtr((KeyInterop.VirtualKeyFromKey(shortcut.Key) << 16) | (int)shortcut.Modifiers));
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Capture shortcut assertion failed.");
    }
}
