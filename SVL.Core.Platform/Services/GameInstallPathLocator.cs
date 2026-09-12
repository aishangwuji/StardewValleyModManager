using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SVL.Core.Platform.Abstractions;

public sealed class GameInstallPathLocator : IGameInstallPathLocator
{
    public string? TryLocateSteamStardewPath()
    {
        foreach (var path in GetSteamCandidatePaths())
        {
            if (IsValidGamePath(path))
            {
                return path;
            }
        }

        return null;
    }

    public string? TryLocateGogStardewPath()
    {
        foreach (var path in GetGogCandidatePaths())
        {
            if (IsValidGamePath(path))
            {
                return path;
            }
        }

        return null;
    }

    public string? TryLocateXboxStardewPath()
    {
        foreach (var path in GetXboxCandidatePaths())
        {
            if (IsValidGamePath(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSteamCandidatePaths()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var roots = new[]
            {
                Path.Combine(home, "Library", "Application Support", "Steam"),
                Path.Combine(home, ".steam", "steam")
            };

            foreach (var candidate in EnumerateSteamGameCandidates(roots))
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }

            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var roots = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
            };

            foreach (var candidate in EnumerateSteamGameCandidates(roots))
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }

            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            var roots = new List<string>
            {
                Path.Combine(programFilesX86, "Steam"),
                Path.Combine(programFiles, "Steam")
            };

            // Steam 可以安装在任意目录。旧 WPF 版本会读取注册表中的真实
            // SteamPath/InstallPath；只扫描固定盘符目录会漏掉最常见的自定义安装。
            foreach (var registryRoot in GetWindowsRegistryValues(
                         ("HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath"),
                         ("HKEY_CURRENT_USER\\Software\\Valve\\Steam", "InstallPath"),
                         ("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam", "InstallPath"),
                         ("HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam", "InstallPath")))
            {
                roots.Add(registryRoot);
            }

            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "Steam"));
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"));
            }

            foreach (var candidate in EnumerateSteamGameCandidates(roots))
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> GetGogCandidatePaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // GOG Galaxy 及旧版 GOG 安装器都可能把 PATH 写入不同的注册表分支。
            // 注册表读取失败时由后面的固定路径继续兜底。
            foreach (var registryPath in GetWindowsRegistryValues(
                         ("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\GOG.com\\Games\\1453375253", "PATH"),
                         ("HKEY_LOCAL_MACHINE\\SOFTWARE\\GOG.com\\Games\\1453375253", "PATH"),
                         ("HKEY_CURRENT_USER\\Software\\GOG.com\\Games\\1453375253", "PATH")))
            {
                yield return registryPath;
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            yield return Path.Combine(programFilesX86, "GOG Galaxy", "Games", "Stardew Valley");
            yield return Path.Combine(programFiles, "GOG Galaxy", "Games", "Stardew Valley");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GOG Games", "Stardew Valley");
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return "/Applications/Stardew Valley.app/Contents/MacOS";
            yield return Path.Combine(home, "Applications", "Stardew Valley.app", "Contents", "MacOS");
            yield return Path.Combine(home, "GOG Games", "Stardew Valley");
            yield break;
        }

        var linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(linuxHome, "GOG Games", "Stardew Valley");
        yield return Path.Combine(linuxHome, "Games", "Stardew Valley");
    }

    private static IEnumerable<string> GetXboxCandidatePaths()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        var roots = new[]
        {
            // Xbox app / Game Pass 的可修改游戏目录。
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ModifiableWindowsApps", "StardewValley"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ModifiableWindowsApps", "StardewValley"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "StardewValley"),
            // 新版 Xbox App 常见的默认库目录。
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XboxGames", "Stardew Valley"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "XboxGames", "Stardew Valley")
        };

        foreach (var candidate in EnumerateXboxGameCandidates(roots))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> EnumerateXboxGameCandidates(IEnumerable<string> roots)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !yielded.Add(root))
            {
                continue;
            }

            // Store/Xbox 包通常把实际文件放在 Content；同时保留根目录候选，
            // 兼容用户把可修改目录直接指向游戏内容的安装方式。
            yield return Path.Combine(root, "Content");
            yield return root;

            if (!Directory.Exists(root))
            {
                continue;
            }

            string[] children;
            try
            {
                // 目录枚举是延迟执行的，必须在 try 内物化；否则受保护的
                // WindowsApps 目录会把异常推迟到 foreach，导致整个探测中断。
                children = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                yield return Path.Combine(child, "Content");
                yield return child;
            }
        }
    }

    private static IEnumerable<string> GetWindowsRegistryValues(
        params (string KeyPath, string ValueName)[] locations)
    {
        var values = new List<string>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return values;
        }

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyPath, valueName) in locations)
        {
            try
            {
                var value = Microsoft.Win32.Registry.GetValue(keyPath, valueName, null)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var normalized = value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (yielded.Add(normalized))
                    {
                        values.Add(normalized);
                    }
                }
            }
            catch
            {
                // 注册表分支不存在、权限不足或平台 API 不可用时继续尝试其它来源。
            }
        }

        return values;
    }

    private static IEnumerable<string> EnumerateSteamGameCandidates(IEnumerable<string> steamRoots)
    {
        foreach (var root in steamRoots.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            foreach (var steamAppsPath in GetSteamAppsDirectories(root))
            {
                var commonPath = Path.Combine(steamAppsPath, "common", "Stardew Valley");
                yield return commonPath;
                yield return Path.Combine(commonPath, "Stardew Valley.app", "Contents", "MacOS");
                yield return Path.Combine(commonPath, "Contents");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && Directory.Exists(commonPath))
                {
                    foreach (var appBundle in Directory.EnumerateDirectories(commonPath, "*.app", SearchOption.TopDirectoryOnly))
                    {
                        yield return Path.Combine(appBundle, "Contents", "MacOS");
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetSteamAppsDirectories(string steamRoot)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultSteamApps = Path.Combine(steamRoot, "steamapps");
        if (Directory.Exists(defaultSteamApps) && yielded.Add(defaultSteamApps))
        {
            yield return defaultSteamApps;
        }

        var libraryFoldersVdf = Path.Combine(defaultSteamApps, "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersVdf))
        {
            if (Directory.Exists(steamRoot) && Path.GetFileName(steamRoot).Equals("steamapps", StringComparison.OrdinalIgnoreCase) && yielded.Add(steamRoot))
            {
                yield return steamRoot;
            }

            yield break;
        }

        string content;
        try
        {
            content = File.ReadAllText(libraryFoldersVdf);
        }
        catch
        {
            yield break;
        }

        var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var pathRaw = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(pathRaw))
            {
                continue;
            }

            var normalizedRoot = pathRaw.Replace("\\\\", "\\");
            var appsPath = Path.Combine(normalizedRoot, "steamapps");
            if (Directory.Exists(appsPath) && yielded.Add(appsPath))
            {
                yield return appsPath;
            }
        }
    }

    private static bool IsValidGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                var appRootCandidates = new[]
                {
                    Path.Combine(path, "Contents", "MacOS", "StardewValley"),
                    Path.Combine(path, "Contents", "MacOS", "Stardew Valley")
                };

                if (appRootCandidates.Any(File.Exists))
                {
                    return true;
                }
            }

            if (Path.GetFileName(path).Equals("Contents", StringComparison.OrdinalIgnoreCase))
            {
                var contentsCandidates = new[]
                {
                    Path.Combine(path, "MacOS", "StardewValley"),
                    Path.Combine(path, "MacOS", "Stardew Valley")
                };

                if (contentsCandidates.Any(File.Exists))
                {
                    return true;
                }
            }

            var appExecutableCandidates = new[]
            {
                Path.Combine(path, "Stardew Valley.app", "Contents", "MacOS", "StardewValley"),
                Path.Combine(path, "Stardew Valley.app", "Contents", "MacOS", "Stardew Valley")
            };

            if (appExecutableCandidates.Any(File.Exists))
            {
                return true;
            }
        }

        var markers = new[]
        {
            "Stardew Valley.dll",
            "Stardew Valley.deps.json",
            "Stardew Valley.exe",
            "Stardew Valley",
            "StardewValley.exe",
            "StardewValley",
            "StardewModdingAPI.exe",
            "StardewModdingAPI"
        };

        return markers.Any(marker => File.Exists(Path.Combine(path, marker)));
    }
}
