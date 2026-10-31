using System.IO;

namespace BetterScreenShot;

internal sealed class ThemePreferenceStore(string filePath)
{
    internal static ThemePreferenceStore Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterScreenShot", "theme.txt"));

    internal AppTheme? Load()
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            return Enum.TryParse<AppTheme>(File.ReadAllText(filePath).Trim(), true, out var theme)
                && Enum.IsDefined(theme) ? theme : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal void Save(AppTheme? theme)
    {
        if (theme is null)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, theme.Value.ToString());
            File.Move(temporary, filePath, overwrite: true);
        }
        finally { ScreenshotFileService.TryDelete(temporary); }
    }
}

internal static class ThemePreferenceService
{
    private static readonly ThemePreferenceStore store = ThemePreferenceStore.Default;
    private static AppTheme? selected = store.Load();
    internal static event Action? Changed;
    internal static AppTheme CurrentTheme => selected ?? ThemeIconService.DetectWindowsTheme();
    internal static bool FollowsSystem => selected is null;

    internal static void Toggle() => Set(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    internal static void Set(AppTheme? theme)
    {
        store.Save(theme);
        selected = theme;
        Changed?.Invoke();
    }
}
