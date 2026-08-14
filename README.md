<div align="center">

<img src="resources/secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**基于动态权重的公平随机工具，让抽取与决策告别争议**

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

SecRandom 是面向课堂、团队、活动、决策等场景的公平抽取应用

## 软件功能

### 抽取流程

- **点名**：支持普通随机、历史平衡及重复控制
- **闪抽**：通过独立悬浮窗快速抽取学生
- **抽奖**：支持奖品盘和库存抽取，学生与奖品独立管理
- **丰富呈现**：统一配置动画、结果、语音、音乐和通知，并支持通知失败回退

### 公平与名单管理

- 根据历史次数、抽取间隔、分组、性别等因素动态调整权重，降低重复与分布失衡
- 使用稳定内部标识维护历史，学号、编号和名称仅作为显示信息
- 支持多名单、多奖品池及 `.xlsx`、`.xls`、`.csv` 导入、映射和预览
- 每轮抽取均保存历史，方便查询和回顾

### 抽取结果可复查

- 每次抽取都会自动保存证明记录文件
- 可以选择让服务器参与并见证抽取过程
- 可以通过官方渠道重新检查抽取结果

### 数据、隐私与安全

- 设置、名单和历史记录都可以导入、导出、备份和恢复
- 备份可以包含名单、历史、抽取证明、图片、音频等信息，但不会包含密码等安全信息
- 支持使用密码、TOTP或 U 盘保护重要操作，并可设置哪些操作需要验证

### 验证边界

| 模式 | 可以做到 | 不能证明 |
|---|---|---|
| 离线证明 | 复查已完成的抽取过程 | 不是抽取前的服务器见证；不能证明本地程序或现实名单未被篡改 |
| 在线见证 | 保护服务端锁定后的抽取流程 | 不能证明名单真实、完整，或提交前未被筛选 |

## 技术演进

| 版本 | 技术栈 | 阶段 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代桌面实现 |
| v2 | Python + PySide6 + qfluentwidgets | Qt 技术栈演进 |
| **v3** | **C# + Avalonia + FluentAvalonia** | .NET 桌面重构，持续发展抽取、验证与桌面集成能力 |

## 下载与更新

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) 提供各版本的发行包与更新说明
- [官方下载页面](https://stk.sectl.cn/SecRandom) 提供下载最新版入口
- 自动更新在部署前验证已签名的发布清单以及制品的长度和哈希；请以每个发行版本提供的安装包和说明为准

## 许可证与第三方声明

- SecRandom 使用 [GNU GPLv3](LICENSE) 协议发布
- 第三方组件、版权和分发审查信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- SecRandom 通过历史平衡的权重与候选过滤策略帮助降低重复抽取、改善长期分布；它不替代对现实名单、规则或组织流程的管理，也不对这些现实条件作出软件无法验证的保证

## 贡献者和特别感谢

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

感谢每一位为 SecRandom 提交代码、报告问题、完善文档和提供反馈的贡献者。头像由 GitHub 贡献者数据动态生成，点击可前往 [GitHub 贡献者页面](https://github.com/SECTL/SecRandom/graphs/contributors) 查看完整统计

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
