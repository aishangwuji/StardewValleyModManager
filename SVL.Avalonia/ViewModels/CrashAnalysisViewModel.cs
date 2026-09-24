using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Text;

namespace SVL.Avalonia.ViewModels;

/// <summary>
/// 崩溃日志分析对话框 ViewModel。
///
/// 打开即自动分析默认日志目录中最值得关注的一份（崩溃日志优先）；
/// 也支持手动导入日志文件。分析逻辑全部委托给 <see cref="SmapiCrashAnalyzer"/>，
/// 本类只负责状态与展示文本，便于无 UI 场景复用。
/// </summary>
public partial class CrashAnalysisViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _sourceLogText = "未选择日志";

    [ObservableProperty]
    private string _versionText = "未解析到版本信息";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _hasFindings;

    public ObservableCollection<CrashFinding> Findings { get; } = [];

    public CrashAnalysisViewModel()
    {
        Analyze();
    }

    /// <summary>重新扫描默认日志目录并分析。</summary>
    [RelayCommand]
    public void Analyze() => Run(() => SmapiCrashAnalyzer.AnalyzeBest());

    /// <summary>分析用户手动导入的日志文件。</summary>
    public void LoadFromFile(string path) => Run(() => SmapiCrashAnalyzer.AnalyzeFile(path));

    /// <summary>生成可复制的纯文本报告。</summary>
    public string BuildReportText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("SVL 崩溃日志分析报告");
        builder.AppendLine($"分析时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"日志来源：{SourceLogText}");
        builder.AppendLine($"版本信息：{VersionText}");
        builder.AppendLine($"结论：{SummaryText}");
        builder.AppendLine();

        if (Findings.Count == 0)
        {
            builder.AppendLine("未命中已知问题规则。");
            return builder.ToString();
        }

        for (var i = 0; i < Findings.Count; i++)
        {
            var finding = Findings[i];
            builder.AppendLine($"{i + 1}. [{finding.SeverityText}] {finding.Title}");
            if (finding.HasAttributedMod)
            {
                builder.AppendLine($"   涉及 Mod：{finding.AttributedMod}");
            }

            builder.AppendLine($"   说明：{finding.Explanation}");
            if (finding.HasSuggestion)
            {
                builder.AppendLine($"   建议：{finding.Suggestion}");
            }

            if (finding.HasExcerpt)
            {
                builder.AppendLine($"   证据：{finding.Excerpt}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private void Run(Func<CrashAnalysisResult> analyze)
    {
        IsAnalyzing = true;
        try
        {
            Apply(analyze());
        }
        catch (Exception ex)
        {
            Findings.Clear();
            HasResult = false;
            HasFindings = false;
            SummaryText = "分析失败";
            StatusMessage = $"分析过程中发生错误：{ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void Apply(CrashAnalysisResult result)
    {
        Findings.Clear();
        foreach (var finding in result.Findings)
        {
            Findings.Add(finding);
        }

        HasResult = true;
        HasFindings = Findings.Count > 0;
        StatusMessage = result.StatusMessage;
        SummaryText = BuildSummary(result);
        SourceLogText = string.IsNullOrWhiteSpace(result.SourceLogPath)
            ? "未选择日志"
            : result.SourceLogPath!;
        VersionText = BuildVersionText(result);
    }

    private static string BuildSummary(CrashAnalysisResult result) => result.Status switch
    {
        CrashAnalysisStatus.NoLogFound => "未找到可分析的 SMAPI 日志",
        CrashAnalysisStatus.EmptyLog => "日志内容为空",
        CrashAnalysisStatus.ReadFailed => "日志读取失败",
        CrashAnalysisStatus.NoErrorDetected => "未发现明显错误",
        _ => $"命中 {result.Findings.Count} 条可能原因"
    };

    private static string BuildVersionText(CrashAnalysisResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.SmapiVersion))
        {
            parts.Add($"SMAPI {result.SmapiVersion}");
        }

        if (!string.IsNullOrWhiteSpace(result.GameVersion))
        {
            parts.Add($"游戏 {result.GameVersion}");
        }

        if (!string.IsNullOrWhiteSpace(result.OperatingSystem))
        {
            parts.Add(result.OperatingSystem!);
        }

        if (result.LoadedModCount > 0)
        {
            parts.Add($"已加载 {result.LoadedModCount} 个 Mod");
        }

        return parts.Count == 0 ? "未解析到版本信息" : string.Join(" · ", parts);
    }
}
