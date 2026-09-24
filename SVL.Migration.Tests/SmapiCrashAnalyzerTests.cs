using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;

namespace SVL.Migration.Tests;

/// <summary>
/// SMAPI 崩溃日志分析器测试。覆盖：
/// - 头部 / 已加载 Mod 列表解析
/// - 分层规则命中（缺前置、ID 重复、版本不兼容、Harmony、OOM、Skipped 等）
/// - “caused by” 与堆栈归因到已安装 Mod
/// - 兜底 generic-error、空日志、干净日志、无文件
/// - UTF-16 编码读取
/// - 结论按严重度排序
/// </summary>
[TestClass]
public class SmapiCrashAnalyzerTests
{
    private const string Header =
        "[14:32:10 INFO  SMAPI] SMAPI 4.1.10 with Stardew Valley 1.6.15 build 24356 on Microsoft Windows 11 10.0.26100";

    private static string LoadedBlock => string.Join("\n",
        "[14:32:12 INFO  SMAPI] Loaded 2 mods:",
        "[14:32:12 INFO  SMAPI]    Content Patcher 2.6.5 by Pathoschild | https://www.nexusmods.com/stardewvalley/mods/1915",
        "[14:32:12 INFO  SMAPI]    Automate 2.1.0 by Pathoschild | https://www.nexusmods.com/stardewvalley/mods/1063");

    // ================================================================
    // 头部与 Mod 列表解析
    // ================================================================

    [TestMethod]
    public void AnalyzeText_ShouldParseHeaderVersionsAndLoadedCount()
    {
        var log = Header + "\n" + LoadedBlock + "\n[14:32:13 INFO  SMAPI] Game started.\n";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.AreEqual("4.1.10", result.SmapiVersion);
        Assert.AreEqual("1.6.15 build 24356", result.GameVersion);
        StringAssert.Contains(result.OperatingSystem, "Windows 11");
        Assert.AreEqual(2, result.LoadedModCount);
        Assert.AreEqual(2, result.LoadedMods.Count);
        Assert.AreEqual("Content Patcher", result.LoadedMods[0].Name);
        Assert.AreEqual("2.6.5", result.LoadedMods[0].Version);
    }

    // ================================================================
    // 规则命中
    // ================================================================

    [TestMethod]
    public void AnalyzeText_MissingQuotedDependency_ShouldBeCriticalAndAttributeMod()
    {
        var log = Header + "\n" + LoadedBlock +
                  "\n[14:32:13 ERROR SMAPI] - Some Mod because it needs the 'Pathoschild.ContentPatcher' mod, which isn't installed.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.AreEqual(CrashAnalysisStatus.Analyzed, result.Status);
        var finding = result.Findings.First(f => f.RuleId == "missing-dependency");
        Assert.AreEqual(CrashSeverity.Critical, finding.Severity);
        Assert.AreEqual("Content Patcher", finding.AttributedMod);
    }

