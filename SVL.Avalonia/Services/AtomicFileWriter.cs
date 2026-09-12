using System.Text;

namespace SVL.Avalonia.Services;

/// <summary>
/// 将小型持久化文件写入临时文件并一次性替换目标文件，避免进程中断时留下半截 JSON。
/// </summary>
internal static class AtomicFileWriter
{
    public static void WriteUtf8(string targetPath, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(contents);

        var fullTargetPath = Path.GetFullPath(targetPath);
        var parentDirectory = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var temporaryPath = fullTargetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       options: FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            // .NET 10 的 File.Move(overwrite:true) 在 Windows 和 Unix 上都能保留原子替换语义。
            File.Move(temporaryPath, fullTargetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // 目标文件已经替换成功时，残留临时文件不应影响主流程。
            }
        }
    }
}
