
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
    }
}
