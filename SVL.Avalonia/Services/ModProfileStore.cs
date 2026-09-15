using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SVL.Avalonia.Models;

namespace SVL.Avalonia.Services;

/// <summary>
/// 管理多整合包 / Mod 预设 (Mod Profiles) 的持久化存储。
/// 对应配置保存在 %LocalAppData%/SVL/Avalonia/mod-profiles.json。
/// </summary>
public sealed class ModProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storageFilePath;
    private readonly object _lock = new();

    public ModProfileStore(string? storageDirectory = null)
    {
        var basePath = string.IsNullOrWhiteSpace(storageDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia")
            : Path.GetFullPath(storageDirectory);

        Directory.CreateDirectory(basePath);
        _storageFilePath = Path.Combine(basePath, "mod-profiles.json");
    }

    public string StorageFilePath => _storageFilePath;

    /// <summary>
    /// 获取指定实例下的所有预设。若无则自动注入一个“默认”预设。
    /// </summary>
    public List<ModProfileRecord> GetProfilesForInstance(string instanceKey, string instanceName = "")
    {
        var all = LoadAllProfiles();
        var key = NormalizeKey(instanceKey);

        var list = all.Where(p => string.IsNullOrWhiteSpace(p.InstanceKey) ||
                                  string.Equals(NormalizeKey(p.InstanceKey), key, StringComparison.OrdinalIgnoreCase))
                      .ToList();

        if (list.Count == 0 || !list.Any(p => p.IsDefault))
        {
            var defaultProfile = new ModProfileRecord
            {
                Id = "default",
                Name = string.IsNullOrWhiteSpace(instanceName) ? "默认 Mods" : $"{instanceName} (默认)",
                InstanceKey = key,
                ModsPath = string.Empty,
                IsDefault = true,
                Description = "使用该实例原本的 Mods 目录"
            };

            list.Insert(0, defaultProfile);
            all.Insert(0, defaultProfile);
            SaveAllProfiles(all);
        }

        return list;
    }

    public List<ModProfileRecord> LoadAllProfiles()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_storageFilePath))
                {
                    return [];
                }

                var json = File.ReadAllText(_storageFilePath);
                return JsonSerializer.Deserialize<List<ModProfileRecord>>(json, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public void SaveAllProfiles(IReadOnlyList<ModProfileRecord> records)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(records, JsonOptions);
                AtomicFileWriter.WriteUtf8(_storageFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModProfileStore] Failed to save profiles: {ex.Message}");
            }
        }
    }

    public void UpsertProfile(ModProfileRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_lock)
        {
            var all = LoadAllProfiles();
            var index = all.FindIndex(p => string.Equals(p.Id, record.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                all[index] = record;
            }
            else
            {
                all.Add(record);
            }
            SaveAllProfiles(all);
        }
    }

    public bool DeleteProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.Equals(profileId, "default", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (_lock)
        {
            var all = LoadAllProfiles();
            var removed = all.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase) && !p.IsDefault);
            if (removed > 0)
            {
                SaveAllProfiles(all);
                return true;
            }
            return false;
        }
    }

    private static string NormalizeKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }
}
