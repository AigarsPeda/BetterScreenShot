using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace BetterScreenShot;

internal sealed class WindowsSnipShortcutState
{
    private bool sDown;
    private bool consumingS;

    internal bool Process(int virtualKey, bool down, bool chordHeld, out bool capture)
    {
        capture = false;
        if (virtualKey != 0x53) return false;
        if (consumingS)
        {
            if (!down) { consumingS = false; sDown = false; }
            return true;
        }
        if (!down) { sDown = false; return false; }
        if (sDown) return false;
        sDown = true;
        if (!chordHeld) return false;
        consumingS = true;
        capture = true;
        return true;
    }
}

internal sealed class WindowsSnipShortcutHook : IDisposable
{
    internal static readonly CaptureShortcut Shortcut = new(Key.S, ModifierKeys.Shift | ModifierKeys.Windows);
    private readonly WindowsSnipShortcutState state = new();
    private readonly HookProc callback;
    private readonly Dispatcher dispatcher;
    private readonly Action capture;
    private IntPtr hook;

    internal WindowsSnipShortcutHook(Dispatcher dispatcher, Action capture)
    {
        this.dispatcher = dispatcher;
        this.capture = capture;
        callback = OnKeyboard;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(hook, code, message, data);
        var type = message.ToInt32();
        if (type is not (0x0100 or 0x0101 or 0x0104 or 0x0105))
            return CallNextHookEx(hook, code, message, data);
        var key = Marshal.ReadInt32(data);
        var down = type is 0x0100 or 0x0104;
        var chordHeld = IsDown(0x10) && (IsDown(0x5B) || IsDown(0x5C)) && !IsDown(0x11) && !IsDown(0x12);
        if (!state.Process(key, down, chordHeld, out var shouldCapture))
            return CallNextHookEx(hook, code, message, data);
        if (shouldCapture) dispatcher.BeginInvoke(capture);
        return new IntPtr(1);
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
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
    private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
