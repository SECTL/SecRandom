# ==================================================
# 导入库
# ==================================================

from loguru import logger
from PySide6.QtWidgets import QApplication, QWidget, QScroller, QSizePolicy
from PySide6.QtGui import QIcon
from PySide6.QtCore import QTimer, QEvent, Signal, QSize, Qt
from PySide6.QtWidgets import QVBoxLayout
from qfluentwidgets import (
    FluentWindow,
    NavigationItemPosition,
    SingleDirectionScrollArea,
    SearchLineEdit,
)

from app.tools.variable import (
    MINIMUM_WINDOW_SIZE,
    APP_INIT_DELAY,
    RESIZE_TIMER_DELAY_MS,
    MAXIMIZE_RESTORE_DELAY_MS,
    SETTINGS_WINDOW_DEFAULT_WIDTH,
    SETTINGS_WINDOW_DEFAULT_HEIGHT,
    SETTINGS_WARMUP_DELAY_MS,
    SETTINGS_DEFAULT_PAGE_DELAY_MS,
)
from app.tools.path_utils import get_data_path
from app.tools.personalised import get_theme_icon
from app.tools.settings_access import (
    readme_settings_async,
    update_settings,
)
from app.page_building.window_template import BackgroundLayer
from app.Language.obtain_language import get_content_name_async
from app.common.IPC_URL.url_command_handler import URLCommandHandler
from app.common.search.settings_search_controller import SettingsSearchController


