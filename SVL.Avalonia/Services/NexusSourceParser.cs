namespace SVL.Avalonia.Services;

/// <summary>
/// 解析 Nexus 网页下载地址中的 Mod/File ID。
/// Modpack 与 Collection 的清单格式不同，但 Nexus 页面 URL 的兼容规则相同，
/// 统一入口可以避免一条链路支持 query 参数而另一条链路把 HTML 当压缩包下载。
/// </summary>
internal static class NexusSourceParser
{
    public static bool TryParsePageIds(
        string? url,
        out long modId,
        out long fileId)
    {
        if (!TryParseIds(url, out modId, out fileId))
        {
            return false;
        }

        return modId > 0 && fileId > 0;
    }

    /// <summary>仅解析 Nexus Mod 页面中的 Mod ID，供缺少 FileID 的旧清单走浏览器回退。</summary>
    public static bool TryParseModId(string? url, out long modId)
    {
        var parsed = TryParseIds(url, out modId, out _);
        return parsed && modId > 0;
    }

    private static bool TryParseIds(
        string? url,
        out long modId,
        out long fileId)
    {
        modId = 0;
        fileId = 0;
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            !IsNexusHost(uri.Host))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < segments.Length; index++)
        {
            if (segments[index].Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                _ = long.TryParse(segments[index + 1], out modId);
            }
            else if (segments[index].Equals("files", StringComparison.OrdinalIgnoreCase))
            {
                _ = long.TryParse(segments[index + 1], out fileId);
            }
        }

        foreach (var queryPart in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = queryPart.IndexOf('=');
            if (separator <= 0 || separator == queryPart.Length - 1)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(queryPart[..separator]);
            var value = Uri.UnescapeDataString(queryPart[(separator + 1)..]);
            var normalizedKey = key.Replace('-', '_').Replace(" ", string.Empty, StringComparison.Ordinal);
            if (normalizedKey.Equals("mod_id", StringComparison.OrdinalIgnoreCase) ||
                normalizedKey.Equals("modid", StringComparison.OrdinalIgnoreCase))
            {
                _ = long.TryParse(value, out modId);
            }
            else if (normalizedKey.Equals("file_id", StringComparison.OrdinalIgnoreCase) ||
                     normalizedKey.Equals("fileid", StringComparison.OrdinalIgnoreCase))
            {
                _ = long.TryParse(value, out fileId);
            }
        }

        return true;
    }

    private static bool IsNexusHost(string host)
    {
        return host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase);
    }
}
