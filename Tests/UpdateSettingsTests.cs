using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BetterScreenShot;

internal static class UpdateSettingsTests
{
    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        internal void Advance(TimeSpan time) => timestamp += time.Ticks;
    }

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int requests;
        internal int Requests => requests;
        internal CancellationToken LastCancellation { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            LastCancellation = cancellationToken;
            return send(cancellationToken);
        }
    }

    internal static void Run(Action<string, Action> check)
    {
        check("Manual update checks allow one request per minute with an exact retry boundary", () =>
        {
            var clock = new Clock();
            using var handler = new Handler(_ => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var updates = new UpdateService(client, clock);
            Require(updates.ManualCheckRetryAfter == TimeSpan.Zero);
            Complete(updates.CheckManualAsync(CancellationToken.None));
            for (var i = 0; i < 20; i++)
                Require(Throws<UpdateCheckThrottledException>(() => updates.CheckManualAsync(CancellationToken.None)).RetryAfter == TimeSpan.FromMinutes(1));
            clock.Advance(TimeSpan.FromMilliseconds(59_999));
            Require(Throws<UpdateCheckThrottledException>(() => updates.CheckManualAsync(CancellationToken.None)).RetryAfter == TimeSpan.FromMilliseconds(1));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Complete(updates.CheckManualAsync(CancellationToken.None));
            Require(handler.Requests == 2 && updates.ManualCheckRetryAfter == TimeSpan.FromMinutes(1));
        });
        check("Concurrent manual update requests share the one-minute limit", () =>
        {
            using var handler = new Handler(_ => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var updates = new UpdateService(client, new Clock());
            var attempts = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            {
                try { await updates.CheckManualAsync(CancellationToken.None); return true; }
                catch (UpdateCheckThrottledException) { return false; }
            }));
            Require(Complete(Task.WhenAll(attempts)).Count(accepted => accepted) == 1 && handler.Requests == 1);
        });
        check("Failed manual update checks are limited while automatic checks remain independent", () =>
        {
            var fail = true;
            using var handler = new Handler(_ => fail
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline"))
                : Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var clock = new Clock();
            var updates = new UpdateService(client, clock);
            Throws<HttpRequestException>(() => Complete(updates.CheckManualAsync(CancellationToken.None)));
            Throws<UpdateCheckThrottledException>(() => updates.CheckManualAsync(CancellationToken.None));
            fail = false;
            Require(Complete(updates.CheckAsync(CancellationToken.None)) is not null);
            Require(handler.Requests == 2 && updates.ManualCheckRetryAfter == TimeSpan.FromMinutes(1));
            clock.Advance(TimeSpan.FromMinutes(1));
            Require(Complete(updates.CheckManualAsync(CancellationToken.None)) is not null && handler.Requests == 3);
        });
        check("An already-cancelled manual check does not use its request allowance", () =>
        {
            using var handler = new Handler(_ => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var updates = new UpdateService(client, new Clock());
            Throws<OperationCanceledException>(() => updates.CheckManualAsync(new CancellationToken(canceled: true)));
            Require(updates.ManualCheckRetryAfter == TimeSpan.Zero && handler.Requests == 0);
            Complete(updates.CheckManualAsync(CancellationToken.None));
            Require(handler.Requests == 1);
        });
        check("Settings shows a newer version and a download action without saving preferences", () =>
        {
            using var handler = new Handler(_ => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var updates = new UpdateService(client, new Clock());
            WithWindow(updates, window =>
            {
                Require(Status(window).Visibility == Visibility.Collapsed && Button(window).IsEnabled);
                Click(window);
                PumpUntil(() => Status(window).Text.Contains("is available"));
                Require(((TextBlock)window.FindName("CurrentVersionText")).Text.Contains(UpdateService.CurrentVersion.ToString(3)));
                Require(((TextBlock)window.FindName("UpdateDownloadLink")).Visibility == Visibility.Visible);
                Require(!Button(window).IsEnabled && Button(window).Content.ToString()!.Contains("60s"));
            });
        });
        check("Settings reports the latest version for current and older server releases", () =>
        {
            foreach (var version in new[] { UpdateService.CurrentVersion, new Version(1, 0, 0, 0) })
            {
                using var handler = new Handler(_ => Task.FromResult(Response(version)));
                using var client = new HttpClient(handler);
                WithWindow(new UpdateService(client, new Clock()), window =>
                {
                    Click(window);
                    PumpUntil(() => Status(window).Text.Contains("latest version"));
                    Require(((TextBlock)window.FindName("UpdateDownloadLink")).Visibility == Visibility.Collapsed);
                });
            }
        });
        check("Repeated Settings clicks cannot create overlapping update checks", () =>
        {
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new Handler(token => response.Task.WaitAsync(token));
            using var client = new HttpClient(handler);
            WithWindow(new UpdateService(client, new Clock()), window =>
            {
                for (var i = 0; i < 20; i++) Click(window);
                Require(handler.Requests == 1 && !Button(window).IsEnabled && Equals(Button(window).Content, "Checking…"));
                Require(((System.Windows.Controls.Button)window.FindName("SaveShortcutsButton")).IsEnabled);
                response.SetResult(Response());
                PumpUntil(() => Status(window).Text.Contains("is available"));
                Require(handler.Requests == 1 && !Button(window).IsEnabled);
            });
        });
        check("Reopening Settings preserves the manual check cooldown and enables retry after expiry", () =>
        {
            using var handler = new Handler(_ => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            var clock = new Clock();
            var updates = new UpdateService(client, clock);
            WithWindow(updates, window => { Click(window); PumpUntil(() => Status(window).Text.Contains("is available")); });
            clock.Advance(TimeSpan.FromSeconds(30));
            WithWindow(updates, window =>
            {
                Require(!Button(window).IsEnabled && Button(window).Content.ToString()!.Contains("30s"));
                Click(window);
                Require(handler.Requests == 1);
                clock.Advance(TimeSpan.FromSeconds(30));
                typeof(CaptureShortcutsWindow).GetMethod("RefreshUpdateButton", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Require(Button(window).IsEnabled && Equals(Button(window).Content, "Check for new version"));
                Click(window);
                PumpUntil(() => Status(window).Text.Contains("is available"));
                Require(handler.Requests == 2);
            });
        });
        check("Settings handles update failures and closing during an active request", () =>
        {
            using (var handler = new Handler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline"))))
            using (var client = new HttpClient(handler))
                WithWindow(new UpdateService(client, new Clock()), window =>
                {
                    Click(window);
                    PumpUntil(() => Status(window).Text.Contains("Could not check"));
                    Require(!Button(window).IsEnabled && ((TextBlock)window.FindName("UpdateDownloadLink")).Visibility == Visibility.Collapsed);
                });
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var pendingHandler = new Handler(token => response.Task.WaitAsync(token));
            using var pendingClient = new HttpClient(pendingHandler);
            WithWindow(new UpdateService(pendingClient, new Clock()), window =>
            {
                Click(window);
                var previousStatus = Status(window).Text;
                window.Close();
                PumpUntil(() => pendingHandler.LastCancellation.IsCancellationRequested);
                PumpUntil(() => !(bool)typeof(CaptureShortcutsWindow).GetField("checkingUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
                Require(Status(window).Text == previousStatus && pendingHandler.Requests == 1);
            });
        });
        check("The About update button stays visible at short window heights in both themes", () =>
        {
            using var handler = new Handler(_ => Task.FromResult(Response()));
            using var client = new HttpClient(handler);
            WithWindow(new UpdateService(client, new Clock()), window =>
            {
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                foreach (var height in new[] { 500d, 720d })
                {
                    ThemeIconService.ApplyViewerTheme(window.Resources, theme);
                    window.Width = 544;
                    window.Height = height;
                    window.UpdateLayout();
                    var button = Button(window);
                    var point = button.TranslatePoint(new System.Windows.Point(), window);
                    Require(button.IsVisible && point.X >= 0 && point.X + button.ActualWidth <= window.ActualWidth);
                    Require(point.Y >= 0 && point.Y + button.ActualHeight <= window.ActualHeight);
                }
            });
        });
    }

    private static HttpResponseMessage Response(Version? version = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"version\":\"{version ?? new Version(UpdateService.CurrentVersion.Major + 1, 0, 0, 0)}\",\"sizeBytes\":1,\"sha256\":\"{new string('A', 64)}\"}}", Encoding.UTF8, "application/json")
    };

    private static void WithWindow(UpdateService updates, Action<CaptureShortcutsWindow> action)
    {
        var previousContext = SynchronizationContext.Current;
        var window = new CaptureShortcutsWindow(CaptureShortcutSettings.Disabled,
            (_, _) => throw new InvalidOperationException("Checking for updates saved preferences."), updates);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            window.Show();
            typeof(CaptureShortcutsWindow).GetMethod("SelectCategory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["About"]);
            action(window);
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static System.Windows.Controls.Button Button(CaptureShortcutsWindow window) => (System.Windows.Controls.Button)window.FindName("CheckVersionButton");
    private static TextBlock Status(CaptureShortcutsWindow window) => (TextBlock)window.FindName("UpdateCheckStatus");
    private static void Click(CaptureShortcutsWindow window) => Button(window).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static T Complete<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.Background);
            Dispatcher.PushFrame(frame);
        }
        Require(done());
    }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Settings update check assertion failed.");
    }
}
