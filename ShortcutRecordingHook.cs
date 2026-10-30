using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace BetterScreenShot;

// Keeps interception active through key-up so a released chord cannot launch another app.
internal sealed class ShortcutRecordingState(IEnumerable<Key>? initialModifiers = null)
{
    private readonly HashSet<Key> modifiers = new(initialModifiers ?? []);
    private readonly HashSet<Key> intercepted = [];
    internal bool Listening { get; private set; } = true;
    internal bool Finished => !Listening && intercepted.Count == 0;
    internal void Stop() => Listening = false;

    internal bool Process(Key key, bool down, bool foreground, out CaptureShortcut? recorded)
    {
        recorded = null;
        if (CaptureShortcut.IsModifier(key))
        {
            if (down) modifiers.Add(key); else modifiers.Remove(key);
        }
        if (!foreground) Stop();
        if (intercepted.Contains(key))
        {
            if (!down) intercepted.Remove(key);
            return true;
        }
        if (!Listening || !down) return false;
        if (CaptureShortcut.IsModifier(key)) { intercepted.Add(key); return true; }

        var flags = ModifierKeys.None;
        foreach (var modifier in modifiers)
            flags |= modifier switch
            {
                Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
                Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
                Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
                Key.LWin or Key.RWin => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
        recorded = new CaptureShortcut(key, flags);
        if (key == Key.Tab && flags == ModifierKeys.None) { Stop(); return false; }
        intercepted.Add(key);
        if (key == Key.Escape || key is Key.Delete or Key.Back && flags == ModifierKeys.None
            || key != Key.None && recorded.ValidationError() is null) Stop();
        return true;
    }
}

internal sealed class ShortcutRecordingHook : IDisposable
{
    private readonly ShortcutRecordingState state;
    private readonly HookProc callback;
    private readonly Dispatcher dispatcher;
    private readonly IntPtr owner;
    private readonly Action<CaptureShortcut> recorded;
    private IntPtr hook;
    private bool disposed;

    internal ShortcutRecordingHook(IntPtr owner, Dispatcher dispatcher, Action<CaptureShortcut> recorded)
    {
        this.owner = owner;
        this.dispatcher = dispatcher;
        this.recorded = recorded;
        // Seed held modifiers before installing the hook. Subsequent state comes from events,
        // since GetAsyncKeyState has not been updated for the current hook event.
        var keys = new[] { Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
            Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin };
        state = new ShortcutRecordingState(keys.Where(key => (GetAsyncKeyState(KeyInterop.VirtualKeyFromKey(key)) & 0x8000) != 0));
        callback = OnKeyboard;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal void Stop()
    {
        state.Stop();
        if (state.Finished) Dispose();
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || disposed) return CallNextHookEx(hook, code, message, data);
        var type = message.ToInt32();
        if (type is not (0x0100 or 0x0101 or 0x0104 or 0x0105)) return CallNextHookEx(hook, code, message, data);
        var key = KeyInterop.KeyFromVirtualKey(Marshal.ReadInt32(data));
        var consume = state.Process(key, type is 0x0100 or 0x0104, GetForegroundWindow() == owner, out var shortcut);
        // Keep the hook fast. Validation messages and WPF updates run after it returns.
        if (shortcut is not null) dispatcher.BeginInvoke(new Action(() => { if (!disposed) recorded(shortcut); }));
        if (state.Finished) dispatcher.BeginInvoke(new Action(Dispose));
        return consume ? new IntPtr(1) : CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
