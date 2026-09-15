using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using System.IO.Compression;
using System.Text.Json;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using SVL.Avalonia.ViewModels;
using SVL.Core.Platform.Abstractions;
using SVL.Core.Platform.IO;
using SVL.Core.Platform.Modpack;
using SVL.Core.Platform.Services;

namespace SVL.Migration.Tests;

[TestClass]
public sealed class AvaloniaMigrationHardeningTests
{
    [TestMethod]
    public void ModInstallTargetOptions_ShouldListAllSmapiTargetsWithFullPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("该断言使用 Windows 驱动器路径，仅在 Windows 上验证完整路径语义。");
        }

        var options = ModInstallTargetOptions.Build(
        [
            new ModInstallTarget("SMAPI 4.5.2", @"D:\\Games\\Stardew Valley\\versions\\SMAPI 4.5.2", @"D:\\Games\\Stardew Valley", false, "4.5.2"),
            new ModInstallTarget("SMAPI 4.5.1", @"D:\\Games\\Stardew Valley\\versions\\SMAPI 4.5.1", @"D:\\Games\\Stardew Valley", false, "4.5.1"),
            // 同一路径可能同时来自手动注册和自动探测，只在对话框保留一项。
            new ModInstallTarget("旧名称", @"D:\\Games\\Stardew Valley\\versions\\SMAPI 4.5.2", @"D:\\Games\\Stardew Valley", false, "4.5.2")
        ]);

        Assert.AreEqual(2, options.Count);
        Assert.AreEqual("SMAPI 4.5.2", options[0].DisplayName);
        Assert.AreEqual(
            @"D:\\Games\\Stardew Valley\\versions\\SMAPI 4.5.2",
            options[0].TargetPath);
        Assert.AreEqual("SMAPI 4.5.1", options[1].DisplayName);
        Assert.IsTrue(options.All(option => Path.IsPathFullyQualified(option.TargetPath)));
    }

    [TestMethod]
    public void ModpackSearchPage_ShouldUseConfiguredDefaultSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-search-page-filter-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsStore = new AppUserSettingsStore(root);
            settingsStore.Save(new AppUserSettings { DefaultModSource = "Curseforge" });
            var catalog = new RemoteCatalogService(settingsStore);

            var modpackPage = new ModpackSearchPageViewModel(catalog);
            Assert.AreEqual("Curseforge", modpackPage.SelectedSource);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try
                {
                    File.SetAttributes(Path.Combine(root, "version"), FileAttributes.Normal);
                }
                catch { }
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void SvlSourceEntries_ShouldMergeIncompleteSourcesWithManifestEntries()
    {
        using var sourcesDocument = JsonDocument.Parse(
            "[{\"displayName\":\"Content Patcher\",\"directoryName\":\"ContentPatcher\",\"source\":\"NexusMods\"}," +
            "{\"id\":\"Generic.ModConfigMenu\",\"directoryName\":\"GenericModConfigMenu\",\"source\":\"NexusMods\"}]",
            new JsonDocumentOptions { AllowTrailingCommas = true });
        using var manifestDocument = JsonDocument.Parse(
            "[{\"name\":\"Content Patcher\",\"source\":{\"platform\":\"NexusMods\",\"modId\":1915,\"fileId\":146548}}," +
            "{\"name\":\"Generic Mod Config Menu\",\"uniqueId\":\"Generic.ModConfigMenu\",\"source\":{\"platform\":\"NexusMods\",\"modId\":5098,\"fileId\":145906}}]");

        var mergeMethod = typeof(ModpackInstallService).GetMethod(
            "MergeSourceEntriesWithManifest",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(mergeMethod);

        var merged = (List<JsonElement>)mergeMethod!.Invoke(
            null,
            [
                sourcesDocument.RootElement.EnumerateArray().Select(item => item.Clone()).ToList(),
                manifestDocument.RootElement.EnumerateArray().Select(item => item.Clone()).ToList()
            ])!;

        Assert.AreEqual(2, merged.Count, "应补入 sources.json 遗漏但有完整来源的 Mod");

        var contentPatcher = merged.Single(entry =>
            entry.GetProperty("name").GetString() == "Content Patcher");
        var contentSource = contentPatcher.GetProperty("source");
        Assert.AreEqual(JsonValueKind.Object, contentSource.ValueKind);
        Assert.AreEqual(1915, contentSource.GetProperty("modId").GetInt64());
        Assert.AreEqual(146548, contentSource.GetProperty("fileId").GetInt64());
        Assert.AreEqual("NexusMods", contentSource.GetProperty("platform").GetString());

        var genericMenu = merged.Single(entry =>
            entry.GetProperty("name").GetString() == "Generic Mod Config Menu");
        Assert.AreEqual(145906, genericMenu.GetProperty("source").GetProperty("fileId").GetInt64());
    }

    [TestMethod]
    public void ExportSourceIdentity_ShouldRecoverLegacyNxmAndCurseforgeTokens()
    {
        var infer = typeof(VersionSettingsPageViewModel).GetMethod(
            "InferExportSourceIdentity",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(infer);

        var nxmArguments = new object[]
        {
            "未知",
            string.Empty,
            string.Empty,
            new[] { "nxm://stardewvalley/mods/29868/files/7448774?key=secret" }
        };
        infer!.Invoke(null, nxmArguments);

        Assert.AreEqual("NexusMods", nxmArguments[0]);
        Assert.AreEqual("29868", nxmArguments[1]);
        Assert.AreEqual("7448774", nxmArguments[2]);

        var curseforgeArguments = new object[]
        {
            string.Empty,
            string.Empty,
            string.Empty,
            new[] { "cf-1012214-5312529" }
        };
        infer.Invoke(null, curseforgeArguments);

        Assert.AreEqual("Curseforge", curseforgeArguments[0]);
        Assert.AreEqual("1012214", curseforgeArguments[1]);
        Assert.AreEqual("5312529", curseforgeArguments[2]);

        var explicitCurseforgeArguments = new object[]
        {
            "Curseforge",
            "1012214",
            "5312529",
            new[]
            {
                "https://www.nexusmods.com/stardewvalley/mods/29868?tab=files&file_id=7448774",
                "https://edge.forgecdn.net/files/5312/529/ContentPatcher.zip"
            }
        };
        infer.Invoke(null, explicitCurseforgeArguments);

        Assert.AreEqual("Curseforge", explicitCurseforgeArguments[0]);
        Assert.AreEqual("1012214", explicitCurseforgeArguments[1]);
        Assert.AreEqual("5312529", explicitCurseforgeArguments[2]);

        var zeroIdArguments = new object[]
        {
            "Unknown",
            "0",
            "0",
            new[] { "nxm://stardewvalley/mods/29868/files/7448774?key=secret" }
        };
        infer.Invoke(null, zeroIdArguments);

        Assert.AreEqual("NexusMods", zeroIdArguments[0]);
        Assert.AreEqual("29868", zeroIdArguments[1]);
        Assert.AreEqual("7448774", zeroIdArguments[2]);
    }

    [TestMethod]
    public void SourceProjectIdNormalization_ShouldKeepCurseforgeProjectAndRejectZero()
    {
        var normalizeProject = typeof(VersionSettingsPageViewModel).GetMethod(
            "NormalizeProjectId",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var normalizePositive = typeof(VersionSettingsPageViewModel).GetMethod(
            "NormalizePositiveId",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(normalizeProject);
        Assert.IsNotNull(normalizePositive);

        Assert.AreEqual("1012214", normalizeProject!.Invoke(null, ["cf-1012214-5312529"]));
        Assert.AreEqual("1012214", normalizeProject.Invoke(
            null,
            ["https://www.curseforge.com/stardewvalley/mods/1012214/files/5312529"]));
        Assert.AreEqual(string.Empty, normalizePositive!.Invoke(null, ["0"]));
        Assert.AreEqual("5312529", normalizePositive.Invoke(null, ["5312529"]));
    }

    [TestMethod]
    public void InstanceNameValidator_RejectsReservedDeviceNamesWithExtensions()
    {
        Assert.IsFalse(InstanceNameValidator.IsValid("CON.txt"));
        Assert.IsFalse(InstanceNameValidator.IsValid("COM1.profile"));
        Assert.AreEqual("CON.foo_Instance", InstanceNameValidator.Sanitize("CON.foo"));
    }

    [TestMethod]
    public void InstanceRuntimePathResolver_DistinguishesLegacyAndCurrentLayouts()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-runtime-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Assert.AreEqual(root, InstanceRuntimePathResolver.Resolve(root));

            var legacyRuntime = Path.Combine(root, "game");
            Directory.CreateDirectory(legacyRuntime);
            File.WriteAllText(Path.Combine(legacyRuntime, "Stardew Valley.dll"), string.Empty);

            Assert.AreEqual(legacyRuntime, InstanceRuntimePathResolver.Resolve(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void WindowTitleService_ReplacesSupportedPlaceholders()
    {
        var title = WindowTitleService.ReplacePlaceholders(
            "<name> | <ver> | <smver> | <modscount>",
            "1.6.15.24354",
            "4.5.2.0",
            43,
            "测试实例");

        Assert.AreEqual("测试实例 | 1.6.15.24354 | 4.5.2.0 | 43", title);
    }

    [TestMethod]
    public void SmapiExternalCallback_ShouldRemainConsumedBrieflyAfterWorkflowCompletes()
    {
        var viewModel = (DownloadPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(DownloadPageViewModel));
        SetPrivateField(viewModel, "_nxmLinkParser", new NxmLinkParser());

        var active = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(
            StringComparer.OrdinalIgnoreCase);
        var recent = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["2400:12000"] = DateTimeOffset.UtcNow,
            ["2400:12001"] = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        SetPrivateField(viewModel, "_activeSmapiExternalWorkflows", active);
        SetPrivateField(viewModel, "_recentSmapiExternalCallbacks", recent);

        Assert.IsTrue(viewModel.IsActiveSmapiExternalCallback(
            "nxm://stardewvalley/mods/2400/files/12000?key=test"));
        Assert.IsFalse(viewModel.IsActiveSmapiExternalCallback(
            "nxm://stardewvalley/mods/2400/files/12001?key=test"));
    }

    [TestMethod]
    public void ModManageItem_ShouldToggleBetweenSourceAndLocalizedText()
    {
        var item = new ModManageItem
        {
            DisplayName = "Content Patcher",
            Description = "Content Patcher description"
        };

        item.SetLocalizationData(
            "Content Patcher",
            "内容补丁",
            "Content Patcher description",
            "内容补丁说明",
            useLocalizedText: true);

        Assert.AreEqual("内容补丁", item.DisplayName);
        Assert.AreEqual("内容补丁说明", item.Description);
        Assert.AreEqual("EN", item.LocalizationToggleButtonText);

        item.SetLocalizationLanguage(useLocalizedText: false);
        Assert.AreEqual("Content Patcher", item.DisplayName);
        Assert.AreEqual("Content Patcher description", item.Description);
        Assert.AreEqual("中", item.LocalizationToggleButtonText);
    }

    [TestMethod]
    public void InstalledSmapiPackageBuilder_ShouldCreateReusablePackageFromExistingInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-installed-smapi-builder-" + Guid.NewGuid().ToString("N"));
        var basePath = Path.Combine(root, "Stardew Valley");
        var runtimePath = Path.Combine(basePath, "versions", "SMAPI 4.5.2", "game");
        var outputPath = Path.Combine(root, "cache");
        try
        {
            Directory.CreateDirectory(Path.Combine(runtimePath, "Mods"));
            File.WriteAllText(Path.Combine(runtimePath, "Stardew Valley.dll"), "game");
            File.WriteAllText(Path.Combine(runtimePath, "StardewModdingAPI.dll"), "smapi");
            File.WriteAllText(Path.Combine(runtimePath, "StardewModdingAPI.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(runtimePath, "Mods", "should-not-be-copied.txt"), "mod");

            var builder = typeof(ModpackInstallService).Assembly.GetType(
                "SVL.Avalonia.Services.InstalledSmapiPackageBuilder");
            Assert.IsNotNull(builder);
            var method = builder!.GetMethod("TryCreate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(method);

            var arguments = new object[] { basePath, "4.5.2", null!, outputPath, "", "", "" };
            var created = (bool)method!.Invoke(null, arguments)!;

            Assert.IsTrue(created);
            var packagePath = (string)arguments[4]!;
            Assert.AreEqual(runtimePath, arguments[5]);
            Assert.AreEqual("4.5.2", arguments[6]);
            using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
            Assert.IsTrue(archive.Entries.Any(entry =>
                entry.FullName.EndsWith("/install.dat", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void InstanceRuntimePathResolver_NormalizesVersionPathToOwningBase()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-base-path-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var versionRoot = Path.Combine(root, "versions", "My Pack");
            var legacyRuntime = Path.Combine(versionRoot, "game");
            Directory.CreateDirectory(legacyRuntime);

            Assert.AreEqual(root, InstanceRuntimePathResolver.ResolveBasePath(root));
            Assert.AreEqual(root, InstanceRuntimePathResolver.ResolveBasePath(versionRoot));
            Assert.AreEqual(root, InstanceRuntimePathResolver.ResolveBasePath(legacyRuntime));
            Assert.AreEqual(root, InstanceRuntimePathResolver.ResolveBasePath(Path.Combine(versionRoot, "Mods")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void DownloadTaskStateStore_PersistsStateAndNexusSourceIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-task-state-test-" + Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(root, "tasks.json.gz");
        try
        {
            Directory.CreateDirectory(root);
            var task = new DownloadTaskItem
            {
                Name = "test-mod",
                SourceModId = 2400,
                SourceFileId = 898372,
                TaskState = DownloadTaskState.Failed,
                Status = "下载失败（可重试）",
                InstalledDirectory = Path.Combine(root, "instance"),
                CollectionSlug = "cached-collection",
                CollectionRevision = 12,
                SpeedText = "2.3 MB/s",
                EtaText = "约 10 秒",
                TotalSizeText = "45.2 MB",
                DownloadedSizeText = "12.8 MB",
                SubProgressText = "3/8 已完成",
                SubProgress = 37
            };

            var store = new DownloadTaskStateStore();
            store.Save(statePath, [task]);
            var records = store.Load(statePath, out var brokenBackupPath);

            Assert.IsTrue(string.IsNullOrEmpty(brokenBackupPath));
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(DownloadTaskState.Failed, records[0].TaskState);
            Assert.AreEqual(2400, records[0].SourceModId);
            Assert.AreEqual(898372, records[0].SourceFileId);
            Assert.AreEqual(task.CollectionSlug, records[0].CollectionSlug);
            Assert.AreEqual(task.CollectionRevision, records[0].CollectionRevision);
            Assert.AreEqual(task.InstalledDirectory, records[0].InstalledDirectory);
            Assert.AreEqual(task.SpeedText, records[0].SpeedText);
            Assert.AreEqual(task.EtaText, records[0].EtaText);
            Assert.AreEqual(task.TotalSizeText, records[0].TotalSizeText);
            Assert.AreEqual(task.DownloadedSizeText, records[0].DownloadedSizeText);
            Assert.AreEqual(task.SubProgressText, records[0].SubProgressText);
            Assert.AreEqual(task.SubProgress, records[0].SubProgress);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void LegacyConfigurationMigration_ImportsWpfSettingsAndInstancesIdempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-legacy-migration-test-" + Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(root, "legacy");
        var avaloniaRoot = Path.Combine(root, "avalonia");
        var gamePath = Path.Combine(root, "Stardew Valley");
        var isolatedPath = Path.Combine(gamePath, "versions", "我的整合包", "game");
        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(avaloniaRoot);
            Directory.CreateDirectory(gamePath);
            Directory.CreateDirectory(isolatedPath);
            File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.dll"), string.Empty);
            File.WriteAllText(Path.Combine(isolatedPath, "Stardew Valley.dll"), string.Empty);
            File.WriteAllText(Path.Combine(isolatedPath, "StardewModdingAPI.dll"), string.Empty);

            File.WriteAllText(
                Path.Combine(legacyRoot, "app.json"),
                """
                {
                  "LauncherTitle": "旧 WPF 启动器",
                  "ThemeMode": 1,
                  "Language": "en-US",
                  "MaxConcurrentModDownloads": 2,
                  "DownloadSegmentThreads": 8
                }
                """);
            File.WriteAllText(
                Path.Combine(legacyRoot, "nexusmods.json"),
                "{\"ApiKey\":\"legacy-nexus-api-key\"}",
                System.Text.Encoding.Unicode);
            File.WriteAllText(
                Path.Combine(legacyRoot, "instances.json"),
                $$"""
                [
                  {
                    "Id": "legacy-default",
                    "Name": "旧实例",
                    "GamePath": "{{gamePath.Replace("\\", "\\\\")}}",
                    "IsSMAPIInstance": true,
                    "IsDefault": true,
                    "IsFavorite": true,
                    "EnableIsolation": false,
                    "Tags": ["Base"]
                  },
                  {
                    "Id": "legacy-pack",
                    "Name": "我的整合包",
                    "GamePath": "{{gamePath.Replace("\\", "\\\\")}}",
                    "IsSMAPIInstance": true,
                    "IsDefault": false,
                    "IsFavorite": true,
                    "EnableIsolation": true,
                    "Tags": []
                  }
                ]
                """, System.Text.Encoding.Unicode);

            var settingsStore = new AppUserSettingsStore(avaloniaRoot);
            var registryStore = new InstanceRegistryStore(avaloniaRoot);
            var migration = new LegacyConfigurationMigrationService(
                settingsStore,
                registryStore,
                legacyRoot,
                Path.Combine(legacyRoot, "instances.json"));

            var first = migration.Migrate();
            var settings = settingsStore.Load();
            var instances = registryStore.LoadManualInstances();

            Assert.IsTrue(first.Completed);
            Assert.IsTrue(first.HasSources);
            Assert.AreEqual("旧 WPF 启动器", settings.LauncherTitle);
            Assert.AreEqual("深色", settings.ThemeMode);
            Assert.AreEqual("en-US", settings.UiLanguage);
            Assert.AreEqual(2, settings.CollectionDownloadParallelism);
            Assert.AreEqual(8, settings.DownloadSegmentThreads);
            Assert.AreEqual("legacy-nexus-api-key", settings.NexusApiKey);
            Assert.AreEqual(gamePath, settings.PreferredInstancePath);
            Assert.AreEqual("旧实例", settings.InstanceName);
            Assert.AreEqual(1, instances.Count);
            Assert.AreEqual(gamePath, instances[0].Path);
            Assert.IsTrue(settings.FavoriteInstanceKeys.Any(key => key.EndsWith("|SMAPI", StringComparison.OrdinalIgnoreCase)));

            // 默认实例仍是旧 WPF 标记的 Base 实例，但隔离实例的收藏键必须落到实际运行目录。
            Assert.IsTrue(settings.FavoriteInstanceKeys.Any(key =>
                key.StartsWith(isolatedPath, StringComparison.OrdinalIgnoreCase) &&
                key.EndsWith("|SMAPI", StringComparison.OrdinalIgnoreCase)));

            // 使用独立目标目录验证：如果默认记录是隔离实例，首选路径应还原到
            // versions/<name>/game，而不是继续指向 Base。
            var isolatedOnlyRoot = Path.Combine(root, "isolated-only");
            var isolatedSettingsStore = new AppUserSettingsStore(isolatedOnlyRoot);
            var isolatedRegistryStore = new InstanceRegistryStore(isolatedOnlyRoot);
            var isolatedLegacyPath = Path.Combine(root, "isolated-only.json");
            File.WriteAllText(
                isolatedLegacyPath,
                $$"""
                [
                  {
                    "Id": "legacy-pack",
                    "Name": "我的整合包",
                    "GamePath": "{{gamePath.Replace("\\", "\\\\")}}",
                    "IsSMAPIInstance": true,
                    "IsDefault": true,
                    "IsFavorite": false,
                    "EnableIsolation": true,
                    "Tags": []
                  }
                ]
                """);
            var isolatedMigration = new LegacyConfigurationMigrationService(
                isolatedSettingsStore,
                isolatedRegistryStore,
                legacyRoot,
                isolatedLegacyPath);
            var isolatedFirst = isolatedMigration.Migrate();
            Assert.IsTrue(isolatedFirst.Completed);
            Assert.AreEqual(isolatedPath, isolatedSettingsStore.Load().PreferredInstancePath);

            var second = migration.Migrate();
            Assert.IsTrue(second.Completed);
            Assert.AreEqual(0, second.ImportedSettingsCount);
            Assert.AreEqual(0, second.ImportedInstanceCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void LegacyConfigurationMigration_HandlesStringEnumsAndExistingDefaultSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-legacy-string-settings-" + Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(root, "legacy");
        var avaloniaRoot = Path.Combine(root, "avalonia");
        var gamePath = Path.Combine(root, "Stardew Valley");
        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(gamePath);
            File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.dll"), string.Empty);

            // 模拟 WPF 配置被某个 JSON 工具以字符串形式写出；同时预先创建
            // Avalonia 默认 settings.json，覆盖“已有配置但仍是默认值”的启动场景。
            var settingsStore = new AppUserSettingsStore(avaloniaRoot);
            settingsStore.Save(new AppUserSettings());
            File.WriteAllText(
                Path.Combine(legacyRoot, "app.json"),
                """
                {
                  "GameWindowTitle": "自定义游戏标题",
                  "WindowSizeMode": "Maximized",
                  "ThemeMode": "Dark",
                  "CheckPrereleaseUpdates": true,
                  "PreferredUpdateSource": "Gitee"
                }
                """, System.Text.Encoding.Unicode);
            File.WriteAllText(
                Path.Combine(legacyRoot, "gamepath.json"),
                JsonSerializer.Serialize(gamePath),
                System.Text.Encoding.Unicode);

            var migration = new LegacyConfigurationMigrationService(
                settingsStore,
                new InstanceRegistryStore(avaloniaRoot),
                legacyRoot,
                Path.Combine(legacyRoot, "instances.json"));

            var result = migration.Migrate();
            var settings = settingsStore.Load();

            Assert.IsTrue(result.Completed);
            Assert.AreEqual("自定义游戏标题", settings.GameWindowTitle);
            Assert.AreEqual("最大化", settings.WindowSizeMode);
            Assert.AreEqual("深色", settings.ThemeMode);
            Assert.AreEqual("预览版", settings.UpdateChannel);
            Assert.AreEqual("Gitee (国内加速)", settings.PreferredUpdateSource);
            Assert.AreEqual(gamePath, settings.PreferredInstancePath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void LegacyConfigurationMigration_ShouldRetryWhenInstanceSourceAppearsLater()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-legacy-late-source-" + Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(root, "legacy");
        var avaloniaRoot = Path.Combine(root, "avalonia");
        var gamePath = Path.Combine(root, "Stardew Valley");
        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(avaloniaRoot);
            Directory.CreateDirectory(gamePath);
            File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.dll"), string.Empty);
            File.WriteAllText(Path.Combine(legacyRoot, "app.json"), "{\"LauncherTitle\":\"旧配置\"}");

            var settingsStore = new AppUserSettingsStore(avaloniaRoot);
            var registryStore = new InstanceRegistryStore(avaloniaRoot);
            var migration = new LegacyConfigurationMigrationService(
                settingsStore,
                registryStore,
                legacyRoot,
                Path.Combine(legacyRoot, "instances.json"));
            Assert.IsTrue(migration.Migrate().Completed);

            // 模拟旧程序目录中的 instances.json 在首次启动后才出现。
            File.WriteAllText(
                Path.Combine(legacyRoot, "instances.json"),
                $$"""
                [{
                  "Id": "late-instance",
                  "Name": "后出现的实例",
                  "GamePath": "{{gamePath.Replace("\\", "\\\\")}}",
                  "IsDefault": true,
                  "Tags": ["Base"]
                }]
                """
            );

            var second = migration.Migrate();
            Assert.IsTrue(second.Completed);
            Assert.AreEqual(1, second.ImportedInstanceCount);
            Assert.AreEqual(gamePath, registryStore.LoadManualInstances().Single().Path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void LegacyConfigurationMigration_ShouldRecoverWhenMigratedTargetsAreRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-legacy-target-recovery-" + Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(root, "legacy");
        var avaloniaRoot = Path.Combine(root, "avalonia");
        var gamePath = Path.Combine(root, "Stardew Valley");
        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(gamePath);
            File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.dll"), string.Empty);
            File.WriteAllText(Path.Combine(legacyRoot, "app.json"), "{\"LauncherTitle\":\"旧配置\"}");
            File.WriteAllText(
                Path.Combine(legacyRoot, "instances.json"),
                $$"""
                [{
                  "Id": "recoverable-instance",
                  "Name": "可恢复实例",
                  "GamePath": "{{gamePath.Replace("\\", "\\\\")}}",
                  "IsDefault": true
                }]
                """
            );

            var settingsStore = new AppUserSettingsStore(avaloniaRoot);
            var registryStore = new InstanceRegistryStore(avaloniaRoot);
            var migration = new LegacyConfigurationMigrationService(
                settingsStore,
                registryStore,
                legacyRoot,
                Path.Combine(legacyRoot, "instances.json"));

            var first = migration.Migrate();
            Assert.IsTrue(first.Completed);
            Assert.AreEqual("旧配置", settingsStore.Load().LauncherTitle);
            Assert.AreEqual(1, registryStore.LoadManualInstances().Count);

            // 模拟用户清理 Avalonia 配置或升级过程中目标文件丢失；迁移标记仍保留。
            File.Delete(settingsStore.GetSettingsPath());
            File.Delete(registryStore.GetRegistryPath());

            var recovered = migration.Migrate();
            Assert.IsTrue(recovered.Completed);
            Assert.IsTrue(recovered.ImportedSettingsCount > 0);
            Assert.AreEqual("旧配置", settingsStore.Load().LauncherTitle);
            Assert.AreEqual(gamePath, registryStore.LoadManualInstances().Single().Path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void LegacyConfigurationMigration_ShouldFindInstancesUnderSiblingWpfDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-legacy-sibling-migration-" + Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(root, "AvaloniaConfig");
        var wpfInstancesPath = Path.Combine(root, "SVL.Desktop", "SVL", "instances.json");
        var avaloniaRoot = Path.Combine(root, "AvaloniaData");
        var gamePath = Path.Combine(root, "Stardew Valley");

        try
        {
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(wpfInstancesPath)!);
            Directory.CreateDirectory(gamePath);
            File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.dll"), string.Empty);
            File.WriteAllText(
                wpfInstancesPath,
                $$"""
                [{
                  "Id": "sibling-wpf-instance",
                  "Name": "并排 WPF 实例",
                  "GamePath": "{{gamePath.Replace("\\", "\\\\")}}",
                  "IsDefault": true,
                  "Tags": ["Base"]
                }]
                """);

            var registryStore = new InstanceRegistryStore(avaloniaRoot);
            var migration = new LegacyConfigurationMigrationService(
                new AppUserSettingsStore(avaloniaRoot),
                registryStore,
                legacyRoot);

            var result = migration.Migrate();
            var instances = registryStore.LoadManualInstances();

            Assert.IsTrue(result.Completed);
            Assert.IsTrue(result.ImportedInstanceCount >= 1);
            Assert.IsTrue(instances.Any(instance =>
                string.Equals(instance.Path, gamePath, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_RecognizesAndExtractsSevenZipCollection()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-7z-detection-test-" + Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source");
        var archivePath = Path.Combine(root, "collection.7z");
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "collection.json"),
                "{\"info\":{\"name\":\"测试 Collection\",\"author\":\"SVL\"},\"mods\":[]}");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var stream = File.OpenRead(Path.Combine(sourceDirectory, "collection.json")))
            {
                writer.Write("collection.json", stream, null);
            }

            Assert.IsTrue(ModpackTypeDetector.IsSupportedFile(archivePath));
            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.NexusCollection, detection.Type);
                Assert.AreEqual("测试 Collection", detection.ModpackName);
                Assert.AreEqual(0, detection.ModCount);
                Assert.IsTrue(File.Exists(Path.Combine(detection.TempExtractPath, "collection.json")));
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void CurseforgeManifestParser_ShouldAcceptStringProjectAndFileIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-string-cf-ids-" + Guid.NewGuid().ToString("N"));
        var manifestPath = Path.Combine(root, "manifest.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                manifestPath,
                "{\"name\":\"字符串 ID 包\",\"version\":\"1.0.0\",\"files\":[{\"projectID\":\"1012214\",\"fileID\":\"5312529\"}]}");

            var manifest = SVL.Core.Platform.Modpack.CurseforgeModpackParser.ParseFromJsonFile(manifestPath);
            Assert.AreEqual(1, manifest.Files.Count);
            Assert.AreEqual(1012214L, manifest.Files[0].ProjectId);
            Assert.AreEqual(5312529L, manifest.Files[0].FileId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackIconImport_ShouldReplaceGeneratedSmapiPresetButKeepCustomIconOnRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-modpack-icon-import-" + Guid.NewGuid().ToString("N"));
        var packageRoot = Path.Combine(root, "package");
        var versionRoot = Path.Combine(root, "version");
        var customIcon = Path.Combine(packageRoot, "icon.png");
        try
        {
            Directory.CreateDirectory(packageRoot);
            Directory.CreateDirectory(versionRoot);
            File.WriteAllBytes(customIcon, [1, 2, 3, 4, 5]);
            var generatedIconPath = Path.Combine(versionRoot, ".svl-instance-icon-smapi.png");
            var generatedMarkerPath = Path.Combine(
                versionRoot,
                ".svl-instance-icon-smapi.generated");
            File.WriteAllBytes(generatedIconPath, [0]);
            File.WriteAllText(generatedMarkerPath, "generated-by-svl\n");

            var extract = typeof(ModpackInstallService).GetMethod(
                "ExtractPackIcon",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(extract);

            var importedPath = extract!.Invoke(null, [packageRoot, versionRoot, null, packageRoot]) as string;
            Assert.AreEqual(generatedIconPath, importedPath);
            CollectionAssert.AreEqual(File.ReadAllBytes(customIcon), File.ReadAllBytes(importedPath!));
            Assert.IsFalse(File.Exists(generatedMarkerPath));

            // 再次导入/部分失败重试时，玩家自定义图标不能被包内图标覆盖。
            File.WriteAllBytes(customIcon, [9, 8, 7]);
            extract.Invoke(null, [packageRoot, versionRoot, null, packageRoot]);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(importedPath!));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void CollectionIconImport_ShouldReplaceGeneratedSmapiPresetButKeepCustomIconOnRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-collection-icon-import-" + Guid.NewGuid().ToString("N"));
        var extractRoot = Path.Combine(root, "collection");
        var versionRoot = Path.Combine(root, "version");
        var importedIcon = Path.Combine(versionRoot, ".svl-instance-icon-smapi.png");
        var generatedMarker = Path.Combine(versionRoot, ".svl-instance-icon-smapi.generated");
        try
        {
            Directory.CreateDirectory(extractRoot);
            Directory.CreateDirectory(versionRoot);
            File.WriteAllBytes(Path.Combine(extractRoot, "collection-icon.png"), [4, 5, 6, 7]);
            File.WriteAllBytes(importedIcon, [0]);
            File.WriteAllText(generatedMarker, "generated-by-svl\n");

            var extract = typeof(CollectionInstallService).GetMethod(
                "ExtractCollectionIcon",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(extract);

            var importedPath = extract!.Invoke(null, [extractRoot, versionRoot, null, null]) as string;
            Assert.AreEqual(importedIcon, importedPath);
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, File.ReadAllBytes(importedIcon));
            Assert.IsFalse(File.Exists(generatedMarker));

            // 部分失败重试时，已由玩家选择的图标不能被包内图标覆盖。
            File.WriteAllBytes(Path.Combine(extractRoot, "collection-icon.png"), [8, 9]);
            extract.Invoke(null, [extractRoot, versionRoot, null, null]);
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, File.ReadAllBytes(importedIcon));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void CollectionIconImport_ShouldPreferCollectionRootIconOverNestedModIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-collection-icon-priority-" + Guid.NewGuid().ToString("N"));
        var extractRoot = Path.Combine(root, "extracted");
        var collectionRoot = Path.Combine(extractRoot, "Collection");
        var nestedModRoot = Path.Combine(collectionRoot, "bundled", "SomeMod");
        var versionRoot = Path.Combine(root, "version");
        var targetIcon = Path.Combine(versionRoot, ".svl-instance-icon-smapi.png");
        try
        {
            Directory.CreateDirectory(nestedModRoot);
            Directory.CreateDirectory(versionRoot);
            File.WriteAllBytes(Path.Combine(collectionRoot, "icon.png"), [10, 11]);
            File.WriteAllBytes(Path.Combine(nestedModRoot, "icon.png"), [20, 21]);
            File.WriteAllBytes(targetIcon, [0]);
            File.WriteAllText(
                Path.Combine(versionRoot, ".svl-instance-icon-smapi.generated"),
                "generated-by-svl\n");

            var extract = typeof(CollectionInstallService).GetMethod(
                "ExtractCollectionIcon",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(extract);

            extract!.Invoke(null, [collectionRoot, versionRoot, null, extractRoot]);
            CollectionAssert.AreEqual(new byte[] { 10, 11 }, File.ReadAllBytes(targetIcon));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ExportIcon_ShouldIgnoreOnlyGeneratedSmapiPreset()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-export-icon-marker-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var iconPath = Path.Combine(root, ".svl-instance-icon-smapi.png");
            File.WriteAllBytes(iconPath, [1, 2, 3]);

            var viewModel = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(VersionSettingsPageViewModel));
            SetPrivateField(viewModel, "_isSmapiInstance", true);

            var resolver = typeof(VersionSettingsPageViewModel).GetMethod(
                "ResolveExportCustomIconPath",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(resolver);

            File.WriteAllText(Path.Combine(root, ".svl-instance-icon-smapi.generated"), "generated-by-svl\n");
            var generatedResult = resolver!.Invoke(viewModel, [root]) as string;
            Assert.IsTrue(string.IsNullOrEmpty(generatedResult));

            // 生成的 SMAPI 默认图标存在时，版本设置中的通用自定义图标仍必须
            // 被导出；不能仅凭目录里存在 generated 标记就把它一并过滤掉。
            var customPath = Path.Combine(root, ".svl-instance-icon.png");
            File.WriteAllBytes(customPath, [9, 8, 7]);
            var customResult = resolver.Invoke(viewModel, [root]) as string;
            Assert.AreEqual(customPath, customResult);

            // 用户主动选择同一内置预设时没有生成标记，仍应作为用户图标导出。
            File.Delete(Path.Combine(root, ".svl-instance-icon-smapi.generated"));
            var userSelectedResult = resolver.Invoke(viewModel, [root]) as string;
            Assert.AreEqual(iconPath, userSelectedResult);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ExportSource_ShouldNotTreatNexusFilePageAsDirectArchive()
    {
        var item = new ExportModSelectionItem
        {
            SourcePlatform = "NexusMods",
            SourceProjectId = "29868",
            SourceDownloadUrl = "https://www.nexusmods.com/stardewvalley/mods/29868?tab=files&nmm=1"
        };

        Assert.IsFalse(item.HasDirectSourceUrl);
        Assert.IsFalse(item.HasCompleteSourceCredential);
        StringAssert.Contains(item.SourceDescription, "FileID 缺失");
    }

    [TestMethod]
    public void ExportPackageSource_ShouldUseSamePageUrlRuleAsSelection()
    {
        var itemType = typeof(VersionSettingsPageViewModel).Assembly.GetType(
            "SVL.Avalonia.ViewModels.ExportModPackageItem");
        Assert.IsNotNull(itemType);

        var item = Activator.CreateInstance(itemType!, nonPublic: true);
        Assert.IsNotNull(item);
        SetProperty(item!, "SourcePlatform", "CurseForge");
        SetProperty(item!, "SourceProjectId", "1012214");
        SetProperty(item!, "SourceDownloadUrl", "https://www.curseforge.com/stardewvalley/mods/1012214/files/5312529");

        var hasDirect = (bool)itemType!.GetProperty("HasDirectSourceUrl")!.GetValue(item)!;
        var hasComplete = (bool)itemType.GetProperty("HasCompleteSourceCredential")!.GetValue(item)!;

        Assert.IsFalse(hasDirect);
        Assert.IsFalse(hasComplete);
    }

    [TestMethod]
    public void DeleteVersionDirectory_ShouldClearReadOnlyFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-delete-readonly-test-" + Guid.NewGuid().ToString("N"));
        var versionRoot = Path.Combine(root, "versions", "ReadOnlyInstance");
        var lockedByAttribute = Path.Combine(versionRoot, ".svl-instance-icon-smapi.png");
        try
        {
            Directory.CreateDirectory(versionRoot);
            File.WriteAllBytes(lockedByAttribute, [1, 2, 3]);
            File.SetAttributes(lockedByAttribute, FileAttributes.ReadOnly);

            var deleteMethod = typeof(VersionSettingsPageViewModel).GetMethod(
                "DeleteVersionDirectorySafe",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(deleteMethod);
            deleteMethod!.Invoke(null, [versionRoot]);

            Assert.IsFalse(Directory.Exists(versionRoot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                File.SetAttributes(root, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModManifestParser_ShouldSkipEmptyAliasAndReadFollowingVersion()
    {
        using var document = JsonDocument.Parse(
            "{\"Version\":\"\",\"releaseVersion\":\" 1.6.15 \"}");
        var parser = typeof(VersionSettingsPageViewModel).GetMethod(
            "GetJsonStringFlexibleByCandidates",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var value = parser!.Invoke(null, [document.RootElement, new[] { "Version", "releaseVersion" }]) as string;
        Assert.AreEqual("1.6.15", value);
    }

    [TestMethod]
    public void ModManifestReader_ShouldDecodeBomlessUtf16AndNestedVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-manifest-encoding-test-" + Guid.NewGuid().ToString("N"));
        var manifestPath = Path.Combine(root, "manifest.json");
        try
        {
            Directory.CreateDirectory(root);
            const string json = "{\"metadata\":{\"version\":\"3.2.1\"}}";
            var utf16WithoutBom = new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: false)
                .GetBytes(json);
            File.WriteAllBytes(manifestPath, utf16WithoutBom);

            Assert.AreEqual(json, ManifestTextReader.ReadAllText(manifestPath));
            using var document = JsonDocument.Parse(ManifestTextReader.ReadAllText(manifestPath));
            var parser = typeof(VersionSettingsPageViewModel).GetMethod(
                "GetManifestVersion",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(parser);
            Assert.AreEqual("3.2.1", parser!.Invoke(null, [document.RootElement]));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void SmapiCacheInspector_ShouldReadVersionFromArchiveRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-smapi-cache-version-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "2400_7448774.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteArchiveText(
                    archive,
                    "SMAPI 4.5.2 installer/internal/windows/install.dat",
                    "install");
            }

            var inspectorType = typeof(ModpackInstallService).Assembly.GetType(
                "SVL.Avalonia.Services.SmapiPackageVersionInspector");
            Assert.IsNotNull(inspectorType);
            var readVersion = inspectorType!.GetMethod(
                "TryReadVersion",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var isCompatible = inspectorType.GetMethod(
                "IsCompatible",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(readVersion);
            Assert.IsNotNull(isCompatible);

            Assert.AreEqual("4.5.2", readVersion!.Invoke(null, [archivePath]));
            Assert.IsTrue((bool)isCompatible!.Invoke(null, ["4.5.2", archivePath]));
            Assert.IsFalse((bool)isCompatible.Invoke(null, ["4.5.3", archivePath]));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void SmapiCacheCompatibility_ShouldRejectUnknownVersionWhenTargetIsKnown()
    {
        var modpackService = typeof(ModpackInstallService);
        var collectionService = typeof(CollectionInstallService);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;

        var modpackMethod = modpackService.GetMethod("IsCompatibleSmapiVersion", flags);
        var collectionMethod = collectionService.GetMethod("IsCompatibleSmapiVersion", flags);
        Assert.IsNotNull(modpackMethod);
        Assert.IsNotNull(collectionMethod);

        Assert.IsFalse((bool)modpackMethod!.Invoke(null, ["4.5.2", ""] )!);
        Assert.IsFalse((bool)collectionMethod!.Invoke(null, ["SMAPI 4.5.2", "latest"] )!);
        Assert.IsTrue((bool)modpackMethod.Invoke(null, ["4.5.2", "4.5.3"])!);
        Assert.IsTrue((bool)collectionMethod.Invoke(null, [null, "unknown"])!);
    }

    [TestMethod]
    public void SmapiOfficialReleaseFallback_ShouldBuildVersionedInstallerUrl()
    {
        var method = typeof(ModpackInstallService).GetMethod(
            "BuildOfficialSmapiReleaseUrl",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        var url = method!.Invoke(null, ["4.5.2"]) as string;
        Assert.AreEqual(
            "https://github.com/Pathoschild/SMAPI/releases/download/4.5.2/SMAPI-4.5.2-installer.zip",
            url);

        var collectionMethod = typeof(CollectionInstallService).GetMethod(
            "BuildOfficialSmapiReleaseUrl",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(collectionMethod);
        Assert.AreEqual(url, collectionMethod!.Invoke(null, ["4.5.2"]));

        var candidatesMethod = typeof(ModpackInstallService).GetMethod(
            "GetOfficialSmapiReleaseVersionCandidates",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(candidatesMethod);
        var candidates = ((IEnumerable<string>)candidatesMethod!.Invoke(null, ["4.5.1.0"])!).ToArray();
        CollectionAssert.AreEqual(new[] { "4.5.1.0", "4.5.1" }, candidates);
    }

    [TestMethod]
    public void ModpackTypeDetector_RecognizesSevenZipWithoutExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-7z-no-extension-test-" + Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source");
        var archivePath = Path.Combine(root, "collection-download");
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            var collectionPath = Path.Combine(sourceDirectory, "collection.json");
            File.WriteAllText(collectionPath, "{\"info\":{\"name\":\"无扩展名 Collection\"},\"mods\":[]}");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var stream = File.OpenRead(collectionPath))
            {
                writer.Write("collection.json", stream, null);
            }

            Assert.IsTrue(ModpackTypeDetector.IsSupportedFile(archivePath));
            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.NexusCollection, detection.Type);
                Assert.AreEqual("无扩展名 Collection", detection.ModpackName);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_UsesSevenZipForPackageArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-7z-install-archive-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "modpack.7z");
        var destination = Path.Combine(root, "extracted");
        try
        {
            Directory.CreateDirectory(root);
            var manifest = Path.Combine(root, "modpack.json");
            File.WriteAllText(manifest, "{\"name\":\"测试整合包\"}");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var stream = File.OpenRead(manifest))
            {
                writer.Write("modpack.json", stream, null);
            }

            var extractor = typeof(ModpackInstallService).GetMethod(
                "ExtractPackageArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(extractor);

            extractor.Invoke(null, [archivePath, destination]);

            Assert.IsTrue(File.Exists(Path.Combine(destination, "modpack.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void LocalModImport_UsesSevenZipAndFlattensNestedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-local-mod-7z-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "ContentPatcher.7z");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            var manifestPath = Path.Combine(root, "manifest.json");
            var contentPath = Path.Combine(root, "content.json");
            File.WriteAllText(manifestPath, "{\"Name\":\"Content Patcher\",\"UniqueID\":\"Pathoschild.ContentPatcher\"}");
            File.WriteAllText(contentPath, "{}");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var manifest = File.OpenRead(manifestPath))
            using (var content = File.OpenRead(contentPath))
            {
                writer.Write("Release/ContentPatcher/manifest.json", manifest, null);
                writer.Write("Release/ContentPatcher/content.json", content, null);
            }

            var importer = typeof(VersionSettingsPageViewModel).GetMethod(
                "ImportModsFromLocalSource",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(importer);

            var imported = (int)importer.Invoke(null, [archivePath, modsPath, true])!;

            Assert.AreEqual(1, imported);
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ContentPatcher", "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ContentPatcher", "content.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "ContentPatcher", "Release")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ReusesCurseforgeSourceCredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-cf-source-reuse-test-" + Guid.NewGuid().ToString("N"));
        var modsPath = Path.Combine(root, "Mods");
        var modDirectory = Path.Combine(modsPath, "Content Patcher");
        try
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(
                Path.Combine(modDirectory, "manifest.json"),
                "{\"Name\":\"Content Patcher\",\"UniqueID\":\"Pathoschild.ContentPatcher\",\"Version\":\"2.9.0\"}");
            File.WriteAllText(
                Path.Combine(modDirectory, "svl-source.json"),
                "{\"platform\":\"Curseforge\",\"projectId\":\"1012214\",\"fileId\":\"5312529\"}");

            var finder = typeof(ModpackInstallService).GetMethod(
                "FindInstalledModDirectories",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(finder);

            var result = finder.Invoke(
                null,
                [modsPath, null, null, null, "Curseforge", "1012214", "5312529"])
                as IReadOnlyList<string>;

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(modDirectory, result[0]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldRecoverCanonicalCurseforgeIds()
    {
        using var document = JsonDocument.Parse(
            "{\"name\":\"Content Patcher\",\"source\":\"cf-1012214-5312529\"}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        var parsed = (bool)parser.Invoke(null, arguments)!;
        Assert.IsTrue(parsed);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("Curseforge", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("1012214", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("5312529", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldReplaceEmptyIdsFromCanonicalToken()
    {
        using var document = JsonDocument.Parse(
            "{\"name\":\"Content Patcher\",\"projectId\":\"\",\"fileId\":\"\",\"source\":\"cf-1012214-5312529\"}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        var parsed = (bool)parser.Invoke(null, arguments)!;
        Assert.IsTrue(parsed);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("Curseforge", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("1012214", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("5312529", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    [DataRow("{\"source\":{\"site\":\"CurseForge\",\"project\":1012214,\"file\":5312529}}")]
    [DataRow("{\"source\":{\"platform\":\"\",\"site\":\"CurseForge\",\"projectId\":\" \",\"project\":1012214,\"fileId\":\"\",\"file\":5312529}}")]
    public void ModpackSourceParser_ShouldReadCommonCurseforgeProjectAliases(string json)
    {
        using var document = JsonDocument.Parse(json);
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        var parsed = (bool)parser.Invoke(null, arguments)!;
        Assert.IsTrue(parsed);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("CurseForge", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("1012214", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("5312529", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ModpackInstall_ShouldPersistDirectSourceUrlForReExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-direct-source-credential-test-" + Guid.NewGuid().ToString("N"));
        var modsPath = Path.Combine(root, "Mods");
        var modDirectory = Path.Combine(modsPath, "Content Patcher");
        try
        {
            Directory.CreateDirectory(modDirectory);
            var writer = typeof(ModpackInstallService).GetMethod(
                "WriteModpackSourceCredentials",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(writer);

            writer!.Invoke(
                null,
                [
                    modsPath,
                    new[] { "Content Patcher" },
                    null,
                    0L,
                    0L,
                    "https://example.invalid/content-patcher.zip",
                    "Content Patcher 2.9.0.zip"
                ]);

            var sourcePath = Path.Combine(modDirectory, "svl-source.json");
            Assert.IsTrue(File.Exists(sourcePath));
            var sourceJson = File.ReadAllText(sourcePath);
            StringAssert.Contains(sourceJson, "https://example.invalid/content-patcher.zip");
            StringAssert.Contains(sourceJson, "Content Patcher 2.9.0.zip");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void CollectionInstall_ShouldPersistDirectSourceUrlForReExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-collection-direct-source-test-" + Guid.NewGuid().ToString("N"));
        var modsPath = Path.Combine(root, "Mods");
        var modDirectory = Path.Combine(modsPath, "Content Patcher");
        try
        {
            Directory.CreateDirectory(modDirectory);
            var writer = typeof(ModpackInstallService).GetMethod(
                "WriteSourceCredentialForInstalledMod",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(writer);

            writer!.Invoke(
                null,
                [
                    modsPath,
                    new[] { "Content Patcher" },
                    null,
                    null,
                    null,
                    "https://example.invalid/content-patcher.zip",
                    "Content Patcher 2.9.0.zip"
                ]);

            var sourcePath = Path.Combine(modDirectory, "svl-source.json");
            Assert.IsTrue(File.Exists(sourcePath));
            var sourceJson = File.ReadAllText(sourcePath);
            StringAssert.Contains(sourceJson, "https://example.invalid/content-patcher.zip");
            StringAssert.Contains(sourceJson, "Content Patcher 2.9.0.zip");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldWriteCredentialForLegacyModNameField()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-modname-source-credential-test-" + Guid.NewGuid().ToString("N"));
        var modsPath = Path.Combine(root, "Mods");
        var modDirectory = Path.Combine(modsPath, "ContentPatcher");
        try
        {
            Directory.CreateDirectory(modDirectory);
            using var document = JsonDocument.Parse(
                "[{\"modName\":\"Content Patcher\",\"directoryName\":\"ContentPatcher\",\"source\":{\"platform\":\"Curseforge\",\"projectId\":1012214,\"fileId\":5312529}}]");
            var writer = typeof(ModpackInstallService).GetMethod(
                "WriteSourceCredentials",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(writer);

            var entries = document.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
            writer!.Invoke(null, [entries, modsPath]);

            var sourcePath = Path.Combine(modDirectory, "svl-source.json");
            Assert.IsTrue(File.Exists(sourcePath));
            StringAssert.Contains(File.ReadAllText(sourcePath), "5312529");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ExportSourceCredential_ShouldTreatHttpUrlAsCompleteSource()
    {
        var item = new ExportModSelectionItem
        {
            SourcePlatform = "未知",
            SourceDownloadUrl = "https://example.invalid/content-patcher.zip"
        };

        Assert.IsTrue(item.HasSourceCredential);
        Assert.IsTrue(item.HasCompleteSourceCredential);
        Assert.AreEqual("直链", item.SourceDescription);
    }

    [TestMethod]
    public void ModDetails_ShouldNotTreatInformationalDownloadTextAsInstallable()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-details-download-option-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new RemoteCatalogService(new AppUserSettingsStore(root));
            var details = new ModDetailsPageViewModel(catalog, new DialogService());
            details.SetResource("[NexusMods#29868] Content Patcher", "");

            details.DownloadOptions.Add("暂无可下载文件");
            details.SelectedDownloadOption = "暂无可下载文件";
            Assert.IsFalse(details.CanQueueDownload);
            Assert.IsFalse(details.CanInstallSelectedDownloadOption);
            Assert.AreEqual(0, details.VersionDownloadGroups.Count);

            details.DownloadOptions.Clear();
            details.DownloadOptions.Add("File 7448774: Content Patcher 2.9.0");
            details.SelectedDownloadOption = details.DownloadOptions[0];
            Assert.IsTrue(details.CanQueueDownload);
            Assert.IsTrue(details.CanInstallSelectedDownloadOption);
            Assert.AreEqual(1, details.VersionDownloadGroups.SelectMany(group => group.Files).Count());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task ModDetails_ShouldExplainWhenResourceSourceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-details-missing-source-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new RemoteCatalogService(new AppUserSettingsStore(root));
            var details = new ModDetailsPageViewModel(catalog, new DialogService());

            await details.LoadDetailsAsync(new CatalogResourceIdentity(
                0,
                "未知资源",
                CatalogSource.Unknown,
                false,
                string.Empty));

            StringAssert.Contains(details.DetailsStatus, "未识别资源来源");
            Assert.IsFalse(details.CanQueueDownload);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModDetails_ShouldStripDownloadOptionMetadataBeforeResolvingUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-details-url-option-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new RemoteCatalogService(new AppUserSettingsStore(root));
            var details = new ModDetailsPageViewModel(catalog, new DialogService());
            details.SetResource("[Curseforge#1012214] Content Patcher", "");

            details.DownloadOptions.Add(
                "File 5312529: Content Patcher 2.9.0 | https://example.invalid/content-patcher.zip ~~channel=Release;gamever=1.6");
            details.SelectedDownloadOption = details.DownloadOptions[0];

            Assert.IsTrue(details.CanQueueDownload);
            Assert.IsTrue(details.CanOpenSelectedDownloadOptionInBrowser);

            details.DownloadOptions.Clear();
            details.DownloadOptions.Add(
                "File 5312529: Content Patcher 2.9.0 | https://example.invalid/content-patcher.zip ~~ channel=Release");
            details.SelectedDownloadOption = details.DownloadOptions[0];
            Assert.IsTrue(details.CanOpenSelectedDownloadOptionInBrowser);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldRecoverNexusFileIdFromFileName()
    {
        using var document = JsonDocument.Parse(
            "{\"source\":{\"platform\":\"NexusMods\",\"projectId\":\"29868\",\"fileName\":\"File 7448774_ Content Patcher 2.9.0 2.9.0.zip\"}}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        var parsed = (bool)parser.Invoke(null, arguments)!;
        Assert.IsTrue(parsed);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("29868", descriptor!.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("7448774", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldRecoverNexusFileIdFromLegacyFileField()
    {
        using var document = JsonDocument.Parse(
            "{\"source\":{\"platform\":\"NexusMods\",\"projectId\":\"29868\",\"file\":\"File 7448774_ Content Patcher 2.9.0.zip\"}}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        Assert.IsTrue((bool)parser!.Invoke(null, arguments)!);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("NexusMods", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("29868", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("7448774", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void LocalSourceMetadata_ShouldTreatLogicalFilenameAsFileNameAndFileIdHint()
    {
        var parser = typeof(VersionSettingsPageViewModel).GetMethod(
            "TryReadSvlSourceMetadata",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var root = Path.Combine(
            Path.GetTempPath(),
            "svl-logical-source-metadata-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "svl-source.json"),
                "{\"platform\":\"NexusMods\",\"projectId\":\"29868\",\"logicalFilename\":\"File 7448774_ Content Patcher 2.9.0.zip\"}");

            var metadata = parser!.Invoke(null, [root]);
            Assert.IsNotNull(metadata);
            Assert.AreEqual(
                "File 7448774_ Content Patcher 2.9.0.zip",
                metadata!.GetType().GetProperty("FileName")?.GetValue(metadata));
            Assert.AreEqual(
                "7448774",
                metadata.GetType().GetProperty("FileId")?.GetValue(metadata));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldRecoverCurseforgeIdsFromLegacyFileToken()
    {
        using var document = JsonDocument.Parse(
            "{\"name\":\"Content Patcher\",\"file\":\"cf-1012214-5312529.zip\"}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        Assert.IsTrue((bool)parser!.Invoke(null, arguments)!);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("Curseforge", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("1012214", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("5312529", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldRecoverNexusIdsFromPageUrlWithEmptyFields()
    {
        using var document = JsonDocument.Parse(
            "{\"source\":{\"platform\":\"NexusMods\",\"projectId\":\"\",\"fileId\":\"\",\"downloadUrl\":\"https://www.nexusmods.com/stardewvalley/mods/29868?tab=files&file_id=7448774&nmm=1\"}}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        Assert.IsTrue((bool)parser!.Invoke(null, arguments)!);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("NexusMods", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("29868", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("7448774", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldInferNexusPlatformBeforeDirectDownloadBranch()
    {
        using var document = JsonDocument.Parse(
            "{\"projectId\":\"29868\",\"fileId\":\"7448774\",\"downloadUrl\":\"https://example.invalid/files/content-patcher.zip\"}");
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        Assert.IsTrue((bool)parser!.Invoke(null, arguments)!);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("NexusMods", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("29868", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("7448774", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldRecoverCurseforgeFileIdFromCdnUrl()
    {
        var parser = typeof(ModpackInstallService).GetMethod(
            "TryParseCurseforgeFileIdFromCdnUrl",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[]
        {
            "https://edge.forgecdn.net/files/5312/529/ContentPatcher-2.9.0.zip",
            0L
        };
        var parsed = (bool)parser.Invoke(null, arguments)!;
        Assert.IsTrue(parsed);
        Assert.AreEqual(5312529L, arguments[1]);
    }

    [TestMethod]
    public void ModpackSourceParser_ShouldPersistCdnFileIdWhenSourceEntryOmitsIt()
    {
        using var document = JsonDocument.Parse("""
            {
              "name": "Content Patcher",
              "source": {
                "platform": "Curseforge",
                "projectId": "1012214",
                "downloadUrl": "https://edge.forgecdn.net/files/5312/529/ContentPatcher-2.9.0.zip"
              }
            }
            """);

        var parser = typeof(ModpackInstallService).GetMethod(
            "TryGetModSourceDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var arguments = new object[] { document.RootElement, null! };
        Assert.IsTrue((bool)parser!.Invoke(null, arguments)!);

        var descriptor = arguments[1];
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("Curseforge", descriptor!.GetType().GetProperty("Platform")?.GetValue(descriptor));
        Assert.AreEqual("1012214", descriptor.GetType().GetProperty("ProjectId")?.GetValue(descriptor));
        Assert.AreEqual("5312529", descriptor.GetType().GetProperty("FileId")?.GetValue(descriptor));
    }

    [TestMethod]
    public void ExportSourceMetadata_ShouldRecoverFullCurseforgeCdnFileId()
    {
        var parser = typeof(VersionSettingsPageViewModel).GetMethod(
            "TryExtractSourceFileIdFromLocalMetadata",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var fileId = parser!.Invoke(
            null,
            [
                "Curseforge",
                "1012214",
                new[] { "https://edge.forgecdn.net/files/5312/529/ContentPatcher-2.9.0.zip" }
            ]);

        Assert.AreEqual("5312529", fileId);
    }

    [TestMethod]
    public void ExportSourceFileId_ShouldRecoverFromNexusCacheWhenLocalMetadataLacksFileId()
    {
        var parser = typeof(VersionSettingsPageViewModel).GetMethod(
            "TryExtractSourceFileIdFromLocalMetadata",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var root = Path.Combine(Path.GetTempPath(), "svl-export-cache-file-id-test-" + Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source.zip");
        var modId = 930_000_000L + Random.Shared.Next(1_000_000);
        var fileId = 930_000_000L + Random.Shared.Next(1_000_000);
        var cachePath = NexusDownloadCache.GetCachePath(modId, fileId);

        try
        {
            Directory.CreateDirectory(root);
            using (var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create))
            {
                WriteArchiveText(archive, "Actual Mod/manifest.json", "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Actual.Mod\",\"Version\":\"1.0.0\"}");
                WriteArchiveText(archive, "content.txt", "cached nexus file");
            }

            NexusDownloadCache.Save(modId, fileId, sourcePath);

            var result = parser!.Invoke(null, [
                "NexusMods",
                modId.ToString(),
                new string[] { "", null, "Content Patcher" }
            ]) as string;

            Assert.AreEqual(fileId.ToString(), result);
        }
        finally
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void CollectionSourceConverter_ShouldAcceptLegacyStringAndUrlForms()
    {
        var sourceType = typeof(CollectionInstallService).Assembly.GetType(
            "SVL.Avalonia.Services.NexusCollectionJsonModSource");
        Assert.IsNotNull(sourceType);
        var optionsField = typeof(CollectionInstallService).GetField(
            "CollectionJsonOptions",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(optionsField);
        var options = (JsonSerializerOptions)optionsField!.GetValue(null)!;

        var source = JsonSerializer.Deserialize(
            "\"cf-1012214-5312529\"",
            sourceType!,
            options);
        Assert.IsNotNull(source);
        Assert.AreEqual("Curseforge", sourceType!.GetProperty("Type")?.GetValue(source));
        Assert.AreEqual(1012214L, sourceType.GetProperty("ModId")?.GetValue(source));
        Assert.AreEqual(5312529L, sourceType.GetProperty("FileId")?.GetValue(source));

        var nexusSource = JsonSerializer.Deserialize(
            "{\"url\":\"https://www.nexusmods.com/stardewvalley/mods/29868?tab=files&file_id=7448774\",\"projectId\":\"29868\"}",
            sourceType,
            options);
        Assert.IsNotNull(nexusSource);
        Assert.AreEqual("NexusMods", sourceType.GetProperty("Type")?.GetValue(nexusSource));
        Assert.AreEqual(29868L, sourceType.GetProperty("ModId")?.GetValue(nexusSource));
        Assert.AreEqual(7448774L, sourceType.GetProperty("FileId")?.GetValue(nexusSource));

        var nexusPageWithLogicalFilename = JsonSerializer.Deserialize(
            "{\"url\":\"https://www.nexusmods.com/stardewvalley/mods/29868?tab=files&nmm=1\",\"projectId\":29868,\"logicalFilename\":\"File 7448774_ Content Patcher 2.9.0.zip\"}",
            sourceType,
            options);
        Assert.IsNotNull(nexusPageWithLogicalFilename);
        Assert.AreEqual("NexusMods", sourceType.GetProperty("Type")?.GetValue(nexusPageWithLogicalFilename));
        Assert.AreEqual(29868L, sourceType.GetProperty("ModId")?.GetValue(nexusPageWithLogicalFilename));
        Assert.AreEqual(7448774L, sourceType.GetProperty("FileId")?.GetValue(nexusPageWithLogicalFilename));

        var curseforgeAliases = JsonSerializer.Deserialize(
            "{\"site\":\"CurseForge\",\"project\":1012214,\"file\":5312529}",
            sourceType,
            options);
        Assert.IsNotNull(curseforgeAliases);
        Assert.AreEqual("CurseForge", sourceType.GetProperty("Type")?.GetValue(curseforgeAliases));
        Assert.AreEqual(1012214L, sourceType.GetProperty("ModId")?.GetValue(curseforgeAliases));
        Assert.AreEqual(5312529L, sourceType.GetProperty("FileId")?.GetValue(curseforgeAliases));

        var emptyOuterFields = JsonSerializer.Deserialize(
            "{\"type\":\"\",\"url\":\"\",\"source\":{\"type\":\"Curseforge\",\"url\":\"https://edge.forgecdn.net/files/5312/529/ContentPatcher.zip\"}}",
            sourceType,
            options);
        Assert.IsNotNull(emptyOuterFields);
        Assert.AreEqual("Curseforge", sourceType.GetProperty("Type")?.GetValue(emptyOuterFields));
        Assert.AreEqual(
            "https://edge.forgecdn.net/files/5312/529/ContentPatcher.zip",
            sourceType.GetProperty("Url")?.GetValue(emptyOuterFields));
    }

    [TestMethod]
    public void CollectionModConverter_ShouldNormalizeTopLevelSourceFields()
    {
        var assembly = typeof(CollectionInstallService).Assembly;
        var modType = assembly.GetType("SVL.Avalonia.Services.NexusCollectionJsonMod");
        Assert.IsNotNull(modType);

        var optionsField = typeof(CollectionInstallService).GetField(
            "CollectionJsonOptions",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(optionsField);
        var options = (JsonSerializerOptions)optionsField!.GetValue(null)!;

        var mod = JsonSerializer.Deserialize(
            "{\"name\":\"Content Patcher\",\"version\":\"2.9.0\",\"type\":\"nexus\",\"modId\":1915,\"fileId\":7448774}",
            modType!,
            options);
        Assert.IsNotNull(mod);

        var source = modType!.GetProperty("Source")?.GetValue(mod);
        Assert.IsNotNull(source);
        var sourceType = source!.GetType();
        Assert.AreEqual("nexus", sourceType.GetProperty("Type")?.GetValue(source));
        Assert.AreEqual(1915L, sourceType.GetProperty("ModId")?.GetValue(source));
        Assert.AreEqual(7448774L, sourceType.GetProperty("FileId")?.GetValue(source));

        var mixed = JsonSerializer.Deserialize(
            "{\"name\":\"Generic Mod Config Menu\",\"source\":{\"type\":\"nexus\",\"modId\":5098},\"fileId\":123456}",
            modType,
            options);
        Assert.IsNotNull(mixed);
        var mixedSource = modType.GetProperty("Source")?.GetValue(mixed);
        Assert.IsNotNull(mixedSource);
        Assert.AreEqual(5098L, sourceType.GetProperty("ModId")?.GetValue(mixedSource));
        Assert.AreEqual(123456L, sourceType.GetProperty("FileId")?.GetValue(mixedSource));
    }

    [TestMethod]
    public void ModpackInstall_SourceReuseSupportsNexusCredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-nexus-source-reuse-test-" + Guid.NewGuid().ToString("N"));
        var modsPath = Path.Combine(root, "Mods");
        var modDirectory = Path.Combine(modsPath, "Generic Mod");
        try
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(
                Path.Combine(modDirectory, "manifest.json"),
                "{\"Name\":\"Generic Mod\",\"UniqueID\":\"Test.GenericMod\",\"Version\":\"1.0.0\"}");
            File.WriteAllText(
                Path.Combine(modDirectory, "svl-source.json"),
                "{\"platform\":\"NexusMods\",\"projectId\":\"29868\",\"fileId\":\"7448774\"}");

            var finder = typeof(ModpackInstallService).GetMethod(
                "FindInstalledModDirectoriesBySource",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(finder);

            var result = finder.Invoke(
                null,
                [modsPath, "NexusMods", 29868L, 7448774L])
                as IReadOnlyList<string>;

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(modDirectory, result[0]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldNotReuseSourceOnlyDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "svl-source-only-directory-test-" + Guid.NewGuid().ToString("N"));
        var modsPath = Path.Combine(root, "Mods");
        var modDirectory = Path.Combine(modsPath, "ContentPatcher");

        try
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(
                Path.Combine(modDirectory, "svl-source.json"),
                "{\"platform\":\"NexusMods\",\"projectId\":\"1915\",\"fileId\":\"7448774\"}");

            var finder = typeof(ModpackInstallService).GetMethod(
                "FindInstalledModDirectoriesBySource",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(finder);

            var result = finder!.Invoke(
                null,
                [modsPath, "NexusMods", 1915L, 7448774L]) as IReadOnlyList<string>;

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldPreserveNxmDownloadCredentials()
    {
        var parser = new NxmLinkParser();
        var ok = parser.TryParse(
            "nxm://stardewvalley/mods/29868/files/7448774?key=test-key&expires=1910000000&user_id=123",
            out var parsed,
            out var error);
        Assert.IsTrue(ok, error);

        var builder = typeof(ModpackInstallService).GetMethod(
            "BuildNexusDownloadInfo",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(builder);

        var result = builder!.Invoke(null, [29868L, 7448774L, parsed]) as NxmLinkInfo;
        Assert.IsNotNull(result);
        Assert.AreEqual("test-key", result!.Key);
        Assert.AreEqual(1910000000L, result.Expires);
        Assert.AreEqual(123L, result.UserId);
    }

    [TestMethod]
    public void SmapiResolveLock_ShouldBeSharedByModpackAndCollection()
    {
        var lockFactory = typeof(ModpackInstallService).GetMethod(
            "GetSmapiResolveLock",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(lockFactory);

        var modpackLock = lockFactory!.Invoke(
            null,
            [Path.Combine("C:\\svl-lock-test", "game-a"), "SMAPI 4.5.2"]) as SemaphoreSlim;
        var collectionLock = lockFactory.Invoke(
            null,
            [Path.Combine("C:\\svl-lock-test", "game-b"), "4.5.2"]) as SemaphoreSlim;

        Assert.IsNotNull(modpackLock);
        Assert.IsNotNull(collectionLock);
        Assert.AreSame(modpackLock, collectionLock);
    }

    [TestMethod]
    public void SmapiGithubAssets_ShouldPreferSingleInstallerZip()
    {
        using var document = JsonDocument.Parse(
            "[{\"name\":\"SMAPI-4.5.2-installer-double-zipped.zip\",\"browser_download_url\":\"https://example.test/double.zip\"}," +
            "{\"name\":\"SMAPI-4.5.2-installer.zip\",\"browser_download_url\":\"https://example.test/installer.zip\"}]");
        var selector = typeof(RemoteCatalogService).GetMethod(
            "SelectSmapiInstallerDownloadUrl",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(selector);

        var result = selector!.Invoke(null, [document.RootElement]) as string;
        Assert.AreEqual("https://example.test/installer.zip", result);
    }

    [TestMethod]
    public void SmapiGithubAssets_ShouldAcceptCaseInsensitiveReleaseFields()
    {
        using var document = JsonDocument.Parse(
            "[{\"Name\":\"SMAPI-4.5.2-installer-double-zipped.zip\",\"Browser_Download_URL\":\"https://example.test/double.zip\"}," +
            "{\"NAME\":\"SMAPI-4.5.2-installer.zip\",\"BROWSER_DOWNLOAD_URL\":\"https://example.test/installer.zip\"}]");
        var selector = typeof(RemoteCatalogService).GetMethod(
            "SelectSmapiInstallerDownloadUrl",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(selector);

        var result = selector!.Invoke(null, [document.RootElement]) as string;
        Assert.AreEqual("https://example.test/installer.zip", result);
    }

    [TestMethod]
    public async Task SmapiCatalog_ShouldFallbackToLatestWhenReleaseListIsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-smapi-latest-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new FixtureHttpMessageHandler(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/releases", StringComparison.OrdinalIgnoreCase))
                {
                    // 模拟 GitHub 发布列表被代理截断为空，但 latest 端点仍可用。
                    return JsonResponse("[]");
                }

                if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
                {
                    // 使用不同大小写字段，覆盖 GitHub/代理返回字段大小写变化。
                    return JsonResponse(
                        "{\"TAG_NAME\":\"v4.5.2\",\"NAME\":\"SMAPI 4.5.2\",\"PUBLISHED_AT\":\"2024-01-01T00:00:00Z\",\"ASSETS\":[" +
                        "{\"NAME\":\"SMAPI-4.5.2-installer.zip\",\"BROWSER_DOWNLOAD_URL\":\"https://example.test/smapi.zip\"}]}" );
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            });
            using var client = new HttpClient(handler);
            var service = new RemoteCatalogService(new AppUserSettingsStore(root), client);

            var entries = await service.GetSmapiVersionEntriesAsync(page: 1, perPage: 10);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("4.5.2", entries[0].Version);
            Assert.AreEqual("GitHub", entries[0].Source);
            Assert.AreEqual("https://example.test/smapi.zip", entries[0].DownloadUrl);
            CollectionAssert.Contains(
                handler.Requests.Select(request => request.AbsolutePath).ToList(),
                "/repos/Pathoschild/SMAPI/releases/latest");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task SmapiCatalog_ShouldHonorCancellationBeforeNetworkFallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-smapi-cancel-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new RemoteCatalogService(new AppUserSettingsStore(root));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => catalog.GetSmapiVersionEntriesAsync(cancellationToken: cancellation.Token));
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => catalog.GetLatestSmapiVersionEntryAsync(cancellation.Token));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void SmapiDownload_ShouldUnwrapDoubleZippedInstaller()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-smapi-double-zip-test-" + Guid.NewGuid().ToString("N"));
        var innerPath = Path.Combine(root, "inner.zip");
        var outerPath = Path.Combine(root, "outer.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var inner = ZipFile.Open(innerPath, ZipArchiveMode.Create))
            {
                WriteArchiveText(inner, "installer/internal/windows/install.dat", "smapi");
            }

            using (var outer = ZipFile.Open(outerPath, ZipArchiveMode.Create))
            using (var source = File.OpenRead(innerPath))
            using (var target = outer.CreateEntry("SMAPI-4.5.2-installer.zip").Open())
            {
                source.CopyTo(target);
            }

            var unwrap = typeof(ModpackInstallService).GetMethod(
                "TryUnwrapDoubleZipped",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(unwrap);
            unwrap!.Invoke(null, [outerPath]);

            using var result = ZipFile.OpenRead(outerPath);
            Assert.IsNotNull(result.GetEntry("installer/internal/windows/install.dat"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void SmapiCacheNormalizer_ShouldUnwrapDoubleZippedNexusCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-smapi-cache-double-zip-test-" + Guid.NewGuid().ToString("N"));
        var innerPath = Path.Combine(root, "inner.zip");
        var outerPath = Path.Combine(root, "2400_7448774.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var inner = ZipFile.Open(innerPath, ZipArchiveMode.Create))
            {
                WriteArchiveText(
                    inner,
                    "SMAPI 4.5.2 installer/internal/windows/install.dat",
                    "smapi");
            }

            using (var outer = ZipFile.Open(outerPath, ZipArchiveMode.Create))
            using (var source = File.OpenRead(innerPath))
            using (var target = outer.CreateEntry("SMAPI-4.5.2-installer.zip").Open())
            {
                source.CopyTo(target);
            }

            var normalizer = typeof(ModpackInstallService).GetMethod(
                "TryNormalizeSmapiArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(normalizer);

            Assert.IsTrue((bool)normalizer!.Invoke(null, [outerPath])!);
            using var normalized = ZipFile.OpenRead(outerPath);
            Assert.IsNotNull(normalized.GetEntry("SMAPI 4.5.2 installer/internal/windows/install.dat"));

            var inspectorType = typeof(ModpackInstallService).Assembly.GetType(
                "SVL.Avalonia.Services.SmapiPackageVersionInspector");
            var readVersion = inspectorType?.GetMethod(
                "TryReadVersion",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(readVersion);
            Assert.AreEqual("4.5.2", readVersion!.Invoke(null, [outerPath]));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_FlattensNestedSevenZipModDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-7z-mod-flatten-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "cf-1012214-5312529.7z");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            var manifestPath = Path.Combine(root, "manifest.json");
            var contentPath = Path.Combine(root, "content.json");
            File.WriteAllText(manifestPath, "{\"Name\":\"Content Patcher\",\"UniqueID\":\"Pathoschild.ContentPatcher\"}");
            File.WriteAllText(contentPath, "{}");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var manifest = File.OpenRead(manifestPath))
            using (var content = File.OpenRead(contentPath))
            {
                writer.Write("Release/ActualMod/manifest.json", manifest, null);
                writer.Write("Release/ActualMod/content.json", content, null);
            }

            var installer = typeof(ModpackInstallService).GetMethod(
                "InstallDownloadedModArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(installer);

            var arguments = new object[] { archivePath, modsPath, "cf-1012214-5312529", null };
            var success = (bool)installer.Invoke(null, arguments)!;
            var installedNames = arguments[3] as List<string>;

            Assert.IsTrue(success);
            Assert.IsNotNull(installedNames);
            Assert.AreEqual("ActualMod", installedNames.Single());
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ActualMod", "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ActualMod", "content.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "ActualMod", "Release")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "cf-1012214-5312529")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_UsesManifestNameForGeneratedCurseforgeDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-generated-mod-directory-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "cf-1012214-5312529.zip");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteArchiveText(
                    archive,
                    "cf-1012214-5312529/manifest.json",
                    "{\"Name\":\"Content Patcher\",\"UniqueID\":\"Pathoschild.ContentPatcher\"}");
                WriteArchiveText(archive, "cf-1012214-5312529/content.json", "{}");
            }

            var installer = typeof(ModpackInstallService).GetMethod(
                "InstallDownloadedModArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(installer);

            var arguments = new object[] { archivePath, modsPath, "cf-1012214-5312529", null };
            var success = (bool)installer.Invoke(null, arguments)!;
            var installedNames = arguments[3] as List<string>;

            Assert.IsTrue(success);
            Assert.IsNotNull(installedNames);
            Assert.AreEqual("Content Patcher", installedNames.Single());
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "Content Patcher", "manifest.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "cf-1012214-5312529")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ArchiveExtractor_DetectsSevenZipBySignatureWithoutExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-7z-signature-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "cdn-download");
        var extractPath = Path.Combine(root, "extract");
        var manifestPath = Path.Combine(root, "manifest.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                manifestPath,
                "{\"Name\":\"Signature Mod\",\"UniqueID\":\"Example.Signature\"}");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var manifest = File.OpenRead(manifestPath))
            {
                writer.Write("SignatureMod/manifest.json", manifest, null);
            }

            Assert.IsTrue(ArchiveExtractor.IsSevenZip(archivePath));
            ArchiveExtractor.ExtractSevenZipToDirectory(archivePath, extractPath);
            Assert.IsTrue(File.Exists(Path.Combine(extractPath, "SignatureMod", "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ArchiveExtractor_DetectsZipBySignatureWithoutExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-zip-signature-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "cdn-download");
        var extractPath = Path.Combine(root, "extract");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("ContentPatcher/manifest.json").Open()))
            {
                writer.Write("{\"Name\":\"Signature ZIP Mod\",\"UniqueID\":\"Example.SignatureZip\"}");
            }

            Assert.IsTrue(ArchiveExtractor.IsZip(archivePath));
            Assert.IsTrue(ModpackTypeDetector.IsSupportedFile(archivePath));
            using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
            {
                Directory.CreateDirectory(extractPath);
                foreach (var entry in archive.Entries)
                {
                    var target = Path.Combine(extractPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var source = entry.Open();
                    using var destination = File.Create(target);
                    source.CopyTo(destination);
                }
            }

            Assert.IsTrue(File.Exists(Path.Combine(extractPath, "ContentPatcher", "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldRejectCorruptedManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-corrupt-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "corrupt.zip");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("Release/ActualMod/manifest.json").Open());
                writer.Write("{not valid json");
            }

            var installer = typeof(ModpackInstallService).GetMethod(
                "InstallDownloadedModArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(installer);

            var arguments = new object[] { archivePath, modsPath, "ActualMod", null };
            var success = (bool)installer.Invoke(null, arguments)!;

            Assert.IsFalse(success);
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "ActualMod")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldIgnoreNonModOuterManifestAndInstallInnerMod()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-invalid-outer-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "wrapped-mod.zip");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteArchiveText(archive, "Release/manifest.json", "{\"formatVersion\":1,\"files\":[]}");
                WriteArchiveText(
                    archive,
                    "Release/ActualMod/manifest.json",
                    "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Example.Actual\",\"Version\":\"1.0.0\"}");
                WriteArchiveText(archive, "Release/ActualMod/content.json", "{}");
            }

            var installer = typeof(ModpackInstallService).GetMethod(
                "InstallDownloadedModArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(installer);

            var arguments = new object[] { archivePath, modsPath, "wrapped-mod", null };
            var success = (bool)installer.Invoke(null, arguments)!;
            var installedNames = arguments[3] as List<string>;

            Assert.IsTrue(success);
            Assert.IsNotNull(installedNames);
            CollectionAssert.AreEqual(new[] { "ActualMod" }, installedNames);
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ActualMod", "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ActualMod", "content.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "Release")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldIgnoreNameOnlyOuterManifestAndInstallInnerMod()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-name-only-outer-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "wrapped-mod.zip");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                // 一些 Nexus/CurseForge 发布包的外层清单只有 Name/Version，
                // 没有 formatVersion/files 等明显的整合包字段。
                WriteArchiveText(archive, "Release/manifest.json", "{\"Name\":\"Release Wrapper\",\"Version\":\"1.0.0\"}");
                WriteArchiveText(
                    archive,
                    "Release/ActualMod/manifest.json",
                    "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Example.Actual\",\"Version\":\"1.0.0\"}");
                WriteArchiveText(archive, "Release/ActualMod/content.json", "{}");
            }

            var installer = typeof(ModpackInstallService).GetMethod(
                "InstallDownloadedModArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(installer);

            var arguments = new object[] { archivePath, modsPath, "wrapped-mod", null };
            var success = (bool)installer.Invoke(null, arguments)!;
            var installedNames = arguments[3] as List<string>;

            Assert.IsTrue(success);
            Assert.IsNotNull(installedNames);
            CollectionAssert.AreEqual(new[] { "ActualMod" }, installedNames);
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ActualMod", "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ActualMod", "content.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "Release")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstall_ShouldAcceptManifestWithCommentsAndTrailingComma()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-compatible-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "content-patcher.zip");
        var modsPath = Path.Combine(root, "Mods");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry("Release/ContentPatcher/manifest.json").Open(),
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write("{\n  // SMAPI manifest files in older packages may contain comments.\n  \"Name\": \"Content Patcher\",\n  \"UniqueID\": \"Pathoschild.ContentPatcher\",\n}");
            }

            var installer = typeof(ModpackInstallService).GetMethod(
                "InstallDownloadedModArchive",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(installer);

            var arguments = new object[] { archivePath, modsPath, "cf-1012214-5312529", null };
            var success = (bool)installer.Invoke(null, arguments)!;
            var installedNames = arguments[3] as List<string>;

            Assert.IsTrue(success);
            Assert.IsNotNull(installedNames);
            Assert.AreEqual("ContentPatcher", installedNames.Single());
            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "ContentPatcher", "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void VersionSettingsManifestReader_ShouldFallbackWhenVersionIsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-manifest-version-fallback-" + Guid.NewGuid().ToString("N"));
        var modDirectory = Path.Combine(root, "Content Patcher 2.9.0");
        try
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(
                Path.Combine(modDirectory, "manifest.json"),
                "{\"Name\":\"Content Patcher\",\"Version\":\"\"}");

            var reader = typeof(ModDetailsPageViewModel).GetMethod(
                "TryReadManifestVersion",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(reader);

            var version = reader.Invoke(null, [modDirectory]) as string;
            Assert.AreEqual("2.9.0", version);

            File.WriteAllText(
                Path.Combine(modDirectory, "manifest.json"),
                "{\"Name\":\"Content Patcher\",\"VersionString\":\"3.0.1-beta\"}");
            version = reader.Invoke(null, [modDirectory]) as string;
            Assert.AreEqual("3.0.1-beta", version);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModManifestDiscovery_ShouldIgnoreOuterPackManifestAndUseNestedModManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-nested-manifest-discovery-" + Guid.NewGuid().ToString("N"));
        var wrapperDirectory = Path.Combine(root, "Wrapper");
        var modDirectory = Path.Combine(wrapperDirectory, "Actual Mod");
        try
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(
                Path.Combine(wrapperDirectory, "manifest.json"),
                "{\"name\":\"Exported Pack\",\"version\":\"1.0.0\"}");
            File.WriteAllText(
                Path.Combine(modDirectory, "manifest.json"),
                "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Example.ActualMod\",\"Version\":\"1.2.3\"}");

            var findManifest = typeof(VersionSettingsPageViewModel).GetMethod(
                "FindManifestPath",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(findManifest);
            Assert.IsNull(findManifest.Invoke(null, [wrapperDirectory]));
            Assert.AreEqual(
                Path.Combine(modDirectory, "manifest.json"),
                findManifest.Invoke(null, [modDirectory]));

            var enumerateVersionSettings = typeof(VersionSettingsPageViewModel).GetMethod(
                "EnumerateCandidateModDirectories",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(enumerateVersionSettings);
            var versionSettingsCandidates = ((IEnumerable<string>)enumerateVersionSettings.Invoke(null, [root])!).ToList();
            CollectionAssert.Contains(versionSettingsCandidates, Path.GetFullPath(modDirectory));
            CollectionAssert.DoesNotContain(versionSettingsCandidates, Path.GetFullPath(wrapperDirectory));

            var enumerateDetails = typeof(ModDetailsPageViewModel).GetMethod(
                "EnumerateCandidateModDirectories",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(enumerateDetails);
            var detailsCandidates = ((IEnumerable<string>)enumerateDetails.Invoke(null, [root])!).ToList();
            CollectionAssert.Contains(detailsCandidates, Path.GetFullPath(modDirectory));
            CollectionAssert.DoesNotContain(detailsCandidates, Path.GetFullPath(wrapperDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModManifestDiscovery_ShouldRejectPackageShapedOuterManifestWithVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-package-shaped-manifest-discovery-" + Guid.NewGuid().ToString("N"));
        var wrapperDirectory = Path.Combine(root, "Release");
        var modDirectory = Path.Combine(wrapperDirectory, "Actual Mod");
        try
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(
                Path.Combine(wrapperDirectory, "manifest.json"),
                "{\"name\":\"Exported Pack\",\"version\":\"1.0.0\",\"formatVersion\":1}");
            File.WriteAllText(
                Path.Combine(modDirectory, "manifest.json"),
                "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Example.ActualMod\",\"Version\":\"1.2.3\"}");

            foreach (var viewModelType in new[]
                     {
                         typeof(VersionSettingsPageViewModel),
                         typeof(ModDetailsPageViewModel)
                     })
            {
                var findManifest = viewModelType.GetMethod(
                    "FindManifestPath",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(findManifest);
                Assert.IsNull(findManifest.Invoke(null, [wrapperDirectory]));

                var enumerateCandidates = viewModelType.GetMethod(
                    "EnumerateCandidateModDirectories",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(enumerateCandidates);
                var candidates = ((IEnumerable<string>)enumerateCandidates.Invoke(null, [root])!).ToList();
                CollectionAssert.Contains(candidates, Path.GetFullPath(modDirectory));
                CollectionAssert.DoesNotContain(candidates, Path.GetFullPath(wrapperDirectory));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackInstallProgress_ShouldKeepFailedDownloadStageBelowFull()
    {
        var calculate = typeof(ModpackInstallService).GetMethod(
            "CalculateFinalSubProgress",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.IsNotNull(calculate);

        Assert.AreEqual(99, calculate.Invoke(null, [2, 1]));
        Assert.AreEqual(100, calculate.Invoke(null, [2, 0]));
        Assert.AreEqual(-1, calculate.Invoke(null, [0, 1]));
    }

    [TestMethod]
    public void ExportSourceCredential_ShouldReadNumericProjectAndFileIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-numeric-source-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "svl-source.json"),
                "{\"platform\":\"Curseforge\",\"projectId\":1012214,\"fileId\":5312529,\"fileName\":\"Content Patcher.zip\"}");

            var reader = typeof(VersionSettingsPageViewModel).GetMethod(
                "TryReadSvlSourceMetadata",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(reader);
            var metadata = reader.Invoke(null, [root]);
            Assert.IsNotNull(metadata);

            var metadataType = metadata.GetType();
            Assert.AreEqual("Curseforge", metadataType.GetProperty("Platform")?.GetValue(metadata));
            Assert.AreEqual("1012214", metadataType.GetProperty("ProjectId")?.GetValue(metadata));
            Assert.AreEqual("5312529", metadataType.GetProperty("FileId")?.GetValue(metadata));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_CleansNestedSevenZipPackageWithOuterTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-nested-7z-detection-test-" + Guid.NewGuid().ToString("N"));
        var innerArchivePath = Path.Combine(root, "modpack.7z");
        var outerArchivePath = Path.Combine(root, "launcher-package.zip");
        try
        {
            Directory.CreateDirectory(root);
            var manifestPath = Path.Combine(root, "modpack.json");
            var iconPath = Path.Combine(root, "icon.png");
            File.WriteAllText(manifestPath, "{\"name\":\"嵌套 7z 整合包\",\"mods\":[]}");
            File.WriteAllBytes(iconPath, [1, 2, 3, 4]);

            using (var writer = SevenZipWriter.OpenWriter(
                       innerArchivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            using (var manifest = File.OpenRead(manifestPath))
            using (var icon = File.OpenRead(iconPath))
            {
                writer.Write("modpack.json", manifest, null);
                writer.Write("icon.png", icon, null);
            }

            using (var outerArchive = System.IO.Compression.ZipFile.Open(
                       outerArchivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            using (var inner = File.OpenRead(innerArchivePath))
            using (var output = outerArchive.CreateEntry("modpack.7z").Open())
            {
                inner.CopyTo(output);
            }

            var detection = ModpackTypeDetector.Detect(outerArchivePath);
            try
            {
                Assert.AreEqual(ModpackType.SVL, detection.Type);
                Assert.AreEqual("嵌套 7z 整合包", detection.ModpackName);
                Assert.IsTrue(File.Exists(detection.ModpackIconPath));
                Assert.IsTrue(detection.ModpackIconPath.StartsWith(
                    detection.TempExtractPath,
                    StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }

            Assert.IsFalse(Directory.Exists(detection.TempExtractPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task SvlModpackInstall_ShouldInstallSevenZipPackageAndFlattenBundledMod()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-7z-install-test-" + Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(root, "game");
        var archivePath = Path.Combine(root, "pack.7z");
        const string instanceName = "7z Import";
        var registry = new InstanceRegistryStore();

        try
        {
            Directory.CreateDirectory(Path.Combine(gamePath, "versions", "Existing SMAPI 4.5.2"));
            File.WriteAllText(
                Path.Combine(gamePath, "versions", "Existing SMAPI 4.5.2", "StardewModdingAPI.dll"),
                "existing-smapi");

            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            {
                // 模拟真实导出包常见的外层目录 + 多层发行包目录布局。
                WriteSevenZipText(
                    writer,
                    "SVL Pack/modpack.json",
                    "{\"name\":\"7z Pack\",\"version\":\"1.0.0\",\"smapi_version\":\"4.5.2\",\"mods\":[]}");
                WriteSevenZipText(
                    writer,
                    "SVL Pack/mods/Release/ActualMod/manifest.json",
                    "{\"Name\":\"7z Bundled Mod\",\"UniqueID\":\"SVL.SevenZipMod\",\"Version\":\"1.0.0\"}");
                WriteSevenZipText(writer, "SVL Pack/mods/Release/ActualMod/content.json", "{}");
            }

            var settingsStore = new AppUserSettingsStore(Path.Combine(root, "settings"));
            var service = new ModpackInstallService(
                new RoundTripGameInstallPathLocator(gamePath),
                new RoundTripSmapiInstallService(),
                new HttpDownloadService(settingsStore),
                new RemoteCatalogService(settingsStore),
                settingsStore,
                new NexusModDownloadResolverService(),
                new SVL.Core.Platform.Abstractions.NxmLinkParser());

            var result = await service.InstallSvlModpackAsync(
                archivePath,
                instanceName,
                gamePath,
                onProgress: null);

            var installedModPath = Path.Combine(
                gamePath,
                "versions",
                instanceName,
                "Mods",
                "ActualMod");
            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(1, result.InstalledMods.Count);
            Assert.AreEqual(0, result.FailedMods.Count);
            Assert.IsTrue(File.Exists(Path.Combine(installedModPath, "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(installedModPath, "content.json")));
            Assert.IsFalse(
                Directory.Exists(Path.Combine(
                    gamePath,
                    "versions",
                    instanceName,
                    "Mods",
                    "Release")),
                "7z 导入不应把外部发行包目录保留在 Mods 下");
        }
        finally
        {
            var records = registry.LoadManualInstances();
            records.RemoveAll(record =>
                string.Equals(record.Name, instanceName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(record.Path) &&
                record.Path.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase));
            registry.SaveManualInstances(records);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_RecognizesUtf16CurseforgeManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-utf16-cf-detection-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "pack.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("manifest.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, System.Text.Encoding.Unicode);
                writer.Write("{\"manifestVersion\":1,\"minecraft\":{\"version\":\"1.0\"},\"files\":[]}");
            }

            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.Curseforge, detection.Type);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_RecognizesCurseforgeManifestWithNullFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-null-files-cf-detection-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "overrides-only.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteArchiveText(
                    archive,
                    "manifest.json",
                    "{\"name\":\"仅 overrides 包\",\"version\":\"1.0.0\",\"manifestVersion\":1,\"files\":null,\"overrides\":\"overrides\"}");
            }

            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.Curseforge, detection.Type);
                Assert.AreEqual("仅 overrides 包", detection.ModpackName);
                Assert.AreEqual(0, detection.ModCount);
                Assert.IsNotNull(detection.CurseforgeManifest);
                Assert.AreEqual(0, detection.CurseforgeManifest!.Files.Count);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_ShouldSkipNonModOuterManifestAndUseInnerCurseforgeManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-inner-cf-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "wrapped-pack.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                // 这是合法 JSON，但属于外层发布工具的元数据，不是 CurseForge manifest。
                WriteArchiveText(archive, "Release/manifest.json", "{\"formatVersion\":1,\"files\":[]}");
                WriteArchiveText(
                    archive,
                    "Release/ActualPack/manifest.json",
                    "{\"name\":\"内层 CurseForge 包\",\"version\":\"1.0.0\",\"manifestVersion\":1,\"files\":[{\"projectID\":1012214,\"fileID\":5312529}]}");
            }

            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.Curseforge, detection.Type);
                Assert.AreEqual("内层 CurseForge 包", detection.ModpackName);
                Assert.IsNotNull(detection.CurseforgeManifest);
                Assert.AreEqual(1, detection.CurseforgeManifest!.Files.Count);
                Assert.AreEqual(5312529L, detection.CurseforgeManifest.Files[0].FileId);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_ShouldSkipNonCollectionOuterJsonAndUseInnerCollection()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-inner-collection-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "wrapped-collection.7z");
        try
        {
            Directory.CreateDirectory(root);
            using (var writer = SevenZipWriter.OpenWriter(
                       archivePath,
                       new SevenZipWriterOptions(CompressionType.LZMA2)))
            {
                WriteSevenZipText(writer, "Release/collection.json", "{\"formatVersion\":1,\"files\":[]}");
                WriteSevenZipText(
                    writer,
                    "Release/ActualCollection/collection.json",
                    "{\"info\":{\"name\":\"内层 Collection\"},\"mods\":[]}");
            }

            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.NexusCollection, detection.Type);
                Assert.AreEqual("内层 Collection", detection.ModpackName);
                Assert.AreEqual(0, detection.ModCount);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void CurseforgeManifestParser_ShouldAcceptStringManifestVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-string-cf-manifest-version-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "pack.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("manifest.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                writer.Write("{\"name\":\"字符串版本包\",\"version\":\"1.0\",\"manifestVersion\":\"1\",\"files\":[{\"projectID\":\"1012214\",\"fileID\":\"5312529\"}]}");
            }

            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.Curseforge, detection.Type);
                Assert.IsNotNull(detection.CurseforgeManifest);
                Assert.AreEqual(1, detection.CurseforgeManifest!.ManifestVersion);
                Assert.AreEqual(1, detection.CurseforgeManifest.Files.Count);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void SvlSourceParser_ShouldAcceptWrappedAndSingleSourceObjects()
    {
        var parser = typeof(ModpackInstallService).GetMethod(
            "ParseSourceEntries",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        using (var aliases = JsonDocument.Parse(
                   "{\"sources\":{\"Content Patcher\":{\"site\":\"CurseForge\",\"project\":1012214,\"file\":5312529}}}"))
        {
            var entries = (IReadOnlyList<JsonElement>)parser!.Invoke(null, [aliases.RootElement])!;
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Content Patcher", entries[0].GetProperty("name").GetString());
            Assert.AreEqual(5312529, entries[0].GetProperty("file").GetInt32());
        }

        using (var wrapped = JsonDocument.Parse(
                   "{\"mods\":[{\"name\":\"Content Patcher\",\"source\":{\"platform\":\"Curseforge\",\"projectId\":\"1\",\"fileId\":\"2\"}}]}"))
        {
            var entries = (IReadOnlyList<JsonElement>)parser!.Invoke(null, [wrapped.RootElement])!;
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Content Patcher", entries[0].GetProperty("name").GetString());
        }

        using (var single = JsonDocument.Parse(
                   "{\"platform\":\"NexusMods\",\"projectId\":\"10\",\"fileId\":\"20\"}"))
        {
            var entries = (IReadOnlyList<JsonElement>)parser!.Invoke(null, [single.RootElement])!;
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("NexusMods", entries[0].GetProperty("platform").GetString());
        }

        using (var map = JsonDocument.Parse(
                   "{\"sources\":{\"Content Patcher\":{\"platform\":\"Curseforge\",\"projectId\":\"1\",\"fileId\":\"2\"},\"Generic Mod\":\"https://example.test/generic.zip\"}}"))
        {
            var entries = (IReadOnlyList<JsonElement>)parser!.Invoke(null, [map.RootElement])!;
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("Content Patcher", entries[0].GetProperty("name").GetString());
            Assert.AreEqual("Generic Mod", entries[1].GetProperty("name").GetString());
            Assert.AreEqual("https://example.test/generic.zip", entries[1].GetProperty("source").GetString());
        }
    }

    [TestMethod]
    public void DeferredVersionDirectoryCleanup_ShouldOnlyRemoveLauncherTempDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-deferred-version-cleanup-test-" + Guid.NewGuid().ToString("N"));
        var versions = Path.Combine(root, "versions");
        var deferred = Path.Combine(versions, ".svl-delete-old-instance");
        var userDirectory = Path.Combine(versions, "keep-instance");
        try
        {
            Directory.CreateDirectory(deferred);
            Directory.CreateDirectory(userDirectory);
            File.WriteAllText(Path.Combine(deferred, "marker.txt"), "remove");
            File.WriteAllText(Path.Combine(userDirectory, "marker.txt"), "keep");

            var cleaned = DeferredVersionDirectoryCleanup.TryCleanup(root);

            Assert.AreEqual(1, cleaned);
            Assert.IsFalse(Directory.Exists(deferred));
            Assert.IsTrue(File.Exists(Path.Combine(userDirectory, "marker.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ModpackTypeDetector_ShouldReadCollectionVersionWhenGameVersionsIsString()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-string-collection-version-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "collection.zip");
        try
        {
            Directory.CreateDirectory(root);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("collection.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                writer.Write("{\"info\":{\"name\":\"字符串版本 Collection\",\"gameVersions\":\"1.6.15\"},\"mods\":[]}");
            }

            var detection = ModpackTypeDetector.Detect(archivePath);
            try
            {
                Assert.AreEqual(ModpackType.NexusCollection, detection.Type);
                Assert.AreEqual("字符串版本 Collection", detection.ModpackName);
                Assert.AreEqual("1.6.15", detection.ModpackVersion);
            }
            finally
            {
                ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void DownloadCatalogItem_ShouldKeepStructuredIdentityForDetails()
    {
        var parser = typeof(DownloadPageViewModel).GetMethod(
            "ParseCatalogItem",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(parser);

        var item = parser!.Invoke(
            null,
            ["[CurseforgePack#12345] 测试整合包 | slug=test-pack | metric=10"])
            as DownloadCatalogItem;

        Assert.IsNotNull(item);
        Assert.AreEqual(12345L, item!.Identity.ResourceId);
        Assert.AreEqual(CatalogSource.Curseforge, item.Identity.Source);
        Assert.IsTrue(item.Identity.IsModpack);
        Assert.AreEqual("test-pack", item.Identity.CollectionSlug);
    }

    [TestMethod]
    public void DownloadPageSteamCmdInput_ShouldBindToGeneratedCommand()
    {
        Assert.IsNotNull(typeof(DownloadPageViewModel).GetProperty("SendSteamCmdInputCommand"));
        Assert.IsNull(typeof(DownloadPageViewModel).GetProperty("SteamCmdInputCommand"));

        var workspace = new DirectoryInfo(AppContext.BaseDirectory);
        while (workspace != null &&
               !File.Exists(Path.Combine(workspace.FullName, "SVL.Avalonia", "SVL.Avalonia.csproj")))
        {
            workspace = workspace.Parent;
        }

        Assert.IsNotNull(workspace, "无法定位工作区根目录");
        var viewPath = Path.Combine(workspace!.FullName, "SVL.Avalonia", "Views", "DownloadPageView.axaml");
        Assert.IsTrue(File.Exists(viewPath), $"找不到视图文件: {viewPath}");
        var viewText = File.ReadAllText(viewPath);
        StringAssert.Contains(viewText, "Command=\"{Binding SendSteamCmdInputCommand}\"");
        Assert.IsFalse(viewText.Contains("Command=\"{Binding SteamCmdInputCommand}\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AvaloniaChromeAndContextMenu_ShouldUseStableLayoutAndThemeResources()
    {
        var workspace = new DirectoryInfo(AppContext.BaseDirectory);
        while (workspace != null &&
               !File.Exists(Path.Combine(workspace.FullName, "SVL.Avalonia", "SVL.Avalonia.csproj")))
        {
            workspace = workspace.Parent;
        }

        Assert.IsNotNull(workspace, "无法定位工作区根目录");
        var root = workspace!.FullName;
        var mainWindowText = File.ReadAllText(Path.Combine(root, "SVL.Avalonia", "MainWindow.axaml"));
        var themeText = File.ReadAllText(Path.Combine(root, "SVL.Avalonia", "Resources", "Theme.axaml"));
        var instancesViewText = File.ReadAllText(Path.Combine(root, "SVL.Avalonia", "Views", "InstancesPageView.axaml"));
        var instancesCodeText = File.ReadAllText(Path.Combine(root, "SVL.Avalonia", "Views", "InstancesPageView.axaml.cs"));

        StringAssert.Contains(mainWindowText, "UseLayoutRounding=\"True\"");
        StringAssert.Contains(mainWindowText, "Width=\"120\" Height=\"48\" RowDefinitions=\"48\" ColumnDefinitions=\"40,40,40\"");
        Assert.AreEqual(3, CountOccurrences(mainWindowText, "Classes=\"winCtrl"));
        // 窗口控制区使用统一尺寸的矢量画布，避免不同图形的透明边界
        // 导致最小化/最大化/关闭图形视觉中心不在同一条线上。
        Assert.AreEqual(3, CountOccurrences(mainWindowText, "Width=\"24\" Height=\"24\""));
        Assert.AreEqual(3, CountOccurrences(mainWindowText, "Grid.Row=\"0\" Grid.Column="));
        StringAssert.Contains(mainWindowText, "Data=\"M 4,12 L 20,12\"");
        StringAssert.Contains(mainWindowText, "Data=\"M 5,5 L 19,19 M 19,5 L 5,19\"");
        StringAssert.Contains(themeText, "<Style Selector=\"ContextMenu\">");
        StringAssert.Contains(themeText, "<Style Selector=\"MenuFlyoutPresenter\">");
        StringAssert.Contains(themeText, "<Style Selector=\"ContextMenu MenuItem:pointerover\">");
        StringAssert.Contains(instancesViewText, "PointerPressed=\"PathEntry_PointerPressed\"");
        StringAssert.Contains(instancesViewText, "Opening=\"PathContextMenu_Opening\"");
        StringAssert.Contains(instancesCodeText, "menu.Open(control)");
        StringAssert.Contains(instancesCodeText, "ResolvePathEntryFromMenuSender");
    }

    [TestMethod]
    public void ModpackSourceFailureRetry_ShouldRetryTransientFailuresOnly()
    {
        var method = typeof(ModpackInstallService).GetMethod(
            "ShouldRetryModSourceFailure",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        static bool Invoke(System.Reflection.MethodInfo method, string message) =>
            (bool)method.Invoke(null, [message])!;

        Assert.IsTrue(Invoke(method!, null!));
        Assert.IsTrue(Invoke(method!, "Nexus 文件下载或 Mod 解压校验失败"));
        Assert.IsTrue(Invoke(method!, "CurseForge 文件下载地址解析失败"));
        Assert.IsFalse(Invoke(method!, "缺少可用下载来源（请补充平台、项目 ID/FileID 或直链）"));
        Assert.IsFalse(Invoke(method!, "未收到有效的 Nexus NXM 文件回调"));
        Assert.IsFalse(Invoke(method!, "Nexus 下载地址刷新失败，未收到浏览器 NXM 回调"));
        Assert.IsFalse(Invoke(method!, "需要登录 Nexus 后才能下载"));
        Assert.IsFalse(Invoke(method!, "已取消"));
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    [TestMethod]
    public async Task ExportedSvlModpack_ShouldRoundTripThroughImport()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-export-roundtrip-test-" + Guid.NewGuid().ToString("N"));
        var sourceInstance = Path.Combine(root, "source-instance");
        var sourceMod = Path.Combine(sourceInstance, "Mods", "ActualMod");
        var outputPath = Path.Combine(root, "Round Trip Pack.zip");
        var modArchivePath = Path.Combine(root, "cached-mod.zip");
        var targetBase = Path.Combine(root, "target-base");
        const long projectId = 987654321;
        const long fileId = 123456789;
        var cachePath = NexusDownloadCache.GetCachePath(projectId, fileId);
        var registry = new InstanceRegistryStore();
        byte[] existingCache = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceMod, "config"));
            File.WriteAllText(
                Path.Combine(sourceMod, "manifest.json"),
                "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Example.ActualMod\",\"Version\":\"1.2.3\"}");
            File.WriteAllText(Path.Combine(sourceMod, "config", "config.json"), "{\"enabled\":true}");
            File.WriteAllBytes(Path.Combine(sourceInstance, ".svl-instance-icon-smapi.png"), [7, 8, 9, 10]);

            using (var archive = System.IO.Compression.ZipFile.Open(
                       modArchivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteArchiveText(
                    archive,
                    "Release/ActualMod/manifest.json",
                    "{\"Name\":\"Actual Mod\",\"UniqueID\":\"Example.ActualMod\",\"Version\":\"1.2.3\"}");
                WriteArchiveText(archive, "Release/ActualMod/content.json", "{}");
            }

            if (File.Exists(cachePath))
            {
                existingCache = File.ReadAllBytes(cachePath);
            }
            NexusDownloadCache.Save(projectId, fileId, modArchivePath);

            // 直接调用导出页的实际打包方法，避免把“导出包格式”另写一套测试实现。
            var viewModel = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(VersionSettingsPageViewModel));
            SetPrivateField(viewModel, "_modpackName", "Round Trip Pack");
            SetPrivateField(viewModel, "_modpackVersion", "1.0.0");
            SetPrivateField(viewModel, "_modpackAuthor", "SVL Test");
            SetPrivateField(viewModel, "_smapiVersionText", "4.5.2");
            SetPrivateField(viewModel, "_includeMods", true);
            SetPrivateField(viewModel, "_includeModSettings", true);
            SetPrivateField(viewModel, "_includeSvlLauncher", false);
            SetPrivateField(viewModel, "_isSmapiInstance", true);

            var itemType = typeof(VersionSettingsPageViewModel).Assembly.GetType(
                "SVL.Avalonia.ViewModels.ExportModPackageItem");
            Assert.IsNotNull(itemType);
            var item = Activator.CreateInstance(itemType!, nonPublic: true);
            Assert.IsNotNull(item);
            SetProperty(item!, "Name", "Actual Mod");
            SetProperty(item!, "UniqueId", "Example.ActualMod");
            SetProperty(item!, "Version", "1.2.3");
            SetProperty(item!, "Author", "SVL Test");
            SetProperty(item!, "ModPath", sourceMod);
            SetProperty(item!, "DirectoryName", "ActualMod");
            SetProperty(item!, "SourcePlatform", "NexusMods");
            SetProperty(item!, "SourceProjectId", projectId.ToString());
            SetProperty(item!, "SourceFileId", fileId.ToString());
            SetProperty(item!, "SourceFileName", "File 123456789_ Actual Mod 1.2.3.zip");
            SetProperty(item!, "SourceDownloadUrl", $"nxm://stardewvalley/mods/{projectId}/files/{fileId}");

            var itemListType = typeof(List<>).MakeGenericType(itemType!);
            var itemList = Activator.CreateInstance(itemListType)!;
            itemListType.GetMethod("Add")!.Invoke(itemList, [item]);

            var exportMethod = typeof(VersionSettingsPageViewModel).GetMethod(
                "BuildVersionSettingsExportPackage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(exportMethod);
            exportMethod!.Invoke(viewModel, [outputPath, sourceInstance, itemList]);

            Assert.IsTrue(File.Exists(outputPath));
            using (var exported = System.IO.Compression.ZipFile.OpenRead(outputPath))
            {
                Assert.IsNotNull(exported.GetEntry("modpack.json"));
                Assert.IsNotNull(exported.GetEntry("sources.json"));
                Assert.IsNotNull(exported.GetEntry("icon.png"));
                Assert.IsNotNull(exported.GetEntry("settings/Mods/ActualMod/config/config.json"));

                using var sourcesReader = new StreamReader(exported.GetEntry("sources.json")!.Open());
                var sourcesJson = await sourcesReader.ReadToEndAsync();
                StringAssert.Contains(sourcesJson, projectId.ToString());
                StringAssert.Contains(sourcesJson, fileId.ToString());
                StringAssert.Contains(sourcesJson, "File 123456789_");
                StringAssert.Contains(sourcesJson, "nxm://stardewvalley");
            }

            // 模拟导出包里混入了没有 manifest.json 的内置目录：它不能被静默
            // 当作“已安装”，而应出现在导入结果的失败列表中。
            using (var exported = System.IO.Compression.ZipFile.Open(
                       outputPath,
                       System.IO.Compression.ZipArchiveMode.Update))
            {
                WriteArchiveText(exported, "mods/BrokenBundledMod/readme.txt", "missing manifest");
            }

            Directory.CreateDirectory(Path.Combine(targetBase, "versions", "SMAPI 4.5.2"));
            File.WriteAllText(
                Path.Combine(targetBase, "versions", "SMAPI 4.5.2", "StardewModdingAPI.dll"),
                "existing-smapi");

            var settingsStore = new AppUserSettingsStore(Path.Combine(root, "settings"));
            var installService = new ModpackInstallService(
                new RoundTripGameInstallPathLocator(targetBase),
                new RoundTripSmapiInstallService(),
                new HttpDownloadService(settingsStore),
                new RemoteCatalogService(settingsStore),
                settingsStore,
                new NexusModDownloadResolverService(),
                new SVL.Core.Platform.Abstractions.NxmLinkParser());

            var result = await installService.InstallSvlModpackAsync(
                outputPath,
                "Imported Round Trip",
                targetBase,
                onProgress: null);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(1, result.InstalledMods.Count);
            Assert.AreEqual(1, result.FailedMods.Count);
            StringAssert.Contains(result.FailedMods[0], "BrokenBundledMod");
            var importedRoot = Path.Combine(targetBase, "versions", "Imported Round Trip");
            Assert.IsTrue(File.Exists(Path.Combine(importedRoot, "Mods", "ActualMod", "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(importedRoot, "Mods", "ActualMod", "content.json")));
            Assert.AreEqual(
                "{\"enabled\":true}",
                File.ReadAllText(Path.Combine(importedRoot, "Mods", "ActualMod", "config", "config.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(importedRoot, "Mods", "Actual Mod")));
            CollectionAssert.AreEqual(
                new byte[] { 7, 8, 9, 10 },
                File.ReadAllBytes(Path.Combine(importedRoot, ".svl-instance-icon-smapi.png")));
            Assert.IsTrue(File.Exists(Path.Combine(importedRoot, "Mods", "ActualMod", "svl-source.json")));
            StringAssert.Contains(
                File.ReadAllText(Path.Combine(importedRoot, "Mods", "ActualMod", "svl-source.json")),
                fileId.ToString());
            StringAssert.Contains(
                File.ReadAllText(Path.Combine(importedRoot, "Mods", "ActualMod", "svl-source.json")),
                "File 123456789_");

            // 第二次导入使用同一 Nexus ModID/FileID。它必须直接复用稳定缓存，
            // 不重新打开文件页或等待浏览器回调；同时仍要保留整合包的失败项报告。
            var secondResult = await installService.InstallSvlModpackAsync(
                outputPath,
                "Imported Round Trip Again",
                targetBase,
                onProgress: null);

            Assert.IsTrue(secondResult.IsSuccess, secondResult.Message);
            Assert.AreEqual(1, secondResult.InstalledMods.Count);
            Assert.AreEqual(1, secondResult.FailedMods.Count);
            Assert.IsTrue(File.Exists(Path.Combine(
                targetBase,
                "versions",
                "Imported Round Trip Again",
                "Mods",
                "ActualMod",
                "manifest.json")));
        }
        finally
        {
            if (existingCache == null)
            {
                try
                {
                    if (File.Exists(cachePath))
                    {
                        File.Delete(cachePath);
                    }
                }
                catch { }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllBytes(cachePath, existingCache);
            }

            // 导入服务会把实例写入真实的 Avalonia 注册表；回归测试使用临时 Base，
            // 清理时只移除本测试创建的那条记录，不能把用户的同名实例一并删除。
            var records = registry.LoadManualInstances();
            records.RemoveAll(record =>
                string.Equals(record.Name, "Imported Round Trip", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(record.Path) &&
                record.Path.StartsWith(targetBase, StringComparison.OrdinalIgnoreCase));
            registry.SaveManualInstances(records);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task ExportedSvlModpack_WithIncludeModFiles_ShouldBundleModsAndInstallOffline()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-export-bundled-offline-test-" + Guid.NewGuid().ToString("N"));
        var sourceInstance = Path.Combine(root, "source-instance");
        var sourceMod = Path.Combine(sourceInstance, "Mods", "OfflineMod");
        var outputPath = Path.Combine(root, "Offline Pack.zip");
        var targetBase = Path.Combine(root, "target-base");
        var registry = new InstanceRegistryStore();

        try
        {
            Directory.CreateDirectory(sourceMod);
            File.WriteAllText(
                Path.Combine(sourceMod, "manifest.json"),
                "{\"Name\":\"OfflineMod\",\"UniqueID\":\"Example.OfflineMod\",\"Version\":\"2.0.0\"}");
            File.WriteAllText(Path.Combine(sourceMod, "content.json"), "{\"data\":123}");

            var viewModel = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(VersionSettingsPageViewModel));
            SetPrivateField(viewModel, "_modpackName", "Offline Pack");
            SetPrivateField(viewModel, "_modpackVersion", "1.0.0");
            SetPrivateField(viewModel, "_modpackAuthor", "SVL Bundled Test");
            SetPrivateField(viewModel, "_smapiVersionText", "4.5.2");
            SetPrivateField(viewModel, "_includeMods", true);
            SetPrivateField(viewModel, "_includeModFiles", true);
            SetPrivateField(viewModel, "_includeModSettings", false);
            SetPrivateField(viewModel, "_includeSvlLauncher", false);
            SetPrivateField(viewModel, "_isSmapiInstance", true);

            var itemType = typeof(VersionSettingsPageViewModel).Assembly.GetType(
                "SVL.Avalonia.ViewModels.ExportModPackageItem");
            Assert.IsNotNull(itemType);
            var item = Activator.CreateInstance(itemType!, nonPublic: true);
            Assert.IsNotNull(item);
            SetProperty(item!, "Name", "OfflineMod");
            SetProperty(item!, "UniqueId", "Example.OfflineMod");
            SetProperty(item!, "Version", "2.0.0");
            SetProperty(item!, "Author", "SVL Bundled Test");
            SetProperty(item!, "ModPath", sourceMod);
            SetProperty(item!, "DirectoryName", "OfflineMod");
            SetProperty(item!, "SourcePlatform", "未知");

            var itemListType = typeof(List<>).MakeGenericType(itemType!);
            var itemList = Activator.CreateInstance(itemListType)!;
            itemListType.GetMethod("Add")!.Invoke(itemList, [item]);

            var exportMethod = typeof(VersionSettingsPageViewModel).GetMethod(
                "BuildVersionSettingsExportPackage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(exportMethod);
            exportMethod!.Invoke(viewModel, [outputPath, sourceInstance, itemList]);

            Assert.IsTrue(File.Exists(outputPath));

            using (var exported = System.IO.Compression.ZipFile.OpenRead(outputPath))
            {
                Assert.IsNotNull(exported.GetEntry("modpack.json"));
                Assert.IsNotNull(exported.GetEntry("sources.json"));
                Assert.IsNotNull(exported.GetEntry("export-manifest.json"));
                // 实体文件必须打包在 mods/ 目录下
                Assert.IsNotNull(exported.GetEntry("mods/OfflineMod/manifest.json"));
                Assert.IsNotNull(exported.GetEntry("mods/OfflineMod/content.json"));

                using var sourcesReader = new StreamReader(exported.GetEntry("sources.json")!.Open());
                var sourcesJson = await sourcesReader.ReadToEndAsync();
                StringAssert.Contains(sourcesJson, "\"bundled\": true");
            }

            Directory.CreateDirectory(Path.Combine(targetBase, "versions", "SMAPI 4.5.2"));
            File.WriteAllText(
                Path.Combine(targetBase, "versions", "SMAPI 4.5.2", "StardewModdingAPI.dll"),
                "existing-smapi");

            var settingsStore = new AppUserSettingsStore(Path.Combine(root, "settings"));
            var installService = new ModpackInstallService(
                new RoundTripGameInstallPathLocator(targetBase),
                new RoundTripSmapiInstallService(),
                new HttpDownloadService(settingsStore),
                new RemoteCatalogService(settingsStore),
                settingsStore,
                new NexusModDownloadResolverService(),
                new SVL.Core.Platform.Abstractions.NxmLinkParser());

            // 导入时无需网络，直接解压 bundled mod
            var result = await installService.InstallSvlModpackAsync(
                outputPath,
                "Imported Offline Pack",
                targetBase,
                onProgress: null);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(1, result.InstalledMods.Count);
            Assert.AreEqual(0, result.FailedMods.Count);
            var importedRoot = Path.Combine(targetBase, "versions", "Imported Offline Pack");
            Assert.IsTrue(File.Exists(Path.Combine(importedRoot, "Mods", "OfflineMod", "manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(importedRoot, "Mods", "OfflineMod", "content.json")));
        }
        finally
        {
            var records = registry.LoadManualInstances();
            records.RemoveAll(record =>
                string.Equals(record.Name, "Imported Offline Pack", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(record.Path) &&
                record.Path.StartsWith(targetBase, StringComparison.OrdinalIgnoreCase));
            registry.SaveManualInstances(records);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void InstancesPage_ImportModpackButton_ShouldBeNamedSearchModpacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-instances-search-button-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppUserSettingsStore(root);
            var locator = new RoundTripGameInstallPathLocator(root);
            var dialog = new DialogService();
            var registry = new InstanceRegistryStore();
            var localization = new LocalizationService(settings);
            var imageResource = new ImageResourceService(localization);

            var vm = new InstancesPageViewModel(locator, dialog, registry, settings, imageResource, localization);
            Assert.AreEqual("搜索整合包", vm.ImportModpackButtonText);

            var requested = false;
            vm.ModpackImportRequested += () => requested = true;
            vm.ImportModpackCommand.Execute(null);

            Assert.IsTrue(requested);
            Assert.AreEqual("正在打开整合包搜索", vm.Status);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void VersionSettingsPage_ModpackSection_ShouldSupportImportAndExportSubTabs()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-version-modpack-tab-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppUserSettingsStore(root);
            var localization = new LocalizationService(settings);

            var vm = (VersionSettingsPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(VersionSettingsPageViewModel));
            SetPrivateField(vm, "_localizationService", localization);
            SetPrivateField(vm, "_modpackNavText", "整合包");
            SetPrivateField(vm, "_exportNavText", "整合包");
            SetPrivateField(vm, "_selectedSection", "Overview");
            SetPrivateField(vm, "_isModpackImportTab", false);

            Assert.AreEqual("整合包", vm.ModpackNavText);
            Assert.AreEqual("整合包", vm.ExportNavText);

            // 切换到整合包分区
            vm.SwitchToModpackSectionCommand.Execute(null);
            Assert.IsTrue(vm.IsModpackSection);
            Assert.IsTrue(vm.IsExportSection);
            Assert.IsFalse(vm.IsModpackImportTab);
            Assert.IsTrue(vm.IsModpackExportTab);

            // 切换到导入整合包子分栏
            vm.SwitchModpackImportTabCommand.Execute(null);
            Assert.IsTrue(vm.IsModpackImportTab);
            Assert.IsFalse(vm.IsModpackExportTab);

            // 切换回导出整合包子分栏
            vm.SwitchModpackExportTabCommand.Execute(null);
            Assert.IsFalse(vm.IsModpackImportTab);
            Assert.IsTrue(vm.IsModpackExportTab);

            // 测试在线整合包搜索跳转事件
            var onlineSearchRequested = false;
            vm.NavigateToModpackSearchRequested += () => onlineSearchRequested = true;
            vm.NavigateToOnlineModpackSearchCommand.Execute(null);
            Assert.IsTrue(onlineSearchRequested);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void VersionSettingsPage_TitleAndNavigationHierarchy_ShouldBeOptimized()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-version-settings-nav-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppUserSettingsStore(root);
            var localization = new LocalizationService(settings);

            // 1. 本地化文案断言：彻底消除“版本设置”，优化侧边栏层级
            localization.SetLanguage("zh-CN");
            Assert.AreEqual("Mod管理", localization.Get("VersionSettings.Title"));
            Assert.AreEqual("Mod列表", localization.Get("VersionSettings.Nav.ModManage"));
            Assert.AreEqual("整合包", localization.Get("VersionSettings.Nav.Modpack"));
            Assert.AreEqual("SMAPI管理", localization.Get("VersionSettings.Nav.AutoInstall"));
            Assert.AreEqual("实例概览", localization.Get("VersionSettings.Nav.Overview"));
            Assert.AreEqual("实例设置", localization.Get("VersionSettings.Nav.Settings"));

            localization.SetLanguage("en-US");
            Assert.AreEqual("Mod Management", localization.Get("VersionSettings.Title"));
            Assert.AreEqual("Mod List", localization.Get("VersionSettings.Nav.ModManage"));
            Assert.AreEqual("Modpack", localization.Get("VersionSettings.Nav.Modpack"));
            Assert.AreEqual("SMAPI", localization.Get("VersionSettings.Nav.AutoInstall"));
            Assert.AreEqual("Instance Overview", localization.Get("VersionSettings.Nav.Overview"));
            Assert.AreEqual("Instance Settings", localization.Get("VersionSettings.Nav.Settings"));

            // 2. ViewModel 状态机与默认落地点断言
            localization.SetLanguage("zh-CN");
            var vm = (VersionSettingsPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(VersionSettingsPageViewModel));
            SetPrivateField(vm, "_localizationService", localization);
            SetPrivateField(vm, "_selectedSection", "ModManage");
            SetPrivateField(vm, "_isModManageSection", true);

            Assert.AreEqual("ModManage", vm.SelectedSection);
            Assert.IsTrue(vm.IsModManageSection);

            // 切换到实例概览
            vm.SwitchToGeneral();
            Assert.AreEqual("Overview", vm.SelectedSection);
            Assert.IsTrue(vm.IsOverviewSection);
            Assert.IsFalse(vm.IsModManageSection);
            Assert.AreEqual("当前处于实例概览", vm.Status);

            // 切换到实例设置
            vm.SwitchToSettings();
            Assert.AreEqual("Settings", vm.SelectedSection);
            Assert.IsTrue(vm.IsSettingsSection);
            Assert.AreEqual("当前处于实例设置", vm.Status);

            // 切换回 Mod 列表
            vm.SwitchToModManage();
            Assert.AreEqual("ModManage", vm.SelectedSection);
            Assert.IsTrue(vm.IsModManageSection);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void GlobalVersionConsistency_WhenSwitchingToSmapi_ShouldSynchronizeAllPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-global-version-consistency-test-" + Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(root, "Stardew Valley");
        Directory.CreateDirectory(gamePath);
        File.WriteAllText(Path.Combine(gamePath, "Stardew Valley.dll"), "fake");
        File.WriteAllText(Path.Combine(gamePath, "StardewModdingAPI.dll"), "fake");

        try
        {
            var settingsStore = new AppUserSettingsStore(root);
            var localization = new LocalizationService(settingsStore);

            // 1. 模拟用户在版本选择页选中了“原版”
            var initialSettings = settingsStore.Load();
            initialSettings.PreferredInstancePath = gamePath;
            initialSettings.PreferredLaunchMode = "Vanilla";
            initialSettings.InstanceName = "Stardew Valley";
            settingsStore.Save(initialSettings);

            // 2. 构造 VersionSettingsPageViewModel 并触发 SwitchToSmapiVersionCommand
            var vsVm = (VersionSettingsPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(VersionSettingsPageViewModel));
            SetPrivateField(vsVm, "_settingsStore", settingsStore);
            SetPrivateField(vsVm, "_localizationService", localization);
            SetPrivateField(vsVm, "_instanceName", "Stardew Valley");
            SetPrivateField(vsVm, "_hasInstalledSmapi", true);
            SetPrivateField(vsVm, "_isSmapiInstance", false);
            SetPrivateField(vsVm, "_detectedGamePath", gamePath);

            // 确认当前状态为可切换
            Assert.IsTrue(vsVm.ShowSwitchToSmapiHint);

            // 执行切换到 SMAPI
            vsVm.SwitchToSmapiVersionCommand.Execute(null);

            // 3. 断言配置中心 settingsStore 已更新为 SMAPI 模式和对应的实例名称
            var savedSettings = settingsStore.Load();
            Assert.AreEqual("SMAPI", savedSettings.PreferredLaunchMode);
            Assert.IsTrue(savedSettings.InstanceName.Contains("SMAPI"));

            // 4. 断言 InstancesPageViewModel 恢复逻辑能够准确匹配选中 SMAPI 实例
            var instancesVm = (InstancesPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(InstancesPageViewModel));
            SetPrivateField(instancesVm, "_settingsStore", settingsStore);
            SetPrivateField(instancesVm, "_localizationService", localization);

            var vanillaItem = new InstanceItem
            {
                Name = "Stardew Valley",
                Path = gamePath,
                IsSmapiInstance = false
            };
            var smapiItem = new InstanceItem
            {
                Name = "Stardew Valley (SMAPI)",
                Path = gamePath,
                IsSmapiInstance = true
            };

            var resolveMethod = typeof(InstancesPageViewModel).GetMethod("ResolveRestoredInstance",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(resolveMethod);

            var restored = (InstanceItem)resolveMethod.Invoke(null,
                new object[] { new[] { vanillaItem, smapiItem }, savedSettings, "Stardew Valley", false });

            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.IsSmapiInstance);
            Assert.AreEqual("Stardew Valley (SMAPI)", restored.Name);

            // 5. 断言 LaunchPageViewModel 刷新后展示为 SMAPI 实例
            var launchVm = (LaunchPageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(LaunchPageViewModel));
            SetPrivateField(launchVm, "_settingsStore", settingsStore);
            SetPrivateField(launchVm, "_localizationService", localization);
            launchVm.RefreshFromSettingsAndEnvironment();

            Assert.IsTrue(launchVm.InstanceName.Contains("SMAPI"));
            Assert.IsTrue(launchVm.VersionStatus.Contains("SMAPI"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task SvlModpackInstall_ShouldRejectMissingManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-missing-modpack-manifest-test-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "not-svl-pack.zip");
        var gamePath = Path.Combine(root, "game");
        try
        {
            Directory.CreateDirectory(gamePath);
            using (var archive = System.IO.Compression.ZipFile.Open(
                       archivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteArchiveText(archive, "readme.txt", "not an SVL modpack");
            }

            var settingsStore = new AppUserSettingsStore(Path.Combine(root, "settings"));
            var service = new ModpackInstallService(
                new RoundTripGameInstallPathLocator(gamePath),
                new RoundTripSmapiInstallService(),
                new HttpDownloadService(settingsStore),
                new RemoteCatalogService(settingsStore),
                settingsStore,
                new NexusModDownloadResolverService(),
                new SVL.Core.Platform.Abstractions.NxmLinkParser());

            var result = await service.InstallSvlModpackAsync(
                archivePath,
                "Missing Manifest",
                gamePath,
                onProgress: null);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "modpack.json");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void ConfigurationStores_ShouldReplaceFilesAtomically()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "svl-atomic-store-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsStore = new AppUserSettingsStore(Path.Combine(root, "settings"));
            settingsStore.Save(new AppUserSettings
            {
                LauncherTitle = "第一次保存",
                ThemeMode = "深色"
            });
            settingsStore.Save(new AppUserSettings
            {
                LauncherTitle = "第二次保存",
                ThemeMode = "浅色",
                PreferredInstancePath = "D:\\Applications\\Stardew Valley"
            });

            var loadedSettings = settingsStore.Load();
            Assert.AreEqual("第二次保存", loadedSettings.LauncherTitle);
            Assert.AreEqual("浅色", loadedSettings.ThemeMode);
            Assert.AreEqual(
                "D:\\Applications\\Stardew Valley",
                loadedSettings.PreferredInstancePath);

            var registryStore = new InstanceRegistryStore(Path.Combine(root, "instances"));
            registryStore.SaveManualInstances(
            [
                new ManualInstanceRecord
                {
                    Name = "测试实例",
                    Path = "D:\\Applications\\Stardew Valley\\versions\\测试实例"
                }
            ]);
            registryStore.SaveManualInstances(
            [
                new ManualInstanceRecord
                {
                    Name = "更新后的实例",
                    Path = "D:\\Applications\\Stardew Valley\\versions\\更新后的实例"
                }
            ]);

            var loadedInstances = registryStore.LoadManualInstances();
            Assert.AreEqual(1, loadedInstances.Count);
            Assert.AreEqual("更新后的实例", loadedInstances[0].Name);
            Assert.AreEqual(
                "D:\\Applications\\Stardew Valley\\versions\\更新后的实例",
                loadedInstances[0].Path);

            Assert.IsFalse(
                Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(),
                "原子写入完成后不应留下临时文件");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ModConflictAnalyzer_ShouldDetectDuplicateDependencyAndAssetConflicts()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "svl-mod-conflict-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var firstPath = CreateModFolder(root, "First", "i18n/default.json", "config.json", "icon.png");
            var secondPath = CreateModFolder(root, "Second", "i18n/default.json", "config.json", "icon.png");
            var duplicateOnePath = CreateModFolder(root, "DuplicateOne", "one.json");
            var duplicateTwoPath = CreateModFolder(root, "DuplicateTwo", "two.json");
            var disabledPath = CreateModFolder(root, "DisabledDependency", "disabled.json");

            File.WriteAllText(
                Path.Combine(firstPath, "content.json"),
                "{\"Format\":\"2.0.0\",\"Changes\":[{\"Action\":\"Load\",\"Target\":\"Characters/Abigail\"}]}");
            File.WriteAllText(
                Path.Combine(secondPath, "content.json"),
                "{\"Format\":\"2.0.0\",\"Changes\":[{\"Action\":\"Load\",\"Target\":\"characters/abigail\"}]}");

            File.WriteAllText(
                Path.Combine(firstPath, "manifest.json"),
                "{\"UniqueID\":\"Author.First\",\"Name\":\"First Mod\",\"Version\":\"1.0.0\",\"Incompatible\":[\"Author.Second\"]}");
            File.WriteAllText(
                Path.Combine(secondPath, "manifest.json"),
                "{\"UniqueID\":\"Author.Second\",\"Name\":\"Second Mod\",\"Version\":\"1.0.0\"}");

            var first = new ModManageItem
            {
                DisplayName = "First Mod",
                DirectoryName = "First",
                FullPath = firstPath,
                UniqueId = "Author.First",
                Version = "1.0.0",
                IsEnabled = true
            };
            first.DisplayDependencies.Add(new ModDependencyDisplayItem
            {
                UniqueId = "Author.Second",
                DisplayName = "Second Mod",
                MinimumVersion = "2.0.0",
                IsRequired = true,
                IsInstalled = true,
                IsInstalledAndEnabled = true
            });
            first.DisplayDependencies.Add(new ModDependencyDisplayItem
            {
                UniqueId = "Author.Missing",
                DisplayName = "Missing Mod",
                MinimumVersion = "1.0.0",
                IsRequired = true
            });
            first.DisplayDependencies.Add(new ModDependencyDisplayItem
            {
                UniqueId = "Author.Disabled",
                DisplayName = "Disabled Dependency",
                IsRequired = true,
                IsInstalled = true,
                IsInstalledButDisabled = true
            });

            var second = new ModManageItem
            {
                DisplayName = "Second Mod",
                DirectoryName = "Second",
                FullPath = secondPath,
                UniqueId = "Author.Second",
                Version = "1.0.0",
                IsEnabled = true
            };
            second.DisplayDependencies.Add(new ModDependencyDisplayItem
            {
                UniqueId = "Author.First",
                DisplayName = "First Mod",
                IsRequired = true,
                IsInstalled = true,
                IsInstalledAndEnabled = true
            });
            var duplicateOne = new ModManageItem
            {
                DisplayName = "Duplicate One",
                DirectoryName = "DuplicateOne",
                FullPath = duplicateOnePath,
                UniqueId = "Author.Duplicate",
                IsEnabled = true
            };
            var duplicateTwo = new ModManageItem
            {
                DisplayName = "Duplicate Two",
                DirectoryName = "DuplicateTwo",
                FullPath = duplicateTwoPath,
                UniqueId = "author.duplicate",
                IsEnabled = true
            };
            var disabled = new ModManageItem
            {
                DisplayName = "Disabled Dependency",
                DirectoryName = "DisabledDependency",
                FullPath = disabledPath,
                UniqueId = "Author.Disabled",
                Version = "1.0.0",
                IsEnabled = false
            };

            var conflicts = ModConflictAnalyzer.Analyze(
                [first, second, duplicateOne, duplicateTwo, disabled]);

            CollectionAssert.Contains(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.DuplicateId);
            CollectionAssert.Contains(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.MissingDependency);
            CollectionAssert.Contains(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.DisabledDependency);
            CollectionAssert.Contains(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.VersionMismatch);
            var cycleConflicts = conflicts
                .Where(item => item.Kind == ModConflictKind.CircularDependency)
                .ToList();
            Assert.AreEqual(1, cycleConflicts.Count, "同一个循环依赖应只展示一条结果");
            StringAssert.Contains(cycleConflicts[0].Description, "First Mod");
            StringAssert.Contains(cycleConflicts[0].Description, "Second Mod");

#pragma warning disable CS0618
            CollectionAssert.DoesNotContain(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.FileConflict,
                "跨 Mod 目录的常规同名文件（如 i18n/default.json、config.json）不应误报文件冲突");
#pragma warning restore CS0618

            CollectionAssert.Contains(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.AssetConflict,
                "两 Mod 均以 Action: Load 独占加载相同目标资产时应检出 CP 资产冲突");
            Assert.IsTrue(
                conflicts.Any(item => item.Kind == ModConflictKind.AssetConflict &&
                                      item.Description.Contains("Characters/Abigail", StringComparison.OrdinalIgnoreCase)),
                "应指明具体冲突的目标游戏资产“Characters/Abigail”");

            CollectionAssert.Contains(
                conflicts.Select(item => item.Kind).ToList(),
                ModConflictKind.IncompatibleMod,
                "Mod manifest 中声明的不兼容应检出互斥冲突");
            Assert.IsTrue(
                conflicts.Any(item => item.Kind == ModConflictKind.IncompatibleMod &&
                                      item.Description.Contains("Second Mod", StringComparison.OrdinalIgnoreCase)),
                "应指出与 Second Mod 互斥不兼容");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task RemoteCatalogService_ShouldRouteCurseforgeSearchAndDetailsThroughHttpFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-remote-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new FixtureHttpMessageHandler(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("{\"data\":[{" +
                        "\"id\":101,\"name\":\"Content Patcher\",\"summary\":\"A mod\"," +
                        "\"downloadCount\":123,\"dateModified\":\"2024-01-01T00:00:00Z\"," +
                        "\"logo\":{\"url\":\"https://cdn.example/content.png\",\"thumbnailUrl\":\"https://cdn.example/content-small.png\"}" +
                        "}]}");
                }

                if (path.EndsWith("/mods/101/files", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("{\"data\":[{" +
                        "\"id\":7448774,\"displayName\":\"Content Patcher 2.9.0\"," +
                        "\"fileName\":\"File 7448774_ Content Patcher 2.9.0.zip\"," +
                        "\"downloadUrl\":\"https://edge.example/files/content-patcher.zip\"," +
                        "\"fileLength\":1234,\"downloadCount\":12," +
                        "\"fileDate\":\"2024-01-01T00:00:00Z\",\"releaseType\":1," +
                        "\"gameVersions\":[\"1.6.15\"]" +
                        "}]}");
                }

                if (path.EndsWith("/mods/101", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("{\"data\":{\"id\":101,\"name\":\"Content Patcher\"," +
                        "\"summary\":\"A mod\",\"logo\":{\"url\":\"https://cdn.example/content.png\"}}}");
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            });
            using var client = new HttpClient(handler);
            var service = new RemoteCatalogService(new AppUserSettingsStore(root), client);

            var search = await service.SearchModsAdvancedPagedAsync(
                "Content",
                "Curseforge",
                useCommunityLocalization: false,
                page: 1,
                pageSize: 10);

            Assert.AreEqual(1, search.Items.Count);
            Assert.AreEqual(101, search.Items[0].Identity.ResourceId);
            Assert.AreEqual(CatalogSource.Curseforge, search.Items[0].Identity.Source);
            Assert.IsFalse(search.HasMore);

            var details = await service.GetResourceDetailsAsync(
                new CatalogResourceIdentity(101, "Content Patcher", CatalogSource.Curseforge, false, string.Empty));
            Assert.AreEqual("Content Patcher", details.Name);
            Assert.AreEqual(1, details.DownloadOptions.Count);
            StringAssert.Contains(details.DownloadOptions[0], "7448774");
            StringAssert.Contains(details.DownloadOptions[0], "https://edge.example/files/content-patcher.zip");
            StringAssert.Contains(details.VersionOptions[0], "1.6.15");

            var requests = handler.Requests.Select(request => request.AbsolutePath).ToList();
            CollectionAssert.Contains(requests, "/v1/mods/search");
            CollectionAssert.Contains(requests, "/v1/mods/101");
            CollectionAssert.Contains(requests, "/v1/mods/101/files");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task RemoteCatalogService_ShouldKeepCurseforgeSmapiFileId()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-smapi-curseforge-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new FixtureHttpMessageHandler(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/mods/898372/files", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        "{\"data\":[{" +
                        "\"id\":\"5312529\",\"displayName\":\"SMAPI 4.5.2\"," +
                        "\"fileName\":\"SMAPI-4.5.2.zip\",\"fileDate\":\"2024-01-01T00:00:00Z\"" +
                        "}]}" );
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            });
            using var client = new HttpClient(handler);
            var service = new RemoteCatalogService(new AppUserSettingsStore(root), client);

            var entries = await service.GetSmapiVersionEntriesFromCurseForgeAsync(1, 5);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Curseforge", entries[0].Source);
            Assert.AreEqual(5312529L, entries[0].FileId);
            StringAssert.Contains(entries[0].DownloadUrl, "edge.forgecdn.net/files/5312/529");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public async Task RemoteCatalogService_ShouldSendNexusCredentialAndParseGraphQlFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-nexus-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new FixtureHttpMessageHandler(_ => JsonResponse(
                "{\"data\":{\"mods\":{\"nodes\":[{" +
                "\"modId\":29868,\"name\":\"Content Patcher\",\"summary\":\"A Nexus mod\"," +
                "\"description\":\"A Nexus mod\",\"pictureUrl\":\"https://cdn.example/nexus.png\"," +
                "\"downloads\":456,\"category\":\"utility\",\"updatedAt\":\"2024-01-02T00:00:00Z\"" +
                "}]}}}"));
            using var client = new HttpClient(handler);
            var store = new AppUserSettingsStore(root);
            store.Save(new AppUserSettings { NexusApiKey = "fixture-api-key" });
            var service = new RemoteCatalogService(store, client);

            var result = await service.SearchModsAdvancedPagedAsync(
                "Content",
                "NexusMods",
                useCommunityLocalization: false,
                page: 1,
                pageSize: 10);

            Assert.AreEqual(1, result.Items.Count);
            Assert.AreEqual(29868, result.Items[0].Identity.ResourceId);
            Assert.AreEqual(CatalogSource.NexusMods, result.Items[0].Identity.Source);
            Assert.AreEqual("Content Patcher", result.Items[0].Name);
            Assert.IsFalse(result.HasMore);
            CollectionAssert.Contains(handler.ApiKeys, "fixture-api-key");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task RemoteCatalogService_ShouldExposeCurseforgeNoFileFixtureAsInformational()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-no-file-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new FixtureHttpMessageHandler(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/mods/202", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        "{\"data\":{\"id\":202,\"name\":\"No File Mod\",\"summary\":\"No release yet\"}}");
                }

                if (path.EndsWith("/mods/202/files", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("{\"data\":[]}");
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            });
            using var client = new HttpClient(handler);
            var service = new RemoteCatalogService(new AppUserSettingsStore(root), client);

            var details = await service.GetResourceDetailsAsync(
                new CatalogResourceIdentity(202, "No File Mod", CatalogSource.Curseforge, false, string.Empty));

            CollectionAssert.AreEqual(
                new[] { "暂无可下载文件" },
                details.DownloadOptions);
            Assert.IsTrue(details.VersionOptions.Count == 0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateModFolder(string root, string name, params string[] files)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        foreach (var file in files)
        {
            var filePath = Path.Combine(path, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, name);
        }

        return path;
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class FixtureHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> ApiKeys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.Headers.TryGetValues("apikey", out var apiKeys))
            {
                ApiKeys.AddRange(apiKeys);
            }

            return Task.FromResult(responder(request));
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"未找到字段 {fieldName}");
        field!.SetValue(target, value);
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(property, $"未找到属性 {propertyName}");
        property!.SetValue(target, value);
    }

    private static void WriteArchiveText(
        System.IO.Compression.ZipArchive archive,
        string entryName,
        string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write(content);
    }

    private static void WriteSevenZipText(
        SharpCompress.Writers.IWriter writer,
        string entryName,
        string content)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        writer.Write(entryName, stream, null);
    }

    private sealed class RoundTripGameInstallPathLocator(string gamePath)
        : SVL.Core.Platform.Abstractions.IGameInstallPathLocator
    {
        public string TryLocateSteamStardewPath() => gamePath;

        public string TryLocateGogStardewPath() => null;
    }

    private sealed class RoundTripSmapiInstallService
        : SVL.Core.Platform.Abstractions.ISmapiInstallService
    {
        public Task<SVL.Core.Platform.Abstractions.SmapiInstallResult> InstallFromZipAsync(
            string zipFilePath,
            string gameBasePath,
            string instanceName,
            CancellationToken cancellationToken = default,
            Action<string> logger = null,
            Func<string, string, CancellationToken, Task> zipExtractor = null,
            bool updateExisting = false)
        {
            var versionRoot = Path.Combine(gameBasePath, "versions", instanceName);
            Directory.CreateDirectory(versionRoot);
            File.WriteAllText(Path.Combine(versionRoot, "StardewModdingAPI.dll"), "smapi");
            return Task.FromResult(
                SVL.Core.Platform.Abstractions.SmapiInstallResult.Success(versionRoot, versionRoot));
        }
    }
}
