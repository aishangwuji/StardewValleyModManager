using System;

namespace SVL.Avalonia.Models;

public sealed class SmapiVersionEntry
{
    public string Version { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    public DateTime PublishedDate { get; set; }

    public bool IsPrerelease { get; set; }

    /// <summary>
    /// 来源文件 ID：NexusMods 用于 NXM 回调匹配，CurseForge 用于 ProjectID/FileID 稳定缓存。
    /// GitHub 来源通常没有文件 ID。
    /// </summary>
    public long? FileId { get; set; }

    /// <summary>用户选择的目标安装路径（由 SmapiVersionPickerDialog 路径下拉框设置）。</summary>
    public string TargetPath { get; set; } = string.Empty;
}
