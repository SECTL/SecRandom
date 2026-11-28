<div align="center">

<img src="resources/secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**面向课堂的开源抽取与互动工具，让点名、抽奖与课堂联动更加方便、透明**

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=问题)](https://github.com/SECTL/SecRandom/issues)
[![最新版本](https://img.shields.io/github/v/release/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=最新正式版)](https://github.com/SECTL/SecRandom/releases/latest)
[![测试版本](https://img.shields.io/github/v/release/SECTL/SecRandom?include_prereleases&style=for-the-badge&label=测试版)](https://github.com/SECTL/SecRandom/releases)
[![最后更新](https://img.shields.io/github/last-commit/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=最后更新时间)](https://github.com/SECTL/SecRandom/commits/master)
[![累计下载](https://img.shields.io/github/downloads/SECTL/SecRandom/total?style=for-the-badge&color=00b4ab&label=累计下载)](https://github.com/SECTL/SecRandom/releases)

[![QQ群](https://img.shields.io/badge/-QQ%E7%BE%A4%20%7C%20833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![Bilibili](https://img.shields.io/badge/-Bilibili%20%7C%20%E9%BB%8E%E6%B3%BD%E6%87%BF-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE)

**语言** [ **简体中文** | [English](resources/README_EN.md) | [日本語](resources/README_JA.md) ]

</div>

> [!NOTE]
> SecRandom 以 GNU GPLv3 协议发布！您可以修改和再发布源代码，但再发布的衍生作品也必须遵循 GNU GPLv3

## SecRandom

SecRandom 是一款面向课堂教学的开源抽取与互动工具。它支持点名、闪抽、抽奖和随机数抽取，提供随机、公平与编排等抽取方式，并结合名单管理、历史记录、结果验证和课程联动，满足日常教学、公开课与课堂活动的不同需求。

## 下载与开始使用

- 从 [GitHub Releases](https://github.com/SECTL/SecRandom/releases) 或 [官方下载页面](https://sectl.cn/download/SecRandom) 获取发行包，安装说明和支持的架构以对应版本为准。
- 桌面端提供 Windows、macOS 和 Linux 发行包；移动端提供 Android 和 iOS 构建。各平台的界面和功能有所不同，具体以发行包及其说明为准。
- 打开软件后，在设置中的名单管理添加或导入学生名单，然后在点名页面选择名单并开始抽取。也可以创建奖池进行抽奖，或选择随机数范围进行抽取。
- 使用过程中的详细说明见 [SecRandom 官方文档](https://secrandom.sectl.cn/doc/overview.html)。

## 主要功能

### 课堂抽取

- **点名与闪抽**：在课堂上抽取学生，桌面端还可通过悬浮窗快速操作。
- **抽奖与随机数**：管理奖池和库存进行抽奖，也可以在指定范围内抽取随机数。
- **灵活的抽取方式**：按需要选择随机抽取、公平抽取或编排抽取，并设置重复规则。公平抽取结合历史次数、间隔等因素调整权重；编排抽取按事先设置的规则和顺序执行，适合公开课和演示等场景。

### 名单、历史与课堂呈现

- 管理多个学生名单和奖池，桌面端支持导入 `.xlsx`、`.xls` 和 `.csv`，并提供列映射与预览。
- 查询、分组和导出点名与抽奖历史，使用稳定的内部标识关联成员和记录。随机数抽取单独统计，不写入名单历史。
- 桌面端提供抽取动画、语音、音乐、浮窗及全屏通知等呈现方式，可根据课堂需要调整。

### 课堂联动与扩展

- 桌面端可结合课程表或 ClassIsland 切换课程对应的名单和奖池，也可通过悬浮窗访问常用操作。
- 支持集控：桌面设备接入后，可通过移动端账号控制台选择在线设备并发起远程抽取。
- 桌面端支持插件、集成 SDK 和自动化接口，方便扩展功能和连接其他应用。

### 数据与安全

- 支持备份和恢复设置、名单、历史及相关数据；备份不包含密码等安全凭据。
- 可使用密码、TOTP 或 U 盘保护重要操作，并选择需要验证的操作。

## 公平与结果验证

公平抽取使用历史记录和候选过滤策略，帮助减少重复、改善长期分布，但不保证每次结果完全均匀。选择编排抽取时，结果由预设规则影响，不应视作随机抽取。

桌面端支持保存抽取证明并在本地验证、回放；离线证明可在后台提交服务端认证，并请求第三方时间戳，网络不可用时认证可能延后或失败。另有可选择的在线见证流程，由服务端参与并锁定抽取过程。具体可用性和验证方式以软件内说明为准。

证明用于复查所记录的过程，不能证明抽取前名单真实、完整或未被筛选，也不能排除离线抽取前反复试验。软件无法替代对名单、规则和课堂流程的管理。

## 技术演进

| 版本 | 技术栈 | 阶段 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代桌面实现 |
| v2 | Python + PySide6 + qfluentwidgets | Qt 技术栈演进 |
| **v3** | **C# + Avalonia + FluentAvalonia** | 面向桌面与移动端，持续发展抽取、验证和课堂联动能力 |

## 许可证与第三方声明

- SecRandom 使用 [GNU GPLv3](LICENSE) 协议发布
- 第三方组件、版权和分发审查信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- SecRandom 通过历史平衡的权重与候选过滤策略帮助降低重复抽取、改善长期分布；它不替代对现实名单、规则或组织流程的管理，也不对这些现实条件作出软件无法验证的保证

## 贡献者和特别感谢

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

感谢每一位为 SecRandom 提交代码、报告问题、完善文档和提供反馈的贡献者。头像由 GitHub 贡献者数据动态生成，点击可前往 [GitHub 贡献者页面](https://github.com/SECTL/SecRandom/graphs/contributors) 查看完整统计

感谢 Dubi906w 提供的 UIAccess 任务栏图标修复方法。代码来自 [LegacyIsland / UIAccessX](https://github.com/segf4ultk1nger/dubiisland)。

## 支持与社区

- [爱发电支持](https://afdian.com/a/lzy0983)
- [邮箱](mailto:lzy.12@foxmail.com)
- [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [QQ 频道](https://pd.qq.com/s/4x5dafd34?b=9)
- [Bilibili 主页](https://space.bilibili.com/520571577)
- [问题反馈](https://github.com/SECTL/SecRandom/issues)
- [SecRandom 官方文档](https://secrandom.sectl.cn/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [简体中文贡献指南](CONTRIBUTING.md)

## Star History

<a href="https://www.star-history.com/?repos=SECTL%2FSecRandom&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&theme=dark&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
 </picture>
</a>

**Copyright © 2025-2026 SECTL**
