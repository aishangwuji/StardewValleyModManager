using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SVL.Core.Platform.Services;

/// <summary>
/// 游戏窗口标题占位符服务。Windows 使用 user32 设置已启动游戏的标题，
/// 其它平台只提供文本替换能力，不调用平台专属窗口 API。
/// </summary>
public static class WindowTitleService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string text);

    public static IReadOnlyDictionary<string, PlaceholderInfo> Placeholders { get; } =
        new Dictionary<string, PlaceholderInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["ver"] = new("<ver>", "原版游戏版本（如 1.6.15）"),
            ["smver"] = new("<smver>", "SMAPI 版本（如 4.5.2）"),
            ["modscount"] = new("<modscount>", "已加载的 Mod 数量"),
            ["name"] = new("<name>", "实例名称")
        };

    public static string ReplacePlaceholders(
        string? template,
        string? gameVersion,
        string? smapiVersion,
        int modsCount,
        string? instanceName)
    {
        var result = string.IsNullOrWhiteSpace(template) ? "Stardew Valley" : template;
        result = result.Replace("<ver>", string.IsNullOrWhiteSpace(gameVersion) ? "未知版本" : gameVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("<smver>", string.IsNullOrWhiteSpace(smapiVersion) ? "未知" : smapiVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("<modscount>", Math.Max(0, modsCount).ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("<name>", string.IsNullOrWhiteSpace(instanceName) ? "实例" : instanceName.Trim(), StringComparison.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>等待进程主窗口出现；返回句柄，超时或进程退出时返回空句柄。</summary>
    public static async Task<IntPtr> WaitForMainWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (process == null || !OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (process.HasExited)
                {
                    return IntPtr.Zero;
                }

                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return IntPtr.Zero;
    }

    /// <summary>等待游戏窗口并设置标题。失败不会抛出平台 API 异常。</summary>
    public static async Task<bool> SetWindowTitleAsync(
        Process process,
        string? titleTemplate,
        string? instanceName,
        string? gamePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (process == null || !OperatingSystem.IsWindows())
        {
            return false;
        }

        var info = ReadGameInfo(gamePath);
        var title = ReplacePlaceholders(
            titleTemplate,
            info.GameVersion,
            info.SmapiVersion,
            info.ModsCount,
            instanceName);
        var handle = await WaitForMainWindowAsync(
            process,
            timeout ?? TimeSpan.FromSeconds(60),
            cancellationToken);
        return handle != IntPtr.Zero && SetWindowText(handle, title);
    }

    public static Task<IntPtr> WaitForGameWindowAsync(
        Process process,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return WaitForMainWindowAsync(
            process,
            timeout ?? TimeSpan.FromSeconds(30),
            cancellationToken);
    }

    private static GameInfo ReadGameInfo(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return new GameInfo();
        }

        var gameVersion = ReadFileVersion(Path.Combine(gamePath, "Stardew Valley.exe"));
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            gameVersion = ReadFileVersion(Path.Combine(gamePath, "Stardew Valley.dll"));
        }

        var smapiVersion = ReadFileVersion(Path.Combine(gamePath, "StardewModdingAPI.exe"));
        if (string.IsNullOrWhiteSpace(smapiVersion))
        {
            smapiVersion = ReadFileVersion(Path.Combine(gamePath, "StardewModdingAPI.dll"));
        }

        return new GameInfo(
            NormalizeVersion(gameVersion),
            NormalizeVersion(smapiVersion),
            CountMods(gamePath));
    }

    private static string ReadFileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = Regex.Match(value, @"^(\d+\.\d+\.\d+)");
        return match.Success ? match.Groups[1].Value : value.Trim();
    }

    private static int CountMods(string gamePath)
    {
        var modsPath = Path.Combine(gamePath, "Mods");
        if (!Directory.Exists(modsPath))
        {
            return 0;
        }

        try
        {
            return Directory.GetDirectories(modsPath)
                .Count(path => File.Exists(Path.Combine(path, "manifest.json")));
        }
        catch
        {
            return 0;
        }
    }

    public sealed record PlaceholderInfo(string Tag, string Description);

    private sealed record GameInfo(string GameVersion = "", string SmapiVersion = "", int ModsCount = 0);
}
