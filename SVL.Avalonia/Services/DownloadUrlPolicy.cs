namespace SVL.Avalonia.Services;

/// <summary>
/// 下载来源 URL 的安全判定。
///
/// CurseForge 的文件页和 API 有时会带有类似 .zip 的路径片段，但返回内容
/// 仍然是 HTML/JSON。所有安装入口必须先使用同一套规则判断，避免某个入口
/// 把页面保存成压缩包后再污染缓存。
/// </summary>
public static class DownloadUrlPolicy
{
    /// <summary>判断 URL 是否属于 CurseForge/curse.tools/Forge CDN 来源。</summary>
    public static bool IsLikelyCurseforgeUrl(string? value)
    {
        if (!TryParseHttpUri(value, out var uri))
        {
            return false;
        }

        return IsForgeCdnHost(uri.Host) || IsCurseforgeHost(uri.Host);
    }

    /// <summary>
    /// 判断 CurseForge 来源 URL 是否已经是可直接下载的归档地址。
    /// 已知 CurseForge 页面/API 无论路径是否带 .zip/.7z 都拒绝；Forge CDN
    /// 和非 CurseForge 的明确归档或 /download 直链才放行。
    /// </summary>
    public static bool IsLikelyCurseforgeDirectDownloadUrl(string? value)
    {
        if (!TryParseHttpUri(value, out var uri))
        {
            return false;
        }

        if (IsForgeCdnHost(uri.Host))
        {
            return true;
        }

        // 必须在归档扩展名判断之前过滤 CurseForge host；API 路径可能伪装成
        // /files/123/foo.zip，但响应实际仍是 JSON。
        if (IsCurseforgeHost(uri.Host))
        {
            return false;
        }

        return HasArchiveExtension(uri.AbsolutePath) ||
               uri.AbsolutePath.Contains("/download", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 通用来源 URL 的直链判断。CurseForge 仍使用严格规则，其它来源保留
    /// 历史上的“归档后缀或 /download 路径”兼容行为。
    /// </summary>
    public static bool IsLikelyDirectDownloadUrl(string? value)
    {
        if (!TryParseHttpUri(value, out var uri))
        {
            return false;
        }

        if (IsForgeCdnHost(uri.Host))
        {
            return true;
        }

        if (IsCurseforgeHost(uri.Host))
        {
            return false;
        }

        return HasArchiveExtension(uri.AbsolutePath) ||
               uri.AbsolutePath.Contains("/download", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseHttpUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsForgeCdnHost(string host)
    {
        var normalized = host.Trim().TrimEnd('.');
        return normalized.Equals("forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurseforgeHost(string host)
    {
        var normalized = host.Trim().TrimEnd('.');
        return normalized.Equals("curseforge.com", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".curseforge.com", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("curse.tools", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".curse.tools", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasArchiveExtension(string path)
    {
        return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
    }
}
