using System.Globalization;
using System.Text.RegularExpressions;
using SVL.Avalonia.ViewModels;

namespace SVL.Avalonia.Services;

/// <summary>本地 Mod 冲突类型。</summary>
public enum ModConflictKind
{
    DuplicateId,
    MissingDependency,
    DisabledDependency,
    VersionMismatch,
    CircularDependency,
    FileConflict
}

/// <summary>一次本地 Mod 冲突的可展示结果。</summary>
public sealed record ModConflictResult(
    ModConflictKind Kind,
    string ModName,
    string RelatedModName,
    string Description)
{
    public string KindText => Kind switch
    {
        ModConflictKind.DuplicateId => "重复 ID",
        ModConflictKind.MissingDependency => "缺少前置",
        ModConflictKind.DisabledDependency => "前置已禁用",
        ModConflictKind.VersionMismatch => "前置版本不满足",
        ModConflictKind.CircularDependency => "循环依赖",
        ModConflictKind.FileConflict => "文件冲突",
        _ => "冲突"
    };

    public string DisplayText => string.IsNullOrWhiteSpace(RelatedModName)
        ? $"[{KindText}] {ModName}：{Description}"
        : $"[{KindText}] {ModName} ↔ {RelatedModName}：{Description}";
}

/// <summary>
/// 对已安装 Mod 做只读冲突分析。
/// 该类不改变文件或启用状态，方便在 UI 外单独回归测试。
/// </summary>
public static class ModConflictAnalyzer
{
    private static readonly Regex s_versionPartRegex = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ModConflictResult> Analyze(IEnumerable<ModManageItem> source)
    {
        var candidates = source
            .Where(IsCandidateMod)
            .ToList();
        var enabledMods = candidates
            .Where(item => item.IsEnabled)
            .ToList();

        var conflicts = new List<ModConflictResult>();
        DetectDuplicateIds(enabledMods, conflicts);
        DetectDependencyConflicts(enabledMods, candidates, conflicts);
        DetectCircularDependencies(enabledMods, candidates, conflicts);
        DetectFileConflicts(enabledMods, conflicts);

        return conflicts
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RelatedModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCandidateMod(ModManageItem item)
    {
        // 复合 Mod 的父项只是分组头，文件实际归属于子项；子项由其父项代表，
        // 否则同一份文件会在父/子之间产生大量误报。
        return item.IsNormalItem &&
               !item.IsChildMod &&
               !item.IsCompositeParent &&
               !string.IsNullOrWhiteSpace(item.FullPath) &&
               Directory.Exists(item.FullPath);
    }

    private static void DetectDuplicateIds(
        IReadOnlyList<ModManageItem> mods,
        ICollection<ModConflictResult> conflicts)
    {
        foreach (var group in mods
                     .Where(item => !string.IsNullOrWhiteSpace(item.UniqueId))
                     .GroupBy(item => item.UniqueId.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var duplicateMods = group
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var firstIndex = 0; firstIndex < duplicateMods.Count - 1; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < duplicateMods.Count; secondIndex++)
                {
                    conflicts.Add(new ModConflictResult(
                        ModConflictKind.DuplicateId,
                        GetModName(duplicateMods[firstIndex]),
                        GetModName(duplicateMods[secondIndex]),
                        $"两者都使用 UniqueID“{group.Key}”"));
                }
            }
        }
    }

    private static void DetectDependencyConflicts(
        IReadOnlyList<ModManageItem> enabledMods,
        IReadOnlyList<ModManageItem> allMods,
        ICollection<ModConflictResult> conflicts)
    {
        var installedById = allMods
            .Where(item => !string.IsNullOrWhiteSpace(item.UniqueId))
            .GroupBy(item => item.UniqueId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var mod in enabledMods)
        {
            foreach (var dependency in mod.DisplayDependencies.Where(item => item.IsRequired))
            {
                if (!installedById.TryGetValue(dependency.UniqueId, out var installed))
                {
                    conflicts.Add(new ModConflictResult(
                        ModConflictKind.MissingDependency,
                        GetModName(mod),
                        dependency.DisplayName,
                        $"需要“{dependency.DisplayText}”，但当前实例未安装"));
                    continue;
                }

                if (!installed.IsEnabled)
                {
                    conflicts.Add(new ModConflictResult(
                        ModConflictKind.DisabledDependency,
                        GetModName(mod),
                        GetModName(installed),
                        $"需要“{dependency.DisplayText}”，但该前置已禁用"));
                    continue;
                }

                if (IsVersionRequirementUnsatisfied(dependency.MinimumVersion, installed.Version))
                {
                    conflicts.Add(new ModConflictResult(
                        ModConflictKind.VersionMismatch,
                        GetModName(mod),
                        GetModName(installed),
                        $"需要最低版本 {dependency.MinimumVersion}，当前为 {installed.Version}"));
                }
            }
        }
    }

