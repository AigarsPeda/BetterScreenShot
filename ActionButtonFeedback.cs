using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace BetterScreenShot;

internal static class ActionButtonFeedback
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(ActionButtonFeedback), new PropertyMetadata(false));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.RegisterAttached(
        "Message", typeof(string), typeof(ActionButtonFeedback), new PropertyMetadata(""));
    private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
        "Timer", typeof(DispatcherTimer), typeof(ActionButtonFeedback));

    public static bool GetIsActive(DependencyObject target) => (bool)target.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject target, bool value) => target.SetValue(IsActiveProperty, value);
    public static string GetMessage(DependencyObject target) => (string)target.GetValue(MessageProperty);
    public static void SetMessage(DependencyObject target, string value) => target.SetValue(MessageProperty, value);

    internal static void Show(Button button, string message = "Saved", bool iconOnly = false)
    {
        Reset(button);
        SetMessage(button, iconOnly ? "✓" : "✓ " + message);
        SetIsActive(button, true);
        AutomationProperties.SetItemStatus(button, message);
        (UIElementAutomationPeer.FromElement(button) ?? UIElementAutomationPeer.CreatePeerForElement(button))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        Animate(button, entering: true);
        // Allow three seconds of still, readable feedback after the entrance finishes.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 3280 : 3000) };
        var leaving = false;
        timer.Tick += (_, _) =>
        {
            if (!leaving && SystemParameters.ClientAreaAnimation)
            {
                leaving = true;
                Animate(button, entering: false);
                timer.Interval = TimeSpan.FromMilliseconds(180);
                return;
            }
            Reset(button);
            Animate(button, entering: true);
        };
        button.SetValue(TimerProperty, timer);
        button.Unloaded += Button_Unloaded;
        timer.Start();
    }

    internal static void Reset(Button button)
    {
        if (button.GetValue(TimerProperty) is DispatcherTimer timer) timer.Stop();
        button.ClearValue(TimerProperty);
        button.Unloaded -= Button_Unloaded;
        button.BeginAnimation(UIElement.OpacityProperty, null);
        if (button.Template?.FindName("ActionContent", button) is ContentPresenter content)
            content.RenderTransform = Transform.Identity;
        SetIsActive(button, false);
        AutomationProperties.SetItemStatus(button, "");
    }

    private static void Button_Unloaded(object sender, RoutedEventArgs e) => Reset((Button)sender);

    private static void Animate(Button button, bool entering)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var duration = TimeSpan.FromMilliseconds(entering ? 280 : 180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        button.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(entering ? 0.72 : 1, entering ? 1 : 0.72, duration)
            { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        if (button.Template?.FindName("ActionContent", button) is ContentPresenter content)
        {
            var offset = new TranslateTransform();
            content.RenderTransform = offset;
            offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(entering ? 5 : 0, entering ? 0 : -4, duration)
                { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        }
    }
}
