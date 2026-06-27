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
# 公平抽取设置
# ==================================================
class fair_draw(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 1. 基础开关设置 - 控制哪些因素参与公平计算
        self.basic_fair_settings_widget = basic_fair_settings(self)
        self.vBoxLayout.addWidget(self.basic_fair_settings_widget)

        # 2. 核心公平机制 - 包括频率函数、平均值差值保护等核心算法
        self.core_fair_mechanism_widget = core_fair_mechanism(self)
        self.vBoxLayout.addWidget(self.core_fair_mechanism_widget)

        # 3. 抽取保护设置 - 包括抽取后屏蔽等保护机制
        self.draw_protection_widget = draw_protection(self)
        self.vBoxLayout.addWidget(self.draw_protection_widget)

        # 4. 初始阶段设置 - 冷启动相关设置
        self.initial_stage_widget = cold_start_settings(self)
        self.vBoxLayout.addWidget(self.initial_stage_widget)

        # 5. 权重调整选项 - 包括权重范围、平衡权重等高级调整
        self.advanced_weight_widget = advanced_weight_settings(self)
        self.vBoxLayout.addWidget(self.advanced_weight_widget)


class basic_fair_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("fair_draw_settings", "basic_fair_settings")
        )
        self.setBorderRadius(8)

        # 总抽取次数是否纳入计算
        self.fair_draw_switch = SwitchButton()
        self.fair_draw_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw", "disable"
            )
        )
        self.fair_draw_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw", "enable"
            )
        )
        self.fair_draw_switch.setChecked(
            readme_settings_async("fair_draw_settings", "fair_draw")
        )
        self.fair_draw_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "fair_draw", self.fair_draw_switch.isChecked()
            )
        )

        # 抽小组次数是否纳入计算
        self.fair_draw_group_switch = SwitchButton()
        self.fair_draw_group_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw_group", "disable"
            )
        )
        self.fair_draw_group_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw_group", "enable"
            )
        )
        self.fair_draw_group_switch.setChecked(
            readme_settings_async("fair_draw_settings", "fair_draw_group")
        )
        self.fair_draw_group_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "fair_draw_group",
                self.fair_draw_group_switch.isChecked(),
            )
        )

        # 抽性别次数是否纳入计算
        self.fair_draw_gender_switch = SwitchButton()
        self.fair_draw_gender_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw_gender", "disable"
            )
        )
        self.fair_draw_gender_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw_gender", "enable"
            )
        )
        self.fair_draw_gender_switch.setChecked(
            readme_settings_async("fair_draw_settings", "fair_draw_gender")
        )
        self.fair_draw_gender_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "fair_draw_gender",
                self.fair_draw_gender_switch.isChecked(),
            )
        )

        # 距上次抽取时间是否纳入计算
        self.fair_draw_time_switch = SwitchButton()
        self.fair_draw_time_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw_time", "disable"
            )
        )
        self.fair_draw_time_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "fair_draw_time", "enable"
            )
        )
        self.fair_draw_time_switch.setChecked(
            readme_settings_async("fair_draw_settings", "fair_draw_time")
        )
        self.fair_draw_time_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "fair_draw_time",
                self.fair_draw_time_switch.isChecked(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "fair_draw"),
            get_content_description_async("fair_draw_settings", "fair_draw"),
            self.fair_draw_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "fair_draw_group"),
            get_content_description_async("fair_draw_settings", "fair_draw_group"),
            self.fair_draw_group_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "fair_draw_gender"),
            get_content_description_async("fair_draw_settings", "fair_draw_gender"),
            self.fair_draw_gender_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "fair_draw_time"),
            get_content_description_async("fair_draw_settings", "fair_draw_time"),
            self.fair_draw_time_switch,
        )


class cold_start_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("fair_draw_settings", "cold_start_settings")
        )
        self.setBorderRadius(8)

        # 冷启动模式开关
        self.cold_start_enabled_switch = SwitchButton()
        self.cold_start_enabled_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "cold_start_enabled", "disable"
            )
        )
        self.cold_start_enabled_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "cold_start_enabled", "enable"
            )
        )
        self.cold_start_enabled_switch.setChecked(
            readme_settings_async("fair_draw_settings", "cold_start_enabled")
        )
        self.cold_start_enabled_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "cold_start_enabled",
                self.cold_start_enabled_switch.isChecked(),
            )
        )

        # 冷启动轮次
        self.cold_start_rounds_spinbox = SpinBox()
        self.cold_start_rounds_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.cold_start_rounds_spinbox.setRange(1, 1000)
        self.cold_start_rounds_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "cold_start_rounds")
        )
        self.cold_start_rounds_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "cold_start_rounds",
                self.cold_start_rounds_spinbox.value(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_rotate_clockwise_20_filled"),
            get_content_name_async("fair_draw_settings", "cold_start_enabled"),
            get_content_description_async("fair_draw_settings", "cold_start_enabled"),
            self.cold_start_enabled_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_rotate_clockwise_20_filled"),
            get_content_name_async("fair_draw_settings", "cold_start_rounds"),
            get_content_description_async("fair_draw_settings", "cold_start_rounds"),
            self.cold_start_rounds_spinbox,
        )


