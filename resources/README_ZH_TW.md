<div align="center">

<image src="../resources/secrandom-icon-paper.png" height="128"/>

# SecRandom - 公平隨機選擇系統

🎯 **真正公平的隨機選擇算法** | 🚀 **現代化教育工具** | 🎨 **優雅的互動體驗**

> 本 Readme **由 AI 翻譯**，並由我們的開發人員審核。如果您發現任何錯誤，請向我們報告。
</div>

<!-- 專案狀態徽章 -->
<div align="center">

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=问题)](https://github.com/SECTL/SecRandom/issues)
[![最新版本](https://img.shields.io/github/v/release/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=最新正式版)](https://github.com/SECTL/SecRandom/releases/latest)
[![最新Beta版本](https://img.shields.io/github/v/release/SECTL/SecRandom?include_prereleases&style=for-the-badge&label=测试版)](https://github.com/SECTL/SecRandom/releases/)
[![上次更新](https://img.shields.io/github/last-commit/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=最后更新时间)](https://github.com/SECTL/SecRandom/commits/master)
[![下载统计](https://img.shields.io/github/downloads/SECTL/SecRandom/total?style=for-the-badge&color=00b4ab&label=累计下载)](https://github.com/SECTL/SecRandom/releases)

[![QQ群](https://img.shields.io/badge/-QQ%E7%BE%A4%EF%BD%9C833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![bilibili](https://img.shields.io/badge/-UP%E4%B8%BB%EF%BD%9C黎泽懿-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](https://opensource.org/licenses/GPL-3.0)

**語言選擇** [ [简体中文](../README.md) | [English](./README_EN.md) | **✔繁體中文** ]

![Code Contribution Statistics](https://repobeats.axiom.co/api/embed/7d42538bcd781370672c00b6b6ecd5282802ee3d.svg "Code Contribution Statistics Chart")

</div>

> [!NOTE]
>
> SecRandom 將在 GNU GPLv3 許可證下開源
>
> GNU GPLv3 具有 Copyleft 特性，這意味著您可以修改 SecRandom 的原始碼，但**必須也以 GNU GPLv3 許可證開源修改後的版本**
---------

## 📖 目錄

- [SecRandom - 公平隨機選擇系統](#secrandom---公平隨機選擇系統)
  - [📖 目錄](#-目錄)
  - [🎯 為何選擇公平選擇](#-為何選擇公平選擇)
  - [🌟 核心亮點](#-核心亮點)
    - [🎯 智能公平選擇系統](#-智能公平選擇系統)
    - [🎨 現代化用戶體驗](#-現代化用戶體驗)
    - [🚀 強大功能集](#-強大功能集)
    - [💻 系統兼容性](#-系統兼容性)
  - [📥 下載](#-下載)
    - [🌐 官方下載頁面](#-官方下載頁面)
  - [📸 軟體截圖](#-軟體截圖)
  - [🙏 貢獻者與特別感謝](#-貢獻者與特別感謝)
  - [第三方依賴與程式碼](#第三方依賴與程式碼)
    - [PythonNET-Stubs-Generator](#pythonnet-stubs-generator)
  - [💝 支持我們](#-支持我們)
    - [愛發電支援](#愛發電支援)
  - [📞 聯絡方式](#-聯絡方式)
  - [📄 官方文檔](#-官方文檔)
  - [貢獻指南與 Actions 構建工作流](#貢獻指南與-actions-構建工作流)
  - [✨ Star 歷程](#-star-歷程)

## 🎯 為何選擇公平選擇

傳統隨機選擇常常存在"某些人反覆被選中，而其他人很少被選中"的問題。SecRandom 使用**智能動態權重算法**結合**平均值差值保護機制**，確保每位成員都有公平的被選中機會：

- **避免重複選中**：被選中次數越多的人，再次被選中的概率越低
- **平衡群體機會**：確保不同群體的成員有相等的選中機會
- **性別平衡考慮**：在選擇過程中考慮不同性別的選中頻率平衡
- **冷啟動保護**：新成員或長期未被選中的成員不會因權重過低而失去機會
- **平均值過濾**：只允許選中次數≤平均值的成員進入候選池，避免過度選中
- **最大差距保護**：當最大選中次數與最小選中次數差距超過閾值時，排除極值並重新計算，確保公平性
- **候選池大小保障**：確保候選池不小於設定的最小人數，避免單人死循環
- **概率可視化**：實時顯示每位成員的選中概率，讓選擇過程透明可信

## 🌟 核心亮點

### 🎯 智能公平選擇系統

- ✅ **動態權重算法**：基於選中次數、群體、性別等多維度智能計算權重，確保每位成員獲得真正公平的選擇機會
- ✅ **冷啟動保護機制**：為新成員或長期未被選中的成員提供權重保護，避免因初始權重過低而失去機會
- ✅ **平均值差值保護**：結合平均值過濾和最大差距保護雙重機制，有效避免極端不均的選擇結果
- ✅ **靈活配置選項**：支持自定義差距閾值、最小候選池大小等核心參數，滿足不同場景需求
- ✅ **實時概率可視化**：直觀展示每位成員被選中的概率變化，讓選擇過程完全透明可信

### 🎨 現代化用戶體驗

- ✅ **Fluent Design 優雅介面**：採用微軟 Fluent Design 設計語言，支持淺色/深色主題自動切換
- ✅ **便捷浮窗模式**：可隨時呼出小型浮動窗口進行快速選擇，不影響當前工作流程
- ✅ **智能語音播報**：選擇結果自動語音播報，支持多種語音引擎和自定義音色設定

### 🚀 強大功能集

- ✅ **多樣化選擇模式**：支持單人選擇、多人選擇、群組選擇、性別選擇等多種模式，滿足不同場景需求
- ✅ **智能歷史記錄**：自動記錄選擇時間、結果等詳細資訊，支持按條件篩選和自動清理過期記錄
- ✅ **多列表管理系統**：支持導入/導出Excel列表，輕鬆管理多個班級或團隊的成員資訊

### 💻 系統兼容性

- ✅ **跨平台支持**：完美兼容 Windows 7/10/11 系統和主流 Linux 發行版
- ✅ **多架構適配**：原生支持 x64、x86 架構，適配不同硬體環境
- ✅ **開機自啟功能**：支持設定開機自動啟動，隨時可用（僅Windows系統）

## 📥 下載

### 🌐 官方下載頁面

- 📥 **[官方下載頁面](https://secrandom.sectl.top/download.html)** - 獲取最新穩定版本和測試版本

## 📸 軟體截圖

<details>
<summary>📸 軟體截圖展示 ✨</summary>

> [!WARNING]
> 目前SecRandom暫不支援繁體中文，您所看到的界面以簡體中文顯示。

<div align="center">

<img src="ScreenShots/zh-cn/pick.png" alt="點名界面" height="400px"/> <br/> <sub> 點名界面 </sub> <br/>
<img src="ScreenShots/zh-cn/lottery.png" alt="抽獎界面" height="400px"/> <br/> <sub> 抽獎界面 </sub> <br/>
<img src="ScreenShots/zh-cn/history.png" alt="歷史記錄" height="400px"/> <br/> <sub> 歷史記錄 </sub> <br/>
<img src="ScreenShots/zh-cn/pick_settings.png" alt="抽取設置" height="400px"/> <br/> <sub> 抽取設置 </sub> <br/>

</div>
</details>

## 🙏 貢獻者與特別感謝

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tbody>
    <tr>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/lzy98276"><img src="../data/assets/contribution/contributor1.png" width="100px;" alt="lzy98276"/><br /><sub><b>lzy98276 (黎澤懿_Aionflux)</b></sub></a><br /><a href="#content-lzy98276" title="Content">🖋</a> <a href="#design-lzy98276" title="Design">🎨</a> <a href="#ideas-lzy98276" title="Ideas, Planning, & Feedback">🤔</a> <a href="#maintenance-lzy98276" title="Maintenance">🚧</a> <a href="#doc-lzy98276" title="Documentation">📖</a> <a href="#bug-lzy98276" title="Bug reports">🐛</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/chenjintang-shrimp"><img src="../data/assets/contribution/contributor2.png" width="100px;" alt="chenjintang-shrimp"/><br /><sub><b>chenjintang-shrimp</b></sub></a><br /><a href="#code-chenjintang-shrimp" title="Code">💻</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/yuanbenxin"><img src="../data/assets/contribution/contributor3.png" width="100px;" alt="yuanbenxin"/><br /><sub><b>yuanbenxin (本新同學)</b></sub></a><br /><a href="#code-yuanbenxin" title="Code">💻</a> <a href="#design-yuanbenxin" title="Design">🎨</a> <a href="#maintenance-yuanbenxin" title="Maintenance">🚧</a> <a href="#doc-yuanbenxin" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/LeafS825"><img src="../data/assets/contribution/contributor4.png" width="100px;" alt="LeafS"/><br /><sub><b>LeafS</b></sub></a><br /><a href="#doc-LeafS" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/QiKeZhiCao"><img src="../data/assets/contribution/contributor5.png" width="100px;" alt="QiKeZhiCao"/><br /><sub><b>QiKeZhiCao (棄稞之草)</b></sub></a><br /><a href="#ideas-QiKeZhiCao" title="Ideas, Planning, & Feedback">🤔</a> <a href="#maintenance-QiKeZhiCao" title="Maintenance">🚧</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/Fox-block-offcial"><img src="../data/assets/contribution/contributor6.png" width="100px;" alt="Fox-block-offcial"/><br /><sub><b>Fox-block-offcial</b></sub></a><br /><a href="#bug-Fox-block-offcial" title="Bug reports">🐛</a> <a href="#testing-Fox-block-offcial" title="Testing">⚠️</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/jursin"><img src="../data/assets/contribution/contributor7.png" width="100px;" alt="Jursin"/><br /><sub><b>Jursin</b></sub></a><br /><a href="#code-jursin" title="Code">💻</a> <a href="#design-jursin" title="Design">🎨</a> <a href="#maintenance-jursin" title="Maintenance">🚧</a> <a href="#doc-jursin" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/LHGS-github"><img src="../data/assets/contribution/contributor8.png" width="100px;" alt="LHGS-github"/><br /><sub><b>LHGS-github</b></sub></a><br /><a href="#doc-LHGS-github" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/real01bit"><img src="../data/assets/contribution/contributor9.png" width="100px;" alt="real01bit"/><br /><sub><b>real01bit</b></sub></a><br /><a href="#code-real01bit" title="Code">💻</a></td>
    </tr>
  </tbody>
</table>

## 第三方依賴與程式碼

本專案使用了以下第三方程式碼：

### SoundFlow
- **用途**：跨平台本機抽取音樂播放（MP3、WAV、FLAC）
- **來源**：[LSXPrime/SoundFlow](https://github.com/LSXPrime/SoundFlow)
- **授權**：MIT License；隨附的 miniaudio 執行階段依 MIT 條款使用
- **聲明**：完整內容見 [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md)

### PythonNET-Stubs-Generator
- **路徑**: `vendors/pythonnet-stub-generator/`
- **來源**: [MHDante/pythonnet-stub-generator](https://github.com/MHDante/pythonnet-stub-generator)
- **授權**: MIT License (MIT 授權)
- **版權**
  - Copyright (c) 2019 Robert McNeel & Associates
  - Copyright (c) 2022 Dante Camarena
- **狀態**: 已修改編譯目標平台為 .NET 9.0
- *註：原始的 MIT License 文字保留於 `vendors/pythonnet-stub-generator/LICENSE.md` 中。*

## 💝 支持我們

如果您覺得 SecRandom 有幫助，歡迎支持我們的開發工作！

### 愛發電支援

> [!CAUTION]
> **愛發電是一個大陸網站。**在中國大陸之外，您可能不能正常訪問愛發電。

- 🌟 **[愛發電支援連接](https://afdian.com/a/lzy0983)** - 通過愛發電平臺支持開發者

## 📞 聯絡方式

* 📧 [電子郵件](mailto:lzy.12@foxmail.com)
* 👥 [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)
* 💬 [QQ頻道](https://pd.qq.com/s/4x5dafd34?b=9)
* 🎥 [B站主頁](https://space.bilibili.com/520571577)
* 🐛 [問題回饋](https://github.com/SECTL/SecRandom/issues)

## 📄 官方文檔

- 📄 **[SecRandom 官方文檔](https://secrandom.sectl.top)**
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)

## 貢獻指南與 Actions 構建工作流

查看我們的貢獻指南了解更多資訊：

- [繁體中文貢獻指南](./CONTRIBUTING_ZH_TW.md)

## ✨ Star 歷程

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>


**Copyright © 2025-2026 SECTL**
