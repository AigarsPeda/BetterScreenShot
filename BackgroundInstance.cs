using System.Security.Principal;

namespace BetterScreenShot;

internal sealed class BackgroundInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show;
    private readonly EventWaitHandle exit;
    private readonly RegisteredWaitHandle? showWait;
    private readonly RegisteredWaitHandle? exitWait;
    public bool IsPrimary { get; }

    public BackgroundInstance(Action showLauncher, Action exitApplication, string? scope = null)
    {
        var name = @"Local\BetterScreenShot-" + (scope ?? WindowsIdentity.GetCurrent().User!.Value);
        mutex = new Mutex(false, name, out var primary);
        IsPrimary = primary;
        // A signal survives startup races until the primary instance begins waiting.
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Show");
        exit = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Exit");
        if (primary)
        {
            showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => showLauncher(), null, Timeout.Infinite, false);
            exitWait = ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => exitApplication(), null, Timeout.Infinite, false);
        }
    }

    public void RequestShow() => show.Set();
    public void RequestExit() => exit.Set();

    public void Dispose()
    {
        showWait?.Unregister(null);
        exitWait?.Unregister(null);
        show.Dispose();
        exit.Dispose();
        mutex.Dispose();
    }
}
