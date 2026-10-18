using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrinterDesk.Core.Settings;

/// <summary>Reads and writes <c>%APPDATA%\PrinterDesk\settings.json</c>.</summary>
public sealed class SettingsStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinterDesk", "settings.json");

    /// <summary>Returns defaults when the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be read; using defaults", ex);
            return new AppSettings();
        }
    }

    /// <summary>Writes atomically (temp file, then move) so a crash never leaves a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }
}
