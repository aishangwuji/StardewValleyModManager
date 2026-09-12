using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SVL.Core.Platform.IO;

/// <summary>
/// 跨平台归档解压工具。
/// 只承载平台层必须的 7z 解压能力；zip/cfmodpack 仍由上层的 ZipExtractor 处理，
/// 这样可以继续保留现有的 ZipFile → SharpZipLib 回退策略。
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>
    /// 当前归档是否为 ZIP 文件。
    /// 除了 .zip/.cfmodpack 扩展名，还检查 ZIP 的文件头；部分下载地址没有保留
    /// 文件名，不能因为缺少扩展名而把合法 ZIP 当成普通文件。
    /// </summary>
    public static bool IsZip(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return false;
        }

        var extension = Path.GetExtension(archivePath);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".cfmodpack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!File.Exists(archivePath))
        {
            return false;
        }

        try
        {
            // PK\x03\x04：普通 ZIP；PK\x05\x06/PK\x07\x08：空 ZIP 或分卷 ZIP。
            Span<byte> signature = stackalloc byte[4];
            using var stream = File.OpenRead(archivePath);
            return stream.Read(signature) == signature.Length &&
                   signature[0] == (byte)'P' &&
                   signature[1] == (byte)'K' &&
                   ((signature[2] == 0x03 && signature[3] == 0x04) ||
                    (signature[2] == 0x05 && signature[3] == 0x06) ||
                    (signature[2] == 0x07 && signature[3] == 0x08));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 当前归档是否为 7z 文件。
    /// 除了扩展名，还检查 7z 的文件签名；部分 Nexus/CurseForge CDN 下载地址
    /// 没有保留 .7z 后缀，不能因此把合法归档误交给 ZIP 解压器。
    /// </summary>
    public static bool IsSevenZip(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return false;
        }

        if (!File.Exists(archivePath))
        {
            return false;
        }

        try
        {
            // 7z signature: 37 7A BC AF 27 1C
            Span<byte> signature = stackalloc byte[6];
            using var stream = File.OpenRead(archivePath);
            return stream.Read(signature) == signature.Length &&
                   signature[0] == 0x37 &&
                   signature[1] == 0x7A &&
                   signature[2] == 0xBC &&
                   signature[3] == 0xAF &&
                   signature[4] == 0x27 &&
                   signature[5] == 0x1C;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 使用 SharpCompress 解压 7z。不会跟随越界路径，避免恶意归档写出目标目录。
    /// </summary>
    public static void ExtractSevenZipToDirectory(string archivePath, string destinationDirectory)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("归档文件不存在", archivePath);
        }

        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var archive = SevenZipArchive.OpenArchive(archivePath, new ReaderOptions());
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            var relativePath = entry.Key
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"7z 归档包含越界路径: {entry.Key}");
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.WriteToFile(destinationPath, new ExtractionOptions { Overwrite = true });
        }
    }
}
