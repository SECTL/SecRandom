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
# 即抽通知设置类
# ==================================================
class instant_draw_notification_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加基础设置组件
        self.basic_settings_widget = basic_settings(self)
        self.vBoxLayout.addWidget(self.basic_settings_widget)

        # 添加窗口模式设置组件
        self.window_mode_widget = window_mode(self)
        self.vBoxLayout.addWidget(self.window_mode_widget)

        # 添加浮窗模式设置组件
        self.floating_window_widget = floating_window_settings(self)
        self.vBoxLayout.addWidget(self.floating_window_widget)


class basic_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async(
                "instant_draw_notification_settings", "basic_settings"
            )
        )
        self.setBorderRadius(8)

        # 选择通知模式下拉框
        self.notification_mode_combo_box = ComboBox()
        self.notification_mode_combo_box.addItems(
            get_content_combo_name_async(
                "instant_draw_notification_settings", "notification_mode"
            )
        )
        self.notification_mode_combo_box.setCurrentText(
            get_content_name_async(
                "instant_draw_notification_settings", "notification_mode"
            )
        )
        self.notification_mode_combo_box.currentIndexChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "notification_mode",
                self.notification_mode_combo_box.currentIndex(),
            )
        )

        # 是否开启动画开关
        self.animation_switch = SwitchButton()
        self.animation_switch.setOffText(
            get_content_switchbutton_name_async(
                "instant_draw_notification_settings", "animation", "disable"
            )
        )
        self.animation_switch.setOnText(
            get_content_switchbutton_name_async(
                "instant_draw_notification_settings", "animation", "enable"
            )
        )
        self.animation_switch.setChecked(
            readme_settings_async("instant_draw_notification_settings", "animation")
        )
        self.animation_switch.checkedChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "animation",
                self.animation_switch.isChecked(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_comment_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "notification_mode"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "notification_mode"
            ),
            self.notification_mode_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_sanitize_20_filled"),
            get_content_name_async("instant_draw_notification_settings", "animation"),
            get_content_description_async(
                "instant_draw_notification_settings", "animation"
            ),
            self.animation_switch,
        )


class window_mode(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("instant_draw_notification_settings", "window_mode")
        )
        self.setBorderRadius(8)

        # 设置窗口的显示位置下拉框
        self.window_position_combo_box = ComboBox()
        self.window_position_combo_box.addItems(
            get_content_combo_name_async(
                "instant_draw_notification_settings", "window_position"
            )
        )
        self.window_position_combo_box.setCurrentIndex(
            readme_settings_async(
                "instant_draw_notification_settings", "window_position"
            )
        )
        self.window_position_combo_box.currentIndexChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "window_position",
                self.window_position_combo_box.currentIndex(),
            )
        )

        # 水平偏移值
        self.horizontal_offset_spin_spinbox = SpinBox()
        self.horizontal_offset_spin_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.horizontal_offset_spin_spinbox.setRange(-25600, 25600)
        self.horizontal_offset_spin_spinbox.setSuffix("px")
        self.horizontal_offset_spin_spinbox.setValue(
            readme_settings_async(
                "instant_draw_notification_settings", "horizontal_offset"
            )
        )
        self.horizontal_offset_spin_spinbox.valueChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "horizontal_offset",
                self.horizontal_offset_spin_spinbox.value(),
            )
        )

        # 垂直偏移值
        self.vertical_offset_spin_spinbox = SpinBox()
        self.vertical_offset_spin_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.vertical_offset_spin_spinbox.setRange(-25600, 25600)
        self.vertical_offset_spin_spinbox.setSuffix("px")
        self.vertical_offset_spin_spinbox.setValue(
            readme_settings_async(
                "instant_draw_notification_settings", "vertical_offset"
            )
        )
        self.vertical_offset_spin_spinbox.valueChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "vertical_offset",
                self.vertical_offset_spin_spinbox.value(),
            )
        )

        # 窗口透明度
        self.window_transparency_spin_spinbox = SpinBox()
        self.window_transparency_spin_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.window_transparency_spin_spinbox.setRange(0, 100)
        self.window_transparency_spin_spinbox.setSuffix("%")
        self.window_transparency_spin_spinbox.setValue(
            readme_settings_async("instant_draw_notification_settings", "transparency")
            * 100
        )
        self.window_transparency_spin_spinbox.valueChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "transparency",
                self.window_transparency_spin_spinbox.value() / 100,
            )
        )

        # 选择启用的显示器下拉框
        self.enabled_monitor_combo_box = ComboBox()
        self.enabled_monitor_combo_box.addItems(self.get_monitor_list())
        if (
            readme_settings_async(
                "instant_draw_notification_settings", "enabled_monitor"
            )
            == "OFF"
        ):
            self.enabled_monitor_combo_box.setCurrentText(self.get_monitor_list()[0])
            update_settings(
                "instant_draw_notification_settings",
                "enabled_monitor",
                self.enabled_monitor_combo_box.currentText(),
            )
        self.enabled_monitor_combo_box.setCurrentText(
            readme_settings_async(
                "instant_draw_notification_settings", "enabled_monitor"
            )
        )
        self.enabled_monitor_combo_box.currentTextChanged.connect(
            lambda: self.on_first_monitor_changed(
                self.enabled_monitor_combo_box.currentText()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_text_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "enabled_monitor"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "enabled_monitor"
            ),
            self.enabled_monitor_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_position_to_back_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "window_position"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "window_position"
            ),
            self.window_position_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_align_stretch_horizontal_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "horizontal_offset"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "horizontal_offset"
            ),
            self.horizontal_offset_spin_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_align_stretch_vertical_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "vertical_offset"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "vertical_offset"
            ),
            self.vertical_offset_spin_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_transparency_square_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "transparency"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "transparency"
            ),
            self.window_transparency_spin_spinbox,
        )

    # 获取显示器列表
    def get_monitor_list(self):
        monitor_list = []
        for screen in QApplication.instance().screens():
            monitor_list.append(screen.name())
        return monitor_list


