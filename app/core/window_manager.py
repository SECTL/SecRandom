import os
from typing import Optional, Callable, TYPE_CHECKING
from loguru import logger
from PySide6.QtCore import QTimer
from app.tools.settings_access import get_bool_setting
from app.core.utils import safe_execute, safe_close_window, activate_window

if TYPE_CHECKING:
    from PySide6.QtWidgets import QWidget


app_start_time: float = 0.0
pending_uiaccess_restart_after_show: bool = False
_pending_uiaccess_restart_consumed: bool = False


class WindowManager:
    """窗口管理器，负责创建和管理所有窗口实例"""

    def __init__(self) -> None:
        """初始化窗口管理器"""
        self.main_window: Optional["QWidget"] = None
        self.settings_window: Optional["QWidget"] = None
        self.float_window: Optional["QWidget"] = None
        self.guide_window: Optional["QWidget"] = None
        self.url_handler: Optional = None

    def set_url_handler(self, url_handler) -> None:
        """设置URL处理器

        Args:
            url_handler: URL处理器实例
        """
        self.url_handler = url_handler

    def create_main_window(self) -> None:
        """创建主窗口实例"""
        guide_completed = get_bool_setting("basic_settings", "guide_completed", False)
        if not guide_completed:
            self.show_guide_window()
            return

        success, _ = safe_execute(
            self._create_main_window_impl, error_message="创建主窗口失败"
        )
        if not success:
            logger.warning("主窗口创建失败")

    def _create_main_window_impl(self) -> None:
        """创建主窗口的实现"""
        from app.view.main.window import MainWindow

        self.create_float_window()
        self.main_window = MainWindow(
            float_window=self.float_window, url_handler_instance=self.url_handler
        )

        self._connect_main_window_signals()
        self._configure_main_window_display()
        self._connect_url_handler_signals()
        self._log_startup_time()

    def _connect_main_window_signals(self) -> None:
        """连接主窗口信号"""
        from app.common.safety.verify_ops import (
            should_require_password,
            require_and_run,
        )

        self.main_window.showSettingsRequested.connect(
            self._create_settings_request_handler(
                should_require_password, require_and_run
            )
        )
        if hasattr(self.main_window, "showSettingsPreviewRequested"):
            self.main_window.showSettingsPreviewRequested.connect(
                lambda page_name: self.show_settings_window(page_name, is_preview=True)
            )
        self.main_window.showSettingsRequestedAbout.connect(
            self.show_settings_window_about
        )
        self.main_window.showFloatWindowRequested.connect(self.show_float_window)
        if hasattr(self.main_window, "url_command_handler"):
            try:
                if hasattr(
                    self.main_window.url_command_handler, "rollCallActionRequested"
                ):
                    self.main_window.url_command_handler.rollCallActionRequested.connect(
                        self._handle_roll_call_action
                    )
                if hasattr(
                    self.main_window.url_command_handler, "lotteryActionRequested"
                ):
                    self.main_window.url_command_handler.lotteryActionRequested.connect(
                        self._handle_lottery_action
                    )
                if hasattr(
                    self.main_window.url_command_handler, "windowActionRequested"
                ):
                    self.main_window.url_command_handler.windowActionRequested.connect(
                        self._handle_window_action
                    )
            except Exception as e:
                logger.exception(
                    "连接主窗口 URLCommandHandler 控制信号失败（已忽略）: {}", e
                )

    def _create_settings_request_handler(
        self, should_require_password_func, require_and_run_func
    ) -> Callable[[str], None]:
        """创建设置请求处理器

        Args:
            should_require_password_func: 检查是否需要密码的函数
            require_and_run_func: 运行验证的函数

        Returns:
            处理函数
        """

        def handle_show_settings_requested(
            page_name: str = "basicSettingsInterface",
        ) -> None:
            """处理显示设置请求，添加验证逻辑"""
            if should_require_password_func("open_settings"):
                logger.debug(f"打开设置页面需要验证：{page_name}")
                self._show_settings_with_verification(page_name, require_and_run_func)
            else:
                logger.debug(f"打开设置页面无需验证：{page_name}")
                self.show_settings_window(page_name, is_preview=False)

        return handle_show_settings_requested

    def _show_settings_with_verification(
        self, page_name: str, require_and_run_func
    ) -> None:
        """显示设置窗口并验证

        Args:
            page_name: 页面名称
            require_and_run_func: 验证函数
        """

        def on_verified() -> None:
            """验证通过后，正常打开设置页面"""
            self.show_settings_window(page_name, is_preview=False)

        def on_preview() -> None:
            """点击预览按钮后，以预览模式打开设置页面"""
            self.show_settings_window(page_name, is_preview=True)

        require_and_run_func(
            "open_settings", self.main_window, on_verified, on_preview=on_preview
        )

    def _configure_main_window_display(self) -> None:
        """配置主窗口显示"""
        show_startup_window = get_bool_setting(
            "basic_settings", "show_startup_window", True
        )
        is_maximized = get_bool_setting("window", "is_maximized", False)
        if show_startup_window:
            if is_maximized:
                from app.tools.variable import APP_INIT_DELAY

                def show_main_window():
                    try:
                        self.main_window.showMaximized()
                    finally:
                        self._schedule_main_window_shown_tasks()

                QTimer.singleShot(APP_INIT_DELAY, show_main_window)
            else:
                self.main_window.show()
                self._schedule_main_window_shown_tasks()

        startup_display_float = get_bool_setting(
            "floating_window_management", "startup_display_floating_window", False
        )
        if startup_display_float:
            if self.float_window is None:
                self.create_float_window()
            if self.float_window is not None and not self.float_window.isVisible():
                self.float_window.show()

    def _schedule_main_window_shown_tasks(self) -> None:
        try:
            QTimer.singleShot(0, self._handle_main_window_shown)
        except Exception:
            pass

    def _handle_main_window_shown(self) -> None:
        global pending_uiaccess_restart_after_show
        global _pending_uiaccess_restart_consumed

        if not bool(pending_uiaccess_restart_after_show):
            return
        if bool(_pending_uiaccess_restart_consumed):
            return
        _pending_uiaccess_restart_consumed = True

        try:
            import platform

            if platform.system() != "Windows":
                return
        except Exception:
            return

        try:
            from PySide6.QtWidgets import QApplication
            from app.tools.variable import EXIT_CODE_RESTART
            from app.common.windows.uiaccess import (
                UIACCESS_RESTART_ENV,
                is_uiaccess_process,
            )

            if bool(is_uiaccess_process()):
                return
            os.environ[str(UIACCESS_RESTART_ENV)] = "1"
            QApplication.exit(EXIT_CODE_RESTART)
        except Exception as e:
            logger.debug("主窗口显示后触发 UIAccess 重启失败（已忽略）: {}", e)

    def _connect_url_handler_signals(self) -> None:
        """连接URL处理器信号"""
        if not self.url_handler:
            return

        self.url_handler.showMainPageRequested.connect(
            self.main_window._handle_main_page_requested
        )
        self.url_handler.showTrayActionRequested.connect(
            lambda action: self.main_window._handle_tray_action_requested(action)
        )
        self.url_handler.showSettingsRequested.connect(self.show_settings_window)
        if hasattr(self.url_handler, "showSettingsPreviewRequested"):
            self.url_handler.showSettingsPreviewRequested.connect(
                lambda page_name: self.show_settings_window(page_name, is_preview=True)
            )
        if hasattr(self.url_handler, "rollCallActionRequested"):
            self.url_handler.rollCallActionRequested.connect(
                self._handle_roll_call_action
            )
        if hasattr(self.url_handler, "lotteryActionRequested"):
            self.url_handler.lotteryActionRequested.connect(self._handle_lottery_action)
        if hasattr(self.url_handler, "windowActionRequested"):
            self.url_handler.windowActionRequested.connect(self._handle_window_action)

    def _ensure_main_window_pages_created(self) -> None:
        if self.main_window is None:
            return
        try:
            roll_call_page = getattr(self.main_window, "roll_call_page", None)
            lottery_page = getattr(self.main_window, "lottery_page", None)
            if roll_call_page is not None or lottery_page is not None:
                return
            if hasattr(self.main_window, "createSubInterface"):
                self.main_window.createSubInterface()
        except Exception as e:
            logger.exception("确保主窗口页面创建失败（已忽略）: {}", e)

    def _get_roll_call_widget(self):
        if self.main_window is None:
            return None
        try:
            if hasattr(self.main_window, "_get_roll_call_widget"):
                return self.main_window._get_roll_call_widget(
                    getattr(self.main_window, "roll_call_page", None)
                )
        except Exception:
            pass

        roll_call_page = getattr(self.main_window, "roll_call_page", None)
        if roll_call_page is None:
            return None
        if (
            hasattr(roll_call_page, "roll_call_widget")
            and roll_call_page.roll_call_widget
        ):
            return roll_call_page.roll_call_widget
        if hasattr(roll_call_page, "contentWidget") and roll_call_page.contentWidget:
            return roll_call_page.contentWidget
        try:
            from PySide6.QtWidgets import QApplication

            roll_call_page.create_content()
            QApplication.processEvents()
        except Exception:
            return None
        if (
            hasattr(roll_call_page, "roll_call_widget")
            and roll_call_page.roll_call_widget
        ):
            return roll_call_page.roll_call_widget
        if hasattr(roll_call_page, "contentWidget") and roll_call_page.contentWidget:
            return roll_call_page.contentWidget
        return None

    def _get_lottery_widget(self):
        if self.main_window is None:
            return None
        lottery_page = getattr(self.main_window, "lottery_page", None)
        if lottery_page is None:
            return None
        if hasattr(lottery_page, "lottery_widget") and lottery_page.lottery_widget:
            return lottery_page.lottery_widget
        if hasattr(lottery_page, "contentWidget") and lottery_page.contentWidget:
            return lottery_page.contentWidget
        try:
            from PySide6.QtWidgets import QApplication

            lottery_page.create_content()
            QApplication.processEvents()
        except Exception:
            return None
        if hasattr(lottery_page, "lottery_widget") and lottery_page.lottery_widget:
            return lottery_page.lottery_widget
        if hasattr(lottery_page, "contentWidget") and lottery_page.contentWidget:
            return lottery_page.contentWidget
        return None

    def _handle_roll_call_action(self, action: str, payload) -> None:
        def impl():
            self._ensure_main_window_pages_created()
            data = payload if isinstance(payload, dict) else {}
            if action == "quick_draw":
                if hasattr(self.main_window, "_handle_quick_draw"):
                    self.main_window._handle_quick_draw()
                return

            widget = self._get_roll_call_widget()
            if widget is None:
                return

            if (
                hasattr(widget, "populate_lists")
                and getattr(widget, "list_combobox", None) is not None
            ):
                try:
                    if widget.list_combobox.count() <= 0:
                        widget.populate_lists()
                except Exception:
                    pass

            if action == "start":
                widget.start_draw()
            elif action == "stop":
                if hasattr(widget, "stop_animation"):
                    widget.stop_animation()
            elif action == "reset":
                if hasattr(widget, "reset_count"):
                    widget.reset_count()
            elif action == "set_count":
                count = data.get("count")
                try:
                    desired = int(count)
                except Exception:
                    return
                if desired <= 0:
                    return
                try:
                    max_count = int(widget.get_total_count() or 0)
                except Exception:
                    max_count = 0
                if max_count <= 0:
                    return
                desired = min(desired, max_count)
                try:
                    current = int(widget.count_label.text())
                except Exception:
                    current = int(getattr(widget, "current_count", 1) or 1)
                widget.update_count(desired - current)
            elif action == "set_group":
                idx = data.get("index")
                text = data.get("group")
                if idx is not None:
                    try:
                        i = int(idx)
                    except Exception:
                        i = None
                    if i is not None and i >= 0 and i < widget.range_combobox.count():
                        widget.range_combobox.setCurrentIndex(i)
                elif isinstance(text, str) and text:
                    found = widget.range_combobox.findText(text)
                    if found >= 0:
                        widget.range_combobox.setCurrentIndex(found)
            elif action == "set_gender":
                idx = data.get("index")
                text = data.get("gender")
                if idx is not None and str(idx).lstrip("-").isdigit():
                    widget.gender_combobox.setCurrentIndex(int(idx))
                elif isinstance(text, str) and text:
                    found = widget.gender_combobox.findText(text)
                    if found >= 0:
                        widget.gender_combobox.setCurrentIndex(found)
            elif action == "set_list":
                idx = data.get("index")
                text = data.get("class_name")
                if idx is not None and str(idx).lstrip("-").isdigit():
                    widget.list_combobox.setCurrentIndex(int(idx))
                elif isinstance(text, str) and text:
                    found = widget.list_combobox.findText(text)
                    if found >= 0:
                        widget.list_combobox.setCurrentIndex(found)

        safe_execute(impl, error_message="执行点名控制失败")

    def _handle_lottery_action(self, action: str, payload) -> None:
        def impl():
            self._ensure_main_window_pages_created()
            data = payload if isinstance(payload, dict) else {}
            widget = self._get_lottery_widget()
            if widget is None:
                return

            if hasattr(widget, "populate_lists"):
                try:
                    if (
                        getattr(widget, "pool_list_combobox", None) is not None
                        and widget.pool_list_combobox.count() <= 0
                    ):
                        widget.populate_lists()
                except Exception:
                    pass

            if action == "start":
                widget.start_draw()
            elif action == "stop":
                if hasattr(widget, "stop_animation"):
                    widget.stop_animation()
            elif action == "reset":
                if hasattr(widget, "reset_count"):
                    widget.reset_count()
            elif action == "set_count":
                count = data.get("count")
                try:
                    desired = int(count)
                except Exception:
                    return
                if desired <= 0:
                    return
                try:
                    max_count = int(
                        widget.manager.get_pool_total_count(
                            widget.pool_list_combobox.currentText()
                        )
                        or 0
                    )
                except Exception:
                    max_count = 0
                if max_count <= 0:
                    return
                desired = min(desired, max_count)
                try:
                    current = int(widget.count_label.text())
                except Exception:
                    current = int(getattr(widget, "current_count", 1) or 1)
                widget.update_count(desired - current)
            elif action == "set_pool":
                idx = data.get("index")
                text = data.get("pool_name")
                if idx is not None and str(idx).lstrip("-").isdigit():
                    widget.pool_list_combobox.setCurrentIndex(int(idx))
                elif isinstance(text, str) and text:
                    found = widget.pool_list_combobox.findText(text)
                    if found >= 0:
                        widget.pool_list_combobox.setCurrentIndex(found)
            elif action == "set_range":
                idx = data.get("index")
                text = data.get("range")
                if idx is not None:
                    try:
                        i = int(idx)
                    except Exception:
                        i = None
                    if i is not None and i >= 0 and i < widget.range_combobox.count():
                        widget.range_combobox.setCurrentIndex(i)
                elif isinstance(text, str) and text:
                    found = widget.range_combobox.findText(text)
                    if found >= 0:
                        widget.range_combobox.setCurrentIndex(found)
            elif action == "set_gender":
                idx = data.get("index")
                text = data.get("gender")
                if idx is not None and str(idx).lstrip("-").isdigit():
                    widget.gender_combobox.setCurrentIndex(int(idx))
                elif isinstance(text, str) and text:
                    found = widget.gender_combobox.findText(text)
                    if found >= 0:
                        widget.gender_combobox.setCurrentIndex(found)
            elif action == "set_list":
                idx = data.get("index")
                text = data.get("class_name")
                if idx is not None and str(idx).lstrip("-").isdigit():
                    widget.list_combobox.setCurrentIndex(int(idx))
                elif isinstance(text, str) and text:
                    found = widget.list_combobox.findText(text)
                    if found >= 0:
                        widget.list_combobox.setCurrentIndex(found)

        safe_execute(impl, error_message="执行抽奖控制失败")

    def _handle_window_action(self, target: str, payload) -> None:
        def impl():
            data = payload if isinstance(payload, dict) else {}
            action = str(data.get("action") or "toggle").strip().lower()

            if target == "main":
                if self.main_window is None:
                    return
                page = data.get("page")
                if not page:
                    query = data.get("query")
                    if isinstance(query, dict):
                        page = (
                            query.get("page")
                            or query.get("page_name")
                            or query.get("name")
                            or query.get("value")
                        )
                page_name = None
                if page is not None:
                    text = str(page).strip()
                    if text:
                        page_name = text

                if action == "toggle":
                    if page_name:
                        is_minimized = (
                            getattr(self.main_window, "isMinimized", None)
                            and self.main_window.isMinimized()
                        )
                        if self.main_window.isVisible() and not is_minimized:
                            self.main_window.hide()
                        elif hasattr(self.main_window, "_handle_main_page_requested"):
                            self.main_window._handle_main_page_requested(page_name)
                        elif hasattr(self.main_window, "toggle_window"):
                            self.main_window.toggle_window()
                        else:
                            self.main_window.show()
                    elif hasattr(self.main_window, "toggle_window"):
                        self.main_window.toggle_window()
                    return
                if action == "hide":
                    self.main_window.hide()
                    return
                if action == "show":
                    if page_name and hasattr(
                        self.main_window, "_handle_main_page_requested"
                    ):
                        self.main_window._handle_main_page_requested(page_name)
                        return
                    if (
                        getattr(self.main_window, "isMinimized", None)
                        and self.main_window.isMinimized()
                    ):
                        if hasattr(self.main_window, "toggle_window"):
                            self.main_window.toggle_window()
                    elif not self.main_window.isVisible():
                        if hasattr(self.main_window, "toggle_window"):
                            self.main_window.toggle_window()
                        else:
                            self.main_window.show()
                    if hasattr(self.main_window, "activateWindow"):
                        self.main_window.activateWindow()
                    if hasattr(self.main_window, "raise_"):
                        self.main_window.raise_()
                    return

            if target == "settings":
                page = str(data.get("page") or "basicSettingsInterface")
                is_preview = bool(data.get("is_preview") or False)
                win = self.settings_window
                if action == "hide":
                    if win is not None and win.isVisible():
                        win.hide()
                    return
                if action == "toggle":
                    if win is not None and win.isVisible():
                        win.hide()
                        return
                    self.show_settings_window(page, is_preview=is_preview)
                    return
                if action == "show":
                    self.show_settings_window(page, is_preview=is_preview)
                    return

            if target == "float":
                if action == "toggle":
                    self.show_float_window()
                    return

                if self.float_window is None:
                    self.create_float_window()
                win = self.float_window
                if win is None:
                    return
                if action == "hide":
                    if win.isVisible():
                        win.hide()
                    return
                if action == "show":
                    if not win.isVisible():
                        win.show()
                    activate_window(win)
                    return

            if target in ("timer", "countdown_timer"):
                try:
                    from app.page_building import (
                        another_window as another_window_module,
                    )
                except Exception:
                    return

                instances = (
                    getattr(another_window_module, "_window_instances", {}) or {}
                )
                win = instances.get("countdown_timer")

                if action == "hide":
                    if (
                        win is not None
                        and getattr(win, "isVisible", None)
                        and win.isVisible()
                    ):
                        win.hide()
                    return

                if action == "toggle" and win is not None:
                    try:
                        if win.isVisible():
                            win.hide()
                            return
                    except Exception:
                        pass

                if hasattr(another_window_module, "create_countdown_timer_window"):
                    another_window_module.create_countdown_timer_window()

                instances = (
                    getattr(another_window_module, "_window_instances", {}) or {}
                )
                win = instances.get("countdown_timer")
                if win is not None:
                    activate_window(win)
                return

        safe_execute(impl, error_message="执行窗口控制失败")

    def _log_startup_time(self) -> None:
        """记录启动时间"""
        try:
            import time

            elapsed = time.perf_counter() - app_start_time
            logger.debug(f"主窗口创建完成，启动耗时: {elapsed:.3f}s")
        except Exception as e:
            logger.exception("计算启动耗时出错（已忽略）: {}", e)

    def create_settings_window(self, is_preview: bool = False) -> None:
        """创建设置窗口实例

        Args:
            is_preview: 是否为预览模式，默认为 False
        """
        if not safe_execute(
            self._create_settings_window_impl,
            is_preview,
            error_message="创建设置窗口失败",
        ):
            logger.exception("设置窗口创建失败", exc_info=True)

    def _create_settings_window_impl(self, is_preview: bool) -> None:
        """创建设置窗口的实现

        Args:
            is_preview: 是否为预览模式
        """
        from app.view.settings.settings import SettingsWindow

        self.settings_window = SettingsWindow(is_preview=is_preview)

    def show_settings_window(
        self, page_name: str = "basicSettingsInterface", is_preview: bool = False
    ) -> None:
        """显示设置窗口

        Args:
            page_name: 设置页面名称，默认为 basicSettingsInterface
            is_preview: 是否为预览模式，默认为 False
        """
        if not safe_execute(
            self._show_settings_window_impl,
            page_name,
            is_preview,
            error_message="显示设置窗口失败",
        ):
            logger.exception("设置窗口显示失败", exc_info=True)

    def _show_settings_window_impl(self, page_name: str, is_preview: bool) -> None:
        """显示设置窗口的实现

        Args:
            page_name: 页面名称
            is_preview: 是否为预览模式
        """
        if self._should_recreate_settings_window(is_preview):
            self._recreate_settings_window()

        if self.settings_window is None:
            self.create_settings_window(is_preview=is_preview)

        if self.settings_window is not None:
            self.settings_window.show_settings_window()
            self.settings_window._handle_settings_page_request(page_name)

    def _should_recreate_settings_window(self, is_preview: bool) -> bool:
        """检查是否需要重新创建设置窗口

        Args:
            is_preview: 请求的预览模式

        Returns:
            bool: 是否需要重新创建
        """
        return (
            self.settings_window is not None
            and hasattr(self.settings_window, "is_preview")
            and self.settings_window.is_preview != is_preview
        )

    def _recreate_settings_window(self) -> None:
        """重新创建设置窗口"""
        logger.debug("重新创建设置窗口")
        safe_close_window(self.settings_window)
        self.settings_window = None

    def show_settings_window_about(self) -> None:
        """显示关于窗口"""
        from app.common.safety.verify_ops import (
            should_require_password,
            require_and_run,
        )

        def on_verified() -> None:
            """验证通过后，正常打开关于窗口"""
            if self.settings_window is None:
                self.create_settings_window()

            if self.settings_window is not None:
                self.show_settings_window("aboutInterface", is_preview=False)

        def on_preview() -> None:
            """点击预览按钮后，以预览模式打开关于窗口"""
            if self.settings_window is None:
                self.create_settings_window()

            if self.settings_window is not None:
                self.show_settings_window("aboutInterface", is_preview=True)

        if should_require_password("open_settings"):
            logger.debug("打开关于窗口需要验证")
            require_and_run(
                "open_settings", self.main_window, on_verified, on_preview=on_preview
            )
        else:
            logger.debug("打开关于窗口无需验证")
            on_verified()

    def create_float_window(self) -> None:
        """创建浮窗实例"""
        if not safe_execute(
            self._create_float_window_impl, error_message="创建浮窗失败"
        ):
            logger.exception("浮窗创建失败", exc_info=True)

    def _create_float_window_impl(self) -> None:
        """创建浮窗的实现"""
        from app.view.floating_window.levitation import LevitationWindow

        self.float_window = LevitationWindow()

    def show_float_window(self) -> None:
        """显示浮窗"""
        success, _ = safe_execute(
            self._show_float_window_impl, error_message="显示浮窗失败"
        )
        if not success:
            logger.exception("浮窗显示失败", exc_info=True)

    def _show_float_window_impl(self) -> None:
        """显示浮窗的实现"""
        if self.float_window is None:
            self.create_float_window()

        if hasattr(self.float_window, "toggle_user_requested_visible"):
            self.float_window.toggle_user_requested_visible()
            return

        if self.float_window.isVisible():
            self.float_window.hide()
        else:
            self.float_window.show()

    def show_guide_window(self) -> None:
        """显示引导窗口"""
        if self.guide_window is None:
            from app.view.guide.guide_window import GuideWindow
            from PySide6.QtWidgets import QApplication

            self.guide_window = GuideWindow()
            self.guide_window.guideFinished.connect(self._on_guide_finished)
            # 居中显示
            screen = QApplication.primaryScreen().availableGeometry()
            w, h = screen.width(), screen.height()
            self.guide_window.move(
                w // 2 - self.guide_window.width() // 2,
                h // 2 - self.guide_window.height() // 2,
            )

        self.guide_window.show()
        self.guide_window.raise_()
        self.guide_window.activateWindow()

    def _on_guide_finished(self) -> None:
        """引导完成处理"""
        self.guide_window = None
        self.create_main_window()

    def get_main_window(self) -> Optional["QWidget"]:
        """获取主窗口实例

        Returns:
            主窗口实例
        """
        return self.main_window

    def get_settings_window(self) -> Optional["QWidget"]:
        """获取设置窗口实例

        Returns:
            设置窗口实例
        """
        return self.settings_window

    def get_float_window(self) -> Optional["QWidget"]:
        """获取浮窗实例

        Returns:
            浮窗实例
        """
        return self.float_window
