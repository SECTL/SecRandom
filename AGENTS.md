# PROJECT KNOWLEDGE BASE - SecRandom

**Generated:** 2026-03-28
**Project:** SecRandom - 公平随机抽取系统 (Fair Random Selection System)
**Stack:** Python 3.13.5 + PySide6 + PySide6-Fluent-Widgets
**License:** GPLv3
**Language:** 中文回复
**Comments:** 中文注释

---

## OVERVIEW

Desktop GUI application for educational random selection with "fair" algorithms. Uses dynamic weighting to ensure all participants get equal chances over time. Built with PySide6 (Qt) using Microsoft's Fluent Design System.

---

## STRUCTURE

```
.
├── app/                    # Main application package
│   ├── common/            # Shared business logic (lottery, roll_call, history, etc.)
│   ├── core/              # Application core (window_manager, app_init, fonts)
│   ├── view/              # UI layer (main/, settings/, another_window/)
│   ├── tools/             # Utilities (config, paths, settings, themes)
│   ├── Language/          # i18n system (modules/, obtain_language.py)
│   └── page_building/     # Window/page construction utilities
├── config/                # Runtime configs (settings.json, secrets.json)
├── data/                  # Runtime data (dlls/, font/, history/, list/)
├── logs/                  # Application logs
├── resources/             # Static assets (icons, screenshots, docs)
├── scripts/               # Utility scripts (language import/export)
├── vendors/               # Vendored deps (pythonnet-stub-generator)
├── main.py                # Entry point
└── pyproject.toml         # Project config
```

---

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Add lottery feature | `app/common/lottery/` | lottery_manager.py, lottery_utils.py |
| Add roll call feature | `app/common/roll_call/` | roll_call_manager.py, roll_call_utils.py |
| Modify UI windows | `app/view/` | main/, settings/, another_window/ |
| Change core behavior | `app/core/` | window_manager.py, app_init.py |
| Add settings option | `app/tools/settings_*.py` | Accessors and defaults |
| Update translations | `app/Language/modules/` | Per-feature translation modules |
| Build/packaging | Root build_*.py | Nuitka + PyInstaller scripts |
| .NET interop | `app/common/IPC_URL/` | C# IPC handler for Windows features |

---

## CODE MAP

### Entry Points
- `main.py` - Application bootstrap (sentry, posthog, window manager)

### Core Modules
| Symbol | Type | File | Role |
|--------|------|------|------|
| WindowManager | Class | `app/core/window_manager.py` | Central window lifecycle |
| AppInitializer | Class | `app/core/app_init.py` | App startup logic |
| configure_dpi_scale | Function | `app/core/font_manager.py` | HiDPI handling |

### Common Domains
| Domain | Manager | Utils | History |
|--------|---------|-------|---------|
| Lottery | `lottery_manager.py` | `lottery_utils.py` | `lottery_history.py` |
| Roll Call | `roll_call_manager.py` | `roll_call_utils.py` | `roll_call_history.py` |
| Fair Draw | `fair_draw/` | - | `weight_utils.py` |

---

## CONVENTIONS

### Code Style
- **Ruff** for linting (configured in pyproject.toml)
- **Pre-commit** hooks enabled (.pre-commit-config.yaml)
- Ignored rules: F405, E722, E501, B012, F403, C901, B007, F841, C416, C414, E402

### Imports
- Absolute imports preferred: `from app.tools.config import ...`
- Star imports used (F403 ignored): `from app.tools.variable import *`

### Architecture
- **MVP-like pattern:** view/ (UI) → common/ (logic) → tools/ (infra)
- Managers handle domain logic (lottery_manager.py, etc.)
- Utils for pure functions (lottery_utils.py, etc.)
- Settings split: `_access.py` (read), `_default.py` (defaults), `_default_storage.py` (schema)

### Naming
- Files: snake_case.py
- Classes: PascalCase
- Functions/vars: snake_case
- UI files: descriptive (roll_call.py, lottery.py)

---

## ANTI-PATTERNS (THIS PROJECT)

### DO NOT
- **Use PyInstaller directly** - Use Nuitka for production builds (configured in build_nuitka.py)
- **Commit uv.lock changes unnecessarily** - Lock file is tracked but regenerate only when deps change
- **Modify vendors/ without documenting** - Vendored code must keep original LICENSE
- **Use Python 3.13.6+** - Strictly pinned to 3.13.5 in pyproject.toml

