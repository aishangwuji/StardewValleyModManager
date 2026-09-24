using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SVL.Avalonia.Models;

namespace SVL.Avalonia.Services;

/// <summary>
/// 定位 SMAPI 生成的日志文件。
///
/// SMAPI 把最近一次会话日志写到 &lt;游戏数据目录&gt;/ErrorLogs/SMAPI-latest.txt，
/// 崩溃时额外写 SMAPI-crash.txt。游戏数据目录随平台不同：
/// Windows 为 %AppData%\StardewValley，Linux/macOS 为 ~/.config/StardewValley。
/// 本类只做只读扫描，不修改任何文件。
/// </summary>
public static class SmapiLogLocator
{
    private const long MinCandidateSize = 1; // 空文件不作为候选

    /// <summary>返回按优先级排序的候选日志（崩溃日志在前，同类型按修改时间倒序）。</summary>
    public static IReadOnlyList<SmapiLogFile> Locate()
    {
        var results = new List<SmapiLogFile>();

        foreach (var directory in EnumerateErrorLogDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                // 目录不可读时跳过，不影响其他候选目录。
                continue;
            }

            foreach (var file in files)
            {
                if (TryDescribe(file, out var descriptor) && descriptor is not null)
                {
                    results.Add(descriptor);
                }
            }
        }

        return results
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => KindPriority(f.Kind))
            .ThenByDescending(f => f.LastWriteUtc)
            .ToList();
    }

    /// <summary>选择最值得分析的日志：崩溃日志优先，其次最新会话日志。</summary>
    public static SmapiLogFile? SelectBest(IReadOnlyList<SmapiLogFile> files)
    {
        if (files is null || files.Count == 0)
        {
            return null;
        }

        return files
            .OrderBy(f => KindPriority(f.Kind))
            .ThenByDescending(f => f.LastWriteUtc)
            .FirstOrDefault();
    }

    /// <summary>从任意路径构造候选描述（供“手动导入日志”使用）。</summary>
    public static bool TryDescribe(string path, out SmapiLogFile? descriptor)
    {
        descriptor = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists || info.Length < MinCandidateSize)
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        descriptor = new SmapiLogFile(
            info.FullName,
            ClassifyKind(info.Name),
            info.LastWriteTimeUtc,
            info.Length);
        return true;
    }

    /// <summary>按文件名推断日志类型。</summary>
    public static SmapiLogKind ClassifyKind(string fileName)
    {
        if (fileName.Contains("crash", StringComparison.OrdinalIgnoreCase))
        {
            return SmapiLogKind.Crash;
        }

        if (fileName.Contains("latest", StringComparison.OrdinalIgnoreCase))
        {
            return SmapiLogKind.Latest;
        }

        if (fileName.Contains("console", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("log", StringComparison.OrdinalIgnoreCase))
        {
            return SmapiLogKind.Console;
        }

        return SmapiLogKind.Imported;
    }

    /// <summary>返回游戏数据目录下的 ErrorLogs 候选路径（存在性由调用方判断）。</summary>
    public static IReadOnlyList<string> EnumerateErrorLogDirectories()
    {
        var directories = new List<string>();

        var appData = SafeGetFolder(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            directories.Add(Path.Combine(appData, "StardewValley", "ErrorLogs"));
        }

        if (!OperatingSystem.IsWindows())
        {
            var home = SafeGetFolder(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                directories.Add(Path.Combine(home, ".config", "StardewValley", "ErrorLogs"));
                directories.Add(Path.Combine(home, "Library", "Application Support", "StardewValley", "ErrorLogs"));
            }
        }

        return directories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int KindPriority(SmapiLogKind kind) => kind switch
    {
        SmapiLogKind.Crash => 0,
        SmapiLogKind.Latest => 1,
        SmapiLogKind.Console => 2,
        _ => 3
    };

    private static string SafeGetFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
