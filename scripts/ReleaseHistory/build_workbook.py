#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把缓存数据生成为多工作表 XLSX：《SecRandom-版本历史.xlsx》。

输入（docs/release-history/）：
  data/repo.json, data/releases.json(经 parsed-changelog.json 汇总),
  data/stargazers.json, data/contributors.json, data/downloads-snapshots.jsonl,
  data/parsed-changelog.json, curated/annotations.json
输出：
  docs/release-history/SecRandom-版本历史.xlsx

用法：
  python scripts/ReleaseHistory/build_workbook.py
重复运行即完整重建（含公式、图表、筛选、冻结窗格）。
"""
from __future__ import annotations

import difflib
import json
import statistics
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

from openpyxl import Workbook
from openpyxl.chart import BarChart, LineChart, Reference
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

ROOT = Path(__file__).resolve().parents[2]
HIST_DIR = ROOT / "docs" / "release-history"
DATA_DIR = HIST_DIR / "data"
OUT_XLSX = HIST_DIR / "SecRandom-版本历史.xlsx"
CST = timezone(timedelta(hours=8))

HDR_FILL = PatternFill("solid", fgColor="1F3864")
HDR_FONT = Font(color="FFFFFF", bold=True, size=10)
GROUP_FILL = PatternFill("solid", fgColor="D9E2F3")
BAND_FILL = PatternFill("solid", fgColor="F2F5FA")
TITLE_FONT = Font(bold=True, size=14, color="1F3864")
SECTION_FONT = Font(bold=True, size=11, color="1F3864")
LINK_FONT = Font(color="0563C1", underline="single", size=10)
NORMAL_FONT = Font(size=10)
BOLD_FONT = Font(size=10, bold=True)
THIN = Side(style="thin", color="BFBFBF")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
WRAP = Alignment(vertical="top", wrap_text=True)
TOP = Alignment(vertical="top")
CENTER = Alignment(horizontal="center", vertical="center")
DATE_FMT = "yyyy-mm-dd"
DATETIME_FMT = "yyyy-mm-dd hh:mm"

SECTION_ORDER = ["新增", "优化", "修复", "移除", "其它"]
MAJORS = ["v1", "v2", "v3"]


def log(msg: str) -> None:
    print(f"[build] {msg}", flush=True)


def load_json(path: Path) -> object:
    return json.loads(path.read_text(encoding="utf-8"))


def to_cst(value: str | None) -> datetime | None:
    if not value:
        return None
    dt = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(CST).replace(tzinfo=None)


def major_of(tag: str) -> str:
    return tag.split(".")[0]


def platform_of(asset_name: str) -> str:
    """按资产名判定平台。

    历史命名有两代：
      v1/v2: SecRandom-Windows-Setup-v2.3.15-x64.exe / SecRandom-linux-Setup-...deb
      v3:    SecRandom-v3.0.0-beta.1-win-x64-setup.exe / ...-osx-arm64.app.zip
    """
    low = (asset_name or "").lower()
    if any(k in low for k in ("windows", "win-", "win_", "win64", "win32", "win-x", "win-x64", "win-x86", "win-arm")):
        return "Windows"
    if any(k in low for k in ("macos", "osx", "mac-", ".dmg", ".app.zip")):
        return "macOS"
    if any(k in low for k in ("linux", "debian", ".deb", ".rpm", "appimage")):
        return "Linux"
    if any(k in low for k in ("android", ".apk", ".aab")):
        return "Android"
    if "ios" in low or low.endswith(".ipa"):
        return "iOS"
    if any(k in low for k in (".exe", ".msi", ".msix")):
        # 旧版有 SecRandom-setup-v2.2.0-x64.exe 这类不含平台词的 Windows 安装包
        return "Windows"
    if any(k in low for k in ("sha256", "sha512", "sums", "manifest", ".sig", ".json", ".txt", ".yml", ".sha")):
        return "校验/清单"
    return "其它"


def arch_of(asset_name: str) -> str:
    low = (asset_name or "").lower()
    if any(k in low for k in ("arm64", "aarch64", "arm-64")):
        return "arm64"
    if any(k in low for k in ("x64", "amd64", "x86_64")):
        return "x64"
    if any(k in low for k in ("x86", "win32", "i386", "i686")):
        return "x86"
    if "universal" in low:
        return "universal"
    return "未标注"


def kind_of(asset_name: str) -> str:
    low = (asset_name or "").lower()
    if any(k in low for k in (".sig", ".json", "sums", "sha256", "manifest")):
        return "清单/签名"
    if low.endswith(".appimage"):
        return "AppImage"
    if low.endswith(".deb"):
        return "deb 安装包"
    if low.endswith(".rpm"):
        return "rpm 安装包"
    if low.endswith(".apk"):
        return "APK"
    if low.endswith(".ipa"):
        return "IPA"
    if ".app.zip" in low:
        return "macOS 应用包"
    if low.endswith(".exe"):
        return "Windows 安装程序"
    if "portable" in low or low.endswith("-dir.zip") or "-portable-" in low:
        return "便携包"
    if low.endswith(".zip"):
        return "压缩包"
    return "其它"


def prepare() -> dict:
    repo = load_json(DATA_DIR / "repo.json")
    parsed = load_json(DATA_DIR / "parsed-changelog.json")
    stars = load_json(DATA_DIR / "stargazers.json")["items"]
    contributors = load_json(DATA_DIR / "contributors.json")["items"]
    curated = load_json(HIST_DIR / "curated" / "annotations.json")
    snapshots = []
    snap_path = DATA_DIR / "downloads-snapshots.jsonl"
    if snap_path.exists():
        for line in snap_path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                snapshots.append(json.loads(line))
    versions = parsed["versions"]
    for i, v in enumerate(versions):
        # 大版本按 CHANGELOG 目录归属（v1.3.2-alpha.* 实为 v2.0 的 Alpha），标签前缀仅作对照
        v["major"] = v.get("family") or major_of(v["tag"])
        v["tag_major"] = v.get("tag_major") or major_of(v["tag"])
        v["pub_dt"] = to_cst(v["published_at"])
        v["tag_dt"] = to_cst(v["tag_date"])
        v["channel"] = "预览版" if v["prerelease"] else "正式版"
    entries = parsed["entries"]
    order = {v["tag"]: v["index"] for v in versions}
    entries.sort(key=lambda e: (order.get(e["version"], 999), e.get("order", 0)))
    identity = build_identities(versions, curated)
    family_of = {v["tag"]: v["major"] for v in versions}
    return {
        "repo": repo,
        "versions": versions,
        "entries": entries,
        "stars": stars,
        "contributors": contributors,
        "curated": curated,
        "snapshots": snapshots,
        "parsed": parsed,
        "identity": identity,
        "family_of": family_of,
    }


def build_identities(versions: list[dict], curated: dict) -> dict:
    """把 git 提交邮箱合并成统一贡献者身份，并统计总量/首末版本。"""
    aliases = {k: v for k, v in curated.get("author_aliases", {}).items() if not k.startswith("_")}
    ident: dict[str, dict] = {}
    for v in versions:
        for item in v.get("committer_identities") or []:
            email = item["email"]
            key = email
            alias = aliases.get(email)
            display = (alias or {}).get("display") or item["name"]
            github = (alias or {}).get("github") or ""
            if not github:
                m = __import__("re").match(r"\d+\+(.+?)@users\.noreply\.github\.com", email)
                if m:
                    github = m.group(1)
            kind = (alias or {}).get("type") or (
                "机器人" if "[bot]" in item["name"] or "bot" in email.lower() else "贡献者"
            )
            d = ident.setdefault(key, {
                "display": display, "github": github, "email": email, "type": kind,
                "commits": 0, "first_version": v["tag"], "first_index": v["index"],
                "last_version": v["tag"], "last_index": v["index"], "by_version": {},
            })
            d["commits"] += item["commits"]
            d["by_version"][v["tag"]] = item["commits"]
            if v["index"] < d["first_index"]:
                d["first_index"], d["first_version"] = v["index"], v["tag"]
            if v["index"] > d["last_index"]:
                d["last_index"], d["last_version"] = v["index"], v["tag"]
    ident = dict(sorted(ident.items(), key=lambda kv: -kv[1]["commits"]))
    # 版本总览里显示的“主要贡献者”用合并后的身份
    for v in versions:
        merged: dict[str, int] = {}
        for item in v.get("committer_identities") or []:
            alias = aliases.get(item["email"]) or {}
            label = alias.get("display") or item["name"]
            merged[label] = merged.get(label, 0) + item["commits"]
        v["merged_committers"] = dict(sorted(merged.items(), key=lambda kv: -kv[1]))
    return ident


def style_header(ws, ncols: int) -> None:
    for c in range(1, ncols + 1):
        cell = ws.cell(row=1, column=c)
        cell.fill = HDR_FILL
        cell.font = HDR_FONT
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        cell.border = BORDER
    ws.row_dimensions[1].height = 30


def finish_sheet(
    ws,
    widths: list[int],
    wrap_cols: tuple[int, ...] = (),
    date_cols: tuple[int, ...] = (),
    datetime_cols: tuple[int, ...] = (),
    link_cols: tuple[int, ...] = (),
    band: bool = True,
    freeze: str = "A2",
    autofilter: bool = True,
) -> None:
    for idx, width in enumerate(widths, start=1):
        ws.column_dimensions[get_column_letter(idx)].width = width
    nrows = ws.max_row
    ncols = ws.max_column
    for r in range(2, nrows + 1):
        for c in range(1, ncols + 1):
            cell = ws.cell(row=r, column=c)
            cell.font = NORMAL_FONT
            if cell.alignment is None or not cell.alignment.wrap_text:
                cell.alignment = TOP
            if band and r % 2 == 0 and cell.fill.fgColor.rgb in (None, "00000000"):
                cell.fill = BAND_FILL
    for c in wrap_cols:
        for r in range(2, nrows + 1):
            ws.cell(row=r, column=c).alignment = WRAP
    for c in date_cols:
        for r in range(2, nrows + 1):
            ws.cell(row=r, column=c).number_format = DATE_FMT
    for c in datetime_cols:
        for r in range(2, nrows + 1):
            ws.cell(row=r, column=c).number_format = DATETIME_FMT
    for c in link_cols:
        for r in range(2, nrows + 1):
            ws.cell(row=r, column=c).font = LINK_FONT
    ws.freeze_panes = freeze
    if autofilter and nrows >= 2:
        ws.auto_filter.ref = f"A1:{get_column_letter(ncols)}{nrows}"


def write_rows(ws, rows: list[list]) -> None:
    for row in rows:
        ws.append(row)


# --------------------------------------------------------------------------
# 说明与更新指南
# --------------------------------------------------------------------------
def build_guide(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("说明与更新指南")
    versions = ctx["versions"]
    entries = ctx["entries"]
    repo = ctx["repo"]
    curated = ctx["curated"]
    snapshots = ctx["snapshots"]
    stars = ctx["stars"]

    first = versions[0]
    last = versions[-1]
    gaps = [v["gap_days"] for v in versions if v["gap_days"] is not None]
    total_downloads = sum(v["downloads"] for v in versions)
    stable = [v for v in versions if not v["prerelease"]]
    prerelease = [v for v in versions if v["prerelease"]]
    counts = {s: sum(1 for e in entries if e["section"] == s) for s in SECTION_ORDER}
    avg_gap = statistics.mean(gaps) if gaps else 0
    med_gap = statistics.median(gaps) if gaps else 0
    newest_snapshot = snapshots[-1] if snapshots else None

    ws["A1"] = "SecRandom 版本历史数据集（多工作表）"
    ws["A1"].font = TITLE_FONT
    ws["A2"] = f"生成时间：{datetime.now(CST).strftime('%Y-%m-%d %H:%M')}（北京时间）"
    ws["A2"].font = NORMAL_FONT

    rows: list[tuple[str, object]] = [
        ("数据仓库", f"{repo.get('repo')}  ·  {repo.get('html_url')}"),
        ("开源协议", str(repo.get("license"))),
        ("仓库创建时间", to_cst(repo.get("created_at")).strftime("%Y-%m-%d %H:%M") if repo.get("created_at") else ""),
        ("最新数据抓取", repo.get("fetched_at")),
        ("首发版本", f"{first['tag']}（{first['pub_dt'].strftime('%Y-%m-%d')}）"),
        ("最新版本", f"{last['tag']}（{last['pub_dt'].strftime('%Y-%m-%d')}）"),
        ("版本总数", f"{len(versions)}（正式版 {len(stable)} / 预览版 {len(prerelease)}）"),
        ("大版本分布", "、".join(f"{m}: {sum(1 for v in versions if v['major'] == m)}" for m in MAJORS)),        ("发布节奏", f"平均间隔 {avg_gap:.1f} 天，中位 {med_gap:.0f} 天，最短 {min(gaps)} 天，最长 {max(gaps)} 天"),
        ("更新条目总数", "  ".join([f"{s} {counts[s]}" for s in SECTION_ORDER]) + f"  | 合计 {sum(counts.values())}"),
        ("资产下载量合计", f"{total_downloads:,} 次（截至抓取时刻的累计值）"),
        ("Star / Fork / 贡献者", f"{repo.get('stars')} ★ / {repo.get('forks')} fork / {len(ctx['contributors'])} 位贡献者"),
        ("Star 明细覆盖", f"{len(stars)} 条 starred_at 事件（{to_cst(stars[0]['starred_at']).strftime('%Y-%m-%d')} ~ {to_cst(stars[-1]['starred_at']).strftime('%Y-%m-%d')}）"),
        ("下载量快照条数", f"{len(snapshots)} 次（首次 {snapshots[0]['snapshot_at'] if snapshots else '—'}）"),
    ]

    r = 4
    ws.cell(row=r, column=1, value="关键指标").font = SECTION_FONT
    r += 1
    for k, v in rows:
        ws.cell(row=r, column=1, value=k).font = BOLD_FONT
        ws.cell(row=r, column=1).fill = GROUP_FILL
        ws.cell(row=r, column=2, value=v).font = NORMAL_FONT
        ws.cell(row=r, column=2).alignment = TOP
        r += 1

    r += 1
    ws.cell(row=r, column=1, value="工作表导航").font = SECTION_FONT
    r += 1
    sheets = [
        ("版本总览", "每个版本一行：发布时间、距上一版天数、变更类型计数、下载量、发布时累计 Star、重点新增与移除摘要"),
        ("更新要点明细", "每条更新日志明细：版本、变更类型、功能域、正文、关联 Issue/PR、是否首次出现"),
        ("版本间差异", "相邻版本对比：本版相对上一版的新增/优化/修复/移除条目与重点变化"),
        ("功能牺牲与降级", "历代被移除、降级、收窄的功能（自动提取 + 人工确认），含替代方案与是否回归"),
        ("功能模块演进", "各功能域在大版本间的起止与增删计数，用于判断哪些方向被放弃"),
        ("贡献者总表", "GitHub 贡献者与其提交数、占比"),
        ("贡献者-版本分布", "每个版本的区间提交数与各贡献者提交数（本地 git，已按邮箱合并同一人）"),
        ("贡献者-git身份对照", "git 提交身份合并结果：显示名、GitHub 登录名、邮箱、首末提交版本"),
        ("Star增长明细", "逐条 starred_at 事件与当时累计 Star，可精确还原增长曲线"),
        ("Star增长月度", "按月汇总的新增/累计 Star 与当月发布版本"),
        ("下载量-版本汇总", "每个版本的资产下载量与累计、日均、占比"),
        ("下载量-资产明细", "逐资产（平台 × 架构 × 包类型）的下载量明细"),
        ("下载量-平台维度", "每个版本按平台拆分的下载量（含合计行）"),
        ("下载量-平台架构汇总", "平台 / 架构 / 包类型三个维度的总量、占比与首末出现版本"),
        ("下载量-快照时间线", "每次抓取记录的下载总量，用于长期绘出真实下载演进曲线"),
        ("大版本主线", "v1/v2/v3 的技术栈、时间跨度、主题与代价（人工维护）"),
    ]
    for idx, (name, desc) in enumerate(sheets, start=1):
        ws.cell(row=r, column=1, value=idx).font = NORMAL_FONT
        ws.cell(row=r, column=2, value=name).font = BOLD_FONT
        ws.cell(row=r, column=3, value=desc).font = NORMAL_FONT
        ws.cell(row=r, column=3).alignment = TOP
        r += 1

    r += 1
    ws.cell(row=r, column=1, value="口径与数据来源").font = SECTION_FONT
    r += 1
    for text in [
        "版本与时间：GitHub Releases 的 published_at（北京时间 UTC+8），另列 git 标签提交时间用于核对",
        "大版本归属：以仓库自身的 CHANGELOG/<版本树>/ 目录为准（v1.3.2-alpha.1~6 在 CHANGELOG/v2/ 下且自述为 v2.0 Alpha，计入 v2），与标签前缀不一致时在「标签前缀」列标出",
        "更新内容：仓库 CHANGELOG/<大版本>/<版本>/CHANGELOG.md 逐条解析；Release 正文与之一致时不重复计入",
        "Star 增长：GitHub stargazers API（star+json，含 starred_at）逐条事件，可精确到秒",
        "下载量：GitHub Release 资产 download_count 累计值；单个资产的按日历史 GitHub 不提供，因此用多次快照累积曲线",
        "贡献者：GitHub contributors API（提交数）+ 本地 git log 的区间作者统计（两者口径不同，见各表说明）",
        "功能域：按关键词自动归类，存在误判，可参考「归类依据」列判断置信度",
    ]:
        ws.cell(row=r, column=1, value=f"• {text}").font = NORMAL_FONT
        r += 1

    r += 1
    ws.cell(row=r, column=1, value="如何更新这份表格").font = SECTION_FONT
    r += 1
    for text in [
        "1) 抓取最新数据：python scripts/ReleaseHistory/fetch_github_data.py（Star 时间线需要本机 gh CLI 已登录）",
        "2) 解析更新日志：python scripts/ReleaseHistory/parse_changelog.py（直接读取仓库 CHANGELOG 目录）",
        "3) 生成表格：python scripts/ReleaseHistory/build_workbook.py（重建本文件，公式与图表一起重算）",
        "4) 一次性执行：powershell -File scripts/ReleaseHistory/update-release-history.ps1（PowerShell 7 可用 pwsh -File）",
        "5) 校验产物：python scripts/ReleaseHistory/verify_workbook.py（压缩包/XML/公式引用/关键数字自洽）",
        "6) 人工结论（取舍、回归判断、大版本主线）维护在 docs/release-history/curated/annotations.json，脚本会自动合并进来",
        "7) 每次运行 fetch 都会向 data/downloads-snapshots.jsonl 追加一条快照，快照越多，下载量演进曲线越完整",
    ]:
        ws.cell(row=r, column=1, value=text).font = NORMAL_FONT
        r += 1

    r += 1
    ws.cell(row=r, column=1, value="注意事项 / 数据质量").font = SECTION_FONT
    r += 1
    notes = list(curated.get("notes", []))
    for item in curated.get("data_quality", []):
        notes.append(f"{item.get('version')}：{item.get('issue')}；处理：{item.get('handling')}")
    if newest_snapshot:
        notes.append(
            f"下载量为 {newest_snapshot['snapshot_at']} 的累计快照；GitHub 不提供历史曲线，越早开始跑本脚本，演进数据越完整"
        )
    notes.append("「是否回归」等人工判断以 CHANGELOG 文本为唯一证据，未出现重新引入描述的一律记「否」")
    for text in notes:
        ws.cell(row=r, column=1, value=f"• {text}").font = NORMAL_FONT
        ws.cell(row=r, column=1).alignment = WRAP
        r += 1

    ws.column_dimensions["A"].width = 34
    ws.column_dimensions["B"].width = 62
    ws.column_dimensions["C"].width = 62
    for row in ws.iter_rows(min_row=1, max_row=ws.max_row, max_col=3):
        for cell in row:
            if cell.font is None or cell.font.size is None:
                cell.font = NORMAL_FONT
    ws.freeze_panes = "A4"


# --------------------------------------------------------------------------
# 版本总览
# --------------------------------------------------------------------------
def build_overview(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("版本总览")
    headers = [
        "序号", "版本号", "版本名称", "渠道", "大版本", "标签前缀", "发布时间(北京时间)",
        "距上一版本(天)", "距首版(天)", "git标签时间", "发布者",
        "新增", "优化", "修复", "移除", "其它", "本版条目合计", "累积复述条目",
        "关联Issue数", "关联Issue", "关联PR",
        "区间提交数", "贡献者数", "主要贡献者",
        "资产数", "本版本下载量", "累计下载量", "发布时累计Star",
        "本版重点新增", "本版移除/降级", "Release 链接", "版本比较链接",
    ]
    ws.append(headers)
    col = {name: i + 1 for i, name in enumerate(headers)}
    entries = ctx["entries"]
    by_version: dict[str, list[dict]] = {}
    for e in entries:
        by_version.setdefault(e["version"], []).append(e)

    first_pub = ctx["versions"][0]["pub_dt"]
    rows_added = 0
    for i, v in enumerate(ctx["versions"]):
        own = by_version.get(v["tag"], [])
        fresh = [e for e in own if not e["is_repeat"]]
        added = [e for e in fresh if e["section"] == "新增"]
        removed = [e for e in fresh if e["section"] == "移除"]
        committers = sorted(v.get("merged_committers", {}).items(), key=lambda kv: -kv[1])
        committers_txt = "、".join(f"{n}({c})" for n, c in committers[:5]) or "—"
        summary = "；".join(e["text"][:80] for e in added[:3]) or "—"
        removal_txt = "；".join(e["text"][:80] for e in removed[:3]) or "—"
        tag_prefix = v["tag_major"] if v["tag_major"] != v["major"] else "—"
        r = i + 2
        row = [
            v["index"], v["tag"], v["name"], v["channel"], v["major"], tag_prefix,
            v["pub_dt"], None, None, v["tag_dt"], v["author"],
            v["entry_counts"]["新增"], v["entry_counts"]["优化"], v["entry_counts"]["修复"],
            v["entry_counts"]["移除"], v["entry_counts"]["其它"], v["entry_total"], v["repeat_total"],
            len(v["issues"]), "、".join(f"#{n}" for n in v["issues"]) or "—",
            "、".join(f"#{n}" for n in v["pulls"]) or "—",
            v["commits"], len(committers), committers_txt,
            v["asset_count"], v["downloads"], None, None,
            summary, removal_txt,
            v["html_url"],
            f"https://github.com/{ctx['repo'].get('repo')}/compare/{ctx['versions'][i-1]['tag']}...{v['tag']}" if i else "",
        ]
        ws.append(row)
        pub_col = get_column_letter(col["发布时间(北京时间)"])
        dl_col = get_column_letter(col["本版本下载量"])
        if i > 0:
            ws.cell(row=r, column=col["距上一版本(天)"], value=f"=ROUND({pub_col}{r}-{pub_col}{r-1},1)")
        ws.cell(row=r, column=col["距首版(天)"], value=f"=ROUND({pub_col}{r}-${pub_col}$2,1)")
        ws.cell(row=r, column=col["累计下载量"], value=f"=SUM(${dl_col}$2:{dl_col}{r})")
        ws.cell(row=r, column=col["发布时累计Star"],
                value=f"=COUNTIFS('Star增长明细'!$B$2:$B$100000,\"<=\"&{pub_col}{r})")
        rows_added += 1

    finish_sheet(
        ws,
        widths=[5, 16, 30, 8, 7, 8, 17, 12, 10, 17, 12, 6, 6, 6, 6, 6, 10, 11,
                 10, 16, 14, 10, 9, 34, 7, 12, 12, 12, 52, 34, 34, 34],
        wrap_cols=(3, 24, 29, 30, 31, 32),
        datetime_cols=(7, 10),
        link_cols=(31, 32),
    )
    log(f"版本总览：{rows_added} 行（大版本按 CHANGELOG 目录归属）")


# --------------------------------------------------------------------------
# 更新要点明细
# --------------------------------------------------------------------------
def build_entries(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("更新要点明细")
    headers = [
        "版本号", "发布序号", "发布时间", "大版本", "变更类型", "功能域", "归类依据",
        "【模块】标签", "条目正文", "关联Issue", "关联PR", "是否首次出现", "首次出现版本", "源文件",
    ]
    ws.append(headers)
    vmap = {v["tag"]: v for v in ctx["versions"]}
    rows = []
    for e in ctx["entries"]:
        v = vmap.get(e["version"], {})
        rows.append([
            e["version"], v.get("index"), v.get("pub_dt"),
            v.get("major") or major_of(e["version"]),
            e["section"], e["domain"], e["domain_source"], e.get("module_tag") or "",
            e["text"],
            "、".join(f"#{n}" for n in e["issues"]),
            "、".join(f"#{n}" for n in e["pulls"]),
            "否" if e.get("is_repeat") else "是",
            e.get("first_seen_version") or e["version"],
            e["file_version"],
        ])
    write_rows(ws, rows)
    finish_sheet(
        ws,
        widths=[16, 9, 17, 7, 10, 16, 11, 18, 90, 11, 10, 12, 16, 14],
        wrap_cols=(9, 8),
        datetime_cols=(3,),
    )
    log(f"更新要点明细：{len(rows)} 行")


# --------------------------------------------------------------------------
# 版本间差异
# --------------------------------------------------------------------------
def build_delta(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("版本间差异")
    headers = [
        "版本号", "上一版本", "距上版(天)", "上一版发布时间", "本版发布时间",
        "新增", "优化", "修复", "移除", "其它", "合计",
        "本版涉及功能域", "本版移除条目", "本版重点新增", "净变化(新增-移除)",
    ]
    ws.append(headers)
    entries = ctx["entries"]
    by_version: dict[str, list[dict]] = {}
    for e in entries:
        if not e["is_repeat"]:
            by_version.setdefault(e["version"], []).append(e)
    versions = ctx["versions"]
    for i, v in enumerate(versions):
        own = by_version.get(v["tag"], [])
        added = [e for e in own if e["section"] == "新增"]
        removed = [e for e in own if e["section"] == "移除"]
        c = v["entry_counts"]
        domains = sorted({e["domain"] for e in own})
        ws.append([
            v["tag"],
            versions[i - 1]["tag"] if i else "—（首个版本）",
            v["gap_days"],
            versions[i - 1]["pub_dt"] if i else None,
            v["pub_dt"],
            c["新增"], c["优化"], c["修复"], c["移除"], c["其它"], v["entry_total"],
            "、".join(domains) or "—",
            "；".join(e["text"] for e in removed) or "—",
            "；".join(e["text"][:90] for e in added[:3]) or "—",
            c["新增"] - c["移除"],
        ])
    finish_sheet(
        ws,
        widths=[16, 18, 11, 17, 17, 6, 6, 6, 6, 6, 7, 42, 48, 60, 14],
        wrap_cols=(12, 13, 14),
        datetime_cols=(4, 5),
    )
    log(f"版本间差异：{len(versions)} 行")


# --------------------------------------------------------------------------
# 功能牺牲与降级
# --------------------------------------------------------------------------
def build_sacrifices(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("功能牺牲与降级")
    headers = [
        "来源", "版本号", "发布时间", "大版本", "被牺牲的功能", "类型", "变更说明",
        "替代方案/后续", "是否回归", "回归版本", "依据",
    ]
    ws.append(headers)
    versions = ctx["versions"]
    vmap = {v["tag"]: v for v in versions}

    auto_rows = []
    seen = set()
    for e in ctx["entries"]:
        if e["section"] != "移除":
            continue
        if e.get("is_repeat"):
            continue  # 累积更新日志里对更早版本移除动作的复述，已在首次出现的版本记录
        key = (e["version"], e["text"])
        if key in seen:
            continue
        seen.add(key)
        feature = e.get("module_tag") or e["text"][:24]
        auto_rows.append({
            "source": "自动提取",
            "version": e["version"],
            "feature": feature,
            "type": "功能移除" if any(k in e["text"] for k in ("移除", "去除", "删除")) else "功能调整",
            "detail": e["text"],
            "replacement": "",
            "returned": "",
            "return_version": "",
            "evidence": f"CHANGELOG/{e['file_version']}/CHANGELOG.md",
        })

    curated_rows = []
    for s in ctx["curated"].get("sacrifices", []):
        curated_rows.append({
            "source": "人工确认",
            "version": s.get("version", ""),
            "feature": s.get("feature", ""),
            "type": s.get("type", ""),
            "detail": s.get("detail", ""),
            "replacement": s.get("replacement", ""),
            "returned": s.get("returned", ""),
            "return_version": s.get("return_version", ""),
            "evidence": s.get("evidence", ""),
        })

    def norm(text: str) -> str:
        return "".join(ch for ch in text if ch.isalnum())

    ACTION_WORDS = ("去除", "删除", "移除", "取消", "停用", "废弃", "下线", "了", "功能", "设置", "选项")

    def norm2(text: str) -> str:
        out = norm(text)
        for w in ACTION_WORDS:
            out = out.replace(w, "")
        return out

    curated_by_version: dict[str, list[dict]] = {}
    for r in curated_rows:
        curated_by_version.setdefault(r["version"], []).append(r)

    def covered_by_curated(auto: dict) -> bool:
        auto_detail = norm2(auto["detail"])
        auto_feature = norm2(auto["feature"])
        for cur in curated_by_version.get(auto["version"], []):
            cur_text = norm2(cur["detail"] + cur["feature"])
            if auto_detail and (auto_detail in cur_text or cur_text in auto_detail):
                return True
            if auto_feature and len(auto_feature) >= 4 and auto_feature in cur_text:
                return True
            if auto_detail and difflib.SequenceMatcher(None, auto_detail, cur_text).ratio() >= 0.8:
                return True
        return False

    merged = curated_rows + [r for r in auto_rows if not covered_by_curated(r)]

    # 跨版本去重：同一次“牺牲”可能在多个版本的日志里被复述（如 v2.0.0 复述 v1.3.2 的移除），
    # 人工确认条目优先，其余按时间顺序做一次相似度去重。
    merged.sort(key=lambda r: (0 if r["source"] == "人工确认" else 1,
                               vmap.get(r["version"], {}).get("index", 999)))
    kept: list[dict] = []
    kept_keys: list[tuple[str, str]] = []  # (功能键, 正文键)

    def feat_sim(a: str, b: str) -> bool:
        """功能名相似。2~4 字的功能名太泛（如「即抽」⊂「即抽界面管理」），只在长度足够时才比较。"""
        if not a or not b:
            return False
        if a == b:
            return True
        if min(len(a), len(b)) < 5:
            return False
        if a in b or b in a:
            return True
        return difflib.SequenceMatcher(None, a, b).ratio() >= 0.82

    def detail_sim(a: str, b: str) -> bool:
        if not a or not b:
            return False
        if a in b or b in a:
            return True
        return difflib.SequenceMatcher(None, a, b).ratio() >= 0.8

    for r in merged:
        feat_key = norm2(r["feature"])
        detail_key = norm2(r["detail"])
        dup = any(
            feat_sim(feat_key, other_feat) or detail_sim(detail_key, other_detail)
            for other_feat, other_detail in kept_keys
        )
        if dup:
            continue
        kept.append(r)
        kept_keys.append((feat_key, detail_key))
    merged = sorted(kept, key=lambda r: vmap.get(r["version"], {}).get("index", 999))

    for r in merged:
        v = vmap.get(r["version"], {})
        ws.append([
            r["source"], r["version"], v.get("pub_dt"),
            v.get("major") or (major_of(r["version"]) if r["version"] else ""),
            r["feature"], r["type"], r["detail"], r["replacement"] or "—",
            r["returned"] or "—", r["return_version"] or "—", r["evidence"],
        ])
    finish_sheet(
        ws,
        widths=[10, 16, 17, 7, 34, 16, 70, 40, 10, 26, 46],
        wrap_cols=(5, 7, 8, 10, 11),
        datetime_cols=(3,),
    )
    log(f"功能牺牲与降级：{len(merged)} 行（人工 {len(curated_rows)} + 自动 {len(auto_rows)}，去重后合并）")


# --------------------------------------------------------------------------
# 功能模块演进
# --------------------------------------------------------------------------
def build_domains(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("功能模块演进")
    headers = ["功能域", "首次出现版本", "首次出现时间", "最后变更版本", "最后变更时间"]
    for m in MAJORS:
        headers += [f"{m}新增", f"{m}优化", f"{m}修复", f"{m}移除", f"{m}其它"]
    headers += ["全期条目数", "生命周期状态", "最近变更摘要"]
    ws.append(headers)

    stats: dict[str, dict] = {}
    vmap = {v["tag"]: v for v in ctx["versions"]}
    family_of = ctx["family_of"]
    for e in ctx["entries"]:
        if e["is_repeat"]:
            continue
        d = stats.setdefault(e["domain"], {
            "first": e["version"], "last": e["version"], "counts": {}, "last_text": "",
            "removed_any": False, "last_index": -1, "first_index": 999,
        })
        idx = vmap.get(e["version"], {}).get("index", 999)
        if idx < d["first_index"]:
            d["first_index"] = idx
            d["first"] = e["version"]
        if idx >= d["last_index"]:
            d["last_index"] = idx
            d["last"] = e["version"]
            d["last_text"] = f"{e['section']}：{e['text'][:70]}"
        key = (family_of.get(e["version"], major_of(e["version"])), e["section"])
        d["counts"][key] = d["counts"].get(key, 0) + 1
        if e["section"] == "移除":
            d["removed_any"] = True

    def status_of(d: dict) -> str:
        v3 = sum(v for (m, s), v in d["counts"].items() if m == "v3")
        v2 = sum(v for (m, s), v in d["counts"].items() if m == "v2")
        v1 = sum(v for (m, s), v in d["counts"].items() if m == "v1")
        last_major = family_of.get(d["last"], major_of(d["last"]))
        if d["removed_any"] and last_major == "v3":
            return "已缩减/移除（v3 内）"
        if d["removed_any"]:
            return "曾发生移除/降级"
        if v1 and not v2 and not v3:
            return "v1 之后停更（疑似放弃）"
        if v2 and not v3:
            return "v2 之后未在 v3 出现（重构中/待补）"
        if v3 and v1 + v2 == 0:
            return "v3 新增方向"
        if v3:
            return "v3 仍在演进"
        if v2:
            return "v2 仍在演进"
        return "维护中"

    rows = sorted(stats.items(), key=lambda kv: -sum(kv[1]["counts"].values()))
    for domain, d in rows:
        row = [domain, d["first"], vmap.get(d["first"], {}).get("pub_dt"),
               d["last"], vmap.get(d["last"], {}).get("pub_dt")]
        for m in MAJORS:
            for s in SECTION_ORDER:
                row.append(d["counts"].get((m, s), 0))
        row += [sum(d["counts"].values()), status_of(d), d["last_text"]]
        ws.append(row)
    finish_sheet(
        ws,
        widths=[18, 16, 17, 16, 17] + [7] * (len(MAJORS) * len(SECTION_ORDER)) + [10, 30, 60],
        wrap_cols=(len(headers),),
        datetime_cols=(3, 5),
    )
    log(f"功能模块演进：{len(rows)} 个功能域，{len(headers)} 列")


# --------------------------------------------------------------------------
# 贡献者
# --------------------------------------------------------------------------
def build_contributors(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("贡献者总表")
    headers = ["排名", "登录名", "类型", "提交数", "占比", "主页", "头像地址"]
    ws.append(headers)
    items = [c for c in ctx["contributors"] if c.get("type") != "Anonymous"]
    total = sum(c.get("contributions", 0) for c in items)
    for rank, c in enumerate(items, start=1):
        r = rank + 1
        ws.append([
            rank, c.get("login") or c.get("name"), c.get("type"),
            c.get("contributions"), f"=D{r}/SUM($D$2:$D${len(items)+1})",
            c.get("html_url"), c.get("avatar_url"),
        ])
    finish_sheet(ws, widths=[6, 22, 10, 10, 10, 40, 46], link_cols=(6,))
    for r in range(2, len(items) + 2):
        ws.cell(row=r, column=5).number_format = "0.0%"

    # 版本区间作者分布（身份已按邮箱合并）
    ws2 = wb.create_sheet("贡献者-版本分布")
    identity = ctx["identity"]
    authors = list(identity.keys())
    headers2 = ["版本号", "发布时间", "区间提交数"] + [identity[a]["display"] for a in authors]
    ws2.append(headers2)
    for v in ctx["versions"]:
        per_version: dict[str, int] = {}
        for item in v.get("committer_identities") or []:
            per_version[item["email"]] = per_version.get(item["email"], 0) + item["commits"]
        ws2.append([v["tag"], v["pub_dt"], v["commits"]] + [per_version.get(a, 0) for a in authors])
    finish_sheet(
        ws2,
        widths=[16, 17, 11] + [12] * len(authors),
        datetime_cols=(2,),
        freeze="D2",
    )

    # git 身份对照
    ws3 = wb.create_sheet("贡献者-git身份对照")
    ws3.append(["显示名", "GitHub 登录名", "类型", "提交数", "占比", "提交邮箱", "首次提交版本", "最后提交版本"])
    total_commits = sum(d["commits"] for d in identity.values())
    for i, (email, d) in enumerate(identity.items(), start=2):
        ws3.append([
            d["display"], d["github"] or "—", d["type"], d["commits"],
            f"=D{i}/SUM($D$2:$D${len(identity)+1})",
            email, d["first_version"], d["last_version"],
        ])
    finish_sheet(ws3, widths=[20, 22, 10, 10, 10, 48, 16, 16])
    for r in range(2, len(identity) + 2):
        ws3.cell(row=r, column=5).number_format = "0.0%"
    log(f"贡献者：GitHub {len(items)} 位；git 合并身份 {len(identity)} 个，共 {total_commits} 次提交")


# --------------------------------------------------------------------------
# Star 增长
# --------------------------------------------------------------------------
def build_stars(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("Star增长明细")
    headers = ["序号", "Star时间(北京时间)", "距仓库创建(天)", "当时累计Star", "里程碑", "用户", "用户主页"]
    ws.append(headers)
    created = to_cst(ctx["repo"].get("created_at"))
    milestones = {50, 100, 150, 200, 250, 300, 500, 1000}
    rows = []
    for i, s in enumerate(ctx["stars"], start=1):
        dt = to_cst(s["starred_at"])
        user = s.get("user") or {}
        rows.append([
            i, dt, (dt - created).days if created else None,
            None,
            "★ 达成" if i in milestones else "",
            user.get("login", ""),
            user.get("html_url", ""),
        ])
    write_rows(ws, rows)
    for i in range(len(rows)):
        ws.cell(row=i + 2, column=4, value=f"=ROW()-1")
    finish_sheet(
        ws,
        widths=[6, 20, 16, 13, 10, 22, 40],
        datetime_cols=(2,),
        link_cols=(7,),
    )
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = f"A1:G{len(rows)+1}"

    # 月度汇总
    ws2 = wb.create_sheet("Star增长月度")
    ws2.append(["月份", "当月新增Star", "月末累计Star", "当月发布版本数", "当月发布版本"])
    monthly: dict[str, dict] = {}
    for s in ctx["stars"]:
        dt = to_cst(s["starred_at"])
        key = dt.strftime("%Y-%m")
        monthly.setdefault(key, {"new": 0, "versions": []})
        monthly[key]["new"] += 1
    for v in ctx["versions"]:
        key = v["pub_dt"].strftime("%Y-%m")
        monthly.setdefault(key, {"new": 0, "versions": []})
        monthly[key]["versions"].append(v["tag"])
    cumulative = 0
    r = 2
    month_rows = []
    for key in sorted(monthly):
        info = monthly[key]
        cumulative += info["new"]
        month_rows.append([key, info["new"], None, len(info["versions"]), "、".join(info["versions"]) or "—"])
        ws2.append(month_rows[-1])
        ws2.cell(row=r, column=3, value=f"=SUM($B$2:B{r})")
        r += 1
    finish_sheet(ws2, widths=[12, 14, 14, 14, 60], wrap_cols=(5,))

    chart = LineChart()
    chart.title = "Star 增长（月度）"
    chart.y_axis.title = "Star 数"
    chart.x_axis.title = "月份"
    data = Reference(ws2, min_col=2, max_col=3, min_row=1, max_row=ws2.max_row)
    cats = Reference(ws2, min_col=1, min_row=2, max_row=ws2.max_row)
    chart.add_data(data, titles_from_data=True)
    chart.set_categories(cats)
    chart.height = 9
    chart.width = 22
    ws2.add_chart(chart, "G2")

    # 里程碑
    ws3 = wb.create_sheet("Star里程碑")
    ws3.append(["里程碑", "达成时间(北京时间)", "距仓库创建(天)", "达成时最新版本"])
    versions = ctx["versions"]
    for m in sorted(milestones):
        if len(ctx["stars"]) >= m:
            dt = to_cst(ctx["stars"][m - 1]["starred_at"])
            latest = None
            for v in versions:
                if v["pub_dt"] <= dt:
                    latest = v["tag"]
            ws3.append([f"{m} ★", dt, (dt - created).days if created else None, latest or "—"])
    ws3.append(["当前 Star", to_cst(ctx["repo"].get("fetched_at")), None, versions[-1]["tag"]])
    finish_sheet(ws3, widths=[14, 22, 16, 20], datetime_cols=(2,))
    log(f"Star：{len(ctx['stars'])} 条明细，{len(month_rows)} 个月度行")


# --------------------------------------------------------------------------
# 下载量
# --------------------------------------------------------------------------
def build_downloads(wb: Workbook, ctx: dict) -> None:
    today = datetime.now(CST).replace(tzinfo=None)
    versions = ctx["versions"]
    total = sum(v["downloads"] for v in versions)

    ws = wb.create_sheet("下载量-版本汇总")
    headers = ["版本号", "发布时间", "已发布天数", "本版本下载量", "累计下载量", "日均下载", "占总量比", "资产数", "下载量排名"]
    ws.append(headers)
    for i, v in enumerate(versions):
        r = i + 2
        days = max((today - v["pub_dt"]).days, 1)
        ws.append([
            v["tag"], v["pub_dt"], (today - v["pub_dt"]).days,
            v["downloads"], None,
            f"=IF(C{r}=0,\"\",ROUND(D{r}/C{r},2))",
            f"=D{r}/{total}" if total else 0,
            v["asset_count"],
            f"=RANK(D{r},$D$2:$D${len(versions)+1},0)",
        ])
    for i in range(len(versions)):
        r = i + 2
        ws.cell(row=r, column=5, value=f"=SUM($D$2:D{r})")
    finish_sheet(ws, widths=[16, 17, 12, 13, 13, 11, 11, 8, 11], datetime_cols=(2,))
    for r in range(2, len(versions) + 2):
        ws.cell(row=r, column=7).number_format = "0.0%"

    chart = BarChart()
    chart.title = "各版本资产下载量"
    chart.y_axis.title = "下载量"
    chart.x_axis.title = "版本"
    data = Reference(ws, min_col=4, min_row=1, max_row=len(versions) + 1)
    cats = Reference(ws, min_col=1, min_row=2, max_row=len(versions) + 1)
    chart.add_data(data, titles_from_data=True)
    chart.set_categories(cats)
    chart.height = 10
    chart.width = 30
    ws.add_chart(chart, "K2")

    ws2 = wb.create_sheet("下载量-资产明细")
    ws2.append(["版本号", "发布时间", "资产名", "平台/类型", "架构", "包类型", "大小(MB)", "下载量", "下载链接"])
    for v in versions:
        for a in v["assets"]:
            ws2.append([
                v["tag"], v["pub_dt"], a["name"], platform_of(a["name"]),
                arch_of(a["name"]), kind_of(a["name"]),
                round((a.get("size") or 0) / 1048576, 1), a.get("downloads", 0), a.get("url"),
            ])
    finish_sheet(ws2, widths=[16, 17, 50, 12, 10, 16, 10, 12, 60], datetime_cols=(2,), link_cols=(9,))

    # 平台维度
    platforms = ["Windows", "macOS", "Linux", "Android", "iOS", "校验/清单", "其它"]
    ws2b = wb.create_sheet("下载量-平台维度")
    ws2b.append(["版本号", "发布时间"] + platforms + ["合计"])
    for v in versions:
        agg: dict[str, int] = {}
        for a in v["assets"]:
            p = platform_of(a["name"])
            agg[p] = agg.get(p, 0) + a.get("downloads", 0)
        r = ws2b.max_row + 1
        ws2b.append([v["tag"], v["pub_dt"]] + [agg.get(p, 0) for p in platforms])
        ws2b.cell(row=r, column=2 + len(platforms) + 1, value=f"=SUM(C{r}:{get_column_letter(2+len(platforms))}{r})")
    total_row = ws2b.max_row + 1
    ws2b.append(["合计", None] + [
        f"=SUM({get_column_letter(3+i)}2:{get_column_letter(3+i)}{total_row-1})" for i in range(len(platforms))
    ] + [f"=SUM(C{total_row}:{get_column_letter(2+len(platforms))}{total_row})"])
    finish_sheet(ws2b, widths=[16, 17] + [11] * len(platforms) + [11], datetime_cols=(2,), autofilter=False)
    for c in range(1, ws2b.max_column + 1):
        ws2b.cell(row=total_row, column=c).font = BOLD_FONT

    # 平台 × 架构 / 包类型汇总
    ws2c = wb.create_sheet("下载量-平台架构汇总")
    ws2c.append(["维度", "分类", "总下载量", "占比", "资产数", "涉及版本数", "首次出现版本", "最后出现版本"])
    vmap = {v["tag"]: v for v in versions}

    def add_group(dimension: str, keyfn) -> None:
        groups: dict[str, dict] = {}
        for v in versions:
            for a in v["assets"]:
                key = keyfn(a["name"])
                g = groups.setdefault(key, {"downloads": 0, "assets": 0, "versions": set(), "first": v, "last": v})
                g["downloads"] += a.get("downloads", 0)
                g["assets"] += 1
                g["versions"].add(v["tag"])
                if v["index"] < g["first"]["index"]:
                    g["first"] = v
                if v["index"] > g["last"]["index"]:
                    g["last"] = v
        for key, g in sorted(groups.items(), key=lambda kv: -kv[1]["downloads"]):
            ws2c.append([
                dimension, key, g["downloads"], 0, g["assets"], len(g["versions"]),
                g["first"]["tag"], g["last"]["tag"],
            ])

    add_group("平台", platform_of)
    add_group("架构", arch_of)
    add_group("包类型", kind_of)
    grand_total = sum(v["downloads"] for v in versions) or 1
    for r in range(2, ws2c.max_row + 1):
        ws2c.cell(row=r, column=4, value=f"=C{r}/{grand_total}")
    finish_sheet(ws2c, widths=[10, 22, 12, 10, 10, 12, 16, 16])
    for r in range(2, ws2c.max_row + 1):
        ws2c.cell(row=r, column=4).number_format = "0.0%"

    ws3 = wb.create_sheet("下载量-快照时间线")
    ws3.append(["快照时间(UTC)", "Star", "Fork", "资产下载总量", "较上次新增下载", "说明"])
    prev_total = None
    for s in ctx["snapshots"]:
        rel = s.get("releases") or []
        total_now = sum(item.get("downloads", 0) for item in rel)
        delta = "" if prev_total is None else total_now - prev_total
        ws3.append([
            s.get("snapshot_at"), s.get("stars"), s.get("forks"), total_now, delta,
            "首次快照（此后每次运行 fetch 脚本都会追加一行，用于还原真实下载演进）" if prev_total is None else "",
        ])
        prev_total = total_now
    finish_sheet(ws3, widths=[24, 10, 10, 16, 16, 70], wrap_cols=(6,))
    log(f"下载量：{len(versions)} 个版本，快照 {len(ctx['snapshots'])} 条")


# --------------------------------------------------------------------------
# 大版本主线
# --------------------------------------------------------------------------
def render_point(item) -> str:
    """关键节点支持两种写法：纯文本，或 {text, evidence} —— 后者渲染成「内容（版本证据）」。"""
    if isinstance(item, dict):
        text = str(item.get("text", "")).strip()
        evidence = str(item.get("evidence", "")).strip()
        return f"{text}（{evidence}）" if evidence else text
    return str(item)


def build_majors(wb: Workbook, ctx: dict) -> None:
    ws = wb.create_sheet("大版本主线")
    ws.append(["大版本", "技术栈", "首发版本", "首发时间", "末版", "末版时间", "版本数",
               "主线主题", "关键节点（附版本证据）", "主要代价/取舍"])
    for m in ctx["curated"].get("major_versions", []):
        ws.append([
            m.get("major"), m.get("stack"), m.get("first_release"), m.get("first_date"),
            m.get("last_release"), m.get("last_date"), m.get("release_count"),
            m.get("theme"),
            "\n".join(f"• {render_point(x)}" for x in m.get("key_points", [])),
            "\n".join(f"• {render_point(x)}" for x in m.get("tradeoffs", [])),
        ])
    finish_sheet(ws, widths=[8, 40, 14, 12, 14, 12, 8, 46, 78, 60], wrap_cols=(2, 8, 9, 10), autofilter=False)
    for r in range(2, ws.max_row + 1):
        ws.row_dimensions[r].height = 190
    log("大版本主线：已写入（关键节点均附版本证据）")


def main() -> int:
    for path in (DATA_DIR / "releases.json", DATA_DIR / "parsed-changelog.json", DATA_DIR / "stargazers.json"):
        if not path.exists():
            log(f"缺少 {path.relative_to(ROOT)}，请先运行 fetch_github_data.py 与 parse_changelog.py")
            return 1
    ctx = prepare()
    wb = Workbook()
    wb.remove(wb.active)
    build_guide(wb, ctx)
    build_overview(wb, ctx)
    build_entries(wb, ctx)
    build_delta(wb, ctx)
    build_sacrifices(wb, ctx)
    build_domains(wb, ctx)
    build_contributors(wb, ctx)
    build_stars(wb, ctx)
    build_downloads(wb, ctx)
    build_majors(wb, ctx)
    OUT_XLSX.parent.mkdir(parents=True, exist_ok=True)
    # 原子替换：Excel 打开着目标文件时替换会失败，先把新内容写成临时文件再换名
    tmp_path = OUT_XLSX.with_name(OUT_XLSX.stem + ".tmp.xlsx")
    wb.save(tmp_path)
    try:
        tmp_path.replace(OUT_XLSX)
    except PermissionError:
        log(f"写入失败：{OUT_XLSX.name} 正被其它程序占用（Excel/WPS 打开着？）")
        log(f"新内容已保存到 {tmp_path.name}，关闭占用后重跑本脚本即可")
        return 2
    size = OUT_XLSX.stat().st_size / 1024
    log(f"写入 {OUT_XLSX.relative_to(ROOT)}（{size:.0f} KiB，{len(wb.sheetnames)} 个工作表）")
    for name in wb.sheetnames:
        ws = wb[name]
        log(f"  · {name}: {ws.max_row - 1} 行 × {ws.max_column} 列")
    return 0


if __name__ == "__main__":
    sys.exit(main())
