using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BetterScreenShot;

internal sealed class BackgroundInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show;
    private readonly EventWaitHandle exit;
    private readonly RegisteredWaitHandle? showWait;
    private readonly RegisteredWaitHandle? exitWait;
    private readonly string imagePipeName;
    private readonly CancellationTokenSource imageCancellation = new();
    private readonly Task? imageListener;
    public bool IsPrimary { get; }

    public BackgroundInstance(Action showLauncher, Action exitApplication, string? scope = null, Action<string[]>? openImages = null)
    {
        var name = @"Local\BetterScreenShot-" + (scope ?? WindowsIdentity.GetCurrent().User!.Value);
        imagePipeName = name[6..] + "-" + Process.GetCurrentProcess().SessionId + "-Images";
        mutex = new Mutex(false, name, out var primary);
        IsPrimary = primary;
        // A signal survives startup races until the primary instance begins waiting.
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Show");
        exit = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Exit");
        if (primary)
        {
            showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => showLauncher(), null, Timeout.Infinite, false);
            exitWait = ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => exitApplication(), null, Timeout.Infinite, false);
            if (openImages is not null) imageListener = ListenForImagesAsync(openImages, imageCancellation.Token);
        }
    }

    public void RequestShow() => show.Set();
    public void RequestExit() => exit.Set();

    internal async Task RequestOpenImagesAsync(string[] paths)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", imagePipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(paths).AsMemory(), timeout.Token).ConfigureAwait(false);
        if (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) != "OK")
            throw new IOException("The running BetterScreenShot app could not receive the image. Try opening it again.");
    }

    private async Task ListenForImagesAsync(Action<string[]> openImages, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(imagePipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    var request = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    var paths = request is null ? null : JsonSerializer.Deserialize<string[]>(request);
                    if (paths is not { Length: > 0 } || paths.Any(string.IsNullOrWhiteSpace)) continue;
                    openImages(paths);
                    await writer.WriteLineAsync("OK".AsMemory(), timeout.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or JsonException or OperationCanceledException)
                {
                    Debug.WriteLine($"Image open request: {error.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { Debug.WriteLine($"Image open listener: {error.Message}"); }
    }

    public void Dispose()
    {
        imageCancellation.Cancel();
        imageListener?.GetAwaiter().GetResult();
        imageCancellation.Dispose();
        showWait?.Unregister(null);
        exitWait?.Unregister(null);
        show.Dispose();
        exit.Dispose();
        mutex.Dispose();
    }
}
