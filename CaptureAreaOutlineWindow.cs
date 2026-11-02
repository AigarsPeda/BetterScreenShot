using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace BetterScreenShot;

internal sealed class CaptureAreaOutlineWindow : Window
{
    private const int ExtendedStyle = -20;
    private const int TransparentToMouse = 0x20;
    private const uint ExcludeFromCapture = 0x00000011;
    private readonly Rectangle bounds;

    internal bool CanExcludeFromCapture { get; private set; }
    internal bool IsClickThrough { get; private set; }

    internal CaptureAreaOutlineWindow(Rectangle bounds)
    {
        this.bounds = bounds;
        Width = Math.Max(1, bounds.Width);
        Height = Math.Max(1, bounds.Height);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ThemeIconService.ApplyViewerTheme(Resources, ThemePreferenceService.CurrentTheme);
        var outline = new Border
        {
            BorderThickness = new Thickness(2),
            Background = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false
        };
        outline.SetResourceReference(Border.BorderBrushProperty, "ViewerAccentBrush");
        Content = outline;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, ExtendedStyle);
            SetWindowLong(handle, ExtendedStyle, style | TransparentToMouse);
            IsClickThrough = (GetWindowLong(handle, ExtendedStyle) & TransparentToMouse) != 0;
            CanExcludeFromCapture = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                && SetWindowDisplayAffinity(handle, ExcludeFromCapture);
        };
        Loaded += (_, _) => DesktopWindowService.PositionCaptureOutline(this, bounds);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
