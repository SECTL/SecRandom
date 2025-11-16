"""Nuitka packaging helper for SecRandom using the shared packaging utilities."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

from packaging_utils import (
    ADDITIONAL_HIDDEN_IMPORTS,
    ICON_FILE,
    PROJECT_ROOT,
    VERSION_FILE,
    collect_data_includes,
    collect_language_modules,
    collect_view_modules,
    normalize_hidden_imports,
)


PACKAGE_INCLUDE_NAMES = {
    "app.Language.modules",
    "app.view",
    "app.tools",
    "app.page_building",
}


def _read_version() -> str:
    try:
        return VERSION_FILE.read_text(encoding="utf-8").strip()
    except FileNotFoundError:
        return "0.0.0.0"


def _print_packaging_summary() -> None:
    data_includes = collect_data_includes()
    hidden_names = normalize_hidden_imports(
        collect_language_modules() + collect_view_modules() + ADDITIONAL_HIDDEN_IMPORTS
    )

    package_names = sorted(
        {name for name in hidden_names if "." not in name} | PACKAGE_INCLUDE_NAMES
    )
    module_names = [name for name in hidden_names if "." in name]

    print("\nSelected data includes ({} entries):".format(len(data_includes)))
    for item in data_includes:
        kind = "dir " if item.is_dir else "file"
        print(f"  - {kind} {item.source} -> {item.target}")

    print("\nRequired packages ({} entries):".format(len(package_names)))
    for pkg in package_names:
        print(f"  - {pkg}")

    print("\nHidden modules ({} entries):".format(len(module_names)))
    for mod in module_names:
        print(f"  - {mod}")


def _gather_data_flags() -> list[str]:
    flags: list[str] = []
    for include in collect_data_includes():
        flag = "--include-data-dir" if include.is_dir else "--include-data-file"
        flags.append(f"{flag}={include.source}={include.target}")
    return flags


def _gather_module_and_package_flags() -> tuple[list[str], list[str]]:
    hidden_names = normalize_hidden_imports(
        collect_language_modules() + collect_view_modules() + ADDITIONAL_HIDDEN_IMPORTS
    )

    package_names = set(PACKAGE_INCLUDE_NAMES)
    module_names: list[str] = []

    for name in hidden_names:
        if "." not in name:
            package_names.add(name)
        else:
            module_names.append(name)

    package_flags = [f"--include-package={pkg}" for pkg in sorted(package_names)]
    module_flags = [f"--include-module={mod}" for mod in module_names]
    return module_flags, package_flags


def get_nuitka_command():
    """生成 Nuitka 打包命令"""

    version = _read_version()

    module_flags, package_flags = _gather_module_and_package_flags()

    cmd = [
        sys.executable,
        "-m",
        "nuitka",
        "--standalone",
        "--onefile",
        "--enable-plugin=pyside6",
        "--assume-yes-for-downloads",
        # 使用 MinGW64 编译器
        "--mingw64",
        # 输出目录
        "--output-dir=dist",
        # 应用程序信息
        "--product-name=SecRandom",
        "--file-description=公平随机抽取系统",
        f"--product-version={version}",
        "--copyright=Copyright (c) 2025",
        # **修复 QFluentWidgets 方法签名检测问题**
        # Nuitka 在 standalone 模式下会改变代码执行环境，
        # 导致 QFluentWidgets 的 overload.py 签名检测失败
        "--no-deployment-flag=self-execution",
    ]

    cmd.extend(_gather_data_flags())
    cmd.extend(package_flags)
    cmd.extend(module_flags)

    if ICON_FILE.exists():
        cmd.append(f"--windows-icon-from-ico={ICON_FILE}")

    # 主入口文件
    cmd.append("main.py")

    return cmd


def check_mingw64():
    """检查 MinGW64 是否可用"""
    print("\n检查 MinGW64 环境...")

    # 检查是否在 PATH 中
    gcc_path = None
    try:
        result = subprocess.run(
            ["gcc", "--version"], capture_output=True, text=True, check=False
        )
        if result.returncode == 0:
            gcc_path = "gcc (在 PATH 中)"
            print(f"✓ 找到 GCC: {gcc_path}")
            print(f"  版本信息: {result.stdout.splitlines()[0]}")
            return True
    except FileNotFoundError:
        pass

    # 检查常见的 MinGW64 安装位置
    common_paths = [
        r"C:\msys64\mingw64\bin",
        r"C:\mingw64\bin",
        r"C:\Program Files\mingw64\bin",
        r"C:\msys64\ucrt64\bin",
    ]

    for path in common_paths:
        gcc_exe = Path(path) / "gcc.exe"
        if gcc_exe.exists():
            print(f"✓ 找到 MinGW64: {path}")
            print(f"  提示: 请确保 {path} 在系统 PATH 环境变量中")
            return True

    print("⚠ 警告: 未找到 MinGW64")
    print("\n请按照以下步骤安装 MinGW64:")
    print("1. 下载 MSYS2: https://www.msys2.org/")
    print("2. 安装后运行: pacman -S mingw-w64-x86_64-gcc")
    print("3. 将 C:\\msys64\\mingw64\\bin 添加到系统 PATH")
    print("\n或者使用 Nuitka 自动下载 MinGW64 (首次运行会自动下载)")

    response = input("\n是否继续? Nuitka 可以自动下载 MinGW64 (y/n): ")
    return response.lower() == "y"


def main():
    """执行打包"""
    print("=" * 60)
    print("开始使用 Nuitka + MinGW64 打包 SecRandom")
    print("=" * 60)

    # 检查 MinGW64
    if not check_mingw64():
        print("\n取消打包")
        sys.exit(1)

    _print_packaging_summary()

    # 生成命令
    cmd = get_nuitka_command()

    # 打印命令
    print("\n执行命令:")
    print(" ".join(cmd))
    print("\n" + "=" * 60)

    # 执行打包
    try:
        subprocess.run(cmd, check=True, cwd=PROJECT_ROOT)
        print("\n" + "=" * 60)
        print("打包成功！")
        print("=" * 60)
    except subprocess.CalledProcessError as e:
        print("\n" + "=" * 60)
        print(f"打包失败: {e}")
        print("=" * 60)
        sys.exit(1)


if __name__ == "__main__":
    main()
