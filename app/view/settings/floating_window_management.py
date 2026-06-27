# ==================================================
# 导入库
# ==================================================

import os
import ctypes

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
from app.common.windows.uiaccess import is_uiaccess_process
from app.view.components.checkable_combo_box import MultiSelectionComboBox


# ==================================================
# 浮窗管理 - 主容器
# ==================================================
class floating_window_management(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 创建基础设置
        self.basic_settings = floating_window_basic_settings(self)
        self.vBoxLayout.addWidget(self.basic_settings)

        # 创建外观设置
        self.appearance_settings = floating_window_appearance_settings(self)
        self.vBoxLayout.addWidget(self.appearance_settings)

        # 创建贴边设置
        self.edge_settings = floating_window_edge_settings(self)
        self.vBoxLayout.addWidget(self.edge_settings)

        # 创建前台应用隐藏设置
        self.foreground_hiding_settings = floating_window_foreground_hiding_settings(
            self
        )
        self.vBoxLayout.addWidget(self.foreground_hiding_settings)

        # 存储浮窗实例的引用
        self.levitation_window = None

    def set_levitation_window(self, window):
        """设置浮窗实例引用"""
        self.levitation_window = window
        # 连接外观设置变化信号到浮窗重建方法
        self.appearance_settings.appearance_settings_changed.connect(
            self._on_appearance_settings_changed
        )

    def _on_appearance_settings_changed(self):
        """处理外观设置变更"""
        if self.levitation_window:
            self.levitation_window.rebuild_ui()


# ==================================================
# 浮窗管理 - 基础设置
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
            get_bool_setting(
                "floating_window_management", "startup_display_floating_window", True
            )
        )
        self.startup_display_floating_window_switch.checkedChanged.connect(
            lambda checked: update_settings(
                "floating_window_management", "startup_display_floating_window", checked
            )
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
            lambda value: update_settings(
                "floating_window_management", "floating_window_opacity", value / 100
            )
        )

        self.floating_window_topmost_mode_combo_box = ComboBox()
        self.floating_window_topmost_mode_combo_box.addItems(
            get_content_combo_name_async(
                "floating_window_management", "floating_window_topmost_mode"
            )
        )
        topmost_mode = readme_settings_async(
            "floating_window_management", "floating_window_topmost_mode"
        )
        self.floating_window_topmost_mode_combo_box.setCurrentIndex(int(topmost_mode))
        self.floating_window_topmost_mode_combo_box.currentIndexChanged.connect(
            self.floating_window_topmost_mode_combo_box_changed
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

        # 浮窗可拖动开关
        self.floating_window_draggable_switch = SwitchButton()
        self.floating_window_draggable_switch.setOffText(
            get_content_switchbutton_name_async(
                "floating_window_management",
                "floating_window_draggable",
                "disable",
            )
        )
        self.floating_window_draggable_switch.setOnText(
            get_content_switchbutton_name_async(
                "floating_window_management",
                "floating_window_draggable",
                "enable",
            )
        )
        self.floating_window_draggable_switch.setChecked(
            readme_settings_async(
                "floating_window_management", "floating_window_draggable"
            )
        )
        self.floating_window_draggable_switch.checkedChanged.connect(
            lambda checked: update_settings(
                "floating_window_management", "floating_window_draggable", checked
            )
        )

        # 浮窗长按拖动时间
        self.floating_window_long_press_duration_spinbox = SpinBox()
        self.floating_window_long_press_duration_spinbox.setFixedWidth(WIDTH_SPINBOX)
        self.floating_window_long_press_duration_spinbox.setRange(50, 3000)
        self.floating_window_long_press_duration_spinbox.setSingleStep(100)
        self.floating_window_long_press_duration_spinbox.setSuffix("ms")
        self.floating_window_long_press_duration_spinbox.setValue(
            readme_settings_async(
                "floating_window_management", "floating_window_long_press_duration"
            )
        )
        self.floating_window_long_press_duration_spinbox.valueChanged.connect(
            lambda value: update_settings(
                "floating_window_management",
                "floating_window_long_press_duration",
                value,
            )
        )

        # 无焦点模式开关
        self.do_not_steal_focus_switch = SwitchButton()
        self.do_not_steal_focus_switch.setOffText(
            get_content_switchbutton_name_async(
                "floating_window_management", "do_not_steal_focus", "disable"
            )
        )
        self.do_not_steal_focus_switch.setOnText(
            get_content_switchbutton_name_async(
                "floating_window_management", "do_not_steal_focus", "enable"
            )
        )
        self.do_not_steal_focus_switch.setChecked(
            readme_settings_async("floating_window_management", "do_not_steal_focus")
        )
        self.do_not_steal_focus_switch.checkedChanged.connect(
            lambda checked: update_settings(
                "floating_window_management", "do_not_steal_focus", checked
            )
        )

        self.extend_quick_draw_component_switch = SwitchButton()
        self.extend_quick_draw_component_switch.setOffText(
            get_content_switchbutton_name_async(
                "floating_window_management", "extend_quick_draw_component", "disable"
            )
        )
        self.extend_quick_draw_component_switch.setOnText(
            get_content_switchbutton_name_async(
                "floating_window_management", "extend_quick_draw_component", "enable"
            )
        )
        self.extend_quick_draw_component_switch.setChecked(
            readme_settings_async(
                "floating_window_management", "extend_quick_draw_component"
            )
        )
        self.extend_quick_draw_component_switch.checkedChanged.connect(
            lambda checked: update_settings(
                "floating_window_management", "extend_quick_draw_component", checked
            )
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
            get_theme_icon("ic_fluent_pin_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_topmost_mode"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_topmost_mode"
            ),
            self.floating_window_topmost_mode_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_gesture_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_draggable"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_draggable"
            ),
            self.floating_window_draggable_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_gesture_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_long_press_duration"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_long_press_duration"
            ),
            self.floating_window_long_press_duration_spinbox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_lock_open_20_filled"),
            get_content_name_async("floating_window_management", "do_not_steal_focus"),
            get_content_description_async(
                "floating_window_management", "do_not_steal_focus"
            ),
            self.do_not_steal_focus_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_panel_right_20_filled"),
            get_content_name_async(
                "floating_window_management", "extend_quick_draw_component"
            ),
            get_content_description_async(
                "floating_window_management", "extend_quick_draw_component"
            ),
            self.extend_quick_draw_component_switch,
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

    def floating_window_topmost_mode_combo_box_changed(self, index):
        previous_index = int(
            readme_settings_async(
                "floating_window_management", "floating_window_topmost_mode"
            )
        )

        # 如果当前已经是 UIA 进程，切换到 UIA 模式不需要重启
        if index == 2 and is_uiaccess_process():
            update_settings(
                "floating_window_management", "floating_window_topmost_mode", index
            )
            return

        # 如果当前是 UIA 进程，且主窗口也是 UIA 模式，切换回非 UIA 模式不需要重启（因为进程必须保持 UIA）
        if previous_index == 2 and index != 2 and is_uiaccess_process():
            main_window_mode = int(
                readme_settings_async("basic_settings", "main_window_topmost_mode") or 0
            )
            if main_window_mode == 2:
                update_settings(
                    "floating_window_management", "floating_window_topmost_mode", index
                )
                return

        if previous_index == 2 and index != 2:
            dialog = MessageBox(
                get_content_name_async(
                    "floating_window_management", "uia_topmost_restart_dialog_title"
                ),
                get_content_name_async(
                    "floating_window_management",
                    "uia_topmost_disable_restart_dialog_content",
                ),
                self.window(),
            )
            dialog.yesButton.setText(
                get_content_name_async(
                    "floating_window_management",
                    "uia_topmost_disable_restart_dialog_ok_btn",
                )
            )
            dialog.cancelButton.setText(
                get_content_name_async(
                    "floating_window_management",
                    "uia_topmost_restart_dialog_cancel_btn",
                )
            )
            if dialog.exec():
                update_settings(
                    "floating_window_management", "floating_window_topmost_mode", index
                )
            else:
                blocker = QSignalBlocker(self.floating_window_topmost_mode_combo_box)
                self.floating_window_topmost_mode_combo_box.setCurrentIndex(
                    previous_index
                )
                del blocker
            return

        if index == 2 and previous_index != 2:
            dialog = MessageBox(
                get_content_name_async(
                    "floating_window_management", "uia_topmost_restart_dialog_title"
                ),
                get_content_name_async(
                    "floating_window_management", "uia_topmost_restart_dialog_content"
                ),
                self.window(),
            )
            dialog.yesButton.setText(
                get_content_name_async(
                    "floating_window_management",
                    "uia_topmost_restart_dialog_restart_btn",
                )
            )
            dialog.cancelButton.setText(
                get_content_name_async(
                    "floating_window_management",
                    "uia_topmost_restart_dialog_cancel_btn",
                )
            )
            if dialog.exec():
                update_settings(
                    "floating_window_management", "floating_window_topmost_mode", index
                )
                try:
                    from app.common.windows.uiaccess import (
                        ELEVATE_RESTART_ENV,
                        UIACCESS_RESTART_ENV,
                    )

                    os.environ[str(UIACCESS_RESTART_ENV)] = "1"
                    try:
                        is_admin = bool(ctypes.windll.shell32.IsUserAnAdmin())
                    except Exception:
                        is_admin = False
                    if not is_admin:
                        os.environ[str(ELEVATE_RESTART_ENV)] = "1"
                except Exception:
                    pass
                QApplication.exit(EXIT_CODE_RESTART)
            else:
                blocker = QSignalBlocker(self.floating_window_topmost_mode_combo_box)
                self.floating_window_topmost_mode_combo_box.setCurrentIndex(
                    previous_index
                )
                del blocker
            return

        update_settings(
            "floating_window_management", "floating_window_topmost_mode", index
        )

    def reset_floating_window_position_button_clicked(self):
        """重置浮窗位置按钮点击处理"""
        # 更新设置为默认位置
        update_settings("float_position", "x", 100)
        update_settings("float_position", "y", 100)


