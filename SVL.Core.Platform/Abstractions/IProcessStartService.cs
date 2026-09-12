using System.Diagnostics;

namespace SVL.Core.Platform.Abstractions;

/// <summary>
/// 启动进程并返回句柄，供需要等待进程生命周期或访问主窗口的功能使用。
/// 保留在独立接口中，避免破坏只需要布尔结果的外部进程调用方。
/// </summary>
public interface IProcessStartService
{
    Process? TryStartProcess(string fileName, string arguments, string? workingDirectory = null);
}
