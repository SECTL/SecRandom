# 命名空间

## SecRandom

主命名空间，存放大部分业务逻辑和 UI 层逻辑。

## SecRandom.Core

核心命名空间，存放大部分模型、部分核心业务逻辑，核心组件。

这个命名空间会开放给插件。

插件开放面集中在 `SecRandom.Core.Plugins`。这里仅放稳定 DTO 和受限接口，例如插件清单、权限、页面注册和 `IPluginDrawInvoker`。不要把宿主运行时、完整 DI、可写配置/Profile 服务或公平抽取内部算法放进插件开放面。

## SecRandom.Core.Tests

单元测试。目前暂不考虑。

## SecRandom.Shared

共享命名空间，存放部分核心模型，用于 IPC。