### NEVER
- **Import from tests/** - No test directory exists (tests are minimal)
- **Use relative imports** - Always use absolute `from app.xxx import ...`
- **Commit .venv/** - Already gitignored but worth reinforcing

### ALWAYS
- **Add translations** - Update `app/Language/modules/` when adding UI text
- **Use settings accessors** - Read via `app.tools.settings_access`, defaults in `settings_default`
- **Handle platform differences** - Windows (pywin32) vs Linux (pulsectl) deps marked in pyproject.toml

---

## UNIQUE STYLES

### .NET Interop
Heavy use of pythonnet for Windows features:
- `app/common/IPC_URL/csharp_ipc_handler.py` - C# IPC communication
- `data/dlls/` - .NET DLLs (protobuf, Newtonsoft.Json, etc.)
- `vendors/pythonnet-stub-generator/` - Modified stub generator

### Weight Algorithm
Fair selection uses dynamic weighting:
- `app/common/fair_draw/avg_gap_protection.py` - Gap protection logic
- `app/common/history/weight_utils.py` - Weight calculation
- Uses "average difference protection" to ensure fairness over time

### UI System
- **Fluent Design:** PySide6-Fluent-Widgets for modern Windows look
- **Theme support:** `app/tools/theme_loader.py`, `app/view/settings/theme_management/`
- **Floating window:** `app/view/floating_window/levitation.py`

### i18n Pattern
Translation system uses module-per-feature:
- `app/Language/modules/guide.py` - Guide translations
- `app/Language/modules/basic_settings.py` - Settings translations
- Loaded via `app/Language/obtain_language.py`

---

## COMMANDS

```bash
# Development (use uv)
uv sync                          # Install deps
uv run python main.py           # Run app

# Linting
ruff check .                     # Check
ruff check --fix .               # Auto-fix

# Build (choose one)
python build_nuitka.py          # Production build (Nuitka)
python build_pyinstaller.py     # Debug build (PyInstaller)

# Scripts
python scripts/import_crowdin_language.py  # Import translations
python scripts/export_zh_cn_language.py    # Export source strings
```

---

## NOTES

### Platform Differences
- **Windows:** Full features (UI access, USB binding, WMI, pycaw audio)
- **Linux:** Limited features (no pywin32, pulsectl for audio)
- **Both:** Core random selection works identically

### Configuration Files
- `config/settings.json` - User preferences
- `config/secrets.json` - Encrypted secrets
- `config/behind_scenes.json` - Hidden settings

### Data Directories
- `data/list/` - Student/prize lists (Excel files)
- `data/history/` - Draw history (JSON)
- `data/font/` - Custom fonts
- `data/dlls/` - .NET assemblies (Windows only)

### Gotchas
- Python version is **EXACTLY** 3.13.5 - don't upgrade
- Entry point is `main.py` at root, not a console script
- Lock file `uv.lock` is tracked (unusual but intentional)
- .NET DLLs required for Windows features (camera, USB binding)
- Sentry + PostHog telemetry initialized in main.py

### Sentry 日志级别与上报行为

Sentry 通过 `LoguruIntegration` 接入，配置在 `main.py:62-66`：
- `event_level=LoggingLevels.ERROR.value` — 只有 ERROR 级别以上才触发 Sentry 事件
- `before_send` 过滤器在 `app/tools/config.py:81-137`，会丢弃没有堆栈信息的 ERROR 事件

**关键规则：选择正确的日志级别来控制是否上报 Sentry**

| 日志方法 | 级别 | 有堆栈? | Sentry 行为 | 适用场景 |
|----------|------|---------|-------------|----------|
| `logger.exception()` | ERROR | ✅ 有 | **上报** | 真正的 bug，需要修复 |
| `logger.error()` | ERROR | ❌ 无 | **不上报**（被 before_send 过滤） | 预期错误，不需要上报但需要记录 |
| `logger.warning()` | WARNING | ❌ 无 | **不上报**（低于 event_level） | 预期的降级状态 |

**实际应用：**
- 网络超时、连接失败等**预期故障** → 用 `logger.warning()` 或 `logger.error()`
- 代码逻辑错误、未处理异常等**真正 bug** → 用 `logger.exception()`
- `before_send` 中还可以按异常类型过滤（如网络异常类型），作为额外防御层

### Vendored Dependencies
- `vendors/pythonnet-stub-generator/` - MIT licensed, modified for .NET 9.0
- Keep original LICENSE.md when updating

---

## SUBDIRECTORY GUIDES

| Directory | Guide |
|-----------|-------|
| `app/common/` | See `app/common/AGENTS.md` |
| `app/view/` | See `app/view/AGENTS.md` |
| `app/core/` | See `app/core/AGENTS.md` |
