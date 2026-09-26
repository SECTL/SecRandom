#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""抓取 SECTL/SecRandom 的 GitHub 公开数据并缓存到 docs/release-history/data/.

产出（可重复运行，覆盖式刷新 + 追加式快照）：
  data/releases.json            全部 Release：标签、发布时间、正文、资产与下载量
  data/stargazers.json          Star 明细（含 starred_at，可还原 Star 增长曲线）
  data/contributors.json        贡献者列表（含提交数）
  data/tags.json                标签与其指向的提交
  data/downloads-snapshots.jsonl 每次运行追加一条下载量快照（用于真实下载演进曲线）

用法:
  python scripts/ReleaseHistory/fetch_github_data.py
  python scripts/ReleaseHistory/fetch_github_data.py --repo SECTL/SecRandom

数据来源优先级:
  1. 本机已登录的 gh CLI（gh auth login）——Star 时间线需要身份，只有它能取到 starred_at
  2. 匿名 GitHub REST API + GITHUB_TOKEN（环境变量，可选）
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

API = "https://api.github.com"
ROOT = Path(__file__).resolve().parents[2]
DATA_DIR = ROOT / "docs" / "release-history" / "data"
GH = shutil.which("gh")


def log(msg: str) -> None:
    print(f"[fetch] {msg}", flush=True)


def gh_available() -> bool:
    if not GH:
        return False
    try:
        proc = subprocess.run([GH, "auth", "status"], capture_output=True, text=True, timeout=30)
        return proc.returncode == 0
    except Exception:
        return False


def gh_api_json(path: str, paginate: bool = True, accept: str | None = None) -> list:
    """用 gh CLI 取 JSON；--paginate + --jq '.[]' 输出 JSONL 后逐行解析。"""
    cmd = [GH, "api", "--paginate", "--jq", ".[]", path]
    if accept:
        cmd[2:2] = ["-H", f"Accept: {accept}"]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", timeout=600)
    if proc.returncode != 0:
        raise RuntimeError(f"gh api 失败: {path}\n{proc.stderr[:500]}")
    items = []
    for line in proc.stdout.splitlines():
        line = line.strip()
        if line:
            items.append(json.loads(line))
    return items


def request(url: str, accept: str = "application/vnd.github+json") -> tuple[object, dict]:
    headers = {
        "User-Agent": "SecRandom-release-history",
        "Accept": accept,
        "X-GitHub-Api-Version": "2022-11-28",
    }
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    for attempt in range(4):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as resp:
                return json.load(resp), dict(resp.headers)
        except urllib.error.HTTPError as exc:
            if exc.code in (403, 429) and attempt < 3:
                wait = 30 * (attempt + 1)
                log(f"被限流 ({exc.code})，{wait}s 后重试: {url}")
                time.sleep(wait)
                continue
            raise
        except (urllib.error.URLError, TimeoutError) as exc:
            if attempt < 3:
                log(f"网络异常 {exc}，5s 后重试: {url}")
                time.sleep(5)
                continue
            raise
    raise RuntimeError(f"请求失败: {url}")


def gh_api_raw(path: str) -> object:
    """取单个 JSON 对象（不加 --jq）。"""
    proc = subprocess.run(
        [GH, "api", path], capture_output=True, text=True, encoding="utf-8", timeout=120
    )
    if proc.returncode != 0:
        raise RuntimeError(f"gh api 失败: {path}\n{proc.stderr[:500]}")
    return json.loads(proc.stdout or "null")


