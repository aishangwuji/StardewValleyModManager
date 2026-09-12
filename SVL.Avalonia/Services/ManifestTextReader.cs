using System.Text;

namespace SVL.Avalonia.Services;

/// <summary>
/// 读取 Mod/整合包清单文本。
///
/// 大多数清单是 UTF-8，但旧 Mod 和第三方导出器也会写入 UTF-16；还有少数
/// 文件没有 BOM，不能只依赖 StreamReader 的 BOM 自动识别，否则后续 JSON
/// 解析会失败，管理页和导出页只能显示“未知版本”。
/// </summary>
public static class ManifestTextReader
{
    public static string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Decode(File.ReadAllBytes(path));
    }

    public static async Task<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Decode(bytes);
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        }

        if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(bytes, 4, bytes.Length - 4);
        }

        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // JSON 的 ASCII 结构字符在无 BOM 的 UTF-16 文件中通常每隔一个字节出现
        // 0x00。只在样本足够大且一侧明显占优时启用推断，避免把普通 UTF-8
        // 文本中的偶发零字节误判为 UTF-16。
        if (LooksLikeUtf16(bytes, littleEndian: true))
        {
            return Encoding.Unicode.GetString(bytes);
        }

        if (LooksLikeUtf16(bytes, littleEndian: false))
        {
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeUtf16(byte[] bytes, bool littleEndian)
    {
        if (bytes.Length < 8)
        {
            return false;
        }

        var sampleLength = Math.Min(bytes.Length, 8192);
        var zeroOnHighByte = 0;
        var zeroOnLowByte = 0;
        var pairCount = sampleLength / 2;

        for (var index = 0; index + 1 < sampleLength; index += 2)
        {
            var lowByte = littleEndian ? bytes[index] : bytes[index + 1];
            var highByte = littleEndian ? bytes[index + 1] : bytes[index];
            if (lowByte == 0)
            {
                zeroOnLowByte++;
            }

            if (highByte == 0)
            {
                zeroOnHighByte++;
            }
        }

        return pairCount >= 4 &&
               zeroOnHighByte >= Math.Max(2, pairCount / 4) &&
               zeroOnHighByte > zeroOnLowByte * 2;
    }
}
