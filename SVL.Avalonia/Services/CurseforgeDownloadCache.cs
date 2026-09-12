namespace SVL.Avalonia.Services;

/// <summary>
/// CurseForge 资源下载缓存：使用稳定的 ProjectID/FileID 作为缓存键。
///
/// CurseForge CDN 地址可能随时间变化，不能只依赖 HttpDownloadService 的 URL
/// 哈希缓存；只要来源身份不变，就应该可以复用已经校验过的归档。
/// </summary>
public static class CurseforgeDownloadCache
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SVL", "Avalonia", "cache", "curseforge");

    public static string GetCachePath(long projectId, long fileId)
        => Path.Combine(Root, $"cf-{projectId}-{fileId}.zip");

    /// <summary>获取有效缓存；validator 可用于阻止 HTML/错误响应冒充 Mod 归档。</summary>
    public static bool TryGet(
        long projectId,
        long fileId,
        out string path,
        Func<string, bool>? validator = null)
    {
        path = GetCachePath(projectId, fileId);
        if (!IsUsable(path, validator))
        {
            path = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>保存已完成下载的归档；失败或格式不正确时静默忽略。</summary>
    public static void Save(
        long projectId,
        long fileId,
        string sourceFile,
        Func<string, bool>? validator = null)
    {
        var tempPath = string.Empty;
        try
        {
            if (projectId <= 0 || fileId <= 0 ||
                string.IsNullOrWhiteSpace(sourceFile) ||
                !IsUsable(sourceFile, validator))
            {
                return;
            }

            Directory.CreateDirectory(Root);
            var cachePath = GetCachePath(projectId, fileId);
            tempPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.Copy(sourceFile, tempPath, overwrite: true);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch
        {
            // 缓存属于加速能力，写入失败不应影响已经成功的安装流程。
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

    public static void Clear()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool IsUsable(string path, Func<string, bool>? validator)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                return false;
            }

            return validator == null || validator(path);
        }
        catch
        {
            return false;
        }
    }
}
