"""
PyInstaller 打包脚本
用于构建 SecRandom 的独立可执行文件
"""

import subprocess
import sys
from pathlib import Path

from packaging_utils import (
    ADDITIONAL_HIDDEN_IMPORTS,
    collect_data_includes,
    collect_language_modules,
    collect_view_modules,
    normalize_hidden_imports,
)

# 获取项目根目录
PROJECT_ROOT = Path(__file__).parent
SPEC_FILE = PROJECT_ROOT / "Secrandom.spec"


def _print_packaging_summary() -> None:
    """Log a quick overview of the resources and modules that will be bundled."""

    data_includes = collect_data_includes()
    hidden_imports = normalize_hidden_imports(
        collect_language_modules() + collect_view_modules() + ADDITIONAL_HIDDEN_IMPORTS
    )

    print("\nSelected data includes ({} entries):".format(len(data_includes)))
    for item in data_includes:
        kind = "dir " if item.is_dir else "file"
        print(f"  - {kind} {item.source} -> {item.target}")

    print("\nHidden imports ({} modules):".format(len(hidden_imports)))
    for name in hidden_imports:
        print(f"  - {name}")


def main():
    """执行 PyInstaller 打包"""
    print("=" * 60)
    print("开始使用 PyInstaller 打包 SecRandom")
    print("=" * 60)

    if not SPEC_FILE.exists():
        print("\nSecrandom.spec 不存在，请先生成或恢复该文件。")
        sys.exit(1)

    _print_packaging_summary()

    cmd = [
        sys.executable,
        "-m",
        "PyInstaller",
        "Secrandom.spec",
        "--clean",
        "--noconfirm",
    ]

    # 打印命令
    print("\n执行命令:")
    print(" ".join(cmd))
    print("\n" + "=" * 60)

    # 执行打包
    try:
        subprocess.run(cmd, check=True, cwd=PROJECT_ROOT)
        print("\n" + "=" * 60)
        print("打包成功！")
        print("可执行文件位于: dist/SecRandom.exe")
        print("=" * 60)
    except subprocess.CalledProcessError as e:
        print("\n" + "=" * 60)
        print(f"打包失败: {e}")
        print("=" * 60)
        sys.exit(1)


if __name__ == "__main__":
    main()
