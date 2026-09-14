# AGENTS 开发指南 — SVL Stardew Valley Launcher

> **强制前置**：任何代码编写/修改/重构、git 提交前，必须先加载 `agent-rules` 技能（`C:\Users\15216\.agents\skills\eng-rules\SKILL.md`）。按技能内分级机制判定 L1/L2/L3，不得凭记忆跳过。

## 项目概览
- **名称**：SVL (Stardew Valley Launcher) — 星露谷物语一站式启动器 / Mod 管理器 / Modpack 工具
- **当前主干**：`Dev-Avalonia`，已完成 WPF (.NET Framework 4.8) → Avalonia 跨平台迁移
- **核心功能**：实例管理、SMAPI 自动安装、Mod 启用/禁用/依赖解析、NexusMods/CurseForge/GitHub 集成、Modpack/Collection 导入导出、下载队列、社区汉化、SteamCMD 游戏本体下载
- **已清理**：`SVL.Core` / `SVL.Desktop` / `SVL.Tests` 已删除（commit `40da3c6`），仅保留 `SVL.Avalonia` + `SVL.Core.Platform` + `SVL.Migration.Tests`

## 技术栈
| 层级 | 技术 |
|------|------|
| 运行时 | .NET 10.0 SDK `10.0.401`（`dotnet --version` 需 10.0.x） |
| UI | Avalonia UI 11.2.8 + Fluent 主题 + `DynamicResource` |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| 解压 | SharpCompress 0.49.1 / SharpZipLib 1.4.2 |
| 配置 | System.Text.Json |
| 测试 | xUnit (`SVL.Migration.Tests` 269 用例) |

## 目录结构（精简后）
```
SVL/
├── SVL.Avalonia/            # 主程序 net10.0 — 二次开发唯一入口
│   ├── ViewModels/          # MVVM（MainWindow/Launch/Download/Instances/FeaturePages 等9个）
│   ├── Views/               # AXAML 页面（20+）
│   ├── Controls/            # 对话框/自定义控件（42个）
│   ├── Services/            # 44个服务（下载/Nexus/Modpack/SMAPI/实例/汉化/缓存）
│   ├── Models/              # DTO（DownloadTaskItem/CatalogItem/AppUserSettings 等12个）
│   ├── Resources/           # Theme.axaml / Icons.axaml
│   ├── Assets/Icons/        # 图标资源（含 icon.ico/icon.png，勿引用已删 SVL.Desktop）
│   ├── App.axaml(.cs)       # 启动流程（迁移/单实例/NXM/崩溃日志）
│   └── Program.cs           # 入口，PendingNxmUrl + SingleInstance
├── SVL.Core.Platform/       # 平台抽象层 net10.0（22文件）
│   ├── Abstractions/        # 9接口（ISingleInstance/IPlatformInfo/IGameInstallPathLocator 等）
│   ├── Services/            # 单实例/路径探测/窗口标题/协议注册
│   └── IO/ArchiveExtractor.cs + Modpack/ModpackTypeDetector.cs
├── SVL.Migration.Tests/     # 迁移回归（267通过/2跳过）
├── build.ps1                # 统一打包（Windows zip / macOS dmg）
├── scripts/package-avalonia.* # 兼容入口，转发 build.ps1
└── SVL.sln                  # 仅含上述3项目
```

## 构建与运行
```powershell
# 环境检查
dotnet --version  # 10.0.401
dotnet --info

# 开发构建（勿直接 build 整个 sln 的旧 net48 项目，已清理）
dotnet restore SVL.Avalonia/SVL.Avalonia.csproj
dotnet build SVL.sln -c Debug                    # 或仅 Avalonia 项目
dotnet test SVL.Migration.Tests -c Debug         # 267通过
dotnet run --project SVL.Avalonia -c Debug

# 打包发布（产物 artifacts/SVL_v1.2.0.0_*）
.\build.ps1 -Config Debug -Targets windows
.\build.ps1 -Config Release -Targets windows
.\build.ps1 -Config Release -Targets macos  # 需 macOS 主机
```

## 开发规则（来自 agent-rules v2.5.1）
1. **任务开始前**：检查 `systemmap/` 是否存在 → 按 `last_verified_commit` 核验陈旧度；新业务域（≥2实体/新状态机）先输出业务全景清单；涉及本机/服务器先读系统级认知文件。
2. **P0 红线闭集**：边界校验 / session token 隔离 / 可重试幂等 / 关键假设落地为断言或测试 / 安全边界（认证/密钥不进 git/参数化）— 触及必完整清单+100% 独立核验。
3. **交付清单分级**：触红线→完整清单+独立核验；含分支/IO不触红线→完整清单+≥10%抽样；纯文档/胶水→`[✅] 本次变更仅涉及文档/注释，不触发业务规则检查`。
4. **未接入模块**：不删不动，标记转人工 + 登 techdebt/（`@DeferDecision`）。
5. **Git 原子提交**：一逻辑一 commit，提交前扫敏感信息（密钥/.env），受保护分支需人工执行。

## 重要约定
- **语言**：默认 `zh-CN`，本地化在 `Services/LocalizationService.cs` 字典 `zh-CN/en-US`，Key 如 `Nav.Launch`/`Launch.ModManage`（显示已由“本地Mod管理”精简为“Mod管理”，内部导航 key 仍为“本地Mod管理”以兼容历史，顶栏位于 `启动` 与 `下载` 之间）。
- **导航**：`MainWindowViewModel` 管理 `CurrentPage`（`启动/本地Mod管理[显示"Mod管理"]/下载/任务/设置`），`DownloadPageViewModel.SelectedCategory` 为 `Smapi/Mods/Modpacks/Game`，`Game` 走 `SteamCmdService`，其余走 `RemoteCatalogService`（`api.curse.tools` / Nexus GraphQL / `api.github.com/repos/Pathoschild/SMAPI`）。
- **图标**：统一 `avares://SVL.Avalonia/Assets/Icons/` + `AssetImageConverter`，`ApplicationIcon=Assets/Icons/icon.ico`，`build.ps1` 的 `iconSrc` 已指向该路径。
- **配置持久化**：`%LocalAppData%\SVL\Avalonia\` 下 `usersettings.json` / `download-tasks-state.json` 等，`AppUserSettingsStore` / `DownloadTaskStateStore` 负责原子写入。
- **平台**：Windows 注册表探测 Steam/GOG/Xbox，`SingleInstanceService` 命名管道转发 `nxm://`，非 Windows 跳过窗口标题 API。
- **测试**：新增逻辑优先补 `SVL.Migration.Tests` 回归，保持 `dotnet test` 通过。

## 常见任务入口
- 新增页面：仿 `Views/LaunchPageView.axaml` + `ViewModels/LaunchPageViewModel.cs`，在 `MainWindowViewModel` 注册导航
- 新增下载源：在 `RemoteCatalogService` 扩展 `CatalogSource`，并在 `DownloadPageViewModel` 更新 `ModSources/ModpackSources`
- 平台相关：改 `SVL.Core.Platform/Services/*`，接口进 `Abstractions/`
- 主题：改 `Resources/Theme.axaml` 的 `DynamicResource`

## 注意事项
- `Tmds.DBus.Protocol 0.20.0` 的 `NU1903` 漏洞警告为上游公告（`migration-audit-report.md:73`），非本项目代码问题
- 进程占用 `bin/Debug/net10.0/SVL.Avalonia.exe` 时 `dotnet build` 会报 `MSB3027`，需先关闭运行中的启动器
- 已删除的旧目录勿再引用，历史参考仅存于 git 历史
- 提交时使用中文描述