# 核心公平机制设置
class core_fair_mechanism(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("fair_draw_settings", "core_fair_mechanism")
        )
        self.setBorderRadius(8)

        # 1. 频率函数设置
        # 频率惩罚函数类型
        self.frequency_function_combobox = ComboBox()
        self.frequency_function_combobox.setFixedWidth(WIDTH_SPINBOX)
        self.frequency_function_combobox.addItems(
            get_content_combo_name_async("fair_draw_settings", "frequency_function")
        )
        self.frequency_function_combobox.setCurrentIndex(
            readme_settings_async("fair_draw_settings", "frequency_function")
        )
        self.frequency_function_combobox.currentIndexChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "frequency_function",
                self.frequency_function_combobox.currentIndex(),
            )
        )

        # 频率惩罚权重
        self.frequency_weight_spinbox = DoubleSpinBox()
        self.frequency_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.frequency_weight_spinbox.setRange(0.01, 100.0)
        self.frequency_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "frequency_weight")
        )
        self.frequency_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "frequency_weight",
                self.frequency_weight_spinbox.value(),
            )
        )

        # 2. 平均值差值保护
        # 平均值差值保护开关
        self.avg_gap_protection_switch = SwitchButton()
        self.avg_gap_protection_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "enable_avg_gap_protection", "disable"
            )
        )
        self.avg_gap_protection_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "enable_avg_gap_protection", "enable"
            )
        )
        self.avg_gap_protection_switch.setChecked(
            readme_settings_async("fair_draw_settings", "enable_avg_gap_protection")
        )
        self.avg_gap_protection_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "enable_avg_gap_protection",
                self.avg_gap_protection_switch.isChecked(),
            )
        )

        # 差距阈值
        self.gap_threshold_spinbox = SpinBox()
        self.gap_threshold_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.gap_threshold_spinbox.setRange(1, 100)
        self.gap_threshold_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "gap_threshold")
        )
        self.gap_threshold_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "gap_threshold",
                self.gap_threshold_spinbox.value(),
            )
        )

        # 候选池最少人数
        self.min_pool_size_spinbox = SpinBox()
        self.min_pool_size_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.min_pool_size_spinbox.setRange(1, 1000)
        self.min_pool_size_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "min_pool_size")
        )
        self.min_pool_size_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "min_pool_size",
                self.min_pool_size_spinbox.value(),
            )
        )

        # 添加设置项到分组
        # 频率函数相关
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_clockwise_20_filled"),
            get_content_name_async("fair_draw_settings", "frequency_function"),
            get_content_description_async("fair_draw_settings", "frequency_function"),
            self.frequency_function_combobox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_clockwise_20_filled"),
            get_content_name_async("fair_draw_settings", "frequency_weight"),
            get_content_description_async("fair_draw_settings", "frequency_weight"),
            self.frequency_weight_spinbox,
        )

        # 平均值差值保护相关
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "enable_avg_gap_protection"),
            get_content_description_async(
                "fair_draw_settings", "enable_avg_gap_protection"
            ),
            self.avg_gap_protection_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "gap_threshold"),
            get_content_description_async("fair_draw_settings", "gap_threshold"),
            self.gap_threshold_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_lottery_20_filled"),
            get_content_name_async("fair_draw_settings", "min_pool_size"),
            get_content_description_async("fair_draw_settings", "min_pool_size"),
            self.min_pool_size_spinbox,
        )


