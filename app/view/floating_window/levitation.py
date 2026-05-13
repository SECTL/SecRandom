# 标准库导入
import ctypes
import json
import os
import time
from typing import Dict, Any

# 第三方库导入
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *
from loguru import logger

# 本地模块导入
from app.tools.personalised import load_custom_font, get_theme_icon, is_dark_theme
from app.tools.settings_access import (
    readme_settings_async,
    update_settings,
    get_settings_signals,
)
from app.tools.path_utils import *
from app.tools.variable import EXIT_CODE_RESTART, DEFAULT_ICON_CODEPOINT
from app.Language.obtain_language import (
    get_content_name_async,
    get_content_combo_name_async,
)
from app.common.extraction.extract import _is_non_class_time
from app.common.safety.verify_ops import require_and_run
from app.common.data.list import get_class_name_list, get_group_list, get_gender_list


class LevitationWindow(QWidget):
    """
    悬浮窗窗口类
    提供可拖拽、贴边隐藏、主题切换等功能的悬浮窗口
    """

    # ==================== 信号定义 ====================
    rollCallRequested = Signal()
    quickDrawRequested = Signal()
    lotteryRequested = Signal()
    faceDrawRequested = Signal()
    timerRequested = Signal()
    visibilityChanged = Signal(bool)
    positionChanged = Signal(int, int)

    # ==================== 类常量 ====================
    DEFAULT_OPACITY = 0.8
    DEFAULT_PLACEMENT = 0
    DEFAULT_DISPLAY_STYLE = 0
    DEFAULT_EDGE_THRESHOLD = 5
    DEFAULT_RETRACT_SECONDS = 5
    DEFAULT_LONG_PRESS_MS = 150  # 默认长按时间，稍微增加避免误触发
    DEFAULT_BUTTON_SIZE = QSize(50, 50)  # 按钮大小
    DEFAULT_ICON_SIZE = QSize(24, 24)  # 图标大小
    DEFAULT_SPACING = 6
    DEFAULT_MARGINS = 6  # 贴边隐藏时的最小间距
    DRAG_THRESHOLD = 12  # 拖拽触发阈值，增加阈值避免误识别按钮点击为拖动
    MIN_DRAG_TIME = 100  # 最小拖动识别时间（毫秒），避免极短时间内的移动被识别为拖动
    _fluent_icon_map = None
    _fluent_font_family = None

    def __init__(self, parent=None):
        """初始化悬浮窗窗口"""
        super().__init__(parent)
        self._startup_initial_show = True
        self._close_guard_enabled = True
        self._close_guard_last_log_ms = 0

        # ==================== 基础设置 ====================
        self._setup_window_properties()

        # ==================== 拖拽相关属性 ====================
        self._init_drag_properties()

        # ==================== 贴边隐藏属性 ====================
        self._init_edge_properties()

        # ==================== UI相关属性 ====================
        self._init_ui_properties()

        # ==================== 周期性置顶属性 ====================
        self._init_periodic_topmost_properties()

        # ==================== 初始化配置 ====================
        self._init_settings()

        # ==================== 构建UI ====================
        self._build_ui()
        self._apply_position()

        # ==================== 信号连接 ====================
        self._connect_signals()

        # ==================== 主题应用 ====================
        self._apply_theme_style()

        self._apply_window()
        self._install_drag_filters()

        # ==================== 启动周期性置顶 ====================
        self._start_periodic_topmost()
        if not bool(getattr(self, "_visible_on_start", False)):
            QTimer.singleShot(0, lambda: self._check_edge_proximity(immediate=True))

    # ==================== 初始化方法 ====================

    def _setup_window_properties(self):
        """设置窗口基础属性"""
        self.setAttribute(Qt.WA_TranslucentBackground)
        self._base_window_flags = (
            Qt.FramelessWindowHint | Qt.Tool | Qt.NoDropShadowWindowHint
        )
        self.setWindowFlags(self._base_window_flags | Qt.WindowStaysOnTopHint)

    def _init_drag_properties(self):
        """初始化拖拽相关属性"""
        self._shadow = None
        self._drag_timer = QTimer(self)
        self._drag_timer.setSingleShot(True)
        self._drag_timer.timeout.connect(self._begin_drag)
        self._dragging = False
        self._press_pos = QPoint()
        self._press_time = 0  # 鼠标按下时间戳
        self._click_intent = False  # 标记是否为点击意图
        # 拖拽属性将在 _init_settings() 中从配置读取

    def _init_edge_properties(self):
        """初始化贴边隐藏相关属性"""
        self._indicator = None
        self._retract_timer = QTimer(self)
        self._retract_timer.setSingleShot(True)
        self._retracted = False
        self._last_stuck = False
        self._edge_threshold = self.DEFAULT_EDGE_THRESHOLD
        self._stick_to_edge = True
        self._retract_seconds = self.DEFAULT_RETRACT_SECONDS
        self._long_press_ms = self.DEFAULT_LONG_PRESS_MS

    def _init_ui_properties(self):
        """初始化UI相关属性"""
        self._buttons_spec = []
        self._font_family = load_custom_font() or QFont().family()
        self._container = QWidget(self)
        self._layout = None
        self._btn_size = self.DEFAULT_BUTTON_SIZE
        self._icon_size = self.DEFAULT_ICON_SIZE
        self._font_size = 10
        self._storage_btn_size = QSize(30, 30)
        self._storage_icon_size = QSize(18, 18)
        self._storage_font_size = 10
        self._spacing = self.DEFAULT_SPACING
        self._margins = self.DEFAULT_MARGINS
        self._placement = self.DEFAULT_PLACEMENT
        self._display_style = self.DEFAULT_DISPLAY_STYLE
        self._quick_draw_disabled = False
        self._disable_quick_draw_timer = QTimer(self)
        self._disable_quick_draw_timer.setSingleShot(True)
        self._disable_quick_draw_timer.timeout.connect(self._enable_quick_draw)
        self._extend_quick_draw_component = False
        self._quick_draw_extend_panel = None
        self._quick_draw_extend_anchor = None
        self._quick_draw_extend_close_timer = QTimer(self)
        self._quick_draw_extend_close_timer.setSingleShot(True)
        self._quick_draw_extend_close_timer.timeout.connect(
            self._close_quick_draw_extend_panel
        )

    def _init_periodic_topmost_properties(self):
        """初始化周期性置顶相关属性"""
        self._periodic_topmost_timer = QTimer(self)
        self._periodic_topmost_timer.timeout.connect(self._periodic_topmost)
        self._periodic_topmost_interval = 100
        self._uia_topmost_timer = QTimer(self)
        self._uia_topmost_timer.timeout.connect(self._uia_keep_topmost)
        self._uia_topmost_interval = 250
        self._uiaccess_funcs = None
        self._uia_last_error_ms = 0
        self._uia_last_error_text = ""
        self._uiaccess_restart_requested = False
        self._uiaccess_restart_exceeded = False
        self._check_uiaccess_restart_count()

    def _get_uiaccess_funcs(self):
        if self._uiaccess_funcs is not None:
            return self._uiaccess_funcs
        try:
            from app.common.windows.uiaccess import (
                UIACCESS_RESTART_ENV,
                UIACCESS_RESTART_COUNT_ENV,
                is_uiaccess_process,
                set_window_band_uiaccess,
            )

            self._uiaccess_funcs = (
                UIACCESS_RESTART_ENV,
                UIACCESS_RESTART_COUNT_ENV,
                is_uiaccess_process,
                set_window_band_uiaccess,
            )
        except Exception:
            self._uiaccess_funcs = (None, None, None, None)
        return self._uiaccess_funcs

    def _check_uiaccess_restart_count(self):
        """检查 UIAccess 重启次数，如果超过上限则标记为已超出"""
        MAX_RESTART_COUNT = 2
        _, count_env_key, _, _ = self._get_uiaccess_funcs()
        if not count_env_key:
            return
        try:
            current_count = int(os.environ.get(str(count_env_key), "0") or "0")
            if current_count >= MAX_RESTART_COUNT:
                self._uiaccess_restart_exceeded = True
                logger.warning(
                    "检测到 UIAccess 重启次数已达上限 ({} 次)，将使用普通置顶模式",
                    current_count,
                )
        except Exception:
            pass

    def _request_uiaccess_restart(self):
        if getattr(self, "_uiaccess_restart_requested", False):
            return

        env_key, count_env_key, _, _ = self._get_uiaccess_funcs()
        if not env_key or not count_env_key:
            return

        MAX_RESTART_COUNT = 2

        try:
            current_count = int(os.environ.get(str(count_env_key), "0") or "0")
        except Exception:
            current_count = 0

        if current_count >= MAX_RESTART_COUNT:
            logger.warning(
                "UIAccess 重启次数已达上限 ({} 次)，回退到普通置顶模式",
                MAX_RESTART_COUNT,
            )
            self._uiaccess_restart_exceeded = True
            return

        self._uiaccess_restart_requested = True
        new_count = current_count + 1
        try:
            os.environ[str(count_env_key)] = str(new_count)
        except Exception:
            pass

        logger.info(
            "请求 UIAccess 重启 (第 {} 次，上限 {} 次)",
            new_count,
            MAX_RESTART_COUNT,
        )

        try:
            os.environ[str(env_key)] = "1"
        except Exception:
            pass
        try:
            if not bool(self._is_admin()):
                from app.common.windows.uiaccess import ELEVATE_RESTART_ENV

                os.environ[str(ELEVATE_RESTART_ENV)] = "1"
        except Exception:
            pass

        app = QApplication.instance()
        if app is not None:
            app.exit(EXIT_CODE_RESTART)
            return
        os._exit(EXIT_CODE_RESTART)

    def _start_periodic_topmost(self):
        """启动周期性置顶定时器"""
        self._apply_topmost_runtime()

    def _periodic_topmost(self):
        """周期性将窗口置顶"""
        if self.isVisible() and getattr(self, "_topmost_mode", 1) != 0:
            self.raise_()

    def _is_admin(self) -> bool:
        try:
            return bool(ctypes.windll.shell32.IsUserAnAdmin())
        except Exception:
            return False

    def _refresh_window_flags(self):
        no_focus = bool(getattr(self, "_do_not_steal_focus", False))
        try:
            self.setAttribute(Qt.WA_ShowWithoutActivating, no_focus)
        except Exception:
            pass
        topmost = getattr(self, "_topmost_mode", 1) != 0
        self.setWindowFlag(Qt.WindowStaysOnTopHint, topmost)
        self.setWindowFlag(Qt.WindowDoesNotAcceptFocus, no_focus)
        if self.isVisible():
            prev_suppress = bool(getattr(self, "_suppress_visibility_tracking", False))
            self._suppress_visibility_tracking = True
            try:
                self.hide()
                self.show()
            finally:
                self._suppress_visibility_tracking = prev_suppress

    def _apply_topmost_runtime(self):
        mode = int(getattr(self, "_topmost_mode", 1) or 0)
        try:
            state = (mode, bool(self.isVisible()), bool(self._is_admin()))
            if getattr(self, "_last_topmost_runtime_state", None) != state:
                self._last_topmost_runtime_state = state
                logger.debug(
                    "置顶运行态: mode={}, visible={}, admin={}",
                    state[0],
                    state[1],
                    state[2],
                )
        except Exception:
            pass
        if mode == 0:
            if self._periodic_topmost_timer.isActive():
                self._periodic_topmost_timer.stop()
            if self._uia_topmost_timer.isActive():
                self._uia_topmost_timer.stop()
        else:
            if not self._periodic_topmost_timer.isActive():
                self._periodic_topmost_timer.start(self._periodic_topmost_interval)
            if mode == 2 and self.isVisible():
                _, _, is_uiaccess, _ = self._get_uiaccess_funcs()
                if is_uiaccess is not None:
                    try:
                        if not bool(is_uiaccess()):
                            if getattr(self, "_uiaccess_restart_exceeded", False):
                                logger.debug(
                                    "UIAccess 重启次数已达上限，使用普通置顶模式"
                                )
                                if self._uia_topmost_timer.isActive():
                                    self._uia_topmost_timer.stop()
                            else:
                                logger.debug(
                                    "需要UIAccess置顶，准备重启切换为UIAccess进程"
                                )
                                self._request_uiaccess_restart()
                            return
                    except Exception:
                        pass
                if not self._uia_topmost_timer.isActive():
                    self._uia_topmost_timer.start(self._uia_topmost_interval)
            else:
                if self._uia_topmost_timer.isActive():
                    self._uia_topmost_timer.stop()

        if hasattr(self, "arrow_widget") and self.arrow_widget:
            try:
                arrow_flags = (
                    Qt.FramelessWindowHint | Qt.Tool | Qt.NoDropShadowWindowHint
                )
                if mode != 0:
                    arrow_flags |= Qt.WindowStaysOnTopHint
                if self._do_not_steal_focus:
                    arrow_flags |= Qt.WindowDoesNotAcceptFocus
                try:
                    self.arrow_widget.setAttribute(
                        Qt.WA_ShowWithoutActivating, bool(self._do_not_steal_focus)
                    )
                except Exception:
                    pass
                self.arrow_widget.setWindowFlags(arrow_flags)
                if hasattr(self.arrow_widget, "set_keep_on_top_enabled"):
                    self.arrow_widget.set_keep_on_top_enabled(mode != 0)
                if self.arrow_widget.isVisible():
                    self.arrow_widget.hide()
                    self.arrow_widget.show()
            except Exception as e:
                logger.exception("更新箭头按钮置顶状态失败（已忽略）: {}", e)

    def _uia_keep_topmost(self):
        if not self.isVisible() or int(getattr(self, "_topmost_mode", 1) or 0) != 2:
            return

        _, _, _, set_band = self._get_uiaccess_funcs()
        if set_band is None:
            return

        hwnds = [int(self.winId())]
        if (
            hasattr(self, "arrow_widget")
            and self.arrow_widget
            and self.arrow_widget.isVisible()
        ):
            try:
                hwnds.append(int(self.arrow_widget.winId()))
            except Exception:
                pass

        for hwnd in hwnds:
            try:
                set_band(hwnd)
            except Exception as e:
                now_ms = int(QDateTime.currentMSecsSinceEpoch())
                text = str(e)
                if now_ms - int(
                    getattr(self, "_uia_last_error_ms", 0) or 0
                ) > 5000 or text != (getattr(self, "_uia_last_error_text", "") or ""):
                    self._uia_last_error_ms = now_ms
                    self._uia_last_error_text = text
                    logger.exception("UIA置顶执行失败（已忽略）: {}", e)

    def _connect_signals(self):
        """连接信号"""
        get_settings_signals().settingChanged.connect(self._on_setting_changed)
        # 连接主题变更信号
        try:
            qconfig.themeChanged.connect(self._on_theme_changed)
        except Exception as e:
            logger.exception("连接 themeChanged 信号时出错（已忽略）: {}", e)

    def rebuild_ui(self):
        """
        重新构建浮窗UI
        删除当前布局并创建新的布局
        """
        panel = getattr(self, "_quick_draw_extend_panel", None)
        if panel is not None:
            try:
                panel.close()
            except Exception:
                pass
        self._quick_draw_extend_anchor = None

        # 清除现有按钮
        self._clear_buttons()

        # 重新创建容器布局
        container_layout = self._create_container_layout()

        # 设置新的布局
        old_layout = self._container.layout()
        if old_layout:
            QWidget().setLayout(old_layout)  # 从容器中移除旧布局

        self._container.setLayout(container_layout)
        try:
            self._container.layout().setAlignment(Qt.AlignCenter)
        except Exception:
            pass

        # 重新添加按钮
        for i, spec in enumerate(self._buttons_spec):
            if spec == "quick_draw" and bool(
                getattr(self, "_extend_quick_draw_component", False)
            ):
                btn = self._create_extended_quick_draw_widget()
            else:
                btn = self._create_button(spec)
            self._add_button(btn, i, len(self._buttons_spec))

        self._container.adjustSize()
        self.adjustSize()
        self._install_drag_filters()

    def _clear_buttons(self):
        """清除所有按钮"""
        # 清除顶层和底层的按钮
        if hasattr(self, "_top") and self._top and self._top.layout():
            top_layout = self._top.layout()
            while top_layout.count():
                item = top_layout.takeAt(0)
                widget = item.widget()
                if widget:
                    widget.deleteLater()

        if hasattr(self, "_bottom") and self._bottom and self._bottom.layout():
            bottom_layout = self._bottom.layout()
            while bottom_layout.count():
                item = bottom_layout.takeAt(0)
                widget = item.widget()
                if widget:
                    widget.deleteLater()

        # 清除容器直接包含的按钮
        container_layout = self._container.layout()
        if container_layout:
            while container_layout.count():
                item = container_layout.takeAt(0)
                widget = item.widget()
                if widget:
                    widget.deleteLater()

    def _font(self, size):
        s = int(size) if size and int(size) > 0 else 8
        if s <= 0:
            s = 8
        f = QFont(self._font_family) if self._font_family else QFont()
        if s > 0:  # 确保字体大小大于0
            f.setPointSize(s)
        return f

    def _apply_theme_style(self):
        dark = self._is_floating_window_dark()
        self._container.setAttribute(Qt.WA_StyledBackground, True)
        if dark:
            self._container.setStyleSheet(
                "background-color: rgba(32,32,32,180); border-radius: 12px; border: 1px solid rgba(255,255,255,20);"
            )
        else:
            self._container.setStyleSheet(
                "background-color: rgba(255,255,255,220); border-radius: 12px; border: 1px solid rgba(0,0,0,12);"
            )
        try:
            if hasattr(self, "arrow_button") and self.arrow_button:
                if dark:
                    self.arrow_button.setStyleSheet(
                        f"background-color: rgba(32,32,32,{self._opacity}); color: rgba(255,255,255,200); border-radius: 6px; border: 1px solid rgba(255,255,255,20);"
                    )
                else:
                    self.arrow_button.setStyleSheet(
                        f"background-color: rgba(255,255,255,{self._opacity}); color: rgba(0,0,0,180); border-radius: 6px; border: 1px solid rgba(0,0,0,12);"
                    )
                if int(getattr(self, "_stick_indicator_style", 0) or 0) == 0:
                    self.arrow_button.setIcon(
                        self._get_icon_for_floating_window(
                            "ic_fluent_people_20_filled", self._storage_icon_size
                        )
                    )
                    self.arrow_button.setIconSize(self._storage_icon_size)
        except Exception:
            pass

    def _is_floating_window_dark(self) -> bool:
        idx = int(getattr(self, "_floating_window_theme", 0) or 0)
        if idx == 0:
            return is_dark_theme(qconfig)
        if idx == 1:
            return False
        return True

    def _get_icon_for_floating_window(self, icon_name, size: QSize):
        dark = self._is_floating_window_dark()
        color = QColor("#ffffff") if dark else QColor("#000000")
        return self._render_fluent_icon(icon_name, size, color)

    def _ensure_fluent_icon_resources(self):
        if LevitationWindow._fluent_icon_map is None:
            try:
                map_path = get_data_path("assets", "FluentSystemIcons-Filled.json")
                with open(map_path, "r", encoding="utf-8") as f:
                    content = f.read()
                LevitationWindow._fluent_icon_map = (
                    json.loads(content) if content and content.strip() else {}
                )
            except Exception:
                LevitationWindow._fluent_icon_map = {}

        if LevitationWindow._fluent_font_family is None:
            try:
                font_path = get_data_path("assets", "FluentSystemIcons-Filled.ttf")
                font_id = QFontDatabase.addApplicationFont(str(font_path))
                if font_id >= 0:
                    families = QFontDatabase.applicationFontFamilies(font_id)
                    LevitationWindow._fluent_font_family = (
                        families[0] if families else ""
                    )
                else:
                    LevitationWindow._fluent_font_family = ""
            except Exception:
                LevitationWindow._fluent_font_family = ""

    def _resolve_fluent_char(self, icon_name) -> str:
        if isinstance(icon_name, str) and icon_name.startswith("\\u"):
            try:
                return chr(int(icon_name[2:], 16))
            except Exception:
                return chr(DEFAULT_ICON_CODEPOINT)

        if isinstance(icon_name, int):
            try:
                return chr(icon_name)
            except Exception:
                return chr(DEFAULT_ICON_CODEPOINT)

        if isinstance(icon_name, str):
            self._ensure_fluent_icon_resources()
            icon_map = LevitationWindow._fluent_icon_map or {}
            try:
                return chr(int(icon_map.get(icon_name, DEFAULT_ICON_CODEPOINT)))
            except Exception:
                return chr(DEFAULT_ICON_CODEPOINT)

        return chr(DEFAULT_ICON_CODEPOINT)

    def _render_fluent_icon(self, icon_name, size: QSize, color: QColor) -> QIcon:
        self._ensure_fluent_icon_resources()
        font_family = LevitationWindow._fluent_font_family or ""
        if not font_family:
            return self._tint_icon(get_theme_icon(icon_name), size, color)

        w = int(size.width()) if size else 24
        h = int(size.height()) if size else 24
        w = w if w > 0 else 24
        h = h if h > 0 else 24
        px = min(w, h)

        char = self._resolve_fluent_char(icon_name)

        pixmap = QPixmap(w, h)
        pixmap.fill(Qt.transparent)

        painter = QPainter(pixmap)
        painter.setRenderHint(QPainter.Antialiasing, True)
        painter.setRenderHint(QPainter.TextAntialiasing, True)
        painter.setPen(QPen(color))

        font = QFont(font_family)
        font.setPixelSize(px)
        painter.setFont(font)
        painter.drawText(QRect(0, 0, w, h), Qt.AlignCenter, char)
        painter.end()

        return QIcon(pixmap)

    def _tint_icon(self, icon: QIcon, size: QSize, color: QColor) -> QIcon:
        try:
            qicon = icon.icon() if hasattr(icon, "icon") else icon
        except Exception:
            qicon = icon

        w = int(size.width()) if size else 24
        h = int(size.height()) if size else 24
        w = w if w > 0 else 24
        h = h if h > 0 else 24

        try:
            src = qicon.pixmap(w, h)
        except Exception:
            src = QIcon().pixmap(w, h)

        tinted = QPixmap(src.size())
        tinted.fill(Qt.transparent)

        painter = QPainter(tinted)
        painter.setRenderHint(QPainter.Antialiasing, True)
        painter.setCompositionMode(QPainter.CompositionMode_Source)
        painter.drawPixmap(0, 0, src)
        painter.setCompositionMode(QPainter.CompositionMode_SourceIn)
        painter.fillRect(tinted.rect(), color)
        painter.end()

        return QIcon(tinted)

    def _icon_pixmap(self, icon):
        if hasattr(icon, "icon"):
            qicon = icon.icon()
        elif isinstance(icon, QIcon):
            qicon = icon
        else:
            qicon = QIcon()
        return qicon.pixmap(self._icon_size)

    def _freeze_icon(self, icon: QIcon, size: QSize) -> QIcon:
        try:
            qicon = icon.icon() if hasattr(icon, "icon") else icon
        except Exception:
            qicon = icon
        try:
            pixmap = qicon.pixmap(size)
        except Exception:
            pixmap = QIcon().pixmap(size)
        return QIcon(pixmap)

    def _text_color(self) -> str:
        dark = self._is_floating_window_dark()
        return "rgba(255,255,255,230)" if dark else "rgba(0,0,0,200)"

    def _init_settings(self):
        """初始化设置配置"""
        # 基础显示设置
        self._visible_on_start = self._get_bool_setting(
            "floating_window_management", "startup_display_floating_window", False
        )
        self._opacity = self._get_float_setting(
            "floating_window_management",
            "floating_window_opacity",
            self.DEFAULT_OPACITY,
        )
        self._floating_window_theme = self._get_int_setting(
            "floating_window_management", "floating_window_theme", 0
        )

        # 布局设置
        self._placement = self._get_int_setting(
            "floating_window_management",
            "floating_window_placement",
            self.DEFAULT_PLACEMENT,
        )
        self._display_style = self._get_int_setting(
            "floating_window_management",
            "floating_window_display_style",
            self.DEFAULT_DISPLAY_STYLE,
        )
        self._extend_quick_draw_component = self._get_bool_setting(
            "floating_window_management", "extend_quick_draw_component", False
        )

        # 拖拽设置
        self._draggable = self._get_bool_setting(
            "floating_window_management", "floating_window_draggable", True
        )
        self._long_press_ms = self._get_int_setting(
            "floating_window_management",
            "floating_window_long_press_duration",
            self.DEFAULT_LONG_PRESS_MS,
        )

        # 贴边设置
        self._stick_to_edge = self._get_bool_setting(
            "floating_window_management", "floating_window_stick_to_edge", True
        )
        self._retract_seconds = self._get_int_setting(
            "floating_window_management",
            "floating_window_stick_to_edge_recover_seconds",
            self.DEFAULT_RETRACT_SECONDS,
        )
        self._stick_indicator_style = self._get_int_setting(
            "floating_window_management",
            "floating_window_stick_to_edge_display_style",
            0,
        )

        # 按钮配置
        button_control_value = readme_settings_async(
            "floating_window_management", "floating_window_button_control"
        )
        self._buttons_spec = self._normalize_button_control_value(button_control_value)

        # 浮窗大小设置
        size_idx = self._get_int_setting(
            "floating_window_management", "floating_window_size", 1
        )
        self._apply_size_setting(size_idx)

        # 无焦点模式设置
        self._do_not_steal_focus = self._get_bool_setting(
            "floating_window_management", "do_not_steal_focus", False
        )
        self._topmost_mode = self._get_int_setting(
            "floating_window_management", "floating_window_topmost_mode", 1
        )
        self._refresh_window_flags()

        # 贴边隐藏功能配置
        self._init_edge_hide_settings()
        self._init_foreground_hide_settings()

        # 联动：下课隐藏浮窗设置
        self._init_class_linkage_settings()

        self._user_requested_visible = bool(self._visible_on_start)

    def _init_class_linkage_settings(self):
        """初始化联动设置：下课隐藏浮窗"""
        try:
            self._hide_on_class_end_enabled = bool(
                self._get_bool_setting(
                    "linkage_settings", "hide_floating_window_on_class_end", False
                )
            )
        except Exception:
            self._hide_on_class_end_enabled = False

        # 状态跟踪
        self._hidden_by_class_end = False
        self._pre_class_hide_main_visible = False
        self._pre_class_hide_storage_visible = False

        # 定时检查器（默认 30 秒）
        self._class_hide_timer = QTimer(self)
        self._class_hide_timer.setInterval(30 * 1000)
        self._class_hide_timer.timeout.connect(self._check_class_end_hide)
        self._apply_class_hide_timer_state()

    def _apply_class_hide_timer_state(self):
        try:
            if bool(getattr(self, "_hide_on_class_end_enabled", False)):
                if not self._class_hide_timer.isActive():
                    self._class_hide_timer.start()
                QTimer.singleShot(0, self._check_class_end_hide)
            else:
                if hasattr(self, "_class_hide_timer") and self._class_hide_timer:
                    if self._class_hide_timer.isActive():
                        self._class_hide_timer.stop()
                # 如果设置被关闭，确保恢复先前的可见性
                self._apply_class_hidden(False)
        except Exception:
            pass

    def _check_class_end_hide(self):
        """检查是否为下课/非上课时间并应用隐藏逻辑"""
        try:
            if not bool(getattr(self, "_hide_on_class_end_enabled", False)):
                return
            is_non_class = False
            try:
                is_non_class = bool(_is_non_class_time())
            except Exception:
                is_non_class = False
            self._apply_class_hidden(bool(is_non_class))
        except Exception:
            pass

    def _apply_class_hidden(self, hidden: bool):
        """根据下课检测结果隐藏或恢复浮窗（记录并恢复之前可见性）"""
        hidden = bool(hidden)
        if hidden == bool(getattr(self, "_hidden_by_class_end", False)):
            return

        self._hidden_by_class_end = hidden

        if hidden:
            # 记录之前可见性
            self._pre_class_hide_main_visible = bool(self.isVisible())
            self._pre_class_hide_storage_visible = bool(
                hasattr(self, "arrow_widget")
                and self.arrow_widget
                and self.arrow_widget.isVisible()
            )

            self._suppress_visibility_tracking = True
            try:
                if self.isVisible():
                    self.hide()
                if (
                    hasattr(self, "arrow_widget")
                    and self.arrow_widget
                    and self.arrow_widget.isVisible()
                ):
                    self.arrow_widget.hide()
            finally:
                self._suppress_visibility_tracking = False
            return

        # 恢复可见性：仅在用户曾经期望可见时恢复
        should_show_main = bool(
            getattr(self, "_user_requested_visible", False)
            and getattr(self, "_pre_class_hide_main_visible", False)
        )
        should_show_storage = bool(
            getattr(self, "_user_requested_visible", False)
            and getattr(self, "_pre_class_hide_storage_visible", False)
        )

        self._suppress_visibility_tracking = True
        try:
            if should_show_main and not self.isVisible():
                self.show()
            if (
                should_show_storage
                and hasattr(self, "arrow_widget")
                and self.arrow_widget
                and not self.arrow_widget.isVisible()
            ):
                self.arrow_widget.show()
        finally:
            self._suppress_visibility_tracking = False

    def _get_bool_setting(self, section: str, key: str, default: bool = False) -> bool:
        """获取布尔类型设置"""
        result = readme_settings_async(section, key)
        return bool(result) if result is not None else default

    def _get_int_setting(self, section: str, key: str, default: int = 0) -> int:
        """获取整数类型设置"""
        result = readme_settings_async(section, key)
        return int(result) if result is not None else default

    def _get_float_setting(self, section: str, key: str, default: float = 0.0) -> float:
        """获取浮点数类型设置"""
        result = readme_settings_async(section, key)
        return float(result) if result is not None else default

    def _init_edge_hide_settings(self):
        """初始化贴边隐藏功能设置"""
        self.floating_window_stick_to_edge = self._get_bool_setting(
            "floating_window_management", "floating_window_stick_to_edge", False
        )
        self.custom_retract_time = self._retract_seconds  # 复用现有的贴边回收秒数配置
        self.custom_display_mode = (
            self._stick_indicator_style
        )  # 复用现有的贴边显示样式配置
        self._retracted = False

        logger.debug(f"贴边隐藏功能配置: {self.floating_window_stick_to_edge}")

    def _init_foreground_hide_settings(self):
        self._hide_on_foreground_enabled = self._get_bool_setting(
            "floating_window_management", "hide_floating_window_on_foreground", False
        )
        self._hide_on_foreground_title_raw = str(
            readme_settings_async(
                "floating_window_management",
                "hide_floating_window_on_foreground_window_titles",
            )
            or ""
        )
        self._hide_on_foreground_process_raw = str(
            readme_settings_async(
                "floating_window_management",
                "hide_floating_window_on_foreground_process_names",
            )
            or ""
        )
        self._hide_on_foreground_titles = self._split_match_list(
            self._hide_on_foreground_title_raw
        )
        self._hide_on_foreground_processes = self._split_match_list(
            self._hide_on_foreground_process_raw
        )

        self._hidden_by_foreground_match = False
        self._pre_foreground_hide_main_visible = False
        self._pre_foreground_hide_storage_visible = False
        self._suppress_visibility_tracking = False

        self._foreground_hide_timer = QTimer(self)
        self._foreground_hide_timer.setInterval(250)
        self._foreground_hide_timer.timeout.connect(self._check_foreground_hide)
        self._apply_foreground_hide_timer_state()

    def _apply_foreground_hide_timer_state(self):
        try:
            if bool(getattr(self, "_hide_on_foreground_enabled", False)):
                if not self._foreground_hide_timer.isActive():
                    self._foreground_hide_timer.start()
                QTimer.singleShot(0, self._check_foreground_hide)
            else:
                if (
                    hasattr(self, "_foreground_hide_timer")
                    and self._foreground_hide_timer
                ):
                    if self._foreground_hide_timer.isActive():
                        self._foreground_hide_timer.stop()
                self._apply_foreground_hidden(False)
        except Exception:
            pass

    def _split_match_list(self, raw_text: str) -> list[str]:
        text = str(raw_text or "").strip()
        if not text:
            return []
        items = []
        for part in text.replace("\n", ";").split(";"):
            s = str(part or "").strip()
            if not s:
                continue
            items.append(s.lower())
        return items

    def _get_foreground_info(self) -> tuple[str, str, int]:
        try:
            user32 = ctypes.WinDLL("user32", use_last_error=True)
            hwnd = user32.GetForegroundWindow()
            if not hwnd:
                return "", "", 0

            length = int(user32.GetWindowTextLengthW(hwnd) or 0)
            buf = ctypes.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buf, length + 1)
            title = str(buf.value or "")

            pid = ctypes.c_ulong(0)
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            pid_int = int(pid.value or 0)

            process_name = ""
            try:
                if pid_int and pid_int != os.getpid():
                    import psutil

                    process_name = str(psutil.Process(pid_int).name() or "")
            except Exception:
                process_name = ""

            return title, process_name, pid_int
        except Exception:
            return "", "", 0

    def _check_foreground_hide(self):
        if not bool(getattr(self, "_hide_on_foreground_enabled", False)):
            return

        title, proc, pid = self._get_foreground_info()
        if pid == os.getpid():
            self._apply_foreground_hidden(False)
            return

        title_l = str(title or "").lower()
        proc_l = str(proc or "").lower()

        matched = False
        if title_l and self._hide_on_foreground_titles:
            matched = any(t and t in title_l for t in self._hide_on_foreground_titles)
        if not matched and proc_l and self._hide_on_foreground_processes:
            matched = any(p and p in proc_l for p in self._hide_on_foreground_processes)

        self._apply_foreground_hidden(bool(matched))

    def _apply_foreground_hidden(self, hidden: bool):
        hidden = bool(hidden)
        if hidden == bool(getattr(self, "_hidden_by_foreground_match", False)):
            return

        self._hidden_by_foreground_match = hidden

        if hidden:
            self._pre_foreground_hide_main_visible = bool(self.isVisible())
            self._pre_foreground_hide_storage_visible = bool(
                hasattr(self, "arrow_widget")
                and self.arrow_widget
                and self.arrow_widget.isVisible()
            )

            self._suppress_visibility_tracking = True
            try:
                if self.isVisible():
                    self.hide()
                if (
                    hasattr(self, "arrow_widget")
                    and self.arrow_widget
                    and self.arrow_widget.isVisible()
                ):
                    self.arrow_widget.hide()
            finally:
                self._suppress_visibility_tracking = False
            return

        should_show_main = bool(
            getattr(self, "_user_requested_visible", False)
            and getattr(self, "_pre_foreground_hide_main_visible", False)
        )
        should_show_storage = bool(
            getattr(self, "_user_requested_visible", False)
            and getattr(self, "_pre_foreground_hide_storage_visible", False)
        )

        self._suppress_visibility_tracking = True
        try:
            if should_show_main and not self.isVisible():
                self.show()
            if (
                should_show_storage
                and hasattr(self, "arrow_widget")
                and self.arrow_widget
                and not self.arrow_widget.isVisible()
            ):
                self.arrow_widget.show()
        finally:
            self._suppress_visibility_tracking = False

    def is_user_requested_visible(self) -> bool:
        return bool(getattr(self, "_user_requested_visible", False))

    def toggle_user_requested_visible(self) -> None:
        self.set_user_requested_visible(not self.is_user_requested_visible())

    def set_user_requested_visible(self, visible: bool) -> None:
        self._user_requested_visible = bool(visible)
        storage = getattr(self, "storage_window", None)
        if self._user_requested_visible:
            super().show()
            if bool(getattr(self, "_retracted", False)) and storage is not None:
                try:
                    storage.show()
                except Exception:
                    pass
        else:
            if storage is not None and bool(storage.isVisible()):
                try:
                    storage.hide()
                except Exception:
                    pass
            super().hide()

    def _apply_size_setting(self, size_idx: int):
        """应用浮窗大小设置

        Args:
            size_idx: 大小索引，0=超级小，1=超小，2=小，3=中，4=大，5=超大，6=超级大
        """
        if size_idx == 0:
            # 超级小
            self._btn_size = QSize(20, 20)
            self._icon_size = QSize(6, 6)
            self._font_size = 4
            self._storage_btn_size = QSize(20, 20)
            self._storage_icon_size = QSize(12, 12)
            self._storage_font_size = 6
        elif size_idx == 1:
            # 超小
            self._btn_size = QSize(30, 30)
            self._icon_size = QSize(12, 12)
            self._font_size = 6
            self._storage_btn_size = QSize(25, 25)
            self._storage_icon_size = QSize(15, 15)
            self._storage_font_size = 8
        elif size_idx == 2:
            # 小
            self._btn_size = QSize(40, 40)
            self._icon_size = QSize(18, 18)
            self._font_size = 8
            self._storage_btn_size = QSize(28, 28)
            self._storage_icon_size = QSize(16, 16)
            self._storage_font_size = 9
        elif size_idx == 3:
            # 中
            self._btn_size = QSize(50, 50)
            self._icon_size = QSize(22, 22)
            self._font_size = 10
            self._storage_btn_size = QSize(30, 30)
            self._storage_icon_size = QSize(18, 18)
            self._storage_font_size = 10
        elif size_idx == 4:
            # 大
            self._btn_size = QSize(60, 60)
            self._icon_size = QSize(28, 28)
            self._font_size = 12
            self._storage_btn_size = QSize(35, 35)
            self._storage_icon_size = QSize(20, 20)
            self._storage_font_size = 11
        elif size_idx == 5:
            # 超大
            self._btn_size = QSize(70, 70)
            self._icon_size = QSize(34, 34)
            self._font_size = 14
            self._storage_btn_size = QSize(40, 40)
            self._storage_icon_size = QSize(22, 22)
            self._storage_font_size = 12
        elif size_idx == 6:
            # 超级大
            self._btn_size = QSize(80, 80)
            self._icon_size = QSize(40, 40)
            self._font_size = 16
            self._storage_btn_size = QSize(45, 45)
            self._storage_icon_size = QSize(24, 24)
            self._storage_font_size = 13

    def _build_ui(self):
        # 两行布局按索引分配，避免 3+ 个按钮全部落到底部
        lay = self._container.layout()
        if lay:
            while lay.count():
                item = lay.takeAt(0)
                w = item.widget()
                if w:
                    w.deleteLater()
            lay.deleteLater()
        if not self._layout:
            self._layout = QHBoxLayout(self)
            self._layout.setContentsMargins(
                self._margins, self._margins, self._margins, self._margins
            )
            self._layout.addWidget(self._container)
        else:
            self._layout.setContentsMargins(
                self._margins, self._margins, self._margins, self._margins
            )
        self._container_layout = self._create_container_layout()
        self._container.setLayout(self._container_layout)
        self._container_layout.setAlignment(Qt.AlignCenter)
        for i, spec in enumerate(self._buttons_spec):
            if spec == "quick_draw" and bool(
                getattr(self, "_extend_quick_draw_component", False)
            ):
                btn = self._create_extended_quick_draw_widget()
            else:
                btn = self._create_button(spec)
            self._add_button(btn, i, len(self._buttons_spec))
        self._container.adjustSize()
        self.adjustSize()
        self._install_drag_filters()

    def _apply_window(self):
        self.setWindowOpacity(self._opacity)
        if self._visible_on_start:
            self.show()
        else:
            self.hide()

    def _apply_focus_mode(self):
        """应用无焦点模式设置"""
        self._refresh_window_flags()

    def showEvent(self, event):
        """重写showEvent，当浮窗显示时检测边缘"""
        super().showEvent(event)
        if not bool(getattr(self, "_suppress_visibility_tracking", False)):
            self._user_requested_visible = True
            if bool(getattr(self, "_retracted", False)):
                storage = getattr(self, "storage_window", None)
                if storage is not None and not bool(storage.isVisible()):
                    try:
                        storage.show()
                    except Exception:
                        pass
        if bool(getattr(self, "_startup_initial_show", False)):
            self._check_edge_proximity(immediate=True)
        else:
            QTimer.singleShot(100, self._check_edge_proximity)
        if bool(getattr(self, "_startup_initial_show", False)):
            self._startup_initial_show = False
            self._schedule_auto_hide(force=False)
        try:
            self._uia_band_applied = False
            self._uia_band_applied_arrow = False
        except Exception:
            pass
        self._apply_topmost_runtime()

    def hideEvent(self, event):
        super().hideEvent(event)
        if not bool(getattr(self, "_suppress_visibility_tracking", False)):
            self._user_requested_visible = False
            storage = getattr(self, "storage_window", None)
            if storage is not None and bool(storage.isVisible()):
                try:
                    storage.hide()
                except Exception:
                    pass
        self._apply_topmost_runtime()

    def closeEvent(self, event):
        try:
            if (
                bool(getattr(self, "_close_guard_enabled", False))
                and not QApplication.instance().closingDown()
            ):
                try:
                    event.ignore()
                except Exception:
                    pass
                now_ms = int(QDateTime.currentMSecsSinceEpoch() or 0)
                if (
                    now_ms - int(getattr(self, "_close_guard_last_log_ms", 0) or 0)
                    >= 5000
                ):
                    self._close_guard_last_log_ms = now_ms
                    logger.warning("检测到外部关闭请求，已阻止关闭浮窗")
                try:
                    if self.isMinimized():
                        self.showNormal()
                    self.show()
                    self.raise_()
                except Exception:
                    pass
                return
        except Exception:
            pass
        return super().closeEvent(event)

    def _apply_position(self):
        x = int(readme_settings_async("float_position", "x") or 100)
        y = int(readme_settings_async("float_position", "y") or 100)
        nx, ny = self._clamp_to_screen(x, y)
        self.move(nx, ny)
        if bool(getattr(self, "_stick_to_edge", False)):
            self._stick_to_nearest_edge()

    def _clamp_to_screen(self, x, y):
        fg = self.frameGeometry()
        scr = QGuiApplication.screenAt(fg.center()) or QApplication.primaryScreen()
        geo = scr.availableGeometry()
        cx = max(geo.left(), min(x, geo.right() - self.width() + 1))
        cy = max(geo.top(), min(y, geo.bottom() - self.height() + 1))
        # logger.debug(
        #     f"_clamp_to_screen: 输入({x}, {y}), 输出({cx}, {cy}), 屏幕区域: {geo}"
        # )
        return cx, cy

    def _create_container_layout(self):
        if hasattr(self, "_top") and self._top:
            self._top.deleteLater()
            self._top = None
        if hasattr(self, "_bottom") and self._bottom:
            self._bottom.deleteLater()
            self._bottom = None
        if self._placement == 1:  # 垂直布局
            lay = QVBoxLayout()
            lay.setContentsMargins(
                self._margins, self._margins, self._margins, self._margins
            )
            lay.setSpacing(self._spacing)
            return lay
        if self._placement == 2:  # 水平布局
            lay = QHBoxLayout()
            lay.setContentsMargins(
                self._margins, self._margins, self._margins, self._margins
            )
            lay.setSpacing(self._spacing)
            return lay
        lay = QVBoxLayout()
        lay.setContentsMargins(
            self._margins, self._margins, self._margins, self._margins
        )
        lay.setSpacing(self._spacing)
        self._top = QWidget()
        self._top.setAttribute(Qt.WA_TranslucentBackground)
        self._bottom = QWidget()
        self._bottom.setAttribute(Qt.WA_TranslucentBackground)
        t = QHBoxLayout(self._top)
        t.setContentsMargins(0, 0, 0, 0)
        t.setSpacing(self._spacing)
        t.setAlignment(Qt.AlignCenter)
        b = QHBoxLayout(self._bottom)
        b.setContentsMargins(0, 0, 0, 0)
        b.setSpacing(self._spacing)
        b.setAlignment(Qt.AlignCenter)
        lay.addWidget(self._top)
        lay.addWidget(self._bottom)
        return lay

    def _emit_signal(self, signal):
        """发出信号的辅助方法"""
        signal.emit()

    def _handle_button_click(self, signal):
        """处理按钮点击事件，添加时间检查逻辑

        Args:
            signal: 要发出的信号
        """
        logger.debug(f"_handle_button_click: 开始处理按钮点击，signal={signal}")

        if signal == self.quickDrawRequested:
            if self._quick_draw_disabled:
                logger.info("闪抽功能已被禁用，请稍后再试")
                return

            was_disabled = self._quick_draw_disabled
            self._quick_draw_disabled = True
            logger.debug(
                f"_handle_button_click: 已禁用闪抽按钮（防抖），之前状态={was_disabled}"
            )

            try:
                is_non_class_time = _is_non_class_time()
                logger.debug(f"当前时间是否在非上课时间段内: {is_non_class_time}")
                if is_non_class_time:
                    verification_required = readme_settings_async(
                        "linkage_settings", "verification_required"
                    )
                    if verification_required:
                        logger.info("当前时间在非上课时间段内，需要密码验证")
                        require_and_run(
                            "quick_draw",
                            self,
                            lambda: self._emit_signal_with_reenable(signal),
                        )
                        return
                    else:
                        logger.info("当前时间在非上课时间段内，禁止闪抽")
                        self._quick_draw_disabled = False
                        logger.debug("_handle_button_click: 检查失败，已恢复闪抽按钮")
                        return

                disable_time = int(
                    readme_settings_async("quick_draw_settings", "disable_after_click")
                    or 0
                )

                if disable_time >= 1:
                    self._disable_quick_draw_timer.start(disable_time * 1000)
                    logger.info(f"闪抽功能已禁用，将在 {disable_time}s 后恢复")
                else:
                    self._quick_draw_disabled = False
                    logger.debug(
                        "_handle_button_click: 未设置禁用时间，立即恢复闪抽按钮"
                    )

            except Exception as e:
                logger.exception(
                    f"_handle_button_click: 处理闪抽按钮点击时发生异常: {e}"
                )
                self._quick_draw_disabled = False
                logger.debug("_handle_button_click: 发生异常，已恢复闪抽按钮")
                return

        logger.debug(f"_handle_button_click: 准备发出信号 {signal}")
        signal.emit()
        logger.debug(f"_handle_button_click: 信号已发出 {signal}")

    def _emit_signal_with_reenable(self, signal):
        """发出信号并在完成后恢复闪抽按钮状态"""
        logger.debug(f"_emit_signal_with_reenable: 准备发出信号 {signal}")
        signal.emit()
        logger.debug(f"_emit_signal_with_reenable: 信号已发出 {signal}")

    def _disable_quick_draw(self):
        """禁用闪抽功能"""
        self._quick_draw_disabled = True

    def _enable_quick_draw(self):
        """启用闪抽功能"""
        self._quick_draw_disabled = False
        logger.info("闪抽功能已恢复")

    def _create_button(self, spec: str) -> QPushButton:
        """创建按钮

        Args:
            spec: 按钮类型标识

        Returns:
            创建好的按钮实例
        """
        # 获取按钮配置信息
        button_config = self._get_button_config(spec)
        icon = button_config["icon"]
        text = button_config["text"]
        signal = button_config["signal"]

        # 根据显示样式创建不同类型的按钮
        if self._display_style == 1:
            btn = self._create_icon_only_button(icon)
        elif self._display_style == 2:
            btn = self._create_text_only_button(text)
        else:
            btn = self._create_composite_button(icon, text)

        # 连接信号
        btn.clicked.connect(lambda: self._handle_button_click(signal))
        btn.setAttribute(Qt.WA_TranslucentBackground)
        return btn

    def _get_button_config(self, spec: str) -> Dict[str, Any]:
        """获取按钮配置信息

        Args:
            spec: 按钮类型标识

        Returns:
            按钮配置字典，包含图标、文本和信号
        """
        button_configs = {
            "roll_call": {
                "icon": self._get_icon_for_floating_window(
                    "ic_fluent_people_20_filled", self._icon_size
                ),
                "text": get_content_name_async(
                    "floating_window_management", "roll_call_button"
                ),
                "signal": self.rollCallRequested,
            },
            "quick_draw": {
                "icon": self._get_icon_for_floating_window(
                    "ic_fluent_flash_20_filled", self._icon_size
                ),
                "text": get_content_name_async(
                    "floating_window_management", "quick_draw_button"
                ),
                "signal": self.quickDrawRequested,
            },
            "lottery": {
                "icon": self._get_icon_for_floating_window(
                    "ic_fluent_gift_20_filled", self._icon_size
                ),
                "text": get_content_name_async(
                    "floating_window_management", "lottery_button"
                ),
                "signal": self.lotteryRequested,
            },
            "face_draw": {
                "icon": self._get_icon_for_floating_window(
                    "ic_fluent_video_person_sparkle_20_filled", self._icon_size
                ),
                "text": get_content_name_async(
                    "floating_window_management", "face_draw_button"
                ),
                "signal": self.faceDrawRequested,
            },
            "timer": {
                "icon": self._get_icon_for_floating_window(
                    "ic_fluent_timer_20_filled", self._icon_size
                ),
                "text": get_content_name_async(
                    "floating_window_management", "timer_button"
                ),
                "signal": self.timerRequested,
            },
        }

        # 默认配置（点名按钮）
        default_config = {
            "icon": self._get_icon_for_floating_window(
                "ic_fluent_people_20_filled", self._icon_size
            ),
            "text": get_content_name_async(
                "floating_window_management", "roll_call_button"
            ),
            "signal": self.rollCallRequested,
        }

        return button_configs.get(spec, default_config)

    def _create_icon_only_button(self, icon: QIcon) -> QPushButton:
        """创建仅图标按钮"""
        btn = QPushButton()
        btn.setIcon(self._freeze_icon(icon, self._icon_size))
        btn.setIconSize(self._icon_size)
        btn.setFixedSize(self._btn_size)
        btn.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        btn.setFocusPolicy(Qt.NoFocus)
        btn.setAttribute(Qt.WA_TranslucentBackground)
        btn.setStyleSheet("background: transparent; border: none;")
        return btn

    def _create_text_only_button(self, text: str) -> PushButton:
        """创建仅文本按钮"""
        btn = PushButton(text)
        btn.setFixedSize(self._btn_size)
        btn.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        btn.setFont(self._font(self._font_size))
        btn.setAttribute(Qt.WA_TranslucentBackground)
        btn.setStyleSheet(
            f"background: transparent; border: none; color: {self._text_color()};"
        )
        return btn

    def _create_composite_button(self, icon: QIcon, text: str) -> QPushButton:
        """创建图文复合按钮"""
        btn = QPushButton()
        layout = QVBoxLayout(btn)
        layout.setContentsMargins(0, 4, 0, 4)
        layout.setSpacing(2)
        layout.setAlignment(Qt.AlignCenter)
        btn.setStyleSheet("background: transparent; border: none;")

        # 图标标签
        icon_label = self._create_icon_label(icon)
        layout.addWidget(icon_label)

        # 文本标签
        text_label = self._create_text_label(text)
        layout.addWidget(text_label)

        # 布局设置
        layout.setAlignment(Qt.AlignCenter)
        layout.setAlignment(icon_label, Qt.AlignCenter)
        layout.setAlignment(text_label, Qt.AlignCenter)

        btn.setFixedSize(self._btn_size)
        btn.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        btn.setAttribute(Qt.WA_TranslucentBackground)
        return btn

    def _create_icon_label(self, icon: QIcon) -> QLabel:
        """创建图标标签（用于复合按钮）"""
        label = QLabel()
        label.setPixmap(
            self._freeze_icon(icon, self._icon_size).pixmap(self._icon_size)
        )
        label.setFixedSize(self._icon_size)
        label.setAlignment(Qt.AlignCenter)
        label.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        return label

    def _create_text_label(self, text: str) -> BodyLabel:
        """创建文本标签（用于复合按钮）"""
        label = BodyLabel(text)
        label.setAlignment(Qt.AlignCenter)
        label.setFont(self._font(self._font_size))
        label.setAttribute(Qt.WA_TransparentForMouseEvents, True)  # 忽略鼠标事件
        label.setFocusPolicy(Qt.NoFocus)  # 无焦点
        # 标签样式：居中对齐、无背景、无边框
        label.setStyleSheet(
            f"background: transparent; border: none; color: {self._text_color()};"
        )
        return label

    def _add_button(self, btn, index, total):
        if self._placement == 1:
            self._container.layout().addWidget(btn, 0, Qt.AlignCenter)
            return
        if self._placement == 2:
            self._container.layout().addWidget(btn, 0, Qt.AlignCenter)
            return
        # 前半放顶行，后半放底行
        split = (total + 1) // 2
        if index < split:
            self._top.layout().addWidget(btn, 0, Qt.AlignCenter)
        else:
            self._bottom.layout().addWidget(btn, 0, Qt.AlignCenter)

    def _create_extended_quick_draw_widget(self) -> QWidget:
        quick_draw_btn = self._create_button("quick_draw")
        arrow_btn = self._create_quick_draw_extend_arrow_button()

        wrapper = QWidget()
        wrapper.setAttribute(Qt.WA_TranslucentBackground)
        lay = QHBoxLayout(wrapper)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(max(2, int(self._spacing / 2)))
        lay.setAlignment(Qt.AlignCenter)
        lay.addWidget(quick_draw_btn)
        lay.addWidget(arrow_btn)

        return wrapper

    def _create_quick_draw_extend_arrow_button(self) -> QPushButton:
        btn = QPushButton()
        btn.setAttribute(Qt.WA_TranslucentBackground)
        btn.setFocusPolicy(Qt.NoFocus)
        btn.setStyleSheet("background: transparent; border: none;")

        w = max(20, int(self._btn_size.width() * 0.45))
        h = int(self._btn_size.height())
        btn.setFixedSize(QSize(w, h))

        icon_size = QSize(
            max(14, int(self._icon_size.width() * 0.75)),
            max(14, int(self._icon_size.height() * 0.75)),
        )
        btn.setIcon(
            self._freeze_icon(
                self._get_icon_for_floating_window(
                    "ic_fluent_chevron_down_20_filled", icon_size
                ),
                icon_size,
            )
        )
        btn.setIconSize(icon_size)
        btn.clicked.connect(lambda: self._toggle_quick_draw_extend_panel(btn))
        return btn

    def _toggle_quick_draw_extend_panel(self, anchor: QWidget) -> None:
        panel = getattr(self, "_quick_draw_extend_panel", None)
        if panel is not None and panel.isVisible():
            self._close_quick_draw_extend_panel()
            return

        panel = self._ensure_quick_draw_extend_panel()
        self._quick_draw_extend_anchor = anchor
        self._populate_quick_draw_extend_panel()
        self._position_quick_draw_extend_panel(anchor)
        panel.show()
        panel.raise_()
        self._arm_quick_draw_extend_panel_auto_close()

    def _arm_quick_draw_extend_panel_auto_close(self) -> None:
        timer = getattr(self, "_quick_draw_extend_close_timer", None)
        if timer is None:
            return
        try:
            if timer.isActive():
                timer.stop()
        except Exception:
            pass

        delay_ms = int(float(getattr(self, "custom_retract_time", 0) or 0) * 1000)
        if delay_ms <= 0:
            return
        try:
            timer.start(delay_ms)
        except Exception:
            pass

    def _close_quick_draw_extend_panel(self) -> None:
        timer = getattr(self, "_quick_draw_extend_close_timer", None)
        if timer is not None:
            try:
                if timer.isActive():
                    timer.stop()
            except Exception:
                pass

        panel = getattr(self, "_quick_draw_extend_panel", None)
        if panel is not None:
            try:
                if panel.isVisible():
                    panel.close()
            except Exception:
                pass
        self._quick_draw_extend_anchor = None

    def _ensure_quick_draw_extend_panel(self) -> QWidget:
        panel = getattr(self, "_quick_draw_extend_panel", None)
        if panel is not None:
            try:
                panel.setParent(None)
            except Exception:
                pass
            try:
                panel.close()
            except Exception:
                pass

        panel = QFrame(None)
        panel.setWindowFlags(
            Qt.Popup | Qt.FramelessWindowHint | Qt.NoDropShadowWindowHint
        )
        panel.setAttribute(Qt.WA_TranslucentBackground)
        try:
            panel.setAttribute(
                Qt.WA_ShowWithoutActivating,
                bool(getattr(self, "_do_not_steal_focus", False)),
            )
        except Exception:
            pass
        try:
            panel.installEventFilter(self)
        except Exception:
            pass

        outer = QVBoxLayout(panel)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)

        card = QWidget(panel)
        card.setObjectName("quick_draw_extend_card")
        card_layout = QVBoxLayout(card)
        card_layout.setContentsMargins(12, 12, 12, 12)
        card_layout.setSpacing(8)

        label_font = self._font(max(8, int(self._font_size)))

        class_combo = ComboBox()
        class_combo.setFont(label_font)
        class_combo.setFixedHeight(34)
        class_combo.setMinimumWidth(220)
        class_combo.setPlaceholderText(
            get_content_name_async("roll_call", "default_empty_item")
        )

        range_combo = ComboBox()
        range_combo.setFont(label_font)
        range_combo.setFixedHeight(34)
        range_combo.setMinimumWidth(220)

        gender_combo = ComboBox()
        gender_combo.setFont(label_font)
        gender_combo.setFixedHeight(34)
        gender_combo.setMinimumWidth(220)

        class_combo.currentTextChanged.connect(
            lambda text: self._on_quick_draw_extend_class_changed(
                text, range_combo, gender_combo
            )
        )
        range_combo.currentTextChanged.connect(
            lambda text: update_settings(
                "floating_window_management", "quick_draw_group_filter", text
            )
        )
        gender_combo.currentTextChanged.connect(
            lambda text: update_settings(
                "floating_window_management", "quick_draw_gender_filter", text
            )
        )

        card_layout.addWidget(class_combo)
        card_layout.addWidget(range_combo)
        card_layout.addWidget(gender_combo)

        outer.addWidget(card)

        self._quick_draw_extend_card = card
        self._quick_draw_extend_class_combo = class_combo
        self._quick_draw_extend_range_combo = range_combo
        self._quick_draw_extend_gender_combo = gender_combo
        self._quick_draw_extend_panel = panel

        self._apply_quick_draw_extend_panel_style()
        return panel

    def _apply_quick_draw_extend_panel_style(self) -> None:
        card = getattr(self, "_quick_draw_extend_card", None)
        if card is None:
            return

        dark = self._is_floating_window_dark()
        if dark:
            card.setStyleSheet(
                "background-color: rgba(32,32,32,220); border-radius: 12px; border: 1px solid rgba(255,255,255,24);"
            )
        else:
            card.setStyleSheet(
                "background-color: rgba(255,255,255,245); border-radius: 12px; border: 1px solid rgba(0,0,0,16);"
            )

    def _position_quick_draw_extend_panel(self, anchor: QWidget) -> None:
        panel = getattr(self, "_quick_draw_extend_panel", None)
        if panel is None:
            return

        try:
            anchor_top_left = anchor.mapToGlobal(QPoint(0, 0))
            anchor_rect = QRect(anchor_top_left, anchor.size())
        except Exception:
            p = QCursor.pos()
            anchor_rect = QRect(p, QSize(1, 1))

        scr = (
            QGuiApplication.screenAt(anchor_rect.center())
            or QApplication.primaryScreen()
        )
        geo = scr.availableGeometry()

        panel.adjustSize()
        main_geo = self.frameGeometry()
        w = int(panel.width())
        h = int(panel.height())

        candidates = []

        x = main_geo.right() + 6
        y = anchor_rect.center().y() - int(h / 2)
        if x + w <= geo.right() + 1:
            candidates.append((x, y))

        x = anchor_rect.center().x() - int(w / 2)
        y = main_geo.bottom() + 6
        if y + h <= geo.bottom() + 1:
            candidates.append((x, y))

        x = main_geo.left() - w - 6
        y = anchor_rect.center().y() - int(h / 2)
        if x >= geo.left():
            candidates.append((x, y))

        x = anchor_rect.center().x() - int(w / 2)
        y = main_geo.top() - h - 6
        if y >= geo.top():
            candidates.append((x, y))

        if not candidates:
            x = main_geo.right() + 6
            y = anchor_rect.center().y() - int(h / 2)
            candidates.append((x, y))

        x, y = candidates[0]
        x = max(geo.left(), min(x, geo.right() - w + 1))
        y = max(geo.top(), min(y, geo.bottom() - h + 1))

        panel.move(x, y)

    def _populate_quick_draw_extend_panel(self) -> None:
        class_combo = getattr(self, "_quick_draw_extend_class_combo", None)
        range_combo = getattr(self, "_quick_draw_extend_range_combo", None)
        gender_combo = getattr(self, "_quick_draw_extend_gender_combo", None)
        if class_combo is None or range_combo is None or gender_combo is None:
            return

        class_list = get_class_name_list()
        saved_class = str(
            readme_settings_async("floating_window_management", "quick_draw_class_name")
            or ""
        )

        class_combo.blockSignals(True)
        class_combo.clear()
        class_combo.addItems(class_list)
        if saved_class and saved_class in class_list:
            class_combo.setCurrentIndex(class_list.index(saved_class))
        elif class_list:
            class_combo.setCurrentIndex(0)
        class_combo.blockSignals(False)

        current_class = class_combo.currentText() if class_combo.count() > 0 else ""
        if current_class:
            update_settings(
                "floating_window_management", "quick_draw_class_name", current_class
            )

        self._refresh_quick_draw_extend_filters(
            current_class, range_combo, gender_combo
        )

    def _on_quick_draw_extend_class_changed(
        self, class_name: str, range_combo: ComboBox, gender_combo: ComboBox
    ) -> None:
        update_settings(
            "floating_window_management", "quick_draw_class_name", class_name
        )
        self._refresh_quick_draw_extend_filters(class_name, range_combo, gender_combo)

    def _refresh_quick_draw_extend_filters(
        self, class_name: str, range_combo: ComboBox, gender_combo: ComboBox
    ) -> None:
        saved_group_filter = str(
            readme_settings_async(
                "floating_window_management", "quick_draw_group_filter"
            )
            or ""
        )
        saved_gender_filter = str(
            readme_settings_async(
                "floating_window_management", "quick_draw_gender_filter"
            )
            or ""
        )

        base_range_options = get_content_combo_name_async("roll_call", "range_combobox")
        group_list = get_group_list(class_name) if class_name else []
        range_items = (
            (base_range_options + group_list)
            if group_list and group_list != [""]
            else base_range_options[:1]
        )

        base_gender_options = get_content_combo_name_async(
            "roll_call", "gender_combobox"
        )
        gender_list = get_gender_list(class_name) if class_name else []
        gender_items = (
            (base_gender_options + gender_list)
            if gender_list and gender_list != [""]
            else base_gender_options[:1]
        )

        range_combo.blockSignals(True)
        range_combo.clear()
        range_combo.addItems(range_items)
        if saved_group_filter:
            idx = range_combo.findText(saved_group_filter)
            range_combo.setCurrentIndex(idx if idx >= 0 else 0)
        else:
            range_combo.setCurrentIndex(0)
        range_combo.blockSignals(False)

        gender_combo.blockSignals(True)
        gender_combo.clear()
        gender_combo.addItems(gender_items)
        if saved_gender_filter:
            idx = gender_combo.findText(saved_gender_filter)
            gender_combo.setCurrentIndex(idx if idx >= 0 else 0)
        else:
            gender_combo.setCurrentIndex(0)
        gender_combo.blockSignals(False)

        update_settings(
            "floating_window_management",
            "quick_draw_group_filter",
            range_combo.currentText() if range_combo.count() > 0 else "",
        )
        update_settings(
            "floating_window_management",
            "quick_draw_gender_filter",
            gender_combo.currentText() if gender_combo.count() > 0 else "",
        )

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            if not self._draggable:
                return  # 如果不可拖动，直接返回
            self._close_quick_draw_extend_panel()
            self._press_pos = e.globalPosition().toPoint()
            self._press_time = int(time.monotonic() * 1000)
            self._dragging = False
            self._drag_timer.stop()
            self._drag_timer.start(self._long_press_ms)

    def _begin_drag(self):
        if not self._draggable:
            return
        self._close_quick_draw_extend_panel()
        self._dragging = True
        self.setCursor(Qt.ClosedHandCursor)
        pass

    def mouseMoveEvent(self, e):
        """处理鼠标移动事件"""
        # 如果不可拖动，停止任何正在进行的拖动
        if not self._draggable:
            if self._dragging:
                self._dragging = False
                self.setCursor(Qt.ArrowCursor)
            return

        if e.buttons() & Qt.LeftButton:
            cur = e.globalPosition().toPoint()

            # 检查是否需要开始拖拽，添加时间检测避免误识别点击为拖动
            if not self._dragging:
                delta = cur - self._press_pos
                press_duration = (
                    int(time.monotonic() * 1000) - self._press_time
                    if self._press_time > 0
                    else 0
                )
                if self._should_start_drag(delta, press_duration):
                    self._begin_drag()

            # 执行拖拽
            if self._dragging:
                delta = cur - self._press_pos
                self.move(self.x() + delta.x(), self.y() + delta.y())
                self._press_pos = cur
                self._cancel_retract()

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._drag_timer.stop()
            self.setCursor(Qt.ArrowCursor)
            if self._dragging:
                self._dragging = False
                # 只有在可拖动且是用户主动拖动的情况下才保存位置
                if self._draggable:
                    self._save_position()

    def _should_start_drag(self, delta: QPoint, duration: int = 0) -> bool:
        """判断是否应该开始拖拽

        Args:
            delta: 鼠标移动偏移量
            duration: 鼠标按下持续时间（毫秒）

        Returns:
            是否应该开始拖拽
        """
        # 智能拖动检测：根据持续时间调整识别阈值
        # 1. 首先检查最小时间阈值，避免极短时间内的移动被识别为拖动
        if duration < self.MIN_DRAG_TIME:
            return False  # 时间太短，不识别为拖动

        # 2. 根据持续时间调整距离阈值
        min_distance = self.DRAG_THRESHOLD
        if duration < 150:  # 快速点击（100-150ms）
            min_distance = self.DRAG_THRESHOLD * 2  # 需要较大的移动距离
        # duration >= 150ms 正常使用默认阈值

        return abs(delta.x()) >= min_distance or abs(delta.y()) >= min_distance

    def eventFilter(self, obj, event):
        """事件过滤器，处理拖拽相关事件"""
        panel = getattr(self, "_quick_draw_extend_panel", None)
        if panel is not None and obj is panel:
            if event.type() in (QEvent.WindowDeactivate, QEvent.Hide, QEvent.Close):
                QTimer.singleShot(0, self._close_quick_draw_extend_panel)
            return False

        if not self._draggable:
            return False

        if event.type() == QEvent.MouseButtonPress:
            return self._handle_mouse_press_event(event)
        elif event.type() == QEvent.MouseMove:
            return self._handle_mouse_move_event(event)
        elif event.type() == QEvent.MouseButtonRelease:
            return self._handle_mouse_release_event(event)

        return False

    def _handle_mouse_press_event(self, event) -> bool:
        """处理鼠标按下事件"""
        if event.button() == Qt.LeftButton:
            if not self._draggable:
                # 如果不可拖动，不启动拖动计时器
                return False
            self._close_quick_draw_extend_panel()
            self._press_pos = event.globalPosition().toPoint()
            self._press_time = int(time.monotonic() * 1000)
            self._dragging = False
            self._drag_timer.stop()
            self._drag_timer.start(self._long_press_ms)
        return False

    def _handle_mouse_move_event(self, event) -> bool:
        """处理鼠标移动事件"""
        # 如果不可拖动，停止拖动操作
        if not self._draggable:
            if self._dragging:
                self._dragging = False
                self.setCursor(Qt.ArrowCursor)
            return False

        if event.buttons() & Qt.LeftButton:
            cur = event.globalPosition().toPoint()

            # 检查是否需要开始拖拽，添加时间检测避免误识别点击为拖动
            if not self._dragging:
                delta = cur - self._press_pos
                press_duration = (
                    int(time.monotonic() * 1000) - self._press_time
                    if self._press_time > 0
                    else 0
                )
                if self._should_start_drag(delta, press_duration):
                    self._begin_drag()

            # 执行拖拽
            if self._dragging:
                delta = cur - self._press_pos
                self.move(self.x() + delta.x(), self.y() + delta.y())
                self._press_pos = cur
                return True

        return False

    def _handle_mouse_release_event(self, event) -> bool:
        """处理鼠标释放事件"""
        if event.button() == Qt.LeftButton:
            self._drag_timer.stop()
            # 如果不可拖动，但仍在拖动状态，则结束拖动操作
            if self._draggable and self._dragging:
                self._end_drag_operation()
                return True
            elif not self._draggable and self._dragging:
                # 如果不可拖动但仍在拖动状态，强制结束拖动
                self._dragging = False
                self.setCursor(Qt.ArrowCursor)

        return False

    def _end_drag_operation(self):
        """结束拖拽操作"""
        self._dragging = False
        self.setCursor(Qt.ArrowCursor)
        self._stick_to_nearest_edge()

        self._save_position()

        # 如果启用了边缘贴边隐藏功能，在拖动结束后检查是否需要贴边
        if self.floating_window_stick_to_edge:
            QTimer.singleShot(100, self._check_edge_proximity)

    def _install_drag_filters(self):
        self._container.installEventFilter(self)
        for w in self._container.findChildren(QWidget):
            w.installEventFilter(self)

    def enterEvent(self, e):
        # 当鼠标进入窗口时，删除可能存在的自动隐藏定时器
        if hasattr(self, "_auto_hide_timer"):
            if self._auto_hide_timer.isActive():
                self._auto_hide_timer.stop()
            # 从对象中移除定时器属性，避免内存泄漏
            delattr(self, "_auto_hide_timer")

    def leaveEvent(self, e):
        self._schedule_auto_hide(force=True)

    def _schedule_auto_hide(self, force: bool = False):
        if not bool(getattr(self, "floating_window_stick_to_edge", False)):
            return
        if bool(getattr(self, "_retracted", False)):
            return
        if not self.isVisible():
            return
        if not bool(force):
            try:
                if self.frameGeometry().contains(QCursor.pos()):
                    return
            except Exception:
                pass

        delay_ms = int(float(getattr(self, "custom_retract_time", 0) or 0) * 1000)
        if delay_ms <= 0:
            return

        if hasattr(self, "_auto_hide_timer") and self._auto_hide_timer.isActive():
            self._auto_hide_timer.stop()
        self._auto_hide_timer = QTimer(self)
        self._auto_hide_timer.setSingleShot(True)
        self._auto_hide_timer.timeout.connect(self._auto_hide_window)
        self._auto_hide_timer.start(delay_ms)

    def _stick_to_nearest_edge(self):
        if not self._stick_to_edge:
            return
        fg = self.frameGeometry()
        scr = QGuiApplication.screenAt(fg.center()) or QApplication.primaryScreen()
        geo = scr.availableGeometry()
        left = fg.left() - geo.left()
        right = geo.right() - fg.right()
        self._last_stuck = False
        if left <= self._edge_threshold:
            self.move(geo.left(), self.y())
            self._last_stuck = True
            return
        if right <= self._edge_threshold:
            self.move(geo.right() - self.width() + 1, self.y())
            self._last_stuck = True

    def _cancel_retract(self):
        if self._retract_timer.isActive():
            self._retract_timer.stop()

    def _retract_into_edge(self):
        # 防多屏错位：基于当前屏幕几何
        fg = self.frameGeometry()
        scr = QGuiApplication.screenAt(fg.center()) or QApplication.primaryScreen()
        geo = scr.availableGeometry()
        if self.x() <= geo.left():
            # 完全移出屏幕左侧
            self.move(geo.left() - self.width(), self.y())
            self._retracted = True
        elif self.x() + self.width() >= geo.right():
            # 完全移出屏幕右侧
            self.move(geo.right(), self.y())
            self._retracted = True

    def _expand_from_edge(self):
        fg = self.frameGeometry()
        scr = QGuiApplication.screenAt(fg.center()) or QApplication.primaryScreen()
        geo = scr.availableGeometry()
        if self.x() < geo.left():
            self.move(geo.left(), self.y())
        elif self.x() + self.width() > geo.right():
            self.move(geo.right() - self.width() + 1, self.y())
        self._retracted = False
        # logger.debug(f"_expand_from_edge: 窗口位置已展开到 ({self.x()}, {self.y()})")

    def _check_edge_proximity(self, immediate: bool = False):
        """检测窗口是否靠近屏幕边缘，并实现贴边隐藏功能（带动画效果）"""
        # logger.debug("开始检查边缘贴边隐藏功能")

        # 如果有正在进行的动画，先停止它
        if (
            hasattr(self, "animation")
            and self.animation.state() == QPropertyAnimation.Running
        ):
            self.animation.stop()

        # logger.debug(f"贴边隐藏功能状态: {self.floating_window_stick_to_edge}")

        # 如果贴边隐藏功能未启用，直接返回
        if not self.floating_window_stick_to_edge:
            logger.debug("贴边隐藏功能未启用，跳过检测")
            return

        # 获取屏幕尺寸
        screen = QApplication.primaryScreen().availableGeometry()

        # 获取窗口当前位置和尺寸
        window_pos = self.pos()
        window_width = self.width()
        window_height = self.height()

        # 定义边缘阈值（像素）
        edge_threshold = 5
        hidden_width = 10  # 隐藏后露出的宽度

        # 检测左边缘
        if window_pos.x() <= edge_threshold:
            # 保存主浮窗的原始位置（但不更新实际坐标）
            if not hasattr(self, "_original_position"):
                self._original_position = window_pos

            if bool(immediate):
                end_rect = QRect(
                    screen.left() - window_width + 1,
                    window_pos.y(),
                    window_width,
                    window_height,
                )
                try:
                    self.setGeometry(end_rect)
                except Exception:
                    pass
                self._create_arrow_button(
                    "right",
                    0,
                    window_pos.y()
                    + window_height // 2
                    - self._storage_btn_size.height() // 2,
                )
                self._retracted = True
                return

            # 创建平滑动画效果
            self.animation = QPropertyAnimation(self, b"geometry")
            # 设置动画持续时间（更流畅的过渡）
            self.animation.setDuration(400)
            # 设置缓动曲线（使用更自然的缓动）
            self.animation.setEasingCurve(QEasingCurve.Type.OutCubic)

            # 设置动画起始值（当前位置）
            self.animation.setStartValue(self.geometry())

            # 设置动画结束值（隐藏位置）- 移出屏幕但保留部分在屏幕内以保持screenAt()正常工作
            end_rect = QRect(
                screen.left() - window_width + 1,
                window_pos.y(),
                window_width,
                window_height,
            )
            self.animation.setEndValue(end_rect)

            # 动画完成后创建收纳浮窗
            def on_animation_finished():
                logger.debug(
                    f"贴边隐藏动画完成，选择显示样式: {self._stick_indicator_style}"
                )
                self._create_arrow_button(
                    "right",
                    0,
                    window_pos.y()
                    + window_height // 2
                    - self._storage_btn_size.height() // 2,
                )
                # 标记为已收纳状态，但保持原始坐标不变
                self._retracted = True

            self.animation.finished.connect(on_animation_finished)

            # 启动动画
            self.animation.start()
            return

        # 检测右边缘
        elif window_pos.x() + window_width >= screen.width() - edge_threshold:
            # 保存主浮窗的原始位置（但不更新实际坐标）
            if not hasattr(self, "_original_position"):
                self._original_position = window_pos

            if bool(immediate):
                end_rect = QRect(
                    screen.right() - 1,
                    window_pos.y(),
                    window_width,
                    window_height,
                )
                try:
                    self.setGeometry(end_rect)
                except Exception:
                    pass
                self._create_arrow_button(
                    "left",
                    screen.width() - self._storage_btn_size.width(),
                    window_pos.y()
                    + window_height // 2
                    - self._storage_btn_size.height() // 2,
                )
                self._retracted = True
                return

            # 创建平滑动画效果
            self.animation = QPropertyAnimation(self, b"geometry")
            # 设置动画持续时间（更流畅的过渡）
            self.animation.setDuration(400)
            # 设置缓动曲线（使用更自然的缓动）
            self.animation.setEasingCurve(QEasingCurve.Type.OutCubic)

            # 设置动画起始值（当前位置）
            self.animation.setStartValue(self.geometry())

            # 设置动画结束值（隐藏位置）- 移出屏幕但保留部分在屏幕内以保持screenAt()正常工作
            end_rect = QRect(
                screen.right() - 1,
                window_pos.y(),
                window_width,
                window_height,
            )
            self.animation.setEndValue(end_rect)

            # 动画完成后创建收纳浮窗
            def on_animation_finished():
                # logger.debug(
                #     f"贴边隐藏动画完成，选择显示样式: {self._stick_indicator_style}"
                # )
                # 根据配置选择创建收纳浮窗或箭头按钮
                self._create_arrow_button(
                    "left",
                    screen.width() - self._storage_btn_size.width(),
                    window_pos.y()
                    + window_height // 2
                    - self._storage_btn_size.height() // 2,
                )
                # 标记为已收纳状态，但保持原始坐标不变
                self._retracted = True

            self.animation.finished.connect(on_animation_finished)

            # 启动动画
            self.animation.start()
            return

        # 保存新位置（仅在窗口未贴边隐藏时）
        if (
            window_pos.x() > edge_threshold
            and window_pos.x() + window_width < screen.width() - edge_threshold
        ):
            # 只有在非收纳状态下才保存位置
            if not self._retracted:
                self._save_position()
            # 清除原始位置
            if hasattr(self, "_original_position"):
                delattr(self, "_original_position")

        self._retracted = False

    def _smart_snap_to_edge(self):
        """智能吸附到屏幕边缘"""
        if not hasattr(self, "storage_window") or not self.storage_window:
            return

        # 获取屏幕信息
        screen = QApplication.primaryScreen().availableGeometry()
        current_pos = self.storage_window.pos()
        window_height = self.storage_window.height()

        # 计算吸附阈值
        snap_threshold = 50

        # 计算目标Y位置
        target_y = current_pos.y()

        # 检查是否靠近屏幕顶部或底部
        if current_pos.y() < screen.top() + snap_threshold:
            # 吸附到顶部
            target_y = screen.top() + 10
        elif current_pos.y() + window_height > screen.bottom() - snap_threshold:
            # 吸附到底部
            target_y = screen.bottom() - window_height - 10
        else:
            # 检查是否靠近其他常用位置（屏幕中央等）
            screen_center_y = screen.center().y()
            if (
                abs(current_pos.y() + window_height // 2 - screen_center_y)
                < snap_threshold
            ):
                target_y = screen_center_y - window_height // 2

        # 如果位置需要调整，添加平滑动画
        if target_y != current_pos.y():
            snap_animation = QPropertyAnimation(self.storage_window, b"geometry")
            snap_animation.setDuration(200)
            snap_animation.setEasingCurve(QEasingCurve.Type.OutBack)
            start_rect = self.storage_window.geometry()
            end_rect = QRect(
                self._storage_initial_x,
                target_y,
                start_rect.width(),
                start_rect.height(),
            )
            snap_animation.setStartValue(start_rect)
            snap_animation.setEndValue(end_rect)
            snap_animation.start()

            # 更新存储的位置
            self._storage_initial_x = self._storage_initial_x

    def _delete_storage_window(self):
        """删除收纳浮窗"""
        # 安全删除 storage_window 与 arrow_widget（它们可能指向相同对象）
        storage = getattr(self, "storage_window", None)
        arrow_w = getattr(self, "arrow_widget", None)

        # 如果 storage 存在，先记录引用再删除
        if storage:
            try:
                storage.deleteLater()
            except Exception:
                pass
            finally:
                try:
                    self.storage_window = None
                except Exception:
                    pass

        # 如果 arrow_widget 存在并且不是已被上述 storage 删除的同一对象，则删除
        if arrow_w and arrow_w is not storage:
            try:
                arrow_w.deleteLater()
            except Exception:
                pass
            finally:
                try:
                    self.arrow_widget = None
                except Exception:
                    pass
        else:
            # 如果它们是同一对象，上面已删除，仍确保引用清除
            try:
                self.arrow_widget = None
            except Exception:
                pass

    def _on_storage_press(self, event):
        """收纳浮窗按下事件 - 增强拖动体验"""
        if event.button() == Qt.LeftButton:
            self._storage_drag_start = event.pos()
            self._storage_dragging = False

            # 添加按下视觉反馈
            if hasattr(self, "storage_window") and self.storage_window:
                current_style = self.storage_window.styleSheet()
                if "background-clip: padding-box;" in current_style:
                    # 提取颜色值并添加按下效果
                    rgba_part = current_style.split("rgba(")[1].split(")")[0]
                    press_style = (
                        current_style.split("background-color: rgba(")[0]
                        + "background-color: rgba("
                        + rgba_part
                        + ", "
                        + str(min(float(self._opacity) + 0.1, 1.0))
                        + ");"
                        + "border: 2px solid rgba(255, 255, 255, 0.5);"
                        + "background-clip: padding-box;"
                    )
                    self.storage_window.setStyleSheet(press_style)

    def _on_storage_move(self, event):
        """收纳浮窗移动事件 - 增强拖动体验"""
        if event.buttons() & Qt.LeftButton:
            delta = event.pos() - self._storage_drag_start
            if not self._storage_dragging:
                # 检测是否超过拖动阈值
                if abs(delta.y()) > 3:
                    self._storage_dragging = True
            if self._storage_dragging:
                # 只在y轴移动，x轴保持固定
                new_y = self.storage_window.y() + delta.y()
                # 限制在屏幕内
                screen = QApplication.primaryScreen().availableGeometry()
                new_y = max(
                    screen.top(),
                    min(new_y, screen.bottom() - self.storage_window.height()),
                )
                self.storage_window.move(self._storage_initial_x, new_y)

                # 添加拖动时的视觉反馈
                if hasattr(self, "storage_window") and self.storage_window:
                    current_style = self.storage_window.styleSheet()
                    if "background-clip: padding-box;" in current_style:
                        # 拖动时增加透明度并添加阴影效果
                        rgba_part = current_style.split("rgba(")[1].split(")")[0]
                        drag_style = (
                            current_style.split("background-color: rgba(")[0]
                            + "background-color: rgba("
                            + rgba_part
                            + ", "
                            + str(min(float(self._opacity) + 0.15, 1.0))
                            + ");"
                            + "border: 1px solid rgba(255, 255, 255, 0.4);"
                            + "background-clip: padding-box;"
                        )
                        self.storage_window.setStyleSheet(drag_style)

    def _on_storage_release(self, event):
        """收纳浮窗释放事件 - 增强交互体验"""
        if event.button() == Qt.LeftButton:
            # 恢复原始样式
            if hasattr(self, "storage_window") and self.storage_window:
                current_style = self.storage_window.styleSheet()
                if "background-clip: padding-box;" in current_style:
                    # 提取颜色值并恢复原始样式
                    rgba_part = current_style.split("rgba(")[1].split(")")[0]
                    original_style = (
                        current_style.split("background-color: rgba(")[0]
                        + "background-color: rgba("
                        + rgba_part
                        + ", "
                        + str(self._opacity)
                        + ");"
                        + "border: none;"
                        + "background-clip: padding-box;"
                    )
                    self.storage_window.setStyleSheet(original_style)

            # 智能吸附功能：释放时检查是否需要吸附到边缘
            if self._storage_dragging:
                self._smart_snap_to_edge()
            else:
                # 点击事件 - 展开主浮窗
                self._expand_window()
            self._storage_dragging = False
        elif event.button() == Qt.RightButton:
            # 右键点击 - 展开主浮窗（提供额外的交互方式）
            if not self._storage_dragging:
                self._expand_window()

    def mouseDoubleClickEvent(self, event):
        """双击事件 - 快速展开主浮窗"""
        # 检查是否在收纳窗口上双击
        if hasattr(self, "storage_window") and self.storage_window:
            # 获取鼠标位置
            mouse_pos = event.globalPos()
            storage_rect = self.storage_window.geometry()

            # 检查鼠标是否在收纳窗口范围内
            if storage_rect.contains(mouse_pos):
                # 双击收纳窗口，快速展开主浮窗
                self._expand_window()
                event.accept()
                return

        # 调用父类的双击事件处理
        super().mouseDoubleClickEvent(event)

    def _expand_window(self):
        """展开隐藏的窗口（带动画效果）"""
        # 如果收纳浮窗不存在，直接返回
        if not hasattr(self, "storage_window") or not self.storage_window:
            return

        # 保存收纳浮窗的当前位置和尺寸
        storage_window = self.storage_window
        storage_pos = storage_window.pos()
        storage_width = storage_window.width()
        storage_height = storage_window.height()

        # 获取屏幕尺寸
        screen = QApplication.primaryScreen().availableGeometry()

        # 创建收纳浮窗的退出动画
        storage_animation = QPropertyAnimation(storage_window, b"geometry")
        storage_animation.setDuration(200)
        storage_animation.setEasingCurve(QEasingCurve.Type.OutQuad)

        # 设置收纳浮窗动画的起始值和结束值
        storage_animation.setStartValue(storage_window.geometry())

        # 根据收纳浮窗的位置，将其移动到屏幕外
        if storage_pos.x() < screen.width() // 2:
            # 左侧收纳浮窗，向右移出屏幕
            storage_end_rect = QRect(
                screen.right() - 1,
                storage_pos.y(),
                storage_width,
                storage_height,
            )
        else:
            # 右侧收纳浮窗，向左移出屏幕
            storage_end_rect = QRect(
                screen.left() - storage_width + 1,
                storage_pos.y(),
                storage_width,
                storage_height,
            )
        storage_animation.setEndValue(storage_end_rect)

        # 获取主窗口当前尺寸
        window_width = self.width()
        window_height = self.height()

        # 如果有正在进行的动画，先停止它
        if (
            hasattr(self, "animation")
            and self.animation.state() == QPropertyAnimation.Running
        ):
            self.animation.stop()

        # 定义主窗口的动画完成回调
        def on_main_animation_finished():
            self._retracted = False
            # 将窗口提升到最前面，但不激活窗口
            self.raise_()
            # 设置自动隐藏定时器
            self._auto_hide_timer = QTimer(self)
            self._auto_hide_timer.setSingleShot(True)
            self._auto_hide_timer.timeout.connect(self._auto_hide_window)
            self._auto_hide_timer.start(self.custom_retract_time * 1000)

        # 定义收纳浮窗动画完成后的回调
        def on_storage_animation_finished():
            # 删除收纳浮窗
            self._delete_storage_window()

            # 获取主窗口应该恢复到的位置
            if hasattr(self, "_original_position"):
                # 使用保存的原始位置
                target_x = self._original_position.x()
                target_y = self._original_position.y()
            else:
                # 如果没有保存的原始位置，使用默认位置
                if self.x() < screen.left():
                    # 从左侧展开
                    target_x = screen.left()
                elif self.x() + window_width > screen.right():
                    # 从右侧展开
                    target_x = screen.right() - window_width + 1
                target_y = self.y()

            # 创建主窗口的动画效果
            self.animation = QPropertyAnimation(self, b"geometry")
            self.animation.setDuration(300)
            self.animation.setEasingCurve(
                QEasingCurve.Type.OutCubic
            )  # 使用更自然的缓动曲线

            # 设置主窗口动画的起始值和结束值
            self.animation.setStartValue(self.geometry())
            main_end_rect = QRect(target_x, target_y, window_width, window_height)
            self.animation.setEndValue(main_end_rect)

            # 连接主窗口动画的完成信号
            self.animation.finished.connect(on_main_animation_finished)

            # 启动主窗口动画
            self.animation.start()

        # 连接收纳浮窗动画的完成信号
        storage_animation.finished.connect(on_storage_animation_finished)

        # 启动收纳浮窗动画
        storage_animation.start()

    def _auto_hide_window(self):
        """自动隐藏窗口"""
        # 检查是否启用了边缘贴边隐藏功能
        if self.floating_window_stick_to_edge:
            if bool(getattr(self, "_retracted", False)):
                return
            # 清除自动隐藏定时器
            if hasattr(self, "_auto_hide_timer") and self._auto_hide_timer.isActive():
                self._auto_hide_timer.stop()
            # 调用边缘检测方法隐藏窗口
            self._check_edge_proximity()

    def _save_position(self):
        update_settings("float_position", "x", self.x())
        update_settings("float_position", "y", self.y())
        self.positionChanged.emit(self.x(), self.y())

    def _create_arrow_button(self, direction, x, y):
        """创建箭头按钮用于显示隐藏的窗口"""
        # 如果已存在箭头按钮，先删除
        if hasattr(self, "arrow_button") and self.arrow_button:
            self._delete_arrow_button()

        # 创建透明的可拖动QWidget作为容器
        # logger.debug(
        #     f"创建DraggableWidget箭头按钮，方向: {direction}, 位置: ({x}, {y})"
        # )
        self.arrow_widget = DraggableWidget()
        self.arrow_widget.setFixedSize(self._storage_btn_size)
        self.arrow_widget.move(x, y)
        self.arrow_widget.setFixedX(x)
        self.arrow_widget.setWindowFlags(
            Qt.FramelessWindowHint
            | Qt.WindowStaysOnTopHint
            | Qt.Tool
            | Qt.NoDropShadowWindowHint
        )
        if self._do_not_steal_focus:
            self.arrow_widget.setWindowFlags(
                self.arrow_widget.windowFlags() | Qt.WindowDoesNotAcceptFocus
            )
        try:
            self.arrow_widget.setAttribute(
                Qt.WA_ShowWithoutActivating, bool(self._do_not_steal_focus)
            )
        except Exception:
            pass

        # 设置容器透明
        self.arrow_widget.setAttribute(Qt.WA_TranslucentBackground)

        # 创建布局
        layout = QVBoxLayout()
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        self.arrow_button = QPushButton()
        self.arrow_button.setFixedSize(self._storage_btn_size)
        self.arrow_button.setAttribute(Qt.WA_TranslucentBackground)
        self.arrow_button.setFocusPolicy(Qt.NoFocus)
        # 设置收纳浮窗背景样式，使用主浮窗的透明度
        dark = self._is_floating_window_dark()
        if dark:
            self.arrow_button.setStyleSheet(
                f"background-color: rgba(32,32,32,{self._opacity}); color: rgba(255,255,255,200); border-radius: 6px; border: 1px solid rgba(255,255,255,20);"
            )
        else:
            self.arrow_button.setStyleSheet(
                f"background-color: rgba(255,255,255,{self._opacity}); color: rgba(0,0,0,180); border-radius: 6px; border: 1px solid rgba(0,0,0,12);"
            )

        # 根据指示器样式设置按钮内容
        if self._stick_indicator_style == 1:  # 文字模式
            self.arrow_button.setText("抽")
            self.arrow_button.setFont(self._font(self._storage_font_size))
        elif self._stick_indicator_style == 0:  # 图标模式
            try:
                icon = self._get_icon_for_floating_window(
                    "ic_fluent_people_20_filled", self._storage_icon_size
                )
                self.arrow_button.setIcon(icon)
                self.arrow_button.setIconSize(self._storage_icon_size)
            except Exception as e:
                logger.exception(f"加载图标失败: {e}")
                # 回退到箭头模式
                if direction == "right":
                    self.arrow_button.setText(">")
                else:
                    self.arrow_button.setText("<")
                self.arrow_button.setFont(self._font(self._storage_font_size))
        else:  # 箭头模式（默认）
            if direction == "right":
                self.arrow_button.setText(">")
            else:
                self.arrow_button.setText("<")
            self.arrow_button.setFont(self._font(self._storage_font_size))

        # 设置按钮点击事件
        self.arrow_button.clicked.connect(lambda: self._show_hidden_window(direction))

        # 为箭头按钮容器添加点击事件处理
        def handle_click():
            if (
                not hasattr(self.arrow_widget, "_was_dragging")
                or not self.arrow_widget._was_dragging
            ) and (
                not hasattr(self.arrow_widget, "_dragging")
                or not self.arrow_widget._dragging
            ):
                self._show_hidden_window(direction)

        # 修改容器的mouseReleaseEvent，处理点击事件
        original_release = self.arrow_widget.mouseReleaseEvent

        def new_mouse_release(event):
            if original_release:
                original_release(event)
            if event.button() == Qt.LeftButton:
                handle_click()

        self.arrow_widget.mouseReleaseEvent = new_mouse_release

        # 将按钮添加到布局中
        layout.addWidget(self.arrow_button, alignment=Qt.AlignCenter)

        # 设置容器的布局
        self.arrow_widget.setLayout(layout)

        # 确保容器显示在最前面
        self.arrow_widget.raise_()
        self.arrow_widget.show()
        # logger.debug(f"DraggableWidget箭头按钮已显示，位置: {self.arrow_widget.pos()}")

        # 将arrow_widget赋值给storage_window，供其他地方使用
        self.storage_window = self.arrow_widget
        self._apply_topmost_runtime()

    def _show_hidden_window(self, direction):
        """显示隐藏的窗口（带动画效果）"""
        # logger.debug(
        #     f"_show_hidden_window: 方向={direction}, 当前窗口位置=({self.x()}, {self.y()})"
        # )

        # 如果有正在进行的动画，先停止它
        if (
            hasattr(self, "animation")
            and self.animation.state() == QPropertyAnimation.Running
        ):
            self.animation.stop()

        # 获取屏幕尺寸
        screen = QApplication.primaryScreen().availableGeometry()
        # logger.debug(f"_show_hidden_window: 屏幕区域={screen}")

        # 获取窗口当前位置和尺寸
        window_width = self.width()
        window_height = self.height()

        # 获取箭头按钮容器的当前位置
        if hasattr(self, "arrow_widget") and self.arrow_widget:
            arrow_pos = self.arrow_widget.pos()
            arrow_height = self.arrow_widget.height()
        else:
            # 如果箭头按钮容器不存在，使用窗口的当前位置
            arrow_pos = self.pos()
            arrow_height = 30  # 默认箭头按钮高度

        # 计算窗口应该显示的位置，使窗口中心与箭头按钮中心对齐
        window_y = arrow_pos.y() + (arrow_height // 2) - (window_height // 2)

        # 确保窗口不会超出屏幕顶部和底部
        if window_y < screen.top():
            window_y = screen.top()
        elif window_y + window_height > screen.bottom():
            window_y = screen.bottom() - window_height

        # 创建动画效果
        self.animation = QPropertyAnimation(self, b"geometry")
        # 设置动画持续时间
        self.animation.setDuration(300)
        # 设置缓动曲线
        self.animation.setEasingCurve(QEasingCurve.Type.OutQuad)

        # 设置动画起始值（当前位置）
        self.animation.setStartValue(self.geometry())

        # 设置动画结束值（显示位置）
        if direction == "right":
            # 从左侧显示窗口
            end_rect = QRect(screen.left(), window_y, window_width, window_height)
        else:  # left
            # 从右侧显示窗口
            end_rect = QRect(
                screen.right() - window_width + 1, window_y, window_width, window_height
            )

        self.animation.setEndValue(end_rect)

        def on_animation_finished():
            self._retracted = False
            self.raise_()

            delay_ms = int(float(getattr(self, "custom_retract_time", 0) or 0) * 1000)
            if delay_ms <= 0:
                return

            if hasattr(self, "_auto_hide_timer") and self._auto_hide_timer.isActive():
                self._auto_hide_timer.stop()
            self._auto_hide_timer = QTimer(self)
            self._auto_hide_timer.setSingleShot(True)
            self._auto_hide_timer.timeout.connect(self._auto_hide_window)
            self._auto_hide_timer.start(delay_ms)

        try:
            self.animation.finished.connect(on_animation_finished)
        except Exception:
            pass

        # 启动动画
        self.animation.start()
        # logger.debug(
        #     f"_show_hidden_window: 动画已启动，目标位置=({end_rect.x()}, {end_rect.y()})"
        # )

        # 删除箭头按钮
        self._delete_arrow_button()

        # 不保存位置，保持贴边隐藏前的原始位置

        # 将窗口提升到最前面，但不激活窗口
        self.raise_()

    def _delete_arrow_button(self):
        """删除箭头按钮"""
        # 删除箭头按钮容器和按钮
        # 保存当前的容器引用，用于判断按钮的父对象
        parent_widget = getattr(self, "arrow_widget", None)

        # 先删除容器（如果存在），因为容器删除会同时删除其子对象
        if parent_widget:
            try:
                parent_widget.deleteLater()
            except Exception:
                pass
            finally:
                # 清除属性引用
                try:
                    self.arrow_widget = None
                except Exception:
                    pass

        # 再删除按钮，但避免在按钮已经被容器删除的情况下再次删除导致错误
        arrow_btn = getattr(self, "arrow_button", None)
        if arrow_btn:
            try:
                # 如果按钮的父对象不是原来的 container（parent_widget），说明容器删除可能已处理子对象
                btn_parent = arrow_btn.parent()
            except Exception:
                btn_parent = None
            if btn_parent is None or btn_parent is not parent_widget:
                try:
                    arrow_btn.deleteLater()
                except Exception:
                    pass
            # 最后清除按钮引用
            try:
                self.arrow_button = None
            except Exception:
                pass

    def _on_setting_changed(self, first, second, value):
        if first == "floating_window_management":
            if second == "startup_display_floating_window":
                if bool(value):
                    self.show()
                else:
                    self.hide()
                self.visibilityChanged.emit(bool(value))
            elif second == "extend_quick_draw_component":
                self._extend_quick_draw_component = bool(value)
                panel = getattr(self, "_quick_draw_extend_panel", None)
                if panel is not None:
                    try:
                        panel.close()
                    except Exception:
                        pass
                self.rebuild_ui()
            elif second == "floating_window_opacity":
                self._opacity = float(value or 0.8)
                self.setWindowOpacity(self._opacity)
            elif second == "floating_window_theme":
                self._floating_window_theme = int(value or 0)
                self.rebuild_ui()
            elif second == "floating_window_topmost_mode":
                mode = int(value or 0)
                self._topmost_mode = mode
                self._refresh_window_flags()
                self._apply_topmost_runtime()
            elif second == "floating_window_draggable":
                old_draggable = self._draggable
                self._draggable = bool(value)
                # 如果禁用拖拽，停止正在进行的拖拽操作
                if not self._draggable and old_draggable:
                    # 强制停止所有拖动相关的操作
                    self._dragging = False
                    self._storage_dragging = False  # 如果存在存储窗口拖动也要停止
                    self.setCursor(Qt.ArrowCursor)
                    self._drag_timer.stop()
                    # 确保结束任何可能的拖动状态，但不保存位置（因为这是设置更改，不是用户拖动操作）
                    self._stick_to_nearest_edge()
            elif second == "floating_window_placement":
                self._placement = int(value or 0)
                self.rebuild_ui()
            elif second == "floating_window_display_style":
                self._display_style = int(value or 0)
                self.rebuild_ui()
            elif second == "floating_window_stick_to_edge":
                # 同时更新旧的贴边功能和新的贴边隐藏功能
                self._stick_to_edge = bool(value)
                self.floating_window_stick_to_edge = bool(value)
                # 如果启用了功能，立即检查边缘
                if bool(value):
                    QTimer.singleShot(100, self._check_edge_proximity)
                else:
                    # 如果禁用了功能，删除收纳浮窗并展开窗口
                    self._delete_storage_window()
                    if self._retracted:
                        self._expand_from_edge()
            elif second == "floating_window_stick_to_edge_recover_seconds":
                self._retract_seconds = int(value or 0)
                self.custom_retract_time = int(value or 5)
            elif second == "floating_window_long_press_duration":
                self._long_press_ms = int(value or 100)
            elif second == "floating_window_stick_to_edge_display_style":
                self._stick_indicator_style = int(value or 0)
                self.custom_display_mode = int(value or 1)
            elif second == "floating_window_size":
                self._apply_size_setting(int(value or 1))
                self.rebuild_ui()
            elif second == "floating_window_button_control":
                self._buttons_spec = self._normalize_button_control_value(value)
                self.rebuild_ui()
            elif second == "do_not_steal_focus":
                try:
                    cur_main_visible = bool(self.isVisible())
                except Exception:
                    cur_main_visible = False
                try:
                    cur_arrow_visible = bool(
                        hasattr(self, "arrow_widget")
                        and self.arrow_widget
                        and self.arrow_widget.isVisible()
                    )
                except Exception:
                    cur_arrow_visible = False

                self._do_not_steal_focus = bool(value)
                self._apply_focus_mode()
                self._apply_topmost_runtime()

                # 恢复之前的可见性状态，过程中抑制可见性跟踪
                try:
                    prev = bool(getattr(self, "_suppress_visibility_tracking", False))
                    self._suppress_visibility_tracking = True
                    try:
                        if cur_main_visible and not self.isVisible():
                            try:
                                self.show()
                            except Exception:
                                pass
                        elif not cur_main_visible and self.isVisible():
                            try:
                                self.hide()
                            except Exception:
                                pass

                        if hasattr(self, "arrow_widget") and self.arrow_widget:
                            try:
                                if (
                                    cur_arrow_visible
                                    and not self.arrow_widget.isVisible()
                                ):
                                    self.arrow_widget.show()
                                elif (
                                    not cur_arrow_visible
                                    and self.arrow_widget.isVisible()
                                ):
                                    self.arrow_widget.hide()
                            except Exception:
                                pass
                    finally:
                        self._suppress_visibility_tracking = prev
                except Exception:
                    pass
            elif second == "hide_floating_window_on_foreground":
                self._hide_on_foreground_enabled = bool(value)
                self._apply_foreground_hide_timer_state()
            elif second == "hide_floating_window_on_foreground_window_titles":
                self._hide_on_foreground_title_raw = str(value or "")
                self._hide_on_foreground_titles = self._split_match_list(
                    self._hide_on_foreground_title_raw
                )
                QTimer.singleShot(0, self._check_foreground_hide)
            elif second == "hide_floating_window_on_foreground_process_names":
                self._hide_on_foreground_process_raw = str(value or "")
                self._hide_on_foreground_processes = self._split_match_list(
                    self._hide_on_foreground_process_raw
                )
                QTimer.singleShot(0, self._check_foreground_hide)
            # 当任何影响外观的设置改变时，重新应用主题样式
            self._apply_theme_style()
        elif first == "linkage_settings":
            # 处理联动设置变化（比如下课隐藏浮窗）
            if second == "hide_floating_window_on_class_end":
                try:
                    self._hide_on_class_end_enabled = bool(value)
                except Exception:
                    self._hide_on_class_end_enabled = False
                self._apply_class_hide_timer_state()
            # 其他 linkage 设置目前不在此处处理
            return
        elif first == "float_position":
            if second == "x":
                x = int(value or 0)
                nx, ny = self._clamp_to_screen(x, self.y())
                self.move(nx, ny)
            elif second == "y":
                y = int(value or 0)
                nx, ny = self._clamp_to_screen(self.x(), y)
                self.move(nx, ny)

    def _on_theme_changed(self):
        """当系统主题变更时调用"""
        if int(getattr(self, "_floating_window_theme", 0) or 0) == 0:
            self.rebuild_ui()
        self._apply_theme_style()

    def _map_button_control(self, idx):
        combos = [
            ["roll_call"],
            ["quick_draw"],
            ["lottery"],
            ["roll_call", "quick_draw"],
            ["roll_call", "lottery"],
            ["quick_draw", "lottery"],
            ["roll_call", "quick_draw", "lottery"],
            ["timer"],
            ["roll_call", "timer"],
            ["quick_draw", "timer"],
            ["lottery", "timer"],
            ["roll_call", "quick_draw", "timer"],
            ["roll_call", "lottery", "timer"],
            ["quick_draw", "lottery", "timer"],
            ["roll_call", "quick_draw", "lottery", "timer"],
        ]
        if idx < 0 or idx >= len(combos):
            return combos[0]
        return combos[idx]

    def _normalize_button_control_value(self, value):
        allowed = {"roll_call", "quick_draw", "lottery", "face_draw", "timer"}
        if isinstance(value, list):
            keys = []
            for v in value:
                if isinstance(v, str):
                    k = v.strip()
                    if k in allowed and k not in keys:
                        keys.append(k)
            if keys:
                return keys
            return self._map_button_control(0)

        try:
            idx = int(value or 0)
        except Exception:
            idx = 0
        return self._map_button_control(idx)


class DraggableWidget(QWidget):
    """可垂直拖动的窗口部件"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMouseTracking(True)
        self._dragging = False
        self._drag_start_y = 0
        self._original_y = 0
        self._fixed_x = 0  # 固定的x坐标
        self._press_start_time = 0  # 记录按下时间
        self._long_press_duration = 100  # 长按时间阈值（毫秒）
        self._long_press_timer = QTimer(self)  # 长按检测计时器
        self._long_press_timer.setSingleShot(True)
        self._long_press_timer.timeout.connect(self._on_long_press)
        self._long_press_triggered = False  # 标记是否触发长按
        self._keep_on_top_enabled = True
        self._init_keep_top_timer()

    def set_keep_on_top_enabled(self, enabled: bool):
        self._keep_on_top_enabled = bool(enabled)
        if hasattr(self, "keep_top_timer") and self.keep_top_timer:
            if self._keep_on_top_enabled:
                if not self.keep_top_timer.isActive():
                    self.keep_top_timer.start(100)
            else:
                if self.keep_top_timer.isActive():
                    self.keep_top_timer.stop()

    def is_draggable_enabled(self):
        """检查主窗口是否允许拖动"""
        # 获取主窗口实例
        main_window = None
        parent = self.parent()
        while parent:
            if hasattr(parent, "_draggable"):
                main_window = parent
                break
            parent = parent.parent()

        if main_window and hasattr(main_window, "_draggable"):
            return main_window._draggable
        return True  # 默认允许拖动

    def setFixedX(self, x):
        """设置固定的x坐标"""
        self._fixed_x = x

    def _init_keep_top_timer(self):
        """初始化保持置顶定时器
        优化：减少定时器间隔并提高置顶效率"""
        self.keep_top_timer = QTimer(self)
        self.keep_top_timer.timeout.connect(self._keep_window_on_top)
        if self._keep_on_top_enabled:
            self.keep_top_timer.start(100)

    def _keep_window_on_top(self):
        """保持窗口置顶
        优化：简化置顶逻辑，提高效率"""
        if not self._keep_on_top_enabled:
            return
        try:
            self.raise_()  # 将窗口提升到最前面
        except Exception as e:
            logger.exception(f"保持窗口置顶失败: {e}")

    def closeEvent(self, event):
        """窗口关闭事件 - 清理所有定时器资源"""
        # 停止保持置顶定时器
        if hasattr(self, "keep_top_timer") and self.keep_top_timer:
            if self.keep_top_timer.isActive():
                self.keep_top_timer.stop()
            self.keep_top_timer.deleteLater()
            self.keep_top_timer = None
            logger.info("DraggableWidget置顶定时器已停止并清理")

        # 停止长按检测计时器
        if hasattr(self, "_long_press_timer") and self._long_press_timer:
            if self._long_press_timer.isActive():
                self._long_press_timer.stop()
            self._long_press_timer.deleteLater()
            self._long_press_timer = None
            logger.info("DraggableWidget长按计时器已停止并清理")

        # 调用父类的closeEvent
        super().closeEvent(event)

    def _on_long_press(self):
        """长按触发事件"""
        self._long_press_triggered = True
        self.setCursor(Qt.ClosedHandCursor)

    def mousePressEvent(self, event):
        """鼠标按下事件"""
        if event.button() == Qt.LeftButton:
            self._press_start_time = QDateTime.currentMSecsSinceEpoch()
            self._drag_start_y = event.globalY()
            self._original_y = self.y()
            self._long_press_triggered = False
            # 重置拖动标志
            self._was_dragging = False
            # 启动长按检测计时器
            self._long_press_timer.start(self._long_press_duration)

    def mouseMoveEvent(self, event):
        """鼠标移动事件 - 只允许在y轴移动，x轴位置固定"""
        # 如果主窗口不允许拖动，停止任何正在进行的拖动
        if not self.is_draggable_enabled():
            if self._dragging:
                self._dragging = False
                self.setCursor(Qt.ArrowCursor)
            return

        if event.buttons() & Qt.LeftButton:
            current_time = QDateTime.currentMSecsSinceEpoch()
            if self._long_press_triggered or (
                current_time - self._press_start_time > 100
                and abs(event.globalY() - self._drag_start_y) > 5
            ):
                if not self._dragging:
                    self._dragging = True
                    self._was_dragging = True
                    self.setCursor(Qt.ClosedHandCursor)
                    if not self._long_press_triggered:
                        self._long_press_timer.stop()

                new_y = self._original_y + (event.globalY() - self._drag_start_y)
                self.move(self._fixed_x, new_y)

    def mouseReleaseEvent(self, event):
        """鼠标释放事件"""
        if event.button() == Qt.LeftButton:
            # 先保存拖动状态，然后再重置
            was_dragging = self._dragging
            self._dragging = False
            self._long_press_timer.stop()  # 停止长按计时器
            self.setCursor(Qt.ArrowCursor)

            # 如果没有触发长按且移动距离很小，则视为点击
            if (
                not self._long_press_triggered
                and abs(event.globalY() - self._drag_start_y) < 5
                and not was_dragging
            ):
                pass

    def enterEvent(self, event):
        """鼠标进入窗口区域时的事件处理，不再自动展开贴边隐藏的窗口"""
        # 调用父类的enterEvent
        super().enterEvent(event)

    def leaveEvent(self, event):
        """鼠标离开窗口区域时的事件处理，不再自动贴边隐藏窗口"""
        # 调用父类的leaveEvent
        super().leaveEvent(event)
