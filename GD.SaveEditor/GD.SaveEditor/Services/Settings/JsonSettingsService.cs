using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GD.SaveEditor.Services.Settings;

/// <summary>Stores settings as JSON in <c>%APPDATA%\GD.SaveEditor\settings.json</c>.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GD.SaveEditor",
        "settings.json");

    public JsonSettingsService()
    {
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // Write to a temp file first so a crash mid-write never leaves a corrupted settings file.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(tempPath, _filePath, overwrite: true);

        Current = settings;
        SettingsChanged?.Invoke(this, settings);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), SerializerOptions)
                   ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or corrupted settings must not prevent the app from starting.
            return new AppSettings();
        }
    }
}
