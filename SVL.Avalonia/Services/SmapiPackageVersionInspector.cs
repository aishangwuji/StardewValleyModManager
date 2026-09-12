using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SVL.Avalonia.Services;

/// <summary>
/// 从 SMAPI 安装包内部路径读取版本，供没有版本信息的 Nexus 缓存复用判断使用。
/// NexusDownloadCache 的文件名是“ModID_FileID.zip”，不能依赖文件名推断版本。
/// </summary>
internal static class SmapiPackageVersionInspector
{
    private static readonly Regex VersionRegex = new(
        @"(?:^|[/\\])SMAPI[ ._-]*(?<version>\d+(?:\.\d+){1,3})(?:[^0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GenericVersionRegex = new(
        @"(?<!\d)(?<version>\d+(?:\.\d+){1,3})(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? TryReadVersion(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                var match = VersionRegex.Match(entry.FullName);
                if (match.Success)
                {
                    return match.Groups["version"].Value;
                }
            }
        }
        catch
        {
            // 上层会继续按普通未命中处理，不能让单个损坏缓存阻断其它来源。
        }

        var fileNameMatch = GenericVersionRegex.Match(Path.GetFileNameWithoutExtension(archivePath));
        return fileNameMatch.Success ? fileNameMatch.Groups["version"].Value : null;
    }

    public static bool IsCompatible(string? preferredVersion, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(preferredVersion))
        {
            return true;
        }

        var preferred = ParseVersion(preferredVersion);
        var candidate = ParseVersion(TryReadVersion(archivePath));

        // 有明确目标版本时，无法从缓存包确认版本就不能安全复用。
        if (preferred == null || candidate == null)
        {
            return preferred == null;
        }

        return candidate.Major == preferred.Major && candidate >= preferred;
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("SMAPI", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["SMAPI".Length..].Trim(' ', '.', '_', '-');
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }
}
