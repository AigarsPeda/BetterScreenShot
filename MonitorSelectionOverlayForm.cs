using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal sealed partial class MonitorSelectionOverlayForm : Forms.Form
    {
        private const byte OverlayAlpha = 115;
        private const byte MonitorInputAlpha = 1;
        private const int LwaAlpha = 0x2;
        private const int WhKeyboardLl = 13;
        private const uint WmKeyDown = 0x0100;
        private const uint WmSysKeyDown = 0x0104;
        private static readonly object OverlaySync = new();
        private static readonly Forms.Cursor MonitorSelectionCursor = CreateMonitorSelectionCursor();
        private static List<MonitorSelectionOverlayForm>? cachedOverlays;
        private static CaptureSelectionSession? activeSession;
        private static bool isShuttingDown;

        private readonly Rectangle overlayBounds;
        private readonly Rectangle captureBounds;
        private readonly string deviceName;
        private readonly Forms.Label instructionLabel;
        private System.Drawing.Point? dragStart;
        private System.Drawing.Point? dragCurrent;
        private bool suppressComplete;
        private bool isHoveringMonitor;
        private SelectionMode selectionMode;
        private Action<Rectangle?>? completeSelection;

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
            instructionLabel.MouseDown += InstructionLabel_MouseDown;
            instructionLabel.MouseMove += InstructionLabel_MouseMove;
            instructionLabel.MouseUp += InstructionLabel_MouseUp;
            instructionLabel.MouseEnter += InstructionLabel_MouseEnter;
            instructionLabel.MouseLeave += InstructionLabel_MouseLeave;
            MouseDown += OverlayMouseDown;
            MouseMove += OverlayMouseMove;
            MouseUp += OverlayMouseUp;
            MouseEnter += OverlayMouseEnter;
            MouseLeave += OverlayMouseLeave;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetOverlayAlpha(OverlayAlpha);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetOverlayAlpha(OverlayAlpha);
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

            if (selectionMode == SelectionMode.Monitor)
            {
                if (isHoveringMonitor)
                {
                    using var hoverPen = new Pen(Color.FromArgb(92, 220, 120), 3);
                    var hoverRect = new Rectangle(1, 1, Math.Max(0, ClientSize.Width - 3), Math.Max(0, ClientSize.Height - 3));
                    if (hoverRect.Width > 0 && hoverRect.Height > 0)
                    {
                        e.Graphics.DrawRectangle(hoverPen, hoverRect);
                    }
                }

                return;
            }

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
                CancelSelection();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BeginSelectionSession(SelectionMode mode, Action<Rectangle?> completion)
        {
            selectionMode = mode;
            var isArea = mode == SelectionMode.Area;
            Cursor = isArea ? Forms.Cursors.Cross : MonitorSelectionCursor;
            instructionLabel.Cursor = Cursor;
            instructionLabel.Text = isArea ? "Drag to select. Esc to cancel." : "Click a monitor to capture it. Esc to cancel.";
            suppressComplete = false;
            completeSelection = completion;
            dragStart = null;
            dragCurrent = null;
            isHoveringMonitor = false;
            instructionLabel.Visible = true;
            Bounds = overlayBounds;
            ResetOverlayRegion();
            SetOverlayAlpha(OverlayAlpha);
            Invalidate();
            if (!isArea)
            {
                ApplyMonitorSelectionCursor();
            }
        }

        private void EndSelectionSession()
        {
            suppressComplete = true;
            Capture = false;
            dragStart = null;
            dragCurrent = null;
            isHoveringMonitor = false;
            completeSelection = null;
            selectionMode = SelectionMode.None;
            Cursor = Forms.Cursors.Default;
            ResetOverlayRegion();
            SetOverlayAlpha(OverlayAlpha);
            RestoreDefaultCursor();
            Hide();
        }

        private void OverlayMouseDown(object? sender, Forms.MouseEventArgs e)
        {
            if (selectionMode != SelectionMode.Area || e.Button != Forms.MouseButtons.Left)
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
            if (selectionMode != SelectionMode.Area || dragStart is null || (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == 0)
            {
                return;
            }

            var previousSelection = GetSelectionRect();
            dragCurrent = e.Location;
            UpdateSelectionPresentation(previousSelection, GetSelectionRect());
        }

        private void OverlayMouseUp(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button != Forms.MouseButtons.Left)
            {
                return;
            }

            if (selectionMode == SelectionMode.Monitor)
            {
                completeSelection?.Invoke(captureBounds);
                return;
            }

            if (dragStart is null)
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

            try
            {
                var screenRect = CaptureGeometry.ToScreenRectangle(overlaySelection.Value, ClientSize, captureBounds);
                completeSelection?.Invoke(screenRect);
            }
            catch (Exception ex)
            {
                activeSession?.Fail(ex);
            }
        }

        private void OverlayMouseEnter(object? sender, EventArgs e)
        {
            if (selectionMode != SelectionMode.Monitor)
            {
                return;
            }

            RefreshMonitorHoverState(cachedOverlays ?? [], Forms.Control.MousePosition);
        }

        private void OverlayMouseLeave(object? sender, EventArgs e)
        {
            if (selectionMode != SelectionMode.Monitor)
            {
                return;
            }

            RefreshMonitorHoverState(cachedOverlays ?? [], Forms.Control.MousePosition);
        }

        private void OverlayKeyDown(object? sender, Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Forms.Keys.Escape)
            {
                CancelSelection();
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

        private void InstructionLabel_MouseDown(object? sender, Forms.MouseEventArgs e)
        {
            OverlayMouseDown(sender, TranslateLabelMouseEvent(e));
        }

        private void InstructionLabel_MouseMove(object? sender, Forms.MouseEventArgs e)
        {
            OverlayMouseMove(sender, TranslateLabelMouseEvent(e));
        }

        private void InstructionLabel_MouseUp(object? sender, Forms.MouseEventArgs e)
        {
            OverlayMouseUp(sender, TranslateLabelMouseEvent(e));
        }

        private void InstructionLabel_MouseEnter(object? sender, EventArgs e)
        {
            OverlayMouseEnter(sender, e);
        }

        private void InstructionLabel_MouseLeave(object? sender, EventArgs e)
        {
            OverlayMouseLeave(sender, e);
        }

        private Forms.MouseEventArgs TranslateLabelMouseEvent(Forms.MouseEventArgs e)
        {
            var screenPoint = instructionLabel.PointToScreen(e.Location);
            var clientPoint = PointToClient(screenPoint);
            return new Forms.MouseEventArgs(e.Button, e.Clicks, clientPoint.X, clientPoint.Y, e.Delta);
        }

        private void UpdateMonitorHoverState(bool isHovered)
        {
            if (selectionMode != SelectionMode.Monitor || isHoveringMonitor == isHovered)
            {
                return;
            }

            isHoveringMonitor = isHovered;
            UpdateOverlayRegion(null);
            Refresh();
        }

        private void CancelSelection()
        {
            Capture = false;
            RestoreDefaultCursor();
            completeSelection?.Invoke(null);
        }

        private static void RefreshMonitorHoverState(IReadOnlyList<MonitorSelectionOverlayForm> overlays, System.Drawing.Point cursorPosition)
        {
            var hoveredScreen = Forms.Screen.FromPoint(cursorPosition);
            var hoveredOverlay = overlays.FirstOrDefault(overlay => overlay.deviceName == hoveredScreen.DeviceName)
                ?? overlays.FirstOrDefault(overlay => overlay.Bounds.Contains(cursorPosition));

            foreach (var overlay in overlays)
            {
                overlay.UpdateMonitorHoverState(ReferenceEquals(overlay, hoveredOverlay));
            }
        }

        private static void ApplyMonitorSelectionCursor()
        {
            Forms.Cursor.Current = MonitorSelectionCursor;
            SetCursor(MonitorSelectionCursor.Handle);
        }

        private static void RestoreDefaultCursor()
        {
            Forms.Cursor.Current = Forms.Cursors.Default;
            SetCursor(IntPtr.Zero);
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
            if (selectionMode == SelectionMode.Monitor && isHoveringMonitor)
            {
                var hole = new Rectangle(2, 2, Math.Max(0, ClientSize.Width - 4), Math.Max(0, ClientSize.Height - 4));
                if (hole.Width > 0 && hole.Height > 0)
                {
                    region.Exclude(hole);
                }
            }
            else if (selection is not null && selection.Value.Width >= 2 && selection.Value.Height >= 2)
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

        private void SetOverlayAlpha(byte alpha)
        {
            if (IsHandleCreated)
            {
                SetLayeredWindowAttributes(Handle, 0, alpha, LwaAlpha);
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
                cachedOverlays = [.. Forms.Screen.AllScreens.Select(screen => new MonitorSelectionOverlayForm(screen.DeviceName, screen.Bounds, MonitorGeometry.GetBounds(screen)))];

                return cachedOverlays;
            }
        }

        public static void Shutdown()
        {
            if (isShuttingDown)
            {
                return;
            }

            isShuttingDown = true;
            activeSession?.Dispose();
            DisposeCachedOverlays();
            if (!ReferenceEquals(MonitorSelectionCursor, Forms.Cursors.Hand))
            {
                DestroyCursor(MonitorSelectionCursor.Handle);
            }
        }

        private static void DisposeCachedOverlays()
        {
            if (cachedOverlays is null)
            {
                return;
            }

            var overlays = cachedOverlays;
            cachedOverlays = null;
            foreach (var overlay in overlays)
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
                if (overlay is null || overlay.IsDisposed)
                {
                    return false;
                }

                if (overlay.overlayBounds != screen.Bounds || overlay.captureBounds != MonitorGeometry.GetBounds(screen))
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

    }
}
