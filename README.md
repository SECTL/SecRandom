<div align="center">

<img src="resources/secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**面向课堂与团队的随机抽取工具，提供可配置的抽取流程、历史管理与可验证的抽取记录。**

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

<div align="center">

![代码贡献统计](https://repobeats.axiom.co/api/embed/7d42538bcd781370672c00b6b6ecd5282802ee3d.svg "代码贡献统计图表")

</div>

> [!NOTE]
> SecRandom 以 GNU GPLv3 协议发布。您可以修改和再发布源代码，但再发布的衍生作品也必须遵循 GNU GPLv3。

## SecRandom v3

SecRandom v3 是面向课堂、班级和团队场景的桌面随机抽取应用。它将点名、快速抽取和奖品抽取放在统一的档案、历史与设置体系中，并提供可检查的抽取证明。

软件通过历史平衡的权重与候选过滤策略帮助降低重复抽取、改善长期分布；它不替代对现实名单、规则或组织流程的管理，也不对这些现实条件作出软件无法验证的保证。

## 软件功能

### 多种抽取流程

- **点名**：支持普通随机与历史平衡抽取，可配置重复、非重复或半重复等规则。
- **闪抽**：从独立悬浮窗快速发起学生抽取，不占用主界面工作流。
- **抽奖**：支持奖品盘和按库存数量抽取，学生与奖品分别维护候选池和历史。
- **呈现体验**：统一配置抽取动画、结果展示、语音、音乐与通知；内置通知可作为外部通知服务失败时的回退方式。

### 历史平衡与名单管理

- 根据历史次数、最近抽取间隔、分组和性别等上下文计算可配置权重，并提供冷启动和差距保护。
- 使用稳定的内部记录标识维护历史，显示的学号、编号或名称可以作为元数据而非唯一身份。
- 管理多个学生名单和奖品池；支持 `.xlsx`、`.xls`、`.csv` 导入、列映射与预览。
- 为每一轮抽取保存历史，便于按档案和抽取类型查看、整理与回顾。

### 公平抽取

- 公平抽取会在加密随机源的基础上，结合个人历史频率、最近抽取间隔、分组与性别分布计算候选权重，并应用冷启动、平均差距和候选池保护。
- 它旨在降低长期重复和分布失衡，具体效果取决于名单、规则和配置，而不是对任何现实结果作出绝对保证。

### 可验证抽取

- 普通抽取会立即保存本地可重放的 `.srproof.json` 证明，记录算法与输入摘要、结果及必要的匿名审计数据。
- 新生成的证明可在后台向验证服务提交一次，用于回放验证和签名收据；证明可导出，并可按保留期限和存储上限清理。
- 可显式选择正式在线见证模式：服务端先锁定匿名请求，再生成服务端随机材料并返回 `OnlineWitnessed` 证明。

### 数据、隐私与安全

- 档案、设置和完整数据支持导出、导入、手动备份与自动备份；导入仅接受 SecRandom v3 生成的清单或封装格式，写入前会创建恢复快照。
- 完整数据可包含名单、历史、证明、音频、课程联动和插件数据，但不包含安全凭据。
- 遥测上传与在线状态上报相互独立，可分别控制。
- 密码、TOTP 与 USB 绑定等可选保护使用独立凭据存储；可为抽取、重置、设置或外部调用配置授权要求。

### 集成、插件与交付

- 支持 CSES 课表和 ClassIsland 的可选课程联动。只有明确确认的课间状态才会按设置限制抽取；数据缺失、无效或状态未知时不会阻塞常规使用。
- ClassIsland 通知需要安装 `SecRandom4Ci` v2 插件。插件只能使用受限的声明式抽取接口，不能直接访问随机源、权重算法、可写历史或宿主服务。
- 更新中心会校验签名发布清单及制品完整性；便携包使用稳定启动器和版本化 `app-*` 应用载荷，方便在更新后保留数据目录。

## 验证模式与边界

**离线可复现证明**适合保存和复查普通抽取。它能让持有相同证明数据的人重放已记录的算法过程；后台签名收据可帮助发现证明提交后被修改的情况。它不是抽取前的服务器见证，也不能证明本地程序、现实候选名单或抽取前的操作未被篡改。

**正式在线见证**是用户主动选择的替代模式。服务端锁定匿名请求后才生成随机材料并计算结果；网络、服务或设备异常时，该模式等待明确结果，不能静默改用本地抽取。它能增强已锁定流程对本地代码、种子和证明替换的防护，但仍不能证明现实名单真实、完整，或证明候选池在提交前没有被筛选。

## 技术演进

| 版本 | 技术栈 | 阶段 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代桌面实现 |
| v2 | Python + PySide6 + qfluentwidgets | Qt 技术栈演进 |
| **v3** | **C# + Avalonia + FluentAvalonia** | .NET 桌面重构，持续发展抽取、验证、插件与桌面集成能力 |

## 下载与更新

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) 提供各版本的发行包与更新说明。
- [官方下载页面](https://stk.sectl.cn/SecRandom) 提供稳定版与测试版入口。
- 自动更新在部署前验证已签名的发布清单以及制品的长度和哈希；请以每个发行版本提供的安装包和说明为准。

## 许可证与第三方声明

SecRandom 使用 [GNU GPLv3](LICENSE) 协议发布。第三方组件、版权和分发审查信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 贡献者和特别感谢

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

感谢每一位为 SecRandom 提交代码、报告问题、完善文档和提供反馈的贡献者。头像由 GitHub 贡献者数据动态生成，点击可前往 [GitHub 贡献者页面](https://github.com/SECTL/SecRandom/graphs/contributors) 查看完整统计。

## 支持与社区

- [爱发电支持](https://afdian.com/a/lzy0983)
- [邮箱](mailto:lzy.12@foxmail.com)
- [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [QQ 频道](https://pd.qq.com/s/4x5dafd34?b=9)
- [Bilibili 主页](https://space.bilibili.com/520571577)
- [问题反馈](https://github.com/SECTL/SecRandom/issues)
- [SecRandom 官方文档](https://secrandom.sectl.top/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [简体中文贡献指南](CONTRIBUTING.md)

## Star History

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>

**Copyright © 2025-2026 SECTL**
