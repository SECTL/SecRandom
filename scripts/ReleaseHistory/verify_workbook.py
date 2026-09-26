#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""校验生成的 XLSX：压缩包完整性、XML 合法性、工作表结构、公式引用、关键数字自洽。

用法：python scripts/ReleaseHistory/verify_workbook.py
"""
from __future__ import annotations

import json
import re
import sys
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path
from xml.etree import ElementTree

from openpyxl import load_workbook
from openpyxl.utils import get_column_letter

ROOT = Path(__file__).resolve().parents[2]
HIST_DIR = ROOT / "docs" / "release-history"
XLSX = HIST_DIR / "SecRandom-版本历史.xlsx"
DATA_DIR = HIST_DIR / "data"
CST = timezone(timedelta(hours=8))

problems: list[str] = []
checks = 0


def check(cond: bool, msg: str) -> None:
    global checks
    checks += 1
    if not cond:
        problems.append(msg)


def main() -> int:
    global checks
    check(XLSX.exists(), f"缺少 {XLSX}")
    if not XLSX.exists():
        return 1

    # 1) 压缩包 / XML
    with zipfile.ZipFile(XLSX) as zf:
        bad = zf.testzip()
        check(bad is None, f"压缩包损坏：{bad}")
        names = zf.namelist()
        check("xl/workbook.xml" in names, "缺少 xl/workbook.xml")
        xml_fail = 0
        for name in names:
            if name.endswith(".xml") or name.endswith(".rels"):
                try:
                    ElementTree.fromstring(zf.read(name))
                except ElementTree.ParseError as exc:
                    xml_fail += 1
                    problems.append(f"XML 解析失败 {name}: {exc}")
        checks += 1
        check(xml_fail == 0, f"{xml_fail} 个 XML 部件解析失败")

    wb = load_workbook(XLSX)
    sheetnames = wb.sheetnames
    check(len(sheetnames) >= 10, f"工作表数量偏少：{len(sheetnames)}")
    for name in sheetnames:
        check(len(name) <= 31, f"工作表名过长：{name}")
        check(not any(ch in name for ch in "[]:*?/\\"), f"工作表名含非法字符：{name}")

    vmap = {v["tag"]: v for v in json.loads((DATA_DIR / "parsed-changelog.json").read_text(encoding="utf-8"))["versions"]}
    repo = json.loads((DATA_DIR / "repo.json").read_text(encoding="utf-8"))
    stars = json.loads((DATA_DIR / "stargazers.json").read_text(encoding="utf-8"))["items"]

    # 2) 公式引用的工作表必须存在
    formula_sheets: set[str] = set()
    formula_count = 0
    for name in sheetnames:
        ws = wb[name]
        for row in ws.iter_rows():
            for cell in row:
                if isinstance(cell.value, str) and cell.value.startswith("="):
                    formula_count += 1
                    for ref in re.findall(r"'([^']+)'!", cell.value):
                        formula_sheets.add(ref)
                    for ref in re.findall(r"(?<![A-Za-z0-9_'])([A-Za-z\u4e00-\u9fff_]+)!", cell.value):
                        formula_sheets.add(ref)
    for ref in formula_sheets:
        check(ref in sheetnames, f"公式引用了不存在的工作表：{ref}")
    check(formula_count > 0, "没有任何公式")

    # 3) 关键数字自洽
    ws = wb["版本总览"]
    headers = [c.value for c in ws[1]]
    idx = {h: i for i, h in enumerate(headers)}
    row_count = ws.max_row - 1
    check(row_count == len(vmap), f"版本总览行数 {row_count} != 版本数 {len(vmap)}")
    total_dl = 0
    for r in range(2, ws.max_row + 1):
        tag = ws.cell(row=r, column=idx["版本号"] + 1).value
        dl = ws.cell(row=r, column=idx["本版本下载量"] + 1).value
        check(tag in vmap, f"版本总览出现未知版本：{tag}")
        if tag in vmap:
            check(dl == vmap[tag]["downloads"], f"{tag} 下载量不一致：{dl} != {vmap[tag]['downloads']}")
        total_dl += dl or 0
    snap_total = sum(a.get("downloads", 0) for v in vmap.values() for a in v["assets"])
    check(total_dl == snap_total, f"下载量合计不一致：{total_dl} != {snap_total}")

    ws_entries = wb["更新要点明细"]
    check(ws_entries.max_row - 1 >= 1000, f"更新要点明细行数偏少：{ws_entries.max_row - 1}")
    ws_delta = wb["版本间差异"]
    check(ws_delta.max_row - 1 == row_count, "版本间差异行数与版本数不一致")
    ws_sac = wb["功能牺牲与降级"]
    check(ws_sac.max_row - 1 >= 20, f"功能牺牲与降级行数偏少：{ws_sac.max_row - 1}")
    ws_dom = wb["功能模块演进"]
    check(ws_dom.max_row - 1 >= 15, f"功能模块演进行数偏少：{ws_dom.max_row - 1}")
    ws_star = wb["Star增长明细"]
    check(ws_star.max_row - 1 == len(stars), f"Star 明细 {ws_star.max_row - 1} != {len(stars)}")
    ws_dl = wb["下载量-资产明细"]
    asset_total = sum(len(v["assets"]) for v in vmap.values())
    check(ws_dl.max_row - 1 == asset_total, f"资产明细 {ws_dl.max_row - 1} != {asset_total}")

    # 平台识别必须覆盖所有资产：命名规则变化时这里会先失败，而不是悄悄变成 0
    import importlib.util

    spec = importlib.util.spec_from_file_location("builder", Path(__file__).with_name("build_workbook.py"))
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    unknown_platform = [
        a["name"] for v in vmap.values() for a in v["assets"] if builder.platform_of(a["name"]) == "其它"
    ]
    check(not unknown_platform, f"有资产无法识别平台（命名规则可能变了）：{unknown_platform[:5]}")
    for major in ("v1", "v2", "v3"):
        win = sum(
            a.get("downloads", 0)
            for tag, v in vmap.items()
            if tag.startswith(major + ".")
            for a in v["assets"]
            if builder.platform_of(a["name"]) == "Windows"
        )
        has_win_asset = any(
            builder.platform_of(a["name"]) == "Windows"
            for tag, v in vmap.items()
            if tag.startswith(major + ".")
            for a in v["assets"]
        )
        check((not has_win_asset) or win > 0, f"{major} 有 Windows 资产但 Windows 下载量为 0")
    ws_contrib = wb["贡献者总表"]
    gh_contribs = json.loads((DATA_DIR / "contributors.json").read_text(encoding="utf-8"))["items"]
    check(ws_contrib.max_row - 1 == len([c for c in gh_contribs if c.get("type") != "Anonymous"]),
          "贡献者总表行数与 GitHub 贡献者数不一致")

    # 4) 抽查：累计下载量公式的首行/末行取值口径
    last_row = ws.max_row
    dl_letter = get_column_letter(idx["本版本下载量"] + 1)
    check(
        ws.cell(row=last_row, column=idx["累计下载量"] + 1).value == f"=SUM(${dl_letter}$2:{dl_letter}{last_row})",
        "累计下载量公式范围不符",
    )
    pub_letter = get_column_letter(idx["发布时间(北京时间)"] + 1)
    check(
        str(ws.cell(row=last_row, column=idx["发布时累计Star"] + 1).value).startswith(
            f"=COUNTIFS('Star增长明细'!$B$2:$B$100000,\"<=\"&{pub_letter}"
        ),
        "发布时累计 Star 公式引用不符",
    )

    # 5) 基准事实抽查
    check(repo["stars"] == len(stars), f"repo.stars {repo['stars']} != stargazers 条数 {len(stars)}")

    # 6) 大版本归属必须与仓库 CHANGELOG 目录一致
    fam_map = {p.parent.name: p.parent.parent.name for p in (ROOT / "CHANGELOG").glob("**/CHANGELOG.md")}
    for tag, v in vmap.items():
        if tag in fam_map:
            check(v.get("family") == fam_map[tag], f"{tag} 的大版本归属 {v.get('family')} != 目录 {fam_map[tag]}")

    # 7) 人工注释层引用的版本必须真实存在（防止手写结论指向不存在的版本）
    curated = json.loads((HIST_DIR / "curated" / "annotations.json").read_text(encoding="utf-8"))
    tags = set(vmap)
    for major in curated.get("major_versions", []):
        for item in major.get("key_points", []):
            evidence = item.get("evidence", "") if isinstance(item, dict) else ""
            for token in re.findall(r"v\d[\w.\-]*", evidence):
                token = token.rstrip(".,;、）)")
                if re.fullmatch(r"v\d", token):
                    continue
                check(token in tags, f"大版本主线引用了不存在的版本：{token}")
    for s in curated.get("sacrifices", []):
        check(s.get("version") in tags, f"牺牲条目引用了不存在的版本：{s.get('version')}")
    for note in curated.get("data_quality", []):
        check(note.get("version") in tags, f"数据质量提示引用了不存在的版本：{note.get('version')}")

    print(f"[verify] 检查项 {checks}，公式 {formula_count} 个，工作表 {len(sheetnames)} 个")
    if problems:
        print(f"[verify] 发现 {len(problems)} 个问题：")
        for p in problems:
            print(f"  ✗ {p}")
        return 1
    print("[verify] 全部通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
