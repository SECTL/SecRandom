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
- 本地化必须按“每页一个文件夹”拆分，不要混在一起。
- 文件路径统一用 `Utils.GetFilePath(...)`（数据落在 `AppContext.BaseDirectory/data/...`）。
- 不要在页面里随意 `new` 可复用服务；需要复用/单例/可测试的服务必须进 Host。

## Host/依赖注入（怎么写才符合本项目）

- Host 构建与所有注册入口：`SecRandom/App.axaml.cs` 的 `BuildHost()`。
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

## 本地化（必须）

- 每个页面的本地化拆分到独立文件夹，结构固定：
  - `Resources.resx`（zh-hans）
  - `Resources.Designer.cs`
  - `Resources.en-us.resx`
  - `Resources.ja-jp.resx`
- `SecRandom/SecRandom.csproj` 只需要注册 `Resources.resx` 和 `Resources.Designer.cs`（照现有条目追加，不要把所有语言文件都注册进去）。
- 页面标题/菜单标题优先直接用 `Langs.*.Resources.*`。

### 语言切换的隐藏点（会影响侧边栏标题刷新）

- 切换语言时会“刷新已注册页面标题”，但依赖 `SecRandom/App.Consts.cs` 里的 `PageNameProviders`。
- 新增页面如果希望语言切换时侧边栏标题跟着变：
  - 把 pageId 加进 `PageNameProviders`（否则标题可能停留在注册时的旧语言字符串）。

## 配置系统（必须理解）

- 配置文件路径由 `ConfigBase.ConfigFilePath` 决定（因此天然支持“可变路径/档案切换”的设计）。
- `ConfigHandlerBase` 默认监听 `PropertyChanged` 自动保存；`MainConfigHandler` 还会对语言/主题/字体等变更触发 UI 行为。
- 保存/读取 JSON 在 `SecRandom/Services/Config/DesktopConfigService.cs`。

### 配置集合类保存（易踩坑）

- `Dictionary` 内部增删改不会触发 `PropertyChanged`，不会自动保存。
- 正确姿势：每次更新都“整体替换属性值”（复制新字典再赋值）。
  - 本项目 list-specific overrides 就是这么做的：`DrawSettingsListSpecificViewModels.cs`。

## UI 常用小用法（写页面时直接拿来用）

- Toast：
  - 页面里直接 `this.ShowWarningToast(...) / ShowErrorToast(...)`
  - 不需要自己管理容器，MainView/SettingsView Loaded 时会注入 `AppToastAdorner`
- 文件夹驱动的下拉列表（名单/奖池名）：
  - 优先复用 `ListNamesSource`（FileSystemWatcher + debounce 自动刷新）

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
