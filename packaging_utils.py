"""Shared helpers for packaging scripts (PyInstaller and Nuitka)."""

from __future__ import annotations

import os
import pkgutil
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List

PROJECT_ROOT = Path(__file__).parent
APP_DIR = PROJECT_ROOT / "app"
RESOURCES_DIR = APP_DIR / "resources"
LANGUAGE_MODULES_DIR = APP_DIR / "Language" / "modules"
VIEW_DIR = APP_DIR / "view"
LICENSE_FILE = PROJECT_ROOT / "LICENSE"
VERSION_FILE = PROJECT_ROOT / "version_info.txt"
ICON_FILE = PROJECT_ROOT / "resources" / "secrandom-icon-paper.ico"


@dataclass(frozen=True)
class DataInclude:
    """Represents a file or directory that must be bundled with the app."""

    source: Path
    target: str
    is_dir: bool = False


BASE_HIDDEN_IMPORTS: List[str] = [
    "app.Language.obtain_language",
    "app.tools.language_manager",
    "app.tools.path_utils",
    "app.tools.variable",
    "app.tools.settings_access",
    "app.tools.settings_default",
    "app.tools.settings_default_storage",
    "app.tools.personalised",
    "app.tools.list",
    "app.tools.history",
    "app.tools.result_display",
    "app.tools.extract",
    "app.tools.config",
    "app.page_building.main_window_page",
    "app.page_building.settings_window_page",
]

ADDITIONAL_HIDDEN_IMPORTS: List[str] = [
    "qfluentwidgets",
    "loguru",
    "edge_tts",
    "aiohttp",
    "imageio",
    "numpy",
    "pandas",
    "PySide6",
    "app.view.another_window.contributor",
    "app.view.settings.settings",
    "app.view.tray.tray",
]


def collect_language_modules() -> List[str]:
    modules: List[str] = []
    if LANGUAGE_MODULES_DIR.exists():
        for file in LANGUAGE_MODULES_DIR.glob("*.py"):
            if file.name == "__init__.py":
                continue
            modules.append(f"app.Language.modules.{file.stem}")
    return modules


def collect_view_modules() -> List[str]:
    modules: List[str] = []
    if VIEW_DIR.exists():
        for _, name, ispkg in pkgutil.walk_packages(
            [str(VIEW_DIR)], prefix="app.view."
        ):
            if not ispkg:
                modules.append(name)
    return modules


def collect_data_includes() -> List[DataInclude]:
    includes: List[DataInclude] = []
    if RESOURCES_DIR.exists():
        includes.append(DataInclude(RESOURCES_DIR, "app/resources", is_dir=True))
    if LANGUAGE_MODULES_DIR.exists():
        includes.append(
            DataInclude(LANGUAGE_MODULES_DIR, "app/Language/modules", is_dir=True)
        )
    if LICENSE_FILE.exists():
        includes.append(DataInclude(LICENSE_FILE, ".", is_dir=False))
    return includes


def normalize_hidden_imports(extra: Iterable[str] = ()) -> List[str]:
    seen = set()
    ordered: List[str] = []
    for name in list(BASE_HIDDEN_IMPORTS) + list(extra):
        if name in seen:
            continue
        seen.add(name)
        ordered.append(name)
    return ordered


def format_add_data(include: DataInclude) -> str:
    sep = ";" if os.name == "nt" else ":"
    return f"{include.source}{sep}{include.target}"


__all__ = [
    "PROJECT_ROOT",
    "APP_DIR",
    "RESOURCES_DIR",
    "LANGUAGE_MODULES_DIR",
    "VIEW_DIR",
    "LICENSE_FILE",
    "VERSION_FILE",
    "ICON_FILE",
    "DataInclude",
    "BASE_HIDDEN_IMPORTS",
    "ADDITIONAL_HIDDEN_IMPORTS",
    "collect_language_modules",
    "collect_view_modules",
    "collect_data_includes",
    "normalize_hidden_imports",
    "format_add_data",
]
