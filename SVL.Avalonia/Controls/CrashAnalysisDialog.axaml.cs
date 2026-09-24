using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SVL.Avalonia.Services;
using SVL.Avalonia.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SVL.Avalonia.Controls;

/// <summary>
/// 崩溃日志分析对话框。展示 SMAPI 日志分析结果，支持导入日志、打开日志目录与复制报告。
/// </summary>
public partial class CrashAnalysisDialog : Window
{
    private static readonly string[] SyncedResourceKeys =
    [
        "AccentBrush",
        "CardBrush",
        "BorderBrush",
        "SurfaceBrush",
        "WindowBackgroundBrush",
        "TextPrimaryBrush",
        "TextSecondaryBrush",
        "TextOnAccentBrush"
    ];

    public CrashAnalysisDialog()
    {
        InitializeComponent();
        RefreshThemeResources();
        ThemeService.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void ImportLog_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 SMAPI 日志文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("日志文件") { Patterns = ["*.txt", "*.log"] }
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path) && DataContext is CrashAnalysisViewModel vm)
        {
            vm.LoadFromFile(path);
        }
    }

    private void OpenLogFolder_Click(object? sender, RoutedEventArgs e)
    {
        var directory = SmapiLogLocator.EnumerateErrorLogDirectories().FirstOrDefault(Directory.Exists);
        if (directory is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打开目录失败不影响对话框使用。
        }
    }

    private async void CopyReport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CrashAnalysisViewModel vm)
        {
            return;
        }

        var clipboard = Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(vm.BuildReportText());
    }

    private void OnThemeChanged()
    {
        Dispatcher.UIThread.Post(RefreshThemeResources);
    }

    private void RefreshThemeResources()
    {
        var appResources = Application.Current?.Resources;
        if (appResources == null)
        {
            return;
        }

        foreach (var key in SyncedResourceKeys)
        {
            if (appResources.TryGetValue(key, out var value))
            {
                Resources[key] = value;
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        Closed -= OnClosed;
    }
}
