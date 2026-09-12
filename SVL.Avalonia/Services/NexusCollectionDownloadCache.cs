using System.Security.Cryptography;
using System.Text;

namespace SVL.Avalonia.Services;

/// <summary>
/// Nexus Collection 本体缓存：按游戏、Collection slug 和 revision 建立稳定键。
/// Collection API 返回的下载地址通常是短期地址，不能只依赖 URL 哈希，否则再次
/// 导入同一 Collection 时仍会重复解析 API 或打开浏览器。
/// </summary>
public static class NexusCollectionDownloadCache
{
    public static string Root { get; } = Path.Combine(
        NexusDownloadCache.Root, "collections");

    public static string GetCachePath(
        string gameDomain,
        string collectionSlug,
        int revision)
    {
        var key = string.Join(
            "\n",
            (gameDomain ?? string.Empty).Trim().ToLowerInvariant(),
            (collectionSlug ?? string.Empty).Trim().ToLowerInvariant(),
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        return Path.Combine(Root, $"collection-{digest}.zip");
    }

    public static bool TryGet(
        string gameDomain,
        string collectionSlug,
        int revision,
        out string path,
        Func<string, bool>? validator = null)
    {
        path = GetCachePath(gameDomain, collectionSlug, revision);
        if (!IsUsable(path, validator))
        {
            path = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>保存已完整下载并通过校验的 Collection 归档。</summary>
    public static void Save(
        string gameDomain,
        string collectionSlug,
        int revision,
        string sourceFile,
        Func<string, bool>? validator = null)
    {
        var temporaryPath = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(collectionSlug) ||
                string.IsNullOrWhiteSpace(sourceFile) ||
                !IsUsable(sourceFile, validator))
            {
                return;
            }

            Directory.CreateDirectory(Root);
            var cachePath = GetCachePath(gameDomain, collectionSlug, revision);
            temporaryPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.Copy(sourceFile, temporaryPath, overwrite: true);
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch
        {
            // 缓存属于加速能力，写入失败不应影响已经成功的下载/安装流程。
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // best-effort
            }
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
