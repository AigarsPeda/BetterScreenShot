using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterScreenShot;

internal sealed class InstallationWindow : Window
{
    private readonly TextBlock status = new() { Text = "Preparing installation…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) };

    internal InstallationWindow()
    {
        Title = "BetterScreenShot setup";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;
        ThemeIconService.ApplyViewerTheme(Resources, ThemePreferenceService.CurrentTheme);
        Background = (System.Windows.Media.Brush)Resources["ViewerPageBackgroundBrush"];
        Foreground = (System.Windows.Media.Brush)Resources["ViewerPrimaryTextBrush"];
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Installing BetterScreenShot {UpdateService.CurrentVersion.ToString(3)}", FontSize = 17, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(status);
        panel.Children.Add(new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Height = 4,
            Foreground = (System.Windows.Media.Brush)Resources["ViewerAccentBrush"] });
        Content = panel;
    }

    internal void SetStatus(string text) => status.Text = text;
}
