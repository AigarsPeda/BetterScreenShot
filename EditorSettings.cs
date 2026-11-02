using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace BetterScreenShot;

internal sealed record EditorSettings
{
    internal static readonly AnnotationTool[] Tools =
    [
        AnnotationTool.Select, AnnotationTool.Hand, AnnotationTool.Pen, AnnotationTool.Highlight,
        AnnotationTool.Line, AnnotationTool.Arrow, AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Text
    ];

    public AnnotationTool DefaultTool { get; init; } = AnnotationTool.Arrow;
    public bool RememberLastTool { get; init; }
    public AnnotationTool? LastTool { get; init; }
    public string Color { get; init; } = "#FF453A";
    public double StrokeWidth { get; init; } = 4;
    public double TextSize { get; init; } = 28;
    public bool SketchEnabled { get; init; } = true;
    public Dictionary<AnnotationTool, Key> ToolKeys { get; init; } = new()
    {
        [AnnotationTool.Select] = Key.V, [AnnotationTool.Hand] = Key.H,
        [AnnotationTool.Pen] = Key.P, [AnnotationTool.Highlight] = Key.M,
        [AnnotationTool.Line] = Key.L, [AnnotationTool.Arrow] = Key.A,
        [AnnotationTool.Rectangle] = Key.R, [AnnotationTool.Ellipse] = Key.O,
        [AnnotationTool.Text] = Key.T
    };

    internal AnnotationTool OpeningTool => RememberLastTool && LastTool is { } last && Tools.Contains(last) ? last : DefaultTool;

    internal string? ValidationError()
    {
        if (!Tools.Contains(DefaultTool) || LastTool is { } last && !Tools.Contains(last)) return "Choose a valid editor tool.";
        if (Color is null || Color.Length != 7 || Color[0] != '#' || !Color.Skip(1).All(Uri.IsHexDigit)) return "Choose a valid annotation color.";
        if (StrokeWidth is not (2 or 4 or 7) || TextSize is not (16 or 24 or 28 or 40 or 64)) return "Choose a valid stroke width and text size.";
        if (ToolKeys is null || ToolKeys.Count != Tools.Length || Tools.Any(tool => !ToolKeys.TryGetValue(tool, out var key) || key is < Key.A or > Key.Z || key == Key.E))
            return "Assign one letter to every editor tool. E is reserved for Extract text.";
        if (ToolKeys.Values.Distinct().Count() != Tools.Length) return "Choose a different letter for each editor tool.";
        return null;
    }
}

internal sealed class EditorSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static EditorSettingsStore Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterScreenShot", "editor.json"));

    internal EditorSettings Load()
    {
        try
        {
            if (!File.Exists(filePath)) return new EditorSettings();
            var settings = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(filePath), JsonOptions);
            return settings is not null && settings.ValidationError() is null ? settings : new EditorSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Editor settings: {error.Message}");
            return new EditorSettings();
        }
    }

    internal void Save(EditorSettings settings)
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

internal static class EditorSettingsService
{
    private static readonly EditorSettingsStore store = EditorSettingsStore.Default;
    internal static EditorSettings Current { get; private set; } = store.Load();
    internal static event Action? Changed;

    internal static void Save(EditorSettings settings)
    {
        store.Save(settings);
        Current = settings;
        Changed?.Invoke();
    }

    internal static void Remember(AnnotationTool tool)
    {
        if (!Current.RememberLastTool || Current.LastTool == tool) return;
        try { Save(Current with { LastTool = tool }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not remember editor tool: {error.Message}");
        }
    }
}
