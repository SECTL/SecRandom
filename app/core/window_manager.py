from typing import Optional, Callable, TYPE_CHECKING
from loguru import logger
from app.tools.settings_access import readme_settings_async
from app.core.utils import safe_execute, safe_close_window

if TYPE_CHECKING:
    from PySide6.QtWidgets import QWidget


app_start_time: float = 0.0


class WindowManager:
    """窗口管理器，负责创建和管理所有窗口实例"""

    def __init__(self) -> None:
        """初始化窗口管理器"""
        self.main_window: Optional["QWidget"] = None
        self.settings_window: Optional["QWidget"] = None
        self.float_window: Optional["QWidget"] = None
        self.url_handler: Optional = None
        self.shared_memory = None

    def set_shared_memory(self, shared_memory) -> None:
        """设置共享内存实例
        
        Args:
            shared_memory: QSharedMemory 实例
        """
        self.shared_memory = shared_memory

    def set_url_handler(self, url_handler) -> None:
        """设置URL处理器

        Args:
            url_handler: URL处理器实例
        """
        self.url_handler = url_handler

    def create_main_window(self) -> None:
        """创建主窗口实例"""
        success, _ = safe_execute(
            self._create_main_window_impl, error_message="创建主窗口失败"
        )
        if not success:
            logger.error("主窗口创建失败", exc_info=True)

    def _create_main_window_impl(self) -> None:
        """创建主窗口的实现"""
        from app.view.main.window import MainWindow

        self.create_float_window()
        self.main_window = MainWindow(
            float_window=self.float_window,
            url_handler_instance=self.url_handler,
            shared_memory=self.shared_memory,
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
        self.main_window.showSettingsRequestedAbout.connect(
            self.show_settings_window_about
        )
        self.main_window.showFloatWindowRequested.connect(self.show_float_window)

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
        show_startup_window = readme_settings_async(
            "basic_settings", "show_startup_window"
        )
        if show_startup_window:
            self.main_window.show()

        startup_display_float = readme_settings_async(
            "floating_window_management", "startup_display_floating_window"
        )
        if startup_display_float:
            self.show_float_window()

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
            logger.error("设置窗口创建失败", exc_info=True)

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
            logger.error("设置窗口显示失败", exc_info=True)

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
        if self.settings_window is None:
            self.create_settings_window()

        if self.settings_window is not None:
            self.settings_window.show_settings_window_about()

    def create_float_window(self) -> None:
        """创建浮窗实例"""
        if not safe_execute(
            self._create_float_window_impl, error_message="创建浮窗失败"
        ):
            logger.error("浮窗创建失败", exc_info=True)

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
            logger.error("浮窗显示失败", exc_info=True)

    def _show_float_window_impl(self) -> None:
        """显示浮窗的实现"""
        if self.float_window is None:
            self.create_float_window()

        if self.float_window is not None:
            self.float_window.show()

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
