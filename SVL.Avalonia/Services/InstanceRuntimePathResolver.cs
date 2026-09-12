namespace SVL.Avalonia.Services;

/// <summary>
/// 解析版本隔离实例的实际运行时目录。
/// 新布局直接使用 versions/&lt;name&gt;，旧布局使用 versions/&lt;name&gt;/game；
/// 只有在 game 目录确实包含游戏文件时才将其视为旧布局。
/// </summary>
public static class InstanceRuntimePathResolver
{
    private static readonly char[] WindowsInvalidFileNameChars =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Resolve(string versionRoot)
    {
        if (string.IsNullOrWhiteSpace(versionRoot))
        {
            return string.Empty;
        }

        var legacyRuntimePath = Path.Combine(versionRoot, "game");
        return IsValidGamePath(legacyRuntimePath) ? legacyRuntimePath : versionRoot;
    }

    /// <summary>
    /// 将 Base、versions/&lt;实例&gt; 或旧布局的 versions/&lt;实例&gt;/game 统一解析为所属 Base。
    /// 任务状态和旧版配置中可能保存的是实例运行目录，而安装整合包时必须把
    /// 新版本追加到 Base/versions 下，不能在实例目录下再创建一层 versions。
    /// </summary>
    public static string ResolveBasePath(string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return string.Empty;
        }

        var normalized = candidatePath.Trim().Trim('"');
        try
        {
            normalized = Path.GetFullPath(normalized)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        try
        {
            var current = new DirectoryInfo(normalized);
            while (current != null)
            {
                if (string.Equals(current.Name, "versions", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Parent?.FullName ?? normalized;
                }

                current = current.Parent;
            }
        }
        catch
        {
            // 路径格式异常时保留规范化后的原路径，交由上层做存在性检查。
        }

        return normalized;
    }

    public static string SanitizeFileNameComponent(string? value, string fallback = "unknown")
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(WindowsInvalidFileNameChars)
            .Distinct()
            .ToArray();
        var sanitized = string.Concat(candidate.Select(c => invalidChars.Contains(c) ? '_' : c));
        sanitized = sanitized.Trim().Trim('.');

        if (string.IsNullOrWhiteSpace(sanitized) || IsReservedDeviceName(sanitized))
        {
            return fallback;
        }

        return sanitized;
    }

    private static bool IsValidGamePath(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var markers = new[]
        {
            "Stardew Valley.dll",
            "Stardew Valley.deps.json",
            "Stardew Valley.exe",
            "StardewValley.exe",
            "StardewValley",
            "Stardew Valley",
            "Stardew Valley.app"
        };

        return markers.Any(marker =>
            File.Exists(Path.Combine(path, marker)) ||
            Directory.Exists(Path.Combine(path, marker)));
    }

    private static bool IsReservedDeviceName(string value)
    {
        var name = value.Split('.', 2)[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
               ((name.StartsWith("COM", StringComparison.Ordinal) ||
                 name.StartsWith("LPT", StringComparison.Ordinal)) &&
                name.Length == 4 && int.TryParse(name[3..], out var number) && number is >= 1 and <= 9);
    }
}
