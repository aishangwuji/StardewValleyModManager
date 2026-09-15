using System.Globalization;
using System.IO;
using System.Text.Json;
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
    [Obsolete("星露谷各 Mod 目录物理隔离，跨目录相对路径比对易造成伪误报，已弃用")]
    FileConflict,
    AssetConflict,
    IncompatibleMod
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
        ModConflictKind.AssetConflict => "CP资产冲突",
        ModConflictKind.IncompatibleMod => "已知互斥",
#pragma warning disable CS0618
        ModConflictKind.FileConflict => "文件冲突",
#pragma warning restore CS0618
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

    private static readonly JsonDocumentOptions s_jsonDocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] s_incompatibleFieldNames =
    [
        "Incompatible",
        "Incompatibilities",
        "Conflicts",
        "IncompatibleMods"
    ];

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
        DetectIncompatibleModConflicts(enabledMods, conflicts);
        DetectContentPatcherAssetConflicts(enabledMods, conflicts);

        return conflicts
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RelatedModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCandidateMod(ModManageItem item)
    {
        // 复合 Mod 的父项只是分组头，实际文件和 manifest 归属于子项；
        // 普通 Mod 和复合子项均作为有效 Mod 参与检测。
        return item.IsNormalItem &&
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

    private static void DetectIncompatibleModConflicts(
        IReadOnlyList<ModManageItem> enabledMods,
        ICollection<ModConflictResult> conflicts)
    {
        var modsById = enabledMods
            .Where(item => !string.IsNullOrWhiteSpace(item.UniqueId))
            .GroupBy(item => item.UniqueId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var reportedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in enabledMods)
        {
            var incompatibleIds = ExtractIncompatibleIds(mod);
            foreach (var targetId in incompatibleIds)
            {
                if (modsById.TryGetValue(targetId, out var conflictingMod))
                {
                    if (ReferenceEquals(mod, conflictingMod))
                    {
                        continue;
                    }

                    var modNameA = GetModName(mod);
                    var modNameB = GetModName(conflictingMod);
                    var pairKey = string.Compare(mod.UniqueId, conflictingMod.UniqueId, StringComparison.OrdinalIgnoreCase) < 0
                        ? $"{mod.UniqueId}|{conflictingMod.UniqueId}"
                        : $"{conflictingMod.UniqueId}|{mod.UniqueId}";

                    if (reportedPairs.Add(pairKey))
                    {
                        conflicts.Add(new ModConflictResult(
                            ModConflictKind.IncompatibleMod,
                            modNameA,
                            modNameB,
                            $"明确声明与“{modNameB}”互斥不兼容"));
                    }
                }
            }
        }
    }

    private static HashSet<string> ExtractIncompatibleIds(ModManageItem mod)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(mod.FullPath) || !Directory.Exists(mod.FullPath))
        {
            return ids;
        }

        var manifestPath = Path.Combine(mod.FullPath, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                var text = File.ReadAllText(manifestPath);
                using var doc = JsonDocument.Parse(text, s_jsonDocOptions);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var fieldName in s_incompatibleFieldNames)
                    {
                        if (TryGetCaseInsensitiveProperty(root, fieldName, out var element) &&
                            element.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in element.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    var val = item.GetString()?.Trim();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        ids.Add(val);
                                    }
                                }
                                else if (item.ValueKind == JsonValueKind.Object)
                                {
                                    if (TryGetCaseInsensitiveProperty(item, "UniqueID", out var idElem) &&
                                        idElem.ValueKind == JsonValueKind.String)
                                    {
                                        var val = idElem.GetString()?.Trim();
                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            ids.Add(val);
                                        }
                                    }
                                    else if (TryGetCaseInsensitiveProperty(item, "UniqueId", out var idElem2) &&
                                             idElem2.ValueKind == JsonValueKind.String)
                                    {
                                        var val = idElem2.GetString()?.Trim();
                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            ids.Add(val);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // manifest 读取异常不中断冲突检测流程
            }
        }

        var sourcePath = Path.Combine(mod.FullPath, "svl-source.json");
        if (File.Exists(sourcePath))
        {
            try
            {
                var text = File.ReadAllText(sourcePath);
                using var doc = JsonDocument.Parse(text, s_jsonDocOptions);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    TryGetCaseInsensitiveProperty(root, "hardConflicts", out var conflictsElem) &&
                    conflictsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in conflictsElem.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var val = item.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                ids.Add(val);
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.Object &&
                                 TryGetCaseInsensitiveProperty(item, "id", out var idElem) &&
                                 idElem.ValueKind == JsonValueKind.String)
                        {
                            var val = idElem.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                ids.Add(val);
                            }
                        }
                    }
                }
            }
            catch
            {
                // svl-source 读取异常忽略
            }
        }

        return ids;
    }

    private static void DetectContentPatcherAssetConflicts(
        IReadOnlyList<ModManageItem> enabledMods,
        ICollection<ModConflictResult> conflicts)
    {
        var assetsByMod = new Dictionary<ModManageItem, HashSet<string>>();

        foreach (var mod in enabledMods)
        {
            var assets = ExtractLoadedAssets(mod.FullPath);
            if (assets.Count > 0)
            {
                assetsByMod[mod] = assets;
            }
        }

        if (assetsByMod.Count < 2)
        {
            return;
        }

        var modsWithAssets = assetsByMod.Keys.ToList();
        var reportedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < modsWithAssets.Count - 1; i++)
        {
            var modA = modsWithAssets[i];
            var assetsA = assetsByMod[modA];

            for (var j = i + 1; j < modsWithAssets.Count; j++)
            {
                var modB = modsWithAssets[j];
                var assetsB = assetsByMod[modB];

                var commonAssets = assetsA.Intersect(assetsB, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (commonAssets.Count > 0)
                {
                    var modAName = GetModName(modA);
                    var modBName = GetModName(modB);
                    var pairKey = $"{modAName}|{modBName}";

                    if (reportedPairs.Add(pairKey))
                    {
                        var description = commonAssets.Count == 1
                            ? $"都尝试独占加载（Action: Load）游戏资产“{commonAssets[0]}”"
                            : $"都尝试独占加载（Action: Load）游戏资产“{commonAssets[0]}”等 {commonAssets.Count} 项资产";

                        conflicts.Add(new ModConflictResult(
                            ModConflictKind.AssetConflict,
                            modAName,
                            modBName,
                            description));
                    }
                }
            }
        }
    }

    private static HashSet<string> ExtractLoadedAssets(string modDir)
    {
        var loadedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(modDir) || !Directory.Exists(modDir))
        {
            return loadedAssets;
        }

        var contentJsonPath = Path.Combine(modDir, "content.json");
        if (!File.Exists(contentJsonPath))
        {
            return loadedAssets;
        }

        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ParseContentChangesFile(contentJsonPath, modDir, loadedAssets, visitedFiles, 0);
        return loadedAssets;
    }

    private static void ParseContentChangesFile(
        string filePath,
        string modDir,
        ISet<string> loadedAssets,
        ISet<string> visitedFiles,
        int depth)
    {
        if (depth > 4)
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch
        {
            return;
        }

        if (!visitedFiles.Add(fullPath) || !File.Exists(fullPath))
        {
            return;
        }

        try
        {
            var content = File.ReadAllText(fullPath);
            using var doc = JsonDocument.Parse(content, s_jsonDocOptions);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!TryGetCaseInsensitiveProperty(root, "Changes", out var changesElement) ||
                changesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var change in changesElement.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetCaseInsensitiveProperty(change, "Action", out var actionElement) ||
                    actionElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var action = actionElement.GetString()?.Trim();
                if (string.Equals(action, "Load", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetCaseInsensitiveProperty(change, "Target", out var targetElement) &&
                        targetElement.ValueKind == JsonValueKind.String)
                    {
                        var target = NormalizeAssetTarget(targetElement.GetString());
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            loadedAssets.Add(target);
                        }
                    }

                    if (TryGetCaseInsensitiveProperty(change, "Targets", out var targetsElement) &&
                        targetsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in targetsElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var target = NormalizeAssetTarget(item.GetString());
                                if (!string.IsNullOrWhiteSpace(target))
                                {
                                    loadedAssets.Add(target);
                                }
                            }
                        }
                    }
                }
                else if (string.Equals(action, "Include", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetCaseInsensitiveProperty(change, "FromFile", out var fromFileElement) &&
                        fromFileElement.ValueKind == JsonValueKind.String)
                    {
                        var fromFile = fromFileElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(fromFile))
                        {
                            var includedPath = Path.Combine(modDir, fromFile);
                            ParseContentChangesFile(includedPath, modDir, loadedAssets, visitedFiles, depth + 1);
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略损坏的 JSON 格式或语法错误
        }
    }

    private static string NormalizeAssetTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        return target.Replace('\\', '/').Trim().Trim('/');
    }

    private static bool TryGetCaseInsensitiveProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) ||
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 保留历史实现供向后兼容，但不再参与常规冲突分析，避免因同名文件导致大面积伪误报。
    /// </summary>
    [Obsolete("星露谷各 Mod 目录物理隔离，跨目录相对路径比对易造成大量伪误报，已弃用")]
    public static void DetectFileConflicts(
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
