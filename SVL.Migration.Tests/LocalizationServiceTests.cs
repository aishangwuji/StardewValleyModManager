using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Services;

namespace SVL.Migration.Tests;

[TestClass]
public class LocalizationServiceTests
{
    [TestMethod]
    public void LaunchGuide_ChineseText_ShouldReflectCurrentUiNavigation()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svl_loc_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var settingsStore = new AppUserSettingsStore(tempDir);
            var localization = new LocalizationService(settingsStore);
        localization.SetLanguage("zh-CN");

        var line1 = localization.Get("Launch.Guide.UsageLine1");
        var line2 = localization.Get("Launch.Guide.UsageLine2");
        var modManageLine = localization.Get("Launch.Guide.UsageModManage");

        StringAssert.Contains(line1, "启动游戏");
        StringAssert.Contains(line2, "版本选择");
        StringAssert.Contains(modManageLine, "Mod管理");

        // 核心断言：左下角按钮已移除，文案不应再包含“左下角”
        Assert.IsFalse(modManageLine.Contains("左下角"), "Launch.Guide.UsageModManage 不应包含已过时的「左下角」描述");
        }
        finally
        {
            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void LaunchGuide_EnglishText_ShouldReflectCurrentUiNavigation()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svl_loc_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var settingsStore = new AppUserSettingsStore(tempDir);
            var localization = new LocalizationService(settingsStore);
            localization.SetLanguage("en-US");

            var line1 = localization.Get("Launch.Guide.UsageLine1");
            var line2 = localization.Get("Launch.Guide.UsageLine2");
            var modManageLine = localization.Get("Launch.Guide.UsageModManage");

            StringAssert.Contains(line1, "Launch Game");
            StringAssert.Contains(line2, "Version Select");
            StringAssert.Contains(modManageLine, "Mods");

            // 核心断言：不应包含过时的 Local Mods 或 lower-left
            Assert.IsFalse(modManageLine.Contains("lower-left"), "Launch.Guide.UsageModManage en-US should not refer to lower-left");
            Assert.IsFalse(modManageLine.Contains("Local Mods"), "Launch.Guide.UsageModManage en-US should use 'Mods' consistent with top nav");
        }
        finally
        {
            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void Settings_UiLanguage_ShouldBeBilingual_InBothChineseAndEnglish()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svl_loc_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var settingsStore = new AppUserSettingsStore(tempDir);
            var localization = new LocalizationService(settingsStore);

            // 中文模式：以中文为主，附带英文
            localization.SetLanguage("zh-CN");
            var zhLabel = localization.Get("Settings.UiLanguage");
            StringAssert.Contains(zhLabel, "界面语言");
            StringAssert.Contains(zhLabel, "Language");

            // 英文模式：以英文为主，附带中文
            localization.SetLanguage("en-US");
            var enLabel = localization.Get("Settings.UiLanguage");
            StringAssert.Contains(enLabel, "Language");
            StringAssert.Contains(enLabel, "界面语言");
        }
        finally
        {
            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void LocalizationService_UnknownKey_ShouldReturnKeyName()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svl_loc_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var settingsStore = new AppUserSettingsStore(tempDir);
            var localization = new LocalizationService(settingsStore);

            var result = localization.Get("Unknown.NonExistent.Key.Test");
            Assert.AreEqual("Unknown.NonExistent.Key.Test", result);
        }
        finally
        {
            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 崩溃分析的全部规则/状态/界面 key 在 zh-CN 与 en-US 下都必须有实际译文，
    /// 防止新增规则时漏配某种语言（Get 命中不到会原样返回 key）。
    /// </summary>
    [TestMethod]
    public void CrashAnalysis_AllKeys_ShouldResolveInBothLanguages()
    {
        string[] ruleIds =
        [
            "missing-dependency", "missing-dependency-uninstalled", "missing-dependency-required",
            "incompatible-game-version", "incompatible-game-version-required",
            "duplicate-id", "duplicate-id-already", "duplicate-id-generic",
            "invalid-manifest", "mod-load-failure", "mod-load-failure-generic",
            "harmony-error", "out-of-memory", "stack-overflow",
            "assembly-mismatch", "missing-assembly", "content-patcher-error",
            "smapi-version-mismatch", "skipped-mods", "game-crashed",
            "generic-error", "attributed-caused-by", "attributed-suspected", "attributed-stack"
        ];

        string[] uiKeys =
        [
            "Crash.Dialog.Title", "Crash.Button.Close", "Crash.Button.Reanalyze",
            "Crash.Button.Import", "Crash.Button.OpenFolder", "Crash.Button.CopyReport",
            "Crash.Empty.Text", "Crash.Source.None", "Crash.Version.None", "Crash.Common.RelatedMod",
            "Crash.Severity.Critical", "Crash.Severity.Error", "Crash.Severity.Warning", "Crash.Severity.Info",
            "Crash.Status.NoLogFound", "Crash.Status.FileMissing", "Crash.Status.ReadFailed",
            "Crash.Status.EmptyLog", "Crash.Status.HitCount", "Crash.Status.NoErrorDetected",
            "Crash.Summary.NoLogFound", "Crash.Summary.EmptyLog", "Crash.Summary.ReadFailed",
            "Crash.Summary.NoErrorDetected", "Crash.Summary.HitCount",
            "Crash.Version.Smapi", "Crash.Version.Game", "Crash.Version.LoadedMods",
            "Crash.Report.Title", "Crash.Report.Time", "Crash.Report.Source", "Crash.Report.Version",
            "Crash.Report.Conclusion", "Crash.Report.NoFindings", "Crash.Report.ModLabel",
            "Crash.Report.ExplainLabel", "Crash.Report.SuggestLabel", "Crash.Report.ExcerptLabel"
        ];

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "svl_loc_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var localization = new LocalizationService(new AppUserSettingsStore(tempDir));

            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                localization.SetLanguage(language);

                foreach (var ruleId in ruleIds)
                {
                    foreach (var suffix in new[] { "Title", "Explain", "Suggest" })
                    {
                        var key = $"Crash.Rule.{ruleId}.{suffix}";
                        Assert.AreNotEqual(key, localization.Get(key), $"{language} 缺少 {key}");
                    }
                }

                foreach (var key in uiKeys)
                {
                    var value = localization.Get(key);
                    Assert.AreNotEqual(key, value, $"{language} 缺少 {key}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{language} 的 {key} 为空");
                }
            }
        }
        finally
        {
            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
        }
    }
}
