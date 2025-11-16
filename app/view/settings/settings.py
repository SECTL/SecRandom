# ==================================================
# 导入库
# ==================================================

from loguru import logger
from PySide6.QtWidgets import QApplication
from PySide6.QtGui import QIcon
from PySide6.QtCore import QTimer, QEvent, Signal
from qfluentwidgets import MSFluentWindow, NavigationItemPosition

from app.tools.variable import (
    MINIMUM_WINDOW_SIZE,
    APP_INIT_DELAY,
    SETTINGS_WARMUP_INTERVAL_MS,
    SETTINGS_WARMUP_MAX_PRELOAD,
)
from app.tools.path_utils import get_resources_path
from app.tools.personalised import get_theme_icon
from app.Language.obtain_language import get_content_name_async
from app.tools.settings_access import readme_settings_async, update_settings

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *


# ==================================================
# 主窗口类
# ==================================================
class SettingsWindow(MSFluentWindow):
    """主窗口类
    程序的核心控制中心"""

    showSettingsRequested = Signal()
    showSettingsRequestedAbout = Signal()

    def __init__(self, parent=None):
        super().__init__()
        self.setObjectName("settingWindow")
        self.parent = parent

        # 初始化变量
        self.homeInterface = None
        self.basicSettingsInterface = None
        self.listManagementInterface = None
        self.extractionSettingsInterface = None
        self.notificationSettingsInterface = None
        self.safetySettingsInterface = None
        self.customSettingsInterface = None
        self.voiceSettingsInterface = None
        self.historyInterface = None
        self.moreSettingsInterface = None
        self.aboutInterface = None

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
            QIcon(str(get_resources_path("assets/icon", "secrandom-icon-paper.png")))
        )

        # 窗口定位
        self._position_window()

        # 启动页面
        self.splashScreen = SplashScreen(self.windowIcon(), self)
        self.splashScreen.setIconSize(QSize(256, 256))
        self.show()

        # 初始化子界面
        QTimer.singleShot(APP_INIT_DELAY, lambda: (self.createSubInterface()))

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
            QTimer.singleShot(100, self.showMaximized)
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

    def createSubInterface(self):
        """创建子界面
        搭建子界面导航系统"""
        # 延迟创建页面：先创建轻量占位容器并注册工厂
        from app.page_building import settings_window_page

        # 存储占位 -> factory 映射
        self._deferred_factories = {}

        def make_placeholder(name: str):
            w = QWidget()
            w.setObjectName(name)
            # 使用空布局以便后续将真正页面加入
            layout = QVBoxLayout(w)
            layout.setContentsMargins(0, 0, 0, 0)
            return w

        self.homeInterface = make_placeholder("homeInterface")
        self._deferred_factories["homeInterface"] = (
            lambda parent=self.homeInterface: settings_window_page.home_page(parent)
        )

        self.basicSettingsInterface = make_placeholder("basicSettingsInterface")
        self._deferred_factories["basicSettingsInterface"] = (
            lambda parent=self.basicSettingsInterface: settings_window_page.basic_settings_page(
                parent
            )
        )

        self.listManagementInterface = make_placeholder("listManagementInterface")
        self._deferred_factories["listManagementInterface"] = (
            lambda parent=self.listManagementInterface: settings_window_page.list_management_page(
                parent
            )
        )

        self.extractionSettingsInterface = make_placeholder(
            "extractionSettingsInterface"
        )
        self._deferred_factories["extractionSettingsInterface"] = (
            lambda parent=self.extractionSettingsInterface: settings_window_page.extraction_settings_page(
                parent
            )
        )

        self.notificationSettingsInterface = make_placeholder(
            "notificationSettingsInterface"
        )
        self._deferred_factories["notificationSettingsInterface"] = (
            lambda parent=self.notificationSettingsInterface: settings_window_page.notification_settings_page(
                parent
            )
        )

        self.safetySettingsInterface = make_placeholder("safetySettingsInterface")
        self._deferred_factories["safetySettingsInterface"] = (
            lambda parent=self.safetySettingsInterface: settings_window_page.safety_settings_page(
                parent
            )
        )

        self.customSettingsInterface = make_placeholder("customSettingsInterface")
        self._deferred_factories["customSettingsInterface"] = (
            lambda parent=self.customSettingsInterface: settings_window_page.custom_settings_page(
                parent
            )
        )

        self.voiceSettingsInterface = make_placeholder("voiceSettingsInterface")
        self._deferred_factories["voiceSettingsInterface"] = (
            lambda parent=self.voiceSettingsInterface: settings_window_page.voice_settings_page(
                parent
            )
        )

        self.historyInterface = make_placeholder("historyInterface")
        self._deferred_factories["historyInterface"] = (
            lambda parent=self.historyInterface: settings_window_page.history_page(
                parent
            )
        )

        self.moreSettingsInterface = make_placeholder("moreSettingsInterface")
        self._deferred_factories["moreSettingsInterface"] = (
            lambda parent=self.moreSettingsInterface: settings_window_page.more_settings_page(
                parent
            )
        )

        self.aboutInterface = make_placeholder("aboutInterface")
        self._deferred_factories["aboutInterface"] = (
            lambda parent=self.aboutInterface: settings_window_page.about_page(parent)
        )

        # 把占位注册到导航，但不要在此刻实例化真实页面
        self.initNavigation()

        # 连接堆叠窗口切换信号，在首次切换到占位时创建真实页面
        try:
            self.stackedWidget.currentChanged.connect(self._on_stacked_widget_changed)
        except Exception:
            pass

        # 在窗口显示后启动后台预热，分批创建其余页面，避免一次性阻塞
        try:
            QTimer.singleShot(300, lambda: self._background_warmup_pages())
        except Exception:
            pass

    def _on_stacked_widget_changed(self, index: int):
        """当导航切换到某个占位页时，按需创建真实页面内容"""
        try:
            widget = self.stackedWidget.widget(index)
            if not widget:
                return
            name = widget.objectName()
            # 如果有延迟工厂且容器尚未填充内容，则创建真实页面
            if (
                name in getattr(self, "_deferred_factories", {})
                and widget.layout()
                and widget.layout().count() == 0
            ):
                factory = self._deferred_factories.pop(name)
                try:
                    real_page = factory()
                    # real_page 会在其内部创建内容（PageTemplate 会在事件循环中再创建内部内容），
                    # 我们把它作为子控件加入占位容器
                    widget.layout().addWidget(real_page)
                    logger.debug(f"设置页面已按需创建: {name}")
                except Exception as e:
                    logger.error(f"延迟创建设置页面 {name} 失败: {e}")
        except Exception as e:
            logger.error(f"处理堆叠窗口改变失败: {e}")

    def _background_warmup_pages(
        self,
        interval_ms: int = SETTINGS_WARMUP_INTERVAL_MS,
        max_preload: int = SETTINGS_WARMUP_MAX_PRELOAD,
    ):
        """分批（间隔）创建剩余的设置页面，减少单次阻塞。

        参数:
            interval_ms: 每个页面创建间隔（毫秒）
        """
        try:
            # 复制键避免在迭代时修改字典
            names = list(getattr(self, "_deferred_factories", {}).keys())
            if not names:
                return
            # 仅预热有限数量的页面，避免一次性占用主线程
            names_to_preload = names[:max_preload]
            logger.debug(f"后台预热将创建 {len(names_to_preload)} / {len(names)} 个页面")
            # 仅为要预热的页面调度创建，避免一次性调度所有页面
            for i, name in enumerate(names_to_preload):
                # 延迟创建，避免短时间内占用主线程
                QTimer.singleShot(
                    interval_ms * i,
                    (lambda n=name: self._create_deferred_page(n)),
                )
        except Exception as e:
            logger.error(f"后台预热设置页面失败: {e}")

    def _create_deferred_page(self, name: str):
        """根据名字创建对应延迟工厂并把结果加入占位容器"""
        try:
            if name not in getattr(self, "_deferred_factories", {}):
                return
            factory = self._deferred_factories.pop(name)
            # 找到对应占位容器
            container = None
            for w in [
                self.homeInterface,
                self.basicSettingsInterface,
                self.listManagementInterface,
                self.extractionSettingsInterface,
                self.notificationSettingsInterface,
                self.safetySettingsInterface,
                self.customSettingsInterface,
                self.voiceSettingsInterface,
                self.historyInterface,
                self.moreSettingsInterface,
                self.aboutInterface,
            ]:
                if w and w.objectName() == name:
                    container = w
                    break
            if container is None:
                return
            try:
                real_page = factory()
                container.layout().addWidget(real_page)
                logger.debug(f"后台预热创建设置页面: {name}")
            except Exception as e:
                logger.error(f"创建延迟页面 {name} 失败: {e}")
        except Exception as e:
            logger.error(f"_create_deferred_page 失败: {e}")

    def initNavigation(self):
        """初始化导航系统
        根据用户设置构建个性化菜单导航"""
        self.addSubInterface(
            self.homeInterface,
            get_theme_icon("ic_fluent_home_20_filled"),
            get_content_name_async("home", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.basicSettingsInterface,
            get_theme_icon("ic_fluent_wrench_settings_20_filled"),
            get_content_name_async("basic_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.listManagementInterface,
            get_theme_icon("ic_fluent_list_20_filled"),
            get_content_name_async("list_management", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.extractionSettingsInterface,
            get_theme_icon("ic_fluent_archive_20_filled"),
            get_content_name_async("extraction_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.notificationSettingsInterface,
            get_theme_icon("ic_fluent_comment_note_20_filled"),
            get_content_name_async("notification_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.safetySettingsInterface,
            get_theme_icon("ic_fluent_shield_20_filled"),
            get_content_name_async("safety_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.customSettingsInterface,
            get_theme_icon("ic_fluent_person_edit_20_filled"),
            get_content_name_async("custom_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.voiceSettingsInterface,
            get_theme_icon("ic_fluent_person_voice_20_filled"),
            get_content_name_async("voice_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.historyInterface,
            get_theme_icon("ic_fluent_history_20_filled"),
            get_content_name_async("history", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.moreSettingsInterface,
            get_theme_icon("ic_fluent_more_horizontal_20_filled"),
            get_content_name_async("more_settings", "title"),
            position=NavigationItemPosition.TOP,
        )

        self.addSubInterface(
            self.aboutInterface,
            get_theme_icon("ic_fluent_info_20_filled"),
            get_content_name_async("about", "title"),
            position=NavigationItemPosition.BOTTOM,
        )

        self.splashScreen.finish()

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
