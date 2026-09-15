using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SVL.Avalonia.Models;

/// <summary>
/// 表示一个 Mod 预设 / 整合包配置项。
/// 支持通过 SMAPI 原生 --mods-path（及可选 --save-path）实现无需物理搬移文件的瞬时切包。
/// </summary>
public sealed partial class ModProfileRecord : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>预设显示名称（如 "SVE 大型扩展"、"纯净美化"、"默认"）</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>所属实例标识或根路径（用于按实例筛选，空表示通用预设）</summary>
    [ObservableProperty]
    private string _instanceKey = string.Empty;

    /// <summary>
    /// 自定义 Mods 目录的绝对路径。
    /// 为空或空白时，表示使用游戏实例自带的默认 Mods 目录。
    /// </summary>
    [ObservableProperty]
    private string _modsPath = string.Empty;

    /// <summary>是否为当前实例下的默认预设（不可删除）</summary>
    [ObservableProperty]
    private bool _isDefault;

    /// <summary>是否启用独立存档空间</summary>
    [ObservableProperty]
    private bool _enableCustomSavePath;

    /// <summary>独立存档目录路径（仅在 EnableCustomSavePath 为 true 时生效）</summary>
    [ObservableProperty]
    private string _customSavePath = string.Empty;

    /// <summary>备注说明</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>创建时间戳</summary>
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 获取该预设在指定游戏路径下实际生效的 Mods 目录。
    /// </summary>
    public string GetEffectiveModsPath(string defaultGamePath)
    {
        if (!string.IsNullOrWhiteSpace(ModsPath))
        {
            return Path.GetFullPath(ModsPath);
        }

        return string.IsNullOrWhiteSpace(defaultGamePath)
            ? string.Empty
            : Path.Combine(defaultGamePath, "Mods");
    }
}
