#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using SVL.Avalonia.ViewModels;
using SVL.Core.Platform.Abstractions;
using System;
using System.IO;
using System.Linq;

namespace SVL.Migration.Tests;

[TestClass]
public class ModProfileTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "svl-modprofile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void ModProfileRecord_EffectiveModsPath_WhenEmpty_ReturnsDefaultModsFolder()
    {
        var gamePath = Path.Combine(_tempDir, "Game");
        Directory.CreateDirectory(gamePath);

        var profile = new ModProfileRecord
        {
            Name = "默认",
            ModsPath = string.Empty
        };

        var effective = profile.GetEffectiveModsPath(gamePath);
        Assert.AreEqual(Path.Combine(gamePath, "Mods"), effective);
    }

    [TestMethod]
    public void ModProfileRecord_EffectiveModsPath_WhenCustom_ReturnsCustomModsFolder()
    {
        var gamePath = Path.Combine(_tempDir, "Game");
        var customMods = Path.Combine(_tempDir, "CustomMods");

        var profile = new ModProfileRecord
        {
            Name = "SVE 扩展包",
            ModsPath = customMods
        };

        var effective = profile.GetEffectiveModsPath(gamePath);
        Assert.AreEqual(Path.GetFullPath(customMods), effective);
    }

    [TestMethod]
    public void ModProfileStore_GetProfilesForInstance_AutoCreatesDefault()
    {
        var store = new ModProfileStore(_tempDir);
        var instancePath = Path.Combine(_tempDir, "InstanceA");

        var profiles = store.GetProfilesForInstance(instancePath, "MyInstance");

        Assert.IsNotNull(profiles);
        Assert.IsTrue(profiles.Count >= 1);
        var defaultProfile = profiles.FirstOrDefault(p => p.IsDefault);
        Assert.IsNotNull(defaultProfile);
        Assert.AreEqual("default", defaultProfile.Id);
    }

    [TestMethod]
    public void ModProfileStore_UpsertAndDelete_WorksAsExpected()
    {
        var store = new ModProfileStore(_tempDir);
        var instancePath = Path.Combine(_tempDir, "InstanceB");

        var newProfile = new ModProfileRecord
        {
            Id = "test-custom-1",
            Name = "田园生活",
            InstanceKey = instancePath,
            ModsPath = Path.Combine(_tempDir, "FarmMods"),
            EnableCustomSavePath = true,
            CustomSavePath = Path.Combine(_tempDir, "FarmSaves")
        };

        store.UpsertProfile(newProfile);

        var list = store.GetProfilesForInstance(instancePath);
        Assert.IsTrue(list.Any(p => p.Id == "test-custom-1"));

        // 不能删除默认预设
        var deletedDefault = store.DeleteProfile("default");
        Assert.IsFalse(deletedDefault);

        // 可以删除自定义预设
        var deletedCustom = store.DeleteProfile("test-custom-1");
        Assert.IsTrue(deletedCustom);

        var listAfter = store.GetProfilesForInstance(instancePath);
        Assert.IsFalse(listAfter.Any(p => p.Id == "test-custom-1"));
    }

    [TestMethod]
    public void LaunchPageViewModel_BuildLaunchArguments_InjectsModsPathAndSavePath()
    {
        var settingsStore = new AppUserSettingsStore(_tempDir);
        var profileStore = new ModProfileStore(_tempDir);
        var localization = new LocalizationService(settingsStore);
        var imageService = new ImageResourceService(localization);

        var gamePath = Path.Combine(_tempDir, "StardewGame");
        Directory.CreateDirectory(gamePath);
        Directory.CreateDirectory(Path.Combine(gamePath, "Mods"));

        var customMods = Path.Combine(_tempDir, "SveMods");
        var customSaves = Path.Combine(_tempDir, "SveSaves");

        var launchVm = (LaunchPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(LaunchPageViewModel));
        SetField(launchVm, "_settingsStore", settingsStore);
        SetField(launchVm, "_localizationService", localization);
        SetField(launchVm, "_imageResourceService", imageService);
        SetField(launchVm, "_profileStore", profileStore);
        SetField(launchVm, "_currentGamePath", gamePath);
        SetField(launchVm, "_instanceName", "TestInstance");

        // 1. 默认预设：不注入 --mods-path
        launchVm.SelectedModProfile = new ModProfileRecord
        {
            Id = "default",
            Name = "默认",
            IsDefault = true,
            ModsPath = string.Empty
        };

        var settings = new AppUserSettings();
        var defaultArgsMethod = typeof(LaunchPageViewModel).GetMethod("BuildLaunchArguments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(defaultArgsMethod);

        var defaultArgs = (string)defaultArgsMethod.Invoke(launchVm, [settings])!;
        Assert.IsFalse(defaultArgs.Contains("--mods-path"));
        Assert.IsFalse(defaultArgs.Contains("--save-path"));

        // 2. 自定义预设且包含独立存档：注入 --mods-path 与 --save-path
        launchVm.SelectedModProfile = new ModProfileRecord
        {
            Id = "custom-1",
            Name = "SVE扩展",
            ModsPath = customMods,
            EnableCustomSavePath = true,
            CustomSavePath = customSaves
        };

        var customArgs = (string)defaultArgsMethod.Invoke(launchVm, [settings])!;
        Assert.IsTrue(customArgs.Contains("--mods-path"));
        Assert.IsTrue(customArgs.Contains("SveMods"));
        Assert.IsTrue(customArgs.Contains("--save-path"));
        Assert.IsTrue(customArgs.Contains("SveSaves"));
    }

    [TestMethod]
    public void LaunchPageViewModel_WhenLaunchModeIsVanilla_HidesModProfileSelectorAndShowsNotice()
    {
        var settingsStore = new AppUserSettingsStore(_tempDir);
        var initialSettings = settingsStore.Load();
        var gamePath = Path.Combine(_tempDir, "StardewGameWithSmapi");
        Directory.CreateDirectory(gamePath);
        File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.exe"), "dummy");
        File.WriteAllText(Path.Combine(gamePath, "StardewModdingAPI.exe"), "dummy");
        initialSettings.PreferredInstancePath = gamePath;
        initialSettings.InstanceName = "VanillaTestInstance";
        initialSettings.PreferredLaunchMode = "Vanilla";
        settingsStore.Save(initialSettings);

        var profileStore = new ModProfileStore(_tempDir);
        var localization = new LocalizationService(settingsStore);
        var imageService = new ImageResourceService(localization);

        var launchVm = (LaunchPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(LaunchPageViewModel));
        SetField(launchVm, "_settingsStore", settingsStore);
        SetField(launchVm, "_localizationService", localization);
        SetField(launchVm, "_imageResourceService", imageService);
        SetField(launchVm, "_profileStore", profileStore);

        launchVm.RefreshFromSettingsAndEnvironment();

        // 验证原版模式下不展示预设选择器，展示原版提示且允许切回 SMAPI
        Assert.IsFalse(launchVm.IsModProfileSelectorVisible, "原版模式下不应显示 Mod 预设下拉选择器");
        Assert.IsTrue(launchVm.IsVanillaLaunchNoticeVisible, "原版模式下应显示原版纯净说明卡片");
        Assert.IsTrue(launchVm.CanSwitchToSmapi, "已安装 SMAPI 的原版模式下应允许切换到 SMAPI 模式");
        Assert.IsTrue(launchVm.StatusHeadline.Contains("运行环境"), "状态栏标题应为功能性运行环境标题");
        Assert.IsTrue(launchVm.StatusSubline.Contains("原版"), "副标题应提示原版启动");

        // 执行一键切换至 SMAPI 启动
        launchVm.SwitchToSmapiLaunchCommand.Execute(null);

        Assert.AreEqual("SMAPI", settingsStore.Load().PreferredLaunchMode);
        Assert.IsTrue(launchVm.IsModProfileSelectorVisible, "切为 SMAPI 后应恢复显示 Mod 预设下拉选择器");
        Assert.IsFalse(launchVm.IsVanillaLaunchNoticeVisible, "切为 SMAPI 后不应展示原版提示");
        Assert.IsFalse(launchVm.CanSwitchToSmapi, "切为 SMAPI 后无需再显示切换 SMAPI 按钮");
        Assert.IsTrue(launchVm.StatusSubline.Contains("SMAPI"), "副标题应提示 SMAPI 模式");
    }

    [TestMethod]
    public void LaunchPageViewModel_WhenLaunchModeIsSmapi_ShowsModProfileSelector()
    {
        var settingsStore = new AppUserSettingsStore(_tempDir);
        var initialSettings = settingsStore.Load();
        var gamePath = Path.Combine(_tempDir, "StardewGameSmapiDefault");
        Directory.CreateDirectory(gamePath);
        File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.exe"), "dummy");
        File.WriteAllText(Path.Combine(gamePath, "StardewModdingAPI.exe"), "dummy");
        initialSettings.PreferredInstancePath = gamePath;
        initialSettings.InstanceName = "SmapiTestInstance";
        initialSettings.PreferredLaunchMode = "SMAPI";
        settingsStore.Save(initialSettings);

        var profileStore = new ModProfileStore(_tempDir);
        var localization = new LocalizationService(settingsStore);
        var imageService = new ImageResourceService(localization);

        var launchVm = (LaunchPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(LaunchPageViewModel));
        SetField(launchVm, "_settingsStore", settingsStore);
        SetField(launchVm, "_localizationService", localization);
        SetField(launchVm, "_imageResourceService", imageService);
        SetField(launchVm, "_profileStore", profileStore);

        launchVm.RefreshFromSettingsAndEnvironment();

        Assert.IsTrue(launchVm.IsModProfileSelectorVisible);
        Assert.IsFalse(launchVm.IsVanillaLaunchNoticeVisible);
        Assert.IsFalse(launchVm.CanSwitchToSmapi);
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(target, value);
    }
}
