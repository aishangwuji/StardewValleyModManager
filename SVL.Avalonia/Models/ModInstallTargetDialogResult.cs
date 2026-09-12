namespace SVL.Avalonia.Models;

/// <summary>未选择游戏版本时，Mod 安装目标对话框的用户操作。</summary>
public enum ModInstallTargetDialogAction
{
    Confirm,
    SaveAs,
    Cancel
}

/// <summary>Mod 安装目标对话框返回值。</summary>
public sealed record ModInstallTargetDialogResult(
    ModInstallTargetDialogAction Action,
    string? SelectedPath = null);