# 抽取保护设置
class draw_protection(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("fair_draw_settings", "draw_protection"))
        self.setBorderRadius(8)

        # 启用抽取后屏蔽
        self.shield_enabled_switch = SwitchButton()
        self.shield_enabled_switch.setOffText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "shield_enabled", "disable"
            )
        )
        self.shield_enabled_switch.setOnText(
            get_content_switchbutton_name_async(
                "fair_draw_settings", "shield_enabled", "enable"
            )
        )
        self.shield_enabled_switch.setChecked(
            readme_settings_async("fair_draw_settings", "shield_enabled")
        )
        self.shield_enabled_switch.checkedChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "shield_enabled",
                self.shield_enabled_switch.isChecked(),
            )
        )

        # 屏蔽时间
        self.shield_time_spinbox = DoubleSpinBox()
        self.shield_time_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.shield_time_spinbox.setRange(0.1, 60.00)
        self.shield_time_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "shield_time")
        )
        self.shield_time_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "shield_time", self.shield_time_spinbox.value()
            )
        )

        # 屏蔽时间单位
        self.shield_time_unit_combobox = ComboBox()
        self.shield_time_unit_combobox.setFixedWidth(WIDTH_SPINBOX)
        self.shield_time_unit_combobox.addItems(
            get_content_combo_name_async("fair_draw_settings", "shield_time_unit")
        )
        self.shield_time_unit_combobox.setCurrentIndex(
            readme_settings_async("fair_draw_settings", "shield_time_unit")
        )
        self.shield_time_unit_combobox.currentIndexChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "shield_time_unit",
                self.shield_time_unit_combobox.currentIndex(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_shield_20_filled"),
            get_content_name_async("fair_draw_settings", "shield_enabled"),
            get_content_description_async("fair_draw_settings", "shield_enabled"),
            self.shield_enabled_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_shield_20_filled"),
            get_content_name_async("fair_draw_settings", "shield_time"),
            get_content_description_async("fair_draw_settings", "shield_time"),
            self.shield_time_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_shield_20_filled"),
            get_content_name_async("fair_draw_settings", "shield_time_unit"),
            get_content_description_async("fair_draw_settings", "shield_time_unit"),
            self.shield_time_unit_combobox,
        )


# 高级权重设置
class advanced_weight_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("fair_draw_settings", "advanced_weight_settings")
        )
        self.setBorderRadius(8)

        # 1. 权重范围设置
        # 设置基础权重
        self.base_weight_spinbox = DoubleSpinBox()
        self.base_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.base_weight_spinbox.setRange(0.01, 1000.0)
        self.base_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "base_weight")
        )
        self.base_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "base_weight", self.base_weight_spinbox.value()
            )
        )

        # 设置权重范围最小值
        self.min_weight_spinbox = DoubleSpinBox()
        self.min_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.min_weight_spinbox.setRange(0.01, 1000.0)
        self.min_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "min_weight")
        )
        self.min_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "min_weight", self.min_weight_spinbox.value()
            )
        )

        # 设置权重范围最大值
        self.max_weight_spinbox = DoubleSpinBox()
        self.max_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.max_weight_spinbox.setRange(0.01, 1000.0)
        self.max_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "max_weight")
        )
        self.max_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "max_weight", self.max_weight_spinbox.value()
            )
        )

        # 2. 平衡权重设置
        # 小组平衡权重
        self.group_weight_spinbox = DoubleSpinBox()
        self.group_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.group_weight_spinbox.setRange(0.01, 1000.0)
        self.group_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "group_weight")
        )
        self.group_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "group_weight", self.group_weight_spinbox.value()
            )
        )

        # 性别平衡权重
        self.gender_weight_spinbox = DoubleSpinBox()
        self.gender_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.gender_weight_spinbox.setRange(0.01, 1000.0)
        self.gender_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "gender_weight")
        )
        self.gender_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings",
                "gender_weight",
                self.gender_weight_spinbox.value(),
            )
        )

        # 时间因子权重
        self.time_weight_spinbox = DoubleSpinBox()
        self.time_weight_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.time_weight_spinbox.setRange(0.01, 1000.0)
        self.time_weight_spinbox.setValue(
            readme_settings_async("fair_draw_settings", "time_weight")
        )
        self.time_weight_spinbox.valueChanged.connect(
            lambda: update_settings(
                "fair_draw_settings", "time_weight", self.time_weight_spinbox.value()
            )
        )

        # 添加设置项到分组
        # 权重范围相关
        self.addGroup(
            get_theme_icon("ic_fluent_scale_fit_20_filled"),
            get_content_name_async("fair_draw_settings", "base_weight"),
            get_content_description_async("fair_draw_settings", "base_weight"),
            self.base_weight_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_scale_fit_20_filled"),
            get_content_name_async("fair_draw_settings", "min_weight"),
            get_content_description_async("fair_draw_settings", "min_weight"),
            self.min_weight_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_scale_fit_20_filled"),
            get_content_name_async("fair_draw_settings", "max_weight"),
            get_content_description_async("fair_draw_settings", "max_weight"),
            self.max_weight_spinbox,
        )

        # 平衡权重相关
        self.addGroup(
            get_theme_icon("ic_fluent_scales_20_filled"),
            get_content_name_async("fair_draw_settings", "group_weight"),
            get_content_description_async("fair_draw_settings", "group_weight"),
            self.group_weight_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_scales_20_filled"),
            get_content_name_async("fair_draw_settings", "gender_weight"),
            get_content_description_async("fair_draw_settings", "gender_weight"),
            self.gender_weight_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_scales_20_filled"),
            get_content_name_async("fair_draw_settings", "time_weight"),
            get_content_description_async("fair_draw_settings", "time_weight"),
            self.time_weight_spinbox,
        )
