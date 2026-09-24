using System;
using System.IO;

namespace SVL.Avalonia.Services;

/// <summary>
/// Mods 目录内容签名，用于判断目录是否发生变化，从而在未变化时跳过昂贵的重扫
/// （逐个读取 manifest.json、解析依赖、重建集合）。
///
/// 签名 = 根目录与所有子项（限制深度）的最大 <see cref="FileSystemInfo.LastWriteTimeUtc"/>
/// 与条目总数的组合：既能感知新增/修改（文件 mtime 变化），也能感知删除
/// （父目录 mtime 变化或条目数变化）。
/// </summary>
public static class ModsFolderSignature
{
    /// <summary>计算目录签名；无法枚举（不存在/无权限）时返回空字符串，调用方应视为“需要重扫”。</summary>
    public static string Compute(string directoryPath, int maxDepth = 4)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return string.Empty;
        }

        try
        {
            var root = new DirectoryInfo(directoryPath);
            var maxTicks = root.LastWriteTimeUtc.Ticks;
            var count = 0;
            Walk(root, 0, maxDepth, ref maxTicks, ref count);
            return $"{maxTicks}|{count}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Walk(DirectoryInfo directory, int depth, int maxDepth, ref long maxTicks, ref int count)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            count++;
            var ticks = entry.LastWriteTimeUtc.Ticks;
            if (ticks > maxTicks)
            {
                maxTicks = ticks;
            }

            if (depth < maxDepth && entry is DirectoryInfo subDirectory)
            {
                Walk(subDirectory, depth + 1, maxDepth, ref maxTicks, ref count);
            }
        }
    }
}
