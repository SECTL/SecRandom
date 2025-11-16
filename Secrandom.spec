"""PyInstaller spec leveraging shared packaging utilities."""

from PyInstaller.utils.hooks import collect_data_files

from packaging_utils import (
    ADDITIONAL_HIDDEN_IMPORTS,
    collect_data_includes,
    collect_language_modules,
    collect_view_modules,
    normalize_hidden_imports,
)

block_cipher = None

base_datas = [(str(item.source), item.target) for item in collect_data_includes()]

try:
    qfluentwidgets_datas = collect_data_files("qfluentwidgets")
except Exception as exc:
    print(f"Warning: unable to collect qfluentwidgets resources: {exc}")
    qfluentwidgets_datas = []

all_datas = base_datas + qfluentwidgets_datas

language_hiddenimports = collect_language_modules()
view_hiddenimports = collect_view_modules()

all_hiddenimports = normalize_hidden_imports(
    language_hiddenimports + view_hiddenimports + ADDITIONAL_HIDDEN_IMPORTS
)

a = Analysis(
    ["main.py"],
    pathex=[],
    binaries=[],
    datas=all_datas,
    hiddenimports=all_hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name="SecRandom",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
