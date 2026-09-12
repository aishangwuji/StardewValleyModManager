using System.IO;

namespace SVL.Avalonia.Services;

/// <summary>
/// Nexus 资源下载缓存：按 ModID_FileID 缓存已完成的下载文件，避免下次再弹浏览器下载指引。
/// 与通用 URL 哈希缓存不同，这里按浏览器指引 URL 所携带的 ModID/FileID 唯一键缓存。
/// </summary>
public static class NexusDownloadCache
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SVL", "Avalonia", "cache", "nexus");

    /// <summary>
    /// WPF/Core 旧版 Nexus 缓存目录。只作为迁移兼容来源读取，命中后会提升到
    /// Avalonia 目录，避免升级后第一次安装仍然重复打开浏览器。
    /// </summary>
    public static string LegacyRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SVL", "cache", "nexusmods", "downloads");

    /// <summary>缓存文件路径（保留 .zip 后缀以兼容早期缓存路径，内容可为任意 Nexus 下载文件）。</summary>
    public static string GetCachePath(long modId, long fileId)
        => Path.Combine(Root, $"{modId}_{fileId}.zip");

    /// <summary>获取 WPF/Core 旧版缓存路径。</summary>
    public static string GetLegacyCachePath(long modId, long fileId)
        => Path.Combine(LegacyRoot, $"mod_{modId}_{fileId}.zip");

    /// <summary>
    /// 从新旧缓存文件名读取 FileID。导出端可以据此补全只有 ModID 的旧来源，
    /// 不必再次访问 Nexus API 或打开浏览器。
    /// </summary>
    public static bool TryGetFileIdFromCachePath(
        string cachePath,
        long modId,
        out long fileId)
    {
        fileId = 0;
        if (string.IsNullOrWhiteSpace(cachePath) || modId <= 0)
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(cachePath);
        var prefixes = new[] { $"{modId}_", $"mod_{modId}_" };
        foreach (var prefix in prefixes)
        {
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return long.TryParse(
                       fileName[prefix.Length..],
                       System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out fileId) &&
                   fileId > 0;
        }

        return false;
    }

    /// <summary>是否命中缓存。</summary>
    public static bool TryGet(
        long modId,
        long fileId,
        out string path,
        Func<string, bool>? validator = null)
    {
        path = GetCachePath(modId, fileId);
        if (IsUsable(path, validator))
        {
            return true;
        }

        // 兼容 WPF/Core 的旧缓存命名：命中后尽力复制到 Avalonia 目录，
        // 复制失败仍返回旧路径，不能因为缓存迁移失败而退化到浏览器下载。
        var legacyPath = GetLegacyCachePath(modId, fileId);
        if (!IsUsable(legacyPath, validator))
        {
            return false;
        }

        if (TryPromoteLegacyCache(legacyPath, path))
        {
            return true;
        }

        path = legacyPath;
        return true;
    }

    private static bool IsUsable(string path, Func<string, bool>? validator)
    {
        if (!IsValidCacheFile(path))
        {
            return false;
        }

        if (validator == null)
        {
            return true;
        }

        try
        {
            return validator(path);
        }
        catch
        {
            // 缓存校验器属于调用方的格式检查；校验异常按未命中处理，
            // 让上层继续走重新下载/浏览器回退，而不是把损坏缓存当作成功。
            return false;
        }
    }

    /// <summary>
    /// 查找某个 Nexus Mod 的任意有效缓存。
    ///
    /// 旧版整合包只保存了 ModID 或 SMAPI 版本文本，没有保存 FileID；这类
    /// 清单无法调用 TryGet(modId, fileId)，但只要缓存目录里已有完整文件，
    /// 仍然可以在打开浏览器前复用它。调用方负责用自己的格式校验器筛选文件。
    /// </summary>
    public static bool TryGetAnyForMod(
        long modId,
        Func<string, bool>? validator,
        out string path)
    {
        path = string.Empty;
        if (modId <= 0)
        {
            return false;
        }

        try
        {
            var prefix = $"{modId}_";
            var candidates = new List<string>();
            if (Directory.Exists(Root))
            {
                candidates.AddRange(Directory.EnumerateFiles(
                    Root,
                    $"{modId}_*.zip",
                    SearchOption.TopDirectoryOnly));
            }

            if (Directory.Exists(LegacyRoot))
            {
                candidates.AddRange(Directory.EnumerateFiles(
                    LegacyRoot,
                    $"mod_{modId}_*.zip",
                    SearchOption.TopDirectoryOnly));
            }

            var candidate = candidates
                .Where(file => Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Concat(candidates.Where(file =>
                    Path.GetFileName(file).StartsWith($"mod_{modId}_", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(file => IsUsable(file, validator))
                .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            if (candidate.StartsWith(LegacyRoot, StringComparison.OrdinalIgnoreCase) &&
                TryParseLegacyFileId(candidate, modId, out var legacyFileId))
            {
                var promotedPath = GetCachePath(modId, legacyFileId);
                if (TryPromoteLegacyCache(candidate, promotedPath))
                {
                    path = promotedPath;
                    return true;
                }
            }

            path = candidate;
            return true;
        }
        catch
        {
            path = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// 保存已下载文件到缓存（best-effort）。
    /// 可选校验器用于阻止 HTML/错误响应等非归档文件污染稳定缓存。
    /// </summary>
    public static void Save(
        long modId,
        long fileId,
        string sourceFile,
        Func<string, bool>? validator = null)
    {
        var tempPath = string.Empty;
        try
        {
            if (modId <= 0 || fileId <= 0 ||
                string.IsNullOrWhiteSpace(sourceFile) ||
                !IsValidCacheFile(sourceFile) ||
                (validator != null && !validator(sourceFile)))
            {
                return;
            }

            Directory.CreateDirectory(Root);
            var cachePath = GetCachePath(modId, fileId);
            tempPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.Copy(sourceFile, tempPath, overwrite: true);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch
        {
            // best-effort
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    /// <summary>清理 Nexus 缓存。</summary>
    public static void Clear()
    {
        DeleteDirectory(Root);
        DeleteDirectory(LegacyRoot);
    }

    private static bool TryPromoteLegacyCache(string sourcePath, string targetPath)
    {
        var tempPath = string.Empty;
        try
        {
            if (!IsValidCacheFile(sourcePath))
            {
                return false;
            }

            Directory.CreateDirectory(Root);
            tempPath = targetPath + ".migrate-" + Guid.NewGuid().ToString("N");
            File.Copy(sourcePath, tempPath, overwrite: true);
            File.Move(tempPath, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static bool TryParseLegacyFileId(string path, long modId, out long fileId)
    {
        return TryGetFileIdFromCachePath(path, modId, out fileId) &&
               Path.GetFileNameWithoutExtension(path)
                   .StartsWith($"mod_{modId}_", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool IsValidCacheFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
