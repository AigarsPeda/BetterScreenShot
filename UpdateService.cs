using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace BetterScreenShot;

internal sealed record AvailableUpdate(Version Version, long SizeBytes, string Sha256)
{
    public string DisplayVersion => Version.ToString(3);
}

internal sealed class UpdateService(HttpClient client)
{
    internal static readonly Uri ManifestUrl = new("https://www.aigarspeda.com/downloads/betterscreenshot/latest.json");
    internal static readonly Uri DownloadUrl = new("https://www.aigarspeda.com/downloads/betterscreenshot/BetterScreenShot.exe");
    internal static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    internal async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await client.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 16384) throw new InvalidDataException("Update information is too large.");
        var data = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        if (data.Length > 16384) throw new InvalidDataException("Update information is too large.");
        var release = ParseManifest(data);
        return release.Version > CurrentVersion ? release : null;
    }

    internal static AvailableUpdate ParseManifest(ReadOnlyMemory<byte> data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        var versionText = root.GetProperty("version").GetString();
        var sha256 = root.GetProperty("sha256").GetString();
        var size = root.GetProperty("sizeBytes").GetInt64();
        if (!Version.TryParse(versionText, out var version) || version.Build < 0 || version.Revision < 0
            || size is < 1 or > 1_000_000_000 || sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Update information is invalid.");
        return new AvailableUpdate(version, size, sha256.ToUpperInvariant());
    }

    internal async Task DownloadAsync(AvailableUpdate release, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.download";
        try
        {
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                131072, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[131072];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += count;
                if (total > release.SizeBytes) throw new InvalidDataException("The download is larger than the published release.");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
            if (total != release.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download did not match the published release.");
            output.Close();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
