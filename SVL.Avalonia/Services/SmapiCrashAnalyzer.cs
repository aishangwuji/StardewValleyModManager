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
/// 纯函数、只读、不依赖 UI，便于单独回归测试。
/// </summary>
public static class SmapiCrashAnalyzer
{
    /// <summary>完整读取的上限；超过则只读头尾，避免大日志撑爆内存。</summary>
    private const long MaxFullReadBytes = 16L * 1024 * 1024;
    private const int HeadTailBytes = 1024 * 1024;
    private const int MaxFindings = 25;

    private const RegexOptions RuleOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private sealed record CrashRule(
        string Id,
        CrashSeverity Severity,
        string Title,
        Regex Pattern,
        string ExplanationTemplate,
        string Suggestion,
        Func<Match, string?>? ModSelector = null,
        bool AllMatches = false);

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
    public static CrashAnalysisResult AnalyzeBest(IReadOnlyList<SmapiModIdentity>? knownMods = null)
    {
        var best = SmapiLogLocator.SelectBest(SmapiLogLocator.Locate());
        if (best is null)
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.NoLogFound,
                StatusMessage = "未找到 SMAPI 日志。请先在启动器中以 SMAPI 模式启动一次游戏，或手动导入日志文件。"
            };
        }

        return AnalyzeFile(best.Path, knownMods);
    }

    /// <summary>读取并分析指定日志文件。</summary>
    public static CrashAnalysisResult AnalyzeFile(
        string path,
        IReadOnlyList<SmapiModIdentity>? knownMods = null)
    {
        if (!SmapiLogLocator.TryDescribe(path, out var descriptor) || descriptor is null)
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.NoLogFound,
                SourceLogPath = path,
                StatusMessage = "日志文件不存在、为空或无法访问。"
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
                StatusMessage = $"读取日志失败：{ex.Message}"
            };
        }

        return AnalyzeText(text, knownMods, descriptor.Path, descriptor.Kind);
    }

    /// <summary>分析日志文本。手动粘贴或测试时直接调用此重载。</summary>
    public static CrashAnalysisResult AnalyzeText(
        string? text,
        IReadOnlyList<SmapiModIdentity>? knownMods = null,
        string? sourcePath = null,
        SmapiLogKind? kind = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CrashAnalysisResult
            {
                Status = CrashAnalysisStatus.EmptyLog,
                SourceLogPath = sourcePath,
                SourceKind = kind,
                StatusMessage = "日志内容为空。"
            };
        }

        var normalized = Normalize(text);
        var header = ParseHeader(normalized);
        var loadedMods = ParseLoadedMods(normalized);
        var effectiveMods = knownMods is { Count: > 0 } ? knownMods : loadedMods;

        var findings = EvaluateRules(normalized, effectiveMods);
        AppendAttributionFindings(normalized, effectiveMods, findings);

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
                ? $"命中 {ordered.Count} 条可能原因。"
                : "未命中已知问题规则，日志中可能没有明显错误。",
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

    private static List<CrashFinding> EvaluateRules(string text, IReadOnlyList<SmapiModIdentity> mods)
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
                findings.Add(BuildFinding(rule, matches[0], mods));
                continue;
            }

            foreach (Match match in matches)
            {
                findings.Add(BuildFinding(rule, match, mods));
            }
        }

        if (findings.Count == 0)
        {
            var generic = BuildGenericFinding(text, mods);
            if (generic is not null)
            {
                findings.Add(generic);
            }
        }

        return findings;
    }

    private static CrashFinding BuildFinding(CrashRule rule, Match match, IReadOnlyList<SmapiModIdentity> mods)
    {
        var rawMod = rule.ModSelector?.Invoke(match);
        var attributed = ResolveMod(rawMod, mods);
        var explanation = rule.ExplanationTemplate.Replace("{mod}", attributed ?? "相关 Mod");

        return new CrashFinding(
            rule.Severity,
            rule.Id,
            rule.Title,
            explanation,
            rule.Suggestion,
            attributed,
            Excerpt: ExtractExcerpt(match.Value));
    }

    private static CrashFinding? BuildGenericFinding(string text, IReadOnlyList<SmapiModIdentity> mods)
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
            "未识别错误",
            "日志中存在错误，但未匹配到已知问题规则，可能为 Mod 作者自定义报错。",
            "请将完整日志反馈给相关 Mod 作者，或尝试逐个禁用近期新增的 Mod。",
            attributed,
            excerpt);
    }

    private static void AppendAttributionFindings(
        string text,
        IReadOnlyList<SmapiModIdentity> mods,
        ICollection<CrashFinding> findings)
    {
        foreach (Match match in s_causedByRegex.Matches(text))
        {
            var attributed = ResolveModFromPhrase(match.Groups["mod"].Value, mods);
            if (attributed is null)
            {
                continue;
            }

            findings.Add(new CrashFinding(
                CrashSeverity.Error,
                "attributed-caused-by",
                "错误指向 Mod",
                $"日志明确将错误归因于 {attributed}。",
                "请优先更新或临时禁用该 Mod 后重试。",
                attributed,
                ExtractExcerpt(match.Value)));
        }

        foreach (Match match in s_suspectedModRegex.Matches(text))
        {
            var attributed = ResolveMod(match.Groups["mod"].Value, mods);
            if (attributed is null)
            {
                continue;
            }

            findings.Add(new CrashFinding(
                CrashSeverity.Warning,
                "attributed-suspected",
                "疑似问题 Mod",
                $"SMAPI 将 {attributed} 标记为疑似问题来源。",
                "若问题复现，请更新或禁用该 Mod 后重试。",
                attributed,
                ExtractExcerpt(match.Value)));
        }

        var stackMods = ResolveModsFromStackTrace(text, mods);
        foreach (var mod in stackMods)
        {
            findings.Add(new CrashFinding(
                CrashSeverity.Warning,
                "attributed-stack",
                "堆栈归因",
                $"崩溃堆栈中出现了 {mod} 的调用帧。",
                "请优先更新或临时禁用该 Mod 后重试。",
                mod,
                Excerpt: null));
        }
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
        builder.Append("\n[... 日志过大，已省略中间内容 ...]\n");
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
        var rules = new List<CrashRule>
        {
            new(
                "missing-dependency",
                CrashSeverity.Critical,
                "缺少前置 Mod",
                new Regex(@"because it needs the '(?<mod>[^']+)' mod", RuleOptions),
                "该 Mod 依赖 {mod}，但它没有被安装。",
                "请在下载页搜索并安装缺失的前置 Mod，安装后重新启动游戏。",
                m => m.Groups["mod"].Value,
                AllMatches: true),
            new(
                "missing-dependency-uninstalled",
                CrashSeverity.Critical,
                "缺少前置 Mod",
                new Regex(@"because it requires mods which aren't installed(?: \((?<mod>[^)]+)\))?", RuleOptions),
                "该 Mod 依赖的其它 Mod 尚未安装。",
                "请根据日志提示补齐所有前置 Mod 后再启动。",
                m => m.Groups["mod"].Success ? m.Groups["mod"].Value : null,
                AllMatches: true),
            new(
                "missing-dependency-required",
                CrashSeverity.Critical,
                "缺少前置 Mod",
                new Regex(@"requires the (?<mod>[A-Za-z0-9_.\-]+) mod, which isn't installed", RuleOptions),
                "该 Mod 依赖 {mod}，但它没有被安装。",
                "请安装缺失的前置 Mod 后重试。",
                m => m.Groups["mod"].Value,
                AllMatches: true),
            new(
                "incompatible-game-version",
                CrashSeverity.Critical,
                "Mod 与游戏版本不兼容",
                new Regex(@"because it's incompatible with the game version", RuleOptions),
                "该 Mod 与当前星露谷版本不兼容，已被跳过加载。",
                "请更新该 Mod 到与当前游戏版本兼容的版本，或回退游戏版本。"),
            new(
                "incompatible-game-version-required",
                CrashSeverity.Critical,
                "Mod 要求特定游戏版本",
                new Regex(@"requires Stardew Valley (?<ver>[0-9][0-9A-Za-z.\-]*)", RuleOptions),
                "该 Mod 要求星露谷 {ver} 或更高版本。",
                "请升级游戏本体，或改用与当前版本匹配的 Mod 版本。"),
            new(
                "duplicate-id",
                CrashSeverity.Critical,
                "Mod 唯一 ID 重复",
                new Regex(@"because it has the same ID as another mod", RuleOptions),
                "两个已安装的 Mod 使用了相同的唯一 ID，SMAPI 无法同时加载。",
                "请在 Mod 管理页删除重复的 Mod 文件夹，只保留其中一个版本。"),
            new(
                "duplicate-id-already",
                CrashSeverity.Critical,
                "Mod 唯一 ID 重复",
                new Regex(@"already has a mod with this ID", RuleOptions),
                "已存在使用相同唯一 ID 的 Mod，导致冲突。",
                "请删除重复的 Mod，仅保留一个版本。"),
            new(
                "duplicate-id-generic",
                CrashSeverity.Error,
                "检测到重复 ID",
                new Regex(@"duplicate (?:unique )?id", RuleOptions),
                "日志提示存在重复的唯一 ID。",
                "请检查 Mod 管理页的冲突分析结果并清理重复项。"),
            new(
                "invalid-manifest",
                CrashSeverity.Error,
                "Mod 清单文件无效",
                new Regex(@"(?:failed to (?:parse|read)|could not (?:parse|read)|invalid)[^\n.]{0,40}manifest", RuleOptions),
                "某个 Mod 的 manifest.json 损坏或格式错误，无法被 SMAPI 解析。",
                "请重新下载并安装该 Mod，确保解压完整、清单文件未被破坏。"),
            new(
                "mod-load-failure",
                CrashSeverity.Error,
                "Mod 加载失败",
                new Regex(@"(?:the mod |mod )['""]?(?<mod>[A-Za-z0-9_.\-]+)['""]? failed to load", RuleOptions),
                "Mod {mod} 在加载阶段抛出异常。",
                "通常是缺少前置或版本不兼容，请更新或重新安装该 Mod。",
                m => m.Groups["mod"].Value,
                AllMatches: true),
            new(
                "mod-load-failure-generic",
                CrashSeverity.Error,
                "Mod 加载失败",
                new Regex(@"an error occurred while loading", RuleOptions),
                "加载过程中发生了错误。",
                "请查看上方 Mod 列表与错误详情，定位并更新相关 Mod。"),
            new(
                "harmony-error",
                CrashSeverity.Error,
                "Harmony 补丁冲突",
                new Regex(@"harmony.{0,200}?(?:exception|failed|error)", RuleOptions),
                "Harmony 补丁应用失败，通常由多个 Mod 修改同一游戏方法引起。",
                "可尝试逐个禁用近期新增的 Mod，或更新冲突 Mod 到最新版本。"),
            new(
                "out-of-memory",
                CrashSeverity.Critical,
                "内存不足",
                new Regex(@"OutOfMemoryException", RuleOptions),
                "游戏进程内存耗尽而崩溃。",
                "请关闭后台占用内存的程序，或为 SMAPI 增大内存上限（如 --max-memory 4096）。"),
            new(
                "stack-overflow",
                CrashSeverity.Critical,
                "堆栈溢出",
                new Regex(@"StackOverflowException", RuleOptions),
                "发生堆栈溢出，通常是 Mod 递归调用导致。",
                "请更新或禁用相关 Mod 后重试。"),
            new(
                "assembly-mismatch",
                CrashSeverity.Error,
                "程序集/类型不匹配",
                new Regex(@"(?:MissingMethodException|MissingFieldException|TypeLoadException|BadImageFormatException)", RuleOptions),
                "Mod 依赖的程序集或类型与当前游戏/SMAPI 版本不匹配。",
                "请更新该 Mod 与 SMAPI 到相互兼容的版本。"),
            new(
                "missing-assembly",
                CrashSeverity.Error,
                "缺少依赖程序集",
                new Regex(@"FileNotFoundException[^\n]*\.dll", RuleOptions),
                "Mod 运行时缺少必要的 .dll 依赖。",
                "请重新安装该 Mod 并确认其依赖文件齐全。"),
            new(
                "content-patcher-error",
                CrashSeverity.Error,
                "Content Patcher 内容包错误",
                new Regex(@"content patcher[^\n]{0,160}?(?:error|failed|exception|invalid)", RuleOptions),
                "Content Patcher 在加载内容包时出错。",
                "请检查对应内容包的 content.json 格式与目标游戏版本。"),
            new(
                "smapi-version-mismatch",
                CrashSeverity.Warning,
                "SMAPI 版本不满足",
                new Regex(@"requires (?:a newer version of )?SMAPI (?<ver>[0-9][0-9A-Za-z.\-]*)", RuleOptions),
                "某 Mod 要求 SMAPI {ver} 或更高版本。",
                "请在启动器中将 SMAPI 升级到满足要求的版本。"),
            new(
                "skipped-mods",
                CrashSeverity.Warning,
                "部分 Mod 被跳过",
                new Regex(@"Skipped (?<n>\d+) mods?", RuleOptions),
                "SMAPI 跳过了部分 Mod（通常因缺少前置或版本不兼容）。",
                "请根据“Skipped”列表补齐前置或修正版本。"),
            new(
                "game-crashed",
                CrashSeverity.Warning,
                "检测到游戏崩溃记录",
                new Regex(@"the game crashed", RuleOptions),
                "日志记录了上一次游戏异常退出。",
                "请结合下方错误详情定位具体原因。")
        };

        return rules;
    }
}
