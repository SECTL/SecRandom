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


# ==================================================
# 浮动窗口管理 - 主容器
# ==================================================
class floating_window_management(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加基础设置组件
        self.basic_settings = floating_window_basic_settings(self)
        self.vBoxLayout.addWidget(self.basic_settings)

        # 添加外观设置组件
        self.appearance_settings = floating_window_appearance_settings(self)
        self.vBoxLayout.addWidget(self.appearance_settings)

        # 添加贴边设置组件
        self.edge_settings = floating_window_edge_settings(self)
        self.vBoxLayout.addWidget(self.edge_settings)


# ==================================================
# 浮动窗口管理 - 基础设置
# ==================================================
class floating_window_basic_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("floating_window_management", "basic_settings")
        )
        self.setBorderRadius(8)

        # 软件启动时浮窗显示隐藏开关
        self.startup_display_floating_window_switch = SwitchButton()
        self.startup_display_floating_window_switch.setOffText(
            get_content_switchbutton_name_async(
                "floating_window_management",
                "startup_display_floating_window",
                "disable",
            )
        )
        self.startup_display_floating_window_switch.setOnText(
            get_content_switchbutton_name_async(
                "floating_window_management",
                "startup_display_floating_window",
                "enable",
            )
        )
        self.startup_display_floating_window_switch.setChecked(
            readme_settings_async(
                "floating_window_management", "startup_display_floating_window"
            )
        )
        self.startup_display_floating_window_switch.checkedChanged.connect(
            self.startup_display_floating_window_switch_changed
        )

        # 浮窗透明度
        self.floating_window_opacity_spinbox = SpinBox()
        self.floating_window_opacity_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.floating_window_opacity_spinbox.setRange(0, 100)
        self.floating_window_opacity_spinbox.setSuffix("%")
        self.floating_window_opacity_spinbox.setValue(
            readme_settings_async(
                "floating_window_management", "floating_window_opacity"
            )
            * 100
        )
        self.floating_window_opacity_spinbox.valueChanged.connect(
            self.floating_window_opacity_spinbox_changed
        )

        # 重置浮窗位置按钮
        self.reset_floating_window_position_button = PushButton(
            get_content_pushbutton_name_async(
                "floating_window_management", "reset_floating_window_position_button"
            )
        )
        self.reset_floating_window_position_button.setText(
            get_content_name_async(
                "floating_window_management", "reset_floating_window_position_button"
            )
        )
        self.reset_floating_window_position_button.clicked.connect(
            self.reset_floating_window_position_button_clicked
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_desktop_sync_20_filled"),
            get_content_name_async(
                "floating_window_management", "startup_display_floating_window"
            ),
            get_content_description_async(
                "floating_window_management", "startup_display_floating_window"
            ),
            self.startup_display_floating_window_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_brightness_high_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_opacity"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_opacity"
            ),
            self.floating_window_opacity_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_reset_20_filled"),
            get_content_name_async(
                "floating_window_management", "reset_floating_window_position_button"
            ),
            get_content_description_async(
                "floating_window_management", "reset_floating_window_position_button"
            ),
            self.reset_floating_window_position_button,
        )

    def startup_display_floating_window_switch_changed(self, checked):
        update_settings(
            "floating_window_management", "startup_display_floating_window", checked
        )

    def floating_window_opacity_spinbox_changed(self, value):
        update_settings(
            "floating_window_management", "floating_window_opacity", value / 100
        )

    def reset_floating_window_position_button_clicked(self):
        # 这里应该实现重置浮窗位置的逻辑
        logger.debug("重置浮窗位置按钮被点击")


# ==================================================
# 浮动窗口管理 - 外观设置
# ==================================================
class floating_window_appearance_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("floating_window_management", "appearance_settings")
        )
        self.setBorderRadius(8)

        # 浮窗按钮控件配置下拉框
        self.floating_window_button_control_combo_box = ComboBox()
        self.floating_window_button_control_combo_box.addItems(
            get_content_combo_name_async(
                "floating_window_management", "floating_window_button_control"
            )
        )
        self.floating_window_button_control_combo_box.setCurrentIndex(
            readme_settings_async(
                "floating_window_management", "floating_window_button_control"
            )
        )
        self.floating_window_button_control_combo_box.currentIndexChanged.connect(
            self.floating_window_button_control_combo_box_changed
        )

        # 浮窗排列方式下拉框
        self.floating_window_placement_combo_box = ComboBox()
        self.floating_window_placement_combo_box.addItems(
            get_content_combo_name_async(
                "floating_window_management", "floating_window_placement"
            )
        )
        self.floating_window_placement_combo_box.setCurrentIndex(
            readme_settings_async(
                "floating_window_management", "floating_window_placement"
            )
        )
        self.floating_window_placement_combo_box.currentIndexChanged.connect(
            self.floating_window_placement_combo_box_changed
        )

        # 浮窗显示样式下拉框
        self.floating_window_display_style_combo_box = ComboBox()
        self.floating_window_display_style_combo_box.addItems(
            get_content_combo_name_async(
                "floating_window_management", "floating_window_display_style"
            )
        )
        self.floating_window_display_style_combo_box.setCurrentIndex(
            readme_settings_async(
                "floating_window_management", "floating_window_display_style"
            )
        )
        self.floating_window_display_style_combo_box.currentIndexChanged.connect(
            self.floating_window_display_style_combo_box_changed
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_button_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_button_control"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_button_control"
            ),
            self.floating_window_button_control_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_align_left_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_placement"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_placement"
            ),
            self.floating_window_placement_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_design_ideas_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_display_style"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_display_style"
            ),
            self.floating_window_display_style_combo_box,
        )

    def floating_window_button_control_combo_box_changed(self, index):
        update_settings(
            "floating_window_management", "floating_window_button_control", index
        )

    def floating_window_placement_combo_box_changed(self, index):
        update_settings(
            "floating_window_management", "floating_window_placement", index
        )

    def floating_window_display_style_combo_box_changed(self, index):
        update_settings(
            "floating_window_management", "floating_window_display_style", index
        )