# ==================================================
# 设置窗口类
# ==================================================
class SettingsWindow(FluentWindow):
    """设置窗口类
    程序的设置管理界面"""

    showSettingsRequested = Signal(str)  # 请求显示设置页面
    showSettingsRequestedAbout = Signal()
    showMainPageRequested = Signal(str)  # 请求显示主页面

    def __init__(self, parent=None, is_preview=False):
        self.resize_timer = None
        super().__init__(parent)
        self.setObjectName("settingWindow")
        self._host_window = parent
        self._is_preview = is_preview

        self._initialize_variables()
        self._setup_timers()
        self._setup_window_properties()
        self._setup_url_handler()
        self._position_window()
        self._setup_splash_screen()

        QTimer.singleShot(APP_INIT_DELAY, lambda: (self.createSubInterface()))

    # ==================================================
    # 初始化方法
    # ==================================================

    def _initialize_variables(self):
        """初始化实例变量"""
        interface_names = [
            "basicSettingsInterface",
            "listManagementInterface",
            "extractionSettingsInterface",
            "floatingWindowManagementInterface",
            "notificationSettingsInterface",
            "safetySettingsInterface",
            "customSettingsInterface",
            "voiceSettingsInterface",
            "themeManagementInterface",
            "historyInterface",
            "moreSettingsInterface",
            "updateInterface",
            "aboutInterface",
        ]

        for name in interface_names:
            setattr(self, name, None)

        self._deferred_factories = {}
        self._deferred_factories_meta = {}
        self._created_pages = {}
        self._page_access_order = []

    def _setup_timers(self):
        """设置定时器"""
        self.resize_timer = QTimer(self)
        self.resize_timer.setSingleShot(True)
        self.resize_timer.timeout.connect(
            lambda: self.save_window_size(self.width(), self.height())
        )

    def _setup_window_properties(self):
        """设置窗口属性"""
        self.resize(SETTINGS_WINDOW_DEFAULT_WIDTH, SETTINGS_WINDOW_DEFAULT_HEIGHT)
        self.setMinimumSize(MINIMUM_WINDOW_SIZE[0], MINIMUM_WINDOW_SIZE[1])
        self.setWindowTitle("SecRandom")
        self.setWindowIcon(
            QIcon(str(get_data_path("assets/icon", "secrandom-icon-paper.png")))
        )
        self._setup_background_layer()
        self._setup_settings_listener()
        self._setup_sidebar_scroll()
        self._setup_titlebar_search()

    def _setup_titlebar_search(self):
        title_bar = getattr(self, "titleBar", None)
        if title_bar is None:
            return

        if getattr(self, "_settings_search_line_edit", None) is not None:
            return

        self._settings_search_line_edit = SearchLineEdit(title_bar)
        try:
            self._settings_search_line_edit.setPlaceholderText(
                get_content_name_async("settings", "search_placeholder")
            )
        except Exception:
            pass

        self._settings_search_controller = SettingsSearchController(
            window=self,
            title_bar=title_bar,
            line_edit=self._settings_search_line_edit,
            handle_page_request=self._handle_settings_page_request,
            get_page_mapping=self._get_page_mapping,
            get_created_page=lambda interface_attr: getattr(
                self, "_created_pages", {}
            ).get(interface_attr, None),
            parent=self,
        )
        try:
            self._settings_search_line_edit.searchSignal.connect(
                self._settings_search_controller.on_search
            )
        except Exception:
            pass

        QTimer.singleShot(0, self._position_titlebar_search)

    def _position_titlebar_search(self):
        title_bar = getattr(self, "titleBar", None)
        line_edit = getattr(self, "_settings_search_line_edit", None)
        if title_bar is None or line_edit is None:
            return

        title_w = int(title_bar.width() or 0)
        title_h = int(title_bar.height() or 0)
        if title_w <= 0 or title_h <= 0:
            return

        left_reserve = 220
        right_reserve = 180
        max_w = max(200, title_w - left_reserve - right_reserve)
        target_w = min(420, max_w)

        try:
            line_edit.setFixedWidth(int(target_w))
        except Exception:
            pass

        try:
            line_edit.adjustSize()
        except Exception:
            pass

        w = int(line_edit.width() or target_w)
        h = int(line_edit.height() or 28)
        x = (title_w - w) // 2
        x = max(left_reserve, min(x, title_w - right_reserve - w))
        y = max(0, (title_h - h) // 2)
        line_edit.move(int(x), int(y))
        line_edit.raise_()

    def _setup_sidebar_scroll(self):
        navigation = getattr(self, "navigationInterface", None)
        if navigation is None:
            return
        if getattr(self, "_sidebar_scroll_area", None) is not None:
            return

        scroll_area = SingleDirectionScrollArea(self)
        scroll_area.setWidgetResizable(False)
        scroll_area.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        scroll_area.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        scroll_area.setStyleSheet(
            """
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
            """
        )
        QScroller.grabGesture(
            scroll_area.viewport(),
            QScroller.ScrollerGestureType.LeftMouseButtonGesture,
        )
        scroll_area.setWidget(navigation)
        scroll_area.setSizePolicy(
            QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Expanding
        )

        layout = getattr(self, "hBoxLayout", None) or self.layout()
        if layout is not None:
            index = layout.indexOf(navigation)
            if index < 0:
                index = 0
            layout.removeWidget(navigation)
            layout.insertWidget(index, scroll_area)

        self._sidebar_scroll_area = scroll_area
        self._sidebar_navigation_widget = navigation

        navigation.installEventFilter(self)
        scroll_area.installEventFilter(self)
        QTimer.singleShot(0, self._sync_sidebar_scroll_geometry)

    def _sync_sidebar_scroll_geometry(self):
        scroll_area = getattr(self, "_sidebar_scroll_area", None)
        navigation = getattr(self, "_sidebar_navigation_widget", None) or getattr(
            self, "navigationInterface", None
        )
        if scroll_area is None or navigation is None:
            return

        target_width = int(navigation.width() or navigation.sizeHint().width() or 0)
        if target_width > 0 and scroll_area.width() != target_width:
            scroll_area.setFixedWidth(target_width)

        viewport_h = int(scroll_area.viewport().height() or 0)
        if viewport_h > 0 and navigation.minimumHeight() != viewport_h:
            navigation.setMinimumHeight(viewport_h)

    def _setup_settings_listener(self):
        try:
            from app.tools.settings_access import get_settings_signals

            get_settings_signals().settingChanged.connect(self._on_setting_changed)
        except Exception:
            pass

    def _on_setting_changed(self, first, second, value):
        if first == "background_management" and str(second or "").startswith(
            "settings_window_background_"
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

        self._background_layer = BackgroundLayer(self, "settings_window")
        self._background_layer.updateGeometryToParent()
        self._background_layer.lower()
        try:
            self._background_layer.applyFromSettings()
        except Exception:
            pass

    def _setup_url_handler(self):
        """设置URL处理器"""
        self.showMainPageRequested.connect(self._handle_main_page_requested)
        self.url_command_handler = URLCommandHandler(self)
        self.url_command_handler.showMainPageRequested.connect(
            self._handle_main_page_requested
        )
        self.url_command_handler.showSettingsRequested.connect(
            self._handle_settings_page_request
        )

    def _setup_splash_screen(self):
        """设置启动画面"""
        from qfluentwidgets import SplashScreen

        self.splashScreen = SplashScreen(self.windowIcon(), self)
        self.splashScreen.setIconSize(QSize(256, 256))
        self.show()

    # ==================================================
    # 属性访问器
    # ==================================================

    @property
    def is_preview(self):
        """获取是否为预览模式"""
        return self._is_preview

    @is_preview.setter
    def is_preview(self, value):
        """设置是否为预览模式，并在值改变时锁定所有已创建的页面

        Args:
            value: 是否为预览模式
        """
        self._is_preview = value
        if hasattr(self, "_created_pages"):
            for page_name, real_page in self._created_pages.items():
                if real_page and hasattr(real_page, "is_preview_mode"):
                    real_page.is_preview_mode = value
                elif real_page:
                    self._lock_all_widgets(real_page)

    def _lock_all_widgets(self, widget):
        """锁定所有子组件

        Args:
            widget: 要锁定的组件
        """
        if widget is None:
            return
        if hasattr(widget, "setEnabled"):
            widget.setEnabled(False)
        for child in widget.children():
            if isinstance(child, QWidget):
                self._lock_all_widgets(child)

    # ==================================================
    # 窗口定位与大小管理
    # ==================================================

    def _position_window(self):
        """窗口定位
        根据屏幕尺寸和用户设置自动计算最佳位置"""
        is_maximized = readme_settings_async("settings", "is_maximized")
        if is_maximized:
            pre_maximized_width = readme_settings_async(
                "settings", "pre_maximized_width"
            )
            pre_maximized_height = readme_settings_async(
                "settings", "pre_maximized_height"
            )
            self.resize(pre_maximized_width, pre_maximized_height)
            self._center_window()
            QTimer.singleShot(APP_INIT_DELAY, self.showMaximized)
        else:
            setting_window_width = readme_settings_async("settings", "width")
            setting_window_height = readme_settings_async("settings", "height")
            self.resize(setting_window_width, setting_window_height)
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
        auto_save_enabled = readme_settings_async(
            "basic_settings", "auto_save_window_size"
        )

        if auto_save_enabled:
            if not self.isMaximized():
                update_settings("settings", "height", height)
                update_settings("settings", "width", width)

    # ==================================================
    # 窗口事件处理
    # ==================================================

    def closeEvent(self, event):
        """窗口关闭事件处理
        拦截窗口关闭事件，隐藏窗口并保存窗口大小

        Args:
            event: 关闭事件对象
        """
        try:
            self._stop_navigation_animations()
        except Exception:
            pass
        self.hide()
        event.ignore()
        is_maximized = self.isMaximized()
        update_settings("settings", "is_maximized", is_maximized)
        if not is_maximized:
            self.save_window_size(self.width(), self.height())

    def _stop_navigation_animations(self):
        """在隐藏设置窗口前尽量停止导航动画，避免悬空回调。"""
        navigation = getattr(self, "navigationInterface", None)
        if navigation is None:
            return

        try:
            panel = getattr(navigation, "panel", None)
            if panel is not None:
                indicator_ani = getattr(panel, "indicatorAni", None)
                if indicator_ani is not None:
                    indicator_ani.stop()
        except Exception:
            pass

    def resizeEvent(self, event):
        """窗口大小变化事件处理
        检测窗口大小变化，启动尺寸记录倒计时

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
        try:
            self._sync_sidebar_scroll_geometry()
        except Exception:
            pass
        try:
            self._position_titlebar_search()
        except Exception:
            pass

    def changeEvent(self, event):
        """窗口状态变化事件处理
        检测窗口最大化/恢复状态变化，保存正确的窗口大小

        Args:
            event: 状态变化事件对象
        """
        if event.type() == QEvent.Type.WindowStateChange:
            is_currently_maximized = self.isMaximized()
            was_maximized = readme_settings_async("settings", "is_maximized")
            if is_currently_maximized != was_maximized:
                update_settings("settings", "is_maximized", is_currently_maximized)
                if is_currently_maximized:
                    normal_geometry = self.normalGeometry()
                    update_settings(
                        "settings", "pre_maximized_width", normal_geometry.width()
                    )
                    update_settings(
                        "settings", "pre_maximized_height", normal_geometry.height()
                    )
                else:
                    pre_maximized_width = readme_settings_async(
                        "settings", "pre_maximized_width"
                    )
                    pre_maximized_height = readme_settings_async(
                        "settings", "pre_maximized_height"
                    )
                    QTimer.singleShot(
                        MAXIMIZE_RESTORE_DELAY_MS,
                        lambda: self.resize(pre_maximized_width, pre_maximized_height),
                    )

        super().changeEvent(event)
        if event.type() == QEvent.Type.WindowStateChange:
            QTimer.singleShot(0, self._sync_sidebar_scroll_geometry)

    def eventFilter(self, obj, event):
        navigation = getattr(self, "_sidebar_navigation_widget", None)
        scroll_area = getattr(self, "_sidebar_scroll_area", None)
        if obj is navigation and event.type() in (
            QEvent.Type.Resize,
            QEvent.Type.LayoutRequest,
            QEvent.Type.Show,
        ):
            try:
                self._sync_sidebar_scroll_geometry()
            except Exception:
                pass
        elif obj is scroll_area and event.type() in (
            QEvent.Type.Resize,
            QEvent.Type.Show,
        ):
            try:
                self._sync_sidebar_scroll_geometry()
            except Exception:
                pass
        return super().eventFilter(obj, event)

    # ==================================================
    # 页面请求处理
    # ==================================================

    def _handle_main_page_requested(self, page_name: str):
        """处理主页面请求

        Args:
            page_name: 页面名称
        """
        logger.debug(f"设置窗口收到主页面请求: {page_name}")

        if page_name.startswith("settings_"):
            self._handle_settings_page_request(page_name)
        else:
            logger.debug(f"设置窗口转发主页面请求: {page_name}")
            host_window = getattr(self, "_host_window", None)
            if host_window is not None and hasattr(
                host_window, "_handle_main_page_requested"
            ):
                host_window._handle_main_page_requested(page_name)

    def _handle_settings_page_request(self, page_name: str):
        """处理设置页面请求

        Args:
            page_name: 设置页面名称
        """
        logger.debug(f"处理设置页面请求: {page_name}")

        self._ensure_sub_interface_created()

        page_mapping = self._get_page_mapping()

        if page_name in page_mapping:
            interface_attr, item_attr = page_mapping[page_name]
            interface = getattr(self, interface_attr, None)
            nav_item = getattr(self, item_attr, None)

            if interface and nav_item:
                logger.debug(f"切换到设置页面: {page_name}")
                try:
                    self._stop_navigation_animations()
                except Exception:
                    pass
                self.switchTo(interface)
                self.show()
                self.activateWindow()
                self.raise_()
            else:
                logger.warning(f"设置页面不存在或尚未初始化: {page_name}")
        else:
            logger.warning(f"未知的设置页面: {page_name}")

    def _ensure_sub_interface_created(self):
        """确保子界面已创建"""
        if not hasattr(self, "_sub_interface_created"):
            self._sub_interface_created = False

        if not self._sub_interface_created:
            logger.debug("子界面尚未创建，立即创建")
            try:
                self.createSubInterface()
                self._sub_interface_created = True
            except Exception as e:
                logger.exception(f"创建子界面失败: {e}")

    def _get_page_mapping(self):
        """获取页面映射字典

        Returns:
            dict: 页面名称到界面属性的映射
        """
        return {
            "settings_basic": ("basicSettingsInterface", "basic_settings_item"),
            "settings_list": ("listManagementInterface", "list_management_item"),
            "settings_extraction": (
                "extractionSettingsInterface",
                "extraction_settings_item",
            ),
            "settings_floating": (
                "floatingWindowManagementInterface",
                "floating_window_management_item",
            ),
            "settings_notification": (
                "notificationSettingsInterface",
                "notification_settings_item",
            ),
            "settings_safety": ("safetySettingsInterface", "safety_settings_item"),
            "settings_linkage": ("courseSettingsInterface", "course_settings_item"),
            "settings_voice": ("voiceSettingsInterface", "voice_settings_item"),
            "settings_theme": ("themeManagementInterface", "theme_management_item"),
            "settings_history": ("historyInterface", "history_item"),
            "settings_more": ("moreSettingsInterface", "more_settings_item"),
            "settings_update": ("updateInterface", "update_item"),
            "settings_about": ("aboutInterface", "about_item"),
            "basicSettingsInterface": ("basicSettingsInterface", "basic_settings_item"),
            "listManagementInterface": (
                "listManagementInterface",
                "list_management_item",
            ),
            "extractionSettingsInterface": (
                "extractionSettingsInterface",
                "extraction_settings_item",
            ),
            "floatingWindowManagementInterface": (
                "floatingWindowManagementInterface",
                "floating_window_management_item",
            ),
            "notificationSettingsInterface": (
                "notificationSettingsInterface",
                "notification_settings_item",
            ),
            "safetySettingsInterface": (
                "safetySettingsInterface",
                "safety_settings_item",
            ),
            "courseSettingsInterface": (
                "courseSettingsInterface",
                "course_settings_item",
            ),
            "voiceSettingsInterface": ("voiceSettingsInterface", "voice_settings_item"),
            "themeManagementInterface": (
                "themeManagementInterface",
                "theme_management_item",
            ),
            "historyInterface": ("historyInterface", "history_item"),
            "moreSettingsInterface": ("moreSettingsInterface", "more_settings_item"),
            "updateInterface": ("updateInterface", "update_item"),
            "aboutInterface": ("aboutInterface", "about_item"),
        }

    # ==================================================
    # 界面创建与导航
    # ==================================================

    def createSubInterface(self):
        """创建子界面
        搭建子界面导航系统"""
        if hasattr(self, "_sub_interface_created") and self._sub_interface_created:
            logger.debug("子界面已创建，跳过重复创建")
            return

        from app.page_building import settings_window_page

        settings = self._get_sidebar_settings()
        page_configs = self._get_page_configs()

        for setting_key, interface_attr, page_method, is_pivot in page_configs:
            setting_value = settings.get(setting_key)
            if setting_value is None or setting_value != 2:
                self._create_page_placeholder(
                    interface_attr, page_method, is_pivot, settings_window_page
                )

        self._create_special_pages(settings_window_page)
        self.initNavigation()
        self._setup_background_warmup()
        self._sub_interface_created = True

    def _get_sidebar_settings(self):
        """获取侧边栏设置

        Returns:
            dict: 侧边栏设置字典
        """
        return {
            "base_settings": 0,
            "name_management": 0,
            "draw_settings": 0,
            "floating_window_management": 0,
            "notification_service": 0,
            "security_settings": 0,
            "linkage_settings": 0,
            "voice_settings": 0,
            "theme_management": 0,
            "settings_history": 0,
            "more_settings": 0,
            "updateInterface": 0,
            "aboutInterface": 0,
        }

    def _get_page_configs(self):
        """获取页面配置列表

        Returns:
            list: 页面配置列表
        """
        return [
            ("base_settings", "basicSettingsInterface", "basic_settings_page", False),
            (
                "name_management",
                "listManagementInterface",
                "list_management_page",
                True,
            ),
            (
                "draw_settings",
                "extractionSettingsInterface",
                "extraction_settings_page",
                True,
            ),
            (
                "floating_window_management",
                "floatingWindowManagementInterface",
                "floating_window_management_page",
                True,
            ),
            (
                "notification_service",
                "notificationSettingsInterface",
                "notification_settings_page",
                True,
            ),
            (
                "security_settings",
                "safetySettingsInterface",
                "safety_settings_page",
                True,
            ),
            (
                "linkage_settings",
                "courseSettingsInterface",
                "linkage_settings_page",
                False,
            ),
            ("voice_settings", "voiceSettingsInterface", "voice_settings_page", True),
            (
                "theme_management",
                "themeManagementInterface",
                "theme_management_page",
                False,
            ),
            ("settings_history", "historyInterface", "history_page", True),
            ("more_settings", "moreSettingsInterface", "more_settings_page", True),
            ("updateInterface", "updateInterface", "update_page", False),
            ("aboutInterface", "aboutInterface", "about_page", False),
        ]

    def _create_page_placeholder(
        self, interface_attr, page_method, is_pivot, settings_window_page
    ):
        """创建页面占位符

        Args:
            interface_attr: 界面属性名
            page_method: 页面方法名
            is_pivot: 是否为pivot页面
            settings_window_page: 设置窗口页面模块
        """
        interface = self._make_placeholder(interface_attr)
        setattr(self, interface_attr, interface)

        def make_factory(method_name=page_method, iface=interface):
            def factory(parent=iface, is_preview=False):
                page_instance = getattr(settings_window_page, method_name)(
                    parent, is_preview=is_preview
                )
                return page_instance

            return factory

        self._deferred_factories[interface_attr] = make_factory()
        self._deferred_factories_meta[interface_attr] = {
            "is_pivot": is_pivot,
            "is_preview": False,
        }

    def _make_placeholder(self, name: str):
        """创建占位符组件

        Args:
            name: 占位符名称

        Returns:
            QWidget: 占位符组件
        """
        w = QWidget()
        w.setObjectName(name)
        layout = QVBoxLayout(w)
        layout.setContentsMargins(0, 0, 0, 0)
        return w

    def _create_special_pages(self, settings_window_page):
        """创建特殊页面（更新和关于页面）

        Args:
            settings_window_page: 设置窗口页面模块
        """
        self.updateInterface = self._make_placeholder("updateInterface")
        self._deferred_factories["updateInterface"] = self._make_page_factory(
            "update_page", self.updateInterface, settings_window_page
        )
        self._deferred_factories_meta["updateInterface"] = {
            "is_pivot": False,
            "is_preview": False,
        }

        self.aboutInterface = self._make_placeholder("aboutInterface")
        self._deferred_factories["aboutInterface"] = self._make_page_factory(
            "about_page", self.aboutInterface, settings_window_page
        )
        self._deferred_factories_meta["aboutInterface"] = {
            "is_pivot": False,
            "is_preview": False,
        }

    def _make_page_factory(self, page_method, interface, settings_window_page):
        """创建页面工厂函数

        Args:
            page_method: 页面方法名
            interface: 界面对象
            settings_window_page: 设置窗口页面模块

        Returns:
            function: 工厂函数
        """

        def factory(parent=interface, is_preview=False):
            page_instance = getattr(settings_window_page, page_method)(
                parent, is_preview=is_preview
            )
            return page_instance

        return factory

    def _setup_background_warmup(self):
        """设置后台预热"""
        try:
            QTimer.singleShot(
                SETTINGS_WARMUP_DELAY_MS, lambda: self._background_warmup_non_pivot()
            )
        except Exception as e:
            logger.exception("Error during settings warmup: {}", e)

        try:
            self.stackedWidget.currentChanged.connect(self._on_stacked_widget_changed)
        except Exception as e:
            logger.exception("Error creating deferred page: {}", e)

        try:
            QTimer.singleShot(
                SETTINGS_WARMUP_DELAY_MS, lambda: self._background_warmup_pages()
            )
        except Exception as e:
            logger.exception("Error scheduling background warmup pages: {}", e)

    def initNavigation(self):
        """初始化导航系统
        根据用户设置构建个性化菜单导航"""
        settings = self._get_sidebar_settings()
        nav_configs = self._get_nav_configs()

        for (
            setting_key,
            interface_attr,
            item_attr,
            icon_name,
            module,
            name_key,
        ) in nav_configs:
            setting_value = settings.get(setting_key)
            if setting_value is None or setting_value != 2:
                self._add_navigation_item(
                    setting_key, interface_attr, item_attr, icon_name, module, name_key
                )

        self.splashScreen.finish()

        if hasattr(self, "basicSettingsInterface") and self.basicSettingsInterface:
            QTimer.singleShot(SETTINGS_DEFAULT_PAGE_DELAY_MS, self._load_default_page)

    def _get_nav_configs(self):
        """获取导航配置列表

        Returns:
            list: 导航配置列表
        """
        return [
            (
                "base_settings",
                "basicSettingsInterface",
                "basic_settings_item",
                "ic_fluent_wrench_settings_20_filled",
                "basic_settings",
                "title",
            ),
            (
                "name_management",
                "listManagementInterface",
                "list_management_item",
                "ic_fluent_list_20_filled",
                "list_management",
                "title",
            ),
            (
                "draw_settings",
                "extractionSettingsInterface",
                "extraction_settings_item",
                "ic_fluent_archive_20_filled",
                "extraction_settings",
                "title",
            ),
            (
                "floating_window_management",
                "floatingWindowManagementInterface",
                "floating_window_management_item",
                "ic_fluent_window_apps_20_filled",
                "floating_window_management",
                "title",
            ),
            (
                "notification_service",
                "notificationSettingsInterface",
                "notification_settings_item",
                "ic_fluent_comment_note_20_filled",
                "notification_settings",
                "title",
            ),
            (
                "security_settings",
                "safetySettingsInterface",
                "safety_settings_item",
                "ic_fluent_shield_20_filled",
                "safety_settings",
                "title",
            ),
            (
                "linkage_settings",
                "courseSettingsInterface",
                "course_settings_item",
                "ic_fluent_calendar_ltr_20_filled",
                "linkage_settings",
                "title",
            ),
            (
                "voice_settings",
                "voiceSettingsInterface",
                "voice_settings_item",
                "ic_fluent_person_voice_20_filled",
                "voice_settings",
                "title",
            ),
            (
                "theme_management",
                "themeManagementInterface",
                "theme_management_item",
                "ic_fluent_paint_brush_20_filled",
                "theme_management",
                "title",
            ),
            (
                "settings_history",
                "historyInterface",
                "history_item",
                "ic_fluent_history_20_filled",
                "history",
                "title",
            ),
            (
                "more_settings",
                "moreSettingsInterface",
                "more_settings_item",
                "ic_fluent_more_horizontal_20_filled",
                "more_settings",
                "title",
            ),
            (
                "updateInterface",
                "updateInterface",
                "update_item",
                "ic_fluent_arrow_sync_20_filled",
                "update",
                "title",
            ),
            (
                "aboutInterface",
                "aboutInterface",
                "about_item",
                "ic_fluent_info_20_filled",
                "about",
                "title",
            ),
        ]

    def _add_navigation_item(
        self, setting_key, interface_attr, item_attr, icon_name, module, name_key
    ):
        """添加导航项

        Args:
            setting_key: 设置键名
            interface_attr: 界面属性名
            item_attr: 导航项属性名
            icon_name: 图标名称
            module: 模块名
            name_key: 名称键
        """
        settings = self._get_sidebar_settings()
        setting_value = settings.get(setting_key)
        interface = getattr(self, interface_attr, None)
        if interface is not None:
            position = (
                NavigationItemPosition.BOTTOM
                if setting_value == 1
                else NavigationItemPosition.TOP
            )

            nav_item = self.addSubInterface(
                interface,
                get_theme_icon(icon_name),
                get_content_name_async(module, name_key),
                position=position,
            )
            setattr(self, item_attr, nav_item)

    def _load_default_page(self):
        """加载默认页面（基础设置页面）"""
        try:
            if "basicSettingsInterface" in getattr(self, "_deferred_factories", {}):
                self._create_deferred_page("basicSettingsInterface")

            if hasattr(self, "basicSettingsInterface") and self.basicSettingsInterface:
                self.switchTo(self.basicSettingsInterface)
                logger.debug("已自动导航到基础设置页面")
        except Exception as e:
            logger.exception(f"加载默认页面失败: {e}")

    # ==================================================
    # 页面加载与卸载
    # ==================================================

    def _on_stacked_widget_changed(self, index: int):
        """当导航切换到某个占位页时，按需创建真实页面内容，并卸载不活动的页面

        Args:
            index: 当前索引
        """
        try:
            widget = self.stackedWidget.widget(index)
            if not widget:
                return
            name = widget.objectName()

            self._unload_inactive_pages(name)

            if (
                name in getattr(self, "_deferred_factories", {})
                and widget.layout()
                and widget.layout().count() == 0
            ):
                factory = self._deferred_factories.pop(name)
                try:
                    logger.debug(f"正在创建页面 {name}，预览模式: {self.is_preview}")
                    real_page = factory(is_preview=self.is_preview)
                    widget.layout().addWidget(real_page)

                    if not hasattr(self, "_created_pages"):
                        self._created_pages = {}
                    self._created_pages[name] = real_page

                    logger.debug(
                        f"设置页面已按需创建: {name}, 预览模式: {self.is_preview}"
                    )
                except Exception as e:
                    logger.exception(f"延迟创建设置页面 {name} 失败: {e}")
        except Exception as e:
            logger.exception(f"处理堆叠窗口改变失败: {e}")

    def _unload_inactive_pages(self, current_page: str):
        """卸载不活动的页面以释放内存

        Args:
            current_page: 当前激活的页面名称
        """
        MAX_CACHED_SETTINGS_PAGES = 2

        if not hasattr(self, "_created_pages"):
            self._created_pages = {}

        if not hasattr(self, "_page_access_order"):
            self._page_access_order = []

        if current_page in self._page_access_order:
            self._page_access_order.remove(current_page)
        self._page_access_order.append(current_page)

        created_pages = list(self._created_pages.keys())

        while len(created_pages) > MAX_CACHED_SETTINGS_PAGES:
            oldest_page = None
            for page_name in self._page_access_order:
                if page_name in created_pages and page_name != current_page:
                    oldest_page = page_name
                    break

            if oldest_page is None:
                break

            self._unload_settings_page(oldest_page)
            created_pages.remove(oldest_page)
            if oldest_page in self._page_access_order:
                self._page_access_order.remove(oldest_page)

    def _unload_settings_page(self, page_name: str):
        """卸载指定的设置页面以释放内存

        Args:
            page_name: 要卸载的页面名称
        """
        if not hasattr(self, "_created_pages") or page_name not in self._created_pages:
            return

        try:
            real_page = self._created_pages.pop(page_name)
            container = getattr(self, page_name, None)
            if container and container.layout():
                container.layout().removeWidget(real_page)

            real_page.deleteLater()

            self._restore_page_factory(page_name, container)

            logger.debug(f"已卸载设置页面 {page_name} 以释放内存")
        except RuntimeError as e:
            logger.warning(f"卸载设置页面 {page_name} 时出现警告: {e}")
        except Exception as e:
            logger.exception(f"卸载设置页面 {page_name} 失败: {e}")

    def _restore_page_factory(self, page_name: str, container):
        """恢复页面工厂函数

        Args:
            page_name: 页面名称
            container: 容器对象
        """
        from app.page_building import settings_window_page

        factory_mapping = {
            "basicSettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.basic_settings_page(
                p, is_preview=is_preview
            ),
            "listManagementInterface": lambda p=container,
            is_preview=False: settings_window_page.list_management_page(
                p, is_preview=is_preview
            ),
            "extractionSettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.extraction_settings_page(
                p, is_preview=is_preview
            ),
            "floatingWindowManagementInterface": lambda p=container,
            is_preview=False: settings_window_page.floating_window_management_page(
                p, is_preview=is_preview
            ),
            "notificationSettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.notification_settings_page(
                p, is_preview=is_preview
            ),
            "safetySettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.safety_settings_page(
                p, is_preview=is_preview
            ),
            "voiceSettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.voice_settings_page(
                p, is_preview=is_preview
            ),
            "themeManagementInterface": lambda p=container,
            is_preview=False: settings_window_page.theme_management_page(
                p, is_preview=is_preview
            ),
            "historyInterface": lambda p=container,
            is_preview=False: settings_window_page.history_page(
                p, is_preview=is_preview
            ),
            "moreSettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.more_settings_page(
                p, is_preview=is_preview
            ),
            "updateInterface": lambda p=container,
            is_preview=False: settings_window_page.update_page(
                p, is_preview=is_preview
            ),
            "aboutInterface": lambda p=container,
            is_preview=False: settings_window_page.about_page(p, is_preview=is_preview),
            "courseSettingsInterface": lambda p=container,
            is_preview=False: settings_window_page.linkage_settings_page(
                p, is_preview=is_preview
            ),
        }

        if page_name in factory_mapping:
            if not hasattr(self, "_deferred_factories"):
                self._deferred_factories = {}
            self._deferred_factories[page_name] = factory_mapping[page_name]

    def _create_deferred_page(self, name: str):
        """根据名字创建对应延迟工厂并把结果加入占位容器

        Args:
            name: 页面名称
        """
        try:
            if name not in getattr(self, "_deferred_factories", {}):
                return
            factory = self._deferred_factories.pop(name)

            container = self._find_container_by_name(name)
            if container is None:
                return

            if not container or not hasattr(container, "layout"):
                return
            layout = container.layout()
            if layout is None:
                return

            try:
                real_page = factory(is_preview=self.is_preview)
            except RuntimeError as e:
                logger.exception(f"创建延迟页面 {name} 失败（父容器可能已销毁）: {e}")
                return
            except Exception as e:
                logger.exception(f"创建延迟页面 {name} 失败: {e}")
                return

            try:
                layout.addWidget(real_page)
                logger.debug(f"后台预热创建设置页面: {name}")
            except RuntimeError as e:
                logger.exception(
                    f"将延迟页面 {name} 插入容器失败（容器可能已销毁）: {e}"
                )
                return
        except Exception as e:
            logger.exception(f"_create_deferred_page 失败: {e}")

    def _find_container_by_name(self, name: str):
        """根据名称查找容器

        Args:
            name: 容器名称

        Returns:
            QWidget: 容器对象或None
        """
        container_attrs = [
            "basicSettingsInterface",
            "listManagementInterface",
            "extractionSettingsInterface",
            "floatingWindowManagementInterface",
            "notificationSettingsInterface",
            "safetySettingsInterface",
            "customSettingsInterface",
            "voiceSettingsInterface",
            "historyInterface",
            "moreSettingsInterface",
            "courseSettingsInterface",
            "updateInterface",
            "aboutInterface",
        ]

        for attr in container_attrs:
            container_obj = getattr(self, attr, None)
            if container_obj and container_obj.objectName() == name:
                return container_obj

        return None

    # ==================================================
    # 后台预热
    # ==================================================

    def _background_warmup_pages(
        self,
        interval_ms: int = 800,
        max_preload: int = 1,
    ):
        """分批（间隔）创建剩余的设置页面，减少单次阻塞

        内存优化：完全禁用后台预热，所有页面按需加载

        Args:
            interval_ms: 每个页面创建间隔（毫秒）（已禁用）
            max_preload: 最大预加载数量（已禁用）
        """
        pass

    def _background_warmup_non_pivot(self, interval_ms: int = 80):
        """在设置窗口首次打开时，分批延时创建所有非 pivot（单页面）项

        内存优化：禁用自动预热，完全按需加载

        Args:
            interval_ms: 每个页面创建的间隔毫秒数
        """
        try:
            pass
        except Exception as e:
            logger.exception(f"后台预热非 pivot 页面失败: {e}")

    # ==================================================
    # 窗口显示
    # ==================================================

    def show_settings_window(self):
        """显示设置窗口"""
        if self.isMinimized():
            self.showNormal()
            self.activateWindow()
            self.raise_()
        else:
            self.show()
            self.activateWindow()
            self.raise_()

    def show_settings_window_about(self):
        """显示关于窗口"""
        if self.isMinimized():
            self.showNormal()
            self.activateWindow()
            self.raise_()
        else:
            self.show()
            self.activateWindow()
            self.raise_()
        self.switchTo(self.aboutInterface)
