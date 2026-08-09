<div align="center">

<img src="secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**A random-selection tool for classrooms and teams, with configurable workflows, managed history, and verifiable draw records.**

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

SecRandom is a fair random-selection application for classrooms, teams, events, decision-making, and other scenarios.

## Features

### Draw workflows

- **Roll call**: Supports standard random, history-balanced, and repeat-control draws.
- **Quick draw**: Quickly draws students through a standalone floating window.
- **Lottery**: Supports prize-wheel and inventory draws, with students and prizes managed independently.
- **Rich presentation**: Provides unified settings for animation, results, speech, music, and notifications, with fallback when a notification fails.

### Fairness and list management

- Dynamically adjusts weights using history count, draw interval, group, gender, and other factors to reduce repeats and distribution imbalance.
- Uses stable internal identifiers to preserve history; student numbers, IDs, and names are display information only.
- Supports multiple student lists, prize pools, and `.xlsx`, `.xls`, `.csv` import, mapping, and preview.
- Saves history for every draw round for convenient review.

### Reviewable draw results

- Every draw automatically saves a proof record file.
- You can choose to involve the server in and witness the draw process.
- Draw results can be checked again through official channels.

### Data, privacy, and security

- Settings, lists, and history can all be imported, exported, backed up, and restored.
- Backups may include lists, history, draw proofs, images, and audio, but never passwords or other security information.
- Password, TOTP, or USB-drive protection can secure important operations, and you can choose which operations require verification.

### Verification boundaries

| Mode | What it can do | What it cannot prove |
|---|---|---|
| Offline proof | Review a completed draw process | It is not a pre-draw server witness; it cannot prove that the local program or real-world roster was not modified |
| Online witnessing | Protect the draw flow after the server locks it | It cannot prove that the roster is authentic, complete, or unfiltered before submission |

## Technical evolution

| Version | Stack | Stage |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | First desktop implementation |
| v2 | Python + PySide6 + qfluentwidgets | Qt stack evolution |
| **v3** | **C# + Avalonia + FluentAvalonia** | .NET desktop rewrite for continued draw, verification, and desktop-integration development |

## Download and updates

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) provides release packages and change notes.
- The [official download page](https://stk.sectl.cn/SecRandom) provides the latest download entry point.
- Automatic updates validate a signed release manifest and artifact length/hash before deployment. Refer to the package and notes supplied with each release for installation details.

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
