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
# 进阶设置
# ==================================================
class advanced_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加公平抽取设置组件
        self.fair_draw_widget = advanced_fair_draw(self)
        self.vBoxLayout.addWidget(self.fair_draw_widget)


class advanced_fair_draw(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("advanced_settings", "advanced_fair_draw"))
        self.setBorderRadius(8)

        # 总抽取次数是否纳入计算
        self.fair_draw_switch = SwitchButton()
        self.fair_draw_switch.setOffText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw", "disable"
            )
        )
        self.fair_draw_switch.setOnText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw", "enable"
            )
        )
        self.fair_draw_switch.setChecked(
            readme_settings_async("advanced_settings", "fair_draw")
        )
        self.fair_draw_switch.checkedChanged.connect(
            lambda: update_settings(
                "advanced_settings", "fair_draw", self.fair_draw_switch.isChecked()
            )
        )

        # 抽小组次数是否纳入计算
        self.fair_draw_group_switch = SwitchButton()
        self.fair_draw_group_switch.setOffText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw_group", "disable"
            )
        )
        self.fair_draw_group_switch.setOnText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw_group", "enable"
            )
        )
        self.fair_draw_group_switch.setChecked(
            readme_settings_async("advanced_settings", "fair_draw_group")
        )
        self.fair_draw_group_switch.checkedChanged.connect(
            lambda: update_settings(
                "advanced_settings",
                "fair_draw_group",
                self.fair_draw_group_switch.isChecked(),
            )
        )

        # 抽性别次数是否纳入计算
        self.fair_draw_gender_switch = SwitchButton()
        self.fair_draw_gender_switch.setOffText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw_gender", "disable"
            )
        )
        self.fair_draw_gender_switch.setOnText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw_gender", "enable"
            )
        )
        self.fair_draw_gender_switch.setChecked(
            readme_settings_async("advanced_settings", "fair_draw_gender")
        )
        self.fair_draw_gender_switch.checkedChanged.connect(
            lambda: update_settings(
                "advanced_settings",
                "fair_draw_gender",
                self.fair_draw_gender_switch.isChecked(),
            )
        )

        # 距上次抽取时间是否纳入计算
        self.fair_draw_time_switch = SwitchButton()
        self.fair_draw_time_switch.setOffText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw_time", "disable"
            )
        )
        self.fair_draw_time_switch.setOnText(
            get_content_switchbutton_name_async(
                "advanced_settings", "fair_draw_time", "enable"
            )
        )
        self.fair_draw_time_switch.setChecked(
            readme_settings_async("advanced_settings", "fair_draw_time")
        )
        self.fair_draw_time_switch.checkedChanged.connect(
            lambda: update_settings(
                "advanced_settings",
                "fair_draw_time",
                self.fair_draw_time_switch.isChecked(),
            )
        )

        # 设置基础权重
        self.base_weight_spinbox = DoubleSpinBox()
        self.base_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.base_weight_spinbox.setRange(0.01, 1000.00)
        self.base_weight_spinbox.setValue(
            readme_settings_async("advanced_settings", "base_weight")
        )
        self.base_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "advanced_settings", "base_weight", self.base_weight_spinbox.value()
            )
        )

        # 设置权重范围最小值
        self.min_weight_spinbox = DoubleSpinBox()
        self.min_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.min_weight_spinbox.setRange(0.01, 1000.00)
        self.min_weight_spinbox.setValue(
            readme_settings_async("advanced_settings", "min_weight")
        )
        self.min_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "advanced_settings", "min_weight", self.min_weight_spinbox.value()
            )
        )

        # 设置权重范围最大值
        self.max_weight_spinbox = DoubleSpinBox()
        self.max_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.max_weight_spinbox.setRange(0.01, 1000.00)
        self.max_weight_spinbox.setValue(
            readme_settings_async("advanced_settings", "max_weight")
        )
        self.max_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "advanced_settings", "max_weight", self.max_weight_spinbox.value()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "fair_draw"),
            get_content_description_async("advanced_settings", "fair_draw"),
            self.fair_draw_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "fair_draw_group"),
            get_content_description_async("advanced_settings", "fair_draw_group"),
            self.fair_draw_group_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "fair_draw_gender"),
            get_content_description_async("advanced_settings", "fair_draw_gender"),
            self.fair_draw_gender_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "fair_draw_time"),
            get_content_description_async("advanced_settings", "fair_draw_time"),
            self.fair_draw_time_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "base_weight"),
            get_content_description_async("advanced_settings", "base_weight"),
            self.base_weight_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "min_weight"),
            get_content_description_async("advanced_settings", "min_weight"),
            self.min_weight_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_clock_20_filled"),
            get_content_name_async("advanced_settings", "max_weight"),
            get_content_description_async("advanced_settings", "max_weight"),
            self.max_weight_spinbox,
        )
