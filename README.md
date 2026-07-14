<div align="center">

<image src="resources/secrandom-icon-paper.png" width="128" height="128" />

# SecRandom - 公平随机抽取系统

🎯 **真正公平的抽取算法** | 🚀 **现代化教育工具** | 🎨 **优雅交互体验**

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=问题)](https://github.com/SECTL/SecRandom/issues)
[![最新版本](https://img.shields.io/github/v/release/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=最新正式版)](https://github.com/SECTL/SecRandom/releases/latest)
[![最新Beta版本](https://img.shields.io/github/v/release/SECTL/SecRandom?include_prereleases&style=for-the-badge&label=测试版)](https://github.com/SECTL/SecRandom/releases/)
[![上次更新](https://img.shields.io/github/last-commit/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=最后更新时间)](https://github.com/SECTL/SecRandom/commits/master)
[![下载统计](https://img.shields.io/github/downloads/SECTL/SecRandom/total?style=for-the-badge&color=00b4ab&label=累计下载)](https://github.com/SECTL/SecRandom/releases)

[![QQ群](https://img.shields.io/badge/-QQ%E7%BE%A4%EF%BD%9C833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![bilibili](https://img.shields.io/badge/-UP%E4%B8%BB%EF%BD%9C黎泽懿-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](https://opensource.org/licenses/GPL-3.0)

**语言选择** [ **✔简体中文** | [English](resources/README_EN.md) | [繁體中文](resources/README_ZH_TW.md) ]

</div>

<div align="center">

![代码贡献统计](https://repobeats.axiom.co/api/embed/7d42538bcd781370672c00b6b6ecd5282802ee3d.svg "代码贡献统计图表")

</div>

> [!NOTE]
>
> SecRandom 本体将基于GNU GPLv3协议开源
>
> GNU GPLv3具有Copyleft特性，也就是说，您可以修改SecRandom的源代码，但是**必须将修改版本同样以GNU GPLv3协议开源**
--------

## 📖 目录

- [SecRandom - 公平随机抽取系统](#secrandom---公平随机抽取系统)
  - [📖 目录](#-目录)
  - [🎯 为什么选择公平抽取](#-为什么选择公平抽取)
  - [🌟 核心亮点](#-核心亮点)
    - [🎯 智能公平抽取系统](#-智能公平抽取系统)
    - [🎨 现代化用户体验](#-现代化用户体验)
    - [🚀 强大功能集](#-强大功能集)
    - [💻 系统兼容性](#-系统兼容性)
  - [📥 下载](#-下载)
    - [🌐 官方下载页面](#-官方下载页面)
  - [📸 软件截图](#-软件截图)
  - [🙏 贡献者和特别感谢](#-贡献者和特别感谢)
  - [第三方依赖与代码](#第三方依赖与代码)
    - [PythonNET-Stubs-Generator](#pythonnet-stubs-generator)
  - [💝 捐献支持](#-捐献支持)
    - [爱发电支持](#爱发电支持)
  - [📞 联系方式](#-联系方式)
  - [📄 官方文档](#-官方文档)
  - [贡献指南与 Actions 构建工作流](#贡献指南与-actions-构建工作流)
  - [✨ Star历程](#-star历程)

## 🎯 为什么选择公平抽取

传统的随机抽取往往存在"重复抽取某些人，而另一些人长期不被抽中"的问题。SecRandom 采用**智能动态权重算法**结合**平均值差值保护机制**，确保每位成员都能获得公平的抽取机会：

- **避免重复抽取**：被抽中次数越多，再次被抽中的概率越低
- **平衡小组机会**：确保不同小组的成员有均等的抽取机会
- **性别均衡考虑**：在抽取过程中平衡不同性别的抽取频率
- **冷启动保护**：新成员或长期未被抽中的成员不会因为权重过低而失去机会
- **平均值过滤**：只允许抽取次数≤平均值的成员进入候选池，避免过度抽取
- **最大差距保护**：当最大抽取次数与最小抽取次数差距超过阈值时，排除极值并重新计算，确保公平性
- **候选池大小保障**：确保候选池不小于设定的最小人数，避免单人死循环
- **可视化概率**：实时显示每位成员被抽中的概率，过程透明可信

## 🌟 核心亮点

### 🎯 智能公平抽取系统

- ✅ **动态权重算法**：基于抽取次数、小组、性别等多维度智能计算权重，确保每位成员获得真正公平的抽取机会
- ✅ **冷启动保护机制**：为新成员或长期未被抽中的成员提供权重保护，避免因初始权重过低而失去机会
- ✅ **平均值差值保护**：结合平均值过滤和最大差距保护双重机制，有效避免极端不均的抽取结果
- ✅ **灵活配置选项**：支持自定义差距阈值、最小候选池大小等核心参数，满足不同场景需求
- ✅ **实时概率可视化**：直观展示每位成员被抽中的概率变化，让抽取过程完全透明可信

### 🎨 现代化用户体验

- ✅ **Fluent Design 优雅界面**：采用微软 Fluent Design 设计语言，支持浅色/深色主题自动切换
- ✅ **便捷悬浮窗模式**：可随时呼出小型悬浮窗口进行快速抽取，不影响当前工作流程
- ✅ **智能语音播报**：抽取结果自动语音播报，支持多种语音引擎和自定义音色设置

### 🚀 强大功能集

- ✅ **多样化抽取模式**：支持单人抽取、多人抽取、小组抽取、性别抽取等多种模式，满足不同场景需求
- ✅ **智能历史记录**：自动记录抽取时间、结果等详细信息，支持按条件筛选和自动清理过期记录
- ✅ **多名单管理系统**：支持导入/导出Excel名单，轻松管理多个班级或团队的成员信息

### 💻 系统兼容性

- ✅ **跨平台支持**：完美兼容 Windows 10/11 系统和主流 Linux 发行版
- ✅ **多架构适配**：原生支持 x64 架构，适配不同硬件环境
- ✅ **开机自启功能**：支持设置开机自动启动，随时可用（仅Windows系统）

## 📥 下载

### 🌐 官方下载页面

- 📥 **[官方下载页面](https://secrandom.sectl.top/download.html)** - 获取最新稳定版本和测试版本

## 📸 软件截图

<details>
<summary>📸 软件截图展示 ✨</summary>

<div align="center">

<img src="resources/ScreenShots/zh-cn/pick.png" alt="点名界面" height="400px"/> <br/> <sub> 点名界面 </sub> <br/>
<img src="resources/ScreenShots/zh-cn/lottery.png" alt="抽奖界面" height="400px"/> <br/> <sub> 抽奖界面 </sub> <br/>
<img src="resources/ScreenShots/zh-cn/history.png" alt="历史记录" height="400px"/> <br/> <sub> 历史记录 </sub> <br/>
<img src="resources/ScreenShots/zh-cn/pick_settings.png" alt="抽取设置" height="400px"/> <br/> <sub> 抽取设置 </sub> <br/>

</div>

</details>

## 🙏 贡献者和特别感谢

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tbody>
    <tr>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/lzy98276"><img src="data/assets/contribution/contributor1.png" width="100px;" alt="lzy98276"/><br /><sub><b>lzy98276 (黎泽懿_Aionflux)</b></sub></a><br /><a href="#content-lzy98276" title="Content">🖋</a> <a href="#design-lzy98276" title="Design">🎨</a> <a href="#ideas-lzy98276" title="Ideas, Planning, & Feedback">🤔</a> <a href="#maintenance-lzy98276" title="Maintenance">🚧</a> <a href="#doc-lzy98276" title="Documentation">📖</a> <a href="#bug-lzy98276" title="Bug reports">🐛</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/chenjintang-shrimp"><img src="data/assets/contribution/contributor2.png" width="100px;" alt="chenjintang-shrimp"/><br /><sub><b>chenjintang-shrimp</b></sub></a><br /><a href="#code-chenjintang-shrimp" title="Code">💻</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/yuanbenxin"><img src="data/assets/contribution/contributor3.png" width="100px;" alt="yuanbenxin"/><br /><sub><b>yuanbenxin (本新同学)</b></sub></a><br /><a href="#code-yuanbenxin" title="Code">💻</a> <a href="#design-yuanbenxin" title="Design">🎨</a> <a href="#maintenance-yuanbenxin" title="Maintenance">🚧</a> <a href="#doc-yuanbenxin" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/LeafS825"><img src="data/assets/contribution/contributor4.png" width="100px;" alt="LeafS"/><br /><sub><b>LeafS</b></sub></a><br /><a href="#doc-LeafS" title="Documentation">📖</a> <a href="#ideas-LeafS" title="Ideas, Planning, & Feedback">🤔</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/QiKeZhiCao"><img src="data/assets/contribution/contributor5.png" width="100px;" alt="QiKeZhiCao"/><br /><sub><b>QiKeZhiCao (弃稞之草)</b></sub></a><br /><a href="#ideas-QiKeZhiCao" title="Ideas, Planning, & Feedback">🤔</a> <a href="#maintenance-QiKeZhiCao" title="Maintenance">🚧</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/Fox-block-offcial"><img src="data/assets/contribution/contributor6.png" width="100px;" alt="Fox-block-offcial"/><br /><sub><b>Fox-block-offcial</b></sub></a><br /><a href="#bug-Fox-block-offcial" title="Bug reports">🐛</a> <a href="#testing-Fox-block-offcial" title="Testing">⚠️</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/jursin"><img src="data/assets/contribution/contributor7.png" width="100px;" alt="Jursin"/><br /><sub><b>Jursin</b></sub></a><br /><a href="#code-jursin" title="Code">💻</a> <a href="#design-jursin" title="Design">🎨</a> <a href="#maintenance-jursin" title="Maintenance">🚧</a> <a href="#doc-jursin" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/LHGS-github"><img src="data/assets/contribution/contributor8.png" width="100px;" alt="LHGS-github"/><br /><sub><b>LHGS-github</b></sub></a><br /><a href="#doc-LHGS-github" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/real01bit"><img src="data/assets/contribution/contributor9.png" width="100px;" alt="real01bit"/><br /><sub><b>real01bit</b></sub></a><br /><a href="#code-real01bit" title="Code">💻</a></td>
    </tr>
  </tbody>
</table>

<!-- markdownlint-restore -->
<!-- prettier-ignore-end -->

<!-- ALL-CONTRIBUTORS-LIST:END -->

## 第三方依赖与代码

本项目使用了以下第三方代码：

### SoundFlow
- **用途**：跨平台本地抽取音乐播放（MP3、WAV、FLAC）
- **来源**：[LSXPrime/SoundFlow](https://github.com/LSXPrime/SoundFlow)
- **许可证**：MIT License；随附的 miniaudio 运行时使用 MIT 条款
- **说明**：完整声明见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)

### PythonNET-Stubs-Generator
- **路径**：`vendors/pythonnet-stub-generator/`
- **来源**：[MHDante/pythonnet-stub-generator](https://github.com/MHDante/pythonnet-stub-generator)
- **许可证**：MIT License
- **版权**
  - Copyright (c) 2019 Robert McNeel & Associates
  - Copyright (c) 2022 Dante Camarena
- **状态**：修改了编译目标平台为 .NET 9.0
- *注：原始 MIT License 文本保留在 `vendors/pythonnet-stub-generator/LICENSE.md` 中。*

## 💝 捐献支持

如果您觉得 SecRandom 对您有帮助，欢迎支持我们的开发工作！

### 爱发电支持

- 🌟 **[爱发电支持链接](https://afdian.com/a/lzy0983)** - 通过爱发电平台支持开发者

## 📞 联系方式

- 📧 [邮箱](mailto:lzy.12@foxmail.com)
- 👥 [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- 💬 [QQ 频道](https://pd.qq.com/s/4x5dafd34?b=9)
- 🎥 [B站主页](https://space.bilibili.com/520571577)
- 🐛 [问题反馈](https://github.com/SECTL/SecRandom/issues)

## 📄 官方文档

- 📄 **[SecRandom 官方文档](https://secrandom.sectl.top/doc/overview.html)**
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)

## 贡献指南与 Actions 构建工作流

查看我们的贡献指南来了解更多内容：

- [简体中文贡献指南](./CONTRIBUTING.md)

## ✨ Star历程

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>


**Copyright © 2025-2026 SECTL**
