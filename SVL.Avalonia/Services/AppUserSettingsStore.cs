using SVL.Avalonia.Models;
using System.Text.Json;

namespace SVL.Avalonia.Services;

public sealed class AppUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public AppUserSettingsStore(string? storageDirectory = null)
    {
        var basePath = string.IsNullOrWhiteSpace(storageDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia")
            : Path.GetFullPath(storageDirectory);

        Directory.CreateDirectory(basePath);
        _settingsPath = Path.Combine(basePath, "settings.json");
    }

    public bool Exists => File.Exists(_settingsPath);

    public AppUserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppUserSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppUserSettings>(json, JsonOptions) ?? new AppUserSettings();
        }
        catch
        {
            return new AppUserSettings();
        }
    }

    public void Save(AppUserSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicFileWriter.WriteUtf8(_settingsPath, json);
    }

    public string GetSettingsPath() => _settingsPath;
}
