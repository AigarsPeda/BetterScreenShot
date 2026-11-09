using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace BetterScreenShot;

public partial class ScreenshotViewerWindow
{
    private bool? propertiesOpen;
    private bool hasProperties;

    internal static string ToolLabel(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Hand => "Pan",
        AnnotationTool.NumberedArrow => "Numbered arrow",
        _ => tool.ToString()
    };

    private void EditorContent_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateEditorLayout();

    private void UpdateEditorLayout()
    {
        if (ToolDock is null || PropertiesButton is null || EditorContent.ActualWidth <= 0) return;
        var width = EditorContent.ActualWidth - 24;
        var compact = width < 760;
        // Ten square tools plus the menu and export groups need 721 DIP in total.
        var oneRow = width >= 724;
        Grid.SetRow(ToolDock, oneRow ? 0 : 1);
        Grid.SetColumn(ToolDock, oneRow ? 1 : 0);
        Grid.SetColumnSpan(ToolDock, oneRow ? 1 : 3);
        ToolDock.Margin = new Thickness(6, oneRow ? 0 : 8, 6, 0);
        ToolDock.Visibility = compact && textPanelOpen ? Visibility.Collapsed : Visibility.Visible;
        var top = oneRow || textPanelOpen && compact ? 60 : 104;
        var showProperties = hasProperties && !textPanelOpen && (propertiesOpen ?? !compact);
        AnnotationOptionsTray.Visibility = showProperties ? Visibility.Visible : Visibility.Collapsed;
        AnnotationOptionsTray.Margin = new Thickness(12, top + 12, 0, 80);
        AnnotationOptionsTray.MaxHeight = Math.Max(80, EditorContent.ActualHeight - top - 92);
        PropertiesButton.IsEnabled = hasProperties && !textPanelOpen;
        PropertiesButton.IsChecked = showProperties;
        // Tool surfaces overlay the canvas; opening properties must never refit the image.
        ExtractedTextHost.Margin = new Thickness(0, top + 12, 12, 64);
        CanvasHost.Visibility = compact && textPanelOpen ? Visibility.Collapsed : Visibility.Visible;
        if (compact && textPanelOpen)
        {
            ExtractedTextHost.BeginAnimation(WidthProperty, null);
            ExtractedTextSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            ExtractedTextHost.Width = width;
            ExtractedTextSlide.X = 0;
        }
        else if (textPanelOpen && ExtractedTextHost.Width != 312)
        {
            ExtractedTextHost.BeginAnimation(WidthProperty, null);
            ExtractedTextHost.Width = 312;
        }
        ExtractedTextPanel.Width = compact ? Math.Max(100, width - 12) : 300;
        ExtractedTextPanel.Margin = new Thickness(compact ? 0 : 12, 0, 0, 0);
        CloseExtractedTextButton.Width = compact ? 104 : 30;
        CloseExtractedTextButton.Content = compact ? "Back to image" : "×";
        System.Windows.Automation.AutomationProperties.SetName(CloseExtractedTextButton, compact ? "Back to image" : "Close text panel");
    }

    private void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        propertiesOpen = AnnotationOptionsTray.Visibility != Visibility.Visible;
        UpdateEditorLayout();
    }

    private void HideProperties_Click(object sender, RoutedEventArgs e)
    {
        propertiesOpen = false;
        UpdateEditorLayout();
        PropertiesButton.Focus();
    }

    private void SetEditorStatus(string message)
    {
        EditorStatus.Text = message;
        if (IsLoaded)
            (UIElementAutomationPeer.FromElement(EditorStatus) ?? UIElementAutomationPeer.CreatePeerForElement(EditorStatus))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
