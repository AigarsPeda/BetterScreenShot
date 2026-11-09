using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace BetterScreenShot;

internal static class ApplicationInstaller
{
    internal static string InstalledExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BetterScreenShot", "BetterScreenShot.exe");

    internal static bool ShouldInstall(string source, string installed, bool bundled, string[] args) =>
        bundled && args.Length == 0 && !Path.GetFullPath(source).Equals(Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase);

    internal static bool IsNewer(Version candidate, Version? installed) => installed is null || candidate > installed;

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty assembly location intentionally identifies the single-file download.")]
    internal static async Task<bool> HandleLaunchAsync(string[] args)
    {
#if PORTABLE_BUILD
        return await Task.FromResult(false);
#else
        var source = Environment.ProcessPath!;
        var target = InstalledExecutable;
        if (!ShouldInstall(source, target, string.IsNullOrEmpty(typeof(App).Assembly.Location), args)) return false;
        using var cancellation = new CancellationTokenSource();
        var progress = new InstallationWindow();
        progress.Closed += (_, _) => cancellation.Cancel();
        progress.Show();
        try
        {
            var root = Path.GetDirectoryName(target)!;
            if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The installation folder is a link. Choose a regular installation folder.");
            Directory.CreateDirectory(root);
            using var installationLock = await AcquireLockAsync(root, cancellation.Token);
            if (File.Exists(target) && !IsNewer(UpdateService.CurrentVersion, ReadVersion(target)))
            {
                StartInstalled(target);
                return true;
            }
            var startupEnabled = !File.Exists(target) || StartupRegistration.IsEnabled(target);
            await ReplaceExecutableAsync(source, target,
                token => StopInstalledAsync(target, progress.SetStatus, token),
                () => Register(target, startupEnabled), () => StartInstalled(target), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (progress.IsVisible)
                AppDialog.ShowError(progress, "Could not update BetterScreenShot", "The installation could not finish. Try opening the downloaded file again.", error.Message);
        }
        finally { progress.Close(); }
        return true;
#endif
    }

    private static async Task<FileStream> AcquireLockAsync(string root, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(root, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (attempt < 120) { await Task.Delay(250, cancellationToken); }
        }
    }

    internal static async Task ReplaceExecutableAsync(string source, string target,
        Func<CancellationToken, Task> stopRunning, Action register, Action launch, CancellationToken cancellationToken)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The running installation cannot replace itself.");
        var root = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, $".install-{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(root, $".install-backup-{Guid.NewGuid():N}.tmp");
        var replaced = false;
        var stopped = false;
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            if (!Hash(source).AsSpan().SequenceEqual(Hash(staging))) throw new IOException("The copied application failed verification.");
            await stopRunning(cancellationToken);
            stopped = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The installed executable is a link.");
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(target)) File.Replace(staging, target, backup);
                    else File.Move(staging, target);
                    break;
                }
                catch (IOException) when (attempt < 30) { await Task.Delay(100, cancellationToken); }
            }
            replaced = true;
            register();
            launch();
            ScreenshotFileService.TryDelete(backup);
        }
        catch
        {
            if (replaced)
            {
                if (File.Exists(backup)) File.Move(backup, target, overwrite: true);
                else ScreenshotFileService.TryDelete(target);
            }
            if (stopped && File.Exists(target))
            {
                if (replaced)
                {
                    try { register(); }
                    catch (Exception recoveryError) { Debug.WriteLine($"Installation registration recovery: {recoveryError.Message}"); }
                }
                try { launch(); }
                catch (Exception recoveryError) { Debug.WriteLine($"Installation startup recovery: {recoveryError.Message}"); }
            }
            throw;
        }
        finally { ScreenshotFileService.TryDelete(staging); }
    }

    private static byte[] Hash(string path)
    {
        using var input = File.OpenRead(path);
        return SHA256.HashData(input);
    }

    private static Version? ReadVersion(string path) => Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var version) ? version : null;

    private static async Task StopInstalledAsync(string target, Action<string> status, CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName("BetterScreenShot").Where(process =>
        {
            try
            {
                if (process.Id != Environment.ProcessId && string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) return true;
                process.Dispose();
                return false;
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException) { process.Dispose(); return false; }
        }).ToArray();
        try
        {
            while (processes.Any(process => !process.HasExited && HasOpenWork(process.Id)))
            {
                status("Waiting for your open screenshots or recording to close…");
                await Task.Delay(500, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            status("Installing the new version…");
            if (processes.Any(process => !process.HasExited))
            {
                using (var signal = new BackgroundInstance(() => { }, () => { }))
                    if (!signal.IsPrimary) signal.RequestExit();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(timeout.Token)));
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static bool IsWorkWindow(string title) => title is not ("BetterScreenShot" or "Settings");

    private static bool HasOpenWork(int processId)
    {
        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            var title = new System.Text.StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            if (!IsWorkWindow(title.ToString())) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static void StartInstalled(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! });
        if (process is null) throw new IOException("Windows could not start BetterScreenShot.");
    }

    private static void Register(string target, bool startupEnabled)
    {
        var root = Path.GetDirectoryName(target)!;
        using (var script = typeof(App).Assembly.GetManifestResourceStream("BetterScreenShot.install.Uninstall.ps1")
            ?? throw new IOException("The uninstaller is missing from this build."))
        using (var output = File.Create(Path.Combine(root, "Uninstall.ps1"))) script.CopyTo(output);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BetterScreenShot.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        try
        {
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = root;
            shortcut.IconLocation = target + ",0";
            shortcut.Description = "Capture screenshots and annotate images";
            shortcut.Save();
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        StartupRegistration.SetEnabled(target, startupEnabled);
        FileAssociationRegistration.Register(target);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BetterScreenShot");
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["DisplayName"] = "BetterScreenShot", ["DisplayVersion"] = FileVersionInfo.GetVersionInfo(target).FileVersion!,
            ["Publisher"] = "A. Peda", ["InstallLocation"] = root, ["DisplayIcon"] = target + ",0",
            ["UninstallString"] = $"\"{target}\" --uninstall", ["InstallDate"] = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
        }) key.SetValue(name, value, RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var bytes = Directory.EnumerateFiles(root, "*", options).Where(path => !Path.GetFileName(path).StartsWith(".install", StringComparison.Ordinal))
            .Sum(path => new FileInfo(path).Length);
        key.SetValue("EstimatedSize", checked((int)Math.Ceiling(bytes / 1024d)), RegistryValueKind.DWord);
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maximum);
}
