
namespace BetterScreenShot
{
    internal static class CaptureGeometry
    {
        public static Rectangle ToScreenRectangle(Rectangle selection, Size clientSize, Rectangle screenBounds)
        {
            if (clientSize.Width <= 0 || clientSize.Height <= 0 || screenBounds.Width <= 0 || screenBounds.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clientSize), "Capture dimensions must be positive.");
            }

            var scaleX = screenBounds.Width / (double)clientSize.Width;
            var scaleY = screenBounds.Height / (double)clientSize.Height;
            var left = Math.Clamp((int)Math.Round(selection.Left * scaleX), 0, screenBounds.Width - 1);
            var top = Math.Clamp((int)Math.Round(selection.Top * scaleY), 0, screenBounds.Height - 1);
            var right = Math.Clamp((int)Math.Round(selection.Right * scaleX), left + 1, screenBounds.Width);
            var bottom = Math.Clamp((int)Math.Round(selection.Bottom * scaleY), top + 1, screenBounds.Height);

            return new Rectangle(screenBounds.Left + left, screenBounds.Top + top, right - left, bottom - top);
        }

        public static Point ToastPosition(Rectangle workArea, Size windowSize, int margin)
        {
            return new Point(
                Math.Max(workArea.Left + margin, workArea.Right - windowSize.Width - margin),
                Math.Max(workArea.Top + margin, workArea.Bottom - windowSize.Height - margin));
        }

        public static Point FloatingControlsPosition(Rectangle workArea, Rectangle captureBounds, Size windowSize, int margin)
        {
            var left = Math.Clamp(captureBounds.Left, workArea.Left + margin, Math.Max(workArea.Left + margin, workArea.Right - windowSize.Width - margin));
            var top = Math.Clamp(captureBounds.Top, workArea.Top + margin, Math.Max(workArea.Top + margin, workArea.Bottom - windowSize.Height - margin));
            var positions = new[]
            {
                new Point(captureBounds.Right + margin, top),
                new Point(captureBounds.Left - windowSize.Width - margin, top),
                new Point(left, captureBounds.Bottom + margin),
                new Point(left, captureBounds.Top - windowSize.Height - margin)
            };
            foreach (var position in positions)
            {
                if (position.X >= workArea.Left + margin && position.Y >= workArea.Top + margin
                    && position.X + windowSize.Width <= workArea.Right - margin
                    && position.Y + windowSize.Height <= workArea.Bottom - margin)
                    return position;
            }
            return new Point(left, top);
        }
    }
}
