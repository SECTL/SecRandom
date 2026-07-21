# 命名空间

## SecRandom

主命名空间，存放大部分业务逻辑和 UI 层逻辑。

## SecRandom.Core

核心命名空间，存放大部分模型、部分核心业务逻辑，核心组件。

这个命名空间会开放给插件。

插件开放面集中在 `SecRandom.Core.Plugins`，而 `SecRandom.Core.Views` 提供可由插件注册/调用的逻辑 Avalonia 视图契约。这里只放稳定 DTO 和受限接口，例如插件清单、权限、页面/视图注册、`IPluginDrawInvoker` 与 `IPluginViewService`。`Services/Config/FileConfigService`、`Services/Profiles/ProfileService`、临时记录和功能开关实现是 desktop/mobile Host 共享的内部运行时实现，不是插件 API。不要把物理宿主、完整 DI、可写配置/Profile 服务或公平抽取内部算法放进插件开放面。

## SecRandom.Core.Tests

单元测试。目前暂不考虑。

## SecRandom.Shared

共享命名空间，存放部分核心模型，用于 IPC。

## SecRandom.Platforms.Abstractions / SecRandom.Platforms

应用内部的平台能力边界。`SecRandom.Platforms.Abstractions` 只放中立能力契约、窗口特性请求/结果和句柄 DTO；`SecRandom.Platforms` 只放启动期上下文、DI 注册和 Stub。它们不属于插件 API，不能让插件、Core 或 Shared 反向依赖平台实现。

## SecRandom.Platforms.Windows / Linux / MacOs

各目标平台的原生实现边界。Win32、X11/EWMH、AppKit 等 API 只能放在各自项目中，App 的窗口和服务只能经已注册的窄平台接口调用。

## SecRandom.Mobile / SecRandom.Android / SecRandom.iOS

移动前端归并进主 `SecRandom` 应用程序集：`SecRandom.Mobile` 是组合和平台中立契约命名空间，页面、控件、服务和本地化分别位于 `SecRandom.Views.Mobile`、`SecRandom.Controls.Mobile`、`SecRandom.Services.Mobile`、`SecRandom.Langs.Mobile`。共享 `App` 在移动生命周期下先选择 app-private data root，再构建只含 Core/mobile 服务的 SingleView Host，并以 `MobileRootView` 启动；它不注册桌面窗口、托盘、快捷键、OOBE、插件或协议服务。`SecRandom.Android` 与 `SecRandom.iOS` 是仅含平台入口的 Exe 头项目（`BuildMobile=true` 时分别以 net10.0-android / net10.0-ios 构建，否则为中性 net10.0 空库），只挂接 Android/iOS 原生 seam，例如更新安装器、媒体播放器和启动诊断。
