using System;
using System.Collections.Generic;
using System.Linq;
using SVL.Core.Stardew.Mod;

namespace SVL.Core.Stardew.Mod.Dependency;

public class ModDependencyResolver
{
    public class DependencyResolution
    {
        public string ModId { get; set; }
        public string Version { get; set; }
        public ResolutionStatus Status { get; set; }
        public string Message { get; set; }
    }

    public enum ResolutionStatus
    {
        Satisfied,
        MissingDependency,
        VersionConflict,
        CircularDependency
    }

    public static List<DependencyResolution> ResolveDependencies(List<SdVMod> mods)
    {
        var resolutions = new List<DependencyResolution>();
        // 冲突检测会单独报告重复 UniqueID；解析阶段取首个有效项，避免
        // ToDictionary 直接抛异常导致整个 Mod 列表无法加载。
        var modMap = mods
            .Where(mod => !string.IsNullOrWhiteSpace(mod.UniqueId))
            .GroupBy(mod => mod.UniqueId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            if (!mod.IsEnabled)
            {
                continue;
            }

            if (mod.Manifest?.Dependencies == null || mod.Manifest.Dependencies.Count == 0)
            {
                continue;
            }

            foreach (var dependency in mod.Manifest.Dependencies)
            {
                var resolution = CheckDependency(modMap, dependency, mod);
                resolutions.Add(resolution);
            }
        }

        // 依赖项逐条满足并不代表整体可加载；A -> B -> A 时，旧实现会把
        // 两条依赖都判定为 Satisfied，导致冲突检测和 ValidateDependencies
        // 错误放行。把循环作为独立结果返回，供旧版冲突检测器复用。
        foreach (var cycle in FindCircularDependencies(modMap))
        {
            resolutions.Add(new DependencyResolution
            {
                ModId = cycle.ModId,
                Version = "*",
                Status = ResolutionStatus.CircularDependency,
                Message = cycle.Message
            });
        }

        return resolutions;
    }

    public static bool ValidateDependencies(List<SdVMod> mods)
    {
        var resolutions = ResolveDependencies(mods);
        return resolutions.All(r => r.Status == ResolutionStatus.Satisfied);
    }

    private static DependencyResolution CheckDependency(Dictionary<string, SdVMod> modMap, ModDependency dependency, SdVMod dependentMod)
    {
        var dependencyId = dependency.UniqueId?.Trim();
        if (string.IsNullOrWhiteSpace(dependencyId) ||
            !modMap.TryGetValue(dependencyId, out var dependencyMod))
        {
            return new DependencyResolution
            {
                ModId = dependency.UniqueId,
                Version = dependency.MinimumVersion ?? "*",
                Status = ResolutionStatus.MissingDependency,
                Message = $"Required mod '{dependency.UniqueId}' not found"
            };
        }

        if (!dependencyMod.IsEnabled)
        {
            return new DependencyResolution
            {
                ModId = dependency.UniqueId,
                Version = dependency.MinimumVersion ?? "*",
                Status = ResolutionStatus.MissingDependency,
                Message = $"Required mod '{dependency.UniqueId}' is disabled"
            };
        }

        if (string.IsNullOrEmpty(dependency.MinimumVersion))
        {
            return new DependencyResolution
            {
                ModId = dependency.UniqueId,
                Version = "*",
                Status = ResolutionStatus.Satisfied,
                Message = string.Empty
            };
        }

        if (!IsVersionSatisfied(dependencyMod.Version, dependency.MinimumVersion))
        {
            return new DependencyResolution
            {
                ModId = dependency.UniqueId,
                Version = dependency.MinimumVersion,
                Status = ResolutionStatus.VersionConflict,
                Message = $"Version mismatch: requires {dependency.MinimumVersion}, found {dependencyMod.Version}"
            };
        }

        return new DependencyResolution
        {
            ModId = dependency.UniqueId,
            Version = dependency.MinimumVersion ?? "*",
            Status = ResolutionStatus.Satisfied,
            Message = string.Empty
        };
    }

    private static bool IsVersionSatisfied(string installedVersion, string requiredVersion)
    {
        if (string.IsNullOrEmpty(requiredVersion) || requiredVersion == "*")
        {
            return true;
        }

        return CompareVersions(installedVersion, requiredVersion) >= 0;
    }

