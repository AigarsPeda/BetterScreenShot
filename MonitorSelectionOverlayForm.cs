using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal sealed class MonitorSelectionOverlayForm : Forms.Form
    {
        private readonly Rectangle overlayBounds;
        private readonly Rectangle captureBounds;
        private readonly Action<SelectedCaptureResult?> completeSelection;
        private readonly string deviceName;
        private readonly Forms.Label instructionLabel;
        private System.Drawing.Point? dragStart;
        private System.Drawing.Point? dragCurrent;
        private bool suppressComplete;

        private MonitorSelectionOverlayForm(string deviceName, Rectangle overlayBounds, Rectangle captureBounds, Action<SelectedCaptureResult?> completeSelection)
        {
            this.deviceName = deviceName;
            this.overlayBounds = overlayBounds;
            this.captureBounds = captureBounds;
            this.completeSelection = completeSelection;

            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = Forms.FormStartPosition.Manual;
            TopMost = true;
            Bounds = overlayBounds;
            Cursor = Forms.Cursors.Cross;
            KeyPreview = true;
            AutoScaleMode = Forms.AutoScaleMode.None;
            BackColor = Color.Black;
            Opacity = 0.45;
            SetStyle(
                Forms.ControlStyles.UserPaint |
                Forms.ControlStyles.AllPaintingInWmPaint |
                Forms.ControlStyles.OptimizedDoubleBuffer |
                Forms.ControlStyles.ResizeRedraw,
                true);
            UpdateStyles();

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

            DebugLog($"Overlay created device={deviceName} overlayBounds={overlayBounds} captureBounds={captureBounds}");
        }

        public static SelectedCaptureResult? SelectArea()
        {
            var screens = Forms.Screen.AllScreens
                .Select(screen => new
                {
                    screen.DeviceName,
                    OverlayBounds = screen.Bounds,
                    CaptureBounds = GetMonitorBounds(screen)
                })
                .ToList();

            var overlays = new List<MonitorSelectionOverlayForm>();
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

                foreach (var overlay in overlays.ToList())
                {
                    overlay.suppressComplete = true;
                    if (!overlay.IsDisposed)
                    {
                        overlay.Close();
                    }
                }

                frame.Continue = false;
            }

            overlays.AddRange(screens.Select(screen => new MonitorSelectionOverlayForm(screen.DeviceName, screen.OverlayBounds, screen.CaptureBounds, Complete)));

            foreach (var overlay in overlays)
            {
                overlay.Show();
            }

            Dispatcher.PushFrame(frame);
            return selectedCapture;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BringInputToFront();
            DebugLog($"Overlay shown device={deviceName} overlayBounds={overlayBounds} captureBounds={captureBounds} windowPx={FormatRect(GetWindowRectPixels())} clientPx=({ClientSize.Width},{ClientSize.Height})");
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Focus();
        }

        protected override void OnFormClosed(Forms.FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            if (!suppressComplete)
            {
                completeSelection(null);
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

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            using (var selectionBrush = new SolidBrush(Color.FromArgb(70, Color.White)))
            {
                e.Graphics.FillRectangle(selectionBrush, selection.Value);
            }

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
                completeSelection(null);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
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
            InvalidateSelection(null, GetSelectionRect());

            DebugLog($"MouseDown device={deviceName} localOverlayPx={FormatPoint(dragStart.Value)} screenOverlayPx={FormatScreenPoint(dragStart.Value)} overlayBounds={overlayBounds} captureBounds={captureBounds}");
        }

        private void OverlayMouseMove(object? sender, Forms.MouseEventArgs e)
        {
            if (dragStart is null || (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == 0)
            {
                return;
            }

            var previousSelection = GetSelectionRect();
            dragCurrent = e.Location;
            InvalidateSelection(previousSelection, GetSelectionRect());
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
            InvalidateSelection(overlaySelection, null);

            if (overlaySelection is null || overlaySelection.Value.Width < 2 || overlaySelection.Value.Height < 2)
            {
                completeSelection(null);
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
            DebugLog($"MouseUp device={deviceName} overlayRectPx={overlaySelection.Value} captureRectPx={captureSelection} screenRectPx={screenRect}");
            SaveSelectionDebugArtifacts(monitorBitmap, overlaySelection.Value, captureSelection, screenRect, dragStart.Value, dragCurrent.Value);

            var croppedBitmap = monitorBitmap.Clone(captureSelection, PixelFormat.Format32bppArgb);
            completeSelection(new SelectedCaptureResult(croppedBitmap, screenRect));
        }

        private void OverlayKeyDown(object? sender, Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Forms.Keys.Escape)
            {
                Capture = false;
                completeSelection(null);
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

        private void HideAllOverlayForms()
        {
            foreach (var overlay in Forms.Application.OpenForms.OfType<MonitorSelectionOverlayForm>().ToList())
            {
                overlay.Hide();
            }
        }

        private static void WaitForOverlayToDisappear()
        {
            Forms.Application.DoEvents();
            Thread.Sleep(60);
            Forms.Application.DoEvents();
        }

        private void SaveSelectionDebugArtifacts(Bitmap monitorBitmap, Rectangle overlayRect, Rectangle captureRect, Rectangle screenRect, System.Drawing.Point startPoint, System.Drawing.Point endPoint)
        {
            try
            {
                var folder = GetDesktopDebugFolder();
                Directory.CreateDirectory(folder);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff");

                using (var annotatedBitmap = (Bitmap)monitorBitmap.Clone())
                using (var graphics = Graphics.FromImage(annotatedBitmap))
                using (var redPen = new Pen(Color.Red, 4))
                using (var greenPen = new Pen(Color.Lime, 2))
                using (var pointBrush = new SolidBrush(Color.Yellow))
                using (var font = new Font("Segoe UI", 18, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.Yellow))
                using (var textBackgroundBrush = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                {
                    graphics.DrawRectangle(redPen, captureRect);
                    graphics.DrawRectangle(greenPen, 0, 0, annotatedBitmap.Width - 1, annotatedBitmap.Height - 1);
                    DrawPointMarker(graphics, pointBrush, ConvertOverlayPointToCapturePoint(startPoint));
                    DrawPointMarker(graphics, pointBrush, ConvertOverlayPointToCapturePoint(endPoint));

                    var lines = new[]
                    {
                        $"device={deviceName}",
                        $"overlayBounds={overlayBounds}",
                        $"captureBounds={captureBounds}",
                        $"overlayRect={overlayRect}",
                        $"captureRect={captureRect}",
                        $"screenRect={screenRect}"
                    };

                    var y = 20f;
                    foreach (var line in lines)
                    {
                        var measured = graphics.MeasureString(line, font);
                        graphics.FillRectangle(textBackgroundBrush, 14, y - 2, measured.Width + 12, measured.Height + 4);
                        graphics.DrawString(line, font, textBrush, 20, y);
                        y += measured.Height + 6;
                    }

                    annotatedBitmap.Save(Path.Combine(folder, $"selection-debug-monitor-{stamp}.png"), ImageFormat.Png);
                }

                using var croppedBitmap = monitorBitmap.Clone(captureRect, PixelFormat.Format32bppArgb);
                croppedBitmap.Save(Path.Combine(folder, $"selection-debug-crop-{stamp}.png"), ImageFormat.Png);
                DebugLog($"Saved debug images device={deviceName} overlayRect={overlayRect} captureRect={captureRect} screenRect={screenRect} stamp={stamp} folder={folder}");
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to save debug images: {ex.Message}");
            }
        }

        private System.Drawing.Point ConvertOverlayPointToCapturePoint(System.Drawing.Point overlayPoint)
        {
            var scaleX = captureBounds.Width / (double)Math.Max(1, ClientSize.Width);
            var scaleY = captureBounds.Height / (double)Math.Max(1, ClientSize.Height);
            return new System.Drawing.Point(
                Math.Clamp((int)Math.Round(overlayPoint.X * scaleX), 0, Math.Max(0, captureBounds.Width - 1)),
                Math.Clamp((int)Math.Round(overlayPoint.Y * scaleY), 0, Math.Max(0, captureBounds.Height - 1)));
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
            return Rectangle.Inflate(rect, 4, 4);
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

        private NativeRect GetWindowRectPixels()
        {
            return GetWindowRect(Handle, out var rect)
                ? rect
                : new NativeRect { Left = overlayBounds.Left, Top = overlayBounds.Top, Right = overlayBounds.Right, Bottom = overlayBounds.Bottom };
        }

        private static void DrawPointMarker(Graphics graphics, Brush brush, System.Drawing.Point point)
        {
            const int size = 10;
            graphics.FillEllipse(brush, point.X - size / 2f, point.Y - size / 2f, size, size);
        }

        private static string FormatPoint(System.Drawing.Point point)
        {
            return $"({point.X},{point.Y})";
        }

        private string FormatScreenPoint(System.Drawing.Point localPoint)
        {
            return $"({overlayBounds.Left + localPoint.X},{overlayBounds.Top + localPoint.Y})";
        }

        private static string FormatRect(NativeRect rect)
        {
            return $"({rect.Left},{rect.Top},{rect.Width},{rect.Height})";
        }

        private static string GetDesktopDebugFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BetterScreenShotDebug");
        }

        private static void DebugLog(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);

            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "selection-debug.log"), line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private const int EnumCurrentSettings = -1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }
    }
}
