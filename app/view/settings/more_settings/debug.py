# ==================================================
# 导入库
# ==================================================

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


# ==================================================
# 调试设置
# ==================================================
class debug(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle("调试设置(DEBUG)")
        self.setBorderRadius(8)

        # 所有显示器下拉框
        self.all_monitor_combo_box = ComboBox()
        self.all_monitor_combo_box.addItems(self.get_monitor_list())

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_panel_separate_window_20_filled"),
            "显示器列表",
            "显示所有显示器列表(DEBUG)",
            self.all_monitor_combo_box,
        )

    def get_monitor_list(self):
        # 获取所有显示器名称
        monitor_list = []
        for screen in QApplication.instance().screens():
            monitor_list.append(screen.name())
        return monitor_list
