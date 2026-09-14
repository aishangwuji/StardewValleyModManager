using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using SVL.Core.Platform.Modpack;
using SVL.Core.Platform.Services;

namespace SVL.Avalonia.ViewModels;

/// <summary>
/// 主窗口导航中枢（Navigation Hub）。
/// <para>职责：管理顶栏 5 个一级页面（启动/Mod管理/下载/任务/设置）与 3 个二级页面（实例/版本设置/资源详情）的栈式导航。</para>
/// <para>历史重命名：顶栏“Mod管理”在 2026-01 前显示为“本地Mod管理”，为保持导航标识稳定，内部 CurrentPage 仍沿用 "本地Mod管理" 作为一级页面 key，显示文本通过 LocalizationService(Nav.LocalModManage) 控制，zh-CN 现为 "Mod管理"，en-US 为 "Local Mods"。</para>
/// <para>Business Rule: 一级页面切换需清空返回栈（clearBackStack），二级页面需压栈（pushCurrentToBackStack），确保左上角 Logo/返回按钮与面包屑一致。</para>
/// <para>Reason: Avalonia 无内置导航框架，手动维护 CurrentPage + CurrentPageViewModel + _backStack 避免页面状态丢失。</para>
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IPlatformInfoService _platformInfoService;
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly AppUserSettingsStore _settingsStore;
    private readonly LocalizationService _localizationService;
    private readonly ImageResourceService _imageResourceService;
    private readonly DialogService _dialogService;
    private readonly LauncherUpdateService _launcherUpdateService;
    private readonly Stack<(string Page, ObservableObject ViewModel)> _backStack = new();
    private readonly HashSet<Models.DownloadTaskItem> _failureDialogsShown = [];
    private Models.DownloadTaskItem? _currentDownloadTask;

    public LaunchPageViewModel LaunchPage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public InstancesPageViewModel InstancesPage { get; }

    /// <summary>SMAPI 下载服务（持有引用用于 NXM 回调路由：SMAPI 专用 NXM 链接优先交给它处理）。</summary>
    public Services.SmapiDownloadService SmapiDownloadService { get; }

    /// <summary>通用浏览器下载回退服务（非 Premium 用户 NXM 回调等待，支持普通 Mod）。</summary>
    public Services.BrowserDownloadFallbackService BrowserDownloadFallbackService { get; }

    public TaskStatusPageViewModel TaskStatusPage { get; }

    public ModSearchPageViewModel ModSearchPage { get; }

    public ModpackSearchPageViewModel ModpackSearchPage { get; }

    public ModDetailsPageViewModel ModDetailsPage { get; }

    public VersionSettingsPageViewModel VersionSettingsPage { get; }

    public InstanceSettingsPageViewModel InstanceSettingsPage { get; }

    /// <summary>当前页面标识（启动/本地Mod管理[显示为"Mod管理"]/下载/任务/设置/实例/资源详情），驱动 Is*Page 与 Header 状态。</summary>
    /// <remarks>显示名与内部 key 分离：UI 显示取自 LocalizationService(Nav.LocalModManage) 的 "Mod管理"，此处 "本地Mod管理" 仅为稳定的内部导航 key，勿与显示文本混用；IsLocalModManagePage 亦基于此 key 判定。</remarks>
    [ObservableProperty]
    private string _currentPage = "启动";

    /// <summary>当前页面对应的 ViewModel 实例，由 DataTemplates 映射到具体 View。</summary>
    [ObservableProperty]
    private ObservableObject? _currentPageViewModel;

    [ObservableProperty]
    private string _windowTitle = "Stardew Valley Launcher";

    /// <summary>顶栏“启动”文案（来自 LocalizationService Nav.Launch）。</summary>
    [ObservableProperty]
    private string _navLaunchText = "启动";

    /// <summary>顶栏“Mod管理”文案（Nav.LocalModManage），位于启动与下载之间。</summary>
    /// <remarks>2026-01 由 "本地Mod管理" 精简为 "Mod管理"；实际显示由 LocalizationService 决定，此处仅为 fallback 默认值。</remarks>
    [ObservableProperty]
    private string _navLocalModManageText = "Mod管理";

    /// <summary>顶栏“下载”文案。</summary>
    [ObservableProperty]
    private string _navDownloadText = "下载";

    [ObservableProperty]
    private string _navTasksText = "任务";

    [ObservableProperty]
    private string _navSettingsText = "设置";

    [ObservableProperty]
    private string _navLaunchIconSource = string.Empty;

    [ObservableProperty]
    private string _navDownloadIconSource = string.Empty;

    [ObservableProperty]
    private string _navTasksIconSource = string.Empty;

    [ObservableProperty]
    private string _navSettingsIconSource = string.Empty;

    [ObservableProperty]
    private string _brandJunimoIconSource = string.Empty;

    [ObservableProperty]
    private bool _showTaskNavNotification;

    [ObservableProperty]
    private bool _showTaskNavSoftHint;

    [ObservableProperty]
    private bool _showDownloadFloatingTaskButton;

    [ObservableProperty]
    private int _floatingTaskBadgeCount;

    [ObservableProperty]
    private string _launcherAppNameText = "SVL";

    [ObservableProperty]
    private string _resourceDetailsHeaderTitle = "资源下载";

    [ObservableProperty]
    private string _sidebarCurrentPageText = "当前页面";

    [ObservableProperty]
    private string _sidebarMigrationStatusText = "迁移状态";

    [ObservableProperty]
    private string _sidebarMigrationPoint1 = "- 保留全部导航结构";

    [ObservableProperty]
    private string _sidebarMigrationPoint2 = "- 保留全部功能域（启动/下载/任务/设置）";

    [ObservableProperty]
    private string _sidebarMigrationPoint3 = "- 正在逐页迁移原 WPF 视图";

    [ObservableProperty]
    private string _sidebarPathDetectText = "路径探测";

    public string PlatformText => _platformInfoService.GetPlatformDisplayName();

    public string SteamPathPreview => _gameInstallPathLocator.TryLocateSteamStardewPath() ?? "未探测到（可手动选择）";

    public string GogPathPreview => _gameInstallPathLocator.TryLocateGogStardewPath() ?? "未探测到（可手动选择）";

    public string XboxPathPreview => _gameInstallPathLocator.TryLocateXboxStardewPath() ?? "未探测到（可手动选择）";

    /// <summary>是否为一级页面“启动”，用于顶栏高亮与 Logo 显示判定。</summary>
    public bool IsLaunchPage => string.Equals(CurrentPage, "启动", StringComparison.Ordinal);

    /// <summary>是否为一级页面“Mod管理”（内部 key 仍为 "本地Mod管理"，复用 VersionSettingsPage 视图），顶栏位于启动与下载之间。</summary>
    /// <remarks>显示名已改为 "Mod管理"(Nav.LocalModManage)，但为保持 IsLocalModManagePage 与历史测试/导航逻辑兼容，判定仍使用稳定的内部 key "本地Mod管理"。</remarks>
    public bool IsLocalModManagePage => string.Equals(CurrentPage, "本地Mod管理", StringComparison.Ordinal);

    /// <summary>是否为一级页面“下载”。</summary>
    public bool IsDownloadPage => string.Equals(CurrentPage, "下载", StringComparison.Ordinal);

    /// <summary>是否为一级页面“任务”。</summary>
    public bool IsTasksPage => string.Equals(CurrentPage, "任务", StringComparison.Ordinal);

    /// <summary>是否为一级页面“设置”。</summary>
    public bool IsSettingsPage => string.Equals(CurrentPage, "设置", StringComparison.Ordinal);

    /// <summary>是否显示 Windows 标题栏控制按钮（仅 Windows）。</summary>
    public bool ShowWindowControlButtons => OperatingSystem.IsWindows();

    /// <summary>是否显示左上角返回按钮。Reason: 仅二级页面（实例/版本设置/资源详情）且返回栈非空时显示，一级页面始终显示 Logo 避免跳动。</summary>
    public bool ShowBackButton => IsBackPage(CurrentPage) && _backStack.Count > 0;

    /// <summary>是否显示资源详情专用标题（替代 Logo）。</summary>
    public bool ShowResourceDetailHeaderTitle => IsResourceDetailsPage;

    /// <summary>是否显示品牌 Logo（Junimo + SVL）。Business Rule: 非返回页且非资源详情页时显示。</summary>
    public bool ShowBrandIdentity => !ShowBackButton && !ShowResourceDetailHeaderTitle;

    private bool IsResourceDetailsPage => string.Equals(CurrentPage, "资源详情", StringComparison.Ordinal);

    public MainWindowViewModel()
    {
        _platformInfoService = new PlatformInfoService();
        _gameInstallPathLocator = new GameInstallPathLocator();
        var externalProcessService = new ExternalProcessService();

        _settingsStore = new AppUserSettingsStore();
        _localizationService = new LocalizationService(_settingsStore);
        _imageResourceService = new ImageResourceService(_localizationService);
        _localizationService.LanguageChanged += ApplyLocalizedTexts;
        _imageResourceService.ResourcesChanged += ApplyImageResources;
        var initialSettings = _settingsStore.Load();
        LauncherAppNameText = string.IsNullOrWhiteSpace(initialSettings.LauncherAppName) ? "SVL" : initialSettings.LauncherAppName;
        ApplyLocalizedTexts();
        ApplyImageResources();

        var dialogService = new DialogService();
        _dialogService = dialogService;
        var nexusAuthService = new NexusAuthService();
        var nexusOAuthService = new NexusOAuthService();
        var httpDownloadService = new HttpDownloadService(_settingsStore);
        var nexusModDownloadResolverService = new NexusModDownloadResolverService();
        var downloadInstallService = new DownloadInstallService(_gameInstallPathLocator);
        var nxmLinkParser = new NxmLinkParser();
        var nxmProtocolRegistrationService = new NxmProtocolRegistrationService();
        var smapiInstallService = new SVL.Avalonia.Services.SmapiInstallService();
        var smapiDownloadService = new SVL.Avalonia.Services.SmapiDownloadService(
            httpDownloadService, nxmLinkParser, nexusModDownloadResolverService);
        SmapiDownloadService = smapiDownloadService;
        var browserDownloadFallbackService = new SVL.Avalonia.Services.BrowserDownloadFallbackService(nxmLinkParser, externalProcessService);
        BrowserDownloadFallbackService = browserDownloadFallbackService;
        var downloadTaskStateStore = new DownloadTaskStateStore();
        var retryDiffReportService = new RetryDiffReportService();
        var instanceRegistryStore = new InstanceRegistryStore();
        var remoteCatalogService = new RemoteCatalogService(_settingsStore);
        var communityLocalizationService = new SVL.Avalonia.Services.CommunityLocalizationService(_settingsStore);
        remoteCatalogService.SetLocalizationService(communityLocalizationService);
        var modpackInstallService = new SVL.Avalonia.Services.ModpackInstallService(
            _gameInstallPathLocator, smapiInstallService, httpDownloadService, remoteCatalogService,
            _settingsStore, nexusModDownloadResolverService, nxmLinkParser, browserDownloadFallbackService);
        var collectionInstallService = new SVL.Avalonia.Services.CollectionInstallService(
            _gameInstallPathLocator, smapiInstallService, httpDownloadService, remoteCatalogService,
            _settingsStore, nexusModDownloadResolverService, nxmLinkParser, browserDownloadFallbackService,
            modpackInstallService);
        var launcherUpdateService = new LauncherUpdateService();
        _launcherUpdateService = launcherUpdateService;
        LaunchPage = new LaunchPageViewModel(_gameInstallPathLocator, externalProcessService, _settingsStore, _localizationService, _imageResourceService);
        DownloadPage = new DownloadPageViewModel(
            _localizationService,
            _imageResourceService,
            nxmLinkParser,
            _gameInstallPathLocator,
            _settingsStore,
            dialogService,
            httpDownloadService,
            nexusModDownloadResolverService,
            downloadInstallService,
            smapiInstallService,
            browserDownloadFallbackService,
            remoteCatalogService,
            downloadTaskStateStore,
            retryDiffReportService,
            modpackInstallService,
            collectionInstallService);
        SettingsPage = new SettingsPageViewModel(_settingsStore, dialogService, nexusAuthService, nexusOAuthService, launcherUpdateService, externalProcessService, nxmProtocolRegistrationService, _localizationService, _imageResourceService);
        InstancesPage = new InstancesPageViewModel(_gameInstallPathLocator, dialogService, instanceRegistryStore, _settingsStore, _imageResourceService, _localizationService);
        TaskStatusPage = new TaskStatusPageViewModel(_localizationService);
        ModSearchPage = new ModSearchPageViewModel(remoteCatalogService);
        ModpackSearchPage = new ModpackSearchPageViewModel(remoteCatalogService);
        ModDetailsPage = new ModDetailsPageViewModel(remoteCatalogService, dialogService);
        ModDetailsPage.QueueDownloadRequested += HandleQueueDownload;
        // 注入 Mods 路径解析器：用于检测 Mod 是否已安装（扫描 Mods 目录 manifest.json）
        ModDetailsPage.CurrentModsPathResolver = () => DownloadPage.GetCurrentModsPath();
        VersionSettingsPage = new VersionSettingsPageViewModel(_settingsStore, _gameInstallPathLocator, _localizationService, _imageResourceService, dialogService, remoteCatalogService, smapiInstallService, smapiDownloadService, communityLocalizationService);
        // 注入路径列表提供者：SMAPI 安装对话框可从版本选择页面的 Base 路径列表中选择安装目标
        // 参考旧架构 GamePathConfirmDialog.LoadGamePaths：只提供 Base 路径，过滤掉版本隔离子目录
        // 路径列表提供者：供 SMAPI 安装对话框和 Collection 安装流程使用
        // 参考旧架构 ModpackDropDialogViewModel.LoadGamePaths：只提供 Base 路径，过滤版本隔离子目录
        // 旧架构通过 Tags.Contains("Base") 过滤，新架构通过 IsBaseInstance 标志 + 子目录过滤双重保障
        Func<IReadOnlyList<string>> basePathsProvider = () =>
        {
            if (!InstancesPage.HasPathEntries)
            {
                InstancesPage.RefreshFromSettingsChange();
            }

            // 参考旧架构：从所有实例中筛选 Base 实例（IsBaseInstance == true，等价于 Tags.Contains("Base")）
            var basePaths = InstancesPage.PathEntries
                .SelectMany(p => p.Instances)
                .Where(i => i.IsBaseInstance)
                .Select(i => i.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p) && System.IO.Directory.Exists(p))
                .Select(p => p.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 补充 PreferredInstancePath（用户在设置中指定的首选路径）
            var preferredPath = ResolveBasePathForInstance(_settingsStore.Load().PreferredInstancePath);
            if (!string.IsNullOrWhiteSpace(preferredPath) && System.IO.Directory.Exists(preferredPath))
            {
                var normalizedPreferred = preferredPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                if (!basePaths.Any(p => string.Equals(p, normalizedPreferred, StringComparison.OrdinalIgnoreCase)))
                {
                    basePaths.Add(normalizedPreferred);
                }
            }

            if (basePaths.Count <= 1)
            {
                return basePaths;
            }

            // 过滤掉是其他路径子目录的路径（版本隔离目录可能也被识别为 Base 实例）
            return basePaths
                .Where(p => !basePaths.Any(other =>
                    !string.Equals(other, p, StringComparison.OrdinalIgnoreCase) &&
                    p.StartsWith(other + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        };

        VersionSettingsPage.AvailableGamePathsProvider = basePathsProvider;
        DownloadPage.AvailableGamePathsProvider = basePathsProvider;
        DownloadPage.AvailableModInstancesProvider = () =>
        {
            if (!InstancesPage.HasPathEntries)
            {
                InstancesPage.RefreshFromSettingsChange();
            }

            return InstancesPage.PathEntries
                .SelectMany(pathEntry => pathEntry.Instances
                    .Where(instance => instance.IsSmapiInstance &&
                                       !string.IsNullOrWhiteSpace(instance.Path) &&
                                       System.IO.Directory.Exists(instance.Path))
                    .Select(instance => new ModInstallTarget(
                        instance.Name,
                        instance.Path,
                        pathEntry.GamePath,
                        instance.IsBaseInstance,
                        instance.SmapiVersion)))
                .GroupBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        };
        VersionSettingsPage.OpenDetailsRequested += HandleOpenDetailsFromModManage;
        VersionSettingsPage.BatchUpdateModsRequested += HandleBatchUpdateModsRequested;
        InstanceSettingsPage = new InstanceSettingsPageViewModel(_settingsStore, dialogService);
        LaunchPage.NavigateToInstancesRequested += HandleNavigateToInstances;
        LaunchPage.NavigateToVersionSettingsRequested += HandleNavigateToVersionSettings;
        LaunchPage.NavigateToModManageRequested += HandleNavigateToModManage;
        VersionSettingsPage.InstanceContextChanged += HandleInstanceContextChanged;
        VersionSettingsPage.SmapiInstallTaskCreated += HandleSmapiInstallTaskCreated;
        VersionSettingsPage.RequestReturnToLaunch += () => NavigateToPage("启动", LaunchPage, clearBackStack: true);
        InstancesPage.InstanceActivated += HandleInstanceActivated;
        InstancesPage.InstanceSettingsRequested += HandleInstanceSettingsRequested;
        InstancesPage.ModpackImportRequested += HandleModpackImportRequested;
        DownloadPage.TaskSelected += HandleTaskSelected;
        DownloadPage.TaskStateChanged += HandleTaskStateChanged;
        DownloadPage.TaskLogGenerated += HandleTaskLogGenerated;
        DownloadPage.NavigateToTaskStatusRequested += HandleNavigateToTaskStatus;
        DownloadPage.NavigateToInstancesRequested += HandleNavigateToInstances;
        DownloadPage.NavigateToModSearchRequested += HandleNavigateToModSearch;
        DownloadPage.NavigateToModpackSearchRequested += HandleNavigateToModpackSearch;
        DownloadPage.NavigateToSettingsRequested += HandleNavigateToSettingsForNexusLogin;
        DownloadPage.OpenDetailsRequested += HandleOpenDetails;
        DownloadPage.OpenStructuredDetailsRequested += HandleOpenDetailsFromSearch;
        // SMAPI/Modpack/Collection 安装成功后刷新 LaunchPage/InstancesPage 实例图标
        DownloadPage.InstanceContextChanged += HandleInstanceContextChanged;
        // 任务状态页统一视图：任务操作事件转发到 DownloadPage 执行
        TaskStatusPage.RetryFailedItemsRequested += HandleRetryFailedItemsRequested;
        TaskStatusPage.NavigateToDownloadRequested += HandleNavigateToDownload;
        TaskStatusPage.CancelTaskRequested += HandleCancelTaskRequested;
        TaskStatusPage.RetryTaskRequested += HandleRetryTaskRequested;
        TaskStatusPage.RemoveTaskRequested += HandleRemoveTaskRequested;
        TaskStatusPage.OpenDirectoryRequested += HandleOpenDirectoryRequested;
        TaskStatusPage.OpenReportRequested += HandleOpenReportRequested;
        TaskStatusPage.OpenRetryReportRequested += HandleOpenRetryReportRequested;
        TaskStatusPage.ClearCompletedRequested += HandleClearCompletedRequested;
        ModSearchPage.OpenDetailsRequested += HandleOpenDetailsFromSearch;
        ModpackSearchPage.OpenDetailsRequested += HandleOpenDetailsFromSearch;
        SettingsPage.PropertyChanged += HandleSettingsPropertyChanged;
        SettingsPage.TakeoverDownloadRequested += HandleTakeoverDownloadRequested;
        SettingsPage.NexusLoggedOut += HandleSettingsNexusLoggedOut;

        CurrentPageViewModel = LaunchPage;
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowResourceDetailHeaderTitle));
        OnPropertyChanged(nameof(ShowBrandIdentity));
        RefreshFloatingTaskButtonState();

        // 冷启动后初始同步任务列表，确保历史任务在任务页立即可见
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);

        // 启动时按设置自动检查启动器更新（延迟 2 秒避免与初始化抢资源）
        _ = PerformAutoUpdateCheckAsync();
    }

    /// <summary>
    /// 主窗口显示后恢复下载页持久化的 Pending 任务。
    /// 延迟到窗口显示完成，确保需要补充交互的任务拥有真实主窗口作为对话框宿主。
    /// </summary>
    public void ResumePendingDownloadTasks()
    {
        DownloadPage.ResumePendingTasks();
    }

    /// <summary>启动时自动检查更新：仅当 EnableAutoUpdateCheck 且未跳过该版本时弹窗。</summary>
    private async Task PerformAutoUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(2000);

            var settings = _settingsStore.Load();
            if (!settings.EnableAutoUpdateCheck)
            {
                return;
            }

            var includePrerelease = settings.UpdateChannel?.Contains("pre", StringComparison.OrdinalIgnoreCase) ?? false;
            var preferGitee = string.Equals(settings.PreferredUpdateSource, "Gitee", StringComparison.OrdinalIgnoreCase);

            var result = await _launcherUpdateService.CheckForUpdateAsync(includePrerelease, preferGitee);
            if (!result.Success || !result.HasUpdate || result.ReleaseInfo == null)
            {
                return;
            }

            var releaseTag = result.ReleaseInfo.TagName;
            if (!string.IsNullOrWhiteSpace(settings.SkippedLauncherVersion) &&
                string.Equals(settings.SkippedLauncherVersion, releaseTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 复用 SettingsPage 的弹窗逻辑：通过事件请求 SettingsPage 弹出更新对话框
            await SettingsPage.ShowUpdateDialogFromAutoCheckAsync(result);
        }
        catch
        {
            // 自动检查失败不应影响启动器正常使用
        }
    }

    private void ApplyLocalizedTexts()
    {
        WindowTitle = _localizationService.Get("Window.Title");
        NavLaunchText = _localizationService.Get("Nav.Launch");
        NavLocalModManageText = _localizationService.Get("Nav.LocalModManage");
        NavDownloadText = _localizationService.Get("Nav.Download");
        NavTasksText = _localizationService.Get("Nav.Tasks");
        NavSettingsText = _localizationService.Get("Nav.Settings");
        SidebarCurrentPageText = _localizationService.Get("Sidebar.CurrentPage");
        SidebarMigrationStatusText = _localizationService.Get("Sidebar.MigrationStatus");
        SidebarMigrationPoint1 = _localizationService.Get("Sidebar.MigrationPoint1");
        SidebarMigrationPoint2 = _localizationService.Get("Sidebar.MigrationPoint2");
        SidebarMigrationPoint3 = _localizationService.Get("Sidebar.MigrationPoint3");
        SidebarPathDetectText = _localizationService.Get("Sidebar.PathDetect");
    }

    private void ApplyImageResources()
    {
        BrandJunimoIconSource = _imageResourceService.Get("header.brand.junimo");
        NavLaunchIconSource = _imageResourceService.Get("nav.launch");
        NavDownloadIconSource = _imageResourceService.Get("nav.download");
        NavTasksIconSource = _imageResourceService.Get("nav.tasks");
        NavSettingsIconSource = _imageResourceService.Get("nav.settings");
    }

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsLaunchPage));
        OnPropertyChanged(nameof(IsLocalModManagePage));
        OnPropertyChanged(nameof(IsDownloadPage));
        OnPropertyChanged(nameof(IsTasksPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowResourceDetailHeaderTitle));
        OnPropertyChanged(nameof(ShowBrandIdentity));
        RefreshTaskNavNotification();
        RefreshFloatingTaskButtonState();
    }

    private void HandleNavigateToInstances()
    {
        NavigateToPage("实例", InstancesPage, pushCurrentToBackStack: true);
    }

    private void HandleNavigateToVersionSettings()
    {
        VersionSettingsPage.ReloadFromSettings();
        VersionSettingsPage.SwitchToGeneral();
        // 预加载版本选择页面的路径列表，确保 SMAPI 安装对话框能获取所有 Base 路径
        if (!InstancesPage.HasPathEntries)
        {
            InstancesPage.RefreshFromSettingsChange();
        }
        NavigateToPage("版本设置", VersionSettingsPage, pushCurrentToBackStack: true);
    }

    private void HandleNavigateToModManage()
    {
        VersionSettingsPage.ReloadFromSettings(reloadModsWhenActive: true);
        VersionSettingsPage.SwitchToModManage();
        NavigateToPage("本地Mod管理", VersionSettingsPage, clearBackStack: true);
    }

    private void HandleInstanceContextChanged()
    {
        LaunchPage.RefreshFromSettingsAndEnvironment();
        InstancesPage.RefreshFromSettingsChange();
        // 整合包安装会更新当前实例路径；若用户正停留在 Mod 管理页，必须同步重新读取
        // 新实例的 Mods，而不是保留安装前页面缓存的清单结果。
        VersionSettingsPage.ReloadFromSettings(reloadModsWhenActive: VersionSettingsPage.IsModManageSection);
    }

    private void HandleSmapiInstallTaskCreated(Models.DownloadTaskItem taskItem)
    {
        // 版本设置页会在这里之后直接执行安装，不能调用 EnqueueTask（会重复执行）；
        // 但仍通过 DownloadPage 的外部任务入口统一挂载持久化和任务列表监听。
        DownloadPage.RegisterExternalTask(taskItem, $"SMAPI 安装任务已创建: {taskItem.Name}");
        // RegisterExternalTask 只负责加入 DownloadPage；这里同步任务页集合，
        // 否则首次从版本设置发起 SMAPI 安装时，右侧虽有状态文字，左侧列表仍为空。
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        UpdateTaskStatusOverview();
        RefreshTaskNavNotification();
        NavigateToPage("任务", TaskStatusPage, pushCurrentToBackStack: true);
        TaskStatusPage.SetCurrentTask(taskItem);

        // 监听任务状态变化以更新导航通知
        taskItem.PropertyChanged += (_, args) =>
        {
            void RefreshExternalTaskPresentation()
            {
                RefreshTaskNavNotification();
                TaskStatusPage.SetCurrentTask(taskItem);
            }

            if (!string.Equals(args.PropertyName, nameof(Models.DownloadTaskItem.Status), StringComparison.Ordinal))
            {
                return;
            }

            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                RefreshExternalTaskPresentation();
            }
            else
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshExternalTaskPresentation);
            }
        };
    }

    private void HandleInstanceActivated(InstanceItem _)
    {
        LaunchPage.RefreshFromSettingsAndEnvironment();
        // 实例切换后 Mod 安装状态可能变化（如从原版切到 SMAPI 实例），刷新详情页按钮文本
        ModDetailsPage.RefreshInstallActionTexts();
        NavigateToPage("启动", LaunchPage, clearBackStack: true);
    }

    private void HandleInstanceSettingsRequested(InstanceItem _)
    {
        VersionSettingsPage.ReloadFromSettings();
        VersionSettingsPage.SwitchToOverview();
        NavigateToPage("版本设置", VersionSettingsPage, pushCurrentToBackStack: true);
    }

    private void HandleModpackImportRequested()
    {
        DownloadPage.OpenModpackSearchPageCommand.Execute(null);
    }

    private void HandleTaskSelected(Models.DownloadTaskItem task)
    {
        _currentDownloadTask = task;
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        TaskStatusPage.SetCurrentTask(task);
        UpdateTaskStatusOverview();
        RefreshTaskNavNotification();
    }

    private void HandleTaskStateChanged(Models.DownloadTaskItem task)
    {
        // 下载/安装队列可能从线程池线程发出状态事件；任务页会更新
        // ObservableCollection，必须在 UI 线程处理整个事件，而不是只切进度回调。
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleTaskStateChanged(task));
            return;
        }

        _currentDownloadTask = task;

        if (task.TaskState == Models.DownloadTaskState.Pending)
        {
            // 允许同一任务重试后再次显示失败汇总。
            _failureDialogsShown.Remove(task);
        }
        else if (task.IsFailed &&
                 (task.TaskAction is Models.DownloadTaskAction.InstallModpack or
                     Models.DownloadTaskAction.InstallCollection) &&
                 _failureDialogsShown.Add(task))
        {
            _ = ShowModpackFailureDialogAsync(task);
        }

        // 仅当任务列表结构变化（新增/删除/状态类型变化）时才同步列表，
        // 避免下载进度回调频繁 Clear+Add 导致整个列表重绘闪烁。
        var currentState = task.TaskState;
        var lastState = task.PreviousSyncedState;
        var stateChanged = currentState != lastState;
        if (stateChanged)
        {
            task.PreviousSyncedState = currentState;
            TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        }

        if (TaskStatusPage.SelectedTask == task)
        {
            TaskStatusPage.SetCurrentTask(task);
        }

        // 概览/通知仅在状态类型变化时刷新，避免进度回调频闪
        if (stateChanged)
        {
            UpdateTaskStatusOverview();
            RefreshTaskNavNotification();
        }
    }

    private void HandleRetryFailedItemsRequested()
    {
        if (_currentDownloadTask == null)
        {
            TaskStatusPage.AddLog("没有可重试的当前任务");
            return;
        }

        DownloadPage.RetryTaskCommand.Execute(_currentDownloadTask);
    }

    // 任务状态页统一视图：操作事件转发到 DownloadPage 执行
    private void HandleCancelTaskRequested(Models.DownloadTaskItem task)
    {
        DownloadPage.CancelTaskCommand.Execute(task);
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
    }

    private void HandleRetryTaskRequested(Models.DownloadTaskItem task)
    {
        DownloadPage.RetryTaskCommand.Execute(task);
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
    }

    private void HandleRemoveTaskRequested(Models.DownloadTaskItem task)
    {
        DownloadPage.RemoveTaskCommand.Execute(task);
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        _failureDialogsShown.Remove(task);
        if (_currentDownloadTask == task)
        {
            _currentDownloadTask = null;
        }
        UpdateTaskStatusOverview();
    }

    private void HandleOpenDirectoryRequested(Models.DownloadTaskItem task)
    {
        DownloadPage.OpenTaskDirectoryCommand.Execute(task);
    }

    private void HandleOpenReportRequested(Models.DownloadTaskItem task)
    {
        DownloadPage.OpenTaskReportCommand.Execute(task);
    }

    private void HandleOpenRetryReportRequested(Models.DownloadTaskItem task)
    {
        DownloadPage.OpenTaskRetryReportCommand.Execute(task);
    }

    private void HandleClearCompletedRequested()
    {
        DownloadPage.ClearCompletedTasksCommand.Execute(null);
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        _failureDialogsShown.RemoveWhere(task => task.IsFinished);
        UpdateTaskStatusOverview();
    }

    /// <summary>接管下载：创建 Generic 下载任务并入队（用 HttpDownloadService 实际下载）。</summary>
    private void HandleTakeoverDownloadRequested(string url, string targetPath)
    {
        var fileName = System.IO.Path.GetFileName(targetPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "接管下载";
        }

        var taskItem = new Models.DownloadTaskItem
        {
            Name = $"接管下载: {fileName}",
            Status = "已加入队列",
            TaskKind = Models.DownloadTaskKind.Generic,
            TaskAction = Models.DownloadTaskAction.SaveOnly,
            SourceUrl = url,
            OutputFilePath = targetPath,
            StatusIconSource = "avares://SVL.Avalonia/Assets/Icons/Modded.png"
        };

        // 接管下载必须进入统一队列：直接 Insert 只会更新列表，不会持久化或触发调度器，
        // 应用重启后也无法恢复这条任务。
        DownloadPage.EnqueueTask(taskItem, $"接管下载任务已创建: {fileName}");
        TaskStatusPage.SetCurrentTask(taskItem);
    }

    private void HandleTaskLogGenerated(string message)
    {
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleTaskLogGenerated(message));
            return;
        }

        TaskStatusPage.AddLog(message);

        const string retryPrefix = "重试对比报告: ";
        if (message.StartsWith(retryPrefix, StringComparison.Ordinal))
        {
            var reportPath = message.Substring(retryPrefix.Length).Trim();
            TaskStatusPage.AddRetryReport(reportPath);
        }

        UpdateTaskStatusOverview();

        RefreshTaskNavNotification();
    }

    private void HandleNavigateToTaskStatus()
    {
        // 进入任务页前同步任务列表，确保历史任务能显示
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        UpdateTaskStatusOverview();
        NavigateToPage("任务", TaskStatusPage);
    }

    private void HandleNavigateToDownload()
    {
        NavigateToPage("下载", DownloadPage, clearBackStack: true);
    }

    private void HandleNavigateToSettingsForNexusLogin()
    {
        SettingsPage.ReloadFromSettings();
        SettingsPage.SelectedTabIndex = 1; // 下载设置标签页（Nexus 账户区）
        NavigateToPage("设置", SettingsPage, clearBackStack: true);
    }

    private void HandleNavigateToModSearch()
    {
        NavigateToPage("Mod搜索", ModSearchPage);
        _ = ModSearchPage.InitializeAsync();
    }

    private void HandleNavigateToModpackSearch()
    {
        NavigateToPage("Modpack搜索", ModpackSearchPage);
        _ = ModpackSearchPage.InitializeAsync();
    }

    private void HandleOpenDetails(string details)
    {
        ModDetailsPage.SetResource(details, "由下载页搜索结果触发的详情上下文");
        // 立即导航到详情页，详情数据在后台异步加载并在页面上显示 loading 动画。
        NavigateToPage("资源详情", ModDetailsPage, pushCurrentToBackStack: true);
        _ = ModDetailsPage.LoadDetailsAsync(details);
    }

    /// <summary>搜索页（ModSearch/ModpackSearch）结构化身份触发的详情跳转。</summary>
    private void HandleOpenDetailsFromSearch(Models.CatalogResourceIdentity identity)
    {
        // SetResource 仍接字符串以填充 header 显示，用 Identity.Name 作为显示名。
        ModDetailsPage.SetResource(identity.Name, "由搜索页触发的详情上下文");
        // 立即导航到详情页，详情数据在后台异步加载并在页面上显示 loading 动画。
        NavigateToPage("资源详情", ModDetailsPage, pushCurrentToBackStack: true);
        _ = ModDetailsPage.LoadDetailsAsync(identity);
    }

    private void HandleOpenDetailsFromModManage(string details)
    {
        ModDetailsPage.SetResource(details, "由 Mod 列表触发的详情上下文");
        // 立即导航到详情页，详情数据在后台异步加载并在页面上显示 loading 动画。
        NavigateToPage("资源详情", ModDetailsPage, pushCurrentToBackStack: true);
        _ = ModDetailsPage.LoadDetailsAsync(details);
    }

    private async void HandleQueueDownload(Models.ExternalDownloadRequest request)
    {
        var queued = await DownloadPage.AddTaskFromExternalAsync(request);
        if (!queued)
        {
            return;
        }

        TaskStatusPage.SetCurrentTask(request.ToTaskDisplayName(), "已加入队列");
        NavigateToPage("任务", TaskStatusPage);
    }

    /// <summary>批量更新路由：把 VersionSettingsPage 收集的可更新 Mod 列表交给 DownloadPage 入队。</summary>
    private async void HandleBatchUpdateModsRequested(IReadOnlyList<ModBatchUpdateEntry> entries)
    {
        await DownloadPage.EnqueueBatchUpdateAsync(entries);
        NavigateToPage("任务", TaskStatusPage);
    }

    /// <summary>导航到一级页面“启动”，清空返回栈并刷新本机实例状态。</summary>
    [RelayCommand]
    private void NavigateToLaunch()
    {
        LaunchPage.RefreshFromSettingsAndEnvironment();
        NavigateToPage("启动", LaunchPage, clearBackStack: true);
    }

    /// <summary>
    /// 导航到一级页面“Mod管理”（显示名已精简，内部 key 仍为 "本地Mod管理"）。
    /// Business Rule: 复用 VersionSettingsPage 的 Mod 管理分栏，需先 Reload 并 SwitchToModManage，避免显示旧实例缓存。
    /// Reason: 一级页面需 clearBackStack，保证顶栏显示 Logo 而非返回按钮，避免布局跳动。
    /// Note: 显示文本通过 _navLocalModManageText(LocalizationService) 呈现，此处字符串为稳定的导航标识，勿改为显示文本。
    /// </summary>
    [RelayCommand]
    private void NavigateToLocalModManage()
    {
        VersionSettingsPage.ReloadFromSettings(reloadModsWhenActive: true);
        VersionSettingsPage.SwitchToModManage();
        NavigateToPage("本地Mod管理", VersionSettingsPage, clearBackStack: true);
    }

    /// <summary>导航到一级页面“下载”。</summary>
    [RelayCommand]
    private void NavigateToDownload()
    {
        NavigateToPage("下载", DownloadPage, clearBackStack: true);
    }

    /// <summary>当外部 NXM 链接需要把窗口置顶时触发。MainWindow 订阅并调用 Activate()。</summary>
    public event Action? BringToFrontRequested;

    /// <summary>
    /// 处理外部 NXM 链接（来自浏览器协议回调或单实例转发）：导航到下载页后交给 DownloadPage 处理，
    /// 并请求把主窗口置顶。ImportNxmLinkAsync 内部入队后还会跳转到任务页（既有行为）。
    /// </summary>
    public async Task HandleExternalNxmLinkAsync(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        // 优先尝试 SMAPI 专用 NXM 回调（匹配 SMAPI mod id 时由 SmapiDownloadService 接管，不走通用入队）。
        if (await SmapiDownloadService.HandleNxmCallbackAsync(link))
        {
            BringToFrontRequested?.Invoke();
            return;
        }

        // 其次尝试通用浏览器下载回退（非 Premium 用户普通 Mod 的 NXM 回调）。
        if (BrowserDownloadFallbackService.HandleNxmCallback(link))
        {
            BringToFrontRequested?.Invoke();
            return;
        }

        // SMAPI 专用下载流程已经保存了目标实例。若协议层重复投递同一回调，
        // 不得再落入通用 ImportNxmLinkAsync，否则会创建第二个任务并再次弹窗。
        if (DownloadPage.IsActiveSmapiExternalCallback(link))
        {
            BringToFrontRequested?.Invoke();
            return;
        }

        // 先导航到下载页，让用户看到导入状态；入队后 DownloadPage 会再跳任务页。
        NavigateToPage("下载", DownloadPage, clearBackStack: true);

        try
        {
            await DownloadPage.HandleExternalNxmLinkAsync(link);
        }
        catch (Exception ex)
        {
            // 外部链接处理失败不应影响启动器运行。
            System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] 外部 NXM 链接处理失败: {ex.Message}");
        }

        BringToFrontRequested?.Invoke();
    }

    /// <summary>
    /// 处理拖放或按钮选中的本地 Modpack 整合包文件：先验证格式，再弹出元数据预览对话框，
    /// 用户确认导入后将整合包任务入队，并由 DownloadPage 的统一调度器执行安装。
    /// 源压缩包路径随任务保留，任务状态可在重启后恢复。
    /// </summary>
    public async Task HandleModpackDropAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !ModpackTypeDetector.IsSupportedFile(filePath))
        {
            await _dialogService.ShowMessageAsync("不支持的文件", "支持 .zip、.cfmodpack 和 .7z 整合包格式。");
            return;
        }

        // 从版本选择页路径列表构造可选安装目标（DisplayName + GamePath）
        // 预加载 PathEntries（若用户未访问过版本选择页面）
        if (!InstancesPage.HasPathEntries)
        {
            InstancesPage.RefreshFromSettingsChange();
        }
        var pathEntries = InstancesPage.PathEntries
            .Where(p => !string.IsNullOrWhiteSpace(p.GamePath))
            .Select(p => (p.DisplayName, p.GamePath))
            .ToList();

        // 默认选中当前版本所在的 Base 路径
        var preferredPath = ResolveBasePathForInstance(_settingsStore.Load().PreferredInstancePath);
        if (string.IsNullOrWhiteSpace(preferredPath))
        {
            // 回退到版本选择页当前选中的路径
            preferredPath = InstancesPage.SelectedPathEntry?.GamePath;
        }

        var result = await _dialogService.ShowModpackDropDialogAsync(filePath, pathEntries, "导入 Modpack", preferredPath);
        if (result == null)
        {
            // 用户取消；ShowModpackDropDialogAsync 的 CancelCommand 已清理临时解压目录
            return;
        }

        // 检测器返回的包内图标位于临时解压目录，不能把这个短生命周期路径写入
        // 持久化任务。安装器会从原始压缩包重新解压并读取包内图标；只有压缩包旁路
        // 的自定义图标才需要作为任务元数据保留。
        var detection = result.Detection;
        if (IsPathUnderDirectory(detection.ModpackIconPath, detection.TempExtractPath))
        {
            detection.ModpackIconPath = null;
        }

        if (!string.IsNullOrWhiteSpace(detection.TempExtractPath))
        {
            ModpackTypeDetector.CleanupTempDirectory(detection.TempExtractPath);
            detection.TempExtractPath = string.Empty;
        }

        EnqueueModpackImportTask(result);
    }

    private static bool IsPathUnderDirectory(string? path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            var normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task ShowModpackFailureDialogAsync(Models.DownloadTaskItem task)
    {
        try
        {
            var failureReason = string.IsNullOrWhiteSpace(task.FailedDetails)
                ? task.Status
                : task.FailedDetails;
            var logPath = !string.IsNullOrWhiteSpace(task.RetryReportPath)
                ? task.RetryReportPath
                : task.ReportPath;
            var action = await _dialogService.ShowModpackFailureDialogAsync(
                failureReason,
                logPath,
                $"整合包安装结果 - {task.Name}");

            if (action == ModpackFailureDialogAction.Retry && task.CanRetry)
            {
                DownloadPage.RetryTaskCommand.Execute(task);
            }
        }
        catch
        {
            // 失败汇总弹窗属于辅助提示；若窗口在关闭过程中不可用，
            // 保留任务页中的失败明细，不影响任务状态和重试入口。
        }
    }

    private static string? ResolveBasePathForInstance(string? instancePath)
    {
        var resolved = InstanceRuntimePathResolver.ResolveBasePath(instancePath);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    /// <summary>将整合包导入任务入队（按检测类型设置 TaskKind，ExecuteTaskAsync 据此路由到 ModpackInstallService）。</summary>
    private void EnqueueModpackImportTask(Models.ModpackDropDialogResult result)
    {
        var detection = result.Detection;
        var displayName = detection.ModpackName ?? System.IO.Path.GetFileNameWithoutExtension(result.ModpackFilePath);
        var typeText = detection.Type.ToString();

        var taskKind = detection.Type switch
        {
            SVL.Core.Platform.Modpack.ModpackType.SVL => Models.DownloadTaskKind.SvlModpack,
            SVL.Core.Platform.Modpack.ModpackType.Curseforge => Models.DownloadTaskKind.CurseforgeModpack,
            SVL.Core.Platform.Modpack.ModpackType.NexusCollection => Models.DownloadTaskKind.NexusCollection,
            _ => Models.DownloadTaskKind.Generic
        };

        var taskAction = taskKind switch
        {
            Models.DownloadTaskKind.NexusCollection => Models.DownloadTaskAction.InstallCollection,
            Models.DownloadTaskKind.Generic => Models.DownloadTaskAction.InstallMod,
            _ => Models.DownloadTaskAction.InstallModpack
        };

        var taskItem = new Models.DownloadTaskItem
        {
            Name = $"{displayName} ({typeText})",
            Status = "已加入队列",
            TaskKind = taskKind,
            TaskAction = taskAction,
            SourceUrl = result.ModpackFilePath,
            OutputFilePath = result.ModpackFilePath,
            TargetInstanceName = result.InstanceName,
            TargetGamePath = result.TargetGamePath,
            CustomIconPath = result.Detection.ModpackIconPath ?? string.Empty,
            StatusIconSource = "avares://SVL.Avalonia/Assets/Icons/Modded.png"
        };
        taskItem.SetState(Models.DownloadTaskState.Pending, "已加入队列");

        DownloadPage.EnqueueTask(taskItem, $"整合包导入任务已创建: {displayName}");
    }

    /// <summary>按钮路径：打开文件选择器选取整合包文件后进入导入流程。</summary>
    [RelayCommand]
    private async Task ImportModpackFromFile()
    {
        var path = await _dialogService.PickModpackFileAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await HandleModpackDropAsync(path);
    }

    /// <summary>导航到一级页面“任务”，进入前同步 DownloadPage 的任务列表以显示历史任务。</summary>
    [RelayCommand]
    private void NavigateToTasks()
    {
        TaskStatusPage.SyncTasks(DownloadPage.DownloadTasks);
        NavigateToPage("任务", TaskStatusPage, clearBackStack: true);
    }

    [RelayCommand]
    private void OpenFloatingTaskManager()
    {
        NavigateToTasks();
    }

    /// <summary>导航到一级页面“设置”。</summary>
    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateToPage("设置", SettingsPage, clearBackStack: true);
    }

    /// <summary>返回上一级二级页面（从返回栈弹出）。仅当二级页面（IsBackPage）有栈时可用。</summary>
    [RelayCommand]
    private void NavigateBack()
    {
        if (_backStack.Count <= 0)
        {
            return;
        }

        var previous = _backStack.Pop();
        NavigateToPage(previous.Page, previous.ViewModel);
    }

    /// <summary>
    /// 核心导航方法。Reason: 统一维护 CurrentPage/CurrentPageViewModel 与 _backStack，避免各处分散修改导致返回逻辑不一致。
    /// </summary>
    /// <param name="page">页面标识（与 Is*Page 判定一致）。</param>
    /// <param name="viewModel">目标 ViewModel。</param>
    /// <param name="pushCurrentToBackStack">是否为二级页面跳转（压栈）。</param>
    /// <param name="clearBackStack">是否为一级页面切换（清空栈）。Business Rule: 一级页面间切换必须清空，否则会误显示返回按钮。</param>
    private void NavigateToPage(string page, ObservableObject viewModel, bool pushCurrentToBackStack = false, bool clearBackStack = false)
    {
        if (clearBackStack)
        {
            _backStack.Clear();
        }

        if (pushCurrentToBackStack && CurrentPageViewModel != null)
        {
            _backStack.Push((CurrentPage, CurrentPageViewModel));
        }

        CurrentPage = page;
        CurrentPageViewModel = viewModel;
        RefreshTaskNavNotification();
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowBrandIdentity));
    }

    /// <summary>
    /// 判定是否为二级页面（需显示返回按钮且支持返回栈）。
    /// Business Rule: 仅 实例/版本设置/资源详情 为二级页面；"Mod管理"(内部 key "本地Mod管理")已提升为一级页面，需显示 Logo 而非返回箭头。
    /// </summary>
    private static bool IsBackPage(string page)
    {
        return string.Equals(page, "实例", StringComparison.Ordinal) ||
               string.Equals(page, "版本设置", StringComparison.Ordinal) ||
               string.Equals(page, "资源详情", StringComparison.Ordinal);
    }

    private void RefreshTaskNavNotification()
    {
        var hasFailedTasks = DownloadPage.DownloadTasks.Any(task => task.IsFailed || task.CanRetry);
        var hasRunningTasks = DownloadPage.DownloadTasks.Any(task => task.IsRunning);

        ShowTaskNavNotification = !IsTasksPage && hasFailedTasks;
        ShowTaskNavSoftHint = !IsTasksPage && !hasFailedTasks && hasRunningTasks;
        UpdateTaskStatusOverview();
    }

    private void UpdateTaskStatusOverview()
    {
        TaskStatusPage.UpdateTaskOverview(
            DownloadPage.ActiveTasks.Count,
            DownloadPage.FinishedTasks.Count,
            DownloadPage.SelectedTaskHint);
        RefreshFloatingTaskButtonState();
    }

    private void RefreshFloatingTaskButtonState()
    {
        // 红点只统计"需要关注"的任务：运行中（ActiveTasks）+ 失败/取消（不含成功）
        // 成功任务（Completed）不计入红点，避免用户处理完任务后红点一直不消失
        var activeCount = DownloadPage.ActiveTasks.Count;
        var failedCount = DownloadPage.FinishedTasks.Count(t => t.IsFailed || t.TaskState == Models.DownloadTaskState.Cancelled);
        var badgeCount = activeCount + failedCount;
        FloatingTaskBadgeCount = badgeCount;

        var enabled = SettingsPage.EnableDownloadFloatingTaskButton;
        ShowDownloadFloatingTaskButton = enabled && badgeCount > 0 && !IsTasksPage;
    }

    private void HandleSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SettingsPageViewModel.LauncherAppName), StringComparison.Ordinal))
        {
            LauncherAppNameText = string.IsNullOrWhiteSpace(SettingsPage.LauncherAppName)
                ? "SVL"
                : SettingsPage.LauncherAppName;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(SettingsPageViewModel.EnableDownloadFloatingTaskButton), StringComparison.Ordinal))
        {
            RefreshFloatingTaskButtonState();
        }
    }

    private void HandleSettingsNexusLoggedOut()
    {
        DownloadPage.ResetNexusAuthNotificationSuppression();
    }
}
