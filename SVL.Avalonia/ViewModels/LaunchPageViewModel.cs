using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;
using SVL.Core.Platform.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SVL.Avalonia.ViewModels;

/// <summary>
/// 启动页 ViewModel（左侧实例卡 + 底部操作区）。
/// <para>导航职责：通过事件将“版本选择/版本设置/Mod管理”委托给 MainWindowViewModel 统一入栈，避免子 ViewModel 直接操作导航栈。</para>
/// <para>历史：顶栏“Mod管理”曾显示为“本地Mod管理”，2026-01 精简为“Mod管理”，显示文本由 LocalizationService(Nav.LocalModManage/Launch.ModManage) 提供。</para>
/// </summary>
public partial class LaunchPageViewModel : ObservableObject
{
    private readonly IGameInstallPathLocator _gameInstallPathLocator;
    private readonly IExternalProcessService _externalProcessService;
    private readonly AppUserSettingsStore _settingsStore;
    private readonly LocalizationService _localizationService;
    private readonly ImageResourceService _imageResourceService;
    private readonly DialogService? _dialogService;
    private ModProfileStore? _profileStore;
    private string _currentGamePath = string.Empty;
    private string _preferredLaunchModeToken = "auto";

    private ObservableCollection<ModProfileRecord>? _modProfiles;
    public ObservableCollection<ModProfileRecord> ModProfiles => _modProfiles ??= [];

    private ModProfileStore SafeProfileStore => _profileStore ??= new ModProfileStore();

    [ObservableProperty]
    private ModProfileRecord? _selectedModProfile;

    public bool IsSaveSharedNoticeVisible => SelectedModProfile != null && !SelectedModProfile.IsDefault && !SelectedModProfile.EnableCustomSavePath;

    public bool IsCustomSaveActive => SelectedModProfile != null && SelectedModProfile.EnableCustomSavePath;

    partial void OnSelectedModProfileChanged(ModProfileRecord? value)
    {
        OnPropertyChanged(nameof(IsSaveSharedNoticeVisible));
        OnPropertyChanged(nameof(IsCustomSaveActive));
    }

    /// <summary>请求导航到“实例”二级页面（由 MainWindow 订阅后压栈）。</summary>
    public event Action? NavigateToInstancesRequested;
    /// <summary>请求导航到“版本设置”二级页面。</summary>
    public event Action? NavigateToVersionSettingsRequested;
    /// <summary>请求导航到“Mod管理”一级页面（顶栏 Mod管理，复用 VersionSettingsPage Mod 分栏；显示名已由“本地Mod管理”精简，内部 key 仍兼容）。</summary>
    public event Action? NavigateToModManageRequested;

    [ObservableProperty]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _gameVersion = string.Empty;

    [ObservableProperty]
    private string _versionStatus = string.Empty;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private string _launchButtonText = string.Empty;

    [ObservableProperty]
    private bool _showModManageButton;

    [ObservableProperty]
    private bool _hasInstances;

    [ObservableProperty]
    private string _actionStatus = string.Empty;

    [ObservableProperty]
    private string _preferredLaunchMode = string.Empty;

    [ObservableProperty]
    private bool _enableSafeLaunch;

    [ObservableProperty]
    private string _safeLaunchState = string.Empty;

    [ObservableProperty]
    private string _instanceInfoLabel = string.Empty;

    [ObservableProperty]
    private string _launchModeLabel = string.Empty;

    [ObservableProperty]
    private string _safeLaunchLabel = string.Empty;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private string _refreshButtonText = string.Empty;

    [ObservableProperty]
    private string _versionSelectButtonText = string.Empty;

    [ObservableProperty]
    private string _modManageButtonText = string.Empty;

    [ObservableProperty]
    private string _versionSettingsButtonText = string.Empty;

    [ObservableProperty]
    private string _welcomeTitle = string.Empty;

    [ObservableProperty]
    private string _getStartedTitle = string.Empty;

    [ObservableProperty]
    private string _statusHeadline = string.Empty;

