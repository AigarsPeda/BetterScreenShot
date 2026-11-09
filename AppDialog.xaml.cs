using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace BetterScreenShot
{
    internal enum UnsavedChoice { KeepEditing, Discard, Save }
    internal enum AppDialogKind
    {
        DeleteConfirmation,
        Error
    }

    public partial class AppDialog : Window
    {
        private UnsavedChoice unsavedChoice;
        internal static UnsavedChoice ConfirmUnsaved(Window owner)
        {
            var dialog = CreateUnsavedDialog(owner);
            var result = dialog.ShowDialog();
            return dialog.unsavedChoice == UnsavedChoice.Save ? UnsavedChoice.Save : result == true ? UnsavedChoice.Discard : UnsavedChoice.KeepEditing;
        }

        internal static AppDialog CreateUnsavedDialog(Window owner)
        {
            var dialog = new AppDialog(owner, "Save your changes before closing?",
                "Your edits have not been saved. Saved originals and exported copies will stay on disk.", AppDialogKind.DeleteConfirmation);
            dialog.Width = 560;
            dialog.SaveActionButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ViewerAccentBrush");
            dialog.SaveActionButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ViewerAccentTextBrush");
            SetActionContent(dialog.PrimaryActionButton, "Discard", "M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7");
            SetActionContent(dialog.CancelActionButton, "Keep editing", "M4 20l4-1 12-12-3-3L5 16z M14 7l3 3");
            SetActionContent(dialog.SaveActionButton, "Save copy…", "M5 3h12l4 4v14H3V3z M7 3v6h10V3 M7 21v-8h10v8");
            System.Windows.Controls.Grid.SetColumn(dialog.PrimaryActionButton, 0);
            System.Windows.Controls.Grid.SetColumn(dialog.SaveActionButton, 3);
            dialog.PrimaryActionButton.Margin = new Thickness(0, 0, 20, 0);
            // Saving is the constructive primary action; discarding stays separate on the left.
            dialog.DialogIcon.Data = Geometry.Parse("M5 3h12l4 4v14H3V3z M7 3v6h10V3 M7 21v-8h10v8");
            dialog.Resources["DialogAccentBrush"] = dialog.Resources["ViewerAccentBrush"];
            dialog.Resources["DialogAccentBackgroundBrush"] = dialog.Resources["ViewerSelectedBrush"];
            dialog.SaveActionButton.Visibility = Visibility.Visible;
            return dialog;
        }

        private static void SetActionContent(System.Windows.Controls.Button button, string label, string geometry)
        {
            var content = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometry), Width = 14, Height = 14, Stretch = Stretch.Uniform,
                StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            content.Children.Add(icon);
            content.Children.Add(new System.Windows.Controls.TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            System.Windows.Automation.AutomationProperties.SetName(button, label == "Discard" ? "Discard changes" : label);
        }

        internal static bool ConfirmDiscard(Window owner, string title, string message)
        {
            var dialog = new AppDialog(owner, title, message, AppDialogKind.DeleteConfirmation);
            dialog.PrimaryActionButton.Content = "Discard";
            dialog.CancelActionButton.Content = "Keep";
            return dialog.ShowDialog() == true;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            unsavedChoice = UnsavedChoice.Save;
            DialogResult = true;
        }
        private AppTheme? currentTheme;
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
            Resources["DialogAccentBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            Resources["DialogAccentBackgroundBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 239, 68, 68));
            DialogIcon.Data = Geometry.Parse(kind switch
            {
                AppDialogKind.DeleteConfirmation => "M10 11v6 M14 11v6 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
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
                DetailsCaption.Text = "Details";
                DetailsText.Text = details;
            }

            Loaded += (_, _) =>
            {
                (isConfirmation ? CancelActionButton : PrimaryActionButton).Focus();
                if (!SystemParameters.ClientAreaAnimation) return;
                var offset = new TranslateTransform();
                DialogSurface.RenderTransform = offset;
                var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
                var duration = TimeSpan.FromMilliseconds(240);
                offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = easing });
                DialogSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
            };
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            ThemePreferenceService.Changed += ThemePreference_Changed;
            Closed += (_, _) =>
            {
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                ThemePreferenceService.Changed -= ThemePreference_Changed;
            };
        }

        internal static bool ConfirmDelete(Window owner, bool imported = false)
        {
            return new AppDialog(owner, imported ? "Close image?" : "Delete screenshot?",
                imported ? "Your original image will stay on disk. Unsaved edits will be discarded." : "The temporary screenshot will be deleted. Saved copies will stay on disk.",
                AppDialogKind.DeleteConfirmation).ShowDialog() == true;
        }

        internal static void ShowError(Window owner, string title, string message, string details)
        {
            new AppDialog(owner, title, message, AppDialogKind.Error, details).ShowDialog();
        }

        private void ApplyThemeFromSystem()
        {
            var theme = ThemePreferenceService.CurrentTheme;
            if (currentTheme != theme)
            {
                ThemeIconService.ApplyViewerTheme(Resources, theme);
                currentTheme = theme;
            }
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            {
                Dispatcher.BeginInvoke(new Action(ApplyThemeFromSystem));
            }
        }

        private void ThemePreference_Changed() => Dispatcher.BeginInvoke(new Action(ApplyThemeFromSystem));

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
