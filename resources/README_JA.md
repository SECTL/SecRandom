<div align="center">

<img src="secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom

**点呼・抽選・授業連携を支える、授業向けオープンソース抽選ツール。**

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

## SecRandom

SecRandom は、授業のためのオープンソース抽選・インタラクションツールです。点呼、クイック抽選、賞品抽選、乱数抽選に対応し、ランダム・公平・編成による抽選、名簿管理、履歴、結果の検証、授業連携を組み合わせて、日々の授業から公開授業やクラス活動まで活用できます。

## ダウンロードと使い始め方

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) または [公式ダウンロードページ](https://stk.sectl.cn/SecRandom) から入手できます。インストール方法と対応アーキテクチャは各リリースの説明をご確認ください。
- デスクトップ版は Windows・macOS・Linux、モバイル版は Android・iOS 向けにビルドされています。画面や機能はプラットフォームによって異なります。提供状況は各リリースをご確認ください。
- アプリを開き、設定の名簿管理で学生名簿を追加またはインポートして、点呼画面で名簿を選んで抽選を始めます。賞品リストを作成した抽選や、指定範囲からの乱数抽選もできます。
- 詳しい使い方は [公式ドキュメント](https://secrandom.sectl.cn/doc/overview.html) をご覧ください。

## 主な機能

### 授業での抽選

- **点呼とクイック抽選**: 授業中に学生を選出できます。デスクトップ版ではフローティングウィンドウから素早く操作できます。
- **賞品抽選と乱数**: 賞品リストと在庫を管理して抽選するほか、指定した範囲から乱数を選べます。
- **抽選方式**: ランダム、公平、編成から選び、重複ルールを設定できます。公平抽選は履歴回数や抽選間隔などに基づいて重みを調整します。編成抽選は事前に設定したルールや順序に従い、公開授業やデモなどに利用できます。

### 名簿・履歴・演出

- 複数の学生名簿と賞品リストを管理できます。デスクトップ版では `.xlsx`、`.xls`、`.csv` の列対応付けとプレビューを含むインポートに対応します。
- 点呼と賞品抽選の履歴を確認、グループ化、エクスポートできます。安定した内部識別子でメンバーと記録を関連付けます。乱数抽選は別途集計され、名簿の履歴には記録されません。
- デスクトップ版ではアニメーション、音声、音楽、フローティング通知、全画面表示などを授業に合わせて設定できます。

### 授業連携と拡張

- デスクトップ版では時間割や ClassIsland と連携し、授業に応じて名簿や賞品リストを切り替えられます。フローティングウィンドウからよく使う操作にもアクセスできます。
- デスクトップ端末を集中管理に接続し、モバイルのアカウントコンソールからオンライン端末を選んで遠隔抽選を開始できます。
- デスクトップ版はプラグイン、連携 SDK、自動化インターフェースを備え、機能の拡張や他のアプリとの接続に対応します。

### データとセキュリティ

- 設定、名簿、履歴などをバックアップ・復元できます。バックアップにパスワードなどの認証情報は含まれません。
- 重要な操作をパスワード、TOTP、USB メモリで保護し、認証が必要な操作を選べます。

## 公平性と結果の検証

公平抽選は履歴と候補者のフィルターを使い、重複を減らして長期的な分布を改善するためのものですが、毎回均等な結果を保証するものではありません。編成抽選の結果は事前のルールに影響されるため、ランダム抽選とは区別する必要があります。

デスクトップ版では抽選証明を保存し、ローカルで検証・再生できます。オフライン証明はバックグラウンドでサーバー認証と第三者タイムスタンプを要求できますが、ネットワーク障害によって認証が遅延または失敗することがあります。サーバーが抽選のロックに参加するオンライン立会いも選択できます。利用可能な機能と検証方法はアプリ内の説明をご確認ください。

証明は記録された過程の確認に役立ちますが、抽選前の名簿が真正・完全で絞り込まれていないことや、事前にオフラインで繰り返し試行していないことは証明できません。名簿、ルール、授業の運用管理は引き続き必要です。

## 技術の変遷

| バージョン | 技術スタック | 段階 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代デスクトップ実装 |
| v2 | Python + PySide6 + qfluentwidgets | Qt スタックの進化 |
| **v3** | **C# + Avalonia + FluentAvalonia** | デスクトップとモバイル向けに、抽選・検証・授業連携を継続的に発展 |

## ライセンスと第三者通知

- SecRandom は [GNU GPLv3](../LICENSE) で公開されています。
- 第三者コンポーネント、著作権情報、配布審査に関する注記は [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) を参照してください。
- 履歴に基づく重み付けと候補者フィルターは、同じ人の連続選出を減らし、長期的な分布を改善するためのものです。現実の名簿、ルール、運用手順を管理する代わりにはならず、それらをソフトウェアで検証できるとは主張しません。

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

<a href="https://www.star-history.com/?repos=SECTL%2FSecRandom&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&theme=dark&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=SECTL/SecRandom&type=date&legend=top-left&sealed_token=ugfdzW7iXV4wxuvKJoxpW6akarha_ogPhHQL86oTVzn8VT5lUiEMRTg8xxLjViyNUEax2PY2wSEeiYHOeJAGJfNRfLdtLGGihK9G5H-0WWX1rWT1YPBBVg" />
 </picture>
</a>

**Copyright © 2025-2026 SECTL**
