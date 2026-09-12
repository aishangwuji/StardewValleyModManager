using System.Globalization;
using System.Text.RegularExpressions;

namespace SVL.Avalonia.Services;

/// <summary>
/// 从在线详情页的“下载项”文本中恢复稳定的文件身份。
/// 详情项同时承担 UI 显示和队列传参，旧版本来源会把 FileID 写在文件名、
/// URL 查询串或 cf-project-file 目录名中，因此这里集中处理这些历史形态。
/// </summary>
public static class DownloadOptionIdentityParser
{
    private static readonly Regex FileIdPattern = new(
        @"(?:[/\\]files[/\\]|[?&](?:file[_-]?id)\s*[=:]\s*|\bfile(?:[\s_-]*id)?\s*[:#]?\s*)(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CurseForgeTokenPattern = new(
        @"\bcf[-_\s](?<project>\d+)[-_\s](?<file>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FilePrefixPattern = new(
        @"^file\s+\d+\s*(?:[:#|_-]\s*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CurseForgePrefixPattern = new(
        @"^cf[-_\s]\d+[-_\s]\d+\s*(?:[:|_-]\s*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryExtractFileId(string? option, out long fileId)
    {
        fileId = 0;
        if (string.IsNullOrWhiteSpace(option))
        {
            return false;
        }

        var normalized = option.Trim();
        try
        {
            normalized = Uri.UnescapeDataString(normalized);
        }
        catch (UriFormatException)
        {
            // 保留原始文本继续解析，畸形的百分号编码不应使整个下载项失效。
        }

        var match = FileIdPattern.Match(normalized);
        if (!match.Success)
        {
            match = CurseForgeTokenPattern.Match(normalized);
            if (!match.Success)
            {
                return false;
            }

            return long.TryParse(
                       match.Groups["file"].Value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out fileId) &&
                   fileId > 0;
        }

        return long.TryParse(
                   match.Groups["id"].Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out fileId) &&
               fileId > 0;
    }

    public static string StripGeneratedFilePrefix(string? title)
    {
        var value = title?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return value;
        }

        var stripped = FilePrefixPattern.Replace(value, string.Empty).Trim();
        stripped = CurseForgePrefixPattern.Replace(stripped, string.Empty).Trim();

        // 只有 ID、没有可读文件名时，清理会得到空串或“.zip”。保留原始
        // 文件名更安全，至少不会把下载目标变成无效的扩展名文件。
        return string.IsNullOrWhiteSpace(stripped) || stripped.StartsWith(".", StringComparison.Ordinal)
            ? value
            : stripped;
    }
}
