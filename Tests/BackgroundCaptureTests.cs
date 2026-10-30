using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BetterScreenShot;

internal static class BackgroundCaptureTests
{
    public static void Run(Action<string, Action> check, string directory)
    {
        check("Background instances coordinate show and exit without starting duplicate launchers", () =>
        {
            var scope = "Regression-" + Guid.NewGuid().ToString("N");
            using var shown = new ManualResetEventSlim();
            using var exited = new ManualResetEventSlim();
            using (var primary = new BackgroundInstance(shown.Set, exited.Set, scope))
            using (var secondary = new BackgroundInstance(() => { }, () => { }, scope))
            {
                Require(primary.IsPrimary && !secondary.IsPrimary);
                secondary.RequestShow();
                Require(shown.Wait(2000));
                secondary.RequestExit();
                Require(exited.Wait(2000));
            }
            using var replacement = new BackgroundInstance(() => { }, () => { }, scope);
            Require(replacement.IsPrimary);
            Require(StartupRegistration.Command(@"C:\Test App\BetterScreenShot.exe") == "\"C:\\Test App\\BetterScreenShot.exe\" --background");
        });
        check("Background capture keeps monitor and area selection but never reveals the launcher", () =>
        {
            var window = new MainWindow(new CaptureShortcutSettingsStore(Path.Combine(directory, "background-shortcuts.json")));
            var shown = 0;
            var closed = 0;
            window.IsVisibleChanged += (_, _) => { if (window.IsVisible) shown++; };
            window.Closed += (_, _) => closed++;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                window.EnableBackgroundMode();
                Require(!window.IsVisible && new WindowInteropHelper(window).Handle != IntPtr.Zero && shown == 0);
                window.Close();
                Require(closed == 0 && !window.IsVisible);
                foreach (var area in new[] { false, true })
                {
                    var capture = window.CaptureFromShortcutAsync(area);
                    Require(!capture.IsCompleted && !window.IsVisible);
                    Require(System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Any(form => form.Visible));
                    MonitorSelectionOverlayForm.CancelActiveSelection();
                    Complete(capture, window.Dispatcher);
                    Require(!window.IsVisible && shown == 0);
                }

                var success = window.CaptureFromShortcutAsync(false);
                var session = (CaptureSelectionSession)typeof(MonitorSelectionOverlayForm)
                    .GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                session.Complete(System.Windows.Forms.Screen.PrimaryScreen!.Bounds);
                Complete(success, window.Dispatcher);
                var toast = System.Windows.Application.Current.Windows.OfType<ScreenshotToastWindow>().Single(item => item.IsVisible);
                Require(!toast.ShowActivated && !window.IsVisible && shown == 0);
                ScreenshotToastWindow.CloseCurrent();

                window.ShowLauncher();
                Require(window.IsVisible && shown == 1);
                window.Close();
                Require(!window.IsVisible && closed == 0);
            }
            finally
            {
                MonitorSelectionOverlayForm.CancelActiveSelection();
                ScreenshotToastWindow.CloseCurrent();
                window.ExitBackgroundMode();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            Require(closed == 1);
        });
    }

    private static void Complete(Task task, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        var timeout = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) => frame.Continue = false;
        timeout.Start();
        Dispatcher.PushFrame(frame);
        timeout.Stop();
        Require(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Background capture assertion failed.");
    }
}
