# ==================================================
# 导入库
# ==================================================

from loguru import logger
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from app.tools.list import *


# ==================================================
# 历史记录管理
# ==================================================
class history_management(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加点名历史记录管理组件
        self.roll_call_history = roll_call_history(self)
        self.vBoxLayout.addWidget(self.roll_call_history)

        # 添加抽奖历史记录管理组件
        self.lottery_history = lottery_history(self)
        self.vBoxLayout.addWidget(self.lottery_history)


class roll_call_history(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("history_management", "roll_call"))
        self.setBorderRadius(8)

        # 是否开启点名历史记录
        self.show_roll_call_history_button_switch = SwitchButton()
        self.show_roll_call_history_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "history_management", "show_roll_call_history", "disable"
            )
        )
        self.show_roll_call_history_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "history_management", "show_roll_call_history", "enable"
            )
        )
        self.show_roll_call_history_button_switch.setChecked(
            readme_settings_async("history_management", "show_roll_call_history")
        )
        self.show_roll_call_history_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "history_management",
                "show_roll_call_history",
                self.show_roll_call_history_button_switch.isChecked(),
            )
        )

        # 选择班级下拉框
        self.class_name_combo = ComboBox()
        self.refresh_class_list()  # 初始化班级列表
        self.class_name_combo.setCurrentIndex(
            readme_settings_async("history_management", "select_class_name")
        )
        if not get_class_name_list():
            self.class_name_combo.setCurrentIndex(-1)
            self.class_name_combo.setPlaceholderText(
                get_content_name_async("history_management", "select_class_name")
            )
        self.class_name_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "history_management",
                "select_class_name",
                self.class_name_combo.currentIndex(),
            )
        )

        # 清除历史记录按钮
        self.clear_roll_call_history_button = PushButton(
            get_content_pushbutton_name_async(
                "history_management", "clear_roll_call_history"
            )
        )
        self.clear_roll_call_history_button.clicked.connect(
            lambda: self.clear_roll_call_history
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_history_20_filled"),
            get_content_name_async("history_management", "show_roll_call_history"),
            get_content_description_async(
                "history_management", "show_roll_call_history"
            ),
            self.show_roll_call_history_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("history_management", "select_class_name"),
            get_content_description_async("history_management", "select_class_name"),
            self.class_name_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_community_20_filled"),
            get_content_name_async("history_management", "clear_roll_call_history"),
            get_content_description_async(
                "history_management", "clear_roll_call_history"
            ),
            self.clear_roll_call_history_button,
        )

        # 设置文件系统监视器
        self.setup_file_watcher()

    def clear_roll_call_history(self):
        """清除点名历史记录"""
        logger.debug("清除点名历史记录")

    def setup_file_watcher(self):
        """设置文件系统监视器，监控班级名单文件夹的变化"""
        # 获取班级名单文件夹路径
        roll_call_list_dir = get_path("app/resources/list/roll_call_list")

        # 确保目录存在
        if not roll_call_list_dir.exists():
            logger.warning(f"班级名单文件夹不存在: {roll_call_list_dir}")
            return

        # 创建文件系统监视器
        self.file_watcher = QFileSystemWatcher()

        # 监视目录
        self.file_watcher.addPath(str(roll_call_list_dir))

        # 连接信号
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)
        # logger.debug(f"已设置文件监视器，监控目录: {roll_call_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成
        QTimer.singleShot(500, self.refresh_class_list)

    def refresh_class_list(self):
        """刷新班级下拉框列表"""
        # 保存当前选中的班级名称
        current_class_name = self.class_name_combo.currentText()

        # 获取最新的班级列表
        class_list = get_class_name_list()

        # 清空并重新添加班级列表
        self.class_name_combo.clear()
        self.class_name_combo.addItems(class_list)

        # 尝试恢复之前选中的班级
        if current_class_name and current_class_name in class_list:
            index = class_list.index(current_class_name)
            self.class_name_combo.setCurrentIndex(index)
        elif not class_list:
            self.class_name_combo.setCurrentIndex(-1)
            self.class_name_combo.setPlaceholderText(
                get_content_name_async("roll_call_list", "select_class_name")
            )

        # logger.debug(f"班级列表已刷新，共 {len(class_list)} 个班级")


