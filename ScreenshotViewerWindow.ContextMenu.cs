using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Color = System.Windows.Media.Color;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    internal ContextMenu? CreateAnnotationMenu()
    {
        if (Annotations.Selected is not { } target) return null;
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("AnnotationContextMenuStyle"),
            Background = (System.Windows.Media.Brush)FindResource("ViewerSurfaceBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("ViewerPrimaryTextBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("ViewerSurfaceBorderBrush"),
            PlacementTarget = Annotations,
            Placement = PlacementMode.MousePoint
        };
        menu.Resources["ViewerSurfaceHoverBrush"] = FindResource("ViewerSurfaceHoverBrush");
        void Add(string label, Action action, string shortcut = "")
        {
            var item = new MenuItem { Header = label, InputGestureText = shortcut, Style = (Style)FindResource("AnnotationMenuItemStyle") };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                // Let the popup restore focus before opening a text editor or color picker.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsLoaded && ReferenceEquals(Annotations.Selected, target)) action();
                }), DispatcherPriority.Input);
            };
            menu.Items.Add(item);
        }

        if (target.Tool == AnnotationTool.Text) Add("Edit text…", () => BeginText(target.Points[0]));
        if (target.Tool == AnnotationTool.NumberedArrow) Add("Edit number…", BeginNumberEdit);
        Add("Duplicate", Annotations.DuplicateSelected, "Ctrl+D");
        Add("Change color…", ChooseCustomColor);
        if (target.CanBend && target.Points.Count == 3)
            Add("Straighten", () => Annotations.ChangeSelected(item => item.Points = [item.Points[0], item.Points[^1]]));
        menu.Items.Add(new Separator());
        Add("Delete", Annotations.DeleteSelected, "Del");
        return menu;
    }

    private void ShowAnnotationMenu()
    {
        if (CreateAnnotationMenu() is { } menu) menu.IsOpen = true;
    }
}
