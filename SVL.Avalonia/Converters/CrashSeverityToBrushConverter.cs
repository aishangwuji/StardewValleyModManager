using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SVL.Avalonia.Models;
using System.Globalization;

namespace SVL.Avalonia.ViewModels;

/// <summary>崩溃结论严重度到画刷的转换器，用于崩溃分析结果着色。</summary>
/// <remarks>
/// Critical/Error/Warning 使用固定警示色；Info 使用主题次级文本色，保证深浅色模式下对比度。
/// </remarks>
public sealed class CrashSeverityToBrushConverter : IValueConverter
{
    public static readonly CrashSeverityToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value is CrashSeverity s ? s : CrashSeverity.Info;
        return severity switch
        {
            CrashSeverity.Critical => Brushes.OrangeRed,
            CrashSeverity.Error => Brushes.Orange,
            CrashSeverity.Warning => Brushes.Goldenrod,
            _ => TryFindResourceBrush("TextSecondaryBrush") ?? Brushes.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush? TryFindResourceBrush(string key)
    {
        var resources = Application.Current?.Resources;
        return resources is not null && resources.TryGetValue(key, out var resource)
            ? resource as IBrush
            : null;
    }
}
