using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using System.Collections.ObjectModel;
using System.Threading;

namespace SVL.Avalonia.ViewModels;

/// <summary>
/// 网盘资源页 ViewModel：展示 Mod 列表，网盘链接由 <see cref="PanResourceService"/> 统一提供。
/// <para>Reason: 与其他项目（网盘链接供给方）的联调尚未开始，页面先完成后端为空时的空态/加载态展示；
/// 接口接通后本 ViewModel 无需改动，数据会自动流入 <see cref="Results"/>。</para>
/// </summary>
public sealed partial class PanResourcePageViewModel : ObservableObject
{
    private readonly PanResourceService _panResourceService;
    private readonly LocalizationService? _localizationService;
    private readonly IExternalProcessService? _externalProcessService;
    private int _loadGeneration;

    /// <summary>无参构造保留用于设计时/单元测试；运行时请使用带服务参数的构造。</summary>
    public PanResourcePageViewModel()
        : this(new PanResourceService(), null, null)
    {
    }

    public PanResourcePageViewModel(
        PanResourceService panResourceService,
        LocalizationService? localizationService,
        IExternalProcessService? externalProcessService)
    {
        _panResourceService = panResourceService;
        _localizationService = localizationService;
        _externalProcessService = externalProcessService;
        if (_localizationService != null)
        {
            _localizationService.LanguageChanged += ApplyLocalizedTexts;
        }
        ApplyLocalizedTexts();
    }

    public string Title => "网盘资源";

    public string Description => "网盘 Mod 资源展示：后续由外部接口统一提供网盘链接。";

    /// <summary>搜索关键词（透传给后续的外部接口）。</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>列表状态提示（加载中/空态/结果数）。</summary>
    [ObservableProperty]
    private string _statusText = "暂无网盘资源，接口联调后将在这里展示 Mod 列表。";

    /// <summary>是否正在加载（驱动页面 loading 态）。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>网盘资源条目（绑定到页面列表）。</summary>
    public ObservableCollection<PanResourceItem> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    private void ApplyLocalizedTexts()
    {
        // 页面内部文案暂用中文 fallback；若后续需要国际化，在此经 LocalizationService 刷新。
        StatusText = HasResults
            ? $"已加载 {Results.Count} 个网盘资源"
            : "暂无网盘资源，接口联调后将在这里展示 Mod 列表。";
    }

    /// <summary>页面首次进入时触发加载（当前后端为空，直接呈现空态）。</summary>
    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Search()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        IsLoading = true;
        StatusText = "正在加载网盘资源...";
        try
        {
            var items = await _panResourceService.GetPanResourcesAsync(Query);
            if (generation != _loadGeneration)
            {
                return;
            }

            Results.Clear();
            foreach (var item in items)
            {
                Results.Add(item);
            }
            OnPropertyChanged(nameof(HasResults));
            ApplyLocalizedTexts();
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            StatusText = $"网盘资源加载失败: {ex.Message}";
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>打开网盘链接（浏览器）。空链接直接忽略，避免误弹浏览器。</summary>
    [RelayCommand]
    private void OpenPanLink(PanResourceItem? item)
    {
        var url = item?.PanUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _externalProcessService?.TryOpenUrl(url);
    }
}