class lottery_history(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("history_management", "lottery_history"))
        self.setBorderRadius(8)

        # 是否开启抽奖历史记录
        self.show_lottery_history_button_switch = SwitchButton()
        self.show_lottery_history_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "history_management", "show_lottery_history", "disable"
            )
        )
        self.show_lottery_history_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "history_management", "show_lottery_history", "enable"
            )
        )
        self.show_lottery_history_button_switch.setChecked(
            readme_settings_async("history_management", "show_lottery_history")
        )
        self.show_lottery_history_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "history_management",
                "show_lottery_history",
                self.show_lottery_history_button_switch.isChecked(),
            )
        )

        # 选择奖池下拉框
        self.pool_name_combo = ComboBox()
        self.refresh_pool_list()  # 初始化奖池列表
        self.pool_name_combo.setCurrentIndex(
            readme_settings_async("history_management", "select_pool_name")
        )
        if not get_pool_name_list():
            self.pool_name_combo.setCurrentIndex(-1)
            self.pool_name_combo.setPlaceholderText(
                get_content_name_async("history_management", "select_pool_name")
            )
        self.pool_name_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "history_management",
                "select_pool_name",
                self.pool_name_combo.currentIndex(),
            )
        )

        # 清除历史记录按钮
        self.clear_lottery_history_button = PushButton(
            get_content_pushbutton_name_async(
                "history_management", "clear_lottery_history"
            )
        )
        self.clear_lottery_history_button.clicked.connect(self.clear_lottery_history)

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_history_20_filled"),
            get_content_name_async("history_management", "show_lottery_history"),
            get_content_description_async("history_management", "show_lottery_history"),
            self.show_lottery_history_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("history_management", "select_pool_name"),
            get_content_description_async("history_management", "select_pool_name"),
            self.pool_name_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_community_20_filled"),
            get_content_name_async("history_management", "clear_lottery_history"),
            get_content_description_async(
                "history_management", "clear_lottery_history"
            ),
            self.clear_lottery_history_button,
        )

        # 设置文件系统监视器
        self.setup_file_watcher()

    def clear_lottery_history(self):
        """清除抽奖历史记录"""
        logger.debug("清除抽奖历史记录")

    def setup_file_watcher(self):
        """设置文件系统监视器，监控奖池名单文件夹的变化"""
        # 获取奖池名单文件夹路径
        lottery_list_dir = get_path("app/resources/list/lottery_list")

        # 确保目录存在
        if not lottery_list_dir.exists():
            logger.warning(f"奖池名单文件夹不存在: {lottery_list_dir}")
            return

        # 创建文件系统监视器
        self.file_watcher = QFileSystemWatcher()

        # 监视目录
        self.file_watcher.addPath(str(lottery_list_dir))

        # 连接信号
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)
        # logger.debug(f"已设置文件监视器，监控目录: {lottery_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成导致的错误
        QTimer.singleShot(500, self.refresh_pool_list)

    def refresh_pool_list(self):
        """刷新奖池下拉框列表"""
        # 保存当前选中的奖池名称
        current_pool_name = self.pool_name_combo.currentText()

        # 获取最新的奖池列表
        pool_list = get_pool_name_list()

        # 清空并重新添加奖池列表
        self.pool_name_combo.clear()
        self.pool_name_combo.addItems(pool_list)

        # 尝试恢复之前选中的奖池
        if current_pool_name and current_pool_name in pool_list:
            index = pool_list.index(current_pool_name)
            self.pool_name_combo.setCurrentIndex(index)
        elif not pool_list:
            self.pool_name_combo.setCurrentIndex(-1)
            self.pool_name_combo.setPlaceholderText(
                get_content_name_async("lottery_list", "select_pool_name")
            )

        # logger.debug(f"奖池列表已刷新，共 {len(pool_list)} 个奖池")
