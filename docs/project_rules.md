# SecRandom 开发 Rules（必须遵守）

## 项目概述与结构

- 技术栈
  - 框架：Avalonia
  - UI：FluentAvalonia
  - 服务主机：Microsoft.Extensions.Hosting（DI 以 Host 为准）
- 项目结构
  - `SecRandom`：UI 主项目（Views / ViewModels / Langs / Models / Services）
  - `SecRandom.Core`：核心通用（抽象、扩展、控件、工具、通用服务）
  - `SecRandom.Desktop`：桌面启动壳（入口 `Program.cs`）

## 硬性规则（违反就会出问题）

- ViewModels 必须注册到 Host（强约定）。
- 导航页面必须：
  - 类上标注 `[PageInfo(...)]`
  - 在 `SecRandom/App.axaml.cs` 的 `BuildHost()` 里用 `services.AddMainPage<T>() / AddSettingsPage<T>()` 注册
- 桌面主/设置子页和移动业务/设置子页都是普通 `UserControl`，不得继承 `ViewBase`。MVE 只承载独立逻辑视图（桌面 `MainView` / `SettingsView`、崩溃恢复和插件 `PluginViewRegistration`）；平台 UI 差异在对应 Host 的条件 DI 注册阶段决定，不通过 MVE 路由表替换页面类型。
- 本地化必须按“每页一个文件夹”拆分，不要混在一起。
- 文件路径统一用 `Utils.GetFilePath(...)`。桌面和便携包数据落在 package root 的 `data/...`；仅 `SecRandom.Mobile.MobileApp` 可以在任何路径首次读取前调用一次 `Utils.ConfigureMobileDataRoot()`，它固定选择 app-private `LocalApplicationData/SecRandom/data`，其他代码不得运行中改写根目录。
- 不要在页面里随意 `new` 可复用服务；需要复用/单例/可测试的服务必须进 Host。
- 平台功能必须使用 `SecRandom.Platforms.Abstractions` 的窄接口，经 `App.BuildHost()` 注册后调用。窗口类只能声明所需特性，不得直接添加 Win32/X11/AppKit 调用或散落的 `OperatingSystem.Is*` 分支。
- 插件只能使用 `SecRandom.Core/Plugins` 中的稳定契约；运行时加载、启用状态和管理 UI 放在 `SecRandom/Services/Plugins`。
- 课程联动的数据源固定为 `0=关闭`、`1=CSES`、`2=ClassIsland`。CSES 文件由 app 层服务管理在 `data/CSES/cses_schedule.yml`，ClassIsland IPC 仅能在 app 层适配器中引用。数据源失效或状态未知时必须允许抽取；只有确认的课间状态可触发限制、浮窗隐藏或课前重置。
- 公平抽取不能开放算法接口给插件；插件只能通过 `IPluginDrawInvoker` 发起宿主抽取调用，不能拿到 `DrawEngine`、权重计算、随机源、历史写入或抽取配置。

## Host/依赖注入（怎么写才符合本项目）

- 桌面 Host 构建与注册入口：`SecRandom/App.axaml.cs` 的 `BuildHost()`。`SecRandom.Mobile.Shared`（RootNamespace 保持 `SecRandom.Mobile`）是不引用桌面应用程序集的独立 SingleView 壳，`MobileApp` 只构建最小 Host，不得调用桌面 `BuildHost()`；`SecRandom.Android` / `SecRandom.iOS` 仅为平台入口头项目。移动端构建用 `-p:BuildMobile=true` 直接构建对应入口头项目，不再使用 `MobileTargetFramework` 开关。
- 取服务统一走静态入口：
  - `IAppHost.GetService<T>()`（拿不到会抛异常）
  - `IAppHost.TryGetService<T>()`（拿不到返回 null）
- 常见生命周期选择（按项目现有用法对齐）：
  - `AddSingleton`：配置 Handler、核心业务服务（例如 list service）
  - `AddTransient`：ViewModel、主容器 View（MainView/SettingsView）、非共享页面实例

## 导航系统（正确注册与正确跳转）

### 注册（你提到的就是正确姿势）

导航不是“手写菜单项”，而是“注册页面 → 生成菜单项 → keyed service 实例化页面”。

- 进入设置导航的标准写法：

```csharp
services.AddSettingsPage<LotteryTablePreviewPage>(
    Langs.SettingsPages.ListManagementPage.Resources.LotteryTableTitle);
```

