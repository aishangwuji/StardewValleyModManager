using System.Diagnostics;
using System.IO.Compression;

namespace SVL.Avalonia.Services;

/// <summary>
/// 从目标 Base 路径中已有的 SMAPI 实例生成一个临时安装包。
/// 用于整合包导入时的离线复用，避免 SMAPI 目录服务不可用导致整个安装流程中止。
/// </summary>
internal static class InstalledSmapiPackageBuilder
{
    public static bool TryCreate(
        string gameBasePath,
        string? preferredVersion,
        string? excludedInstanceName,
        string outputDirectory,
        out string packagePath,
        out string selectedRuntimePath,
        out string selectedVersion)
    {
        packagePath = string.Empty;
        selectedRuntimePath = string.Empty;
        selectedVersion = string.Empty;

        // 任务状态/旧配置可能把 versions/<实例> 或其 game 子目录传进来；
        // 复用器必须始终从所属 Base 扫描所有候选实例。
        gameBasePath = InstanceRuntimePathResolver.ResolveBasePath(gameBasePath);
        var candidates = FindCandidates(gameBasePath, excludedInstanceName);
        var orderedCandidates = OrderCandidates(candidates, preferredVersion).ToList();
        if (orderedCandidates.Count == 0)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch
        {
            return false;
        }

        // 某个实例目录可能包含正在使用或损坏的文件。不能因为排序第一的
        // 候选无法打包，就放弃其它已安装的 SMAPI 实例。
        foreach (var selected in orderedCandidates)
        {
            var safeVersion = InstanceRuntimePathResolver.SanitizeFileNameComponent(
                string.IsNullOrWhiteSpace(selected.Version) ? preferredVersion : selected.Version,
                "installed");
            var candidatePackagePath = Path.Combine(outputDirectory, $"SMAPI-installed-{safeVersion}.zip");
            try
            {
                CreatePackage(selected.RuntimePath, candidatePackagePath);
                if (!File.Exists(candidatePackagePath) || new FileInfo(candidatePackagePath).Length == 0)
                {
                    throw new InvalidDataException("生成的 SMAPI 复用安装包为空");
                }

                packagePath = candidatePackagePath;
                selectedRuntimePath = selected.RuntimePath;
                selectedVersion = selected.Version;
                return true;
            }
            catch
            {
                try
                {
                    if (File.Exists(candidatePackagePath))
                    {
                        File.Delete(candidatePackagePath);
                    }
                }
                catch
                {
                    // best-effort 清理，然后继续尝试下一个候选实例
                }
            }
        }

        return false;
    }

