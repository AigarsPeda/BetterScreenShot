using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace BetterScreenShot
{
    internal enum AppDialogKind
    {
        DeleteConfirmation,
        Success,
        Error
    }

    public partial class AppDialog : Window
    {
        internal AppDialog(Window owner, string title, string message, AppDialogKind kind, string? details = null)
        {
            InitializeComponent();
            Owner = owner is ScreenshotToastWindow && System.Windows.Application.Current.MainWindow is MainWindow { IsVisible: true } mainWindow
                ? mainWindow
                : owner;
            Title = title;
            DialogTitle.Text = title;
            MessageText.Text = message;
            ApplyThemeFromSystem();

            var isConfirmation = kind == AppDialogKind.DeleteConfirmation;
            var isSuccess = kind == AppDialogKind.Success;
            var accent = isSuccess ? "#10B981" : "#EF4444";
            Resources["DialogAccentBrush"] = (SolidColorBrush)new BrushConverter().ConvertFromString(accent)!;
            Resources["DialogAccentBackgroundBrush"] = (SolidColorBrush)new BrushConverter().ConvertFromString(isSuccess ? "#1810B981" : "#18EF4444")!;
            DialogIcon.Data = Geometry.Parse(kind switch
            {
                AppDialogKind.DeleteConfirmation => "M10 11v6 M14 11v6 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
                AppDialogKind.Success => "M22 11.08V12a10 10 0 1 1-5.93-9.14 M22 4 12 14.01 9 11.01",
                _ => "M12 8v4 M12 16h.01 M22 12a10 10 0 1 1-20 0a10 10 0 0 1 20 0"
            });

            if (!isConfirmation)
            {
                CancelActionButton.Visibility = Visibility.Collapsed;
                CancelActionButton.IsDefault = false;
                CancelActionButton.IsCancel = false;
                PrimaryActionButton.Style = (Style)FindResource("DialogButtonStyle");
                PrimaryActionButton.Content = "_OK";
                PrimaryActionButton.IsDefault = true;
                PrimaryActionButton.IsCancel = true;
            }

            if (!string.IsNullOrWhiteSpace(details))
            {
                DetailsPanel.Visibility = Visibility.Visible;
                DetailsCaption.Text = isSuccess ? "Saved to" : "Details";
                DetailsText.Text = details;
            }

            Loaded += (_, _) => (isConfirmation ? CancelActionButton : PrimaryActionButton).Focus();
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            Closed += (_, _) => SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }

        internal static bool ConfirmDelete(Window owner)
        {
            return new AppDialog(owner, "Delete screenshot?",
                "The temporary screenshot will be deleted. Saved copies will stay on disk.",
                AppDialogKind.DeleteConfirmation).ShowDialog() == true;
        }

        internal static void ShowSaved(Window owner, string savedPath)
        {
            new AppDialog(owner, "Screenshot saved", "Your screenshot was saved successfully.", AppDialogKind.Success, savedPath).ShowDialog();
        }

        internal static void ShowError(Window owner, string title, string message, string details)
        {
            new AppDialog(owner, title, message, AppDialogKind.Error, details).ShowDialog();
        }

        private void ApplyThemeFromSystem() => ThemeIconService.ApplyViewerTheme(Resources, ThemeIconService.DetectWindowsTheme());

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            {
                Dispatcher.BeginInvoke(new Action(ApplyThemeFromSystem));
            }
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
