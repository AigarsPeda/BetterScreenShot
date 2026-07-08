using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace BetterScreenShot
{
    public partial class SelectionOverlayWindow : Window
    {
        private Point? dragStart;
        private readonly int screenLeft;
        private readonly int screenTop;
        private readonly int screenWidth;
        private readonly int screenHeight;
        private readonly Action<Rectangle?> completeSelection;

        public SelectionOverlayWindow(Forms.Screen screen, Action<Rectangle?> completeSelection)
        {
            InitializeComponent();

            this.completeSelection = completeSelection;

            var bounds = screen.Bounds;
            screenLeft = bounds.Left;
            screenTop = bounds.Top;
            screenWidth = bounds.Width;
            screenHeight = bounds.Height;

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            InitializeMask();
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

            var left = Math.Min(dragStart.Value.X, endPoint.X);
            var top = Math.Min(dragStart.Value.Y, endPoint.Y);
            var width = Math.Abs(endPoint.X - dragStart.Value.X);
            var height = Math.Abs(endPoint.Y - dragStart.Value.Y);

            dragStart = null;

            if (width < 2 || height < 2)
            {
                completeSelection(null);
                return;
            }

            completeSelection(new Rectangle(
                (int)Math.Round(left + screenLeft),
                (int)Math.Round(top + screenTop),
                (int)Math.Round(width),
                (int)Math.Round(height)));
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
            TopMask.Width = screenWidth;
            TopMask.Height = screenHeight;

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
            TopMask.Width = screenWidth;
            TopMask.Height = top;

            Canvas.SetLeft(LeftMask, 0);
            Canvas.SetTop(LeftMask, top);
            LeftMask.Width = left;
            LeftMask.Height = height;

            Canvas.SetLeft(RightMask, right);
            Canvas.SetTop(RightMask, top);
            RightMask.Width = Math.Max(0, screenWidth - right);
            RightMask.Height = height;

            Canvas.SetLeft(BottomMask, 0);
            Canvas.SetTop(BottomMask, bottom);
            BottomMask.Width = screenWidth;
            BottomMask.Height = Math.Max(0, screenHeight - bottom);
        }
    }
}