- 分组（侧边栏折叠组）：
  - `services.AddGroup(new GroupInfo(name, groupId, iconGlyph));`
  - 页面 `[PageInfo(..., groupId: "settings.listManagement")]` 加入该组

### 跳转（推荐）

- 设置页内部跳转（最常用）：
  - `SettingsView.Current?.SelectNavigationItemById("settings.xxx");`
- 主界面内部跳转：
  - `MainView.Current?.SelectNavigationItemById("main.xxx");`
- 注意：导航页面是 keyed service 取出来的，没注册就会显示“页面未找到”的占位控件。

### PageId 约定（建议）

- 主界面：`main.xxx`
- 设置页：`settings.xxx`
- 设置子页：`settings.group.xxx`
- 插件页：`plugin.<plugin-id>.main.xxx` 或 `plugin.<plugin-id>.settings.xxx`，不能占用内置 `main.*` / `settings.*`。

## 插件系统

- 插件清单放在 `data/plugins/<plugin-id>/plugin.json`，插件私有数据放在 `data/plugins/<plugin-id>/data/`。
- 插件启用/禁用默认需要重启；设置页应调用 `SettingsView.RequestRestartApp()`。
- 插件日志必须接入原有 `ILogger` / `FileLoggerProvider`，分类前缀固定为 `SecRandom.Plugin[<plugin-id>].`。
- 插件详情页只能按自己的分类前缀筛选日志，不能展示其他插件或宿主日志。
- 不要向插件暴露 `IAppHost.Host`、完整 `IServiceProvider`、可写 `MainConfigHandler`、可写 `IProfileService`、shell/process 能力、遥测/在线状态服务或任意文件系统访问。

## 本地化（必须）

- 每个页面的本地化拆分到独立文件夹，结构固定：
  - `Resources.resx`（zh-hans）
  - `Resources.Designer.cs`
  - `Resources.en-US.resx`
  - `Resources.ja-JP.resx`
- `SecRandom/SecRandom.csproj` 只需要注册 `Resources.resx` 和 `Resources.Designer.cs`（照现有条目追加，不要把所有语言文件都注册进去）。
- 注意必须使用 `PublicResXFileCodeGenerator`
- 页面标题/菜单标题优先直接用 `Langs.*.Resources.*`。
- 大部分情况无需处理 en-US 和 ja-JP 的创建和本地化，由 Crowdin 处理。

## 配置系统（必须理解）

- 配置文件路径由 `ConfigBase.ConfigFilePath` 决定（因此天然支持“可变路径/档案切换”的设计）。
- `ConfigHandlerBase` 默认监听 `PropertyChanged` 自动保存；`MainConfigHandler` 还会对语言/主题/字体等变更触发 UI 行为。
- 保存/读取 JSON 由 `SecRandom.Core/Services/Config/FileConfigService.cs` 实现，并从 desktop/mobile Host 注册；它不是插件 API。

### 配置集合类保存（易踩坑）

- `Dictionary` 内部增删改不会触发 `PropertyChanged`，不会自动保存。
- 正确姿势：在 Unloaded 等方法调用 Save 方法，或更新完毕就调用。

## UI 常用小用法（写页面时直接拿来用）

- Toast：
  - 页面里直接 `this.ShowWarningToast(...) / ShowErrorToast(...)`
  - 不需要自己管理容器，MainView/SettingsView Loaded 时会注入 `AppToastAdorner`

## 新增功能 Checklist（照这个做，基本不会漏）

- 新增设置页
  - 添加页面类 + `[PageInfo]`
  - 新增本地化文件夹（Resources 三件套）
  - `BuildHost()` 里 `services.AddSettingsPage<>()` 注册（必要时先 `AddGroup`）
  - 需要语言切换刷新标题：补 `App.Consts.cs` 的 `PageNameProviders`
  - 页面跳转使用 `SettingsView.Current?.SelectNavigationItemById(...)`
- 新增 ViewModel
  - 在 `BuildHost()` 里注册到 Host
  - 页面/容器通过 `IAppHost.GetService<>()` 或构造注入（以现有风格为准）
- 新增服务
  - 优先放 `SecRandom.Core/Services`（通用）或 `SecRandom/Services`（UI 专属）
  - 在 `BuildHost()` 注册（需要复用的一律不要 `new`）
- 平台原生实现放在对应 `SecRandom.Platforms.<OS>` 项目；平台抽象、启动上下文和 Stub 不进入 `Core`/`Shared` 的插件或 IPC 公开面