# ==================================================
# 浮窗管理 - 外观设置
# ==================================================
class floating_window_appearance_settings(GroupHeaderCardWidget):
    appearance_settings_changed = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("floating_window_management", "appearance_settings")
        )
        self.setBorderRadius(8)

        # 浮窗按钮控件配置下拉框
        self.floating_window_button_control_combo_box = MultiSelectionComboBox(
            minimum_checked=1
        )
        self.floating_window_button_control_combo_box.addItem(
            get_content_name_async("floating_window_management", "roll_call_button"),
            userData="roll_call",
        )
        self.floating_window_button_control_combo_box.addItem(
            get_content_name_async("floating_window_management", "quick_draw_button"),
            userData="quick_draw",
        )
        self.floating_window_button_control_combo_box.addItem(
            get_content_name_async("floating_window_management", "lottery_button"),
            userData="lottery",
        )
        self.floating_window_button_control_combo_box.addItem(
            get_content_name_async("floating_window_management", "face_draw_button"),
            userData="face_draw",
        )
        self.floating_window_button_control_combo_box.addItem(
            get_content_name_async("floating_window_management", "timer_button"),
            userData="timer",
        )
        self.floating_window_button_control_combo_box.setCheckedData(
            self._normalize_button_control_value(
                readme_settings_async(
                    "floating_window_management", "floating_window_button_control"
                )
            )
        )
        self.floating_window_button_control_combo_box.checkedDataChanged.connect(
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

        # 浮窗大小下拉框
        self.floating_window_size_combo_box = ComboBox()
        self.floating_window_size_combo_box.addItems(
            get_content_combo_name_async(
                "floating_window_management", "floating_window_size"
            )
        )
        self.floating_window_size_combo_box.setCurrentIndex(
            readme_settings_async("floating_window_management", "floating_window_size")
        )
        self.floating_window_size_combo_box.currentIndexChanged.connect(
            self.floating_window_size_combo_box_changed
        )

        self.floating_window_theme_combo_box = ComboBox()
        follow_global_text = get_content_name_async(
            "floating_window_management", "floating_window_theme_follow_global"
        )
        basic_theme_items = get_content_combo_name_async("basic_settings", "theme")
        if isinstance(basic_theme_items, list):
            basic_theme_items = basic_theme_items[:-1]
        else:
            basic_theme_items = []
        self.floating_window_theme_combo_box.addItems(
            [follow_global_text] + list(basic_theme_items)
        )
        self.floating_window_theme_combo_box.setCurrentIndex(
            int(
                readme_settings_async(
                    "floating_window_management", "floating_window_theme"
                )
                or 0
            )
        )
        self.floating_window_theme_combo_box.currentIndexChanged.connect(
            self.floating_window_theme_combo_box_changed
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
        self.addGroup(
            get_theme_icon("ic_fluent_resize_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_size"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_size"
            ),
            self.floating_window_size_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_dark_theme_20_filled"),
            get_content_name_async(
                "floating_window_management", "floating_window_theme"
            ),
            get_content_description_async(
                "floating_window_management", "floating_window_theme"
            ),
            self.floating_window_theme_combo_box,
        )

    def _normalize_button_control_value(self, value):
        allowed = {"roll_call", "quick_draw", "lottery", "face_draw", "timer"}
        if isinstance(value, list):
            keys = []
            for v in value:
                if isinstance(v, str):
                    k = v.strip()
                    if k in allowed and k not in keys:
                        keys.append(k)
            return keys

        combos = [
            ["roll_call"],
            ["quick_draw"],
            ["lottery"],
            ["roll_call", "quick_draw"],
            ["roll_call", "lottery"],
            ["quick_draw", "lottery"],
            ["roll_call", "quick_draw", "lottery"],
            ["timer"],
            ["roll_call", "timer"],
            ["quick_draw", "timer"],
            ["lottery", "timer"],
            ["roll_call", "quick_draw", "timer"],
            ["roll_call", "lottery", "timer"],
            ["quick_draw", "lottery", "timer"],
            ["roll_call", "quick_draw", "lottery", "timer"],
        ]
        try:
            idx = int(value or 0)
        except Exception:
            idx = 0
        if idx < 0 or idx >= len(combos):
            idx = 0
        return combos[idx]

    def floating_window_button_control_combo_box_changed(self, keys):
        update_settings(
            "floating_window_management", "floating_window_button_control", keys
        )
        self.appearance_settings_changed.emit()

    def floating_window_placement_combo_box_changed(self, index):
        update_settings(
            "floating_window_management", "floating_window_placement", index
        )
        self.appearance_settings_changed.emit()

    def floating_window_display_style_combo_box_changed(self, index):
        update_settings(
            "floating_window_management", "floating_window_display_style", index
        )
        self.appearance_settings_changed.emit()

    def floating_window_size_combo_box_changed(self, index):
        update_settings("floating_window_management", "floating_window_size", index)
        self.appearance_settings_changed.emit()

    def floating_window_theme_combo_box_changed(self, index):
        update_settings("floating_window_management", "floating_window_theme", index)
        self.appearance_settings_changed.emit()


# ==================================================
# 浮窗管理 - 贴边设置
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
        self.floating_window_stick_to_edge_recover_seconds_spinbox.setRange(1, 60)
        self.floating_window_stick_to_edge_recover_seconds_spinbox.setSuffix("s")
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


# ==================================================
# 浮窗管理 - 前台应用隐藏设置
# ==================================================
class floating_window_foreground_hiding_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async(
                "floating_window_management", "foreground_hiding_settings"
            )
        )
        self.setBorderRadius(8)

        # 在前台应用时隐藏浮窗开关
        self.hide_floating_window_on_foreground_switch = SwitchButton()
        self.hide_floating_window_on_foreground_switch.setOffText(
            get_content_switchbutton_name_async(
                "floating_window_management",
                "hide_floating_window_on_foreground",
                "disable",
            )
        )
        self.hide_floating_window_on_foreground_switch.setOnText(
            get_content_switchbutton_name_async(
                "floating_window_management",
                "hide_floating_window_on_foreground",
                "enable",
            )
        )
        self.hide_floating_window_on_foreground_switch.setChecked(
            readme_settings_async(
                "floating_window_management", "hide_floating_window_on_foreground"
            )
        )
        self.hide_floating_window_on_foreground_switch.checkedChanged.connect(
            self.hide_floating_window_on_foreground_switch_changed
        )

        # 隐藏浮窗时的窗口标题
        self.hide_floating_window_on_foreground_window_titles_line_edit = LineEdit()
        self.hide_floating_window_on_foreground_window_titles_line_edit.setFixedWidth(
            WIDTH_SPINBOX
        )
        self.hide_floating_window_on_foreground_window_titles_line_edit.setText(
            str(
                readme_settings_async(
                    "floating_window_management",
                    "hide_floating_window_on_foreground_window_titles",
                )
                or ""
            )
        )
        self.hide_floating_window_on_foreground_window_titles_line_edit.editingFinished.connect(
            self.hide_floating_window_on_foreground_window_titles_changed
        )

        # 隐藏浮窗时的进程名称
        self.hide_floating_window_on_foreground_process_names_line_edit = LineEdit()
        self.hide_floating_window_on_foreground_process_names_line_edit.setFixedWidth(
            WIDTH_SPINBOX
        )
        self.hide_floating_window_on_foreground_process_names_line_edit.setText(
            str(
                readme_settings_async(
                    "floating_window_management",
                    "hide_floating_window_on_foreground_process_names",
                )
                or ""
            )
        )
        self.hide_floating_window_on_foreground_process_names_line_edit.editingFinished.connect(
            self.hide_floating_window_on_foreground_process_names_changed
        )

        self._update_foreground_hide_inputs_enabled(
            bool(self.hide_floating_window_on_foreground_switch.isChecked())
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_ad_20_filled"),
            get_content_name_async(
                "floating_window_management", "hide_floating_window_on_foreground"
            ),
            get_content_description_async(
                "floating_window_management", "hide_floating_window_on_foreground"
            ),
            self.hide_floating_window_on_foreground_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_window_20_filled"),
            get_content_name_async(
                "floating_window_management",
                "hide_floating_window_on_foreground_window_titles",
            ),
            get_content_description_async(
                "floating_window_management",
                "hide_floating_window_on_foreground_window_titles",
            ),
            self.hide_floating_window_on_foreground_window_titles_line_edit,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_window_20_filled"),
            get_content_name_async(
                "floating_window_management",
                "hide_floating_window_on_foreground_process_names",
            ),
            get_content_description_async(
                "floating_window_management",
                "hide_floating_window_on_foreground_process_names",
            ),
            self.hide_floating_window_on_foreground_process_names_line_edit,
        )

    def _update_foreground_hide_inputs_enabled(self, enabled: bool):
        try:
            self.hide_floating_window_on_foreground_window_titles_line_edit.setEnabled(
                bool(enabled)
            )
            self.hide_floating_window_on_foreground_process_names_line_edit.setEnabled(
                bool(enabled)
            )
        except Exception:
            pass

    def hide_floating_window_on_foreground_switch_changed(self, checked):
        update_settings(
            "floating_window_management", "hide_floating_window_on_foreground", checked
        )
        self._update_foreground_hide_inputs_enabled(bool(checked))

    def hide_floating_window_on_foreground_window_titles_changed(self):
        update_settings(
            "floating_window_management",
            "hide_floating_window_on_foreground_window_titles",
            self.hide_floating_window_on_foreground_window_titles_line_edit.text().strip(),
        )

    def hide_floating_window_on_foreground_process_names_changed(self):
        update_settings(
            "floating_window_management",
            "hide_floating_window_on_foreground_process_names",
            self.hide_floating_window_on_foreground_process_names_line_edit.text().strip(),
        )
