#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""解析 CHANGELOG/ 与本地 git，产出结构化 JSON 中间数据。

产出 docs/release-history/data/parsed-changelog.json：
  versions[]  每个版本的元数据 + 分类型条目 + git 区间贡献者
  entries[]   全部条目（版本、类型、功能域、正文、关联 Issue/PR）
  meta        解析告警（未识别标题、疑似异常内容）

设计要点：
  * v1.x 的条目类型写在内联前缀里（“新增 xxx”“优化 xxx”），标题只是模块分组；
    v2/v3 的条目类型来自 `## 🚀 主要更新` 这类标题。两者都要支持。
  * v1.2.0.0 是累积式更新日志（内含 `> v1.1.2.0 更新日志` 子块），
    子块条目归到其真实版本，并与该版本自身文件的条目去重。
"""
from __future__ import annotations

import difflib
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHANGELOG_DIR = ROOT / "CHANGELOG"
DATA_DIR = ROOT / "docs" / "release-history" / "data"

SECTION_KEYWORDS: list[tuple[str, str]] = [
    ("贡献者", "贡献者"),
    ("移除功能", "移除"),
    ("移除", "移除"),
    ("去除", "移除"),
    ("修复", "修复"),
    ("bug", "修复"),
    # “新增功能与优化”这类标题同时含“新增”和“优化”，必须先判新增
    ("主要更新", "新增"),
    ("新增", "新增"),
    ("新内容", "新增"),
    ("优化", "优化"),
    ("其它", "其它"),
    ("其他", "其它"),
    ("已知问题", "其它"),
    ("注意事项", "其它"),
    ("重要提示", "其它"),
]

# 条目内联前缀 -> 变更类型（按长度降序匹配，避免“删除”被“删”抢先）
PREFIX_TYPES: list[tuple[str, str]] = [
    ("新增", "新增"),
    ("新加", "新增"),
    ("添加", "新增"),
    ("增加", "新增"),
    ("支持", "新增"),
    ("优化", "优化"),
    ("改进", "优化"),
    ("调整", "优化"),
    ("修改", "优化"),
    ("替换", "优化"),
    ("重构", "优化"),
    ("提升", "优化"),
    ("完善", "优化"),
    ("修复", "修复"),
    ("解决", "修复"),
    ("修正", "修复"),
    ("补上", "修复"),
    ("移除", "移除"),
    ("去除", "移除"),
    ("删除", "移除"),
    ("下线", "移除"),
    ("停用", "移除"),
    ("废弃", "移除"),
    ("取消", "移除"),
    ("变更", "其它"),
    ("说明", "其它"),
]

# 功能域关键词表，顺序即优先级
DOMAIN_RULES: list[tuple[str, tuple[str, ...]]] = [
    ("抽取可验证/公证", ("可验证", "公证", "审计", "存证", "srproof", "公平性证明", "证明文件")),
    ("公平抽取算法", ("公平抽取", "权重", "概率", "平均值", "随机", "算法", "不重复", "剩余人数", "已抽取", "抽取池")),
    ("课程联动", ("课程", "cses", "classisland", "联动", "上课", "下课", "课表", "课时")),
    ("语音播报", ("语音", "播报", "tts", "edge", "omnitts", "朗读")),
    ("音乐播放", ("音乐", "mp3", "wav", "flac", "音效", "音量")),
    ("抽取流程", ("抽人", "抽单人", "抽多人", "抽取", "即抽", "闪抽", "点名", "抽选", "抽取方式", "抽取动画", "抽取人数")),
    ("奖励/奖品", ("奖品", "奖池", "抽奖", "奖励", "奖项")),
    ("名单与班级", ("名单", "学生", "班级", "小组", "花名册", "性别", "学号", "名单导入")),
    ("历史记录", ("历史记录", "历史", "抽取记录", "记录", "过期")),
    ("浮窗与通知", ("浮窗", "悬浮窗", "通知", "闪抽窗口", "置顶")),
    ("安全与验证", ("安全", "密码", "验证", "totp", "2fa", "usb", "加密", "防篡改", "凭据", "锁定", "解锁", "密钥")),
    ("账户与云服务", ("账户", "账号", "登录", "oauth", "云端", "云备份", "sectl", "心跳")),
    ("备份与迁移", ("备份", "恢复", "归档", "迁移", "存档", "导入导出")),
    ("插件系统", ("插件", "srpx", "plugin")),
    ("托盘与系统集成", ("托盘", "开机自启", "自启动", "多开", "单实例", "快捷键", "系统音量", "进程", "重启功能")),
    ("界面与主题", ("主题", "界面", "ui", "样式", "字体", "图标", "配色", "深色", "浅色", "窗口", "布局", "动画", "缩放", "导航", "控件", "侧边栏", "选项卡")),
    ("设置系统", ("设置", "配置", "选项", "开关", "参数", "页面")),
    ("更新与分发", ("更新", "升级", "安装包", "发布", "版本检查", "下载", "更新日志界面")),
    ("跨平台支持", ("windows", "linux", "macos", "android", "ios", "移动端", "平台", "debian", "arm", "x64", "x86", "架构")),
    ("性能与稳定性", ("性能", "内存", "cpu", "卡顿", "崩溃", "闪退", "启动速度", "卡退", "假死", "占用", "响应")),
    ("日志与诊断", ("日志", "诊断", "异常", "报错", "错误提示")),
    ("多语言", ("多语言", "语言切换", "翻译", "本地化", "i18n", "英文", "日文")),
    ("构建与发布流程", ("ci", "工作流", "action", "签名", "构建", "编译", "流水线", "nuget", "安装包制作")),
    ("文档与站点", ("文档", "readme", "官网", "帮助", "关于", "公告", "wiki", "deepwiki")),
    ("OOBE/引导", ("引导", "oobe", "首次使用", "欢迎")),
    ("捐赠与运营", ("捐赠", "捐献", "赞助", "活动")),
]

REMOVAL_HINTS = ("移除", "去除", "删除", "不再支持", "取消", "下线", "停用", "废弃", "弃用", "降级", "收窄")

ANOMALY_PATTERNS = ("单词", "答题", "PK功能", "学习")

RE_HEADING = re.compile(r"^\s{0,3}#{1,4}\s*(.+?)\s*$")
RE_QUOTE_VERSION = re.compile(r"^\s*>\s*\**\s*(v[\d][\w.\-]*)\s*更新日志")
RE_BULLET = re.compile(r"^\s*(?:[-*+]\s+|\d+\.\s+)(.*)$")
RE_MD_LINK = re.compile(r"\[([^\]]*)\]\(([^)]*)\)")
RE_ISSUE = re.compile(r"(?:issues|pull)/(\d+)")
RE_TAG = re.compile(r"[【\[]([^】\]]{1,24})[】\]]")
RE_EMOJI_PREFIX = re.compile(
    "^(?:[\\s\\u2600-\\u27BF\\uFE0F\\u2B00-\\u2BFF✅❌⚠️🚀💡🐛🔧🎉🙏✨🎯📢📣📅📜👥🎁🏮🛠️⚙️🎨🔹▪●•]+)"
)


def log(msg: str) -> None:
    print(f"[parse] {msg}", flush=True)


def classify_section(heading: str) -> str | None:
    low = heading.lower()
    for kw, section in SECTION_KEYWORDS:
        if kw in low:
            return section
    return None


def clean_text(raw: str) -> str:
    text = raw.strip()
    for _ in range(3):
        new = RE_EMOJI_PREFIX.sub("", text).strip()
        new = re.sub(r"^\*\*(.+?)\*\*\s*[:：]?\s*", r"\1 ", new)
        new = re.sub(r"^\*+\s*", "", new).strip()
        if new == text:
            break
        text = new
    text = RE_MD_LINK.sub(lambda m: m.group(1) or m.group(2), text)
    text = text.replace("**", "").replace("`", "")
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text.strip(" -—·:：")


def prefix_type(text: str) -> str | None:
    head = text[:4]
    for kw, section in PREFIX_TYPES:
        if head.startswith(kw):
            rest = text[len(kw):]
            if not rest.strip():
                return None
            return section
    return None


def refs_of(text: str) -> tuple[list[int], list[int]]:
    issues: list[int] = []
    pulls: list[int] = []
    for m in re.finditer(r"\[([^\]]*)\]\((https://github\.com/[^)]+)\)", text):
        label, url = m.group(1), m.group(2)
        mm = RE_ISSUE.search(url)
        if not mm:
            continue
        num = int(mm.group(1))
        if "pull" in url or "PR" in label.upper():
            pulls.append(num)
        else:
            issues.append(num)
    return sorted(set(issues)), sorted(set(pulls))


def classify_domain(text: str) -> str | None:
    low = text.lower()
    for domain, kws in DOMAIN_RULES:
        for kw in kws:
            if kw in low:
                return domain
    return None


def normalize_version(raw: str, tags: list[str]) -> str:
    if raw in tags:
        return raw
    cands = difflib.get_close_matches(raw, tags, n=1, cutoff=0.75)
    if cands:
        return cands[0]
    pref = [t for t in tags if t.startswith(raw) or raw.startswith(t)]
    if pref:
        return max(pref, key=len)
    return raw


def resolve_token(token: str, file_version: str, tags: list[str]) -> str:
    """把标题/引用里的版本号解析为真实 tag。

    标题里的版本号往常常是版本前缀（如 v3.0.0-beta.1 的标题写 `# v3.0.0 - Nonomi Beta 1`），
    此时必须保留文件自身版本，否则会把条目错误归到同族的其它版本。
    """
    resolved = normalize_version(token, tags)
    if file_version.startswith(token) or token.startswith(file_version):
        return file_version
    return resolved


def parse_file(path: Path, file_version: str, tags: list[str]) -> tuple[list[dict], list[str]]:
    section = "其它"
    section_known = False
    ctx_domain: str | None = None
    sub_version = file_version
    entries: list[dict] = []
    unknown_headings: list[str] = []
    in_tail = False  # Full Changelog 之后的下载/校验表格，永久跳过
    skip_block = False  # 仅跳过一个公告类小节，遇到下一个正式分组即恢复
    seq = 0

    for line in path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped.startswith("Full Changelog:"):
            in_tail = True
            continue

        heading = RE_HEADING.match(line)
        if heading:
            title = clean_text(heading.group(1))
            if title.lower().startswith("full changelog"):
                in_tail = True
                continue
            if re.match(r"^v\d", title):
                token = re.match(r"^(v[\d][\w.\-]*)", title).group(1)
                sub_version = resolve_token(token, file_version, tags)
                continue
            sec = classify_section(title)
            if sec:
                section = sec
                section_known = True
                ctx_domain = None
                skip_block = False
                if sec == "贡献者":
                    in_tail = True
            elif any(
                k in title
                for k in (
                    "下载",
                    "仓库日志",
                    "插件上架",
                    "解压",
                    "选择系统架构",
                    "选择打包模式",
                    "参与方式",
                    "活动时间",
                )
            ):
                skip_block = True
            else:
                ctx_domain = classify_domain(title) or ctx_domain
                if title and not title.startswith("v"):
                    unknown_headings.append(title)
                skip_block = False
            continue

        quote = RE_QUOTE_VERSION.match(line)
        if quote:
            sub_version = resolve_token(quote.group(1), file_version, tags)
            continue

        if in_tail or skip_block or stripped.startswith("|") or stripped.startswith(">"):
            continue
        bullet = RE_BULLET.match(line)
        if not bullet:
            continue
        raw = bullet.group(1).strip()
        if not raw or raw.startswith("|"):
            continue
        text = clean_text(raw)
        if len(text) < 2 or text in ("无", "暂无"):
            continue
        if "Full Changelog" in text or text.startswith("http"):
            continue

        ptype = prefix_type(text)
        if ptype:
            entry_section = ptype
            section_source = "条目前缀"
        else:
            entry_section = section if section_known else "其它"
            section_source = "标题分组"

        tag_hit = RE_TAG.search(raw)
        module_tag = tag_hit.group(1).strip() if tag_hit else ""
        domain = classify_domain(text)
        domain_source = "正文关键词"
        if domain is None and module_tag:
            domain = classify_domain(module_tag)
            domain_source = "【模块】标签"
        if domain is None and ctx_domain:
            domain = ctx_domain
            domain_source = "模块标题"
        if domain is None:
            domain = "其它/未归类"
            domain_source = "未归类"

        issues, pulls = refs_of(raw)
        seq += 1
        entries.append(
            {
                "file_version": file_version,
                "version": sub_version,
                "order": seq,
                "is_own_version": sub_version == file_version,
                "section": entry_section,
                "section_source": section_source,
                "title_section": section,
                "domain": domain,
                "domain_source": domain_source,
                "module_tag": module_tag,
                "text": text,
                "raw": raw,
                "issues": issues,
                "pulls": pulls,
                "removal_hint": any(h in text for h in REMOVAL_HINTS),
            }
        )
    return entries, unknown_headings


def git(*args: str) -> str:
    proc = subprocess.run(
        ["git", "-C", str(ROOT), *args], capture_output=True, text=True, encoding="utf-8", timeout=300
    )
    return proc.stdout if proc.returncode == 0 else ""


def main() -> int:
    releases_path = DATA_DIR / "releases.json"
    if not releases_path.exists():
        log("缺少 data/releases.json，请先运行 fetch_github_data.py")
        return 1
    releases = json.loads(releases_path.read_text(encoding="utf-8"))["items"]
    tags = sorted(r["tag_name"] for r in releases)

    files = sorted(CHANGELOG_DIR.glob("**/CHANGELOG.md"))
    log(f"发现 {len(files)} 份 CHANGELOG")

    raw_entries: list[dict] = []
    unknown: dict[str, list[str]] = {}
    for path in files:
        version = path.parent.name
        entries, unknown_headings = parse_file(path, version, tags)
        if unknown_headings:
            unknown[version] = sorted(set(unknown_headings))
        raw_entries.extend(entries)

    dedup: dict[tuple, dict] = {}
    for e in raw_entries:
        key = (e["version"], e["section"], e["text"])
        cur = dedup.get(key)
        if cur is None or (e["is_own_version"] and not cur["is_own_version"]):
            dedup[key] = e
    entries = sorted(
        dedup.values(),
        key=lambda e: (e["version"], e["order"]),
    )
    log(f"条目 {len(raw_entries)} -> 去重后 {len(entries)}")

    # 累积式更新日志（如 v2.0.0 重述 v1.3.2-alpha.*）会在多个版本重复同一条目，
    # 只有最早出现的版本才算“该版本引入”，后续版本标记为累积复述。
    ordered_releases = sorted(releases, key=lambda r: (r.get("published_at") or ""))
    version_index = {r["tag_name"]: i for i, r in enumerate(ordered_releases, start=1)}
    first_seen: dict[tuple, str] = {}
    for e in sorted(entries, key=lambda x: (version_index.get(x["version"], 999), x["order"])):
        key = (e["section"], e["text"])
        origin = first_seen.get(key)
        if origin and origin != e["version"]:
            e["is_repeat"] = True
            e["first_seen_version"] = origin
            e["first_seen_index"] = version_index.get(origin)
        else:
            e["is_repeat"] = False
            e["first_seen_version"] = e["version"]
            e["first_seen_index"] = version_index.get(e["version"])
            first_seen.setdefault(key, e["version"])
    repeat_count = sum(1 for e in entries if e["is_repeat"])
    log(f"其中 {repeat_count} 条为累积更新日志里的复述条目")

    local_versions = {p.parent.name for p in files}
    # 大版本归属以仓库自身的 CHANGELOG/<版本树>/ 目录为准：
    # v1.3.2-alpha.1~6 虽沿用 v1.3.2 标签号，但自述为 “v2.0 ... Release Alpha N” 且位于 CHANGELOG/v2/。
    family_map = {p.parent.name: p.parent.parent.name for p in files}
    orphan = sorted({e["version"] for e in entries} - set(tags))
    if orphan:
        log(f"警告：条目归属到未知版本: {orphan}")

    ordered = sorted(releases, key=lambda r: (r.get("published_at") or ""))
    versions: list[dict] = []
    prev_dt = None
    first_dt = None
    for idx, r in enumerate(ordered, start=1):
        tag = r["tag_name"]
        pub = r.get("published_at") or ""
        pub_dt = datetime.fromisoformat(pub.replace("Z", "+00:00")) if pub else None
        gap = None
        if pub_dt:
            if first_dt is None:
                first_dt = pub_dt
            gap = (pub_dt - prev_dt).days if prev_dt else None
            prev_dt = pub_dt
        own = [e for e in entries if e["version"] == tag]
        own_fresh = [e for e in own if not e["is_repeat"]]
        counts = {"新增": 0, "优化": 0, "修复": 0, "移除": 0, "其它": 0}
        counts_all = {"新增": 0, "优化": 0, "修复": 0, "移除": 0, "其它": 0}
        for e in own:
            counts_all[e["section"]] = counts_all.get(e["section"], 0) + 1
            if not e["is_repeat"]:
                counts[e["section"]] = counts.get(e["section"], 0) + 1
        assets = r.get("assets") or []
        versions.append(
            {
                "index": idx,
                "tag": tag,
                "name": r.get("name") or tag,
                "family": family_map.get(tag, tag.split(".")[0]),
                "tag_major": tag.split(".")[0],
                "family_from_changelog": tag in family_map,
                "prerelease": bool(r.get("prerelease")),
                "published_at": pub,
                "gap_days": gap,
                "html_url": r.get("html_url"),
                "author": (r.get("author") or {}).get("login"),
                "assets": [
                    {
                        "name": a.get("name"),
                        "size": a.get("size"),
                        "downloads": a.get("download_count") or 0,
                        "url": a.get("browser_download_url"),
                    }
                    for a in assets
                ],
                "asset_count": len(assets),
                "downloads": sum((a.get("download_count") or 0) for a in assets),
                "entry_counts": counts,
                "entry_counts_all": counts_all,
                "entry_total": sum(counts.values()),
                "entry_total_all": sum(counts_all.values()),
                "repeat_total": sum(counts_all.values()) - sum(counts.values()),
                "domains": sorted({e["domain"] for e in own_fresh}),
                "issues": sorted({n for e in own for n in e["issues"]}),
                "pulls": sorted({n for e in own for n in e["pulls"]}),
                "has_local_changelog": tag in local_versions,
            }
        )

    no_changelog = [v["tag"] for v in versions if not v["has_local_changelog"]]
    if no_changelog:
        log(f"警告：以下 Release 无本地 CHANGELOG: {no_changelog}")

    log("统计 git 区间贡献者…")
    for i, v in enumerate(versions):
        prev_tag = versions[i - 1]["tag"] if i > 0 else None
        rng = f"{prev_tag}..{v['tag']}" if prev_tag else v["tag"]
        out = git("log", rng, "--no-merges", "--format=%an\t%ae")
        by_name: dict[str, int] = {}
        by_email: dict[str, int] = {}
        names_by_email: dict[str, dict[str, int]] = {}
        for line in out.splitlines():
            if not line.strip():
                continue
            name, _, email = line.partition("\t")
            name, email = name.strip(), email.strip()
            by_name[name] = by_name.get(name, 0) + 1
            by_email[email] = by_email.get(email, 0) + 1
            slot = names_by_email.setdefault(email, {})
            slot[name] = slot.get(name, 0) + 1
        v["commit_range"] = rng
        v["commits"] = sum(by_name.values())
        v["committers"] = by_name
        v["committer_emails"] = by_email
        v["committer_identities"] = [
            {
                "email": email,
                "name": max(names.items(), key=lambda kv: kv[1])[0],
                "commits": sum(names.values()),
            }
            for email, names in sorted(
                names_by_email.items(), key=lambda kv: -sum(kv[1].values())
            )
        ]
        v["tag_date"] = git("log", "-1", "--format=%cI", v["tag"]).strip()

    anomalies = [
        {"version": e["version"], "text": e["text"]}
        for e in entries
        if any(p in e["text"] for p in ANOMALY_PATTERNS)
    ]

    payload = {
        "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "versions": versions,
        "entries": entries,
        "unknown_headings": unknown,
        "anomalies": anomalies,
    }
    out_path = DATA_DIR / "parsed-changelog.json"
    out_path.write_text(json.dumps(payload, ensure_ascii=False, indent=1), encoding="utf-8")
    log(f"写入 {out_path.relative_to(ROOT)}")

    section_counts: dict[str, int] = {}
    domain_counts: dict[str, int] = {}
    source_counts: dict[str, int] = {}
    for e in entries:
        section_counts[e["section"]] = section_counts.get(e["section"], 0) + 1
        domain_counts[e["domain"]] = domain_counts.get(e["domain"], 0) + 1
        source_counts[e["section_source"]] = source_counts.get(e["section_source"], 0) + 1
    log(f"变更类型分布: {dict(sorted(section_counts.items(), key=lambda kv: -kv[1]))}")
    log(f"类型来源分布: {source_counts}")
    log("功能域分布:")
    for d, c in sorted(domain_counts.items(), key=lambda kv: -kv[1]):
        log(f"  {d}: {c}")
    if unknown:
        log("未识别标题（已作为模块上下文）:")
        for v, hs in unknown.items():
            log(f"  {v}: {hs}")
    log(f"疑似混入其它项目内容: {len(anomalies)} 条")
    return 0


if __name__ == "__main__":
    sys.exit(main())
