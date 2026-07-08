using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal sealed class MonitorSelectionOverlayForm : Forms.Form
    {
        private const byte OverlayAlpha = 115;
        private const int EnumCurrentSettings = -1;
        private const int LwaAlpha = 0x2;
        private static readonly object OverlaySync = new();
        private static List<MonitorSelectionOverlayForm>? cachedOverlays;

        private readonly Rectangle overlayBounds;
        private readonly Rectangle captureBounds;
        private readonly string deviceName;
        private readonly Forms.Label instructionLabel;
        private System.Drawing.Point? dragStart;
        private System.Drawing.Point? dragCurrent;
        private bool suppressComplete;
        private Action<SelectedCaptureResult?>? completeSelection;

        private MonitorSelectionOverlayForm(string deviceName, Rectangle overlayBounds, Rectangle captureBounds)
        {
            this.deviceName = deviceName;
            this.overlayBounds = overlayBounds;
            this.captureBounds = captureBounds;

            SuspendLayout();
            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = Forms.FormStartPosition.Manual;
            TopMost = true;
            Bounds = overlayBounds;
            Cursor = Forms.Cursors.Cross;
            KeyPreview = true;
            AutoScaleMode = Forms.AutoScaleMode.None;
            BackColor = Color.Black;
            DoubleBuffered = true;
            SetStyle(
                Forms.ControlStyles.UserPaint |
                Forms.ControlStyles.AllPaintingInWmPaint |
                Forms.ControlStyles.OptimizedDoubleBuffer |
                Forms.ControlStyles.ResizeRedraw,
                true);
            UpdateStyles();
            ResetOverlayRegion();

            instructionLabel = new Forms.Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(180, 17, 17, 17),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Padding = new Forms.Padding(10, 6, 10, 6),
                Text = "Drag to select. Esc to cancel.",
                Location = new System.Drawing.Point(24, 24)
            };

            Controls.Add(instructionLabel);
            MouseDown += OverlayMouseDown;
            MouseMove += OverlayMouseMove;
            MouseUp += OverlayMouseUp;
            KeyDown += OverlayKeyDown;
            ResumeLayout(false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TOOLWINDOW = 0x00000080;

                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public static void WarmUpOverlays()
        {
            foreach (var overlay in GetOrCreateOverlays())
            {
                _ = overlay.Handle;
            }
        }

        public static SelectedCaptureResult? SelectArea(Action? onOverlaysShown = null)
        {
            var overlays = GetOrCreateOverlays();
            var frame = new DispatcherFrame();
            SelectedCaptureResult? selectedCapture = null;
            var completed = false;

            void Complete(SelectedCaptureResult? capture)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                selectedCapture = capture;

                foreach (var overlay in overlays)
                {
                    overlay.EndSelectionSession();
                }

                frame.Continue = false;
            }

            foreach (var overlay in overlays)
            {
                overlay.BeginSelectionSession(Complete);
                overlay.Show();
                overlay.BringInputToFront();
            }

            Forms.Application.DoEvents();
            Thread.Sleep(15);
            Forms.Application.DoEvents();
            onOverlaysShown?.Invoke();

            Dispatcher.PushFrame(frame);
            return selectedCapture;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLayeredAlpha();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayeredAlpha();
            BringInputToFront();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Focus();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateOverlayRegion(GetSelectionRect());
        }

        protected override void OnFormClosed(Forms.FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            if (!suppressComplete)
            {
                completeSelection?.Invoke(null);
            }
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);

            var selection = GetSelectionRect();
            if (selection is null || selection.Value.Width < 2 || selection.Value.Height < 2)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var borderPen = new Pen(Color.FromArgb(92, 200, 255), 2);
            var drawRect = Rectangle.Inflate(selection.Value, -1, -1);
            if (drawRect.Width > 0 && drawRect.Height > 0)
            {
                e.Graphics.DrawRectangle(borderPen, drawRect);
            }
        }

        protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
        {
            if (keyData == Forms.Keys.Escape)
            {
                Capture = false;
                completeSelection?.Invoke(null);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BeginSelectionSession(Action<SelectedCaptureResult?> completion)
        {
            suppressComplete = false;
            completeSelection = completion;
            dragStart = null;
            dragCurrent = null;
            instructionLabel.Visible = true;
            Bounds = overlayBounds;
            ResetOverlayRegion();
            Invalidate();
        }

        private void EndSelectionSession()
        {
            suppressComplete = true;
            Capture = false;
            dragStart = null;
            dragCurrent = null;
            completeSelection = null;
            ResetOverlayRegion();
            Hide();
        }

        private void OverlayMouseDown(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button != Forms.MouseButtons.Left)
            {
                return;
            }

            dragStart = e.Location;
            dragCurrent = e.Location;
            Capture = true;
            instructionLabel.Visible = false;
            UpdateSelectionPresentation(null, GetSelectionRect());
        }

        private void OverlayMouseMove(object? sender, Forms.MouseEventArgs e)
        {
            if (dragStart is null || (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == 0)
            {
                return;
            }

            var previousSelection = GetSelectionRect();
            dragCurrent = e.Location;
            UpdateSelectionPresentation(previousSelection, GetSelectionRect());
        }

        private void OverlayMouseUp(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button != Forms.MouseButtons.Left || dragStart is null)
            {
                return;
            }

            dragCurrent = e.Location;
            var overlaySelection = GetSelectionRect();
            Capture = false;
            UpdateSelectionPresentation(overlaySelection, null);

            if (overlaySelection is null || overlaySelection.Value.Width < 2 || overlaySelection.Value.Height < 2)
            {
                completeSelection?.Invoke(null);
                return;
            }

            var captureSelection = ConvertOverlayRectToCaptureRect(overlaySelection.Value);
            var screenRect = new Rectangle(
                captureBounds.Left + captureSelection.Left,
                captureBounds.Top + captureSelection.Top,
                captureSelection.Width,
                captureSelection.Height);

            HideAllOverlayForms();
            WaitForOverlayToDisappear();

            using var monitorBitmap = CaptureBitmap(captureBounds);
            var croppedBitmap = monitorBitmap.Clone(captureSelection, PixelFormat.Format32bppArgb);
            completeSelection?.Invoke(new SelectedCaptureResult(croppedBitmap, screenRect));
        }

        private void OverlayKeyDown(object? sender, Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Forms.Keys.Escape)
            {
                Capture = false;
                completeSelection?.Invoke(null);
            }
        }

        private void BringInputToFront()
        {
            Activate();
            BringToFront();
            Focus();
        }

        private Rectangle? GetSelectionRect()
        {
            if (dragStart is null || dragCurrent is null)
            {
                return null;
            }

            var left = Math.Min(dragStart.Value.X, dragCurrent.Value.X);
            var top = Math.Min(dragStart.Value.Y, dragCurrent.Value.Y);
            var right = Math.Max(dragStart.Value.X, dragCurrent.Value.X);
            var bottom = Math.Max(dragStart.Value.Y, dragCurrent.Value.Y);

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Rectangle ConvertOverlayRectToCaptureRect(Rectangle overlayRect)
        {
            var scaleX = captureBounds.Width / (double)Math.Max(1, ClientSize.Width);
            var scaleY = captureBounds.Height / (double)Math.Max(1, ClientSize.Height);

            var left = Math.Clamp((int)Math.Round(overlayRect.Left * scaleX), 0, Math.Max(0, captureBounds.Width - 1));
            var top = Math.Clamp((int)Math.Round(overlayRect.Top * scaleY), 0, Math.Max(0, captureBounds.Height - 1));
            var right = Math.Clamp((int)Math.Round(overlayRect.Right * scaleX), left + 1, captureBounds.Width);
            var bottom = Math.Clamp((int)Math.Round(overlayRect.Bottom * scaleY), top + 1, captureBounds.Height);

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static void HideAllOverlayForms()
        {
            foreach (var overlay in cachedOverlays ?? Enumerable.Empty<MonitorSelectionOverlayForm>())
            {
                overlay.ResetOverlayRegion();
                overlay.Hide();
            }
        }

        private static void WaitForOverlayToDisappear()
        {
            Forms.Application.DoEvents();
            Thread.Sleep(60);
            Forms.Application.DoEvents();
        }

        private void UpdateSelectionPresentation(Rectangle? previousSelection, Rectangle? currentSelection)
        {
            UpdateOverlayRegion(currentSelection);
            InvalidateSelection(previousSelection, currentSelection);
        }

        private void UpdateOverlayRegion(Rectangle? selection)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            var region = new Region(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            if (selection is not null && selection.Value.Width >= 2 && selection.Value.Height >= 2)
            {
                var hole = Rectangle.Inflate(selection.Value, -2, -2);
                if (hole.Width > 0 && hole.Height > 0)
                {
                    region.Exclude(hole);
                }
            }

            var oldRegion = Region;
            Region = region;
            oldRegion?.Dispose();
        }

        private void ResetOverlayRegion()
        {
            UpdateOverlayRegion(null);
        }

        private void ApplyLayeredAlpha()
        {
            if (IsHandleCreated)
            {
                SetLayeredWindowAttributes(Handle, 0, OverlayAlpha, LwaAlpha);
            }
        }

        private static List<MonitorSelectionOverlayForm> GetOrCreateOverlays()
        {
            lock (OverlaySync)
            {
                if (cachedOverlays is not null && MatchesCurrentScreens(cachedOverlays))
                {
                    return cachedOverlays;
                }

                DisposeCachedOverlays();
                cachedOverlays = Forms.Screen.AllScreens
                    .Select(screen => new MonitorSelectionOverlayForm(screen.DeviceName, screen.Bounds, GetMonitorBounds(screen)))
                    .ToList();

                return cachedOverlays;
            }
        }

        private static void DisposeCachedOverlays()
        {
            if (cachedOverlays is null)
            {
                return;
            }

            foreach (var overlay in cachedOverlays)
            {
                overlay.suppressComplete = true;
                overlay.Close();
                overlay.Dispose();
            }
        }

        private static bool MatchesCurrentScreens(IReadOnlyList<MonitorSelectionOverlayForm> overlays)
        {
            var screens = Forms.Screen.AllScreens;
            if (screens.Length != overlays.Count)
            {
                return false;
            }

            foreach (var screen in screens)
            {
                var overlay = overlays.FirstOrDefault(item => item.deviceName == screen.DeviceName);
                if (overlay is null)
                {
                    return false;
                }

                if (overlay.overlayBounds != screen.Bounds || overlay.captureBounds != GetMonitorBounds(screen))
                {
                    return false;
                }
            }

            return true;
        }

        private void InvalidateSelection(Rectangle? previousSelection, Rectangle? currentSelection)
        {
            var invalidRect = Rectangle.Empty;

            if (previousSelection is not null)
            {
                invalidRect = InflateForBorder(previousSelection.Value);
            }

            if (currentSelection is not null)
            {
                invalidRect = invalidRect.IsEmpty
                    ? InflateForBorder(currentSelection.Value)
                    : Rectangle.Union(invalidRect, InflateForBorder(currentSelection.Value));
            }

            if (invalidRect.IsEmpty)
            {
                Invalidate();
            }
            else
            {
                Invalidate(invalidRect);
            }
        }

        private static Rectangle InflateForBorder(Rectangle rect)
        {
            return Rectangle.Inflate(rect, 6, 6);
        }

        private static Rectangle GetMonitorBounds(Forms.Screen screen)
        {
            if (TryGetNativeMonitorBounds(screen.DeviceName, out var nativeBounds))
            {
                return nativeBounds;
            }

            return screen.Bounds;
        }

        private static bool TryGetNativeMonitorBounds(string deviceName, out Rectangle bounds)
        {
            var devMode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
            {
                bounds = default;
                return false;
            }

            bounds = new Rectangle(devMode.dmPositionX, devMode.dmPositionY, devMode.dmPelsWidth, devMode.dmPelsHeight);
            return true;
        }

        private static Bitmap CaptureBitmap(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            return bitmap;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            private const int DeviceNameLength = 32;
            private const int FormNameLength = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameLength)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FormNameLength)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
