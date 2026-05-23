"""
Nuitka 打包脚本
用于构建 SecRandom 的独立可执行文件
"""

import os
import subprocess
import sys
import re
from pathlib import Path

if sys.platform == "win32":
    import io

    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

from packaging_utils import (
    ICON_FILE,
    PROJECT_ROOT,
    collect_data_includes,
)

sys.path.insert(0, str(Path(__file__).parent))
from app.tools.variable import APPLY_NAME, VERSION, APP_DESCRIPTION, AUTHOR, WEBSITE

from packaging_utils_deb import DebBuilder


def _print_packaging_summary() -> None:
    data_includes = collect_data_includes()
    print("\n数据文件 ({} entries):".format(len(data_includes)))
    for item in data_includes:
        kind = "dir" if item.is_dir else "file"
        print(f"  {kind}  {item.source} -> {item.target}")
    print("\n动态导入包: --include-package=app (递归包含所有子包)")


def _gather_data_flags() -> list[str]:
    flags: list[str] = []
    for include in collect_data_includes():
        flag = "--include-data-dir" if include.is_dir else "--include-data-file"
        source = include.source
        target = include.target
        if not include.is_dir and target == ".":
            target = Path(source).name
        flags.append(f"{flag}={source}={target}")
    return flags


def _sanitize_version(ver_str: str) -> str:
    if not ver_str:
        return "0.0.0.0"
    ver_str = ver_str.lstrip("vV").strip()
    match = re.match(r"^(\d+(\.\d+)*)", ver_str)
    if match:
        clean_ver = match.group(1)
        if "." not in clean_ver:
            clean_ver += ".0"
        return clean_ver
    return "0.0.0.0"


def get_nuitka_command() -> list[str]:
    raw_version = VERSION if VERSION else "0.0.0"
    clean_version = _sanitize_version(raw_version)
    print(f"\n版本号: '{raw_version}' -> '{clean_version}'")

    cmd = [
        sys.executable,
        "-m",
        "nuitka",
        "--standalone",
        "--enable-plugin=pyside6",
        "--assume-yes-for-downloads",
        "--output-dir=dist",
        "--product-name=SecRandom",
        "--file-description=公平随机抽取系统",
        f"--product-version={clean_version}",
        f"--file-version={clean_version}",
        "--copyright=Copyright (c) 2025",
        "--no-deployment-flag=self-execution",
    ]

    if sys.platform == "win32":
        if sys.version_info >= (3, 13):
            print("\n[注意] Python 3.13+ 不支持 MinGW64，将使用 MSVC。")
            print("       请确保已安装 Visual Studio C++ 生成工具。")
            cmd.append("--msvc=latest")
        else:
            cmd.append("--mingw64")
    else:
        cmd.append("--linux-onefile-icon")

    cmd.extend(_gather_data_flags())

    # 递归包含 app/ 下所有子包（覆盖语言模块、设置页面等动态导入）
    cmd.append("--include-package=app")

    if sys.platform == "win32" and ICON_FILE.exists():
        cmd.append(f"--windows-icon-from-ico={ICON_FILE}")
    elif sys.platform == "linux" and ICON_FILE.exists():
        cmd.append(f"--linux-icon={ICON_FILE}")

    cmd.append("main.py")
    return cmd


def check_compiler_env() -> bool:
    if sys.platform != "win32":
        return True

    if sys.version_info >= (3, 13):
        return True

    print("\n检查 MinGW64 环境...")
    try:
        result = subprocess.run(
            ["gcc", "--version"],
            capture_output=True,
            text=True,
            check=False,
            encoding="utf-8",
            errors="replace",
        )
        if result.returncode == 0:
            line = result.stdout.splitlines()[0] if result.stdout else "Unknown"
            print(f"找到 GCC: {line}")
            return True
    except FileNotFoundError:
        pass

    common_paths = [
        r"C:\msys64\mingw64\bin",
        r"C:\mingw64\bin",
        r"C:\Program Files\mingw64\bin",
    ]
    for p in common_paths:
        if (Path(p) / "gcc.exe").exists():
            print(f"找到 MinGW64: {p}")
            return True

    print("未找到 MinGW64，Nuitka 将自动下载。")
    return input("是否继续? (y/n): ").lower() == "y"


def build_deb() -> None:
    if sys.platform != "linux":
        return

    print("\n" + "=" * 60)
    print("开始构建 deb 包...")
    print("=" * 60)

    try:
        DebBuilder.build_from_nuitka(
            PROJECT_ROOT, APPLY_NAME, VERSION, APP_DESCRIPTION, AUTHOR, WEBSITE
        )
        print("=" * 60)
    except Exception as e:
        print(f"构建 deb 包失败: {e}")
        sys.exit(1)


def main():
    print("=" * 60)
    print("Nuitka 打包 SecRandom")
    print("=" * 60)

    if not os.environ.get("CI") and sys.platform == "win32" and not check_compiler_env():
        sys.exit(1)

    _print_packaging_summary()
    cmd = get_nuitka_command()

    print("\n执行命令:")
    print(" ".join(cmd))
    print("\n" + "=" * 60)

    try:
        subprocess.run(
            cmd,
            check=True,
            cwd=PROJECT_ROOT,
            capture_output=False,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        print("\n" + "=" * 60)
        print("Nuitka 打包成功！")
        print("=" * 60)

        build_deb()

    except subprocess.CalledProcessError as e:
        print(f"\n打包失败 (返回码 {e.returncode})")
        sys.exit(1)
    except KeyboardInterrupt:
        print("\n用户取消")
        sys.exit(1)
    except Exception as e:
        print(f"\n错误: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
