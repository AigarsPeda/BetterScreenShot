using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using BetterScreenShot;

internal static class UpdateServiceTests
{
    private sealed class ResponseHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    internal static void Run(Action<string, Action> check, string directory)
    {
        check("Update manifest accepts a newer release and ignores the installed version", () =>
        {
            var payload = Encoding.UTF8.GetBytes("portable release");
            var hash = Convert.ToHexString(SHA256.HashData(payload));
            byte[] Manifest(string version) => Encoding.UTF8.GetBytes($"{{\"version\":\"{version}\",\"sizeBytes\":{payload.Length},\"sha256\":\"{hash}\"}}");
            var nextVersion = new Version(UpdateService.CurrentVersion.Major + 1, 0, 0, 0);
            using var newerClient = new HttpClient(new ResponseHandler(Manifest(nextVersion.ToString())));
            var newer = new UpdateService(newerClient).CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(newer is not null && newer.DisplayVersion == nextVersion.ToString(3) && newer.SizeBytes == payload.Length);
            using var currentClient = new HttpClient(new ResponseHandler(Manifest(UpdateService.CurrentVersion.ToString())));
            Require(new UpdateService(currentClient).CheckAsync(CancellationToken.None).GetAwaiter().GetResult() is null);
            Require(UpdateService.ParseManifest(Manifest("1.2.0.0")).Sha256 == hash);
            try { UpdateService.ParseManifest(Encoding.UTF8.GetBytes("{\"version\":\"1.2.0.0\",\"sizeBytes\":1,\"sha256\":\"bad\"}"));
                throw new InvalidOperationException("Invalid hash was accepted."); }
            catch (InvalidDataException) { }
        });
        check("Update download verifies its hash before replacing a file", () =>
        {
            var payload = Encoding.UTF8.GetBytes("portable release");
            var path = Path.Combine(directory, "BetterScreenShot-update.exe");
            File.WriteAllText(path, "existing file");
            using var client = new HttpClient(new ResponseHandler(payload));
            var updates = new UpdateService(client);
            var release = new AvailableUpdate(new Version(1, 2, 0, 0), payload.Length, new string('0', 64));
            // Run off the WPF context when this check follows desktop UI tests.
            try { Task.Run(() => updates.DownloadAsync(release, path, CancellationToken.None)).GetAwaiter().GetResult();
                throw new InvalidOperationException("Invalid download was saved."); }
            catch (InvalidDataException) { }
            Require(File.ReadAllText(path) == "existing file" && !Directory.GetFiles(directory, "*.download").Any());
            Task.Run(() => updates.DownloadAsync(release with { Sha256 = Convert.ToHexString(SHA256.HashData(payload)) }, path, CancellationToken.None))
                .GetAwaiter().GetResult();
            Require(File.ReadAllBytes(path).SequenceEqual(payload));
        });
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Update assertion failed.");
    }
}
