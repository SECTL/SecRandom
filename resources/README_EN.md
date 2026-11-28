<div align="center">

<img src="secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**An open-source classroom draw and interaction tool for roll calls, lotteries, and connected teaching workflows.**

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=Issues)](https://github.com/SECTL/SecRandom/issues)
[![Latest Release](https://img.shields.io/github/v/release/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=Latest%20Release)](https://github.com/SECTL/SecRandom/releases/latest)
[![Pre-release](https://img.shields.io/github/v/release/SECTL/SecRandom?include_prereleases&style=for-the-badge&label=Pre-release)](https://github.com/SECTL/SecRandom/releases)
[![Last Update](https://img.shields.io/github/last-commit/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=Last%20Update)](https://github.com/SECTL/SecRandom/commits/master)
[![Downloads](https://img.shields.io/github/downloads/SECTL/SecRandom/total?style=for-the-badge&color=00b4ab&label=Downloads)](https://github.com/SECTL/SecRandom/releases)

[![QQ Group](https://img.shields.io/badge/-QQ%20Group%20%7C%20833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![Bilibili](https://img.shields.io/badge/-Bilibili%20%7C%20%E9%BB%8E%E6%B3%BD%E6%87%BF-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](../LICENSE)

**Language** [ [简体中文](../README.md) | **English** | [日本語](README_JA.md) ]

</div>

> [!NOTE]
> SecRandom is released under GNU GPLv3. You may modify and redistribute the source, but derivative redistributions must also use GNU GPLv3.

## SecRandom

SecRandom is an open-source draw and interaction tool designed for teaching. It brings together roll calls, quick draws, lotteries, and random-number draws with configurable random, fair, and orchestrated selection, roster management, history, result verification, and course integration. It supports everyday lessons, demonstration classes, and classroom activities.

## Download and Get Started

- Download a release from [GitHub Releases](https://github.com/SECTL/SecRandom/releases) or the [official download page](https://stk.sectl.cn/SecRandom). Check the release notes for installation instructions and supported architectures.
- Desktop packages are built for Windows, macOS, and Linux; mobile builds are available for Android and iOS. Interfaces and features vary by platform; refer to the package and its release notes for availability.
- Open the app, add or import a student roster in settings, select it on the roll-call page, and start a draw. You can also create a prize pool for a lottery or draw a number from a specified range.
- See the [official documentation](https://secrandom.sectl.cn/doc/overview.html) for more details.

## Features

### Classroom Draws

- **Roll call and quick draw**: Select students during a lesson, with a floating window for quick access on desktop.
- **Lottery and random numbers**: Manage prize pools and inventory, or draw a number from a chosen range.
- **Selection methods**: Choose random, fair, or orchestrated draws and configure repeat rules. Fair draws adjust weights using factors such as history and draw intervals. Orchestrated draws follow rules and an order set in advance, for demonstration classes and similar situations.

### Rosters, History, and Presentation

- Manage multiple student rosters and prize pools. On desktop, import `.xlsx`, `.xls`, and `.csv` files with column mapping and preview.
- Review, group, and export roll-call and lottery history, with stable internal identifiers linking members to records. Random-number draws are counted separately and do not write roster history.
- Configure desktop animations, speech, music, floating notifications, and full-screen results for the classroom.

### Classroom Integration and Extensions

- On desktop, use a timetable or ClassIsland to switch rosters and prize pools by course, and access common actions from the floating window.
- Connect desktop devices to the control center and use the mobile account console to select online devices and start remote draws.
- Desktop plugins, the integration SDK, and automation interfaces allow extensions and connections to other applications.

### Data and Security

- Back up and restore settings, rosters, history, and related data. Backups exclude passwords and other security credentials.
- Protect selected operations with a password, TOTP, or USB drive.

## Fairness and Verification

Fair draws use history and candidate filters to help reduce repeats and improve long-term distribution; they do not guarantee an even result each time. Orchestrated draws are influenced by preset rules and should not be presented as random selection.

On desktop, draw proofs can be saved, verified, and replayed locally. Offline proofs can be submitted for server attestation and a third-party timestamp in the background; network failures may delay or prevent attestation. An optional online witnessing flow also involves the server in locking the draw process. See the in-app descriptions for availability and verification options.

A proof helps review the recorded process. It cannot establish that a roster was authentic, complete, or unfiltered before a draw, or rule out repeated offline trials beforehand. The software does not replace management of rosters, rules, and classroom procedures.

## Technical evolution

| Version | Stack | Stage |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | First desktop implementation |
| v2 | Python + PySide6 + qfluentwidgets | Qt stack evolution |
| **v3** | **C# + Avalonia + FluentAvalonia** | Desktop and mobile development, with continued work on draws, verification, and classroom integration |

## License and third-party notices

- SecRandom is released under [GNU GPLv3](../LICENSE).
- See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for third-party components, copyright information, and distribution-review notes.
- History-balanced weights and candidate filters help reduce repeat selections and improve long-term distribution. They do not replace management of real-world rosters, rules, or processes, and SecRandom does not claim to verify those conditions.

## Contributors and special thanks

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

Thank you to everyone who contributes code, reports issues, improves documentation, or provides feedback. The avatars are generated from GitHub contributor data; select them to open the [GitHub contributors page](https://github.com/SECTL/SecRandom/graphs/contributors) for complete statistics.

## Support and community

- [Support us on Afdian](https://afdian.com/a/lzy0983)
- [Email](mailto:lzy.12@foxmail.com)
- [QQ Group 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [QQ Channel](https://pd.qq.com/s/4x5dafd34?b=9)
- [Bilibili](https://space.bilibili.com/520571577)
- [Report an issue](https://github.com/SECTL/SecRandom/issues)
- [SecRandom documentation](https://secrandom.sectl.cn/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [English contributing guide](CONTRIBUTING_EN.md)

## Star History

<a href="https://www.star-history.com/?repos=SECTL%2FSecRandom&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&theme=dark&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
 </picture>
</a>

**Copyright © 2025-2026 SECTL**
