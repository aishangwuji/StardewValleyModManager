using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SVL.Avalonia.Models;

/// <summary>
/// 星露谷网盘资源目录接口响应根对象：GET /api/v1/stardew/catalog
/// </summary>
public sealed class WanPanCatalogResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public WanPanCatalogData? Data { get; set; }
}

/// <summary>
/// 目录接口数据负载
/// </summary>
public sealed class WanPanCatalogData
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("categories")]
    public List<WanPanCategoryDto> Categories { get; set; } = [];

    [JsonPropertyName("items")]
    public List<WanPanResourceDto> Items { get; set; } = [];
}

/// <summary>
/// 分类元数据项
/// </summary>
public sealed class WanPanCategoryDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 单个星露谷网盘资源条目 DTO
/// </summary>
public sealed class WanPanResourceDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("cloud_type")]
    public string CloudType { get; set; } = string.Empty;

    [JsonPropertyName("cloud_type_name")]
    public string CloudTypeName { get; set; } = string.Empty;

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("size_formatted")]
    public string SizeFormatted { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("resource_id")]
    public string ResourceId { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// 单条资源详情响应根对象：GET /api/v1/stardew/items/{id}
/// </summary>
public sealed class WanPanItemDetailResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public WanPanResourceDto? Data { get; set; }
}

/// <summary>
/// 星露谷网盘目录本地持久化缓存条目，包含缓存生成时间与分类及条目列表。
/// </summary>
public sealed class PanCatalogCacheEntry
{
    [JsonPropertyName("cached_at_utc")]
    public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("categories")]
    public List<WanPanCategoryDto> Categories { get; set; } = [];

    [JsonPropertyName("items")]
    public List<WanPanResourceDto> Items { get; set; } = [];
}