class floating_window_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async(
                "instant_draw_notification_settings", "floating_window_mode"
            )
        )
        self.setBorderRadius(8)

        # 窗口位置
        self.window_position_combo_box = ComboBox()
        self.window_position_combo_box.addItems(
            get_content_combo_name_async(
                "instant_draw_notification_settings", "floating_window_position"
            )
        )
        self.window_position_combo_box.setCurrentIndex(
            readme_settings_async(
                "instant_draw_notification_settings", "floating_window_position"
            )
        )
        self.window_position_combo_box.currentTextChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "floating_window_position",
                self.window_position_combo_box.currentIndex(),
            )
        )

        # 水平偏移值
        self.horizontal_offset_spin_spinbox = SpinBox()
        self.horizontal_offset_spin_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.horizontal_offset_spin_spinbox.setRange(-25600, 25600)
        self.horizontal_offset_spin_spinbox.setSuffix("px")
        self.horizontal_offset_spin_spinbox.setValue(
            readme_settings_async(
                "instant_draw_notification_settings",
                "floating_window_horizontal_offset",
            )
        )
        self.horizontal_offset_spin_spinbox.valueChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "floating_window_horizontal_offset",
                self.horizontal_offset_spin_spinbox.value(),
            )
        )

        # 垂直偏移值
        self.vertical_offset_spin_spinbox = SpinBox()
        self.vertical_offset_spin_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.vertical_offset_spin_spinbox.setRange(-25600, 25600)
        self.vertical_offset_spin_spinbox.setSuffix("px")
        self.vertical_offset_spin_spinbox.setValue(
            readme_settings_async(
                "instant_draw_notification_settings", "floating_window_vertical_offset"
            )
        )
        self.vertical_offset_spin_spinbox.valueChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "floating_window_vertical_offset",
                self.vertical_offset_spin_spinbox.value(),
            )
        )

        # 窗口透明度
        self.window_transparency_spin_spinbox = SpinBox()
        self.window_transparency_spin_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.window_transparency_spin_spinbox.setRange(0, 100)
        self.window_transparency_spin_spinbox.setSuffix("%")
        self.window_transparency_spin_spinbox.setValue(
            readme_settings_async(
                "instant_draw_notification_settings", "floating_window_transparency"
            )
            * 100
        )
        self.window_transparency_spin_spinbox.valueChanged.connect(
            lambda: update_settings(
                "instant_draw_notification_settings",
                "floating_window_transparency",
                self.window_transparency_spin_spinbox.value() / 100,
            )
        )

        # 选择启用的显示器下拉框
        self.enabled_monitor_combo_box = ComboBox()
        self.enabled_monitor_combo_box.addItems(self.get_monitor_list())
        if (
            readme_settings_async(
                "instant_draw_notification_settings", "floating_window_enabled_monitor"
            )
            == "OFF"
        ):
            self.enabled_monitor_combo_box.setCurrentText(self.get_monitor_list()[0])
            update_settings(
                "instant_draw_notification_settings",
                "floating_window_enabled_monitor",
                self.enabled_monitor_combo_box.currentText(),
            )
        self.enabled_monitor_combo_box.setCurrentText(
            readme_settings_async(
                "instant_draw_notification_settings", "floating_window_enabled_monitor"
            )
        )
        self.enabled_monitor_combo_box.currentTextChanged.connect(
            lambda: self.on_floating_first_monitor_changed(
                self.enabled_monitor_combo_box.currentText()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_text_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "floating_window_enabled_monitor"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "floating_window_enabled_monitor"
            ),
            self.enabled_monitor_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_position_to_back_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "floating_window_position"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "floating_window_position"
            ),
            self.window_position_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_align_stretch_horizontal_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings",
                "floating_window_horizontal_offset",
            ),
            get_content_description_async(
                "instant_draw_notification_settings",
                "floating_window_horizontal_offset",
            ),
            self.horizontal_offset_spin_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_align_stretch_vertical_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "floating_window_vertical_offset"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "floating_window_vertical_offset"
            ),
            self.vertical_offset_spin_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_transparency_square_20_filled"),
            get_content_name_async(
                "instant_draw_notification_settings", "floating_window_transparency"
            ),
            get_content_description_async(
                "instant_draw_notification_settings", "floating_window_transparency"
            ),
            self.window_transparency_spin_spinbox,
        )

    # 获取显示器列表
    def get_monitor_list(self):
        monitor_list = []
        for screen in QApplication.instance().screens():
            monitor_list.append(screen.name())
        return monitor_list
