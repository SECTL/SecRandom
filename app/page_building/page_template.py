# ==================================================
# 导入库
# ==================================================
import importlib
import time

from loguru import logger

from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *


class PageTemplate(QFrame):
    # 暂时禁用实例缓存以解决初始化问题
    # _instances = {}
    def __new__(cls, content_widget_class=None, parent: QFrame = None):
        # 直接创建新实例，不使用缓存
        return super(PageTemplate, cls).__new__(cls)

    def __init__(self, content_widget_class=None, parent: QFrame = None):
        super().__init__(parent=parent)

        self.ui_created = False
        self.content_created = False
        self.content_widget_class = content_widget_class

        self.__connectSignalToSlot()

        QTimer.singleShot(0, self.create_ui_components)

    def __connectSignalToSlot(self):
        qconfig.themeChanged.connect(setTheme)

    def create_ui_components(self):
        """后台创建UI组件，避免堵塞进程"""
        if self.ui_created:
            return

        self.scroll_area_personal = SingleDirectionScrollArea(self)
        self.scroll_area_personal.setWidgetResizable(True)
        self.scroll_area_personal.setStyleSheet("""
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
        """)
        QScroller.grabGesture(
            self.scroll_area_personal.viewport(),
            QScroller.ScrollerGestureType.LeftMouseButtonGesture,
        )

        self.inner_frame_personal = QWidget(self.scroll_area_personal)
        self.inner_layout_personal = QVBoxLayout(self.inner_frame_personal)
        self.inner_layout_personal.setAlignment(
            Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignTop
        )

        self.scroll_area_personal.setWidget(self.inner_frame_personal)

        self.main_layout = QVBoxLayout(self)
        self.main_layout.addWidget(self.scroll_area_personal)

        self.ui_created = True

        if self.content_widget_class:
            QTimer.singleShot(0, self.create_content)

    def create_content(self):
        """后台创建内容组件，避免堵塞进程"""
        if not self.ui_created or self.content_created or not self.content_widget_class:
            return

        # 支持传入三种类型的 content_widget_class:
        # 1) 直接的类 / 可调用对象 -> content_widget_class(self)
        # 2) 字符串形式的导入路径，如 'app.view.settings.home:home' 或 'app.view.settings.home.home'
        #    -> 动态导入模块并获取类
        start = time.perf_counter()
        try:
            content_cls = None
            content_name = None
            if isinstance(self.content_widget_class, str):
                path = self.content_widget_class
                content_name = path
                if ":" in path:
                    module_name, attr = path.split(":", 1)
                else:
                    module_name, attr = path.rsplit(".", 1)
                module = importlib.import_module(module_name)
                content_cls = getattr(module, attr)
            else:
                content_cls = self.content_widget_class
                content_name = getattr(content_cls, "__name__", str(content_cls))

            # 实例化并添加到布局
            self.contentWidget = content_cls(self)
            self.inner_layout_personal.addWidget(self.contentWidget)
            self.content_created = True

            elapsed = time.perf_counter() - start
            logger.debug(f"创建内容组件 {content_name} 耗时: {elapsed:.3f}s")
        except Exception as e:
            elapsed = time.perf_counter() - start
            logger.error(f"创建内容组件失败 ({elapsed:.3f}s): {e}")

    def create_empty_content(self, message="该页面正在开发中，敬请期待！"):
        """创建空页面内容"""
        if self.content_created:
            return

        empty_widget = QWidget()
        empty_layout = QVBoxLayout(empty_widget)

        center_container = QWidget()
        center_layout = QVBoxLayout(center_container)
        center_layout.setAlignment(Qt.AlignCenter)
        center_layout.setSpacing(20)

        if message:
            custom_label = BodyLabel(message)
            custom_label.setAlignment(Qt.AlignCenter)
            custom_label.setFont(QFont(load_custom_font(), 12))
            center_layout.addWidget(custom_label)

        empty_layout.addWidget(center_container)
        empty_layout.addStretch()

        self.contentWidget = empty_widget
        self.inner_layout_personal.addWidget(self.contentWidget)
        self.content_created = True

    @classmethod
    def clear_instance_cache(cls):
        """清除实例缓存，用于强制重新创建页面"""
        cls._instances.clear()

    @classmethod
    def remove_instance(cls, content_widget_class=None, parent=None):
        """移除特定实例"""
        if content_widget_class is None:
            content_class_name = "None"
        else:
            if hasattr(content_widget_class, "__name__"):
                content_class_name = content_widget_class.__name__
            else:
                content_class_name = str(type(content_widget_class).__name__)

        parent_id = id(parent) if parent else "None"
        instance_key = f"{cls.__name__}_{content_class_name}_{parent_id}"

        if instance_key in cls._instances:
            del cls._instances[instance_key]


