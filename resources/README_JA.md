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

- **点呼**: 通常のランダム抽選と履歴バランス抽選に対応し、重複、重複なし、部分的重複のルールを設定できます。
- **クイック抽選**: 独立したフローティングウィンドウから、メイン画面を妨げずに生徒を素早く抽選できます。
- **抽選会**: 賞品ルーレットと在庫数による抽選に対応し、生徒と賞品は別々の候補プールと履歴を保持します。
- **演出**: 抽選アニメーション、結果表示、音声、音楽、通知を共通設定で管理できます。外部通知サービスが失敗した場合は内蔵通知へ切り替えられます。

### 履歴バランスとリスト管理

- 履歴回数、前回選出からの時間、グループ、性別の文脈を考慮した設定可能な重み付けに加え、初期保護と差分保護を提供します。
- 安定した内部レコード ID で履歴を管理します。表示用の学籍番号、ID、名前は必須の一意識別子ではなくメタデータです。
- 複数の生徒リストと賞品プールを管理し、`.xlsx`、`.xls`、`.csv` のインポート、列マッピング、プレビューに対応します。
- 論理的な抽選ラウンドごとに履歴を保存し、プロファイルと抽選種別ごとに確認・整理できます。

### 公平性を考慮した抽選

公平性を考慮した抽選は、暗号学的乱数源を基礎とし、個人の抽選履歴、前回選出からの時間、グループ、性別分布から候補者の重みを計算します。初期保護、平均差分、候補プール保護により、繰り返し選出と長期的な偏りを抑えます。結果は名簿、ルール、設定に依存するため、現実世界の公平性を絶対に保証するものではありません。

### 検証可能な抽選

- 通常の抽選では、アルゴリズムと入力の要約、結果、必要な匿名監査データを含む、ローカルで再生可能な `.srproof.json` 証明を直ちに保存します。
- 新しく作成された証明は、再生検証と署名付き受領書のためにバックグラウンドで一度だけ送信できます。証明はエクスポートでき、保存期間と容量上限に従って管理されます。
- 正式なオンライン立会いは明示的なオプションです。サービスが匿名リクエストをロックした後、サーバー側の乱数材料を生成し、`OnlineWitnessed` 証明を返します。

### データ、プライバシー、セキュリティ

- プロファイル、設定、完全データは、エクスポート、インポート、手動バックアップ、自動バックアップに対応します。インポートは SecRandom v3 のマニフェストまたは封筒形式のみを受け付け、書き込み前に復元スナップショットを作成します。
- 完全データのエクスポートにはリスト、履歴、証明、音声、授業連携、プラグインデータを含められますが、セキュリティ資格情報は含まれません。
- テレメトリ送信とオンライン状態の報告は個別に制御できます。
- 任意のパスワード、TOTP、USB バインディングは別の資格情報ストアで管理され、抽選、リセット、設定、外部コマンドを保護できます。

### 連携、プラグイン、配布

- CSES の時間割と ClassIsland の任意の授業連携に対応します。設定した場合でも、抽選を制限するのは確認済みの休み時間状態のみで、データ未取得、無効、不明な状態では通常の操作を妨げません。
- ClassIsland 通知には `SecRandom4Ci` v2 プラグインが必要です。プラグインは制限された宣言型抽選インターフェースを使用し、乱数源、重み計算、書き込み可能な履歴、ホストサービスへ直接アクセスできません。
- 更新センターは署名付きリリースマニフェストと成果物の完全性を検証します。ポータブルパッケージは安定したランチャーとバージョン付き `app-*` ペイロードを使用し、更新後もデータを保持します。

## 検証モードと制限

**オフライン再現可能証明** は通常の抽選を保存し、再生するためのものです。バックグラウンドの署名付き受領書は、送信後の証明変更の検出に役立ちます。これは抽選前のサーバー立会いではなく、ローカル実行ファイル、現実の候補者プール、抽選前の操作が変更されていないことを証明するものでもありません。

**正式なオンライン立会い** は、明示的に選択する代替モードです。サービスは乱数材料を生成して結果を計算する前に匿名リクエストをロックします。ネットワーク、サービス、デバイスの障害時には明確な結果を待ち、ローカル抽選へ黙ってフォールバックしません。ロックされたフローをローカルコード、シード、証明の置換から守る能力を高めますが、現実の名簿が真正、完全であり、送信前に絞り込まれていないことまでは証明できません。

## 技術の変遷

| バージョン | 技術スタック | 段階 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代デスクトップ実装 |
| v2 | Python + PySide6 + qfluentwidgets | Qt スタックの進化 |
| **v3** | **C# + Avalonia + FluentAvalonia** | 抽選、検証、プラグイン、デスクトップ連携を継続的に発展させる .NET デスクトップ再構築 |

## ダウンロードと更新

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) でリリースパッケージと変更履歴を提供しています。
- [公式ダウンロードページ](https://secrandom.sectl.top/download.html) から安定版とプレリリース版を利用できます。
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
- [SecRandom 公式ドキュメント](https://secrandom.sectl.top/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [日本語の貢献ガイド](CONTRIBUTING_JA.md)

## Star History

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=SECTL/SecRandom&type=Date">
</picture>

**Copyright © 2025-2026 SECTL**
