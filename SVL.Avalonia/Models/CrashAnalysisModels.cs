using System;
using System.Collections.Generic;
using System.Linq;

namespace SVL.Avalonia.Models;

/// <summary>崩溃分析命中的严重程度，数值越大越严重，用于排序与配色。</summary>
public enum CrashSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>SMAPI 日志文件类型，决定分析时的优先级（崩溃日志优先于常规日志）。</summary>
public enum SmapiLogKind
{
    Crash,
    Latest,
    Console,
    Imported
}

/// <summary>候选 SMAPI 日志文件的只读元信息，不加载内容。</summary>
public sealed record SmapiLogFile(
    string Path,
    SmapiLogKind Kind,
    DateTime LastWriteUtc,
    long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>日志分析整体状态，驱动 UI 的“可分析/无日志/无异常”分支。</summary>
public enum CrashAnalysisStatus
{
    /// <summary>已完成分析，findings 可能为空（表示未命中已知问题）。</summary>
    Analyzed,

    /// <summary>日志成功解析但未命中任何已知问题规则。</summary>
    NoErrorDetected,

    /// <summary>未在磁盘上找到任何候选日志。</summary>
    NoLogFound,

    /// <summary>日志文件存在但内容为空。</summary>
    EmptyLog,

    /// <summary>日志读取失败（权限/占用/损坏）。</summary>
    ReadFailed
}

/// <summary>单条分析结论：一条规则命中，或一次堆栈归因结果。</summary>
/// <remarks>
/// Title/Explanation/Suggestion/SeverityLabel 均由分析器按传入的本地化函数解析后写入，
/// 模型本身不持有语言逻辑，便于在无 UI 场景下复用。
/// </remarks>
public sealed record CrashFinding(
    CrashSeverity Severity,
    string RuleId,
    string Title,
    string Explanation,
    string Suggestion,
    string SeverityLabel,
    string? AttributedMod = null,
    string? Excerpt = null)
{
    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Suggestion);

    public bool HasAttributedMod => !string.IsNullOrWhiteSpace(AttributedMod);

    public bool HasExcerpt => !string.IsNullOrWhiteSpace(Excerpt);
}

/// <summary>已安装 Mod 的精简身份，用于把堆栈/日志文本归因到具体 Mod。</summary>
public sealed record SmapiModIdentity(
    string UniqueId,
    string Name,
    string Version = "",
    bool IsEnabled = true);

/// <summary>一次崩溃日志分析的完整结果。</summary>
public sealed class CrashAnalysisResult
{
    public CrashAnalysisStatus Status { get; init; }

    /// <summary>被分析的日志文件路径；手动粘贴文本时为 null。</summary>
    public string? SourceLogPath { get; init; }

    public SmapiLogKind? SourceKind { get; init; }

    /// <summary>面向用户的状态描述（无日志原因、读取失败原因等）。</summary>
    public string StatusMessage { get; init; } = string.Empty;

    public string? SmapiVersion { get; init; }

    public string? GameVersion { get; init; }

    public string? OperatingSystem { get; init; }

    /// <summary>日志头部声明的“Loaded N mods”数量；未解析到为 0。</summary>
    public int LoadedModCount { get; init; }

    /// <summary>按严重度降序排列的诊断结论。</summary>
    public IReadOnlyList<CrashFinding> Findings { get; init; } = [];

    /// <summary>从日志“Loaded mods”段解析出的 Mod 列表（未命中时可能为空）。</summary>
    public IReadOnlyList<SmapiModIdentity> LoadedMods { get; init; } = [];

    public DateTime AnalyzedAtUtc { get; init; } = DateTime.UtcNow;

    public bool HasFindings => Findings.Count > 0;

    public CrashSeverity HighestSeverity =>
        Findings.Count == 0 ? CrashSeverity.Info : Findings.Max(f => f.Severity);

    /// <summary>最可能的根因（取排序后的第一条结论标题）。</summary>
    public string? PrimaryCause => Findings.Count == 0 ? null : Findings[0].Title;

    /// <summary>本次分析是否成功读取到了日志内容（用于 UI 判断是否展示“重新选择日志”）。</summary>
    public bool HasLogContent =>
        Status is CrashAnalysisStatus.Analyzed or CrashAnalysisStatus.NoErrorDetected;
}
