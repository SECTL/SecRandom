from PySide6.QtCore import QTimer
from loguru import logger

from app.tools.settings_default import manage_settings_file
from app.tools.config import remove_record
from app.tools.settings_access import get_bool_setting, get_setting
from app.tools.update_utils import check_for_updates_on_startup
from app.tools.variable import APP_INIT_DELAY
from app.core.font_manager import (
    apply_font_settings,
    ensure_application_font_point_size,
)
from app.core.window_manager import WindowManager
from app.core.utils import safe_execute
from app.common.history.file_utils import load_history_data, get_all_history_names


def calculate_total_draw_counts():
    """计算总抽取次数

    Returns:
        tuple: (总抽取次数, 点名总次数, 抽奖总次数)
    """
    roll_call_total = 0
    for class_name in get_all_history_names("roll_call"):
        data = load_history_data("roll_call", class_name)
        roll_call_total += int(data.get("total_rounds", 0) or 0)

    lottery_total = 0
    for pool_name in get_all_history_names("lottery"):
        data = load_history_data("lottery", pool_name)
        lotterys = data.get("lotterys", {})
        if not isinstance(lotterys, dict):
            continue
        draw_times = set()
        for entry in lotterys.values():
            if not isinstance(entry, dict):
                continue
            hist = entry.get("history", [])
            if not isinstance(hist, list):
                continue
            for record in hist:
                if not isinstance(record, dict):
                    continue
                draw_time = record.get("draw_time")
                if draw_time:
                    draw_times.add(draw_time)
        lottery_total += len(draw_times)

    total_draw_count = roll_call_total + lottery_total
    return total_draw_count, roll_call_total, lottery_total


class AppInitializer:
    """应用程序初始化器，负责协调所有初始化任务"""

    def __init__(self, window_manager: WindowManager) -> None:
        """初始化应用初始化器

        Args:
            window_manager: 窗口管理器实例
        """
        self.window_manager = window_manager

    def initialize(self) -> None:
        """初始化应用程序"""
        self._manage_settings_file()
        self._schedule_initialization_tasks()
        logger.debug("应用初始化调度已启动，主窗口将在延迟后创建")

    def _manage_settings_file(self) -> None:
        """管理设置文件，确保其存在且完整"""
        manage_settings_file()

    def _schedule_initialization_tasks(self) -> None:
        """调度所有初始化任务"""
        self._apply_font_settings()
        self._load_theme()
        self._load_theme_color()
        self._clear_restart_record()
        self._check_updates()
        self._warmup_face_detector_devices()
        self._create_main_window()

    def _load_theme(self) -> None:
        """加载主题设置"""
        QTimer.singleShot(
            APP_INIT_DELAY,
            lambda: safe_execute(self._apply_theme, error_message="加载主题失败"),
        )

    def _apply_theme(self) -> None:
        """应用主题设置"""
        from qfluentwidgets import setTheme, Theme

        theme = get_setting("basic_settings", "theme")
        if theme == "DARK":
            setTheme(Theme.DARK)
        elif theme == "AUTO":
            setTheme(Theme.AUTO)
        else:
            setTheme(Theme.LIGHT)
        ensure_application_font_point_size()

    def _load_theme_color(self) -> None:
        """加载主题颜色"""
        from qfluentwidgets import setThemeColor

        QTimer.singleShot(
            APP_INIT_DELAY,
            lambda: safe_execute(
                lambda: setThemeColor(get_setting("basic_settings", "theme_color")),
                error_message="加载主题颜色失败",
            ),
        )

    def _clear_restart_record(self) -> None:
        """清除重启记录"""
        QTimer.singleShot(
            APP_INIT_DELAY,
            lambda: safe_execute(
                lambda: remove_record("", "", "", "restart"),
                error_message="清除重启记录失败",
            ),
        )

    def _check_updates(self) -> None:
        """检查是否需要安装更新"""
        QTimer.singleShot(
            APP_INIT_DELAY,
            lambda: safe_execute(
                lambda: check_for_updates_on_startup(None), error_message="检查更新失败"
            ),
        )

    def _create_main_window(self) -> None:
        """创建主窗口实例（但不自动显示）"""
        guide_completed = get_bool_setting("basic_settings", "guide_completed", False)
        init_delay = 0 if not guide_completed else APP_INIT_DELAY
        QTimer.singleShot(
            init_delay,
            lambda: safe_execute(
                self.window_manager.create_main_window, error_message="创建主窗口失败"
            ),
        )

    def _apply_font_settings(self) -> None:
        """应用字体设置"""
        guide_completed = get_bool_setting("basic_settings", "guide_completed", False)
        init_delay = 0 if not guide_completed else APP_INIT_DELAY
        QTimer.singleShot(
            init_delay,
            lambda: safe_execute(apply_font_settings, error_message="应用字体设置失败"),
        )

    def _warmup_face_detector_devices(self) -> None:
        guide_completed = get_bool_setting("basic_settings", "guide_completed", False)
        init_delay = 1500 if not guide_completed else APP_INIT_DELAY + 1500
        QTimer.singleShot(
            init_delay,
            lambda: safe_execute(
                self._do_warmup_face_detector_devices,
                error_message="预热摄像头设备失败",
            ),
        )

    def _do_warmup_face_detector_devices(self) -> None:
        from app.common.camera_preview_backend import warmup_camera_devices_async

        warmup_camera_devices_async(force_refresh=True)
