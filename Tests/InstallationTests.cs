using System.IO;
using BetterScreenShot;

internal static class InstallationTests
{
    internal static void Run(Action<string, Action> check, string directory)
    {
        check("Downloaded single-file apps install before single-instance routing", () =>
        {
            const string target = @"C:\Users\Example\AppData\Local\Programs\BetterScreenShot\BetterScreenShot.exe";
            Require(ApplicationInstaller.ShouldInstall(@"C:\Downloads\BetterScreenShot.exe", target, true, []));
            Require(ApplicationInstaller.ShouldInstall(@"C:\Downloads\BetterScreenShot (2).exe", target, true, []));
            Require(!ApplicationInstaller.ShouldInstall(target.ToUpperInvariant(), target, true, []));
            Require(!ApplicationInstaller.ShouldInstall(@"C:\build\BetterScreenShot.exe", target, false, []));
            foreach (var flag in new[] { "--portable", "--background", "--exit", "--uninstall" })
                Require(!ApplicationInstaller.ShouldInstall(@"C:\Downloads\BetterScreenShot.exe", target, true, [flag]));
        });
        check("Installation never downgrades or reinstalls an equal version", () =>
        {
            var candidate = new Version(1, 1, 4, 0);
            Require(ApplicationInstaller.IsNewer(candidate, null));
            Require(ApplicationInstaller.IsNewer(candidate, new Version(1, 1, 3, 0)));
            Require(!ApplicationInstaller.IsNewer(candidate, candidate));
            Require(!ApplicationInstaller.IsNewer(candidate, new Version(1, 2, 0, 0)));
        });
        check("Fresh one-click installation copies the complete file before registering and launching", () =>
        {
            var (source, target) = Paths(directory, "fresh");
            var expected = new byte[131_173];
            new Random(17).NextBytes(expected);
            File.WriteAllBytes(source, expected);
            var steps = new List<string>();
            Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => { Require(!File.Exists(target)); steps.Add("stop"); return Task.CompletedTask; },
                () => { Require(File.ReadAllBytes(target).SequenceEqual(expected)); steps.Add("register"); },
                () => steps.Add("launch"), CancellationToken.None));
            Require(steps.SequenceEqual(new[] { "stop", "register", "launch" }));
            Require(File.ReadAllBytes(source).SequenceEqual(expected));
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("Updating replaces only the app and preserves preference files", () =>
        {
            var (source, target) = Paths(directory, "update");
            File.WriteAllText(source, "new version");
            File.WriteAllText(target, "old version");
            var preferences = Path.Combine(Path.GetDirectoryName(target)!, "editor.json");
            File.WriteAllText(preferences, "saved preferences");
            Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => { Require(File.ReadAllText(target) == "old version"); return Task.CompletedTask; },
                () => { }, () => { Require(File.ReadAllText(target) == "new version"); }, CancellationToken.None));
            Require(File.ReadAllText(target) == "new version" && File.ReadAllText(preferences) == "saved preferences");
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("Registration failure restores the previous executable and registration", () =>
        {
            var (source, target) = Paths(directory, "rollback-registration");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            string? registered = null;
            var launched = false;
            Throws<IOException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => Task.CompletedTask,
                () => { if (File.ReadAllText(target) == "new") throw new IOException("Registration denied"); registered = File.ReadAllText(target); },
                () => launched = true, CancellationToken.None)));
            Require(File.ReadAllText(target) == "old" && registered == "old" && launched);
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("Launch failure rolls back the replacement and restarts the previous version", () =>
        {
            var (source, target) = Paths(directory, "rollback-launch");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            string? registered = null;
            var launchedOld = false;
            Throws<IOException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => Task.CompletedTask, () => registered = File.ReadAllText(target),
                () => { if (File.ReadAllText(target) == "new") throw new IOException("Launch denied"); launchedOld = true; }, CancellationToken.None)));
            Require(File.ReadAllText(target) == "old" && registered == "old" && launchedOld);
        });
        check("The previous app restarts even when registration recovery is unavailable", () =>
        {
            var (source, target) = Paths(directory, "registration-denied");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            var launched = false;
            Throws<UnauthorizedAccessException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => Task.CompletedTask, () => throw new UnauthorizedAccessException("Registry policy"),
                () => launched = true, CancellationToken.None)));
            Require(launched && File.ReadAllText(target) == "old");
        });
        check("Cancelling before replacement leaves the old app intact and cleans staging files", () =>
        {
            var (source, target) = Paths(directory, "cancel");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            using var cancel = new CancellationTokenSource();
            var launched = false;
            Throws<OperationCanceledException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => { cancel.Cancel(); return Task.CompletedTask; }, () => throw new InvalidOperationException("Cancellation registered an update"),
                () => launched = true, cancel.Token)));
            Require(File.ReadAllText(target) == "old" && launched);
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("Copy failures never stop or replace the running version", () =>
        {
            var (source, target) = Paths(directory, "missing-source");
            File.WriteAllText(target, "old");
            var stopped = false;
            Throws<FileNotFoundException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => { stopped = true; return Task.CompletedTask; }, () => { }, () => { }, CancellationToken.None)));
            Require(!stopped && File.ReadAllText(target) == "old");
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("Self replacement is rejected and open capture windows block automatic exit", () =>
        {
            var (source, _) = Paths(directory, "self");
            File.WriteAllText(source, "keep");
            Throws<IOException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, source,
                _ => Task.CompletedTask, () => { }, () => { }, CancellationToken.None)));
            Require(File.ReadAllText(source) == "keep");
            Require(!ApplicationInstaller.IsWorkWindow("BetterScreenShot") && !ApplicationInstaller.IsWorkWindow("Settings"));
            foreach (var title in new[] { "Screenshot Editor", "Image Editor — photo.png", "Screenshot Saved", "View recording", "Record area", "Scrolling capture", "" })
                Require(ApplicationInstaller.IsWorkWindow(title));
        });
        check("A failed stop leaves the old version running and never registers the replacement", () =>
        {
            var (source, target) = Paths(directory, "stop-failure");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            var changed = false;
            Throws<IOException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                _ => Task.FromException(new IOException("The old app is still busy")), () => changed = true,
                () => changed = true, CancellationToken.None)));
            Require(!changed && File.ReadAllText(target) == "old");
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("A locked installed file cannot be corrupted by an update", () =>
        {
            var (source, target) = Paths(directory, "locked-target");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            var registered = false;
            using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                Throws<IOException>(() => Complete(() => ApplicationInstaller.ReplaceExecutableAsync(source, target,
                    _ => Task.CompletedTask, () => registered = true, () => { }, CancellationToken.None)));
            Require(!registered && File.ReadAllText(target) == "old");
            Require(!Directory.GetFiles(Path.GetDirectoryName(target)!, ".install*.tmp").Any());
        });
        check("Single-file builds include the uninstaller without companion downloads", () =>
        {
            using var resource = typeof(App).Assembly.GetManifestResourceStream("BetterScreenShot.install.Uninstall.ps1");
            Require(resource is not null && new StreamReader(resource).ReadToEnd().Contains("BetterScreenShot"));
        });
    }

    private static (string Source, string Target) Paths(string directory, string name)
    {
        var root = Path.Combine(directory, "installation", name);
        Directory.CreateDirectory(root);
        var installed = Path.Combine(root, "installed");
        Directory.CreateDirectory(installed);
        return (Path.Combine(root, "Downloaded app.exe"), Path.Combine(installed, "BetterScreenShot.exe"));
    }
    private static void Complete(Func<Task> operation) => Task.Run(operation).GetAwaiter().GetResult();
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("One-click installation assertion failed.");
    }
}
