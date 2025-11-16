# ==================================================
# 导入库
# ==================================================
from typing import Dict, Optional, Type

from loguru import logger
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *
from qframelesswindow import *

from app.tools.variable import *
from app.tools.settings_access import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.Language.obtain_language import *


class SimpleWindowTemplate(FramelessWindow):
    """简单窗口模板类

    提供一个更简单的窗口模板，不包含导航栏，适用于简单的对话框或弹出窗口。
    该类继承自FramelessWindow，提供了页面管理功能，可以动态添加、切换和移除页面。

    特性:
    - 无边框窗口设计，带有标准标题栏
    - 支持多页面管理，使用QStackedWidget实现页面切换
    - 提供页面添加、移除和切换的便捷方法
    - 支持从页面模板或已有控件创建页面
    - 窗口关闭时发出windowClosed信号

    使用示例:
        # 创建简单窗口
        window = SimpleWindowTemplate("我的窗口", parent=None)

        # 从页面模板添加页面
        page_instance = window.add_page_from_template("settings", SettingsPage)

        # 从已有控件添加页面
        custom_widget = QWidget()
        window.add_page_from_widget("custom", custom_widget)

        # 切换到指定页面
        window.switch_to_page("settings")

        # 显示窗口
        window.show()

    信号:
        windowClosed: 窗口关闭时发出，无参数

    属性:
        parent_window: 父窗口引用
        pages: 页面类字典 {page_name: page_class}
        page_instances: 页面实例字典 {page_name: page_instance}
        stacked_widget: 页面堆叠窗口控件
        main_layout: 主布局
    """

    # 信号定义
    windowClosed = Signal()

    def __init__(
        self,
        title: str = "窗口",
        width: int = 700,
        height: int = 500,
        parent: Optional[QWidget] = None,
    ):
        """
        初始化简单窗口模板

        Args:
            title: 窗口标题
            width: 窗口宽度
            height: 窗口高度
            parent: 父窗口
        """
        super().__init__(parent)

        # 保存父窗口引用
        self.parent_window = parent

        # 存储页面
        self.pages: Dict[str, Type] = {}
        self.page_instances: Dict[str, QWidget] = {}

        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(0, self.titleBar.height(), 0, 0)
        self.main_layout.setSpacing(0)

        # 创建堆叠窗口
        self.stacked_widget = QStackedWidget(self)
        self.main_layout.addWidget(self.stacked_widget)

        # 连接信号
        self.__connectSignalToSlot()

        # 直接创建UI组件，不使用延迟初始化
        self.create_ui_components()

        # 初始化窗口
        self.initWindow(title, width, height)

    def initWindow(self, title: str, width: int = 700, height: int = 500) -> None:
        """初始化窗口"""
        self.setTitleBar(StandardTitleBar(self))
        self.setMinimumSize(MINIMUM_WINDOW_SIZE[0], MINIMUM_WINDOW_SIZE[1])
        self.resize(width, height)
        self.setWindowIcon(
            QIcon(str(get_resources_path("assets/icon", "secrandom-icon-paper.png")))
        )
        self.setWindowTitle(title)
        self.titleBar.setAttribute(Qt.WidgetAttribute.WA_StyledBackground)
        # 设置标题栏字体
        custom_font = load_custom_font()
        title_font = QFont(custom_font, 9)
        self.titleBar.setFont(title_font)
        
        # 确保在设置标题栏后应用当前主题和自定义字体
        self._apply_current_theme()
        
        if self.parent_window is None:
            screen = QApplication.primaryScreen().availableGeometry()
            w, h = screen.width(), screen.height()
            self.move(w // 2 - self.width() // 2, h // 2 - self.height() // 2)

    def __connectSignalToSlot(self) -> None:
        """连接信号与槽"""
        try:
            qconfig.themeChanged.connect(self._on_theme_changed)
        except Exception as e:
            logger.error(f"连接信号时发生未知错误: {e}")

    def _on_theme_changed(self) -> None:
        """主题变化时自动更新窗口背景"""
        try:
            # 强制刷新窗口背景
            self._apply_current_theme()
        except Exception as e:
            logger.error(f"主题变化时更新窗口背景失败: {e}")

    def _apply_current_theme(self) -> None:
        """应用当前主题设置到窗口"""
        try:
            # 获取当前主题设置
            current_theme = readme_settings("basic_settings", "theme")
            
            # 根据主题设置窗口背景色
            if current_theme == "DARK":
                # 深色主题 - 设置深色背景
                self.setStyleSheet("background-color: #202020;")
                self.default_page.setStyleSheet("background-color: transparent;")
            elif current_theme == "AUTO":
                # 自动主题 - 根据系统设置
                try:
                    from darkdetect import isDark
                    if isDark():
                        self.setStyleSheet("background-color: #202020;")
                        self.default_page.setStyleSheet("background-color: transparent;")
                    else:
                        self.setStyleSheet("background-color: #ffffff;")
                        self.default_page.setStyleSheet("background-color: transparent;")
                except:
                    # 如果检测失败，使用浅色主题
                    self.setStyleSheet("background-color: #ffffff;")
                    self.default_page.setStyleSheet("background-color: transparent;")
            else:
                # 浅色主题
                self.setStyleSheet("background-color: #ffffff;")
                self.default_page.setStyleSheet("background-color: transparent;")
            
            # 应用标题栏自定义字体和颜色
            self._set_titlebar_colors()
                
            logger.debug(f"窗口主题已更新为: {current_theme}")
        except Exception as e:
            logger.error(f"应用主题时出错: {e}")
            # 设置默认的浅色背景作为备选
            self.setStyleSheet("background-color: #ffffff;")
            self.default_page.setStyleSheet("background-color: transparent;")

    def _set_titlebar_colors(self) -> None:
        """设置标题栏颜色"""
        try:
            # 判断是否为深色主题
            is_dark = is_dark_theme(qconfig)
            
            if is_dark:
                # 深色主题
                title_color = "#ffffff"  # 白色文字
                background_color = "#202020"
            else:
                # 浅色主题
                title_color = "#000000"  # 黑色文字
                background_color = "#ffffff"
            
            # 设置标题栏颜色样式
            titlebar_style = f"""
                QWidget {{
                    color: {title_color};
                    background-color: {background_color};
                }}
                QLabel {{
                    color: {title_color};
                    background-color: transparent;
                }}
                QPushButton {{
                    color: {title_color};
                    background-color: transparent;
                }}
                QToolButton {{
                    color: {title_color};
                    background-color: transparent;
                }}
                #minimizeButton, #maximizeButton, #closeButton {{
                    color: {title_color};
                    background-color: transparent;
                }}
                #minimizeButton:hover, #maximizeButton:hover, #closeButton:hover {{
                    background-color: rgba(255, 255, 255, 0.1);
                }}
                #closeButton:hover {{
                    background-color: rgba(232, 17, 35, 0.8);
                }}
            """
            self.titleBar.setStyleSheet(titlebar_style)
            
            logger.debug(f"标题栏颜色已设置: 文字色={title_color}, 背景色={background_color}")
        except Exception as e:
            logger.error(f"设置标题栏颜色失败: {e}")

    def create_ui_components(self) -> None:
        """创建UI组件"""
        try:
            # 创建默认页面
            self.default_page = QWidget()
            default_layout = QVBoxLayout(self.default_page)
            default_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

            # 添加默认页面到堆叠窗口
            self.stacked_widget.addWidget(self.default_page)
        except Exception as e:
            logger.error(f"创建UI组件时出错: {e}")
            raise

    def add_page_from_template(
        self, page_name: str, page_class: Type
    ) -> Optional[QWidget]:
        """
        从页面模板添加页面

        Args:
            page_name: 页面名称（唯一标识）
            page_class: 页面类
            width: 页面宽度
            height: 页面高度

        Returns:
            页面实例，如果创建失败则返回None
        """
        # 输入验证
        if not page_name or not isinstance(page_name, str):
            logger.error("页面名称必须是非空字符串")
            return None

        if page_name in self.page_instances:
            logger.warning(f"页面 {page_name} 已存在，将返回现有实例")
            return self.page_instances[page_name]

        try:
            # 创建页面实例
            page_instance = page_class(self)

            # 设置对象名称
            page_instance.setObjectName(page_name)

            # 添加到堆叠窗口
            self.stacked_widget.addWidget(page_instance)

            # 存储页面
            self.page_instances[page_name] = page_instance
            self.pages[page_name] = page_class
            return page_instance
        except Exception as e:
            logger.error(f"创建页面 {page_name} 时出错: {e}")
            return None

    def add_page_from_widget(self, page_name: str, widget: QWidget) -> QWidget:
        """
        从控件添加页面

        Args:
            page_name: 页面名称（唯一标识）
            widget: 页面控件

        Returns:
            页面控件

        Raises:
            ValueError: 如果输入参数无效
        """
        # 输入验证
        if not page_name or not isinstance(page_name, str):
            raise ValueError("页面名称必须是非空字符串")

        if page_name in self.page_instances:
            logger.warning(f"页面 {page_name} 已存在，将返回现有实例")
            return self.page_instances[page_name]

        # 设置对象名称
        widget.setObjectName(page_name)

        try:
            # 添加到堆叠窗口
            self.stacked_widget.addWidget(widget)

            # 存储页面
            self.page_instances[page_name] = widget
            return widget
        except Exception as e:
            logger.error(f"添加页面 {page_name} 到堆叠窗口时出错: {e}")
            raise

    def get_page(self, page_name: str) -> Optional[QWidget]:
        """
        获取页面实例

        Args:
            page_name: 页面名称

        Returns:
            页面实例，如果不存在则返回None
        """
        # 输入验证
        if not page_name or not isinstance(page_name, str):
            logger.warning("页面名称必须是非空字符串")
            return None

        page = self.page_instances.get(page_name, None)
        if page is None:
            logger.debug(f"请求的页面 {page_name} 不存在")

        return page

    def remove_page(self, page_name: str) -> bool:
        """
        移除页面

        Args:
            page_name: 页面名称

        Returns:
            是否成功移除页面
        """
        # 输入验证
        if not page_name or not isinstance(page_name, str):
            logger.error("页面名称必须是非空字符串")
            return False

        if page_name not in self.page_instances:
            logger.warning(f"尝试移除不存在的页面: {page_name}")
            return False

        try:
            page = self.page_instances[page_name]

            # 如果当前显示的是要删除的页面，先切换到默认页面
            if self.stacked_widget.currentWidget() == page:
                if self.default_page:
                    self.stacked_widget.setCurrentWidget(self.default_page)
                else:
                    logger.warning(
                        f"删除当前显示的页面 {page_name} 但没有默认页面可切换"
                    )

            # 从堆叠窗口中移除
            self.stacked_widget.removeWidget(page)

            # 从存储中移除
            del self.page_instances[page_name]
            if page_name in self.pages:
                del self.pages[page_name]
            return True
        except Exception as e:
            logger.error(f"移除页面 {page_name} 时出错: {e}")
            return False

    def switch_to_page(self, page_name: str) -> bool:
        """
        切换到指定页面

        Args:
            page_name: 页面名称

        Returns:
            是否成功切换到页面
        """
        # 输入验证
        if not page_name or not isinstance(page_name, str):
            logger.error("页面名称必须是非空字符串")
            return False

        if page_name not in self.page_instances:
            logger.warning(f"尝试切换到不存在的页面: {page_name}")
            return False

        try:
            target_page = self.page_instances[page_name]
            current_page = self.stacked_widget.currentWidget()

            # 检查是否已经在目标页面
            if current_page == target_page:
                return True

            # 切换页面
            self.stacked_widget.setCurrentWidget(target_page)
            return True
        except Exception as e:
            logger.error(f"切换到页面 {page_name} 时出错: {e}")
            return False

    def closeEvent(self, event) -> None:
        """窗口关闭事件处理"""
        self.windowClosed.emit()
        super().closeEvent(event)
