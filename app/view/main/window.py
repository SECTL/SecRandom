# ==================================================
# 导入库
# ==================================================
from loguru import logger
from PySide6.QtWidgets import QApplication, QWidget
from PySide6.QtGui import QIcon
from PySide6.QtCore import QTimer, QEvent, Signal, QThreadPool, QRunnable, Qt
from qfluentwidgets import FluentWindow, NavigationItemPosition

from app.common.IPC_URL.csharp_ipc_handler import CSharpIPCHandler
from app.common.shortcut import ShortcutManager
from app.tools.variable import (
    MINIMUM_WINDOW_SIZE,
    APP_INIT_DELAY,
    PRE_CLASS_RESET_INTERVAL_MS,
    RESIZE_TIMER_DELAY_MS,
    MAXIMIZE_RESTORE_DELAY_MS,
    EXIT_CODE_RESTART,
)
from app.tools.path_utils import get_data_path
from app.tools.personalised import get_theme_icon
from app.tools.config import clear_temp_draw_records
from app.Language.obtain_language import (
    get_content_name_async,
    readme_settings_async,
    update_settings,
)
from app.common.safety.verify_ops import require_and_run
from app.common.data.list import get_class_name_list
from app.view.main.quick_draw_animation import QuickDrawAnimation
from app.tools.list_specific_settings_access import (
    read_quick_draw_setting,
    get_safe_font_size_list_specific,
)
from app.view.main.camera_preview import CameraPreview
from app.page_building.main_window_page import (
    roll_call_page,
    lottery_page,
    history_page,
)
from app.view.tray.tray import Tray
from app.view.floating_window.levitation import LevitationWindow
from app.common.IPC_URL.url_command_handler import URLCommandHandler
from app.page_building.window_template import BackgroundLayer
from app.page_building.another_window import create_countdown_timer_window


