using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BetterScreenShot;

internal static class FileAssociationRegistration
{
    internal static void Register(string executable)
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        Register(classes, executable);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
    }

    internal static void Register(RegistryKey classes, string executable)
    {
        var command = $"\"{executable}\" \"%1\"";
        using var application = classes.CreateSubKey(@"Applications\BetterScreenShot.exe");
        application.SetValue("FriendlyAppName", "BetterScreenShot");
        using (var icon = application.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{executable}\",0");
        using (var open = application.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
        using var supported = application.CreateSubKey("SupportedTypes");
        foreach (var extension in ImportedImageService.SupportedExtensions)
        {
            supported.SetValue(extension, "");
            var progId = "BetterScreenShot" + extension;
            using var type = classes.CreateSubKey(progId);
            type.SetValue("", extension[1..].ToUpperInvariant() + " image");
            using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{executable}\",0");
            using (var open = type.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
            using var choices = classes.CreateSubKey(extension + @"\OpenWithProgids");
            choices.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