    private static List<CircularDependency> FindCircularDependencies(Dictionary<string, SdVMod> modMap)
    {
        var enabledMods = modMap.Values
            .Where(mod => mod.IsEnabled)
            .ToList();
        var states = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var path = new List<SdVMod>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<CircularDependency>();

        void Visit(SdVMod mod)
        {
            var key = mod.UniqueId.Trim();
            if (states.TryGetValue(key, out var state))
            {
                if (state != VisitState.Visiting)
                {
                    return;
                }

                var start = path.FindIndex(item =>
                    string.Equals(item.UniqueId.Trim(), key, StringComparison.OrdinalIgnoreCase));
                if (start < 0)
                {
                    return;
                }

                var cycle = path.Skip(start).ToList();
                var cycleKey = string.Join("|", cycle
                    .Select(item => item.UniqueId.Trim())
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
                if (reported.Add(cycleKey))
                {
                    var chain = cycle
                        .Select(item => item.UniqueId.Trim())
                        .Append(key);
                    cycles.Add(new CircularDependency(
                        cycle[0].UniqueId.Trim(),
                        $"Circular dependency: {string.Join(" -> ", chain)}"));
                }

                return;
            }

            states[key] = VisitState.Visiting;
            path.Add(mod);
            foreach (var dependency in mod.Manifest?.Dependencies ?? new List<ModDependency>())
            {
                var dependencyId = dependency.UniqueId?.Trim();
                if (!string.IsNullOrWhiteSpace(dependencyId) &&
                    modMap.TryGetValue(dependencyId, out var dependencyMod) &&
                    dependencyMod.IsEnabled)
                {
                    Visit(dependencyMod);
                }
            }

            path.RemoveAt(path.Count - 1);
            states[key] = VisitState.Visited;
        }

        foreach (var mod in enabledMods)
        {
            Visit(mod);
        }

        return cycles;
    }

    private static int CompareVersions(string version1, string version2)
    {
        var v1 = (version1 ?? string.Empty).Split('.');
        var v2 = (version2 ?? string.Empty).Split('.');

        for (int i = 0; i < Math.Max(v1.Length, v2.Length); i++)
        {
            var part1 = i < v1.Length ? v1[i] : "0";
            var part2 = i < v2.Length ? v2[i] : "0";
            var n1 = int.TryParse(part1, out var num1) ? num1 : 0;
            var n2 = int.TryParse(part2, out var num2) ? num2 : 0;

            if (n1 != n2)
            {
                return n1.CompareTo(n2);
            }
        }

        return 0;
    }

    public static List<SdVMod> GetLoadOrder(List<SdVMod> mods)
    {
        var sorted = new List<SdVMod>();
        var enabledMods = mods
            .Where(mod => mod.IsEnabled && !string.IsNullOrWhiteSpace(mod.UniqueId))
            .GroupBy(mod => mod.UniqueId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var modMap = enabledMods.ToDictionary(mod => mod.UniqueId.Trim(), mod => mod, StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var cycleNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Visit(SdVMod mod)
        {
            var modId = mod.UniqueId.Trim();
            if (states.TryGetValue(modId, out var state))
            {
                if (state == VisitState.Visited)
                {
                    return !cycleNodes.Contains(modId);
                }

                if (state == VisitState.Visiting)
                {
                    cycleNodes.Add(modId);
                    return false;
                }
            }

            states[modId] = VisitState.Visiting;
            var canLoad = true;
            foreach (var dependency in mod.Manifest?.Dependencies ?? [])
            {
                var dependencyId = dependency.UniqueId?.Trim();
                if (string.IsNullOrWhiteSpace(dependencyId) ||
                    !modMap.TryGetValue(dependencyId, out var dependencyMod))
                {
                    continue;
                }

                if (!Visit(dependencyMod))
                {
                    canLoad = false;
                }
            }

            states[modId] = VisitState.Visited;
            if (canLoad && !cycleNodes.Contains(modId))
            {
                sorted.Add(mod);
                return true;
            }

            cycleNodes.Add(modId);
            return false;
        }

        foreach (var mod in enabledMods)
        {
            Visit(mod);
        }

        return sorted;
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }

    private sealed class CircularDependency
    {
        public CircularDependency(string modId, string message)
        {
            ModId = modId;
            Message = message;
        }

        public string ModId { get; }
        public string Message { get; }
    }
}
