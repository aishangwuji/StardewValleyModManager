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
/// 本类只负责状态、展示文本与本地化，便于无 UI 场景复用。
/// </summary>
public partial class CrashAnalysisViewModel : ObservableObject
{
    private readonly LocalizationService _localization;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _sourceLogText;

    [ObservableProperty]
    private string _versionText;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _hasFindings;

    public ObservableCollection<CrashFinding> Findings { get; } = [];

    // 以下为对话框固定文案，集中从本地化字典读取，XAML 直接绑定。
    public string DialogTitle => _localization.Get("Crash.Dialog.Title");
    public string CloseButtonText => _localization.Get("Crash.Button.Close");
    public string ReanalyzeButtonText => _localization.Get("Crash.Button.Reanalyze");
    public string ImportButtonText => _localization.Get("Crash.Button.Import");
    public string OpenFolderButtonText => _localization.Get("Crash.Button.OpenFolder");
    public string CopyReportButtonText => _localization.Get("Crash.Button.CopyReport");
    public string EmptyText => _localization.Get("Crash.Empty.Text");

    public CrashAnalysisViewModel(LocalizationService? localizationService = null)
    {
        _localization = localizationService ?? new LocalizationService(new AppUserSettingsStore());
        _sourceLogText = _localization.Get("Crash.Source.None");
        _versionText = _localization.Get("Crash.Version.None");
        Analyze();
    }

    /// <summary>重新扫描默认日志目录并分析。</summary>
    [RelayCommand]
    public void Analyze() => Run(() => SmapiCrashAnalyzer.AnalyzeBest(null, _localization.Get));

    /// <summary>分析用户手动导入的日志文件。</summary>
    public void LoadFromFile(string path) => Run(() => SmapiCrashAnalyzer.AnalyzeFile(path, null, _localization.Get));

    /// <summary>生成可复制的纯文本报告。</summary>
    public string BuildReportText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(_localization.Get("Crash.Report.Title"));
        builder.AppendLine(string.Format(_localization.Get("Crash.Report.Time"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        builder.AppendLine(string.Format(_localization.Get("Crash.Report.Source"), SourceLogText));
        builder.AppendLine(string.Format(_localization.Get("Crash.Report.Version"), VersionText));
        builder.AppendLine(string.Format(_localization.Get("Crash.Report.Conclusion"), SummaryText));
        builder.AppendLine();

        if (Findings.Count == 0)
        {
            builder.AppendLine(_localization.Get("Crash.Report.NoFindings"));
            return builder.ToString();
        }

        for (var i = 0; i < Findings.Count; i++)
        {
            var finding = Findings[i];
            builder.AppendLine($"{i + 1}. [{finding.SeverityLabel}] {finding.Title}");
            if (finding.HasAttributedMod)
            {
                builder.AppendLine("   " + string.Format(_localization.Get("Crash.Report.ModLabel"), finding.AttributedMod));
            }

            builder.AppendLine("   " + string.Format(_localization.Get("Crash.Report.ExplainLabel"), finding.Explanation));
            if (finding.HasSuggestion)
            {
                builder.AppendLine("   " + string.Format(_localization.Get("Crash.Report.SuggestLabel"), finding.Suggestion));
            }

            if (finding.HasExcerpt)
            {
                builder.AppendLine("   " + string.Format(_localization.Get("Crash.Report.ExcerptLabel"), finding.Excerpt));
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
            SummaryText = _localization.Get("Crash.Summary.ReadFailed");
            StatusMessage = _localization.Get("Crash.Status.ReadFailed").Replace("{msg}", ex.Message);
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
            ? _localization.Get("Crash.Source.None")
            : result.SourceLogPath!;
        VersionText = BuildVersionText(result);
    }

    private string BuildSummary(CrashAnalysisResult result) => result.Status switch
    {
        CrashAnalysisStatus.NoLogFound => _localization.Get("Crash.Summary.NoLogFound"),
        CrashAnalysisStatus.EmptyLog => _localization.Get("Crash.Summary.EmptyLog"),
        CrashAnalysisStatus.ReadFailed => _localization.Get("Crash.Summary.ReadFailed"),
        CrashAnalysisStatus.NoErrorDetected => _localization.Get("Crash.Summary.NoErrorDetected"),
        _ => _localization.Get("Crash.Summary.HitCount").Replace("{count}", result.Findings.Count.ToString())
    };

    private string BuildVersionText(CrashAnalysisResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.SmapiVersion))
        {
            parts.Add(string.Format(_localization.Get("Crash.Version.Smapi"), result.SmapiVersion));
        }

        if (!string.IsNullOrWhiteSpace(result.GameVersion))
        {
            parts.Add(string.Format(_localization.Get("Crash.Version.Game"), result.GameVersion));
        }

        if (!string.IsNullOrWhiteSpace(result.OperatingSystem))
        {
            parts.Add(result.OperatingSystem!);
        }

        if (result.LoadedModCount > 0)
        {
            parts.Add(string.Format(_localization.Get("Crash.Version.LoadedMods"), result.LoadedModCount));
        }

        return parts.Count == 0 ? _localization.Get("Crash.Version.None") : string.Join(" · ", parts);
    }
}
