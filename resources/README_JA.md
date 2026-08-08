<div align="center">

<img src="secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**授業やチームで使える、設定可能な抽選フロー、履歴管理、検証可能な抽選記録を備えたランダム抽選ツール。**

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=Issues)](https://github.com/SECTL/SecRandom/issues)
[![Latest Release](https://img.shields.io/github/v/release/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=Latest%20Release)](https://github.com/SECTL/SecRandom/releases/latest)
[![Pre-release](https://img.shields.io/github/v/release/SECTL/SecRandom?include_prereleases&style=for-the-badge&label=Pre-release)](https://github.com/SECTL/SecRandom/releases)
[![Last Update](https://img.shields.io/github/last-commit/SECTL/SecRandom?style=for-the-badge&color=00b4ab&label=Last%20Update)](https://github.com/SECTL/SecRandom/commits/master)
[![Downloads](https://img.shields.io/github/downloads/SECTL/SecRandom/total?style=for-the-badge&color=00b4ab&label=Downloads)](https://github.com/SECTL/SecRandom/releases)

[![QQ Group](https://img.shields.io/badge/-QQ%20Group%20%7C%20833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![Bilibili](https://img.shields.io/badge/-Bilibili%20%7C%20%E9%BB%8E%E6%B3%BD%E6%87%BF-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](../LICENSE)

**言語** [ [简体中文](../README.md) | [English](README_EN.md) | **日本語** ]

</div>

> [!NOTE]
> SecRandom は GNU GPLv3 で公開されています。ソースコードの変更と再配布は可能ですが、派生物も GNU GPLv3 で公開する必要があります。

## SecRandom v3

SecRandom v3 は、授業、クラス、チームでのランダム抽選のためのデスクトップアプリケーションです。点呼、クイック抽選、賞品抽選を、共通のプロファイル、履歴、設定システムに統合し、確認できる抽選証明を提供します。

履歴に基づく重み付けと候補者フィルターは、同じ人の連続選出を減らし、長期的な分布を改善するためのものです。現実の名簿、ルール、運用手順を管理する代わりにはならず、それらをソフトウェアで検証できるとは主張しません。

## 機能

### 抽選ワークフロー

- **点呼**: 通常のランダム抽選、履歴バランス抽選、重複制御に対応します。
- **クイック抽選**: 独立したフローティングウィンドウから、生徒をすばやく抽選します。
- **抽選会**: 賞品ルーレットと在庫抽選に対応し、生徒と賞品を個別に管理します。
- **豊かな演出**: アニメーション、結果、音声、音楽、通知を統一設定で管理し、通知失敗時のフォールバックに対応します。

### 公平性とリスト管理

- 履歴回数、抽選間隔、グループ、性別などに基づいて重みを動的に調整し、重複と分布の偏りを抑えます。
- 安定した内部識別子で履歴を管理します。学籍番号、ID、名前は表示情報のみです。
- 複数の生徒リストと賞品プール、および `.xlsx`、`.xls`、`.csv` のインポート、マッピング、プレビューに対応します。
- すべての抽選ラウンドの履歴を保存し、確認しやすくします。

### 抽選結果の再確認

- 抽選ごとに証明記録ファイルを自動保存します。
- サーバーを抽選に参加させ、立ち会わせるかどうかを選択できます。
- 公式チャンネルを通じて抽選結果を再確認できます。

### データ、プライバシー、セキュリティ

- 設定、リスト、履歴はすべてインポート、エクスポート、バックアップ、復元に対応します。
- バックアップにはリスト、履歴、抽選証明、画像、音声を含められますが、パスワードなどのセキュリティ情報は含まれません。
- パスワード、TOTP、USB メモリによる保護で重要な操作を守り、検証が必要な操作を設定できます。

### 検証の境界

| モード | できること | 証明できないこと |
|---|---|---|
| オフライン証明 | 完了した抽選プロセスを再確認する | 抽選前のサーバー立会いではなく、ローカルプログラムや現実の名簿が変更されていないことを証明できない |
| オンライン立会い | サーバーがロックした後の抽選フローを保護する | 名簿が真正かつ完全で、送信前に絞り込まれていないことを証明できない |

## 技術の変遷

| バージョン | 技術スタック | 段階 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代デスクトップ実装 |
| v2 | Python + PySide6 + qfluentwidgets | Qt スタックの進化 |
| **v3** | **C# + Avalonia + FluentAvalonia** | 抽選、検証、デスクトップ連携を継続的に発展させる .NET デスクトップ再構築 |

## ダウンロードと更新

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) でリリースパッケージと変更履歴を提供しています。
- [公式ダウンロードページ](https://stk.sectl.cn/SecRandom) から最新版のダウンロード入口を利用できます。
- 自動更新では、配置前に署名付きリリースマニフェストと成果物の長さ・ハッシュを検証します。インストールの詳細は各リリースに含まれるパッケージと説明を参照してください。

## ライセンスと第三者通知

SecRandom は [GNU GPLv3](../LICENSE) で公開されています。第三者コンポーネント、著作権情報、配布審査に関する注記は [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) を参照してください。

## 貢献者と特別な謝辞

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

コードの提供、問題報告、ドキュメント改善、フィードバックを寄せてくださるすべての貢献者に感謝します。アバターは GitHub の貢献者データから動的に生成され、クリックすると完全な統計を [GitHub の貢献者ページ](https://github.com/SECTL/SecRandom/graphs/contributors) で確認できます。

## サポートとコミュニティ

- [Afdian で支援する](https://afdian.com/a/lzy0983)
- [メール](mailto:lzy.12@foxmail.com)
- [QQ グループ 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [QQ チャンネル](https://pd.qq.com/s/4x5dafd34?b=9)
- [Bilibili](https://space.bilibili.com/520571577)
- [問題を報告する](https://github.com/SECTL/SecRandom/issues)
- [SecRandom 公式ドキュメント](https://secrandom.sectl.cn/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [日本語の貢献ガイド](CONTRIBUTING_JA.md)

## Star History

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>

**Copyright © 2025-2026 SECTL**
