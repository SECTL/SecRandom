<div align="center">

<image src="../resources/secrandom-icon-paper.png" height="128"/>

# SecRandom - Fair Random Selection System

🎯 **Truly Fair Random Selection Algorithm** | 🚀 **Modern Educational Tools** | 🎨 **Elegant Interactive Experience**

> The Readme you are currently reading is **translated by AI** and reviewed by our developers. If you find any errors, please report it.
</div>

<!-- Project Status Badges -->
<div align="center">

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=Issues)](https://github.com/SECTL/SecRandom/issues)
[![Latest Release](https://img.shields.io/github/v/release/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=Latest%20Release)](https://github.com/SECTL/SecRandom/releases/latest)
[![Latest Beta](https://img.shields.io/github/v/release/SECTL/SecRandom?include_prereleases&style=for-the-badge&label=Beta)](https://github.com/SECTL/SecRandom/releases/)
[![Last Update](https://img.shields.io/github/last-commit/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=Last%20Update)](https://github.com/SECTL/SecRandom/commits/master)
[![Downloads](https://img.shields.io/github/downloads/SECTL/SecRandom/total?style=for-the-badge&color=00b4ab&label=Total%20Downloads)](https://github.com/SECTL/SecRandom/releases)

[![QQ Group](https://img.shields.io/badge/-QQ%20Group%7C833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![bilibili](https://img.shields.io/badge/-Bilibili%7C%E9%BB%8E%E6%B3%BD%E6%87%BF-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](https://opensource.org/licenses/GPL-3.0)

**Language** [ [简体中文](../README.md) | **✔English** | [繁體中文](./README_ZH_TW.md) ]
</div>

<div align="center">

![Code Contribution Statistics](https://repobeats.axiom.co/api/embed/7d42538bcd781370672c00b6b6ecd5282802ee3d.svg "Code Contribution Statistics Chart")

</div>

> [!NOTE]
>
> SecRandom will be open source under the GNU GPLv3 license
>
> GNU GPLv3 has Copyleft characteristics, which means you can modify the source code of SecRandom, but **must also open source the modified version under the GNU GPLv3 license**
---------

## 📖 Table of Contents

- [SecRandom - Fair Random Selection System](#secrandom---fair-random-selection-system)
  - [📖 Table of Contents](#-table-of-contents)
  - [🎯 Why Choose Fair Selection](#-why-choose-fair-selection)
  - [🌟 Core Features](#-core-features)
    - [🎯 Intelligent Fair Selection System](#-intelligent-fair-selection-system)
    - [🎨 Modern User Experience](#-modern-user-experience)
    - [🚀 Powerful Feature Set](#-powerful-feature-set)
    - [💻 System Compatibility](#-system-compatibility)
  - [📥 Download](#-download)
    - [🌐 Official Download Page](#-official-download-page)
  - [📸 Software Screenshots](#-software-screenshots)
  - [🙏 Contributors and Special Thanks](#-contributors-and-special-thanks)
  - [Third-Party Dependencies and Code](#third-party-dependencies-and-code)
    - [PythonNET-Stubs-Generator](#pythonnet-stubs-generator)
  - [💝 Support Us](#-support-us)
    - [Afdian Support](#afdian-support)
  - [📞 Contact](#-contact)
  - [📄 Official Documentation](#-official-documentation)
  - [Contributing Guidelines and Actions Build Workflow](#contributing-guidelines-and-actions-build-workflow)
  - [✨ Star History](#-star-history)

## 🎯 Why Choose Fair Selection

Traditional random selection often has the problem of "repeatedly selecting certain people while others are rarely selected". SecRandom uses an **intelligent dynamic weight algorithm** combined with **average gap protection mechanism** to ensure every member gets a fair chance of being selected:

- **Avoid Repeated Selection**: The more times someone is selected, the lower their probability of being selected again
- **Balance Group Opportunities**: Ensure members from different groups have equal selection opportunities
- **Gender Balance Consideration**: Balance selection frequency of different genders during the selection process
- **Cold Start Protection**: New members or those who haven't been selected for a long time won't lose opportunities due to low weight
- **Average Filtering**: Only allow members with selection count ≤ average to enter the candidate pool, avoiding over-selection
- **Maximum Gap Protection**: When the gap between max and min selection counts exceeds the threshold, exclude extremes and recalculate to ensure fairness
- **Candidate Pool Size Guarantee**: Ensure the candidate pool is not smaller than the set minimum size, avoiding single-person dead loops
- **Probability Visualization**: Real-time display of each member's selection probability, making the process transparent and trustworthy

## 🌟 Core Features

### 🎯 Intelligent Fair Selection System

- ✅ **Dynamic Weighting Algorithm**: Intelligently calculates weights based on multiple dimensions including selection count, group, and gender, ensuring every member gets a truly fair chance
- ✅ **Cold Start Protection Mechanism**: Provides weight protection for new members or those who haven't been selected for a long time, avoiding missed opportunities due to low initial weight
- ✅ **Average Gap Protection**: Combines dual mechanisms of average filtering and maximum gap protection to effectively avoid extremely uneven selection results
- ✅ **Flexible Configuration Options**: Supports customizing core parameters like gap threshold and minimum candidate pool size to meet different scenario needs
- ✅ **Real-time Probability Visualization**: Intuitively displays probability changes for each member being selected, making the selection process completely transparent and trustworthy

### 🎨 Modern User Experience

- ✅ **Fluent Design Elegant Interface**: Adopts Microsoft Fluent Design language, supporting automatic light/dark theme switching
- ✅ **Convenient Floating Window Mode**: Can call up a small floating window for quick selection at any time, without affecting current workflow
- ✅ **Smart Voice Announcement**: Automatically voice broadcasts selection results, supporting multiple voice engines and custom voice settings

### 🚀 Powerful Feature Set

- ✅ **Diverse Selection Modes**: Supports single selection, multiple selection, group selection, gender selection, etc., meeting different scenario needs
- ✅ **Smart History Records**: Automatically records detailed information such as selection time and results, supporting conditional filtering and automatic cleanup of expired records
- ✅ **Multi-list Management System**: Supports importing/exporting Excel lists, easily managing member information for multiple classes or teams

### 💻 System Compatibility

- ✅ **Cross-platform Support**: Perfectly compatible with Windows 7/10/11 systems and mainstream Linux distributions
- ✅ **Multi-architecture Adaptation**: Natively supports x64 and x86 architectures, adapting to different hardware environments
- ✅ **Startup on Boot Function**: Supports setting automatic startup on boot, always available (Windows only)

## 📥 Download

### 🌐 Official Download Page

- 📥 **[Official Download Page](https://secrandom.sectl.top/download.html)** - Get the latest stable version and beta versions

## 📸 Software Screenshots

<details>
<summary>📸 Software Screenshots Display ✨</summary>

<div align="center">

<img src="ScreenShots/en_us/pick.png" alt="Pick Interface" height="400px"/> <br/> <sub> Pick Interface </sub> <br/>
<img src="ScreenShots/en_us/lottery.png" alt="Lottery Interface" height="400px"/> <br/> <sub> Lottery Interface </sub> <br/>
<img src="ScreenShots/en_us/history.png" alt="History Records" height="400px"/> <br/> <sub> History Records </sub> <br/>
<img src="ScreenShots/en_us/pick_settings.png" alt="Pick Settings" height="400px"/> <br/> <sub> Pick Settings </sub> <br/>

</div>
</details>

## 🙏 Contributors and Special Thanks

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tbody>
    <tr>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/lzy98276"><img src="../data/assets/contribution/contributor1.png" width="100px;" alt="lzy98276"/><br /><sub><b>lzy98276 (黎泽懿_Aionflux)</b></sub></a><br /><a href="#content-lzy98276" title="Content">🖋</a> <a href="#design-lzy98276" title="Design">🎨</a> <a href="#ideas-lzy98276" title="Ideas, Planning, & Feedback">🤔</a> <a href="#maintenance-lzy98276" title="Maintenance">🚧</a> <a href="#doc-lzy98276" title="Documentation">📖</a> <a href="#bug-lzy98276" title="Bug reports">🐛</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/chenjintang-shrimp"><img src="../data/assets/contribution/contributor2.png" width="100px;" alt="chenjintang-shrimp"/><br /><sub><b>chenjintang-shrimp</b></sub></a><br /><a href="#code-chenjintang-shrimp" title="Code">💻</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/yuanbenxin"><img src="../data/assets/contribution/contributor3.png" width="100px;" alt="yuanbenxin"/><br /><sub><b>yuanbenxin (本新同学)</b></sub></a><br /><a href="#code-yuanbenxin" title="Code">💻</a> <a href="#design-yuanbenxin" title="Design">🎨</a> <a href="#maintenance-yuanbenxin" title="Maintenance">🚧</a> <a href="#doc-yuanbenxin" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/LeafS825"><img src="../data/assets/contribution/contributor4.png" width="100px;" alt="LeafS"/><br /><sub><b>LeafS</b></sub></a><br /><a href="#doc-LeafS" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/QiKeZhiCao"><img src="../data/assets/contribution/contributor5.png" width="100px;" alt="QiKeZhiCao"/><br /><sub><b>QiKeZhiCao (弃稞之草)</b></sub></a><br /><a href="#ideas-QiKeZhiCao" title="Ideas, Planning, & Feedback">🤔</a> <a href="#maintenance-QiKeZhiCao" title="Maintenance">🚧</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/Fox-block-offcial"><img src="../data/assets/contribution/contributor6.png" width="100px;" alt="Fox-block-offcial"/><br /><sub><b>Fox-block-offcial</b></sub></a><br /><a href="#bug-Fox-block-offcial" title="Bug reports">🐛</a> <a href="#testing-Fox-block-offcial" title="Testing">⚠️</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/jursin"><img src="../data/assets/contribution/contributor7.png" width="100px;" alt="Jursin"/><br /><sub><b>Jursin</b></sub></a><br /><a href="#code-jursin" title="Code">💻</a> <a href="#design-jursin" title="Design">🎨</a> <a href="#maintenance-jursin" title="Maintenance">🚧</a> <a href="#doc-jursin" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/LHGS-github"><img src="../data/assets/contribution/contributor8.png" width="100px;" alt="LHGS-github"/><br /><sub><b>LHGS-github</b></sub></a><br /><a href="#doc-LHGS-github" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/real01bit"><img src="../data/assets/contribution/contributor9.png" width="100px;" alt="real01bit"/><br /><sub><b>real01bit</b></sub></a><br /><a href="#code-real01bit" title="Code">💻</a></td>
    </tr>
  </tbody>
</table>

## Third-Party Dependencies and Code

This project uses the following third-party code:

### SoundFlow
- **Purpose**: Cross-platform local draw-music playback (MP3, WAV, and FLAC)
- **Source**: [LSXPrime/SoundFlow](https://github.com/LSXPrime/SoundFlow)
- **License**: MIT License; the bundled miniaudio runtime is used under MIT terms
- **Notice**: See [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) for the full notice

### PythonNET-Stubs-Generator
- **Path**: `vendors/pythonnet-stub-generator/`
- **Source**: [MHDante/pythonnet-stub-generator](https://github.com/MHDante/pythonnet-stub-generator)
- **License**: MIT License
- **Copyright**
  - Copyright (c) 2019 Robert McNeel & Associates
  - Copyright (c) 2022 Dante Camarena
- **Status**: Modified compilation target platform to .NET 9.0
- *Note: The original MIT License text is preserved in `vendors/pythonnet-stub-generator/LICENSE.md`.*

## 💝 Support Us

If you find SecRandom helpful, you're welcome to support our development work!

### Afdian Support

> [!CAUTION]
> **Afdian is a Chinese-based donation platform.** You may not use Afdian out of mainland China.

- 🌟 **[Afdian Support Link](https://afdian.com/a/lzy0983)** - Support developers through the Afdian platform

## 📞 Contact

* 📧 [Email](mailto:lzy.12@foxmail.com)
* 👥 [QQ Group 833875216](https://qm.qq.com/q/iWcfaPHn7W)
* 💬 [QQ Channel](https://pd.qq.com/s/4x5dafd34?b=9)
* 🎥 [Bilibili Homepage](https://space.bilibili.com/520571577)
* 🐛 [Issue Report](https://github.com/SECTL/SecRandom/issues)

## 📄 Official Documentation

- 📄 **[SecRandom Official Documentation](https://secrandom.sectl.top)**
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)

## Contributing Guidelines and Actions Build Workflow

Check out our Contributing Guidelines to learn more:

- [English Contributing Guidelines](./CONTRIBUTING_EN.md)

## ✨ Star History

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>


**Copyright © 2025-2026 SECTL**