    private static void DetectFileConflicts(
        IReadOnlyList<ModManageItem> mods,
        ICollection<ModConflictResult> conflicts)
    {
        var filesByRelativePath = new Dictionary<string, List<ModManageItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(mod.FullPath, "*", SearchOption.AllDirectories)
                    .Select(path => new { Path = path, Relative = Path.GetRelativePath(mod.FullPath, path) })
                    .Where(item => !IsIgnoredAsset(item.Relative))
                    .Select(item => NormalizeRelativePath(item.Relative))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 单个目录不可读时不影响其他 Mod 的检测；管理页会继续显示该 Mod。
                continue;
            }

            foreach (var relativePath in files)
            {
                if (!filesByRelativePath.TryGetValue(relativePath, out var owners))
                {
                    owners = [];
                    filesByRelativePath[relativePath] = owners;
                }

                owners.Add(mod);
            }
        }

        foreach (var fileGroup in filesByRelativePath
                     .Where(pair => pair.Value.Count > 1)
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var owners = fileGroup.Value
                .Distinct()
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var firstIndex = 0; firstIndex < owners.Count - 1; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < owners.Count; secondIndex++)
                {
                    conflicts.Add(new ModConflictResult(
                        ModConflictKind.FileConflict,
                        GetModName(owners[firstIndex]),
                        GetModName(owners[secondIndex]),
                        $"都包含文件“{fileGroup.Key}”"));
                }
            }
        }
    }

    private static void DetectCircularDependencies(
        IReadOnlyList<ModManageItem> enabledMods,
        IReadOnlyList<ModManageItem> allMods,
        ICollection<ModConflictResult> conflicts)
    {
        var installedById = allMods
            .Where(item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.UniqueId))
            .GroupBy(item => item.UniqueId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var path = new List<ModManageItem>();
        var reportedCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(ModManageItem mod)
        {
            var key = GetDependencyKey(mod);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (states.TryGetValue(key, out var state))
            {
                if (state != VisitState.Visiting)
                {
                    return;
                }

                var cycleStart = path.FindIndex(item =>
                    string.Equals(GetDependencyKey(item), key, StringComparison.OrdinalIgnoreCase));
                if (cycleStart < 0)
                {
                    return;
                }

                var cycle = path.Skip(cycleStart).ToList();
                var cycleKey = string.Join("|", cycle
                    .Select(GetDependencyKey)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
                if (reportedCycles.Add(cycleKey))
                {
                    var cycleNames = cycle
                        .Select(GetModName)
                        .Append(GetModName(mod))
                        .ToList();
                    conflicts.Add(new ModConflictResult(
                        ModConflictKind.CircularDependency,
                        GetModName(cycle[0]),
                        GetModName(mod),
                        $"依赖链“{string.Join(" → ", cycleNames)}”形成循环"));
                }

                return;
            }

            states[key] = VisitState.Visiting;
            path.Add(mod);
            foreach (var dependency in mod.DisplayDependencies.Where(item =>
                         item.IsRequired &&
                         !string.IsNullOrWhiteSpace(item.UniqueId) &&
                         installedById.TryGetValue(item.UniqueId, out _)))
            {
                if (!installedById.TryGetValue(dependency.UniqueId, out var dependencyMod))
                {
                    continue;
                }

                Visit(dependencyMod);
            }

            path.RemoveAt(path.Count - 1);
            states[key] = VisitState.Visited;
        }

        foreach (var mod in enabledMods)
        {
            Visit(mod);
        }
    }

    private static string GetDependencyKey(ModManageItem mod)
    {
        return string.IsNullOrWhiteSpace(mod.UniqueId)
            ? NormalizeRelativePath(mod.FullPath)
            : mod.UniqueId.Trim();
    }

    private static bool IsIgnoredAsset(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        return fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static bool IsVersionRequirementUnsatisfied(string? minimumVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion) || minimumVersion.Trim() == "*")
        {
            return false;
        }

        if (!TryGetVersionParts(minimumVersion, out var required) ||
            !TryGetVersionParts(installedVersion, out var installed))
        {
            // 版本字段不是标准数字版本时不武断报错；缺失/禁用状态仍会正常报告。
            return false;
        }

        var length = Math.Max(required.Count, installed.Count);
        for (var index = 0; index < length; index++)
        {
            var requiredPart = index < required.Count ? required[index] : 0;
            var installedPart = index < installed.Count ? installed[index] : 0;
            if (installedPart != requiredPart)
            {
                return installedPart < requiredPart;
            }
        }

        return false;
    }

    private static bool TryGetVersionParts(string? text, out List<int> parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("未知版本", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var matches = s_versionPartRegex.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var part))
            {
                return false;
            }

            parts.Add(part);
        }

        return parts.Count > 0;
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }

    private static string GetModName(ModManageItem item)
    {
        return string.IsNullOrWhiteSpace(item.DisplayName)
            ? (string.IsNullOrWhiteSpace(item.DirectoryName) ? item.UniqueId : item.DirectoryName)
            : item.DisplayName;
    }
}