# ==================================================
# 浮动窗口管理 - 贴边设置
# ==================================================
class floating_window_edge_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("floating_window_management", "edge_settings")
        )
        self.setBorderRadius(8)

        # 浮窗贴边开关
        self.floating_window_stick_to_edge_switch = SwitchButton()
        self.floating_window_stick_to_edge_switch.setOffText(
            get_content_switchbutton_name_async(
                "floating_window_management", "floating_window_stick_to_edge", "disable"
            )
        )
        self.floating_window_stick_to_edge_switch.setOnText(
            get_content_switchbutton_name_async(
                "floating_window_management", "floating_window_stick_to_edge", "enable"
            )
        )
        self.floating_window_stick_to_edge_switch.setChecked(
            readme_settings_async(
                "floating_window_management", "floating_window_stick_to_edge"
            )
        )
        self.floating_window_stick_to_edge_switch.checkedChanged.connect(
            self.floating_window_stick_to_edge_switch_changed
        )

        # 浮窗贴边回收秒数
        self.floating_window_stick_to_edge_recover_seconds_spinbox = SpinBox()
        self.floating_window_stick_to_edge_recover_seconds_spinbox.setFixedWidth(
            WIDTH_SPINBOX
        )
        self.floating_window_stick_to_edge_recover_seconds_spinbox.setRange(0, 25600)
        self.floating_window_stick_to_edge_recover_seconds_spinbox.setSuffix("秒")
        self.floating_window_stick_to_edge_recover_seconds_spinbox.setValue(
            readme_settings_async(
                "floating_window_management",
                "floating_window_stick_to_edge_recover_seconds",
            )
        )
        self.floating_window_stick_to_edge_recover_seconds_spinbox.valueChanged.connect(
            self.floating_window_stick_to_edge_recover_seconds_spinbox_changed
        )

        # 浮窗贴边显示样式下拉框
        self.floating_window_stick_to_edge_display_style_combo_box = ComboBox()
        self.floating_window_stick_to_edge_display_style_combo_box.addItems(
            get_content_combo_name_async(
                "floating_window_management",
                "floating_window_stick_to_edge_display_style",
            )
        )
        self.floating_window_stick_to_edge_display_style_combo_box.setCurrentIndex(
            readme_settings_async(
                "floating_window_management",
                "floating_window_stick_to_edge_display_style",
            )
        )
        self.floating_window_stick_to_edge_display_style_combo_box.currentIndexChanged.connect(
            self.floating_window_stick_to_edge_display_style_combo_box_changed
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_pin_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_stick_to_edge"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_stick_to_edge"
            ),
            self.floating_window_stick_to_edge_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_timer_20_filled"),
            get_content_name_async(
                "floating_window_management",
                "floating_window_stick_to_edge_recover_seconds",
            ),
            get_content_description_async(
                "floating_window_management",
                "floating_window_stick_to_edge_recover_seconds",
            ),
            self.floating_window_stick_to_edge_recover_seconds_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_desktop_sync_20_filled"),
            get_content_name_async(
                "floating_window_management",
                "floating_window_stick_to_edge_display_style",
            ),
            get_content_description_async(
                "floating_window_management",
                "floating_window_stick_to_edge_display_style",
            ),
            self.floating_window_stick_to_edge_display_style_combo_box,
        )

    def floating_window_stick_to_edge_switch_changed(self, checked):
        update_settings(
            "floating_window_management", "floating_window_stick_to_edge", checked
        )

    def floating_window_stick_to_edge_recover_seconds_spinbox_changed(self, value):
        update_settings(
            "floating_window_management",
            "floating_window_stick_to_edge_recover_seconds",
            value,
        )

    def floating_window_stick_to_edge_display_style_combo_box_changed(self, index):
        update_settings(
            "floating_window_management",
            "floating_window_stick_to_edge_display_style",
            index,
        )
