using System.Text.Json;

namespace SVL.Avalonia.Services;

public sealed class InstanceRegistryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _registryPath;

    public InstanceRegistryStore(string? storageDirectory = null)
    {
        var basePath = string.IsNullOrWhiteSpace(storageDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia")
            : Path.GetFullPath(storageDirectory);

        Directory.CreateDirectory(basePath);
        _registryPath = Path.Combine(basePath, "instances-registry.json");
    }

    public bool Exists => File.Exists(_registryPath);

    public string GetRegistryPath() => _registryPath;

    public List<ManualInstanceRecord> LoadManualInstances()
    {
        try
        {
            if (!File.Exists(_registryPath))
            {
                return [];
            }

            var json = File.ReadAllText(_registryPath);
            return JsonSerializer.Deserialize<List<ManualInstanceRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveManualInstances(IReadOnlyList<ManualInstanceRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        AtomicFileWriter.WriteUtf8(_registryPath, json);
    }
}

public sealed class ManualInstanceRecord
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
