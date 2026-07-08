using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace BetterScreenShot
{
    public partial class SelectionOverlayWindow : Window
    {
        private const double CoveragePaddingDip = 1;
        private Point? dragStart;
        private readonly Rectangle pixelBounds;
        private readonly Action<Rectangle?> completeSelection;
        private double overlayWidthDip;
        private double overlayHeightDip;
        private Matrix transformFromDevice;
        private Matrix transformToDevice;

        public SelectionOverlayWindow(Forms.Screen screen, Action<Rectangle?> completeSelection)
        {
            InitializeComponent();

            this.completeSelection = completeSelection;
            pixelBounds = screen.Bounds;

            SourceInitialized += (_, _) => ApplyMonitorBounds();
        }

        public static Rectangle? SelectArea()
        {
            var overlays = new List<SelectionOverlayWindow>();
            var frame = new DispatcherFrame();
            Rectangle? selectedArea = null;
            var completed = false;

            void Complete(Rectangle? area)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                selectedArea = area;

                foreach (var overlay in overlays.Where(window => window.IsVisible))
                {
                    overlay.Close();
                }

                frame.Continue = false;
            }

            overlays.AddRange(Forms.Screen.AllScreens.Select(screen => new SelectionOverlayWindow(screen, Complete)));

            foreach (var overlay in overlays)
            {
                overlay.Show();
            }

            Dispatcher.PushFrame(frame);
            return selectedArea;
        }

        private void ApplyMonitorBounds()
        {
            var source = PresentationSource.FromVisual(this);

            if (source?.CompositionTarget is null)
            {
                return;
            }

            transformFromDevice = source.CompositionTarget.TransformFromDevice;
            transformToDevice = source.CompositionTarget.TransformToDevice;

            var topLeftDip = transformFromDevice.Transform(new Point(pixelBounds.Left, pixelBounds.Top));
            var bottomRightDip = transformFromDevice.Transform(new Point(pixelBounds.Right, pixelBounds.Bottom));

            Left = Math.Floor(topLeftDip.X) - CoveragePaddingDip;
            Top = Math.Floor(topLeftDip.Y) - CoveragePaddingDip;
            Width = Math.Ceiling(bottomRightDip.X - topLeftDip.X) + (CoveragePaddingDip * 2);
            Height = Math.Ceiling(bottomRightDip.Y - topLeftDip.Y) + (CoveragePaddingDip * 2);

            overlayWidthDip = Width;
            overlayHeightDip = Height;
            InitializeMask();
        }

        private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStart = e.GetPosition(OverlayCanvas);
            SelectionRectangle.Visibility = Visibility.Visible;
            InstructionBadge.Visibility = Visibility.Collapsed;
            UpdateSelection(dragStart.Value, dragStart.Value);
            OverlayCanvas.CaptureMouse();
        }

        private void OverlayCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (dragStart is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            UpdateSelection(dragStart.Value, e.GetPosition(OverlayCanvas));
        }

        private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (dragStart is null)
            {
                return;
            }

            var endPoint = e.GetPosition(OverlayCanvas);
            OverlayCanvas.ReleaseMouseCapture();
            UpdateSelection(dragStart.Value, endPoint);

            var leftDip = Math.Min(dragStart.Value.X, endPoint.X);
            var topDip = Math.Min(dragStart.Value.Y, endPoint.Y);
            var widthDip = Math.Abs(endPoint.X - dragStart.Value.X);
            var heightDip = Math.Abs(endPoint.Y - dragStart.Value.Y);

            dragStart = null;

            var topLeftPx = transformToDevice.Transform(new Point(leftDip, topDip));
            var bottomRightPx = transformToDevice.Transform(new Point(leftDip + widthDip, topDip + heightDip));

            var leftPx = (int)Math.Round(topLeftPx.X + pixelBounds.Left - CoveragePaddingDip);
            var topPx = (int)Math.Round(topLeftPx.Y + pixelBounds.Top - CoveragePaddingDip);
            var rightPx = (int)Math.Round(bottomRightPx.X + pixelBounds.Left - CoveragePaddingDip);
            var bottomPx = (int)Math.Round(bottomRightPx.Y + pixelBounds.Top - CoveragePaddingDip);
            var widthPx = rightPx - leftPx;
            var heightPx = bottomPx - topPx;

            if (widthPx < 2 || heightPx < 2)
            {
                completeSelection(null);
                return;
            }

            completeSelection(new Rectangle(leftPx, topPx, widthPx, heightPx));
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                completeSelection(null);
            }
        }

        private void InitializeMask()
        {
            TopMask.Width = overlayWidthDip;
            TopMask.Height = overlayHeightDip;

            LeftMask.Width = 0;
            LeftMask.Height = 0;
            RightMask.Width = 0;
            RightMask.Height = 0;
            BottomMask.Width = 0;
            BottomMask.Height = 0;
        }

        private void UpdateSelection(Point startPoint, Point endPoint)
        {
            var left = Math.Min(startPoint.X, endPoint.X);
            var top = Math.Min(startPoint.Y, endPoint.Y);
            var width = Math.Abs(endPoint.X - startPoint.X);
            var height = Math.Abs(endPoint.Y - startPoint.Y);
            var right = left + width;
            var bottom = top + height;

            Canvas.SetLeft(SelectionRectangle, left);
            Canvas.SetTop(SelectionRectangle, top);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;

            Canvas.SetLeft(TopMask, 0);
            Canvas.SetTop(TopMask, 0);
            TopMask.Width = overlayWidthDip;
            TopMask.Height = top;

            Canvas.SetLeft(LeftMask, 0);
            Canvas.SetTop(LeftMask, top);
            LeftMask.Width = left;
            LeftMask.Height = height;

            Canvas.SetLeft(RightMask, right);
            Canvas.SetTop(RightMask, top);
            RightMask.Width = Math.Max(0, overlayWidthDip - right);
            RightMask.Height = height;

            Canvas.SetLeft(BottomMask, 0);
            Canvas.SetTop(BottomMask, bottom);
            BottomMask.Width = overlayWidthDip;
            BottomMask.Height = Math.Max(0, overlayHeightDip - bottom);
        }
    }
}
