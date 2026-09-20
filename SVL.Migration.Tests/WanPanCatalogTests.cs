using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using SVL.Avalonia.ViewModels;
using System.Net;
using System.Text.Json;

namespace SVL.Migration.Tests;

[TestClass]
public class WanPanCatalogTests
{
    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    [TestMethod]
    public void WanPanCatalogResponse_Deserialize_ShouldPopulateFieldsCorrectly()
    {
        const string json = """
        {
            "code": 0,
            "msg": "success",
            "data": {
                "total": 1,
                "page": 1,
                "size": 20,
                "items": [
                    {
                        "id": "stardew_res_test123",
                        "name": "星露谷物语 汉化整合包",
                        "category": "modpacks",
                        "summary": "包含常用基础模组与汉化补丁",
                        "version": "1.6.14",
                        "download_url": "https://pan.quark.cn/s/testquark",
                        "password": "ABCD",
                        "cloud_type": "quark",
                        "cloud_type_name": "夸克网盘",
                        "file_size": 104857600,
                        "size_formatted": "100.0 MB",
                        "icon_url": "https://pan.originagent.cn/icons/test.png",
                        "tags": ["汉化", "推荐"],
                        "updated_at": "2026-03-20"
                    }
                ]
            }
        }
        """;

        var response = JsonSerializer.Deserialize<WanPanCatalogResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.IsNotNull(response);
        Assert.AreEqual(0, response.Code);
        Assert.AreEqual("success", response.Msg);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(1, response.Data.Total);
        Assert.AreEqual(1, response.Data.Items.Count);

        var item = response.Data.Items[0];
        Assert.AreEqual("stardew_res_test123", item.Id);
        Assert.AreEqual("星露谷物语 汉化整合包", item.Name);
        Assert.AreEqual("modpacks", item.Category);
        Assert.AreEqual("https://pan.quark.cn/s/testquark", item.DownloadUrl);
        Assert.AreEqual("ABCD", item.Password);
        Assert.AreEqual("quark", item.CloudType);
        Assert.AreEqual("夸克网盘", item.CloudTypeName);
        Assert.AreEqual("100.0 MB", item.SizeFormatted);
    }

