# SecRandom 启动流程、配置系统和服务注册架构总结

## 1. 启动流程概览

**入口** → **语言初始化** → **DI容器构建** → **遥测初始化** → **Host启动** → **窗口创建**

### 关键文件
- SecRandom.Desktop/Program.cs - Avalonia 入口点
- SecRandom/App.axaml.cs - 应用生命周期和 DI 配置
- SecRandom/Views/FloatingWindow.axaml.cs - 主窗口（浮动工具栏）

### 启动顺序
1. Initialize() - 预加载配置文件，设置应用语言（在 DI 之前）
2. OnFrameworkInitializationCompleted() - 调用 BuildHost() 构建 DI 容器
3. BuildHost() - 注册所有服务、页面、ViewModel
4. StartRuntimeServicesAsync() - 先初始化遥测，再启动 Host
5. InitializeApp() - 配置任务栏图标和事件

### 窗口架构
- **FloatingWindow**: 桌面主窗口（浮动工具栏，始终显示）
- **MainWindow**: 主功能窗口（通过 App.ShowMainWindow() 按需创建）
- **SettingsWindow**: 设置界面（按需创建）
- **ProfileSettingsWindow**: 档案管理（按需创建）

## 2. 配置系统

### 配置文件路径
- 主配置: data/config/settings.json
- 学生名单: data/list/roll_call_list/{name}.json
- 奖品池: data/list/lottery_list/{name}.json
- 历史记录: data/history/{profile_name}_history.json

### 配置层次
MainConfigModel (根配置对象)
├─ General.Basic - 语言、启动选项、窗口置顶
├─ General.Backup - 备份配置
├─ General.PrivacySettings - 遥测和在线状态
├─ Appearance - 主题、字体、颜色
├─ RollCallSettings - 点名设置
├─ QuickDrawSettings - 快速抽取
├─ LotterySettings - 抽奖设置
└─ ... (其他功能设置)

### 配置处理器模式
ConfigHandlerBase<T>
├─ MainConfigHandler - 主配置
└─ ProfileConfigHandlerBase<T> - 档案配置
    ├─ StudentListConfig
    ├─ StudentHistoryConfig
    ├─ PrizeListConfig
    └─ PrizeHistoryConfig

### 持久化机制
- **DesktopConfigService** 实现文件读写
- **自动保存**: ConfigHandlerBase 监听 PropertyChanged 事件自动保存
- **JSON格式**: snake_case 命名，缩进格式化
- **迁移支持**: 通过 JsonPropertyName 映射遗留字段

## 3. 服务注册模式

### 服务生命周期

**单例服务 (AddSingleton)**:
- ConfigServiceBase, MainConfigHandler
- IProfileService, SettingsSearchService
- IPluginManager, TelemetryRuntimeService
- IVoiceAnnouncementService

**瞬态服务 (AddTransient)**:
- DrawEngine (每次抽取创建新实例)
- ViewModels (每个窗口独立实例)
- Views (页面视图)

**托管服务 (AddHostedService)**:
- PluginHostedService - 插件生命周期
- PluginCatalogHostedService - 插件目录扫描
- OnlineStatusService - 在线状态上报（BackgroundService 后台循环）
- TaskBarIconService - 任务栏图标管理

### IHostedService 使用模式

**同步启动** (StartAsync):
`csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    foreach (var plugin in plugins)
    {
        await plugin.OnLoadedAsync(context);
    }
}
`

**后台循环** (ExecuteAsync from BackgroundService):
`csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await DoWorkAsync();
        await Task.Delay(interval, stoppingToken);
    }
}
`

### 页面注册模式

**注册扩展方法**:
- AddMainPage<T>(name) - 注册主界面页面
- AddSettingsPage<T>(name) - 注册设置页面
- AddGroup(groupInfo) - 添加页面分组
- AddSettingsPageSeparator(location) - 添加分隔符

**双重注册机制**:
1. DI容器: services.AddKeyedTransient<UserControl, T>(pageId)
2. 导航表: PagesRegistryService.SettingsItems.Add(pageInfo)

**页面ID约定**:
- 主页面: main.rollCall, main.lottery, main.history；闪抽从浮窗入口打开，不注册为主导航页。
- 设置页面: settings.overview, settings.general.basic
- 插件页面: plugin.{plugin-id}.{page-name}

## 4. 跨平台考虑

### 平台检测
- OperatingSystem.IsWindows() / IsMacOS() / IsLinux()
- 任务栏图标点击事件仅在 Windows 上使用反射实现

### 文件路径
- 统一通过 Utils.GetFilePath(params string[]) 构造
- 基于 AppContext.BaseDirectory 的相对路径
- 自动创建不存在的目录

### 锁机制
- OnlineStatusService 使用 SemaphoreSlim 防止并发刷新 IP 缓存
- 配置保存使用锁保护（在 ConfigHandlerBase 中）

### 启动选项
- 自动启动: BasicSettings.Autostart (需平台特定实现)
- 后台常驻: BasicSettings.BackgroundResident
- URL协议: BasicSettings.UrlProtocol

## 5. 关键架构决策

### IAppHost 静态服务访问
`csharp
public interface IAppHost
{
    public static IHost? Host;
    public static T GetService<T>() => (T)Host.Services.GetService(typeof(T));
    public static T? TryGetService<T>() => (T?)Host.Services.GetService(typeof(T));
}
`
- 允许非 DI 上下文访问服务（如静态工具类）
- 用于 ProfileService 构造函数中获取依赖

### 配置变更响应
- ConfigHandlerBase 自动保存机制（PropertyChanged）
- OnlineStatusService 监听隐私设置变更清空缓存
- App.RefreshPersonalizedSettings() 应用主题/字体变更

### 生命周期钩子
- AppStarted 事件 - Host 启动后触发
- AppStopping 事件 - 停止前保存配置
- CurrentDomain.ProcessExit - 进程退出时强制保存
- Dispatcher.UIThread.UnhandledException - 全局异常捕获

### 关闭和重启
- **Stop()**: 保存配置 → 停止遥测 → 停止Host → 关闭桌面生命周期
- **Restart()**: Stop(不关闭生命周期) → 启动新进程 → 关闭当前进程
- FloatingWindow 使用 CanClose 标志防止意外关闭

## 6. 配置迁移策略

### 遗留字段处理
`csharp
[JsonPropertyName("basic")]  // 旧的根级字段
public BasicSettingsConfig LegacyBasicOnLoad
{
    set => General.ApplyLegacyBasic(value);  // 迁移到 General.Basic
}
`

### 隐私设置迁移
- 旧字段: Basic.telemetry_enabled, Basic.telemetry_mode
- 新位置: General.PrivacySettings.SentryTelemetryEnabled, OnlineStatusMode
- 加载时自动提取并迁移

---

**主要参考文件**:
- App.axaml.cs (BuildHost, 启动流程)
- DesktopConfigService.cs (配置持久化)
- ConfigHandlerBase.cs (配置处理器基类)
- ProfileService.cs (档案服务实现)
- OnlineStatusService.cs (后台服务示例)
- PagesRegistryExtensions.cs (页面注册)