def paged(url: str, accept: str = "application/vnd.github+json", max_pages: int = 60) -> list:
    out: list = []
    page = 1
    sep = "&" if "?" in url else "?"
    while page <= max_pages:
        data, headers = request(f"{url}{sep}per_page=100&page={page}", accept)
        if not isinstance(data, list):
            raise RuntimeError(f"预期列表响应: {url} -> {type(data)}")
        out.extend(data)
        link = headers.get("Link", "")
        if 'rel="next"' not in link:
            break
        page += 1
        time.sleep(0.4)
    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default="SECTL/SecRandom")
    args = parser.parse_args()

    DATA_DIR.mkdir(parents=True, exist_ok=True)
    now = datetime.now(timezone.utc)
    stamp = now.strftime("%Y-%m-%dT%H:%M:%SZ")

    log(f"仓库 {args.repo}")
    use_gh = gh_available()
    log(f"gh CLI 可用: {use_gh}")

    repo, _ = request(f"{API}/repos/{args.repo}")
    log(f"stars={repo.get('stargazers_count')} forks={repo.get('forks_count')}")

    if use_gh:
        releases = gh_api_json(f"repos/{args.repo}/releases")
        tags = gh_api_json(f"repos/{args.repo}/tags")
        stars = gh_api_json(
            f"repos/{args.repo}/stargazers", accept="application/vnd.github.star+json"
        )
        contributors = gh_api_json(f"repos/{args.repo}/contributors?anon=1")
        try:
            views = gh_api_raw(f"repos/{args.repo}/traffic/views")
        except Exception as exc:  # 需要 push 权限，失败不影响主流程
            log(f"traffic/views 不可用: {exc}")
            views = None
    else:
        releases = paged(f"{API}/repos/{args.repo}/releases")
        tags = paged(f"{API}/repos/{args.repo}/tags")
        stars = paged(f"{API}/repos/{args.repo}/stargazers", accept="application/vnd.github.star+json")
        contributors = paged(f"{API}/repos/{args.repo}/contributors?anon=1")
        views = None

    log(f"releases={len(releases)} tags={len(tags)} stargazers={len(stars)} contributors={len(contributors)}")

    meta = {
        "fetched_at": stamp,
        "repo": args.repo,
        "html_url": repo.get("html_url"),
        "description": repo.get("description"),
        "created_at": repo.get("created_at"),
        "pushed_at": repo.get("pushed_at"),
        "license": (repo.get("license") or {}).get("spdx_id"),
        "stars": repo.get("stargazers_count"),
        "forks": repo.get("forks_count"),
        "watchers": repo.get("subscribers_count"),
        "open_issues": repo.get("open_issues_count"),
        "default_branch": repo.get("default_branch"),
    }

    def dump(name: str, payload: object) -> None:
        path = DATA_DIR / name
        path.write_text(json.dumps(payload, ensure_ascii=False, indent=1), encoding="utf-8")
        log(f"写入 {path.relative_to(ROOT)} ({path.stat().st_size / 1024:.1f} KiB)")

    dump("repo.json", meta)
    dump("releases.json", {"fetched_at": stamp, "items": releases})
    dump("tags.json", {"fetched_at": stamp, "items": tags})
    dump("stargazers.json", {"fetched_at": stamp, "items": stars})
    dump("contributors.json", {"fetched_at": stamp, "items": contributors})
    if views:
        dump("traffic-views.json", {"fetched_at": stamp, "items": views})
        log(f"traffic/views 近14天: {views.get('count')} 次访问 / {views.get('uniques')} 独立访客")

    snapshot = {
        "snapshot_at": stamp,
        "stars": repo.get("stargazers_count"),
        "forks": repo.get("forks_count"),
        "releases": [
            {
                "tag": r.get("tag_name"),
                "published_at": r.get("published_at"),
                "downloads": sum((a.get("download_count") or 0) for a in r.get("assets") or []),
                "assets": {
                    (a.get("name") or ""): (a.get("download_count") or 0)
                    for a in r.get("assets") or []
                },
            }
            for r in releases
        ],
    }
    snap_path = DATA_DIR / "downloads-snapshots.jsonl"
    with snap_path.open("a", encoding="utf-8") as fh:
        fh.write(json.dumps(snapshot, ensure_ascii=False) + "\n")
    log(f"追加下载量快照 -> {snap_path.relative_to(ROOT)}")

    total = sum(item["downloads"] for item in snapshot["releases"])
    log(f"资产下载量合计(截至目前) = {total}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
