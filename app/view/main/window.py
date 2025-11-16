# ==================================================
# 导入库
# ==================================================
import sys
import subprocess

import loguru
from loguru import logger
from PySide6.QtWidgets import QApplication, QWidget
from PySide6.QtGui import QIcon
from PySide6.QtCore import QTimer, QEvent, Signal
from qfluentwidgets import MSFluentWindow, NavigationItemPosition

from app.tools.variable import MINIMUM_WINDOW_SIZE, APP_INIT_DELAY
from app.tools.path_utils import get_resources_path
from app.tools.path_utils import get_app_root
from app.tools.personalised import get_theme_icon
from app.Language.obtain_language import get_content_name_async
from app.Language.obtain_language import readme_settings_async, update_settings
from app.page_building.main_window_page import roll_call_page
from app.view.tray.tray import Tray


# ==================================================
# 主窗口类
# ==================================================
class MainWindow(MSFluentWindow):
    """主窗口类
    程序的核心控制中心"""

    showSettingsRequested = Signal()
    showSettingsRequestedAbout = Signal()

    def __init__(self):
        super().__init__()
        # 设置窗口对象名称，方便其他组件查找
        self.setObjectName("MainWindow")

        self.roll_call_page = None
        self.settingsInterface = None

        self.roll_call_page = None
        self.settingsInterface = None

        # resize_timer的初始化
        self.resize_timer = QTimer(self)
        self.resize_timer.setSingleShot(True)
        self.resize_timer.timeout.connect(
            lambda: self.save_window_size(self.width(), self.height())
        )

        # 设置窗口属性
        self.setMinimumSize(MINIMUM_WINDOW_SIZE[0], MINIMUM_WINDOW_SIZE[1])
        self.setWindowTitle("SecRandom")
        self.setWindowIcon(
            QIcon(str(get_resources_path("assets/icon", "secrandom-icon-paper.png")))
        )

        self._position_window()

        # 导入并创建托盘图标
        tray_icon = Tray(self)
        tray_icon.showSettingsRequested.connect(self.showSettingsRequested.emit)
        tray_icon.showSettingsRequestedAbout.connect(
            self.showSettingsRequestedAbout.emit
        )
        tray_icon.show_tray_icon()

        QTimer.singleShot(APP_INIT_DELAY, lambda: (self.createSubInterface()))

    def _position_window(self):
        """窗口定位
        根据屏幕尺寸和用户设置自动计算最佳位置"""
        is_maximized = readme_settings_async("window", "is_maximized")
        if is_maximized:
            pre_maximized_width = readme_settings_async("window", "pre_maximized_width")
            pre_maximized_height = readme_settings_async(
                "window", "pre_maximized_height"
            )
            self.resize(pre_maximized_width, pre_maximized_height)
            self._center_window()
            QTimer.singleShot(100, self.showMaximized)
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

        self.roll_call_page = roll_call_page(self)
        self.roll_call_page.setObjectName("roll_call_page")

        self.settingsInterface = QWidget(self)
        self.settingsInterface.setObjectName("settingsInterface")

        self.initNavigation()

    def initNavigation(self):
        """初始化导航系统
        根据用户设置构建个性化菜单导航"""
        self.addSubInterface(
            self.roll_call_page,
            get_theme_icon("ic_fluent_people_20_filled"),
            get_content_name_async("roll_call", "title"),
            position=NavigationItemPosition.TOP,
        )

        settings_item = self.addSubInterface(
            self.settingsInterface,
            get_theme_icon("ic_fluent_settings_20_filled"),
            "设置",
            position=NavigationItemPosition.BOTTOM,
        )
        settings_item.clicked.connect(lambda: self.showSettingsRequested.emit())
        settings_item.clicked.connect(lambda: self.switchTo(self.roll_call_page))

    def closeEvent(self, event):
        """窗口关闭事件处理
        拦截窗口关闭事件，隐藏窗口并保存窗口大小"""
        self.hide()
        event.ignore()

        # 保存当前窗口状态
        is_maximized = self.isMaximized()
        update_settings("window", "is_maximized", is_maximized)
        if is_maximized:
            pass
        else:
            self.save_window_size(self.width(), self.height())

    def resizeEvent(self, event):
        """窗口大小变化事件处理
        检测窗口大小变化，但不启动尺寸记录倒计时，减少IO操作"""
        self.resize_timer.start(500)
        super().resizeEvent(event)

    def changeEvent(self, event):
        """窗口状态变化事件处理
        检测窗口最大化/恢复状态变化，保存正确的窗口大小"""
        # 检查是否是窗口状态变化
        if event.type() == QEvent.Type.WindowStateChange:
            is_currently_maximized = self.isMaximized()
            was_maximized = readme_settings_async("window", "is_maximized")

            # 如果最大化状态发生变化
            if is_currently_maximized != was_maximized:
                # 更新最大化状态
                update_settings("window", "is_maximized", is_currently_maximized)

                # 如果进入最大化，保存当前窗口大小作为最大化前的大小
                if is_currently_maximized:
                    # 获取正常状态下的窗口大小
                    normal_geometry = self.normalGeometry()
                    update_settings(
                        "window", "pre_maximized_width", normal_geometry.width()
                    )
                    update_settings(
                        "window", "pre_maximized_height", normal_geometry.height()
                    )
                # 如果退出最大化，恢复到最大化前的大小
                else:
                    pre_maximized_width = readme_settings_async(
                        "window", "pre_maximized_width"
                    )
                    pre_maximized_height = readme_settings_async(
                        "window", "pre_maximized_height"
                    )
                    # 延迟执行，确保在最大化状态完全退出后再调整大小
                    QTimer.singleShot(
                        100,
                        lambda: self.resize(pre_maximized_width, pre_maximized_height),
                    )

        super().changeEvent(event)

    def save_window_size(self, width, height):
        """保存窗口大小
        记录当前窗口尺寸，下次启动时自动恢复"""
        # 只有在非最大化状态下才保存窗口大小
        if not self.isMaximized():
            update_settings("window", "height", height)
            update_settings("window", "width", width)

    def toggle_window(self):
        """切换窗口显示状态
        在显示和隐藏状态之间切换窗口，切换时自动激活点名界面"""
        if self.isVisible():
            self.hide()
            if self.isMinimized():
                self.showNormal()
                self.activateWindow()
                self.raise_()
        else:
            if self.isMinimized():
                self.showNormal()
                self.activateWindow()
                self.raise_()
            else:
                self.show()
                self.activateWindow()
                self.raise_()

    def close_window_secrandom(self):
        """关闭窗口
        执行安全验证后关闭程序，释放所有资源"""
        try:
            loguru.logger.remove()
        except Exception as e:
            logger.error(f"日志系统关闭出错: {e}")

        QApplication.quit()
        sys.exit(0)

    def restart_app(self):
        """重启应用程序
        执行安全验证后重启程序，清理所有资源"""
        try:
            working_dir = str(get_app_root())

            filtered_args = [arg for arg in sys.argv if not arg.startswith("--")]

            startup_info = subprocess.STARTUPINFO()
            startup_info.dwFlags |= subprocess.STARTF_USESHOWWINDOW

            subprocess.Popen(
                [sys.executable] + filtered_args,
                cwd=working_dir,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP
                | subprocess.DETACHED_PROCESS,
                startupinfo=startup_info,
            )
        except Exception as e:
            logger.error(f"启动新进程失败: {e}")
            return

        try:
            loguru.logger.remove()
        except Exception as e:
            logger.error(f"日志系统关闭出错: {e}")

        # 完全退出当前应用程序
        QApplication.quit()
        sys.exit(0)

    def show_about_tab(self):
        """显示关于页面
        打开设置窗口并导航到关于页面"""
        self.showSettingsRequestedAbout.emit()
