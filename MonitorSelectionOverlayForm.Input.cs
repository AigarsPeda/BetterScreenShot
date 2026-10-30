using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal sealed partial class MonitorSelectionOverlayForm
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hmod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private enum SelectionMode
        {
            None,
            Area,
            Monitor
        }

        private sealed class MonitorSelectionHooks : IDisposable
        {
            private readonly Action<MonitorSelectionOverlayForm?> completeSelection;
            private readonly LowLevelKeyboardProc keyboardProc;
            private IntPtr keyboardHook;
            private bool isDisposed;

            public MonitorSelectionHooks(Action<MonitorSelectionOverlayForm?> completeSelection)
            {
                this.completeSelection = completeSelection;
                keyboardProc = KeyboardHookCallback;
                InstallHooks();
            }

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;

                if (keyboardHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(keyboardHook);
                    keyboardHook = IntPtr.Zero;
                }
            }

            private void InstallHooks()
            {
                using var currentProcess = Process.GetCurrentProcess();
                using var currentModule = currentProcess.MainModule;
                var moduleHandle = GetModuleHandle(currentModule?.ModuleName);

                keyboardHook = SetWindowsHookEx(WhKeyboardLl, keyboardProc, moduleHandle, 0);
                if (keyboardHook == IntPtr.Zero)
                {
                    Dispose();
                    throw new InvalidOperationException("Unable to install monitor selection hooks.");
                }
            }

            private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode < 0)
                {
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                var message = unchecked((uint)wParam.ToInt64());
                if (message != WmKeyDown && message != WmSysKeyDown)
                {
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                var keyboardData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if (keyboardData.vkCode == (uint)Forms.Keys.Escape)
                {
                    completeSelection(null);
                    return new IntPtr(1);
                }

                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }
        }

        private sealed class MonitorSelectionInputForm : Forms.Form
        {
            private readonly MonitorSelectionOverlayForm overlay;
            private readonly IReadOnlyList<MonitorSelectionOverlayForm> overlays;
            private readonly Action<MonitorSelectionOverlayForm?> completeSelection;

            public MonitorSelectionInputForm(
                MonitorSelectionOverlayForm overlay,
                IReadOnlyList<MonitorSelectionOverlayForm> overlays,
                Action<MonitorSelectionOverlayForm?> completeSelection)
            {
                this.overlay = overlay;
                this.overlays = overlays;
                this.completeSelection = completeSelection;

                FormBorderStyle = Forms.FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = Forms.FormStartPosition.Manual;
                TopMost = true;
                Bounds = overlay.overlayBounds;
                BackColor = Color.Black;
                Cursor = MonitorSelectionCursor;

                MouseEnter += (_, _) => RefreshCursorAndHover();
                MouseMove += (_, _) => RefreshCursorAndHover();
                MouseDown += (_, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                    {
                        RefreshCursorAndHover();
                    }
                };
                MouseUp += (_, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                    {
                        completeSelection(overlay);
                    }
                };
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_LAYERED = 0x00080000;
                    const int WS_EX_TOOLWINDOW = 0x00000080;
                    const int WS_EX_NOACTIVATE = 0x08000000;

                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SetLayeredWindowAttributes(Handle, 0, MonitorInputAlpha, LwaAlpha);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                SetLayeredWindowAttributes(Handle, 0, MonitorInputAlpha, LwaAlpha);
                RefreshCursorAndHover();
            }

            private void RefreshCursorAndHover()
            {
                RefreshMonitorHoverState(overlays, Forms.Control.MousePosition);
                Cursor = MonitorSelectionCursor;
                ApplyMonitorSelectionCursor();
            }
        }

    }
}