    [TestMethod]
    public void AnalyzeText_UninstalledDependency_ShouldKeepRawTokenWhenUnknown()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] - Some Mod because it needs the 'Unknown.Author.Dep' mod, which isn't installed.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        var finding = result.Findings.First(f => f.RuleId == "missing-dependency");
        Assert.AreEqual("Unknown.Author.Dep", finding.AttributedMod);
    }

    [TestMethod]
    public void AnalyzeText_DuplicateId_ShouldBeCritical()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] Some Mod because it has the same ID as another mod.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "duplicate-id" && f.Severity == CrashSeverity.Critical));
    }

    [TestMethod]
    public void AnalyzeText_IncompatibleGameVersion_ShouldBeCritical()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] - Old Mod because it's incompatible with the game version.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "incompatible-game-version" && f.Severity == CrashSeverity.Critical));
    }

    [TestMethod]
    public void AnalyzeText_HarmonyError_ShouldBeError()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] HarmonyLib.HarmonyException: patching failed in Harmony patch.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "harmony-error" && f.Severity == CrashSeverity.Error));
    }

    [TestMethod]
    public void AnalyzeText_OutOfMemory_ShouldBeCritical()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] System.OutOfMemoryException: Insufficient memory to continue.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "out-of-memory" && f.Severity == CrashSeverity.Critical));
    }

    [TestMethod]
    public void AnalyzeText_SkippedMods_ShouldBeWarning()
    {
        var log = Header + "\n[14:32:13 WARN  SMAPI] Skipped 3 mods:";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "skipped-mods" && f.Severity == CrashSeverity.Warning));
    }

    [TestMethod]
    public void AnalyzeText_InvalidManifest_ShouldBeError()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] Failed to parse manifest.json for mod SomeMod.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "invalid-manifest"));
    }

    // ================================================================
    // 归因
    // ================================================================

    [TestMethod]
    public void AnalyzeText_CausedBy_ShouldAttributeToInstalledMod()
    {
        var log = Header + "\n" + LoadedBlock +
                  "\n[14:32:13 ERROR SMAPI] The error was caused by Content Patcher while loading.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f =>
            f.RuleId == "attributed-caused-by" &&
            string.Equals(f.AttributedMod, "Content Patcher", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AnalyzeText_StackTrace_ShouldAttributeToInstalledMod()
    {
        var log = Header + "\n" + LoadedBlock +
                  "\n[14:32:13 ERROR SMAPI] An error occurred.\n" +
                  "   at Automate.Framework.AutomationFactory.Build()\n" +
                  "   at StardewModdingAPI.Framework.SCore.RunInteractively()";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f =>
            f.RuleId == "attributed-stack" &&
            string.Equals(f.AttributedMod, "Automate", StringComparison.OrdinalIgnoreCase)));
    }

    // ================================================================
    // 兜底与边界
    // ================================================================

    [TestMethod]
    public void AnalyzeText_UnknownErrorLine_ShouldFallBackToGeneric()
    {
        var log = Header + "\n[14:32:13 ERROR SMAPI] SomeMod threw a bespoke error that matches no rule.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Any(f => f.RuleId == "generic-error"));
    }

    [TestMethod]
    public void AnalyzeText_CleanLog_ShouldReportNoError()
    {
        var log = Header + "\n" + LoadedBlock + "\n[14:32:15 INFO  SMAPI] Game started successfully.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.AreEqual(CrashAnalysisStatus.NoErrorDetected, result.Status);
        Assert.IsFalse(result.HasFindings);
    }

    [TestMethod]
    public void AnalyzeText_Empty_ShouldReturnEmptyLogStatus()
    {
        var result = SmapiCrashAnalyzer.AnalyzeText("   \n  ");

        Assert.AreEqual(CrashAnalysisStatus.EmptyLog, result.Status);
    }

    [TestMethod]
    public void AnalyzeFile_MissingFile_ShouldReturnNoLogFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"svl-missing-{Guid.NewGuid():N}.txt");

        var result = SmapiCrashAnalyzer.AnalyzeFile(missing);

        Assert.AreEqual(CrashAnalysisStatus.NoLogFound, result.Status);
    }

    [TestMethod]
    public void AnalyzeFile_Utf16EncodedLog_ShouldBeReadAndParsed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"svl-utf16-{Guid.NewGuid():N}.txt");
        var log = Header + "\n[14:32:13 ERROR SMAPI] System.OutOfMemoryException: boom";
        File.WriteAllText(path, log, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        try
        {
            var result = SmapiCrashAnalyzer.AnalyzeFile(path);

            Assert.AreEqual(CrashAnalysisStatus.Analyzed, result.Status);
            Assert.IsTrue(result.Findings.Any(f => f.RuleId == "out-of-memory"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnalyzeText_Findings_ShouldBeOrderedBySeverityDescending()
    {
        var log = Header +
                  "\n[14:32:13 WARN  SMAPI] Skipped 2 mods:" +
                  "\n[14:32:13 ERROR SMAPI] - Bad Mod because it's incompatible with the game version.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log);

        Assert.IsTrue(result.Findings.Count >= 2);
        Assert.AreEqual(CrashSeverity.Critical, result.Findings[0].Severity);
        Assert.AreEqual(CrashSeverity.Critical, result.HighestSeverity);
    }

    [TestMethod]
    public void AnalyzeText_WithLocalizer_ShouldResolveLocalizedText()
    {
        var log = Header +
                  "\n[14:32:13 ERROR SMAPI] - Some Mod because it needs the 'Foo.Bar' mod, which isn't installed.";

        var result = SmapiCrashAnalyzer.AnalyzeText(log, null, null, null, key => $"<{key}>");

        var finding = result.Findings.First(f => f.RuleId == "missing-dependency");
        StringAssert.Contains(finding.Title, "<Crash.Rule.missing-dependency.Title>");
        StringAssert.Contains(finding.Explanation, "<Crash.Rule.missing-dependency.Explain>");
        StringAssert.Contains(finding.SeverityLabel, "<Crash.Severity.Critical>");
    }

    // ================================================================
    // 定位器
    // ================================================================

    [TestMethod]
    public void ClassifyKind_ShouldIdentifyCrashAndLatest()
    {
        Assert.AreEqual(SmapiLogKind.Crash, SmapiLogLocator.ClassifyKind("SMAPI-crash.txt"));
        Assert.AreEqual(SmapiLogKind.Latest, SmapiLogLocator.ClassifyKind("SMAPI-latest.txt"));
        Assert.AreEqual(SmapiLogKind.Imported, SmapiLogLocator.ClassifyKind("notes.txt"));
    }

    [TestMethod]
    public void SelectBest_ShouldPreferCrashOverLatest()
    {
        var now = DateTime.UtcNow;
        var files = new[]
        {
            new SmapiLogFile("C:/logs/SMAPI-latest.txt", SmapiLogKind.Latest, now, 100),
            new SmapiLogFile("C:/logs/SMAPI-crash.txt", SmapiLogKind.Crash, now.AddMinutes(-5), 100)
        };

        var best = SmapiLogLocator.SelectBest(files);

        Assert.IsNotNull(best);
        Assert.AreEqual(SmapiLogKind.Crash, best!.Kind);
    }
}
