using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SVL.Avalonia.ViewModels;
using System.ComponentModel;

namespace SVL.Avalonia.Views;

public partial class InstancesPageView : UserControl
{
    public InstancesPageView()
    {
        InitializeComponent();
    }

    private void PathEntry_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            DataContext is not InstancesPageViewModel vm ||
            control.DataContext is not PathEntryItem item)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        var isRightClick = point.Properties.IsRightButtonPressed;
        var isMacCtrlClick = OperatingSystem.IsMacOS() &&
                             point.Properties.IsLeftButtonPressed &&
                             e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!isRightClick && !isMacCtrlClick)
        {
            return;
        }

        // 不依赖平台默认的 ContextMenu 手势。自定义 ListBoxItem 模板和 Popup
        // 在不同 Avalonia/Fluent 版本下可能不会把 PlacementTarget 正确传给菜单，
        // 这里显式打开并先保存选中项，保证菜单项始终作用于右键所在路径。
        vm.SelectedPathEntry = item;
        if (control.ContextMenu is { } menu)
        {
            menu.Open(control);
            e.Handled = true;
        }
    }

    private void PathContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        // ContextMenu 位于独立的 Popup 视觉树中，打开前从 PlacementTarget
        // 保存路径项，避免菜单打开后 Binding="." 丢失原 DataTemplate 上下文。
        if (DataContext is not InstancesPageViewModel vm)
        {
            return;
        }

        var item = menu.PlacementTarget is Control { DataContext: PathEntryItem placementItem }
            ? placementItem
            : menu.DataContext as PathEntryItem;
        if (item == null)
        {
            // 某些 Popup 实现会在 Opening 事件中暂时清空 PlacementTarget；
            // 此时保留当前选中路径，避免菜单出现但点击后没有目标。
            item = vm.SelectedPathEntry;
        }
        if (item == null)
        {
            e.Cancel = true;
            return;
        }

        vm.SelectedPathEntry = item;
        foreach (var menuItem in menuItems(menu))
        {
            menuItem.Tag = item;
        }
    }

    private static IEnumerable<MenuItem> menuItems(ContextMenu? menu)
    {
        return menu?.Items.OfType<MenuItem>() ?? Enumerable.Empty<MenuItem>();
    }

    private static PathEntryItem? ResolvePathEntryFromMenuSender(object? sender)
    {
        if (sender is not MenuItem menuItem)
        {
            return null;
        }

        if (menuItem.Tag is PathEntryItem fromTag)
        {
            return fromTag;
        }

        if (menuItem.DataContext is PathEntryItem fromDataContext)
        {
            return fromDataContext;
        }

        return menuItem.Parent is ContextMenu
        {
            PlacementTarget: Control { DataContext: PathEntryItem fromPlacement }
        }
            ? fromPlacement
            : null;
    }

    private void OnPathRenameClicked(object? sender, RoutedEventArgs e)
    {
        ExecutePathCommand(sender, (vm, item) => vm.RenamePathCommand.Execute(item));
    }

    private void OnPathOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        ExecutePathCommand(sender, (vm, item) => vm.OpenFolderCommand.Execute(item));
    }

    private void OnPathRefreshClicked(object? sender, RoutedEventArgs e)
    {
        ExecutePathCommand(sender, (vm, item) => vm.RefreshPathCommand.Execute(item));
    }

    private void OnPathDeleteClicked(object? sender, RoutedEventArgs e)
    {
        ExecutePathCommand(sender, (vm, item) => vm.DeletePathCommand.Execute(item));
    }

    private void ExecutePathCommand(object? sender, Action<InstancesPageViewModel, PathEntryItem> execute)
    {
        if (DataContext is InstancesPageViewModel viewModel)
        {
            // Tag/PlacementTarget 是首选；SelectedPathEntry 是兼容回退，覆盖
            // 某些平台在 ContextMenu 关闭瞬间清理 Popup 关联的情况。
            var item = ResolvePathEntryFromMenuSender(sender) ?? viewModel.SelectedPathEntry;
            if (item != null)
            {
                execute(viewModel, item);
            }
        }
    }
}