class PivotPageTemplate(QFrame):
    """Pivot 导航页面模板类，支持动态加载不同的页面组件"""

    def __init__(self, page_config: dict, parent: QFrame = None):
        """
        初始化 Pivot 页面模板

        Args:
            page_config: 页面配置字典，格式为 {"page_name": "display_name", ...}
            parent: 父窗口
        """
        super().__init__(parent=parent)

        self.page_config = page_config  # 页面配置字典
        self.ui_created = False
        self.pages = {}  # 存储页面组件
        self.current_page = None  # 当前页面
        self.base_path = "app.view.settings.list_management"  # 默认基础路径

        self.__connectSignalToSlot()

        QTimer.singleShot(0, self.create_ui_components)

    def __connectSignalToSlot(self):
        """连接信号与槽"""
        qconfig.themeChanged.connect(setTheme)

    def create_ui_components(self):
        """创建UI组件"""
        if self.ui_created:
            return

        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(0, 0, 0, 0)
        self.main_layout.setSpacing(0)

        # 创建 Pivot 控件
        self.pivot = SegmentedWidget(self)

        # 创建堆叠窗口控件
        self.stacked_widget = QStackedWidget(self)

        # 添加到主布局
        self.main_layout.addWidget(self.pivot, 0, Qt.AlignmentFlag.AlignHCenter)
        self.main_layout.addWidget(self.stacked_widget)

        # 连接信号
        self.stacked_widget.currentChanged.connect(self.on_current_index_changed)

        self.ui_created = True

        # 添加页面
        self.add_pages()

    def add_pages(self):
        """根据配置添加所有页面"""
        for page_name, display_name in self.page_config.items():
            self.add_page(page_name, display_name)

        # 如果有页面，设置第一个页面为当前页面
        if self.pages:
            first_page_name = next(iter(self.pages))
            self.switch_to_page(first_page_name)

    def add_page(self, page_name: str, display_name: str):
        """
        添加单个页面

        Args:
            page_name: 页面名称，用于导入模块
            display_name: 在 Pivot 中显示的名称
        """
        if not self.ui_created:
            # 如果UI尚未创建，延迟添加
            QTimer.singleShot(100, lambda: self.add_page(page_name, display_name))
            return

        # 创建滑动区域
        scroll_area = SingleDirectionScrollArea(self)
        scroll_area.setWidgetResizable(True)
        scroll_area.setStyleSheet("""
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
        """)
        QScroller.grabGesture(
            scroll_area.viewport(), QScroller.ScrollerGestureType.LeftMouseButtonGesture
        )

        # 创建内部框架
        inner_frame = QWidget(scroll_area)
        inner_layout = QVBoxLayout(inner_frame)
        inner_layout.setAlignment(
            Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignTop
        )

        scroll_area.setWidget(inner_frame)
        scroll_area.setObjectName(page_name)

        # 添加到堆叠窗口
        self.stacked_widget.addWidget(scroll_area)

        # 添加到 Pivot
        self.pivot.addItem(
            routeKey=page_name,
            text=display_name,
            onClick=lambda: self.switch_to_page(page_name),
        )

        # 存储滑动区域引用
        self.pages[page_name] = scroll_area

        # 延迟加载实际页面组件
        QTimer.singleShot(
            0,
            lambda: self._load_page_content(
                page_name, display_name, scroll_area, inner_layout
            ),
        )

    def _load_page_content(
        self,
        page_name: str,
        display_name: str,
        scroll_area: QScrollArea,
        inner_layout: QVBoxLayout,
    ):
        """
        后台加载页面内容，避免堵塞进程

        Args:
            page_name: 页面名称
            display_name: 在 Pivot 中显示的名称
            scroll_area: 滑动区域
            inner_layout: 内部布局
        """
        try:
            # 动态导入页面组件
            start = time.perf_counter()
            module = importlib.import_module(f"{self.base_path}.{page_name}")
            content_widget_class = getattr(module, page_name)

            # 创建页面组件
            widget = content_widget_class(self)
            widget.setObjectName(page_name)

            # 清除加载提示
            if inner_layout.count() > 0:
                item = inner_layout.itemAt(0)
                if item:
                    inner_layout.removeItem(item)
                    if item.widget():
                        item.widget().deleteLater()

            # 添加实际内容到内部布局
            inner_layout.addWidget(widget)

            elapsed = time.perf_counter() - start
            logger.debug(f"加载页面组件 {page_name} 耗时: {elapsed:.3f}s")

            # 如果当前页面就是正在加载的页面，确保滑动区域是当前可见的
            if self.current_page == page_name:
                self.stacked_widget.setCurrentWidget(scroll_area)

        except (ImportError, AttributeError) as e:
            print(f"无法导入页面组件 {page_name}: {e}")

            # 清除加载提示
            if inner_layout.count() > 0:
                item = inner_layout.itemAt(0)
                if item:
                    inner_layout.removeItem(item)
                    if item.widget():
                        item.widget().deleteLater()

            # 创建错误页面
            error_widget = QWidget()
            error_layout = QVBoxLayout(error_widget)
            error_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

            error_title = BodyLabel("页面加载失败")
            error_title.setAlignment(Qt.AlignmentFlag.AlignCenter)
            error_title.setFont(QFont(load_custom_font(), 16))

            error_content = BodyLabel(f"无法加载页面 {page_name}: {str(e)}")
            error_content.setAlignment(Qt.AlignmentFlag.AlignCenter)

            error_layout.addWidget(error_title)
            error_layout.addWidget(error_content)
            error_layout.addStretch()

            # 添加错误页面到内部布局
            inner_layout.addWidget(error_widget)

            # 如果当前页面就是正在加载的页面，确保滑动区域是当前可见的
            if self.current_page == page_name:
                self.stacked_widget.setCurrentWidget(scroll_area)

    def switch_to_page(self, page_name: str):
        """切换到指定页面"""
        if page_name in self.pages:
            self.stacked_widget.setCurrentWidget(self.pages[page_name])
            self.pivot.setCurrentItem(page_name)
            self.current_page = page_name

    def on_current_index_changed(self, index: int):
        """堆叠窗口索引改变时的处理"""
        widget = self.stacked_widget.widget(index)
        if widget:
            self.pivot.setCurrentItem(widget.objectName())
            self.current_page = widget.objectName()

    def get_current_page(self) -> str:
        """获取当前页面名称"""
        return self.current_page

    def get_page(self, page_name: str):
        """根据页面名称获取页面组件"""
        return self.pages.get(page_name, None)

    def set_base_path(self, base_path: str):
        """设置页面模块的基础路径"""
        self.base_path = base_path
