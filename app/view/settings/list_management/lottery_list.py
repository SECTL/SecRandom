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
# 抽奖名单
# ==================================================
class lottery_list(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("lottery_list", "title"))
        self.setBorderRadius(8)

        # 设置班级名称按钮
        self.pool_name_button = PushButton(
            get_content_name_async("lottery_list", "set_pool_name")
        )
        self.pool_name_button.clicked.connect(lambda: self.set_pool_name())

        # 选择奖池下拉框
        self.pool_name_combo = ComboBox()
        self.refresh_pool_list()  # 初始化奖池列表
        self.pool_name_combo.setCurrentIndex(
            readme_settings_async("lottery_list", "select_pool_name")
        )
        if not get_pool_name_list():
            self.pool_name_combo.setCurrentIndex(-1)
            self.pool_name_combo.setPlaceholderText(
                get_content_name_async("lottery_list", "select_pool_name")
            )
        self.pool_name_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_list", "select_pool_name", self.pool_name_combo.currentIndex()
            )
        )

        # 导入奖品名单按钮
        self.import_prize_button = PushButton(
            get_content_name_async("lottery_list", "import_prize_name")
        )
        self.import_prize_button.clicked.connect(lambda: self.import_prize_name())

        # 奖品设置按钮
        self.prize_setting_button = PushButton(
            get_content_name_async("lottery_list", "prize_setting")
        )
        self.prize_setting_button.clicked.connect(lambda: self.prize_setting())

        # 奖品权重设置按钮
        self.prize_weight_setting_button = PushButton(
            get_content_name_async("lottery_list", "prize_weight_setting")
        )
        self.prize_weight_setting_button.clicked.connect(
            lambda: self.prize_weight_setting()
        )

        # 导出奖品名单按钮
        self.export_prize_button = PushButton(
            get_content_name_async("lottery_list", "export_prize_name")
        )
        self.export_prize_button.clicked.connect(lambda: self.export_prize_name())

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_edit_20_filled"),
            get_content_name_async("lottery_list", "set_pool_name"),
            get_content_description_async("lottery_list", "set_pool_name"),
            self.pool_name_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("lottery_list", "select_pool_name"),
            get_content_description_async("lottery_list", "select_pool_name"),
            self.pool_name_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("lottery_list", "import_prize_name"),
            get_content_description_async("lottery_list", "import_prize_name"),
            self.import_prize_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_rename_20_filled"),
            get_content_name_async("lottery_list", "prize_setting"),
            get_content_description_async("lottery_list", "prize_setting"),
            self.prize_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_person_board_20_filled"),
            get_content_name_async("lottery_list", "prize_weight_setting"),
            get_content_description_async("lottery_list", "prize_weight_setting"),
            self.prize_weight_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("lottery_list", "export_prize_name"),
            get_content_description_async("lottery_list", "export_prize_name"),
            self.export_prize_button,
        )

        # 设置文件系统监视器
        self.setup_file_watcher()

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
