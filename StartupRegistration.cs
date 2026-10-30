using Microsoft.Win32;

namespace BetterScreenShot;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BetterScreenShot";
    internal static string Command(string executable) => $"\"{executable}\" --background";

    public static bool IsEnabled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command(executable), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(string executable, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
