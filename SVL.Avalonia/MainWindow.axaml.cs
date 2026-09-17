using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using SVL.Avalonia.Services;
using SVL.Avalonia.ViewModels;
using SVL.Core.Platform.Modpack;
using System.Linq;

namespace SVL.Avalonia;

public partial class MainWindow : Window
{
    /// <summary>本窗口关联的浮窗通知服务实例。静态门面 NotificationService.Show 委托到此实例。</summary>
    public NotificationService Notifications { get; }

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            // Force custom chrome on Windows to match legacy WPF behavior.
            SystemDecorations = SystemDecorations.None;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 48;
        }
        else
        {
            // 非 Windows：保留原生窗口装饰（macOS 红绿灯 / Linux 窗口按钮），
            // 但把内容延伸进原生标题栏，让自定义橙头栏本身充当标题栏。
            // Reason: 此前为 false，导致"原生标题栏 + 自定义 48px 头栏"双层堆叠，
            // 既浪费纵向空间又割裂视觉风格。
            // Business Rule: 仅改变内容延伸，不接管窗口装饰；平台若不支持该 hint
            // 会安全退化为原生标题栏 + 自定义头栏（与旧行为一致），不会破坏窗口管理。
            SystemDecorations = SystemDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            ExtendClientAreaTitleBarHeightHint = 48;
        }

        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SVL.Avalonia/Assets/Icons/icon.png")));
        }
        catch
        {
            // If icon loading fails on a platform, keep default icon to avoid startup interruption.
        }

        // 初始化浮窗通知服务：绑定 ItemsSource 并注册为静态门面宿主。
        Notifications = new NotificationService();
        NotificationContainer.ItemsSource = Notifications.ActiveNotifications;
        NotificationService.RegisterHost(Notifications);

        // DataContext 由 App 的对象初始化器在构造函数后设置，故用事件订阅置顶请求。
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty && change.NewValue is WindowState state)
        {
            UpdateMaximizeRestoreButton(state);
        }
    }

    private void UpdateMaximizeRestoreButton(WindowState state)
    {
        if (MaximizeIcon == null || RestoreIcon == null || MaximizeButton == null)
            return;

        bool isMaximized = state == WindowState.Maximized;
        MaximizeIcon.IsVisible = !isMaximized;
        RestoreIcon.IsVisible = isMaximized;
        ToolTip.SetTip(MaximizeButton, isMaximized ? "还原" : "最大化");
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.BringToFrontRequested += OnBringToFrontRequested;
        }
    }

    private void OnBringToFrontRequested()
    {
        // 收到外部 NXM 链接时激活窗口：恢复最小化并置顶。
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    /// <summary>拖拽悬停时：仅当数据包含文件时允许 Copy 效果，否则拒绝。</summary>
    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files != null && files.Any())
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>拖放释放时：取首个受支持的整合包文件路径交给 ViewModel 处理。</summary>
    private async void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        string? modpackPath = null;
        foreach (var file in files)
        {
            var path = file.Path.IsAbsoluteUri
                ? Uri.UnescapeDataString(file.Path.LocalPath)
                : file.Path.ToString();
            if (ModpackTypeDetector.IsSupportedFile(path))
            {
                modpackPath = path;
                break;
            }
        }

        if (string.IsNullOrEmpty(modpackPath))
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.HandleModpackDropAsync(modpackPath);
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control sourceControl &&
            (sourceControl is Button || sourceControl.GetVisualAncestors().Any(visual => visual is Button)))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }
}
