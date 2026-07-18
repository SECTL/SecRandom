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

## SecRandom v3

SecRandom v3 is a desktop application for classroom, class, and team random selection. It unifies roll call, quick draw, and prize drawing under one profile, history, and settings system, with inspectable draw proofs.

History-balanced weights and candidate filters help reduce repeat selections and improve long-term distribution. They do not replace management of real-world rosters, rules, or processes, and SecRandom does not claim to verify those conditions.

## Features

### Draw workflows

- **Roll call**: Standard random and history-balanced draws with configurable repeat, no-repeat, and partial-repeat rules.
- **Quick draw**: Fast student draws from a standalone floating window without taking over the main workspace.
- **Lottery**: Prize-wheel and inventory-count drawing modes; student and prize pools maintain separate histories.
- **Presentation**: Shared draw animation, result display, speech, music, and notification settings. The built-in notification can fall back when an external notification service fails.

### History balance and lists

- Configurable weights consider history count, time since a previous result, group, and gender context, with cold-start and gap protections.
- Stable internal record identities preserve history; displayed student numbers, IDs, and names are metadata rather than required unique identities.
- Multiple student lists and prize pools with `.xlsx`, `.xls`, and `.csv` import, field mapping, and preview.
- Every logical draw round is retained for review and management by profile and draw type.

### Fair selection

Fair selection combines a cryptographic random source with candidate weights derived from personal draw history, time since the last result, group and gender distribution. Cold-start, average-gap, and candidate-pool protections help reduce repeated selections and long-term imbalance. Its results depend on the list, rules, and configuration; it does not make absolute claims about real-world fairness.

### Verifiable draws

- Ordinary draws immediately save a locally replayable `.srproof.json` proof containing algorithm/input summaries, results, and required anonymous audit data.
- A newly created proof may be submitted once in the background for replay verification and a signed receipt. Proofs can be exported and retained under age and storage limits.
- Formal online witnessing is an explicit option: the service locks an anonymous request, then creates server-side random material and returns an `OnlineWitnessed` proof.

### Data, privacy, and security

- Profiles, settings, and full data support export, import, manual backup, and automatic backup. Imports accept only SecRandom v3 manifest/envelope formats and create a recovery snapshot before writes.
- Full-data exports can include lists, history, proofs, audio, course-linkage, and plugin data, but never security credentials.
- Telemetry upload and online-status reporting are independent controls.
- Optional password, TOTP, and USB binding protections use a separate credential store and can protect draws, resets, settings, or external commands.

### Integration, plugins, and delivery

- Optional course linkage supports CSES schedules and ClassIsland. Only a confirmed break state restricts draws when configured; unavailable, invalid, or unknown data does not block normal use.
- ClassIsland notifications require the `SecRandom4Ci` v2 plugin. Plugins use a restricted declarative draw interface and cannot access random sources, weighting logic, writable history, or host services directly.
- The update center validates a signed release manifest and artifact integrity. Portable packages use a stable launcher and versioned `app-*` payloads so data can survive updates.

## Verification modes and limits

**Offline reproducible proofs** preserve and replay ordinary draws. A background signed receipt can help detect proof changes after submission. It is not a pre-draw server witness, and it cannot prove that the local executable, the real-world candidate pool, or actions before the draw were unmodified.

**Formal online witnessing** is an explicitly selected alternative. The service locks an anonymous request before generating random material and calculating a result; network, service, or device failures wait for a clear outcome and must not silently fall back to a local draw. It strengthens protection of the locked flow against local code, seed, and proof replacement, but it still cannot establish that a real-world roster is authentic, complete, or unfiltered before submission.

## Technical evolution

| Version | Stack | Stage |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | First desktop implementation |
| v2 | Python + PySide6 + qfluentwidgets | Qt stack evolution |
| **v3** | **C# + Avalonia + FluentAvalonia** | .NET desktop rewrite for continued draw, verification, plugin, and desktop-integration development |

## Download and updates

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) provides release packages and change notes.
- The [official download page](https://secrandom.sectl.top/download.html) provides stable and pre-release entry points.
- Automatic updates validate a signed release manifest and artifact length/hash before deployment. Refer to the package and notes supplied with each release for installation details.

## License and third-party notices

SecRandom is released under [GNU GPLv3](../LICENSE). See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for third-party components, copyright information, and distribution-review notes.

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
- [SecRandom documentation](https://secrandom.sectl.top/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [English contributing guide](CONTRIBUTING_EN.md)

## Star History

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>

**Copyright © 2025-2026 SECTL**
