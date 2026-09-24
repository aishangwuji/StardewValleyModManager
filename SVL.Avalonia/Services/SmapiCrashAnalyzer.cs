using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SVL.Avalonia.Models;

namespace SVL.Avalonia.Services;

/// <summary>
/// SMAPI / 星露谷崩溃日志分析器。
///
/// 采用分层策略（参考 PCL CrashAnalyzer 的思路）：
///   1. 高置信度精确特征匹配（缺前置、版本不兼容、ID 重复、清单损坏等）；
///   2. 堆栈 / “caused by” 归因，把错误映射到具体已安装 Mod；
///   3. 兜底：抓取首条 [ERROR] 行，避免“什么都没分析出来”。
///
/// 纯函数、只读、不依赖 UI。规则只保存本地化 key（按 RuleId 推导），文案由调用方
/// 传入的 localize 函数解析；未传时退化为返回 key，便于无 UI 场景测试。
/// </summary>
public static class SmapiCrashAnalyzer
{
    /// <summary>完整读取的上限；超过则只读头尾，避免大日志撑爆内存。</summary>
    private const long MaxFullReadBytes = 16L * 1024 * 1024;
    private const int HeadTailBytes = 1024 * 1024;
    private const int MaxFindings = 25;

    private const RegexOptions RuleOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Func<string, string> IdentityLocalizer = key => key;

    private sealed record CrashRule(
        string Id,
        CrashSeverity Severity,
        Regex Pattern,
        Func<Match, string?>? ModSelector = null,
        Func<Match, string?>? DetailSelector = null,
        bool AllMatches = false)
    {
        public string TitleKey => $"Crash.Rule.{Id}.Title";
        public string ExplanationKey => $"Crash.Rule.{Id}.Explain";
        public string SuggestionKey => $"Crash.Rule.{Id}.Suggest";
    }

    private static readonly Regex s_headerRegex = new(
        @"SMAPI (?<smapi>[0-9][0-9A-Za-z.\-]*) with Stardew Valley (?<game>[0-9][0-9A-Za-z.\-]*(?: build [0-9]+)?) on (?<os>[^\r\n]+)",
        RuleOptions);

    private static readonly Regex s_smapiVersionRegex = new(@"SMAPI (?<v>[0-9][0-9A-Za-z.\-]*)", RuleOptions);
    private static readonly Regex s_gameVersionRegex = new(@"Stardew Valley (?<v>[0-9][0-9A-Za-z.\-]*)", RuleOptions);
    private static readonly Regex s_loadedCountRegex = new(@"Loaded (?<n>\d+) mods", RuleOptions);
    private static readonly Regex s_loadedModLineRegex = new(
        @"^\[[^\]]*\]\s+(?<name>.+?) (?<ver>[0-9][0-9A-Za-z.\-]*) by (?<author>.+?)(?:\s*\||\s*$)",
        RuleOptions);
    private static readonly Regex s_errorLineRegex = new(@"^\[[^\]]*ERROR[^\]]*\].*$", RegexOptions.Multiline | RuleOptions);
    private static readonly Regex s_causedByRegex = new(
        @"(?:the error was )?caused by (?:mod )?(?<mod>[^\r\n]+)",
        RuleOptions);
    private static readonly Regex s_suspectedModRegex = new(
        @"suspected mod[:\s]+(?<mod>[A-Za-z0-9_.\-]+)",
        RuleOptions);
    private static readonly Regex s_stackTypeRegex = new(
        @"^\s*at (?<type>[A-Za-z_][A-Za-z0-9_.]*)\.(?<method>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<CrashRule> s_rules = BuildRules();

    /// <summary>扫描默认日志目录并分析最值得关注的一份日志。</summary>
    public static CrashAnalysisResult AnalyzeBest(
        IReadOnlyList<SmapiModIdentity>? knownMods = null,
        Func<string, string>? localize = null)
    {
        var l = localize ?? IdentityLocalizer;
        var best = SmapiLogLocator.SelectBest(SmapiLogLocator.Locate());
        if (best is null)
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.NoLogFound,
                StatusMessage = l("Crash.Status.NoLogFound")
            };
        }