# ==================================================
# 主窗口类
# ==================================================
class MainWindow(FluentWindow):
    """主窗口类
    程序的核心控制中心"""

    showSettingsRequested = Signal(str)  # 请求显示设置页面
    showSettingsPreviewRequested = Signal(str)  # 请求以预览模式显示设置页面
    showSettingsRequestedAbout = Signal()
    showFloatWindowRequested = Signal()
    showMainPageRequested = Signal(str)  # 请求显示主页面
    showTrayActionRequested = Signal(str)  # 请求执行托盘操作
    classIslandDataReceived = Signal(dict)  # 接收ClassIsland数据信号

    def __init__(self, float_window: LevitationWindow, url_handler_instance=None):
        self.resize_timer = None
        super().__init__()
        self.setObjectName("MainWindow")

        self.url_handler_instance = url_handler_instance

        self._initialize_variables()
        self._setup_timers()
        self._setup_shortcuts()
        self._setup_window_properties()
        self._setup_url_handler()
        self._setup_tray()
        self._setup_float_window(float_window)

        QTimer.singleShot(APP_INIT_DELAY, lambda: (self.createSubInterface()))

    def _initialize_variables(self):
        """初始化实例变量"""
        self.roll_call_page = None
        self.lottery_page = None
        self.camera_preview_page = None
        self.history_page = None
        self.settingsInterface = None
        self._quick_draw_roll_call_widget = None
        self._has_been_shown = False
        self.pre_class_reset_performed = False

    def _setup_timers(self):
        """设置定时器"""
        self.resize_timer = QTimer(self)
        self.resize_timer.setSingleShot(True)
        self.resize_timer.timeout.connect(
            lambda: self.save_window_size(self.width(), self.height())
        )

        self.pre_class_reset_timer = QTimer(self)
        self.pre_class_reset_timer.timeout.connect(self._check_pre_class_reset)

        QTimer.singleShot(1000, self._init_pre_class_reset)

        self._auto_backup_running = False
        self.backup_timer = QTimer(self)
        self.backup_timer.timeout.connect(self._check_and_run_auto_backup)
        self.backup_timer.start(60 * 60 * 1000)
        QTimer.singleShot(5000, self._check_and_run_auto_backup)

    def _check_and_run_auto_backup(self):
        if self._auto_backup_running:
            return
        try:
            from app.tools.backup_utils import (
                create_backup,
                get_auto_backup_max_count,
                is_backup_due,
                prune_backups,
                set_last_success_backup,
            )
        except Exception:
            return

        try:
            if not is_backup_due():
                return
        except Exception:
            return

        self._auto_backup_running = True

        def task():
            try:
                result = create_backup(kind="auto")
                set_last_success_backup(result)
                prune_backups(get_auto_backup_max_count())
                logger.info(f"自动备份完成: {result.file_path}")
            except Exception as e:
                logger.exception(f"自动备份失败: {e}")
            finally:
                self._auto_backup_running = False

        QThreadPool.globalInstance().start(QRunnable.create(task))

    def _setup_shortcuts(self):
        """设置快捷键管理器"""
        self.shortcut_manager = ShortcutManager(self)
        self._connect_shortcut_signals()
        self.shortcut_manager._init_shortcuts()
        self._setup_shortcut_settings_listener()

    def _setup_window_properties(self):
        """设置窗口属性"""
        self.setMinimumSize(MINIMUM_WINDOW_SIZE[0], MINIMUM_WINDOW_SIZE[1])
        self.setWindowTitle("SecRandom")
        self.setWindowIcon(
            QIcon(str(get_data_path("assets/icon", "secrandom-icon-paper.png")))
        )
        self._position_window()
        self._setup_general_settings_listener()
        self._apply_topmost_mode()
        self._setup_background_layer()

    def _setup_general_settings_listener(self):
        """设置通用设置监听器"""
        from app.tools.settings_access import get_settings_signals

        get_settings_signals().settingChanged.connect(self._on_general_setting_changed)

    def _on_general_setting_changed(self, first, second, value):
        """处理通用设置变更"""
        if first == "basic_settings" and second == "main_window_topmost_mode":
            self._apply_topmost_mode(value)
        if first == "background_management" and str(second or "").startswith(
            "main_window_background_"
        ):
            try:
                if getattr(self, "_background_layer", None) is not None:
                    self._background_layer.applyFromSettings()
            except Exception:
                pass

    def _setup_background_layer(self):
        if getattr(self, "_background_layer", None) is not None:
            try:
                self._background_layer.applyFromSettings()
            except Exception:
                pass
            return

        self._background_layer = BackgroundLayer(self, "main_window")
        self._background_layer.updateGeometryToParent()
        self._background_layer.lower()
        try:
            self._background_layer.applyFromSettings()
        except Exception:
            pass

    def _apply_topmost_mode(self, mode=None):
        """应用主窗口置顶模式"""
        if mode is None:
            mode = (
                readme_settings_async("basic_settings", "main_window_topmost_mode") or 0
            )

        mode = int(mode)
        self.setWindowFlag(Qt.WindowStaysOnTopHint, mode != 0)
        if self.isVisible():
            self.show()

    def _setup_url_handler(self):
        """设置URL处理器"""
        self.url_command_handler = URLCommandHandler(self)
        self.url_command_handler.showMainPageRequested.connect(
            self._handle_main_page_requested
        )
        self.url_command_handler.showSettingsRequested.connect(
            self.showSettingsRequested.emit
        )
        self.url_command_handler.showSettingsPreviewRequested.connect(
            self.showSettingsPreviewRequested.emit
        )
        self.url_command_handler.showTrayActionRequested.connect(
            self._handle_tray_action_requested
        )

    def _setup_tray(self):
        """设置系统托盘"""
        self.tray_icon = Tray(self)
        self.tray_icon.showSettingsRequested.connect(self.showSettingsRequested.emit)
        self.tray_icon.showSettingsRequestedAbout.connect(
            self.showSettingsRequestedAbout.emit
        )
        self.tray_icon.showFloatWindowRequested.connect(
            self.showFloatWindowRequested.emit
        )
        self.tray_icon.showTrayActionRequested.connect(
            self.showTrayActionRequested.emit
        )
        self.tray_icon.show_tray_icon()

    def _setup_float_window(self, float_window: LevitationWindow):
        """设置悬浮窗"""
        self.float_window = float_window
        self.showFloatWindowRequested.connect(self._toggle_float_window)
        self.showMainPageRequested.connect(self._handle_main_page_requested)
        self.showTrayActionRequested.connect(self._handle_tray_action_requested)
        self.float_window.rollCallRequested.connect(
            lambda: self._show_and_switch_to(self.roll_call_page)
        )
        self.float_window.quickDrawRequested.connect(self._handle_quick_draw)
        self.float_window.lotteryRequested.connect(
            lambda: self._show_and_switch_to(self.lottery_page)
        )
        self.float_window.faceDrawRequested.connect(
            lambda: self._show_and_switch_to(self.camera_preview_page)
        )
        self.float_window.timerRequested.connect(
            lambda: create_countdown_timer_window()
        )

    # ==================================================
    # IPC 服务器管理
    # ==================================================

    def restart_ipc_server(self, new_port: int):
        """重启IPC服务器

        Args:
            new_port: 新的端口号

        Returns:
            bool: 是否重启成功
        """
        logger.info(f"正在请求重启IPC服务器，新端口: {new_port}")

        if self.url_handler_instance and hasattr(
            self.url_handler_instance, "url_ipc_handler"
        ):
            url_handler = self.url_handler_instance

            if url_handler and hasattr(url_handler, "url_ipc_handler"):
                url_handler.url_ipc_handler.stop_ipc_server()
                logger.info("旧IPC服务器已停止")

                if url_handler.url_ipc_handler.start_ipc_server():
                    url_handler.url_ipc_handler.register_message_handler(
                        "url", url_handler._handle_ipc_url_message
                    )
                    logger.info("IPC服务器已重新启动")
                    return True
                else:
                    logger.exception("IPC服务器启动失败")
                    return False
            else:
                logger.exception("无法访问URLHandler实例")
                return False
        else:
            logger.exception("无法获取url_handler_instance")
            return False

    # ==================================================
    # 窗口定位与大小管理
    # ==================================================

    def _position_window(self):
        """窗口定位
        根据屏幕尺寸和用户设置自动计算最佳位置"""
        if self.resize_timer is not None:
            self.resize_timer.stop()

        is_maximized = readme_settings_async("window", "is_maximized")
        if is_maximized:
            pre_maximized_width = readme_settings_async("window", "pre_maximized_width")
            pre_maximized_height = readme_settings_async(
                "window", "pre_maximized_height"
            )
            self.resize(pre_maximized_width, pre_maximized_height)
            self._center_window()
        else:
            window_width = readme_settings_async("window", "width")
            window_height = readme_settings_async("window", "height")
            self.resize(window_width, window_height)
            self._center_window()

    def _center_window(self):
        """窗口居中
        将窗口移动到屏幕中心"""
        screen = QApplication.primaryScreen()
        desktop = screen.availableGeometry()
        w, h = desktop.width(), desktop.height()

        target_x = w // 2 - self.width() // 2
        target_y = h // 2 - self.height() // 2

        self.move(target_x, target_y)

    def save_window_size(self, width, height):
        """保存窗口大小
        记录当前窗口尺寸，下次启动时自动恢复

        Args:
            width: 窗口宽度
            height: 窗口高度
        """
        if not self._has_been_shown:
            return

        auto_save_enabled = readme_settings_async(
            "basic_settings", "auto_save_window_size"
        )

        if auto_save_enabled:
            if not self.isMaximized():
                if self.isVisible():
                    update_settings("window", "height", height)
                    update_settings("window", "width", width)

    def _show_main_window(self):
        is_maximized = readme_settings_async("window", "is_maximized")
        if self.isMinimized() or not self._has_been_shown:
            self._position_window()

        if is_maximized:
            self.showMaximized()
            return

        self.showNormal()

    def toggle_window(self):
        """切换窗口显示状态
        在显示和隐藏状态之间切换窗口，切换时自动激活点名界面"""
        if self.isMinimized():
            self._show_main_window()
            self.activateWindow()
            self.raise_()
            return

        if self.isVisible():
            self.hide()
            return

        self._show_main_window()
        self.activateWindow()
        self.raise_()

    # ==================================================
    # 窗口事件处理
    # ==================================================

    def closeEvent(self, event):
        """窗口关闭事件处理
        根据"后台驻留"设置决定是否真正关闭窗口

        Args:
            event: 关闭事件对象
        """
        resident = readme_settings_async("basic_settings", "background_resident")
        if resident:
            self.hide()
            event.ignore()

            is_maximized = self.isMaximized()
            update_settings("window", "is_maximized", is_maximized)
            if not is_maximized:
                self.save_window_size(self.width(), self.height())
        else:
            event.accept()

    def showEvent(self, event):
        """窗口显示事件处理
        标记窗口已经显示过

        Args:
            event: 显示事件对象
        """
        self._has_been_shown = True
        super().showEvent(event)

    def resizeEvent(self, event):
        """窗口大小变化事件处理
        检测窗口大小变化，启动尺寸记录倒计时，避免频繁IO操作

        Args:
            event: 大小变化事件对象
        """
        resize_timer = getattr(self, "resize_timer", None)
        if resize_timer is not None:
            resize_timer.start(RESIZE_TIMER_DELAY_MS)
        try:
            if getattr(self, "_background_layer", None) is not None:
                self._background_layer.updateGeometryToParent()
        except Exception:
            pass
        super().resizeEvent(event)

    def changeEvent(self, event):
        """窗口状态变化事件处理
        检测窗口最大化/恢复状态变化，保存正确的窗口大小

        Args:
            event: 状态变化事件对象
        """
        if event.type() == QEvent.Type.WindowStateChange:
            is_currently_maximized = self.isMaximized()
            was_maximized = readme_settings_async("window", "is_maximized")

            if is_currently_maximized != was_maximized:
                update_settings("window", "is_maximized", is_currently_maximized)

                if is_currently_maximized:
                    normal_geometry = self.normalGeometry()
                    update_settings(
                        "window", "pre_maximized_width", normal_geometry.width()
                    )
                    update_settings(
                        "window", "pre_maximized_height", normal_geometry.height()
                    )
                else:
                    pre_maximized_width = readme_settings_async(
                        "window", "pre_maximized_width"
                    )
                    pre_maximized_height = readme_settings_async(
                        "window", "pre_maximized_height"
                    )
                    QTimer.singleShot(
                        MAXIMIZE_RESTORE_DELAY_MS,
                        lambda: self.resize(pre_maximized_width, pre_maximized_height),
                    )

        super().changeEvent(event)

    # ==================================================
    # 界面创建与导航
    # ==================================================

    def createSubInterface(self):
        """创建子界面
        搭建子界面导航系统"""
        self.roll_call_page = roll_call_page(self)
        self.roll_call_page.setObjectName("roll_call_page")

        self.lottery_page = lottery_page(self)
        self.lottery_page.setObjectName("lottery_page")

        camera_sidebar_pos = readme_settings_async(
            "sidebar_management_window", "camera_preview_sidebar_position"
        )
        if camera_sidebar_pos is None or camera_sidebar_pos != 2:
            self.camera_preview_page = CameraPreview(self)
            self.camera_preview_page.setObjectName("camera_preview_page")
        else:
            self.camera_preview_page = None

        self.history_page = history_page(self)
        self.history_page.setObjectName("history_page")

        self.settingsInterface = QWidget(self)
        self.settingsInterface.setObjectName("settingsInterface")

        pages = [
            self.roll_call_page,
            self.lottery_page,
        ]
        if self.camera_preview_page:
            pages.append(self.camera_preview_page)
        pages.append(self.history_page)

        for page in pages:
            page.installEventFilter(self)

        self.initNavigation()

    def initNavigation(self):
        """初始化导航系统
        根据用户设置构建个性化菜单导航"""
        self._add_roll_call_navigation()
        self._add_lottery_navigation()
        self._add_camera_preview_navigation()
        self._add_history_navigation()
        self._add_settings_navigation()

    def _add_roll_call_navigation(self):
        """添加点名页面导航项"""
        roll_call_sidebar_pos = readme_settings_async(
            "sidebar_management_window", "roll_call_sidebar_position"
        )
        if roll_call_sidebar_pos is None or roll_call_sidebar_pos != 2:
            roll_call_position = (
                NavigationItemPosition.TOP
                if (roll_call_sidebar_pos is None or roll_call_sidebar_pos != 1)
                else NavigationItemPosition.BOTTOM
            )

            self.addSubInterface(
                self.roll_call_page,
                get_theme_icon("ic_fluent_people_20_filled"),
                get_content_name_async("roll_call", "title"),
                position=roll_call_position,
            )

    def _add_lottery_navigation(self):
        """添加抽奖页面导航项"""
        lottery_sidebar_pos = readme_settings_async(
            "sidebar_management_window", "lottery_sidebar_position"
        )
        if lottery_sidebar_pos is None or lottery_sidebar_pos != 2:
            lottery_position = (
                NavigationItemPosition.TOP
                if (lottery_sidebar_pos is None or lottery_sidebar_pos != 1)
                else NavigationItemPosition.BOTTOM
            )

            self.addSubInterface(
                self.lottery_page,
                get_theme_icon("ic_fluent_gift_20_filled"),
                get_content_name_async("lottery", "title"),
                position=lottery_position,
            )

    def _add_camera_preview_navigation(self):
        if self.camera_preview_page is None:
            return
        camera_sidebar_pos = readme_settings_async(
            "sidebar_management_window", "camera_preview_sidebar_position"
        )
        if camera_sidebar_pos is None or camera_sidebar_pos != 2:
            camera_position = (
                NavigationItemPosition.TOP
                if (camera_sidebar_pos is None or camera_sidebar_pos != 1)
                else NavigationItemPosition.BOTTOM
            )
            self.addSubInterface(
                self.camera_preview_page,
                get_theme_icon("ic_fluent_video_person_sparkle_20_filled"),
                get_content_name_async("camera_preview", "title"),
                position=camera_position,
            )

    def _add_history_navigation(self):
        """添加历史记录页面导航项"""
        history_sidebar_pos = readme_settings_async(
            "sidebar_management_window", "main_window_history"
        )
        if history_sidebar_pos is None or history_sidebar_pos != 2:
            history_position = (
                NavigationItemPosition.TOP
                if (history_sidebar_pos is None or history_sidebar_pos != 1)
                else NavigationItemPosition.BOTTOM
            )

            self.addSubInterface(
                self.history_page,
                get_theme_icon("ic_fluent_history_20_filled"),
                get_content_name_async("history", "title"),
                position=history_position,
            )

    def _add_settings_navigation(self):
        """添加设置页面导航项"""
        settings_icon_pos = readme_settings_async(
            "sidebar_management_window", "settings_icon"
        )
        if settings_icon_pos is None or settings_icon_pos != 2:
            settings_position = (
                NavigationItemPosition.BOTTOM
                if (settings_icon_pos == 1)
                else NavigationItemPosition.TOP
            )

            settings_item = self.addSubInterface(
                self.settingsInterface,
                get_theme_icon("ic_fluent_settings_20_filled"),
                get_content_name_async("settings", "title"),
                position=settings_position,
            )
            settings_item.clicked.connect(
                lambda: self.showSettingsRequested.emit("basicSettingsInterface")
            )
            settings_item.clicked.connect(lambda: self.switchTo(self.roll_call_page))

    # ==================================================
    # 窗口切换与显示
    # ==================================================

    def _toggle_float_window(self):
        """切换悬浮窗显示状态"""
        if hasattr(self.float_window, "toggle_user_requested_visible"):
            self.float_window.toggle_user_requested_visible()
            return

        if self.float_window.isVisible():
            self.float_window.hide()
        else:
            self.float_window.show()

    def _show_and_switch_to(self, page):
        """显示并切换到指定页面

        Args:
            page: 要切换到的页面对象
        """
        self._show_main_window()
        self.activateWindow()
        self.raise_()
        self.switchTo(page)

    def _handle_main_page_requested(self, page_name: str):
        """处理主页面请求

        Args:
            page_name: 页面名称 ('roll_call_page', 'lottery_page' 或 'main_window')
        """
        logger.info(
            f"MainWindow._handle_main_page_requested: 收到主页面请求: {page_name}"
        )
        if page_name == "main_window":
            logger.debug("MainWindow._handle_main_page_requested: 显示主窗口")
            self._show_main_window()
            self.raise_()
            self.activateWindow()
        elif (
            hasattr(self, f"{page_name}") and getattr(self, f"{page_name}") is not None
        ):
            logger.debug(
                f"MainWindow._handle_main_page_requested: 切换到页面: {page_name}"
            )
            self._show_and_switch_to(getattr(self, page_name))
        else:
            logger.warning(
                f"MainWindow._handle_main_page_requested: 请求的页面不存在: {page_name}"
            )

    def _handle_tray_action_requested(self, action: str):
        """处理托盘操作请求

        Args:
            action: 托盘操作类型 ('toggle_main_window', 'settings', 'float', 'restart', 'exit')
        """
        logger.debug(f"收到托盘操作请求: {action}")
        if action == "toggle_main_window":
            self.toggle_window()
        elif action == "settings":
            self.showSettingsRequested.emit("basicSettingsInterface")
        elif action == "float":
            self._toggle_float_window()
        elif action == "restart":
            require_and_run("restart", self, self.restart_app)
        elif action == "exit":
            require_and_run("exit", self, self.close_window_secrandom)
        else:
            logger.warning(f"未知的托盘操作: {action}")

    # ==================================================
    # 快捷键管理
    # ==================================================

    def _connect_shortcut_signals(self):
        """连接快捷键管理器的信号"""
        logger.debug("开始连接快捷键信号...")

        self.shortcut_manager.openRollCallPageRequested.connect(
            lambda: self._show_and_switch_to(self.roll_call_page)
        )
        logger.debug("快捷键信号已连接: openRollCallPageRequested")

        self.shortcut_manager.useQuickDrawRequested.connect(self._handle_quick_draw)
        logger.debug("快捷键信号已连接: useQuickDrawRequested")

        self.shortcut_manager.openLotteryPageRequested.connect(
            lambda: self._show_and_switch_to(self.lottery_page)
        )
        logger.debug("快捷键信号已连接: openLotteryPageRequested")

        self.shortcut_manager.increaseRollCallCountRequested.connect(
            self._handle_increase_roll_call_count
        )
        logger.debug("快捷键信号已连接: increaseRollCallCountRequested")

        self.shortcut_manager.decreaseRollCallCountRequested.connect(
            self._handle_decrease_roll_call_count
        )
        logger.debug("快捷键信号已连接: decreaseRollCallCountRequested")

        self.shortcut_manager.increaseLotteryCountRequested.connect(
            self._handle_increase_lottery_count
        )
        logger.debug("快捷键信号已连接: increaseLotteryCountRequested")

        self.shortcut_manager.decreaseLotteryCountRequested.connect(
            self._handle_decrease_lottery_count
        )
        logger.debug("快捷键信号已连接: decreaseLotteryCountRequested")

        self.shortcut_manager.startRollCallRequested.connect(
            self._handle_start_roll_call
        )
        logger.debug("快捷键信号已连接: startRollCallRequested")

        self.shortcut_manager.startLotteryRequested.connect(self._handle_start_lottery)
        logger.debug("快捷键信号已连接: startLotteryRequested")

        logger.debug("所有快捷键信号连接完成")

    def _handle_increase_roll_call_count(self):
        """处理增加点名人数快捷键"""
        roll_call_widget = self._get_roll_call_business_widget(
            getattr(self, "roll_call_page", None), allow_fallback=False
        )
        if roll_call_widget and hasattr(roll_call_widget, "update_count"):
            roll_call_widget.update_count(1)

    def _handle_decrease_roll_call_count(self):
        """处理减少点名人数快捷键"""
        roll_call_widget = self._get_roll_call_business_widget(
            getattr(self, "roll_call_page", None), allow_fallback=False
        )
        if roll_call_widget and hasattr(roll_call_widget, "update_count"):
            roll_call_widget.update_count(-1)

    def _handle_increase_lottery_count(self):
        """处理增加抽奖人数快捷键"""
        if hasattr(self, "lottery_page") and self.lottery_page:
            if (
                hasattr(self.lottery_page, "contentWidget")
                and self.lottery_page.contentWidget
            ):
                self.lottery_page.contentWidget.update_count(1)

    def _handle_decrease_lottery_count(self):
        """处理减少抽奖人数快捷键"""
        if hasattr(self, "lottery_page") and self.lottery_page:
            if (
                hasattr(self.lottery_page, "contentWidget")
                and self.lottery_page.contentWidget
            ):
                self.lottery_page.contentWidget.update_count(-1)

    def _handle_start_roll_call(self):
        """处理开始点名快捷键"""
        roll_call_widget = self._get_roll_call_business_widget(
            getattr(self, "roll_call_page", None), allow_fallback=False
        )
        if roll_call_widget and hasattr(roll_call_widget, "start_draw"):
            roll_call_widget.start_draw()

    def _handle_start_lottery(self):
        """处理开始抽奖快捷键"""
        if hasattr(self, "lottery_page") and self.lottery_page:
            if (
                hasattr(self.lottery_page, "contentWidget")
                and self.lottery_page.contentWidget
            ):
                self.lottery_page.contentWidget.start_draw()

    def _setup_shortcut_settings_listener(self):
        """设置快捷键设置监听器，监听快捷键设置变化"""
        from app.tools.settings_access import get_settings_signals

        settings_signals = get_settings_signals()
        settings_signals.settingChanged.connect(self._on_shortcut_settings_changed)

    def _on_shortcut_settings_changed(
        self, first_level_key: str, second_level_key: str, value
    ):
        """当设置发生变化时的处理函数

        Args:
            first_level_key: 第一级设置键
            second_level_key: 第二级设置键
            value: 新的设置值
        """
        if first_level_key == "shortcut_settings":
            if second_level_key == "enable_shortcut":
                logger.debug(f"快捷键启用状态变化: {value}")
                self.shortcut_manager.set_enabled(value)
            else:
                config_key = second_level_key
                shortcut_str = value if value else ""
                logger.debug(f"快捷键更新: {config_key} = {shortcut_str}")
                self.shortcut_manager.update_shortcut(config_key, shortcut_str)

    def cleanup_shortcuts(self):
        """清理快捷键"""
        if hasattr(self, "shortcut_manager"):
            self.shortcut_manager.cleanup()

    # ==================================================
    # 闪抽功能
    # ==================================================

    def _handle_quick_draw(self):
        """处理闪抽请求
        点击悬浮窗中的闪抽按钮时调用"""
        logger.debug("_handle_quick_draw: 收到闪抽请求")

        roll_call_page = getattr(self, "roll_call_page", None)
        if roll_call_page is None:
            logger.warning("_handle_quick_draw: 点名页面尚未创建，尝试使用闪抽后台组件")
        else:
            logger.debug("_handle_quick_draw: roll_call_page已创建")

        roll_call_widget = self._get_roll_call_business_widget(
            roll_call_page, allow_fallback=True
        )

        if not roll_call_widget:
            logger.error("_handle_quick_draw: 无法获取可用于闪抽的点名业务组件")
            return

        logger.debug("_handle_quick_draw: 成功获取roll_call_widget")

        original_settings = self._save_original_settings(roll_call_widget)

        try:
            self._apply_quick_draw_settings(roll_call_widget)
            self._execute_quick_draw_animation(roll_call_widget)
        finally:
            self._restore_original_settings(roll_call_widget, original_settings)

    def _get_roll_call_business_widget(self, roll_call_page, allow_fallback=False):
        """获取可执行闪抽的点名业务组件

        Args:
            roll_call_page: 点名页面对象
            allow_fallback: 是否允许使用闪抽后台组件

        Returns:
            点名业务组件对象或None
        """
        business_widget = self._find_roll_call_business_widget(roll_call_page)
        if business_widget:
            return business_widget

        if roll_call_page is None:
            return self._get_quick_draw_roll_call_widget() if allow_fallback else None

        roll_call_page.create_content()
        QApplication.processEvents()

        business_widget = self._find_roll_call_business_widget(roll_call_page)
        if business_widget:
            return business_widget

        if not allow_fallback:
            logger.warning("_get_roll_call_business_widget: 当前点名页面不是业务组件")
            return None

        logger.warning(
            "_get_roll_call_business_widget: 当前点名页面不是业务组件，改用闪抽后台组件"
        )
        return self._get_quick_draw_roll_call_widget()

    def _find_roll_call_business_widget(self, roll_call_page):
        """从点名页面中查找真正的点名业务组件"""
        if roll_call_page is None:
            return None

        for attr_name in ("roll_call_widget", "contentWidget"):
            widget = getattr(roll_call_page, attr_name, None)
            if self._is_roll_call_widget_usable(widget):
                return widget

        return None

    def _is_roll_call_widget_usable(self, widget):
        """判断组件是否具备闪抽所需的点名业务能力"""
        if widget is None:
            return False

        try:
            # HTML 主题包装器只负责展示，不承载点名业务控件。
            if widget.property("theme_html_wrapper"):
                return False

            required_attrs = (
                "list_combobox",
                "range_combobox",
                "gender_combobox",
                "count_label",
                "many_count_label",
                "manager",
                "result_grid",
            )
            required_methods = (
                "on_class_changed",
                "on_filter_changed",
                "_update_remaining_list_delayed",
                "play_voice_result",
            )

            return all(hasattr(widget, attr) for attr in required_attrs) and all(
                callable(getattr(widget, method, None)) for method in required_methods
            )
        except RuntimeError:
            return False

    def _get_quick_draw_roll_call_widget(self):
        """获取闪抽使用的后台点名组件"""
        if (
            self._quick_draw_roll_call_widget is not None
            and self._is_roll_call_widget_usable(self._quick_draw_roll_call_widget)
        ):
            return self._quick_draw_roll_call_widget

        try:
            from app.view.main.roll_call import roll_call

            widget = roll_call(self)
            widget.setObjectName("quick_draw_roll_call_widget")
            widget.hide()
            self._quick_draw_roll_call_widget = widget
            QApplication.processEvents()
            return widget
        except Exception as e:
            logger.exception(f"创建闪抽默认点名组件失败: {e}")
            return None

    def _save_original_settings(self, roll_call_widget):
        """保存原始设置

        Args:
            roll_call_widget: 点名组件对象

        Returns:
            dict: 原始设置字典
        """
        return {
            "count": getattr(roll_call_widget, "current_count", 1),
            "list_index": roll_call_widget.list_combobox.currentIndex(),
            "range_index": roll_call_widget.range_combobox.currentIndex(),
            "gender_index": roll_call_widget.gender_combobox.currentIndex(),
        }

    def _apply_quick_draw_settings(self, roll_call_widget):
        """应用闪抽设置

        Args:
            roll_call_widget: 点名组件对象
        """
        roll_call_widget.current_count = 1
        roll_call_widget.count_label.setText("1")

        list_name = self._get_effective_quick_draw_class_name()
        if roll_call_widget.list_combobox.count() > 0 and list_name:
            list_index = roll_call_widget.list_combobox.findText(list_name)
            if list_index >= 0:
                roll_call_widget.list_combobox.setCurrentIndex(list_index)
                roll_call_widget.on_class_changed()

        if roll_call_widget.range_combobox.count() > 0:
            roll_call_widget.range_combobox.setCurrentIndex(0)

        if roll_call_widget.gender_combobox.count() > 0:
            roll_call_widget.gender_combobox.setCurrentIndex(0)

        roll_call_widget.on_filter_changed()
        QApplication.processEvents()

    def _execute_quick_draw_animation(self, roll_call_widget):
        """执行闪抽动画

        Args:
            roll_call_widget: 点名组件对象
        """
        list_name = self._get_effective_quick_draw_class_name()
        quick_draw_settings = {
            "draw_mode": read_quick_draw_setting(list_name, "draw_mode"),
            "half_repeat": read_quick_draw_setting(list_name, "half_repeat"),
            "font_size": get_safe_font_size_list_specific(
                "quick_draw_settings",
                "quick_draw_list_specific_settings",
                list_name,
                "font_size",
            ),
            "display_format": read_quick_draw_setting(list_name, "display_format"),
            "display_style": read_quick_draw_setting(list_name, "display_style"),
            "show_tags": read_quick_draw_setting(list_name, "show_tags"),
            "animation": read_quick_draw_setting(list_name, "animation"),
            "animation_interval": read_quick_draw_setting(
                list_name, "animation_interval"
            ),
            "autoplay_count": read_quick_draw_setting(list_name, "autoplay_count"),
            "animation_color_theme": read_quick_draw_setting(
                list_name, "animation_color_theme"
            ),
            "student_image": read_quick_draw_setting(list_name, "student_image"),
            "show_random": read_quick_draw_setting(list_name, "show_random"),
            "default_class": list_name,
        }

        quick_draw_animation = QuickDrawAnimation(roll_call_widget)
        quick_draw_animation.execute_quick_draw(quick_draw_settings)

    def _get_effective_quick_draw_class_name(self):
        """获取闪抽实际使用的班级名称"""
        list_name = readme_settings_async("quick_draw_settings", "default_class")
        class_names = get_class_name_list()

        try:
            extend_enabled = bool(
                readme_settings_async(
                    "floating_window_management", "extend_quick_draw_component"
                )
            )
        except Exception:
            extend_enabled = False

        if extend_enabled:
            saved_class_name = str(
                readme_settings_async(
                    "floating_window_management", "quick_draw_class_name"
                )
                or ""
            )
            if saved_class_name and saved_class_name in get_class_name_list():
                return saved_class_name

        if list_name in class_names:
            return list_name

        return class_names[0] if class_names else ""

    def _restore_original_settings(self, roll_call_widget, original_settings):
        """恢复原始设置

        Args:
            roll_call_widget: 点名组件对象
            original_settings: 原始设置字典
        """
        roll_call_widget.current_count = original_settings["count"]
        roll_call_widget.count_label.setText(str(original_settings["count"]))

        if roll_call_widget.list_combobox.count() > 0:
            roll_call_widget.list_combobox.setCurrentIndex(
                original_settings["list_index"]
            )
            roll_call_widget.on_class_changed()

        if roll_call_widget.range_combobox.count() > 0:
            roll_call_widget.range_combobox.setCurrentIndex(
                original_settings["range_index"]
            )

        if roll_call_widget.gender_combobox.count() > 0:
            roll_call_widget.gender_combobox.setCurrentIndex(
                original_settings["gender_index"]
            )

        roll_call_widget.on_filter_changed()
        QApplication.processEvents()

    # ==================================================
    # 课前重置功能
    # ==================================================

    def _check_pre_class_reset(self):
        """每秒检测课前重置条件"""
        try:
            if self.pre_class_reset_performed:
                self.pre_class_reset_timer.stop()
                return

            pre_class_reset_time = readme_settings_async(
                "linkage_settings", "pre_class_reset_time", 120
            )

            data_source = readme_settings_async("linkage_settings", "data_source", 0)

            on_class_left_time = self._get_on_class_left_time(data_source)

            if on_class_left_time is None:
                self.pre_class_reset_timer.stop()
                return

            if on_class_left_time > 0 and on_class_left_time <= pre_class_reset_time:
                logger.info(f"距离上课还有 {on_class_left_time} 秒，执行课前重置")
                self._perform_pre_class_reset()
                self.pre_class_reset_performed = True
                self.pre_class_reset_timer.stop()

        except Exception as e:
            logger.exception(f"检测课前重置时出错: {e}")

    def _get_on_class_left_time(self, data_source):
        """根据数据源获取距离上课时间

        Args:
            data_source: 数据源类型 (0: 不使用, 1: CSES, 2: ClassIsland)

        Returns:
            int: 距离上课的秒数，如果不需要重置则返回None
        """
        if data_source == 2:
            return CSharpIPCHandler.instance().get_on_class_left_time()
        elif data_source == 1:
            from app.common.extraction.extract import _get_seconds_to_next_class

            return _get_seconds_to_next_class()
        else:
            return None

    def _perform_pre_class_reset(self):
        """执行课前重置操作"""
        try:
            self._clear_roll_call_result()
            self._clear_lottery_result()
            self._clear_temp_folder()
            self._refresh_page_displays()
            logger.info("课前重置完成")

        except Exception as e:
            logger.exception(f"执行课前重置时出错: {e}")

    def _clear_roll_call_result(self):
        """清除点名页面结果"""
        if self.roll_call_page and hasattr(self.roll_call_page, "clear_result"):
            self.roll_call_page.clear_result()
            logger.info("已清除点名页面结果")

    def _clear_lottery_result(self):
        """清除抽奖页面结果"""
        if self.lottery_page and hasattr(self.lottery_page, "clear_result"):
            self.lottery_page.clear_result()
            logger.info("已清除抽奖页面结果")

    def _clear_temp_folder(self):
        """清除TEMP文件夹"""
        try:
            deleted_count = clear_temp_draw_records()
            if deleted_count:
                logger.info("已清除抽取临时记录文件")
        except Exception as e:
            logger.error(f"清除抽取临时记录失败: {e}")

    def _refresh_page_displays(self):
        """刷新页面显示"""
        if self.roll_call_page and hasattr(
            self.roll_call_page, "update_many_count_label"
        ):
            self.roll_call_page.update_many_count_label()
            logger.debug("已刷新点名页面剩余人数显示")

        if self.lottery_page and hasattr(self.lottery_page, "update_many_count_label"):
            self.lottery_page.update_many_count_label()
            logger.debug("已刷新抽奖页面显示")

    def _init_pre_class_reset(self):
        """初始化课前重置功能"""
        try:
            logger.debug("初始化课前重置功能")
            pre_class_reset_enabled = readme_settings_async(
                "linkage_settings", "pre_class_reset_enabled", False
            )
            if not pre_class_reset_enabled:
                return

            data_source = readme_settings_async("linkage_settings", "data_source", 0)

            if data_source == 0:
                logger.debug("未启用数据源，不启动课前重置定时器")
                return

            if not self.pre_class_reset_timer.isActive():
                self.pre_class_reset_timer.start(PRE_CLASS_RESET_INTERVAL_MS)
                if data_source == 2:
                    logger.debug("课前重置定时器已启动（ClassIsland模式）")
                else:
                    logger.debug("课前重置定时器已启动（CSES模式）")

        except Exception as e:
            logger.exception(f"初始化课前重置功能时出错: {e}")

    # ==================================================
    # 应用程序退出与重启
    # ==================================================

    def restart_app(self):
        """重启应用程序（跨平台支持）
        执行安全验证后重启程序，清理所有资源"""
        logger.info("正在发起重启请求...")

        if self.pre_class_reset_timer.isActive():
            self.pre_class_reset_timer.stop()

        self.cleanup_shortcuts()

        # 使用 EXIT_CODE_RESTART 退出码来触发重启
        # main.py 中的 handle_exit() 函数会检测此退出码并执行重启逻辑
        logger.info("正在退出以触发重启流程...")
        QApplication.exit(EXIT_CODE_RESTART)

    def close_window_secrandom(self):
        """关闭窗口
        执行安全验证后关闭程序，释放所有资源"""
        logger.info("程序正在退出 (close_window_secrandom)...")

        self._stop_pre_class_reset_timer()
        self._cleanup_shortcuts()
        self._stop_ipc_client()
        self._close_all_windows()
        self._close_main_window()
        self._quit_application()

    def _stop_pre_class_reset_timer(self):
        """停止课前重置定时器"""
        if (
            hasattr(self, "pre_class_reset_timer")
            and self.pre_class_reset_timer.isActive()
        ):
            self.pre_class_reset_timer.stop()
            logger.debug("课前重置定时器已停止")

    def _cleanup_shortcuts(self):
        """快速清理快捷键"""
        self.cleanup_shortcuts()
        logger.debug("快捷键已清理")

    def _stop_ipc_client(self):
        """停止IPC客户端"""
        try:
            CSharpIPCHandler.instance().stop_ipc_client()
            logger.debug("C# IPC 停止请求已发出")
        except Exception as e:
            logger.exception(f"停止 IPC 客户端失败: {e}")

    def _close_all_windows(self):
        """显式关闭所有顶层窗口（包括悬浮窗、设置窗口等）"""
        try:
            top_level_widgets = QApplication.topLevelWidgets()
            logger.debug(f"正在关闭所有顶层窗口，共 {len(top_level_widgets)} 个")
            for widget in top_level_widgets:
                if widget != self:
                    logger.debug(f"正在关闭窗口: {widget.objectName() or widget}")
                    widget.close()
                    if hasattr(widget, "hide"):
                        widget.hide()
        except Exception as e:
            logger.exception(f"关闭其他窗口时出错: {e}")

    def _close_main_window(self):
        """关闭主窗口"""
        logger.debug("正在关闭主窗口...")
        self.close()

    def _quit_application(self):
        """请求退出应用程序"""
        logger.info("已发出 QApplication.quit() 请求")
        QApplication.quit()
