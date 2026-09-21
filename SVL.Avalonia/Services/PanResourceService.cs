using CommunityToolkit.Mvvm.ComponentModel;
using SVL.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SVL.Avalonia.Services;

/// <summary>
/// 分类选项模型（用于商店头部切换 Chips）。
/// </summary>
public sealed partial class PanCategoryOption : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    public string Icon { get; }

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
    public bool HasCount => Count > 0;

    public PanCategoryOption(string key, string name, string icon = "", int count = 0, bool isSelected = false)
    {
        Key = key;
        Name = name;
        Icon = icon;
        Count = count;
        IsSelected = isSelected;
    }
}

/// <summary>
/// 网盘资源卡片条目。
/// <para>Business Rule: 包含封面、标题、提取码、网盘厂商、版本、作者等丰富字段，支持卡片式、商店式展示。</para>
/// </summary>
public sealed partial class PanResourceItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string PanUrl { get; init; } = string.Empty;
    public string Category { get; init; } = "all";
    public string CategoryName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string SizeFormatted { get; init; } = string.Empty;
    public string IconUrl { get; init; } = string.Empty;
    public string CloudType { get; init; } = string.Empty;
    public string CloudTypeName { get; init; } = string.Empty;
    public string UpdatedAt { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool IsRecommended { get; init; }

    [ObservableProperty]
    private string _iconSource = string.Empty;

    public bool HasPassword => !string.IsNullOrWhiteSpace(Password);
    public string PasswordTag => HasPassword ? $"提取码: {Password}" : string.Empty;
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public string VersionDisplay => HasVersion ? $"v{Version}" : string.Empty;
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public string AuthorDisplay => HasAuthor ? $"by {Author}" : string.Empty;
    public bool HasSize => !string.IsNullOrWhiteSpace(SizeFormatted);
    public bool HasTags => Tags != null && Tags.Count > 0;
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    public string CategoryDisplay => Category?.ToLowerInvariant() switch
    {
        "game" => "游戏本体",
        "smapi" => "SMAPI",
        "mods" => "精选模组",
        "modpacks" => "整合包",
        _ => !string.IsNullOrWhiteSpace(CategoryName) ? CategoryName : "综合资源"
    };

    public bool IsDirectDownload =>
        string.Equals(CloudType, "local", StringComparison.OrdinalIgnoreCase) ||
        PanUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        PanUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
        PanUrl.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
        PanUrl.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    public string ActionButtonText => IsDirectDownload ? "立即下载" : "获取网盘链接";

    public PanResourceItem() { }

    /// <summary>保留 4 参数构造函数以确保对旧代码的完全兼容。</summary>
    public PanResourceItem(string name, string description, string source, string panUrl)
    {
        Name = name;
        Description = description;
        Source = source;
        PanUrl = panUrl;
    }
}

/// <summary>
/// 网盘资源服务：为顶栏“网盘资源”页提供卡片式 Mod/整合包/本体展示数据。
/// <para>Business Rule: 本地磁盘与内存双层持久化缓存。仅当手动刷新、本地无缓存或超过设定周期时才调用后端接口，避免给服务端造成无谓压力。</para>
/// </summary>
public sealed class PanResourceService
{
    private readonly HttpClient _httpClient;
    private readonly AppUserSettingsStore? _settingsStore;
    private readonly string _cacheFilePath;
    private PanCatalogCacheEntry? _memoryCache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>本地缓存有效周期（默认 24 小时）。</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>本地持久化缓存文件路径。</summary>
    public string CacheFilePath => _cacheFilePath;

    /// <summary>最近一次缓存生成的 UTC 时间。</summary>
    public DateTime? LastCachedTimeUtc => _memoryCache?.CachedAtUtc;

    public Action<string>? DebugLogger { get; set; }