    private static List<SmapiCandidate> FindCandidates(string gameBasePath, string? excludedInstanceName)
    {
        var candidates = new List<SmapiCandidate>();
        if (string.IsNullOrWhiteSpace(gameBasePath) || !Directory.Exists(gameBasePath))
        {
            return candidates;
        }

        AddCandidate(gameBasePath, instanceName: null, excludedInstanceName, candidates);

        var versionsPath = Path.Combine(gameBasePath, "versions");
        if (!Directory.Exists(versionsPath))
        {
            return candidates;
        }

        IEnumerable<string> versionRoots;
        try
        {
            versionRoots = Directory.EnumerateDirectories(versionsPath).ToList();
        }
        catch
        {
            // versions 目录中的单个权限/IO 问题不应影响 Base 根目录候选；
            // 也不应让整合包安装在这里直接抛出异常。
            return candidates;
        }

        foreach (var versionRoot in versionRoots)
        {
            try
            {
                var instanceName = Path.GetFileName(versionRoot);
                if (instanceName.StartsWith(".svl-delete-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(excludedInstanceName) &&
                    string.Equals(instanceName, excludedInstanceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var runtimePath = InstanceRuntimePathResolver.Resolve(versionRoot);
                AddCandidate(runtimePath, instanceName, excludedInstanceName, candidates);
            }
            catch
            {
                // 跳过当前损坏/无权限实例，继续扫描其它版本。
            }
        }

        return candidates;
    }

    private static void AddCandidate(
        string runtimePath,
        string? instanceName,
        string? excludedInstanceName,
        ICollection<SmapiCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(runtimePath) || !Directory.Exists(runtimePath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(instanceName) &&
            !string.IsNullOrWhiteSpace(excludedInstanceName) &&
            string.Equals(instanceName, excludedInstanceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var markerPath = new[]
            {
                Path.Combine(runtimePath, "StardewModdingAPI.exe"),
                Path.Combine(runtimePath, "StardewModdingAPI.dll"),
                Path.Combine(runtimePath, "StardewModdingAPI")
            }
            .FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return;
        }

        var version = TryGetFileVersion(markerPath);
        if (string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(instanceName))
        {
            version = ExtractVersion(instanceName);
        }

        if (candidates.Any(c => string.Equals(c.RuntimePath, runtimePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(new SmapiCandidate(runtimePath, instanceName ?? string.Empty, version));
    }

    private static IEnumerable<SmapiCandidate> OrderCandidates(
        IReadOnlyCollection<SmapiCandidate> candidates,
        string? preferredVersion)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var preferred = NormalizeVersion(preferredVersion);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var exact = candidates.Where(candidate =>
                string.Equals(NormalizeVersion(candidate.Version), preferred, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeVersion(candidate.InstanceName), preferred, StringComparison.OrdinalIgnoreCase));

            // SMAPI 同一大版本内通常向前兼容；优先使用不低于清单要求的最近版本。
            var preferredParsed = TryParseVersion(preferred);
            var compatible = candidates
                .Where(candidate => IsCompatible(preferredParsed, TryParseVersion(candidate.Version)))
                .OrderBy(candidate => TryParseVersion(candidate.Version) ?? new Version(0, 0, 0))
                .ThenByDescending(candidate => GetLastWriteTimeUtc(candidate.RuntimePath));

            return DistinctCandidates(exact.Concat(compatible));
        }

        return candidates
            .OrderByDescending(candidate => TryParseVersion(candidate.Version) ?? new Version(0, 0, 0))
            .ThenByDescending(candidate => GetLastWriteTimeUtc(candidate.RuntimePath));
    }

    private static IEnumerable<SmapiCandidate> DistinctCandidates(IEnumerable<SmapiCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (seen.Add(candidate.RuntimePath))
            {
                yield return candidate;
            }
        }
    }

    private static DateTime GetLastWriteTimeUtc(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool IsCompatible(Version? preferred, Version? candidate)
    {
        if (preferred == null || candidate == null)
        {
            return false;
        }

        return candidate.Major == preferred.Major && candidate >= preferred;
    }

    private static void CreatePackage(string runtimePath, string packagePath)
    {
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var platformName = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macOS"
            : "linux";
        var payloadEntry = archive.CreateEntry(
            $"SMAPI installed/installer/internal/{platformName}/install.dat",
            CompressionLevel.Fastest);

        using var payloadStream = payloadEntry.Open();
        using var payload = new MemoryStream();
        using (var payloadArchive = new ZipArchive(payload, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(runtimePath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(runtimePath, file);
                if (ShouldSkip(relative))
                {
                    continue;
                }

                var entry = payloadArchive.CreateEntry(relative.Replace(Path.DirectorySeparatorChar, '/'), CompressionLevel.Fastest);
                using var source = File.OpenRead(file);
                using var target = entry.Open();
                source.CopyTo(target);
            }
        }

        payload.Position = 0;
        payload.CopyTo(payloadStream);
    }

    private static bool ShouldSkip(string relativePath)
    {
        var firstSegment = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        return string.Equals(firstSegment, "Content", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstSegment, "Mods", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".svl-", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetFileVersion(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return NormalizeVersion(version);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractVersion(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value, @"\d+(?:\.\d+){1,3}");
        return match.Success ? match.Value : string.Empty;
    }

    private static string NormalizeVersion(string? value)
    {
        var extracted = ExtractVersion(value?.Trim() ?? string.Empty);
        return extracted;
    }

    private static Version? TryParseVersion(string? value)
    {
        return Version.TryParse(NormalizeVersion(value), out var version) ? version : null;
    }

    private sealed record SmapiCandidate(string RuntimePath, string InstanceName, string Version);
}