    [ObservableProperty]
    private string _statusSubline = string.Empty;

    [ObservableProperty]
    private string _brandText = string.Empty;

    [ObservableProperty]
    private string _guideNoInstanceLead = string.Empty;

    [ObservableProperty]
    private string _guideStep1 = string.Empty;

    [ObservableProperty]
    private string _guideStep2 = string.Empty;

    [ObservableProperty]
    private string _guideStep3 = string.Empty;

    [ObservableProperty]
    private string _guideStep4 = string.Empty;

    [ObservableProperty]
    private string _guideUsageTitle = string.Empty;

    [ObservableProperty]
    private string _guideUsageLine1 = string.Empty;

    [ObservableProperty]
    private string _guideUsageLine2 = string.Empty;

    [ObservableProperty]
    private string _guideUsageModManageLine = string.Empty;

    [ObservableProperty]
    private string _instanceIconSource = "avares://SVL.Avalonia/Assets/Icons/Junimo.png";

    public bool ShowOnboardingCard => !HasInstances;

    public bool ShowNoInstanceIcon => !HasInstances;

    public bool ShowModdedInstanceIcon => HasInstances && ShowModManageButton;

    public bool ShowVanillaInstanceIcon => HasInstances && !ShowModManageButton;

    public bool CanOpenVersionSettings => HasInstances;

    partial void OnActionStatusChanged(string value)
    {
        RefreshStatusBanner();
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        RefreshStatusBanner();
    }

    partial void OnEnableSafeLaunchChanged(bool value)
    {
        SafeLaunchState = GetSafeLaunchStateText(value);
    }

    partial void OnShowModManageButtonChanged(bool value)
    {
        NotifyInstanceFlavorIconVisibilityChanged();
        RefreshStatusBanner();
    }