    public PanResourceService(
        HttpClient? httpClient = null,
        AppUserSettingsStore? settingsStore = null,
        string? cacheFilePath = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _settingsStore = settingsStore;

        if (!string.IsNullOrWhiteSpace(cacheFilePath))
        {
            _cacheFilePath = Path.GetFullPath(cacheFilePath);
        }
        else
        {
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SVL",
                "Avalonia");
            _cacheFilePath = Path.Combine(basePath, "pan-catalog-cache.json");
        }
    }

    public string GetWanPanBaseUrl()
    {
        var baseUri = _settingsStore?.Load()?.WanPanApiBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUri))
        {
            return "https://pan.originagent.cn";
        }
        return baseUri.TrimEnd('/');
    }

    /// <summary>
    /// 获取网盘资源目录（包含分类元数据与卡片条目）。
    /// <para>优先使用本地持久化缓存；仅当 forceReload 为 true、本地无有效缓存或缓存超过有效周期时才调用远程接口。</para>
    /// </summary>
    public async Task<(IReadOnlyList<PanCategoryOption> Categories, IReadOnlyList<PanResourceItem> Items)> GetCatalogAsync(
        string category = "all",
        string? keyword = null,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        var isFullCatalog = (string.IsNullOrWhiteSpace(category) || string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
                            && string.IsNullOrWhiteSpace(keyword);

        // 如果是常规全量浏览且未指定强制刷新，优先检查内存及本地磁盘缓存
        if (isFullCatalog && !forceReload)
        {
            if (IsCacheValid(_memoryCache))
            {
                LogDebug("[PanResourceService] Returning full catalog from memory cache");
                return MapCacheEntryToResult(_memoryCache!);
            }

            var diskCache = TryLoadDiskCache();
            if (IsCacheValid(diskCache))
            {
                LogDebug($"[PanResourceService] Returning full catalog from valid disk cache (cached at {diskCache!.CachedAtUtc:u})");
                _memoryCache = diskCache;
                return MapCacheEntryToResult(diskCache);
            }
        }

        // 需要从网络拉取（强制刷新 / 缓存不存在 / 缓存过期 / 带关键词定向检索）
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // 加锁后复核：避免并发调用时重复穿透网络请求
            if (isFullCatalog && !forceReload && IsCacheValid(_memoryCache))
            {
                return MapCacheEntryToResult(_memoryCache!);
            }

            try
            {
                var (dtoCategories, dtoItems) = await FetchFromRemoteAsync(category, keyword, cancellationToken);

                if (isFullCatalog && dtoItems.Count > 0)
                {
                    var cacheEntry = new PanCatalogCacheEntry
                    {
                        CachedAtUtc = DateTime.UtcNow,
                        Categories = dtoCategories,
                        Items = dtoItems
                    };

                    _memoryCache = cacheEntry;
                    SaveDiskCache(cacheEntry);
                }

                var categories = dtoCategories.Select(c => new PanCategoryOption(c.Key, c.Name)).ToList();
                var items = dtoItems.Select(MapDtoToItem).ToList();
                LogDebug($"[PanResourceService] Catalog fetched from remote: categories={categories.Count}, items={items.Count}");
                return (categories, items);
            }
            catch (Exception ex)
            {
                // 弱网或服务端异常容错：若本地存在历史缓存（即使已过期），优雅降级返回本地缓存以保障可用性
                var fallbackCache = _memoryCache ?? TryLoadDiskCache();
                if (fallbackCache != null && fallbackCache.Items.Count > 0)
                {
                    LogDebug($"[PanResourceService] Remote fetch failed ({ex.Message}), falling back to disk cache (cached at {fallbackCache.CachedAtUtc:u})");
                    _memoryCache = fallbackCache;
                    return MapCacheEntryToResult(fallbackCache);
                }

                throw;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 获取网盘资源列表（指定分类与关键词）。
    /// </summary>
    public async Task<IReadOnlyList<PanResourceItem>> GetPanResourcesAsync(
        string category = "all",
        string? keyword = null,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        var (_, items) = await GetCatalogAsync(category, keyword, forceReload, cancellationToken);
        return items;
    }

    /// <summary>
    /// 兼容旧方法签名：获取网盘资源列表。
    /// </summary>
    public Task<IReadOnlyList<PanResourceItem>> GetPanResourcesAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default)
        => GetPanResourcesAsync("all", keyword, forceReload: false, cancellationToken);

    /// <summary>
    /// 手动清空内存与本地磁盘缓存。
    /// </summary>
    public void ClearCache()
    {
        _memoryCache = null;
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
                LogDebug($"[PanResourceService] Deleted disk cache file: {_cacheFilePath}");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[PanResourceService] Failed to clear disk cache: {ex.Message}");
        }
    }

    private bool IsCacheValid(PanCatalogCacheEntry? entry)
    {
        if (entry == null || entry.Items == null || entry.Items.Count == 0)
        {
            return false;
        }

        return (DateTime.UtcNow - entry.CachedAtUtc) < CacheDuration;
    }

    private PanCatalogCacheEntry? TryLoadDiskCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_cacheFilePath);
            var entry = JsonSerializer.Deserialize<PanCatalogCacheEntry>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entry != null && entry.Items != null && entry.Items.Count > 0)
            {
                return entry;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[PanResourceService] Failed to read disk cache: {ex.Message}");
        }

        return null;
    }

    private void SaveDiskCache(PanCatalogCacheEntry entry)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            AtomicFileWriter.WriteUtf8(_cacheFilePath, json);
            LogDebug($"[PanResourceService] Atomically saved {entry.Items.Count} items to disk cache: {_cacheFilePath}");
        }
        catch (Exception ex)
        {
            LogDebug($"[PanResourceService] Failed to write disk cache: {ex.Message}");
        }
    }

    private static (IReadOnlyList<PanCategoryOption> Categories, IReadOnlyList<PanResourceItem> Items) MapCacheEntryToResult(PanCatalogCacheEntry entry)
    {
        var categories = (entry.Categories ?? [])
            .Select(c => new PanCategoryOption(c.Key, c.Name))
            .ToList();

        var items = (entry.Items ?? [])
            .Select(MapDtoToItem)
            .ToList();

        return (categories, items);
    }

    private async Task<(List<WanPanCategoryDto> Categories, List<WanPanResourceDto> Items)> FetchFromRemoteAsync(
        string category,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var baseUrl = GetWanPanBaseUrl();
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            queryParams.Add($"category={Uri.EscapeDataString(category)}");
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            queryParams.Add($"keyword={Uri.EscapeDataString(keyword.Trim())}");
        }
        queryParams.Add("page=1");
        queryParams.Add("page_size=100");

        var url = $"{baseUrl}/api/v1/stardew/catalog?{string.Join("&", queryParams)}";
        LogDebug($"[PanResourceService] Fetching catalog url={url}");

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogDebug($"[PanResourceService] Fetch catalog failed: {(int)response.StatusCode}");
            throw new HttpRequestException($"网盘资源接口请求失败: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var catalogResponse = JsonSerializer.Deserialize<WanPanCatalogResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (catalogResponse == null || (catalogResponse.Code != 0 && catalogResponse.Code != 200) || catalogResponse.Data == null)
        {
            var msg = catalogResponse?.Msg;
            if (string.IsNullOrWhiteSpace(msg))
            {
                msg = "获取网盘资源目录失败";
            }
            LogDebug($"[PanResourceService] Catalog response error: {msg}");
            throw new InvalidOperationException(msg);
        }

        return (catalogResponse.Data.Categories ?? [], catalogResponse.Data.Items ?? []);
    }

    public static PanResourceItem MapDtoToItem(WanPanResourceDto dto)
    {
        return new PanResourceItem
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Summary,
            Source = !string.IsNullOrWhiteSpace(dto.CloudTypeName) ? dto.CloudTypeName : "网盘高速源",
            PanUrl = dto.DownloadUrl,
            Category = dto.Category,
            CategoryName = dto.CloudTypeName,
            Password = dto.Password,
            Version = dto.Version,
            Author = dto.Author,
            SizeFormatted = dto.SizeFormatted,
            IconUrl = dto.IconUrl,
            CloudType = dto.CloudType,
            CloudTypeName = dto.CloudTypeName,
            Tags = dto.Tags ?? [],
            SortOrder = dto.SortOrder,
            UpdatedAt = dto.UpdatedAt,
            IsRecommended = dto.SortOrder >= 80 || (dto.Tags != null && (dto.Tags.Contains("推荐") || dto.Tags.Contains("热门")))
        };
    }

    private void LogDebug(string message)
    {
        DebugLogger?.Invoke(message);
        Debug.WriteLine(message);
    }
}
