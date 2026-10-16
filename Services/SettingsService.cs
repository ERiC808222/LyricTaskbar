using System.IO;
using System.Text.Json;
using DesktopLyrics.Models;
namespace DesktopLyrics.Services;
public class SettingsService
{
    public static string DataFolder => Environment.GetEnvironmentVariable("LYRICBAR_DATA") ?? Path.Combine(AppContext.BaseDirectory, "Data");
    private readonly string _settingsFile;
    public AppSettings Settings { get; private set; }
    public SettingsService(string? folder = null)
    {
        _settingsFile = Path.Combine(folder ?? DataFolder, "settings.json");
        Settings = LoadSettings();
    }
    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile)) ?? new();
        }
        catch (IOException) { }
        catch (JsonException) { }
        return new();
    }
    public void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