    partial void OnHasInstancesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOnboardingCard));
        OnPropertyChanged(nameof(CanOpenVersionSettings));
        NotifyInstanceFlavorIconVisibilityChanged();
        RefreshStatusBanner();
    }

    public LaunchPageViewModel(
        IGameInstallPathLocator gameInstallPathLocator,
        IExternalProcessService externalProcessService,
        AppUserSettingsStore settingsStore,
        LocalizationService localizationService,
        ImageResourceService imageResourceService,
        DialogService? dialogService = null,
        ModProfileStore? profileStore = null)
    {
        _gameInstallPathLocator = gameInstallPathLocator;
        _externalProcessService = externalProcessService;
        _settingsStore = settingsStore;
        _localizationService = localizationService;
        _imageResourceService = imageResourceService;
        _dialogService = dialogService;
        _profileStore = profileStore ?? new ModProfileStore();
        _localizationService.LanguageChanged += ApplyLocalizedTexts;
        _imageResourceService.ResourcesChanged += RefreshInstanceFromLocalEnvironment;
        ApplyLocalizedTexts();
        RefreshLaunchPreferencesFromSettings();
        RefreshInstanceFromLocalEnvironment();
        NotifyInstanceFlavorIconVisibilityChanged();
        RefreshStatusBanner();
    }

    private void NotifyInstanceFlavorIconVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowNoInstanceIcon));
        OnPropertyChanged(nameof(ShowModdedInstanceIcon));
        OnPropertyChanged(nameof(ShowVanillaInstanceIcon));
    }

    private void RefreshStatusBanner()
    {
        if (IsLaunching)
        {
            StatusHeadline = _localizationService.Get("Launch.Status.StartingTitle");
            StatusSubline = _localizationService.Get("Launch.Status.StartingSubtitle");
            return;
        }

        if (!HasInstances)
        {
            StatusHeadline = _localizationService.Get("Launch.Status.NoInstanceTitle");
            StatusSubline = _localizationService.Get("Launch.Status.NoInstanceSubtitle");
            return;
        }

        StatusHeadline = ShowModManageButton
            ? _localizationService.Get("Launch.Status.ReadySmapiTitle")
            : _localizationService.Get("Launch.Status.ReadyVanillaTitle");
        StatusSubline = _localizationService.Get("Launch.Status.ReadySubtitle");
    }

    private void ApplyLocalizedTexts()
    {
        InstanceInfoLabel = Text("Launch.InstanceInfo");
        LaunchModeLabel = Text("Launch.Mode");
        SafeLaunchLabel = Text("Launch.SafeLaunch");
        StatusLabel = Text("Launch.Status");
        RefreshButtonText = Text("Launch.Refresh");
        VersionSelectButtonText = Text("Launch.VersionSelect");
        ModManageButtonText = Text("Launch.ModManage");
        VersionSettingsButtonText = Text("Launch.VersionSettings");
        WelcomeTitle = Text("Launch.Welcome");
        GetStartedTitle = Text("Launch.GetStarted");
        BrandText = Text("Launch.Brand");
        GuideNoInstanceLead = Text("Launch.Guide.NoInstanceLead");
        GuideStep1 = Text("Launch.Guide.Step1");
        GuideStep2 = Text("Launch.Guide.Step2");
        GuideStep3 = Text("Launch.Guide.Step3");
        GuideStep4 = Text("Launch.Guide.Step4");
        GuideUsageTitle = Text("Launch.Guide.UsageTitle");
        GuideUsageLine1 = Text("Launch.Guide.UsageLine1");
        GuideUsageLine2 = Text("Launch.Guide.UsageLine2");
        GuideUsageModManageLine = Text("Launch.Guide.UsageModManage");
        LaunchButtonText = IsLaunching ? Text("Launch.Button.Launching") : Text("Launch.Button.Launch");
        PreferredLaunchMode = GetLaunchModeDisplayText(_preferredLaunchModeToken);
        SafeLaunchState = GetSafeLaunchStateText(EnableSafeLaunch);
        RefreshStatusBanner();
    }

    [RelayCommand]
    private void RefreshLocalGamePath()
    {
        RefreshFromSettingsAndEnvironment(true);
    }

    public void RefreshFromSettingsAndEnvironment(bool updateStatus = false)
    {
        RefreshLaunchPreferencesFromSettings();
        RefreshInstanceFromLocalEnvironment();
        if (updateStatus)
        {
            ActionStatus = Format("Launch.Action.Refreshed", DateTime.Now.ToString("HH:mm:ss"));
        }
    }

    public void RefreshLaunchPreferencesFromSettings()
    {
        var settings = _settingsStore.Load();
        _preferredLaunchModeToken = NormalizeLaunchModeToken(settings.PreferredLaunchMode);
        PreferredLaunchMode = GetLaunchModeDisplayText(_preferredLaunchModeToken);
        EnableSafeLaunch = settings.EnableSafeLaunch;
        SafeLaunchState = GetSafeLaunchStateText(EnableSafeLaunch);
    }

    private void RefreshInstanceFromLocalEnvironment()
    {
        var settings = _settingsStore.Load();
        var preferredPath = settings.PreferredInstancePath;
        if (!string.IsNullOrWhiteSpace(preferredPath) && Directory.Exists(preferredPath))
        {
            HasInstances = true;
            _currentGamePath = preferredPath;
            var rawName = string.IsNullOrWhiteSpace(settings.InstanceName)
                ? Path.GetFileName(preferredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : settings.InstanceName;
            GameVersion = string.Empty;

            var preferredHasSmapi = InstanceIconResolver.IsSmapiRuntime(preferredPath);
            var selectedModeToken = NormalizeLaunchModeToken(settings.PreferredLaunchMode);
            // Auto 模式与 ResolveLaunchTarget 保持同一优先级：存在 SMAPI 运行时就启动 SMAPI。
            // 隔离实例没有 Base 的双变体语义，必须按实际运行时判定；实例名不能决定图标。
            var isIsolatedInstance = InstanceIconResolver.IsVersionIsolatedInstance(preferredPath);
            var selectedIsSmapi = preferredHasSmapi &&
                                  (isIsolatedInstance ||
                                   string.Equals(selectedModeToken, "smapi", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(selectedModeToken, "auto", StringComparison.OrdinalIgnoreCase));

            if (!isIsolatedInstance && preferredHasSmapi)
            {
                if (selectedIsSmapi && !rawName.EndsWith("(SMAPI)", StringComparison.OrdinalIgnoreCase))
                {
                    rawName = $"{rawName} (SMAPI)";
                }
                else if (!selectedIsSmapi && rawName.EndsWith("(SMAPI)", StringComparison.OrdinalIgnoreCase))
                {
                    rawName = rawName.Substring(0, rawName.Length - 7).TrimEnd();
                }
            }

            InstanceName = rawName;
            var gameVersion = DetectGameVersion(preferredPath);
            var smapiVersion = preferredHasSmapi ? DetectSmapiVersion(preferredPath) : "未安装";

            GameVersion = string.IsNullOrWhiteSpace(gameVersion) || string.Equals(gameVersion, "未知版本", StringComparison.OrdinalIgnoreCase)
                ? "星露谷物语"
                : $"游戏版本: {gameVersion}";

            ShowModManageButton = preferredHasSmapi;
            VersionStatus = BuildVersionStatusText(selectedIsSmapi, gameVersion, smapiVersion);
            SetInstanceIconSource(ResolveInstanceIconSource(preferredPath, selectedIsSmapi));
            ReloadModProfiles();
            ActionStatus = "已就绪";
            return;
        }

        var steamPath = _gameInstallPathLocator.TryLocateSteamStardewPath();
        var gogPath = _gameInstallPathLocator.TryLocateGogStardewPath();
        var xboxPath = _gameInstallPathLocator.TryLocateXboxStardewPath();
        var gamePath = steamPath ?? gogPath ?? xboxPath;

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            HasInstances = false;
            _currentGamePath = string.Empty;
            InstanceName = Text("Launch.Instance.NoneName");
            GameVersion = Text("Launch.Instance.NoneVersion");
            VersionStatus = Text("Launch.Instance.NoneStatus");
            ShowModManageButton = false;
            SetInstanceIconSource(ResolveNoneInstanceIcon());
            ReloadModProfiles();
            ActionStatus = Text("Launch.Action.NoPathDetected");
            return;
        }

        HasInstances = true;
        _currentGamePath = gamePath;
        InstanceName = Path.GetFileName(gamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var hasSmapi = InstanceIconResolver.IsSmapiRuntime(gamePath);
        var detectedGameVersion = DetectGameVersion(gamePath);
        var detectedSmapiVersion = hasSmapi ? DetectSmapiVersion(gamePath) : "未安装";

        GameVersion = string.IsNullOrWhiteSpace(detectedGameVersion) || string.Equals(detectedGameVersion, "未知版本", StringComparison.OrdinalIgnoreCase)
            ? "星露谷物语"
            : $"游戏版本: {detectedGameVersion}";

        ShowModManageButton = hasSmapi;
        VersionStatus = BuildVersionStatusText(hasSmapi, detectedGameVersion, detectedSmapiVersion);
        SetInstanceIconSource(ResolveInstanceIconSource(gamePath, hasSmapi));
        ReloadModProfiles();
        ActionStatus = "已就绪";
    }

    private void SetInstanceIconSource(string source)
    {
        var normalized = NormalizeImageSource(source);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (File.Exists(normalized))
        {
            InstanceIconSource = $"{normalized}?v={DateTime.UtcNow.Ticks}";
            return;
        }

        if (string.Equals(InstanceIconSource, normalized, StringComparison.OrdinalIgnoreCase))
        {
            InstanceIconSource = string.Empty;
        }

        InstanceIconSource = normalized;
    }

    private static string NormalizeImageSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var index = source.IndexOfAny(['?', '#']);
        return index < 0 ? source : source[..index];
    }

    private static string BuildVersionStatusText(bool isSmapiMode, string gameVersion, string smapiVersion)
    {
        if (isSmapiMode)
        {
            return string.IsNullOrWhiteSpace(smapiVersion)
                ? "SMAPI 未安装"
                : $"SMAPI {smapiVersion}";
        }

        var resolvedGameVersion = string.IsNullOrWhiteSpace(gameVersion) ? "未知版本" : gameVersion;
        return $"原版 {resolvedGameVersion}";
    }

    private static string DetectGameVersion(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return "未知版本";
        }

        var depsPath = Path.Combine(gamePath, "Stardew Valley.deps.json");
        if (File.Exists(depsPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(depsPath));
                if (doc.RootElement.TryGetProperty("targets", out var targetsElement))
                {
                    foreach (var target in targetsElement.EnumerateObject())
                    {
                        foreach (var package in target.Value.EnumerateObject())
                        {
                            if (!package.Name.StartsWith("Stardew Valley/", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var parts = package.Name.Split('/');
                            if (parts.Length == 2)
                            {
                                return parts[1];
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to file metadata below.
            }
        }

        var dllPath = Path.Combine(gamePath, "Stardew Valley.dll");
        if (!File.Exists(dllPath))
        {
            return "未知版本";
        }

        try
        {
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
            return string.IsNullOrWhiteSpace(fileVersion) ? "未知版本" : fileVersion;
        }
        catch
        {
            return "未知版本";
        }
    }

    private static string DetectSmapiVersion(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return "未安装";
        }

        var markers = new[]
        {
            Path.Combine(gamePath, "StardewModdingAPI.exe"),
            Path.Combine(gamePath, "StardewModdingAPI"),
            Path.Combine(gamePath, "StardewModdingAPI.dll")
        };

        var markerPath = markers.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return "未安装";
        }

        try
        {
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(markerPath).FileVersion;
            return string.IsNullOrWhiteSpace(version) ? "Unknown" : version;
        }
        catch
        {
            return "Unknown";
        }
    }

    private string ResolveInstanceIconSource(string instancePath, bool isSmapiInstance)
    {
        // 自定义图标优先级高于系统预设图标
        var customIcon = InstanceIconResolver.ResolveIconPath(instancePath, isSmapiInstance);
        if (!string.IsNullOrWhiteSpace(customIcon))
        {
            return customIcon;
        }

        // 异常检测：路径无效或游戏文件缺失时使用异常图标
        var isAnomaly = IsInstanceAnomaly(instancePath);
        return ResolveDefaultInstanceIcon(isSmapiInstance, isAnomaly);
    }

    /// <summary>检测实例是否处于异常状态（路径无效或关键游戏文件缺失）。</summary>
    private static bool IsInstanceAnomaly(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
        {
            return true;
        }

        // 关键游戏文件均不存在则视为异常
        return !File.Exists(Path.Combine(instancePath, "Stardew Valley.dll")) &&
               !File.Exists(Path.Combine(instancePath, "Stardew Valley.exe")) &&
               !File.Exists(Path.Combine(instancePath, "Stardew Valley.deps.json"));
    }

    private string ResolveNoneInstanceIcon()
    {
        var resolved = _imageResourceService.Get("launch.instance.none");
        return string.IsNullOrWhiteSpace(resolved)
            ? "avares://SVL.Avalonia/Assets/Icons/Junimo.png"
            : resolved;
    }

    /// <summary>
    /// 解析默认预设图标。【临时占位】后续有新的预设条件可随时更换。
    /// 优先级：自定义图标 > 系统预设图标（SMAPI=Modded.png / 原版=Vanilla.png / 异常=Junimo2.png）
    /// </summary>
    private string ResolveDefaultInstanceIcon(bool isSmapiInstance, bool isAnomaly)
    {
        // 异常状态直接使用异常图标（不经过 imageResourceService 覆盖，确保异常可见性）
        if (isAnomaly)
        {
            var anomalyResolved = _imageResourceService.Get("launch.instance.anomaly");
            return string.IsNullOrWhiteSpace(anomalyResolved)
                ? InstanceIconResolver.ResolveDefaultPresetIcon(isSmapiInstance, isAnomaly)
                : anomalyResolved;
        }

        var key = isSmapiInstance ? "launch.instance.modded" : "launch.instance.vanilla";
        var fallback = InstanceIconResolver.ResolveDefaultPresetIcon(isSmapiInstance, isAnomaly);
        if (_imageResourceService == null)
        {
            return fallback;
        }

        var resolved = _imageResourceService.Get(key);
        return string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
    }

    [RelayCommand]
    private async Task LaunchGame()
    {
        if (IsLaunching)
        {
            return;
        }

        RefreshLaunchPreferencesFromSettings();
        var settings = _settingsStore.Load();

        if (!HasInstances)
        {
            ActionStatus = Text("Launch.Action.RequireInstance");
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentGamePath) || !Directory.Exists(_currentGamePath))
        {
            ActionStatus = Text("Launch.Action.InvalidInstancePath");
            return;
        }

        var launchTarget = ResolveLaunchTarget(_currentGamePath, _preferredLaunchModeToken);
        if (string.IsNullOrWhiteSpace(launchTarget) || (!File.Exists(launchTarget) && !Directory.Exists(launchTarget)))
        {
            ActionStatus = Format("Launch.Action.TargetNotFound", PreferredLaunchMode);
            return;
        }

        IsLaunching = true;
        LaunchButtonText = Text("Launch.Button.Launching");

        var launchArguments = BuildLaunchArguments(settings);
        var hasArguments = !string.IsNullOrWhiteSpace(launchArguments);

        Process? gameProcess = null;
        try
        {
            var launched = false;
            if (File.Exists(launchTarget) && _externalProcessService is IProcessStartService processStarter)
            {
                gameProcess = processStarter.TryStartProcess(
                    launchTarget,
                    launchArguments,
                    Path.GetDirectoryName(launchTarget));
                launched = gameProcess != null;
            }
            else
            {
                launched = hasArguments && !Directory.Exists(launchTarget)
                    ? _externalProcessService.TryLaunchProcess(
                        launchTarget,
                        launchArguments,
                        Path.GetDirectoryName(launchTarget))
                    : _externalProcessService.TryOpenPath(launchTarget);
            }

            if (!launched)
            {
                ActionStatus = Text("Launch.Action.LaunchFailed");
                return;
            }

            var safeText = EnableSafeLaunch ? Text("Launch.Action.SafeTag") : string.Empty;
            var argsText = hasArguments ? Format("Launch.Action.ArgsTag", launchArguments) : string.Empty;
            ActionStatus = Format(
                "Launch.Action.Started",
                DateTime.Now.ToString("HH:mm:ss"),
                Path.GetFileName(launchTarget),
                PreferredLaunchMode,
                safeText,
                argsText);

            // 只有拿到真实进程句柄且配置了自定义标题时才等待窗口并设置标题；
            // 启动器不应等待游戏退出，否则“启动中”会持续整个游戏生命周期。
            if (gameProcess != null && OperatingSystem.IsWindows())
            {
                var titleTemplate = ResolveGameWindowTitle(settings.GameWindowTitle);
                if (!string.IsNullOrWhiteSpace(titleTemplate))
                {
                    await WindowTitleService.SetWindowTitleAsync(
                        gameProcess,
                        titleTemplate,
                        InstanceName,
                        _currentGamePath);
                }
            }
        }
        catch (Exception ex)
        {
            ActionStatus = Format("Launch.Action.LaunchFailed") + $"：{ex.Message}";
        }
        finally
        {
            gameProcess?.Dispose();
            IsLaunching = false;
            LaunchButtonText = Text("Launch.Button.Launch");
        }
    }

    private static string ResolveGameWindowTitle(string? configuredTitle)
    {
        var normalized = configuredTitle?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ||
               string.Equals(normalized, "<default>", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : normalized;
    }

    /// <summary>跳转到版本选择（实例列表）二级页面。</summary>
    [RelayCommand]
    private void NavigateToVersionSelect()
    {
        ActionStatus = Text("Launch.Action.NavigateVersionSelect");
        NavigateToInstancesRequested?.Invoke();
    }

    /// <summary>跳转到 Mod管理（顶栏一级页面，原“本地Mod管理”）。前置校验实例有效性，避免空路径进入 Mod 管理。</summary>
    [RelayCommand]
    private void OpenModManage()
    {
        if (!HasInstances || string.IsNullOrWhiteSpace(_currentGamePath))
        {
            ActionStatus = Text("Launch.Action.RequireValidInstance");
            return;
        }

        ActionStatus = Text("Launch.Action.OpenModManage");
        NavigateToModManageRequested?.Invoke();
    }

    /// <summary>跳转到版本设置（二级页面，需已选实例）。</summary>
    [RelayCommand]
    private void OpenVersionSettings()
    {
        if (!HasInstances)
        {
            ActionStatus = Text("Launch.Action.RequireInstance");
            return;
        }

        ActionStatus = Text("Launch.Action.OpenVersionSettings");
        NavigateToVersionSettingsRequested?.Invoke();
    }

    /// <summary>打开当前游戏安装目录。</summary>
    [RelayCommand]
    private void OpenGameFolder()
    {
        if (string.IsNullOrWhiteSpace(_currentGamePath) || !Directory.Exists(_currentGamePath))
        {
            ActionStatus = Text("Launch.Action.InvalidInstancePath");
            return;
        }

        _externalProcessService.TryOpenPath(_currentGamePath);
    }

    /// <summary>打开当前游戏 Mods 目录（若选中自定义预设则打开对应预设的 Mods 目录）。</summary>
    [RelayCommand]
    private void OpenModsFolder()
    {
        if (string.IsNullOrWhiteSpace(_currentGamePath) || !Directory.Exists(_currentGamePath))
        {
            ActionStatus = Text("Launch.Action.InvalidInstancePath");
            return;
        }

        var modsPath = SelectedModProfile != null && !string.IsNullOrWhiteSpace(SelectedModProfile.ModsPath)
            ? SelectedModProfile.GetEffectiveModsPath(_currentGamePath)
            : Path.Combine(_currentGamePath, "Mods");

        if (!Directory.Exists(modsPath))
        {
            try { Directory.CreateDirectory(modsPath); } catch { }
        }

        _externalProcessService.TryOpenPath(Directory.Exists(modsPath) ? modsPath : _currentGamePath);
    }

    /// <summary>获取当前生效的 Mods 目录绝对路径。</summary>
    public string GetCurrentEffectiveModsPath()
    {
        if (SelectedModProfile != null && !string.IsNullOrWhiteSpace(SelectedModProfile.ModsPath))
        {
            return SelectedModProfile.GetEffectiveModsPath(_currentGamePath);
        }

        return string.IsNullOrWhiteSpace(_currentGamePath) ? string.Empty : Path.Combine(_currentGamePath, "Mods");
    }

    /// <summary>重新加载当前实例的 Mod 预设列表。</summary>
    public void ReloadModProfiles()
    {
        var previousSelectedId = SelectedModProfile?.Id;
        ModProfiles.Clear();
        if (string.IsNullOrWhiteSpace(_currentGamePath))
        {
            SelectedModProfile = null;
            return;
        }

        var profiles = SafeProfileStore.GetProfilesForInstance(_currentGamePath, InstanceName);
        foreach (var p in profiles)
        {
            ModProfiles.Add(p);
        }

        var match = ModProfiles.FirstOrDefault(p => string.Equals(p.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                    ?? ModProfiles.FirstOrDefault();
        SelectedModProfile = match;
    }

    /// <summary>呼出 Mod 整合包/预设管理弹窗。</summary>
    [RelayCommand]
    private async Task ManageModProfilesAsync()
    {
        if (_dialogService == null || string.IsNullOrWhiteSpace(_currentGamePath))
        {
            return;
        }

        var result = await _dialogService.ShowModProfileManageDialogAsync(
            SafeProfileStore,
            _currentGamePath,
            _currentGamePath,
            InstanceName,
            SelectedModProfile?.Id);

        ReloadModProfiles();
        if (result != null)
        {
            var match = ModProfiles.FirstOrDefault(p => string.Equals(p.Id, result.Id, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedModProfile = match;
            }
        }
    }

    private static string ResolveLaunchTarget(string gamePath, string launchModeToken)
    {
        var smapiCandidates = new[]
        {
            Path.Combine(gamePath, "StardewModdingAPI.exe"),
            Path.Combine(gamePath, "StardewModdingAPI")
        };

        var gameCandidates = new[]
        {
            Path.Combine(gamePath, "Stardew Valley.exe"),
            Path.Combine(gamePath, "StardewValley.exe"),
            Path.Combine(gamePath, "StardewValley"),
            Path.Combine(gamePath, "Stardew Valley.app")
        };

        if (string.Equals(launchModeToken, "smapi", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in smapiCandidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        if (string.Equals(launchModeToken, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in gameCandidates)
            {
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        foreach (var candidate in smapiCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in gameCandidates)
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private string Text(string key) => _localizationService.Get(key);

    private string Format(string key, params object[] args) => string.Format(Text(key), args);

    private string GetSafeLaunchStateText(bool enabled) => enabled
        ? Text("Launch.SafeLaunch.Enabled")
        : Text("Launch.SafeLaunch.Disabled");

    private string GetLaunchModeDisplayText(string modeToken)
    {
        return modeToken switch
        {
            "smapi" => Text("Launch.ModeValue.Smapi"),
            "vanilla" => Text("Launch.ModeValue.Vanilla"),
            _ => Text("Launch.ModeValue.Auto")
        };
    }

    private static string NormalizeLaunchModeToken(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "auto";
        }

        var normalized = mode.Trim();

        if (string.Equals(normalized, "SMAPI", StringComparison.OrdinalIgnoreCase))
        {
            return "smapi";
        }

        if (string.Equals(normalized, "原版", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            return "vanilla";
        }

        if (string.Equals(normalized, "自动", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        return "auto";
    }

    private string BuildLaunchArguments(Models.AppUserSettings settings)
    {
        var args = new List<string>();

        if (EnableSafeLaunch)
        {
            args.Add("--safe");
        }

        // 方案 C: SMAPI 原生 --mods-path 支持
        if (SelectedModProfile != null && !string.IsNullOrWhiteSpace(SelectedModProfile.ModsPath))
        {
            var effectiveMods = SelectedModProfile.GetEffectiveModsPath(_currentGamePath);
            if (!string.IsNullOrWhiteSpace(effectiveMods))
            {
                try { Directory.CreateDirectory(effectiveMods); } catch { }
                args.Add($"--mods-path \"{effectiveMods}\"");
            }
        }

        // 独立存档空间 --save-path 支持
        if (SelectedModProfile != null && SelectedModProfile.EnableCustomSavePath)
        {
            var effectiveSave = !string.IsNullOrWhiteSpace(SelectedModProfile.CustomSavePath)
                ? Path.GetFullPath(SelectedModProfile.CustomSavePath)
                : (!string.IsNullOrWhiteSpace(SelectedModProfile.ModsPath)
                    ? Path.Combine(Path.GetDirectoryName(SelectedModProfile.ModsPath) ?? SelectedModProfile.ModsPath, "Saves")
                    : Path.Combine(_currentGamePath, "Saves"));

            if (!string.IsNullOrWhiteSpace(effectiveSave))
            {
                try { Directory.CreateDirectory(effectiveSave); } catch { }
                args.Add($"--save-path \"{effectiveSave}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.InstanceCustomLaunchArguments))
        {
            args.Add(settings.InstanceCustomLaunchArguments.Trim());
        }

        return string.Join(" ", args);
    }
}
