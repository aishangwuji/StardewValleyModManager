namespace SVL.Core.Platform.Abstractions;

public interface IGameInstallPathLocator
{
    string? TryLocateSteamStardewPath();

    string? TryLocateGogStardewPath();

    /// <summary>
    /// 尝试定位 Xbox/Microsoft Store 版本的游戏目录。
    /// 默认实现保持第三方路径定位器的二进制兼容，旧实现可以暂不提供该来源。
    /// </summary>
    string? TryLocateXboxStardewPath() => null;
}