        return AnalyzeFile(best.Path, knownMods, localize);
    }

    /// <summary>读取并分析指定日志文件。</summary>
    public static CrashAnalysisResult AnalyzeFile(
        string path,
        IReadOnlyList<SmapiModIdentity>? knownMods = null,
        Func<string, string>? localize = null)
    {
        var l = localize ?? IdentityLocalizer;
        if (!SmapiLogLocator.TryDescribe(path, out var descriptor) || descriptor is null)
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.NoLogFound,
                SourceLogPath = path,
                StatusMessage = l("Crash.Status.FileMissing")
            };
        }

        string text;
        try
        {
            text = ReadLogText(descriptor.Path);
        }
        catch (Exception ex)
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.ReadFailed,
                SourceLogPath = descriptor.Path,
                SourceKind = descriptor.Kind,
                StatusMessage = l("Crash.Status.ReadFailed").Replace("{msg}", ex.Message)
            };
        }

        return AnalyzeText(text, knownMods, descriptor.Path, descriptor.Kind, localize);
    }

    /// <summary>分析日志文本。手动粘贴或测试时直接调用此重载。</summary>
    public static CrashAnalysisResult AnalyzeText(
        string? text,
        IReadOnlyList<SmapiModIdentity>? knownMods = null,
        string? sourcePath = null,
        SmapiLogKind? kind = null,
        Func<string, string>? localize = null)
    {
        var l = localize ?? IdentityLocalizer;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.EmptyLog,
                SourceLogPath = sourcePath,
                SourceKind = kind,
                StatusMessage = l("Crash.Status.EmptyLog")
            };
        }

        var normalized = Normalize(text);
        var header = ParseHeader(normalized);
        var loadedMods = ParseLoadedMods(normalized);
        var effectiveMods = knownMods is { Count: > 0 } ? knownMods : loadedMods;

        var findings = EvaluateRules(normalized, effectiveMods, l);
        AppendAttributionFindings(normalized, effectiveMods, findings, l);

        var ordered = findings
            .GroupBy(f => $"{f.RuleId}|{f.AttributedMod}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .Take(MaxFindings)
            .ToList();

        return new CrashAnalysisResult
        {
            Status = ordered.Count > 0 ? CrashAnalysisStatus.Analyzed : CrashAnalysisStatus.NoErrorDetected,
            SourceLogPath = sourcePath,
            SourceKind = kind,
            StatusMessage = ordered.Count > 0
                ? l("Crash.Status.HitCount").Replace("{count}", ordered.Count.ToString())
                : l("Crash.Status.NoErrorDetected"),
            SmapiVersion = header.SmapiVersion,
            GameVersion = header.GameVersion,
            OperatingSystem = header.OperatingSystem,
            LoadedModCount = header.LoadedModCount,
            Findings = ordered,
            LoadedMods = loadedMods
        };
    }

    // ── 头部 / Mod 列表解析 ────────────────────────────────────────────────

    private static (string? SmapiVersion, string? GameVersion, string? OperatingSystem, int LoadedModCount)
        ParseHeader(string text)
    {
        var header = s_headerRegex.Match(text);
        if (header.Success)
        {
            var count = s_loadedCountRegex.Match(text);
            return (
                header.Groups["smapi"].Value,
                header.Groups["game"].Value,
                header.Groups["os"].Value.Trim(),
                count.Success && int.TryParse(count.Groups["n"].Value, out var n) ? n : 0);
        }

        var smapi = s_smapiVersionRegex.Match(text);
        var game = s_gameVersionRegex.Match(text);
        var loaded = s_loadedCountRegex.Match(text);
        return (
            smapi.Success ? smapi.Groups["v"].Value : null,
            game.Success ? game.Groups["v"].Value : null,
            null,
            loaded.Success && int.TryParse(loaded.Groups["n"].Value, out var ln) ? ln : 0);
    }

    private static IReadOnlyList<SmapiModIdentity> ParseLoadedMods(string text)
    {
        var mods = new List<SmapiModIdentity>();
        var marker = s_loadedCountRegex.Match(text);
        if (!marker.Success)
        {
            return mods;
        }

        var tail = text.Substring(marker.Index);
        var lines = tail.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var match = s_loadedModLineRegex.Match(line);
            if (!match.Success)
            {
                break;
            }

            var name = match.Groups["name"].Value.Trim();
            mods.Add(new SmapiModIdentity(name, name, match.Groups["ver"].Value.Trim()));
        }

        return mods;
    }

    // ── 规则匹配 ──────────────────────────────────────────────────────────

    private static List<CrashFinding> EvaluateRules(
        string text,
        IReadOnlyList<SmapiModIdentity> mods,
        Func<string, string> localize)
    {
        var findings = new List<CrashFinding>();

        foreach (var rule in s_rules)
        {
            var matches = rule.Pattern.Matches(text);
            if (matches.Count == 0)
            {
                continue;
            }

            if (!rule.AllMatches)
            {
                findings.Add(BuildFinding(rule, matches[0], mods, localize));
                continue;
            }

            foreach (Match match in matches)
            {
                findings.Add(BuildFinding(rule, match, mods, localize));
            }
        }

        if (findings.Count == 0)
        {
            var generic = BuildGenericFinding(text, mods, localize);
            if (generic is not null)
            {
                findings.Add(generic);
            }
        }

        return findings;
    }

    private static CrashFinding BuildFinding(
        CrashRule rule,
        Match match,
        IReadOnlyList<SmapiModIdentity> mods,
        Func<string, string> localize)
    {
        var rawMod = rule.ModSelector?.Invoke(match);
        var attributed = ResolveMod(rawMod, mods);
        var detail = rule.DetailSelector?.Invoke(match);

        var explanation = localize(rule.ExplanationKey)
            .Replace("{mod}", attributed ?? localize("Crash.Common.RelatedMod"))
            .Replace("{detail}", detail ?? string.Empty);

        return new CrashFinding(
            rule.Severity,
            rule.Id,
            localize(rule.TitleKey),
            explanation,
            localize(rule.SuggestionKey),
            localize($"Crash.Severity.{rule.Severity}"),
            attributed,
            Excerpt: ExtractExcerpt(match.Value));
    }

    private static CrashFinding? BuildGenericFinding(
        string text,
        IReadOnlyList<SmapiModIdentity> mods,
        Func<string, string> localize)
    {
        var errorLine = s_errorLineRegex.Match(text);
        if (!errorLine.Success)
        {
            return null;
        }

        var excerpt = ExtractExcerpt(errorLine.Value);
        var attributed = ResolveModFromText(errorLine.Value, mods);
        return new CrashFinding(
            CrashSeverity.Error,
            "generic-error",
            localize("Crash.Rule.generic-error.Title"),
            localize("Crash.Rule.generic-error.Explain"),
            localize("Crash.Rule.generic-error.Suggest"),
            localize("Crash.Severity.Error"),
            attributed,
            excerpt);
    }

    private static void AppendAttributionFindings(
        string text,
        IReadOnlyList<SmapiModIdentity> mods,
        ICollection<CrashFinding> findings,
        Func<string, string> localize)
    {
        foreach (Match match in s_causedByRegex.Matches(text))
        {
            var attributed = ResolveModFromPhrase(match.Groups["mod"].Value, mods);
            if (attributed is null)
            {
                continue;
            }

            findings.Add(BuildAttributionFinding(
                localize, "attributed-caused-by", CrashSeverity.Error, attributed, ExtractExcerpt(match.Value)));
        }

        foreach (Match match in s_suspectedModRegex.Matches(text))
        {
            var attributed = ResolveMod(match.Groups["mod"].Value, mods);
            if (attributed is null)
            {
                continue;
            }

            findings.Add(BuildAttributionFinding(
                localize, "attributed-suspected", CrashSeverity.Warning, attributed, ExtractExcerpt(match.Value)));
        }

        foreach (var mod in ResolveModsFromStackTrace(text, mods))
        {
            findings.Add(BuildAttributionFinding(
                localize, "attributed-stack", CrashSeverity.Warning, mod, Excerpt: null));
        }
    }

    private static CrashFinding BuildAttributionFinding(
        Func<string, string> localize,
        string ruleId,
        CrashSeverity severity,
        string attributedMod,
        string? Excerpt)
    {
        return new CrashFinding(
            severity,
            ruleId,
            localize($"Crash.Rule.{ruleId}.Title"),
            localize($"Crash.Rule.{ruleId}.Explain").Replace("{mod}", attributedMod),
            localize($"Crash.Rule.{ruleId}.Suggest"),
            localize($"Crash.Severity.{severity}"),
            attributedMod,
            Excerpt);
    }

    // ── Mod 归因 ──────────────────────────────────────────────────────────

    private static string? ResolveMod(string? rawToken, IReadOnlyList<SmapiModIdentity> mods)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var token = rawToken.Trim().Trim('\'', '"');
        var normalizedToken = NormalizeToken(token);
        // 日志中的前置标识通常是 UniqueID（如 Author.ModName），其末段常与 Mod 显示名一致，
        // 因此在没有完整 UniqueID 清单时用末段做一次保守匹配。
        var tail = normalizedToken.Contains('.')
            ? normalizedToken[(normalizedToken.LastIndexOf('.') + 1)..]
            : normalizedToken;

        var match = mods.FirstOrDefault(m =>
            string.Equals(m.UniqueId, token, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeToken(m.Name), normalizedToken, StringComparison.OrdinalIgnoreCase) ||
            (tail.Length >= 4 && string.Equals(NormalizeToken(m.Name), tail, StringComparison.OrdinalIgnoreCase)));

        // 未安装该 Mod 时也保留原始标识，便于用户按提示去找前置。
        return match?.Name ?? (LooksLikeModToken(token) ? token : null);
    }

    /// <summary>从“caused by &lt;短语&gt;”里提取 Mod：优先匹配显示名作为前缀的已安装 Mod。</summary>
    private static string? ResolveModFromPhrase(string phrase, IReadOnlyList<SmapiModIdentity> mods)
    {
        var trimmed = phrase.Trim().Trim('\'', '"', '.', ',', ';', '。');
        if (trimmed.Length == 0)
        {
            return null;
        }

        var byPrefix = mods
            .Where(m => !string.IsNullOrWhiteSpace(m.Name) &&
                        trimmed.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Name.Length)
            .FirstOrDefault();

        if (byPrefix is not null)
        {
            return byPrefix.Name;
        }

        var firstToken = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ResolveMod(firstToken, mods);
    }

    private static string? ResolveModFromText(string line, IReadOnlyList<SmapiModIdentity> mods)
    {
        foreach (var mod in mods)
        {
            if (!string.IsNullOrWhiteSpace(mod.Name) &&
                line.Contains(mod.Name, StringComparison.OrdinalIgnoreCase))
            {
                return mod.Name;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveModsFromStackTrace(string text, IReadOnlyList<SmapiModIdentity> mods)
    {
        if (mods.Count == 0)
        {
            return [];
        }

        var hits = new List<string>();
        foreach (Match match in s_stackTypeRegex.Matches(text))
        {
            var typeName = match.Groups["type"].Value;
            foreach (var mod in mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Name) || mod.Name.Length < 4)
                {
                    continue;
                }

                var normalizedName = NormalizeToken(mod.Name);
                if (normalizedName.Length < 4)
                {
                    continue;
                }

                if (typeName.Replace(" ", string.Empty).Contains(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                    !hits.Contains(mod.Name, StringComparer.OrdinalIgnoreCase))
                {
                    hits.Add(mod.Name);
                }
            }
        }

        return hits;
    }

    // ── 工具方法 ──────────────────────────────────────────────────────────

    private static string ReadLogText(string path)
    {
        var info = new FileInfo(path);
        if (info.Length <= MaxFullReadBytes)
        {
            return ManifestTextReader.ReadAllText(path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var head = new byte[HeadTailBytes];
        var headRead = stream.Read(head, 0, head.Length);

        stream.Seek(-HeadTailBytes, SeekOrigin.End);
        var tail = new byte[HeadTailBytes];
        var tailRead = stream.Read(tail, 0, tail.Length);

        var builder = new StringBuilder();
        builder.Append(Encoding.UTF8.GetString(head, 0, headRead));
        builder.Append("\n[... log truncated ...]\n");
        builder.Append(Encoding.UTF8.GetString(tail, 0, tailRead));
        return builder.ToString();
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string NormalizeToken(string token) =>
        token.Replace(" ", string.Empty).Replace("_", string.Empty).Trim();

    private static bool LooksLikeModToken(string token) =>
        token.Length >= 3 &&
        token.Contains('.') &&
        !token.StartsWith("System", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractExcerpt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var single = value.Replace('\n', ' ').Trim();
        return single.Length <= 300 ? single : single.Substring(0, 300) + "…";
    }

    private static IReadOnlyList<CrashRule> BuildRules()
    {
        return new List<CrashRule>
        {
            new(
                "missing-dependency",
                CrashSeverity.Critical,
                new Regex(@"because it needs the '(?<mod>[^']+)' mod", RuleOptions),
                m => m.Groups["mod"].Value,
                AllMatches: true),
            new(
                "missing-dependency-uninstalled",
                CrashSeverity.Critical,
                new Regex(@"because it requires mods which aren't installed(?: \((?<mod>[^)]+)\))?", RuleOptions),
                m => m.Groups["mod"].Success ? m.Groups["mod"].Value : null,
                AllMatches: true),
            new(
                "missing-dependency-required",
                CrashSeverity.Critical,
                new Regex(@"requires the (?<mod>[A-Za-z0-9_.\-]+) mod, which isn't installed", RuleOptions),
                m => m.Groups["mod"].Value,
                AllMatches: true),
            new(
                "incompatible-game-version",
                CrashSeverity.Critical,
                new Regex(@"because it's incompatible with the game version", RuleOptions)),
            new(
                "incompatible-game-version-required",
                CrashSeverity.Critical,
                new Regex(@"requires Stardew Valley (?<ver>[0-9][0-9A-Za-z.\-]*)", RuleOptions),
                DetailSelector: m => m.Groups["ver"].Value),
            new(
                "duplicate-id",
                CrashSeverity.Critical,
                new Regex(@"because it has the same ID as another mod", RuleOptions)),
            new(
                "duplicate-id-already",
                CrashSeverity.Critical,
                new Regex(@"already has a mod with this ID", RuleOptions)),
            new(
                "duplicate-id-generic",
                CrashSeverity.Error,
                new Regex(@"duplicate (?:unique )?id", RuleOptions)),
            new(
                "invalid-manifest",
                CrashSeverity.Error,
                new Regex(@"(?:failed to (?:parse|read)|could not (?:parse|read)|invalid)[^\n.]{0,40}manifest", RuleOptions)),
            new(
                "mod-load-failure",
                CrashSeverity.Error,
                new Regex(@"(?:the mod |mod )['""]?(?<mod>[A-Za-z0-9_.\-]+)['""]? failed to load", RuleOptions),
                m => m.Groups["mod"].Value,
                AllMatches: true),
            new(
                "mod-load-failure-generic",
                CrashSeverity.Error,
                new Regex(@"an error occurred while loading", RuleOptions)),
            new(
                "harmony-error",
                CrashSeverity.Error,
                new Regex(@"harmony.{0,200}?(?:exception|failed|error)", RuleOptions)),
            new(
                "out-of-memory",
                CrashSeverity.Critical,
                new Regex(@"OutOfMemoryException", RuleOptions)),
            new(
                "stack-overflow",
                CrashSeverity.Critical,
                new Regex(@"StackOverflowException", RuleOptions)),
            new(
                "assembly-mismatch",
                CrashSeverity.Error,
                new Regex(@"(?:MissingMethodException|MissingFieldException|TypeLoadException|BadImageFormatException)", RuleOptions)),
            new(
                "missing-assembly",
                CrashSeverity.Error,
                new Regex(@"FileNotFoundException[^\n]*\.dll", RuleOptions)),
            new(
                "content-patcher-error",
                CrashSeverity.Error,
                new Regex(@"content patcher[^\n]{0,160}?(?:error|failed|exception|invalid)", RuleOptions)),
            new(
                "smapi-version-mismatch",
                CrashSeverity.Warning,
                new Regex(@"requires (?:a newer version of )?SMAPI (?<ver>[0-9][0-9A-Za-z.\-]*)", RuleOptions),
                DetailSelector: m => m.Groups["ver"].Value),
            new(
                "skipped-mods",
                CrashSeverity.Warning,
                new Regex(@"Skipped (?<n>\d+) mods?", RuleOptions)),
            new(
                "game-crashed",
                CrashSeverity.Warning,
                new Regex(@"the game crashed", RuleOptions))
        };
    }
}
