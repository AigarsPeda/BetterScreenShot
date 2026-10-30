using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace BetterScreenShot;

internal sealed record CaptureShortcut(Key Key = Key.None, ModifierKeys Modifiers = ModifierKeys.None)
{
    [JsonIgnore] public bool IsEnabled => Key != Key.None;
    [JsonIgnore] public string DisplayText
    {
        get
        {
            if (!IsEnabled) return "Not set";
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(Key switch
            {
                >= Key.D0 and <= Key.D9 => ((int)Key - (int)Key.D0).ToString(),
                Key.Snapshot => "Print Screen",
                Key.Return => "Enter",
                Key.Escape => "Esc",
                _ => new KeyConverter().ConvertToString(Key) ?? Key.ToString()
            });
            return string.Join("+", parts);
        }
    }

    public string? ValidationError()
    {
        if ((Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0)
            return "Choose a valid modifier key.";
        if (!IsEnabled) return Modifiers == ModifierKeys.None ? null : "Choose a key as well as the modifiers.";
        if (!Enum.IsDefined(Key) || KeyInterop.VirtualKeyFromKey(Key) is <= 0 or > 255 || IsModifier(Key))
            return "Press a key together with Ctrl, Alt, or Win.";
        if (Key == Key.F12) return "F12 is reserved by Windows. Choose another key.";
        if ((Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0
            && Key is not (>= Key.F1 and <= Key.F24) && Key != Key.Snapshot)
            return "Include Ctrl, Alt, or Win, or use a function key or Print Screen.";
        return null;
    }

    internal static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}

internal sealed record CaptureShortcutSettings
{
    public CaptureShortcut FullScreen { get; init; } = new();
    public CaptureShortcut Area { get; init; } = new();
    public static CaptureShortcutSettings Disabled => new();

    public string? ValidationError()
    {
        if (FullScreen is null || Area is null) return "Both capture options need a shortcut setting.";
        if (FullScreen.ValidationError() is { } fullError) return $"Capture Full Screen: {fullError}";
        if (Area.ValidationError() is { } areaError) return $"Select Area: {areaError}";
        return FullScreen.IsEnabled && FullScreen == Area ? "Choose different shortcuts for the two capture options." : null;
    }
}

internal sealed class CaptureShortcutSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static CaptureShortcutSettingsStore Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterScreenShot", "shortcuts.json"));

    public CaptureShortcutSettings Load(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(filePath)) return CaptureShortcutSettings.Disabled;
            var settings = JsonSerializer.Deserialize<CaptureShortcutSettings>(File.ReadAllText(filePath), JsonOptions);
            if (settings is null || settings.ValidationError() is not null) throw new JsonException("Invalid shortcut settings.");
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "Could not load your shortcuts. Open Shortcuts to configure them again.";
            System.Diagnostics.Debug.WriteLine($"Shortcut settings: {ex.Message}");
            return CaptureShortcutSettings.Disabled;
        }
    }

    public void Save(CaptureShortcutSettings settings)
    {
        if (settings.ValidationError() is { } error) throw new ArgumentException(error, nameof(settings));
        var path = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally { ScreenshotFileService.TryDelete(temporaryPath); }
    }
}
