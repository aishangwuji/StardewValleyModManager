using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SVL.Avalonia.Models;
using SVL.Avalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SVL.Avalonia.Controls;

public partial class ModProfileManageDialog : Window
{
    private readonly ModProfileStore _profileStore;
    private readonly string _instanceKey;
    private readonly string _gamePath;
    private readonly string _instanceName;

    public ObservableCollection<ModProfileRecord> Profiles { get; } = [];

    public static readonly global::Avalonia.DirectProperty<ModProfileManageDialog, ModProfileRecord?> SelectedProfileProperty =
        global::Avalonia.AvaloniaProperty.RegisterDirect<ModProfileManageDialog, ModProfileRecord?>(
            nameof(SelectedProfile),
            o => o.SelectedProfile,
            (o, v) => o.SelectedProfile = v);

    private ModProfileRecord? _selectedProfile;
    public ModProfileRecord? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetAndRaise(SelectedProfileProperty, ref _selectedProfile, value))
            {
                CopySelectedToEditing();
            }
        }
    }

    public static readonly global::Avalonia.DirectProperty<ModProfileManageDialog, ModProfileRecord> EditingProfileProperty =
        global::Avalonia.AvaloniaProperty.RegisterDirect<ModProfileManageDialog, ModProfileRecord>(
            nameof(EditingProfile),
            o => o.EditingProfile,
            (o, v) => o.EditingProfile = v);

    private ModProfileRecord _editingProfile = new();
    public ModProfileRecord EditingProfile
    {
        get => _editingProfile;
        set => SetAndRaise(EditingProfileProperty, ref _editingProfile, value);
    }

    public static readonly global::Avalonia.DirectProperty<ModProfileManageDialog, string> StatusMessageProperty =
        global::Avalonia.AvaloniaProperty.RegisterDirect<ModProfileManageDialog, string>(
            nameof(StatusMessage),
            o => o.StatusMessage,
            (o, v) => o.StatusMessage = v);

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetAndRaise(StatusMessageProperty, ref _statusMessage, value);
    }

    public ModProfileManageDialog()
    {
        InitializeComponent();
        _profileStore = new ModProfileStore();
        _instanceKey = string.Empty;
        _gamePath = string.Empty;
        _instanceName = string.Empty;
    }

    public ModProfileManageDialog(
        ModProfileStore profileStore,
        string instanceKey,
        string gamePath,
        string instanceName,
        string? activeProfileId = null) : this()
    {
        _profileStore = profileStore;
        _instanceKey = instanceKey;
        _gamePath = gamePath;
        _instanceName = instanceName;

        LoadProfiles(activeProfileId);
    }

    private void LoadProfiles(string? selectId = null)
    {
        Profiles.Clear();
        var items = _profileStore.GetProfilesForInstance(_instanceKey, _instanceName);
        foreach (var item in items)
        {
            Profiles.Add(item);
        }

        var target = Profiles.FirstOrDefault(p => string.Equals(p.Id, selectId, StringComparison.OrdinalIgnoreCase))
                     ?? Profiles.FirstOrDefault();
        SelectedProfile = target;
    }

    private void CopySelectedToEditing()
    {
        if (SelectedProfile == null)
        {
            EditingProfile = new ModProfileRecord();
            return;
        }

        EditingProfile = new ModProfileRecord
        {
            Id = SelectedProfile.Id,
            Name = SelectedProfile.Name,
            InstanceKey = SelectedProfile.InstanceKey,
            ModsPath = SelectedProfile.ModsPath,
            IsDefault = SelectedProfile.IsDefault,
            EnableCustomSavePath = SelectedProfile.EnableCustomSavePath,
            CustomSavePath = SelectedProfile.CustomSavePath,
            Description = SelectedProfile.Description,
            CreatedTime = SelectedProfile.CreatedTime
        };
    }

    private void NewProfile_Click(object? sender, RoutedEventArgs e)
    {
        var newRecord = new ModProfileRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"整合包预设 {Profiles.Count + 1}",
            InstanceKey = _instanceKey,
            ModsPath = string.Empty,
            IsDefault = false,
            EnableCustomSavePath = false,
            CustomSavePath = string.Empty,
            Description = string.Empty
        };

        Profiles.Add(newRecord);
        SelectedProfile = newRecord;
        StatusMessage = "已新建预设，请设置名称与 Mods 目录后保存。";
    }

    private async void BrowseModsFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择此整合包的 Mods 文件夹",
                AllowMultiple = false
            });

            var folder = folders.FirstOrDefault();
            if (folder != null)
            {
                var localPath = folder.Path.IsAbsoluteUri ? Uri.UnescapeDataString(folder.Path.LocalPath) : folder.Path.ToString();
                EditingProfile.ModsPath = localPath;
                // Force UI property update
                RaisePropertyChanged(EditingProfileProperty, EditingProfile, EditingProfile);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"选择文件夹失败: {ex.Message}";
        }
    }

    private async void BrowseSaveFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择独立存档存放文件夹 (Saves)",
                AllowMultiple = false
            });

            var folder = folders.FirstOrDefault();
            if (folder != null)
            {
                var localPath = folder.Path.IsAbsoluteUri ? Uri.UnescapeDataString(folder.Path.LocalPath) : folder.Path.ToString();
                EditingProfile.CustomSavePath = localPath;
                RaisePropertyChanged(EditingProfileProperty, EditingProfile, EditingProfile);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"选择文件夹失败: {ex.Message}";
        }
    }

    private void OpenModsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var path = EditingProfile.GetEffectiveModsPath(_gamePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "无法解析 Mods 文件夹路径。";
            return;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            StatusMessage = $"已打开目录: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"无法打开目录: {ex.Message}";
        }
    }

    private void DeleteProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || SelectedProfile.IsDefault)
        {
            StatusMessage = "默认预设不能删除。";
            return;
        }

        var deletedName = SelectedProfile.Name;
        var deletedId = SelectedProfile.Id;

        _profileStore.DeleteProfile(deletedId);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();

        StatusMessage = $"已删除预设: {deletedName}";
    }

    private void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var targetProfile = SelectedProfile;
            if (targetProfile == null)
            {
                StatusMessage = "请先在左侧选择一个预设。";
                return;
            }

            if (string.IsNullOrWhiteSpace(EditingProfile.Name))
            {
                StatusMessage = "预设名称不能为空。";
                return;
            }

            var savedName = EditingProfile.Name.Trim();

            // Apply changes from EditingProfile to targetProfile
            targetProfile.Name = savedName;
            targetProfile.ModsPath = EditingProfile.ModsPath?.Trim() ?? string.Empty;
            targetProfile.EnableCustomSavePath = EditingProfile.EnableCustomSavePath;
            targetProfile.CustomSavePath = EditingProfile.CustomSavePath?.Trim() ?? string.Empty;

            // Auto-create folder if specified and does not exist
            if (!string.IsNullOrWhiteSpace(targetProfile.ModsPath))
            {
                try
                {
                    if (!Directory.Exists(targetProfile.ModsPath))
                    {
                        Directory.CreateDirectory(targetProfile.ModsPath);
                    }
                }
                catch { }
            }

            if (targetProfile.EnableCustomSavePath && !string.IsNullOrWhiteSpace(targetProfile.CustomSavePath))
            {
                try
                {
                    if (!Directory.Exists(targetProfile.CustomSavePath))
                    {
                        Directory.CreateDirectory(targetProfile.CustomSavePath);
                    }
                }
                catch { }
            }

            _profileStore.UpsertProfile(targetProfile);

            StatusMessage = $"预设「{savedName}」已成功保存！";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(SelectedProfile);
    }
}
