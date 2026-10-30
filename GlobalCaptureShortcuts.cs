using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace BetterScreenShot;

internal sealed class GlobalCaptureShortcuts : IDisposable
{
    internal const int FullScreenId = 0xB510;
    internal const int AreaId = 0xB511;
    private readonly HwndSource source;
    private readonly Action<bool> capture;
    private readonly Func<int, CaptureShortcut, bool> register;
    private readonly Action<int> unregister;
    private readonly Dictionary<int, CaptureShortcut> active = [];
    private bool disposed;
    internal CaptureShortcutSettings Current { get; private set; } = CaptureShortcutSettings.Disabled;

    public GlobalCaptureShortcuts(HwndSource source, Action<bool> capture,
        Func<int, CaptureShortcut, bool>? register = null, Action<int>? unregister = null)
    {
        this.source = source;
        this.capture = capture;
        this.register = register ?? ((id, shortcut) => RegisterHotKey(source.Handle, id, (uint)shortcut.Modifiers | 0x4000,
            (uint)KeyInterop.VirtualKeyFromKey(shortcut.Key)));
        this.unregister = unregister ?? (id => UnregisterHotKey(source.Handle, id));
        source.AddHook(WindowMessage);
    }

    public bool TryApply(CaptureShortcutSettings settings, out string? error)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        error = settings.ValidationError();
        if (error is not null) return false;
        if (settings == Current) return true;
        var previous = Current;
        UnregisterAll();
        if (TryRegister(settings, out error)) { Current = settings; return true; }
        UnregisterAll();
        if (!TryRegister(previous, out var restoreError))
        {
            UnregisterAll();
            Current = CaptureShortcutSettings.Disabled;
            error += $" Previous shortcuts could not be restored. {restoreError}";
        }
        return false;
    }

    private bool TryRegister(CaptureShortcutSettings settings, out string? error)
    {
        foreach (var (id, shortcut, label) in new[] { (FullScreenId, settings.FullScreen, "Capture Full Screen"), (AreaId, settings.Area, "Select Area") })
        {
            if (!shortcut.IsEnabled) continue;
            if (!register(id, shortcut))
            {
                error = $"{label}: {shortcut.DisplayText} is already in use or unavailable. Disable it in the other app or choose another shortcut."
                    + (shortcut.Modifiers.HasFlag(ModifierKeys.Windows) ? " Windows reserves some Win-key combinations." : "");
                return false;
            }
            active.Add(id, shortcut);
        }
        error = null;
        return true;
    }

    private void UnregisterAll()
    {
        foreach (var id in active.Keys) unregister(id);
        active.Clear();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312 || !active.TryGetValue(wParam.ToInt32(), out var shortcut)) return IntPtr.Zero;
        var data = unchecked((uint)lParam.ToInt64());
        if ((data & 0xFFFF) != (uint)shortcut.Modifiers || (data >> 16) != KeyInterop.VirtualKeyFromKey(shortcut.Key)) return IntPtr.Zero;
        handled = true;
        capture(wParam.ToInt32() == AreaId);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        UnregisterAll();
        if (!source.IsDisposed) source.RemoveHook(WindowMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
