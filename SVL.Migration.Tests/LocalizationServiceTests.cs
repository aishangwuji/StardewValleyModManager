using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Services;

namespace SVL.Migration.Tests;

[TestClass]
public class LocalizationServiceTests
{
    [TestMethod]
    public void LaunchGuide_ChineseText_ShouldReflectCurrentUiNavigation()
    {
        var settingsStore = new AppUserSettingsStore();
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

    [TestMethod]
    public void LaunchGuide_EnglishText_ShouldReflectCurrentUiNavigation()
    {
        var settingsStore = new AppUserSettingsStore();
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

    [TestMethod]
    public void Settings_UiLanguage_ShouldBeBilingual_InBothChineseAndEnglish()
    {
        var settingsStore = new AppUserSettingsStore();
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

    [TestMethod]
    public void LocalizationService_UnknownKey_ShouldReturnKeyName()
    {
        var settingsStore = new AppUserSettingsStore();
        var localization = new LocalizationService(settingsStore);

        var result = localization.Get("Unknown.NonExistent.Key.Test");
        Assert.AreEqual("Unknown.NonExistent.Key.Test", result);
    }
}
