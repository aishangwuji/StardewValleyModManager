using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Converters;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SVL.Avalonia.ViewModels;

/// <summary>
/// 网盘资源页 ViewModel：卡片式、商店式呈现星露谷精选 Mod、整合包、SMAPI 运行库与游戏本体。
/// <para>每次进入页面或刷新时，通过网络自动调用北行搜接口获取最新资源并呈现卡片流。</para>
/// </summary>
public sealed partial class PanResourcePageViewModel : ObservableObject
{
    private readonly PanResourceService _panResourceService;
    private readonly LocalizationService? _localizationService;
    private readonly IExternalProcessService? _externalProcessService;
    private readonly HttpClient _httpClient;
    private int _loadGeneration;
    private readonly List<PanResourceItem> _allLoadedItems = [];

    public PanResourcePageViewModel()
        : this(new PanResourceService(), null, null)
    {
    }

    public PanResourcePageViewModel(
        PanResourceService panResourceService,
        LocalizationService? localizationService,
        IExternalProcessService? externalProcessService,
        HttpClient? httpClient = null)
    {
        _panResourceService = panResourceService;
        _localizationService = localizationService;
        _externalProcessService = externalProcessService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        InitDefaultCategories();

        if (_localizationService != null)
        {
            _localizationService.LanguageChanged += ApplyLocalizedTexts;
        }
        ApplyLocalizedTexts();
    }

    public string Title => "网盘资源";

    public string Description => "北行搜高速直通 · 收录精选模组、热门整合包、SMAPI运行库与游戏本体，免登录高速直达。";

    /// <summary>搜索过滤关键词。</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>当前选中的分类 Key（all / mods / modpacks / smapi / game）。</summary>
    [ObservableProperty]
    private string _selectedCategory = "all";

    /// <summary>列表状态提示（加载中/空态/结果数）。</summary>
    [ObservableProperty]
    private string _statusText = "正在加载网盘资源...";

    /// <summary>操作反馈提示（例如复制提取码提示）。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>错误信息。</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>是否正在加载。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>分类切换 Chips 集合。</summary>
    public ObservableCollection<PanCategoryOption> Categories { get; } = [];

    /// <summary>原始条目集合（向后兼容）。</summary>
    public ObservableCollection<PanResourceItem> Results { get; } = [];

    /// <summary>过滤后展示在卡片网格中的条目集合。</summary>
    public ObservableCollection<PanResourceItem> FilteredResults { get; } = [];

    public bool HasResults => FilteredResults.Count > 0;

    public bool IsEmpty => !IsLoading && !HasResults && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    private void InitDefaultCategories()
    {
        Categories.Clear();
        Categories.Add(new PanCategoryOption("all", "全部", "🌟", 0, isSelected: true));
        Categories.Add(new PanCategoryOption("mods", "精选模组", "🧩", 0));
        Categories.Add(new PanCategoryOption("modpacks", "整合包", "📦", 0));
        Categories.Add(new PanCategoryOption("smapi", "SMAPI运行库", "⚙️", 0));
        Categories.Add(new PanCategoryOption("game", "游戏本体", "🎮", 0));
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        UpdateCategorySelectionState();
        ApplyFilters();
    }