    [TestMethod]
    public async Task SearchWanPanResourcesPagedAsync_ShouldMapToModSearchResultItems()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "svl_wanpan_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsStore = new AppUserSettingsStore(tempDir);
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                var json = """
                {
                    "code": 0,
                    "msg": "success",
                    "data": {
                        "total": 2,
                        "page": 1,
                        "size": 20,
                        "items": [
                            {
                                "id": "smapi_res_001",
                                "name": "SMAPI 4.1.8 安装包",
                                "category": "smapi",
                                "summary": "星露谷 1.6 模组加载器",
                                "version": "4.1.8",
                                "download_url": "https://pan.originagent.cn/files/SMAPI-4.1.8-installer.zip",
                                "cloud_type": "local",
                                "cloud_type_name": "直链下载",
                                "size_formatted": "15.0 MB"
                            },
                            {
                                "id": "mod_res_002",
                                "name": "CJB 作弊菜单",
                                "category": "mods",
                                "summary": "内置修改器",
                                "version": "1.34.0",
                                "download_url": "https://pan.baidu.com/s/testbaidu",
                                "password": "BAID",
                                "cloud_type": "baidu",
                                "cloud_type_name": "百度网盘",
                                "size_formatted": "2.5 MB"
                            }
                        ]
                    }
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            });

            var httpClient = new HttpClient(mockHandler);
            var catalogService = new RemoteCatalogService(settingsStore, httpClient);

            var (items, total) = await catalogService.SearchWanPanResourcesPagedAsync("all", "test");

            Assert.AreEqual(2, total);
            Assert.AreEqual(2, items.Count);

            var item1 = items[0];
            Assert.AreEqual("SMAPI 4.1.8 安装包", item1.Name);
            Assert.AreEqual(CatalogSource.WanPan, item1.Identity.Source);
            Assert.AreEqual("smapi_res_001", item1.CollectionSlug);
            Assert.AreEqual("https://pan.originagent.cn/files/SMAPI-4.1.8-installer.zip", item1.DownloadUrl);
            Assert.AreEqual("直链下载 · 15.0 MB", item1.Stat);

            var item2 = items[1];
            Assert.AreEqual("CJB 作弊菜单", item2.Name);
            Assert.AreEqual(CatalogSource.WanPan, item2.Identity.Source);
            Assert.AreEqual("BAID", item2.Password);
            Assert.AreEqual("baidu", item2.CloudType);
            Assert.AreEqual("百度网盘", item2.CloudTypeName);
            Assert.AreEqual("网盘高速源", item2.SourceTag);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task GetWanPanResourceDetailsAsync_ShouldBuildDownloadOptionsAndDetails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "svl_wanpan_details_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsStore = new AppUserSettingsStore(tempDir);
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                var json = """
                {
                    "code": 0,
                    "msg": "success",
                    "data": {
                        "id": "stardew_res_cjb",
                        "name": "CJB 物品产出器",
                        "category": "mods",
                        "summary": "按键即可刷出任意游戏内物品",
                        "version": "2.2.0",
                        "download_url": "https://pan.quark.cn/s/cjb123",
                        "password": "9999",
                        "cloud_type": "quark",
                        "cloud_type_name": "夸克网盘",
                        "size_formatted": "1.2 MB",
                        "tags": ["作弊类", "功能扩展"]
                    }
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            });

            var httpClient = new HttpClient(mockHandler);
            var catalogService = new RemoteCatalogService(settingsStore, httpClient);

            var identity = new CatalogResourceIdentity(12345, "CJB 物品产出器", CatalogSource.WanPan, false, "stardew_res_cjb");
            var details = await catalogService.GetResourceDetailsAsync(identity);

            Assert.AreEqual("CJB 物品产出器", details.Name);
            Assert.IsTrue(details.Source.Contains("网盘高速源"));
            Assert.IsTrue(details.Source.Contains("夸克网盘"));
            Assert.AreEqual(1, details.DownloadOptions.Count);
            Assert.IsTrue(details.DownloadOptions[0].Contains("url=https://pan.quark.cn/s/cjb123"));
            Assert.IsTrue(details.DownloadOptions[0].Contains("pwd=9999"));
            Assert.AreEqual(2, details.Dependencies.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void TryExtractPasswordFromOption_ShouldExtractPasswordCorrectly()
    {
        var opt1 = "夸克网盘 (100.0 MB) 提取码: ABCD | url=https://pan.quark.cn/s/123 | pwd=ABCD";
        Assert.AreEqual("ABCD", DownloadPageViewModel.TryExtractPasswordFromOption(opt1));

        var opt2 = "百度网盘 提取码：XYZ8 | url=https://pan.baidu.com/s/456";
        Assert.AreEqual("XYZ8", DownloadPageViewModel.TryExtractPasswordFromOption(opt2));

        var opt3 = "直链下载 | url=https://cdn.example.com/mod.zip";
        Assert.AreEqual(string.Empty, DownloadPageViewModel.TryExtractPasswordFromOption(opt3));
    }

    [TestMethod]
    public void IsDirectArchiveUrl_ShouldRecognizeArchiveExtensions()
    {
        Assert.IsTrue(DownloadPageViewModel.IsDirectArchiveUrl("https://example.com/mod.zip"));
        Assert.IsTrue(DownloadPageViewModel.IsDirectArchiveUrl("https://example.com/package.7z?token=abc"));
        Assert.IsTrue(DownloadPageViewModel.IsDirectArchiveUrl("https://example.com/archive.rar"));
        Assert.IsTrue(DownloadPageViewModel.IsDirectArchiveUrl("https://example.com/build.tar.gz"));

        Assert.IsFalse(DownloadPageViewModel.IsDirectArchiveUrl("https://pan.quark.cn/s/123"));
        Assert.IsFalse(DownloadPageViewModel.IsDirectArchiveUrl("https://pan.baidu.com/s/xyz"));
        Assert.IsFalse(DownloadPageViewModel.IsDirectArchiveUrl("https://www.nexusmods.com/stardewvalley/mods/2400"));
    }

    [TestMethod]
    public void DownloadCatalogItem_WanPanTags_ShouldProduceExpectedDisplay()
    {
        var item = new DownloadCatalogItem
        {
            Name = "测试网盘模组",
            CloudType = "quark",
            CloudTypeName = "夸克网盘",
            Password = "K9K9",
            DownloadUrl = "https://pan.quark.cn/s/test"
        };

        Assert.IsTrue(item.HasPassword);
        Assert.AreEqual("提取码: K9K9", item.PasswordTag);
        Assert.IsTrue(item.HasCloudTag);
        Assert.AreEqual("夸克网盘", item.CloudTag);
        Assert.IsTrue(item.HasDownloadUrl);
    }
}
