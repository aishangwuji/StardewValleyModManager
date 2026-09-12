using System;
using System.Collections.Generic;
using System.Linq;
using SVL.Core.Stardew.Mod;

namespace SVL.Core.Stardew.Mod.Dependency;

public class ModConflictDetector
{
    public class ConflictResult
    {
        public string ModId1 { get; set; }
        public string ModId2 { get; set; }
        public ConflictType Type { get; set; }
        public string Description { get; set; }
    }

    public enum ConflictType
    {
        DuplicateId,
        FileConflict,
        DependencyConflict,
        VersionMismatch
    }

    public static List<ConflictResult> DetectConflicts(List<SdVMod> mods)
    {
        var conflicts = new List<ConflictResult>();

        foreach (var duplicateGroup in mods
                     .Where(m => m.IsEnabled && !string.IsNullOrWhiteSpace(m.UniqueId))
                     .GroupBy(m => m.UniqueId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var modsWithDuplicateId = duplicateGroup.ToList();

            for (int i = 0; i < modsWithDuplicateId.Count - 1; i++)
            {
                for (int j = i + 1; j < modsWithDuplicateId.Count; j++)
                {
                    conflicts.Add(new ConflictResult
                    {
                        ModId1 = modsWithDuplicateId[i].UniqueId,
                        ModId2 = modsWithDuplicateId[j].UniqueId,
                        Type = ConflictType.DuplicateId,
                        Description = $"Duplicate UniqueID: {duplicateGroup.Key}"
                    });
                }
            }
        }

        var dependencyConflicts = ModDependencyResolver.ResolveDependencies(mods)
            .Where(r => r.Status != ModDependencyResolver.ResolutionStatus.Satisfied)
            .Select(r => new ConflictResult
            {
                ModId1 = r.ModId,
                ModId2 = r.Version,
                Type = ConflictType.DependencyConflict,
                Description = r.Message
            })
            .ToList();

        conflicts.AddRange(dependencyConflicts);

        var fileConflicts = DetectFileConflicts(mods);
        conflicts.AddRange(fileConflicts);

        return conflicts;
    }

    private static List<ConflictResult> DetectFileConflicts(List<SdVMod> mods)
    {
        var conflicts = new List<ConflictResult>();
        var enabledMods = mods.Where(m => m.IsEnabled).ToList();

        for (var firstIndex = 0; firstIndex < enabledMods.Count - 1; firstIndex++)
        {
            var mod1 = enabledMods[firstIndex];
            for (var secondIndex = firstIndex + 1; secondIndex < enabledMods.Count; secondIndex++)
            {
                var mod2 = enabledMods[secondIndex];
                if (string.Equals(mod1.UniqueId, mod2.UniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (HasFileConflict(mod1, mod2))
                {
                    conflicts.Add(new ConflictResult
                    {
                        ModId1 = mod1.UniqueId,
                        ModId2 = mod2.UniqueId,
                        Type = ConflictType.FileConflict,
                        Description = $"File conflict between {mod1.Name} and {mod2.Name}"
                    });
                }
            }
        }

        return conflicts;
    }

    private static bool HasFileConflict(SdVMod mod1, SdVMod mod2)
    {
        var mod1Files = GetModFiles(mod1);
        var mod2Files = GetModFiles(mod2);

        var commonFiles = mod1Files.Intersect(mod2Files, StringComparer.OrdinalIgnoreCase).ToList();

        if (commonFiles.Count == 0)
        {
            return false;
        }

        var conflictingFiles = commonFiles.Where(f => !IsAssetFile(f)).ToList();

        return conflictingFiles.Count > 0;
    }

    private static List<string> GetModFiles(SdVMod mod)
    {
        var files = new List<string>();
        var modPath = mod.ModPath;

        if (!System.IO.Directory.Exists(modPath))
        {
            return files;
        }

        try
        {
            files.AddRange(System.IO.Directory
                .GetFiles(modPath, "*", System.IO.SearchOption.AllDirectories)
                .Select(path => GetRelativeFilePath(modPath, path)
                    .Replace(System.IO.Path.DirectorySeparatorChar, '/')));
        }
        catch (System.IO.IOException)
        {
            // 单个损坏/被占用的 Mod 目录不应阻止其它目录继续检测。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上，权限不足时把该目录视为没有可扫描文件。
        }

        return files;
    }

    private static string GetRelativeFilePath(string rootPath, string filePath)
    {
        var normalizedRoot = System.IO.Path.GetFullPath(rootPath)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) +
            System.IO.Path.DirectorySeparatorChar;
        var normalizedFile = System.IO.Path.GetFullPath(filePath);
        return normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedFile.Substring(normalizedRoot.Length)
            : System.IO.Path.GetFileName(normalizedFile);
    }

    private static bool IsAssetFile(string fileName)
    {
        var lowerName = System.IO.Path.GetFileName(fileName).ToLowerInvariant();
        return lowerName == "icon.png" ||
               lowerName == "manifest.json" ||
               lowerName.EndsWith(".cs") ||
               lowerName.EndsWith(".dll");
    }

    public static bool HasConflicts(List<SdVMod> mods)
    {
        var conflicts = DetectConflicts(mods);
        return conflicts.Count > 0;
    }
}