    partial void OnQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ApplyFilters();
        }
    }

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void UpdateCategorySelectionState()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = string.Equals(category.Key, SelectedCategory, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ApplyLocalizedTexts()
    {
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (IsLoading)
        {
            StatusText = "正在连接北行搜网盘资源库...";
        }
        else if (HasError)
        {
            StatusText = ErrorMessage;
        }
        else if (HasResults)
        {
            StatusText = $"共呈现 {FilteredResults.Count} 个精选资源";
        }
        else
        {
            StatusText = "未检索到匹配的网盘资源";
        }
    }

    /// <summary>页面首次进入或切换进来时自动调用拉取。</summary>
    public async Task InitializeAsync()
    {
        if (_allLoadedItems.Count == 0 || HasError)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync(forceReload: true);
    }

    [RelayCommand]
    private async Task Search()
    {
        ApplyFilters();

        // 若本地没有筛选出结果，且有关键词输入，尝试向服务端发起关键词检索
        if (FilteredResults.Count == 0 && !string.IsNullOrWhiteSpace(Query))
        {
            await LoadAsync(forceReload: true);
        }
    }

    [RelayCommand]
    private void SelectCategory(string? categoryKey)
    {
        var target = string.IsNullOrWhiteSpace(categoryKey) ? "all" : categoryKey.Trim();
        SelectedCategory = target;
    }

    [RelayCommand]
    private void ResetFilter()
    {
        Query = string.Empty;
        SelectedCategory = "all";
        ErrorMessage = string.Empty;
        ApplyFilters();
    }

    [RelayCommand]
    private async Task Retry()
    {
        await LoadAsync(forceReload: true);
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        UpdateStatusText();

        try
        {
            var keywordParam = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
            var (categories, items) = await _panResourceService.GetCatalogAsync(
                category: "all",
                keyword: keywordParam);

            if (generation != _loadGeneration)
            {
                return;
            }

            _allLoadedItems.Clear();
            _allLoadedItems.AddRange(items);

            // 更新分类计数
            UpdateCategoryCounts(_allLoadedItems);

            // 应用筛选
            ApplyFilters();

            // 启动异步图片缓存拉取
            _ = ResolveItemIconsAsync(_allLoadedItems, generation);
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            ErrorMessage = $"网盘资源加载失败: {ex.Message}";
            UpdateStatusText();
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoading = false;
                UpdateStatusText();
            }
        }
    }

    private void UpdateCategoryCounts(IReadOnlyList<PanResourceItem> items)
    {
        foreach (var category in Categories)
        {
            if (string.Equals(category.Key, "all", StringComparison.OrdinalIgnoreCase))
            {
                category.Count = items.Count;
            }
            else
            {
                category.Count = items.Count(item => string.Equals(item.Category, category.Key, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private void ApplyFilters()
    {
        var categoryFilter = SelectedCategory;
        var query = Query?.Trim() ?? string.Empty;

        var matched = _allLoadedItems.AsEnumerable();

        if (!string.Equals(categoryFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            matched = matched.Where(item => string.Equals(item.Category, categoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            matched = matched.Where(item =>
                (item.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Tags?.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false));
        }

        var matchedList = matched.ToList();

        Results.Clear();
        FilteredResults.Clear();

        foreach (var item in matchedList)
        {
            Results.Add(item);
            FilteredResults.Add(item);
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
        UpdateStatusText();
    }

    private async Task ResolveItemIconsAsync(IReadOnlyList<PanResourceItem> items, int generation)
    {
        foreach (var item in items)
        {
            if (generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            var fallback = item.Category?.ToLowerInvariant() switch
            {
                "game" => "avares://SVL.Avalonia/Assets/Icons/Generated/Cropped/Gamepad.png",
                "smapi" => "avares://SVL.Avalonia/Assets/Icons/Generated/Cropped/Package.png",
                "modpacks" => "avares://SVL.Avalonia/Assets/Icons/Generated/Cropped/FolderOpen.png",
                _ => "avares://SVL.Avalonia/Assets/Icons/Generated/Cropped/Package.png"
            };

            if (string.IsNullOrWhiteSpace(item.IconUrl) ||
                !Uri.TryCreate(item.IconUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                item.IconSource = fallback;
                continue;
            }

            var cachePath = AssetImageConverter.GetIconCachePath(item.IconUrl);
            if (File.Exists(cachePath))
            {
                item.IconSource = cachePath;
                continue;
            }

            item.IconSource = fallback;

            try
            {
                var dir = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var response = await _httpClient.GetAsync(uri);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(cachePath, bytes);
                    if (generation == Volatile.Read(ref _loadGeneration))
                    {
                        item.IconSource = cachePath;
                    }
                }
            }
            catch
            {
                // 网络图标拉取失败保持 fallback，不阻断主流程
            }
        }
    }

    /// <summary>打开网盘链接并自动复制提取码（如果有）。</summary>
    [RelayCommand]
    private async Task OpenPanLink(PanResourceItem? item)
    {
        var url = item?.PanUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (item != null && item.HasPassword)
        {
            await CopyToClipboardAsync(item.Password);
            StatusMessage = $"已复制提取码【{item.Password}】，正在打开网盘页面...";
        }
        else
        {
            StatusMessage = "正在打开网盘页面...";
        }

        _externalProcessService?.TryOpenUrl(url);
    }

    /// <summary>一键复制提取码。</summary>
    [RelayCommand]
    private async Task CopyPassword(PanResourceItem? item)
    {
        if (item == null || !item.HasPassword)
        {
            return;
        }

        await CopyToClipboardAsync(item.Password);
        StatusMessage = $"提取码【{item.Password}】已复制到剪贴板！";
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var clipboard = GetClipboard();
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch
        {
            // 忽略剪贴板访问异常
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }
}
