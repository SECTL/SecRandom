# ==================================================
# 导入库
# ==================================================

from loguru import logger
from PySide6.QtWidgets import QApplication, QWidget
from PySide6.QtGui import QIcon
from PySide6.QtCore import QTimer, QEvent, Signal
from qfluentwidgets import FluentWindow, NavigationItemPosition

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from app.common.IPC_URL.url_command_handler import URLCommandHandler


# ==================================================
# 主窗口类
# ==================================================
class SettingsWindow(FluentWindow):
    """主窗口类
    程序的核心控制中心"""

    showSettingsRequested = Signal(str)  # 请求显示设置页面
    showSettingsRequestedAbout = Signal()
    showMainPageRequested = Signal(str)  # 请求显示主页面

    def __init__(self, parent=None, is_preview=False):
        super().__init__()
        self.setObjectName("settingWindow")
        self.parent = parent
        self._is_preview = is_preview

        # 初始化变量
        self._init_interface_variables()

        # resize_timer的初始化
        self.resize_timer = QTimer(self)
        self.resize_timer.setSingleShot(True)
        self.resize_timer.timeout.connect(
            lambda: self.save_window_size(self.width(), self.height())
        )

        # 设置窗口属性
        window_width = 800
        window_height = 600
        self.resize(window_width, window_height)
        self.setMinimumSize(MINIMUM_WINDOW_SIZE[0], MINIMUM_WINDOW_SIZE[1])
        self.setWindowTitle("SecRandom")
        self.setWindowIcon(
            QIcon(str(get_data_path("assets/icon", "secrandom-icon-paper.png")))
        )

        # 初始化URL命令处理器
        self.url_command_handler = URLCommandHandler(self)
        self.url_command_handler.showSettingsRequested.connect(
            self._handle_settings_page_request
        )

        # 窗口定位
        self._position_window()

        # 启动页面
        self.splashScreen = SplashScreen(self.windowIcon(), self)
        self.splashScreen.setIconSize(QSize(256, 256))
        self.show()

        # 初始化子界面
        QTimer.singleShot(APP_INIT_DELAY, lambda: (self.createSubInterface()))

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
        # 锁定所有已创建的页面
        if hasattr(self, "_created_pages"):
            for page_name, real_page in self._created_pages.items():
                if real_page and hasattr(real_page, "is_preview_mode"):
                    real_page.is_preview_mode = value
                elif real_page:
                    # 如果页面没有is_preview_mode属性，直接锁定所有组件
                    def lock_all_widgets(widget):
                        if widget is None:
                            return
                        if hasattr(widget, "setEnabled"):
                            widget.setEnabled(False)
                        for child in widget.children():
                            if isinstance(child, QWidget):
                                lock_all_widgets(child)

                    lock_all_widgets(real_page)

    def _init_interface_variables(self):
        """初始化界面变量"""
        interface_names = [
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
            "updateInterface",
            "aboutInterface",
        ]

        for name in interface_names:
            setattr(self, name, None)

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
        """窗口定位-正常居中显示
        窗口大小设置完成后，将窗口居中显示在屏幕上"""
        screen = QApplication.primaryScreen()
        desktop = screen.availableGeometry()
        w, h = desktop.width(), desktop.height()

        target_x = w // 2 - self.width() // 2
        target_y = h // 2 - self.height() // 2

        self.move(target_x, target_y)

    def _apply_window_visibility_settings(self):
        """应用窗口显示设置
        根据用户保存的设置决定窗口是否显示"""
        try:
            self.show()
        except Exception as e:
            logger.error(f"加载窗口显示设置失败: {e}")

    def _handle_main_page_requested(self, page_name: str):
        """处理主页面请求

        Args:
            page_name: 页面名称
        """
        logger.debug(f"设置窗口收到主页面请求: {page_name}")

        # 处理设置页面特定的页面请求
        if page_name.startswith("settings_"):
            self._handle_settings_page_request(page_name)
        else:
            # 设置窗口通常不需要处理主页面请求，可以转发给父窗口或记录日志
            logger.debug(f"设置窗口转发主页面请求: {page_name}")
            # 如果有父窗口，可以转发信号
            if hasattr(self, "parent") and self.parent:
                self.showMainPageRequested.emit(page_name)

    def _handle_settings_page_request(self, page_name: str):
        """处理设置页面请求

        Args:
            page_name: 设置页面名称 (如 'settings_basic', 'settings_about' 或 'basicSettingsInterface' 等)
        """
        logger.debug(f"处理设置页面请求: {page_name}")

        # 映射设置页面名称到对应的界面属性
        page_mapping = {
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
            "settings_voice": ("voiceSettingsInterface", "voice_settings_item"),
            "settings_history": ("historyInterface", "history_item"),
            "settings_more": ("moreSettingsInterface", "more_settings_item"),
            "settings_update": ("updateInterface", "update_item"),
            "settings_about": ("aboutInterface", "about_item"),
        }

        # 直接映射，从界面名称到对应的界面属性和导航项属性
        direct_interface_mapping = {
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
            "voiceSettingsInterface": ("voiceSettingsInterface", "voice_settings_item"),
            "historyInterface": ("historyInterface", "history_item"),
            "moreSettingsInterface": ("moreSettingsInterface", "more_settings_item"),
            "updateInterface": ("updateInterface", "update_item"),
            "aboutInterface": ("aboutInterface", "about_item"),
        }

        # 反向映射，从界面名称到页面名称
        interface_to_page = {
            "basicSettingsInterface": "settings_basic",
            "listManagementInterface": "settings_list",
            "extractionSettingsInterface": "settings_extraction",
            "floatingWindowManagementInterface": "settings_floating",
            "notificationSettingsInterface": "settings_notification",
            "safetySettingsInterface": "settings_safety",
            "voiceSettingsInterface": "settings_voice",
            "historyInterface": "settings_history",
            "moreSettingsInterface": "settings_more",
            "updateInterface": "settings_update",
            "aboutInterface": "settings_about",
        }

        # 检查 page_name 是否是直接的界面名称
        if page_name in direct_interface_mapping:
            interface_attr, item_attr = direct_interface_mapping[page_name]
            interface = getattr(self, interface_attr, None)
            nav_item = getattr(self, item_attr, None)

            if interface and nav_item:
                logger.debug(f"切换到设置页面: {page_name}")
                self.switchTo(interface)
                self.show()
                self.activateWindow()
                self.raise_()
            else:
                logger.warning(f"设置页面不存在或尚未初始化: {page_name}")
        elif page_name in page_mapping:
            interface_attr, item_attr = page_mapping[page_name]
            interface = getattr(self, interface_attr, None)
            nav_item = getattr(self, item_attr, None)

            if interface and nav_item:
                logger.debug(f"切换到设置页面: {page_name}")
                self.switchTo(interface)
                self.show()
                self.activateWindow()
                self.raise_()
            else:
                logger.warning(f"设置页面不存在或尚未初始化: {page_name}")
        elif page_name in interface_to_page.values():
            # 如果页面名称已经是对应的界面名称，直接切换
            interface_name = None
            for iface_name, mapped_page in interface_to_page.items():
                if mapped_page == page_name:
                    interface_name = iface_name
                    break

            if interface_name and hasattr(self, interface_name):
                interface = getattr(self, interface_name)
                logger.debug(f"切换到设置界面: {interface_name}")
                self.switchTo(interface)
                self.show()
                self.activateWindow()
                self.raise_()
            else:
                logger.warning(f"设置界面不存在: {interface_name}")
        else:
            logger.warning(f"未知的设置页面: {page_name}")

    def createSubInterface(self):
        """创建子界面
        搭建子界面导航系统"""
        # 廉价创建页面：先创建轻量占位容器并注册工厂
        from app.page_building import settings_window_page

        # 存储占位 -> factory 映射
        self._deferred_factories = {}
        # 存储工厂的元信息（例如是否为 pivot 类型），用于预热策略调整
        self._deferred_factories_meta = {}

        def make_placeholder(name: str):
            w = QWidget()
            w.setObjectName(name)
            # 使用空布局以便后续将真正页面加入
            layout = QVBoxLayout(w)
            layout.setContentsMargins(0, 0, 0, 0)
            return w

        # 获取所有设置值
        settings = {
            "base_settings": readme_settings_async(
                "sidebar_management_settings", "base_settings"
            ),
            "name_management": readme_settings_async(
                "sidebar_management_settings", "name_management"
            ),
            "draw_settings": readme_settings_async(
                "sidebar_management_settings", "draw_settings"
            ),
            "floating_window_management": readme_settings_async(
                "sidebar_management_settings", "floating_window_management"
            ),
            "notification_service": readme_settings_async(
                "sidebar_management_settings", "notification_service"
            ),
            "security_settings": readme_settings_async(
                "sidebar_management_settings", "security_settings"
            ),
            "voice_settings": readme_settings_async(
                "sidebar_management_settings", "voice_settings"
            ),
            "settings_history": readme_settings_async(
                "sidebar_management_settings", "settings_history"
            ),
            "more_settings": readme_settings_async(
                "sidebar_management_settings", "more_settings"
            ),
        }

        # 定义页面配置
        page_configs = [
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
                "voice_settings",
                "voiceSettingsInterface",
                "voice_settings_page",
                True,
            ),
            ("settings_history", "historyInterface", "history_page", True),
            (
                "more_settings",
                "moreSettingsInterface",
                "more_settings_page",
                True,
            ),
        ]

        # 根据设置创建对应的界面
        for setting_key, interface_attr, page_method, is_pivot in page_configs:
            setting_value = settings.get(setting_key)
            # 如果设置不为"不显示"(值不等于2)或者设置未定义，则创建界面
            if setting_value is None or setting_value != 2:
                interface = make_placeholder(interface_attr)
                setattr(self, interface_attr, interface)

                # 使用默认参数解决闭包问题
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

        # 单独处理更新页面和关于页面
        self.updateInterface = make_placeholder("updateInterface")

        def make_update_factory(iface=self.updateInterface):
            def factory(parent=iface, is_preview=False):
                page_instance = settings_window_page.update_page(
                    parent, is_preview=is_preview
                )
                return page_instance

            return factory

        self._deferred_factories["updateInterface"] = make_update_factory()
        self._deferred_factories_meta["updateInterface"] = {
            "is_pivot": False,
            "is_preview": False,
        }

        self.aboutInterface = make_placeholder("aboutInterface")

        def make_about_factory(iface=self.aboutInterface):
            def factory(parent=iface, is_preview=False):
                page_instance = settings_window_page.about_page(
                    parent, is_preview=is_preview
                )
                return page_instance

            return factory

        self._deferred_factories["aboutInterface"] = make_about_factory()
        self._deferred_factories_meta["aboutInterface"] = {
            "is_pivot": False,
            "is_preview": False,
        }

        # 把占位注册到导航，但不要在此刻实例化真实页面
        self.initNavigation()

        # 在窗口显示后启动针对非 pivot 页面的后台预热（分批创建）
        try:
            QTimer.singleShot(300, lambda: self._background_warmup_non_pivot())
        except Exception as e:
            logger.exception("Error during settings warmup: {}", e)

        # 连接堆叠窗口切换信号，在首次切换到占位时创建真实页面
        try:
            self.stackedWidget.currentChanged.connect(self._on_stacked_widget_changed)
        except Exception as e:
            logger.exception("Error creating deferred page: {}", e)

        # 在窗口显示后启动后台预热，分批创建其余页面，避免一次性阻塞
        try:
            QTimer.singleShot(300, lambda: self._background_warmup_pages())
        except Exception as e:
            logger.exception("Error scheduling background warmup pages: {}", e)

    def _on_stacked_widget_changed(self, index: int):
        """当导航切换到某个占位页时，按需创建真实页面内容，并卸载不活动的页面"""
        try:
            widget = self.stackedWidget.widget(index)
            if not widget:
                return
            name = widget.objectName()

            # 内存优化：卸载其他已加载的页面
            self._unload_inactive_pages(name)

            # 如果有延迟工厂且容器尚未填充内容，则创建真实页面
            if (
                name in getattr(self, "_deferred_factories", {})
                and widget.layout()
                and widget.layout().count() == 0
            ):
                factory = self._deferred_factories.pop(name)
                try:
                    logger.debug(f"正在创建页面 {name}，预览模式: {self.is_preview}")
                    # 传递is_preview参数给工厂函数
                    real_page = factory(is_preview=self.is_preview)
                    # real_page 会在其内部创建内容（PageTemplate 会在其内部事件循环中再创建内部内容），
                    # 我们把它作为子控件加入占位容器
                    widget.layout().addWidget(real_page)

                    # 记录已创建的页面
                    if not hasattr(self, "_created_pages"):
                        self._created_pages = {}
                    self._created_pages[name] = real_page

                    # 如果是 PivotPageTemplate，不再预加载所有子页面
                    # 子页面会在用户点击时按需加载
                    logger.debug(
                        f"设置页面已按需创建: {name}, 预览模式: {self.is_preview}"
                    )
                except Exception as e:
                    logger.error(f"延迟创建设置页面 {name} 失败: {e}")
        except Exception as e:
            logger.error(f"处理堆叠窗口改变失败: {e}")

    def _unload_inactive_pages(self, current_page: str):
        """卸载不活动的页面以释放内存

        Args:
            current_page: 当前激活的页面名称
        """
        # 最大同时保留在内存中的页面数量
        MAX_CACHED_SETTINGS_PAGES = 2

        if not hasattr(self, "_created_pages"):
            self._created_pages = {}

        if not hasattr(self, "_page_access_order"):
            self._page_access_order = []

        # 更新访问顺序
        if current_page in self._page_access_order:
            self._page_access_order.remove(current_page)
        self._page_access_order.append(current_page)

        # 获取已创建的页面列表
        created_pages = list(self._created_pages.keys())

        # 如果已创建页面数量超过限制，卸载最早访问的页面
        while len(created_pages) > MAX_CACHED_SETTINGS_PAGES:
            # 找到最早访问的页面（不包括当前页面）
            oldest_page = None
            for page_name in self._page_access_order:
                if page_name in created_pages and page_name != current_page:
                    oldest_page = page_name
                    break

            if oldest_page is None:
                # 没有可卸载的页面
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

            # 查找容器
            container = getattr(self, page_name, None)
            if container and container.layout():
                # 从布局中移除
                container.layout().removeWidget(real_page)

            # 安全删除widget
            real_page.deleteLater()

            # 重新添加工厂以便下次访问时可以重新创建
            from app.page_building import settings_window_page

            # 恢复工厂函数，支持is_preview参数
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
                is_preview=False: settings_window_page.about_page(
                    p, is_preview=is_preview
                ),
            }

            if page_name in factory_mapping:
                if not hasattr(self, "_deferred_factories"):
                    self._deferred_factories = {}
                self._deferred_factories[page_name] = factory_mapping[page_name]

            logger.debug(f"已卸载设置页面 {page_name} 以释放内存")
        except RuntimeError as e:
            logger.warning(f"卸载设置页面 {page_name} 时出现警告: {e}")
        except Exception as e:
            logger.error(f"卸载设置页面 {page_name} 失败: {e}")

    def _background_warmup_pages(
        self,
        interval_ms: int = SETTINGS_WARMUP_INTERVAL_MS,
        max_preload: int = SETTINGS_WARMUP_MAX_PRELOAD,
    ):
        """分批（间隔）创建剩余的设置页面，减少单次阻塞。

        内存优化：完全禁用后台预热，所有页面按需加载。

        参数:
            interval_ms: 每个页面创建间隔（毫秒）（已禁用）
            max_preload: 最大预加载数量（已禁用）
        """
        # 内存优化：完全禁用后台预热
        # 所有页面都将在用户首次访问时按需创建
        # 这可以将内存占用从1.2GB降低到350MB以下
        pass

    def _background_warmup_non_pivot(self, interval_ms: int = 80):
        """
        在设置窗口首次打开时，分批延时创建所有非 pivot（单页面）项，避免用户首次打开时卡顿。

        内存优化：禁用自动预热，完全按需加载

        Args:
            interval_ms: 每个页面创建的间隔毫秒数。
        """
        try:
            # 内存优化：完全禁用非pivot页面的自动预热
            # 所有页面都将在用户首次访问时按需创建
            pass
        except Exception as e:
            logger.error(f"后台预热非 pivot 页面失败: {e}")

    def _create_deferred_page(self, name: str):
        """根据名字创建对应延迟工厂并把结果加入占位容器"""
        try:
            if name not in getattr(self, "_deferred_factories", {}):
                return
            factory = self._deferred_factories.pop(name)

            # 查找对应的容器
            container = None
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
                "updateInterface",
                "aboutInterface",
            ]

            for attr in container_attrs:
                container_obj = getattr(self, attr, None)
                if container_obj and container_obj.objectName() == name:
                    container = container_obj
                    break

            if container is None:
                return

            # 如果容器已经被销毁或没有 layout，则跳过
            if not container or not hasattr(container, "layout"):
                return
            layout = container.layout()
            if layout is None:
                return

            try:
                real_page = factory(is_preview=self.is_preview)
            except RuntimeError as e:
                logger.error(f"创建延迟页面 {name} 失败（父容器可能已销毁）: {e}")
                return
            except Exception as e:
                logger.error(f"创建延迟页面 {name} 失败: {e}")
                return

            try:
                layout.addWidget(real_page)
                logger.debug(f"后台预热创建设置页面: {name}")
            except RuntimeError as e:
                logger.error(f"将延迟页面 {name} 插入容器失败（容器可能已销毁）: {e}")
                return
        except Exception as e:
            logger.error(f"_create_deferred_page 失败: {e}")

    def initNavigation(self):
        """初始化导航系统
        根据用户设置构建个性化菜单导航"""
        # 获取所有设置值
        settings = {
            "base_settings": readme_settings_async(
                "sidebar_management_settings", "base_settings"
            ),
            "name_management": readme_settings_async(
                "sidebar_management_settings", "name_management"
            ),
            "draw_settings": readme_settings_async(
                "sidebar_management_settings", "draw_settings"
            ),
            "floating_window_management": readme_settings_async(
                "sidebar_management_settings", "floating_window_management"
            ),
            "notification_service": readme_settings_async(
                "sidebar_management_settings", "notification_service"
            ),
            "security_settings": readme_settings_async(
                "sidebar_management_settings", "security_settings"
            ),
            "voice_settings": readme_settings_async(
                "sidebar_management_settings", "voice_settings"
            ),
            "settings_history": readme_settings_async(
                "sidebar_management_settings", "settings_history"
            ),
            "more_settings": readme_settings_async(
                "sidebar_management_settings", "more_settings"
            ),
        }

        # 定义导航项配置
        nav_configs = [
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
                "voice_settings",
                "voiceSettingsInterface",
                "voice_settings_item",
                "ic_fluent_person_voice_20_filled",
                "voice_settings",
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
        ]

        # 根据设置添加导航项
        for (
            setting_key,
            interface_attr,
            item_attr,
            icon_name,
            module,
            name_key,
        ) in nav_configs:
            setting_value = settings.get(setting_key)
            # 如果设置不为"不显示"(值不等于2)或者设置未定义，则添加导航项
            if setting_value is None or setting_value != 2:
                interface = getattr(self, interface_attr, None)
                if interface is not None:
                    # 确定位置：设置为1表示底部，其他情况为顶部
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

        # 关于页面始终显示在底部
        self.update_item = self.addSubInterface(
            self.updateInterface,
            get_theme_icon("ic_fluent_arrow_sync_20_filled"),
            get_content_name_async("update", "title"),
            position=NavigationItemPosition.BOTTOM,
        )

        self.about_item = self.addSubInterface(
            self.aboutInterface,
            get_theme_icon("ic_fluent_info_20_filled"),
            get_content_name_async("about", "title"),
            position=NavigationItemPosition.BOTTOM,
        )

        self.splashScreen.finish()

        # 连接信号
        self.showMainPageRequested.connect(self._handle_main_page_requested)

        # 默认导航到基础设置页面并确保其内容已创建
        if hasattr(self, "basicSettingsInterface") and self.basicSettingsInterface:
            # 延迟一点以确保UI初始化完成
            QTimer.singleShot(100, self._load_default_page)

    def _load_default_page(self):
        """加载默认页面（基础设置页面）"""
        try:
            # 先创建页面内容
            if "basicSettingsInterface" in getattr(self, "_deferred_factories", {}):
                self._create_deferred_page("basicSettingsInterface")

            # 然后切换到该页面
            if hasattr(self, "basicSettingsInterface") and self.basicSettingsInterface:
                self.switchTo(self.basicSettingsInterface)
                logger.debug("已自动导航到基础设置页面")
        except Exception as e:
            logger.error(f"加载默认页面失败: {e}")

    def closeEvent(self, event):
        """窗口关闭事件处理
        拦截窗口关闭事件，隐藏窗口并保存窗口大小"""
        self.hide()
        event.ignore()
        is_maximized = self.isMaximized()
        update_settings("settings", "is_maximized", is_maximized)
        if is_maximized:
            pass
        else:
            self.save_window_size(self.width(), self.height())

    def resizeEvent(self, event):
        """窗口大小变化事件处理
        检测窗口大小变化，但不启动尺寸记录倒计时，减少IO操作"""
        # 正常的窗口大小变化处理
        self.resize_timer.start(500)
        super().resizeEvent(event)

    def changeEvent(self, event):
        """窗口状态变化事件处理
        检测窗口最大化/恢复状态变化，保存正确的窗口大小"""
        # 检查是否是窗口状态变化
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
                        100,
                        lambda: self.resize(pre_maximized_width, pre_maximized_height),
                    )

        super().changeEvent(event)

    def save_window_size(self, setting_window_width, setting_window_height):
        """保存窗口大小
        记录当前窗口尺寸，下次启动时自动恢复"""
        # 检查是否启用了自动保存窗口大小功能
        auto_save_enabled = readme_settings_async(
            "basic_settings", "auto_save_window_size"
        )

        if auto_save_enabled:
            if not self.isMaximized():
                update_settings("settings", "height", setting_window_height)
                update_settings("settings", "width", setting_window_width)

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
