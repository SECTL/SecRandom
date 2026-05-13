# ==================================================
# 导入库
# ==================================================
import os

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
from app.tools.settings_access import get_safe_font_size
from app.tools.list_specific_settings_access import (
    read_roll_call_setting,
    set_roll_call_setting_override,
    get_safe_font_size_list_specific,
    clear_list_specific_overrides,
)
from app.Language.obtain_language import *
from app.common.data.list import *


# ==================================================
# 点名设置
# ==================================================
class roll_call_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        self.list_specific_entry_widget = roll_call_list_specific_entry(self)
        self.vBoxLayout.addWidget(self.list_specific_entry_widget)

        # 添加抽取功能设置组件
        self.extraction_function_widget = roll_call_extraction_function(self)
        self.vBoxLayout.addWidget(self.extraction_function_widget)

        # 添加显示设置组件
        self.display_settings_widget = roll_call_display_settings(self)
        self.vBoxLayout.addWidget(self.display_settings_widget)

        # 添加动画设置组件
        self.animation_settings_widget = roll_call_animation_settings(self)
        self.vBoxLayout.addWidget(self.animation_settings_widget)


class roll_call_list_specific_entry(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("roll_call_settings", "list_specific_settings_entry")
        )
        self.setBorderRadius(8)

        self.open_button = PushButton(
            get_content_pushbutton_name_async(
                "roll_call_settings", "list_specific_settings_entry"
            )
        )
        self.open_button.clicked.connect(self._open_window)

        self.addGroup(
            get_theme_icon("ic_fluent_settings_20_filled"),
            get_content_name_async(
                "roll_call_settings", "list_specific_settings_entry"
            ),
            get_content_description_async(
                "roll_call_settings", "list_specific_settings_entry"
            ),
            self.open_button,
        )

    def _open_window(self):
        try:
            from app.page_building.another_window import (
                create_roll_call_list_specific_settings_window,
            )

            create_roll_call_list_specific_settings_window()
        except Exception as e:
            logger.exception(f"打开独立名单配置窗口失败: {e}")


class roll_call_extraction_function(GroupHeaderCardWidget):
    def __init__(
        self,
        parent=None,
        list_name: str | None = None,
        *,
        show_default_class: bool = True,
        enable_file_watcher: bool = True,
    ):
        super().__init__(parent)
        self._list_name = list_name
        self.setTitle(
            get_content_name_async("roll_call_settings", "extraction_function")
        )
        self.setBorderRadius(8)

        self._show_default_class = show_default_class
        self._enable_file_watcher = enable_file_watcher

        # 抽取模式下拉框
        self.draw_mode_combo = ComboBox()
        self.draw_mode_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "draw_mode")
        )
        self.draw_mode_combo.setCurrentIndex(self._read_setting("draw_mode"))
        self.draw_mode_combo.currentIndexChanged.connect(self.on_draw_mode_changed)

        # 清除抽取记录方式下拉框
        self.clear_record_combo = ComboBox()
        self.clear_record_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "clear_record")
        )
        self.clear_record_combo.setCurrentIndex(self._read_setting("clear_record"))
        self.clear_record_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "clear_record", self.clear_record_combo.currentIndex()
            )
        )

        # 半重复抽取次数输入框
        self.half_repeat_spin = SpinBox()
        self.half_repeat_spin.setFixedWidth(WIDTH_SPINBOX)
        self.half_repeat_spin.setRange(0, 100)
        self.half_repeat_spin.setValue(self._read_setting("half_repeat"))
        self.half_repeat_spin.valueChanged.connect(
            lambda: self._write_setting("half_repeat", self.half_repeat_spin.value())
        )

        # 抽取方式下拉框
        self.draw_type_combo = ComboBox()
        self.draw_type_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "draw_type")
        )
        self.draw_type_combo.setCurrentIndex(self._read_setting("draw_type"))
        self.draw_type_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "draw_type", self.draw_type_combo.currentIndex()
            )
        )

        self.default_class_combo = None
        if self._show_default_class:
            self.default_class_combo = ComboBox()
            self.refresh_class_list()
            if not get_class_name_list():
                self.default_class_combo.setCurrentIndex(-1)
                self.default_class_combo.setPlaceholderText(
                    get_content_name_async("roll_call_settings", "default_class")
                )
            else:
                self.default_class_combo.setCurrentText(
                    readme_settings_async("roll_call_settings", "default_class")
                )
            self.default_class_combo.currentIndexChanged.connect(
                lambda: update_settings(
                    "roll_call_settings",
                    "default_class",
                    self.default_class_combo.currentText(),
                )
            )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_cube_20_filled"),
            get_content_name_async("roll_call_settings", "draw_mode"),
            get_content_description_async("roll_call_settings", "draw_mode"),
            self.draw_mode_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_clear_formatting_20_filled"),
            get_content_name_async("roll_call_settings", "clear_record"),
            get_content_description_async("roll_call_settings", "clear_record"),
            self.clear_record_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_clipboard_bullet_list_20_filled"),
            get_content_name_async("roll_call_settings", "half_repeat"),
            get_content_description_async("roll_call_settings", "half_repeat"),
            self.half_repeat_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_drawer_add_20_filled"),
            get_content_name_async("roll_call_settings", "draw_type"),
            get_content_description_async("roll_call_settings", "draw_type"),
            self.draw_type_combo,
        )
        if self._show_default_class and self.default_class_combo is not None:
            self.addGroup(
                get_theme_icon("ic_fluent_class_20_filled"),
                get_content_name_async("roll_call_settings", "default_class"),
                get_content_description_async("roll_call_settings", "default_class"),
                self.default_class_combo,
            )

        if self._enable_file_watcher and self._show_default_class:
            self.setup_file_watcher()

        self.on_draw_mode_changed()

    def _read_setting(self, key: str, default=None):
        if self._list_name:
            return read_roll_call_setting(self._list_name, key, default)
        return readme_settings_async("roll_call_settings", key, default)

    def _write_setting(self, key: str, value):
        if self._list_name:
            set_roll_call_setting_override(self._list_name, key, value)
            return
        update_settings("roll_call_settings", key, value)

    def setup_file_watcher(self):
        """设置文件系统监视器，监控班级名单文件夹的变化"""
        # 获取班级名单文件夹路径
        roll_call_list_dir = get_data_path("list", "roll_call_list")

        # 确保目录存在
        if not roll_call_list_dir.exists():
            roll_call_list_dir.mkdir(parents=True, exist_ok=True)

        # 创建文件系统监视器
        self.file_watcher = QFileSystemWatcher()

        # 监视目录
        self.file_watcher.addPath(str(roll_call_list_dir))

        # 连接信号
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法"""
        # 延迟刷新，避免文件操作未完成
        QTimer.singleShot(500, self.refresh_class_list)

    def refresh_class_list(self):
        """刷新班级下拉框列表"""
        # 从设置文件中读取默认班级
        default_class = readme_settings_async("roll_call_settings", "default_class")

        # 获取最新的班级列表
        class_list = get_class_name_list()

        # 清空并重新添加班级列表
        self.default_class_combo.clear()
        self.default_class_combo.addItems(class_list)

        # 尝试恢复设置文件中的默认班级
        if default_class:
            self.default_class_combo.setCurrentText(default_class)
        elif not class_list:
            self.default_class_combo.setCurrentIndex(-1)
            self.default_class_combo.setPlaceholderText(
                get_content_name_async("roll_call_settings", "default_class")
            )

    def on_draw_mode_changed(self):
        """当抽取模式改变时的处理逻辑"""
        # 更新设置值
        self._write_setting("draw_mode", self.draw_mode_combo.currentIndex())

        # 获取当前抽取模式索引
        draw_mode_index = self.draw_mode_combo.currentIndex()

        # 根据抽取模式设置不同的控制逻辑
        if draw_mode_index == 0:  # 重复抽取模式
            # 暂时屏蔽信号，防止修改选项时触发不必要的更新
            self.clear_record_combo.blockSignals(True)

            # 禁用清除抽取记录方式下拉框
            self.clear_record_combo.setEnabled(False)
            # 清空当前选项
            self.clear_record_combo.clear()
            self.clear_record_combo.addItems(
                get_any_position_value_async(
                    "roll_call_settings", "clear_record", "combo_items_other"
                )
            )
            # 强制设置为"无需清除"（索引2）
            self.clear_record_combo.setCurrentIndex(2)

            # 恢复信号
            self.clear_record_combo.blockSignals(False)

            # 更新设置
            self._write_setting("clear_record", 2)

            # 设置half_repeat_spin为0并禁用
            self.half_repeat_spin.setEnabled(False)
            self.half_repeat_spin.setRange(0, 0)
            self.half_repeat_spin.setValue(0)
            # 更新设置
            self._write_setting("half_repeat", 0)

        else:  # 不重复抽取模式或半重复抽取模式
            # 启用清除抽取记录方式下拉框
            self.clear_record_combo.setEnabled(True)

            # 暂时屏蔽信号，防止clear()触发更新导致设置被覆盖
            self.clear_record_combo.blockSignals(True)

            # 清空当前选项
            self.clear_record_combo.clear()

            # 添加前两个选项（不包含"无需清除"）
            self.clear_record_combo.addItems(
                get_content_combo_name_async("roll_call_settings", "clear_record")
            )

            # 读取保存的设置
            saved_clear_record = self._read_setting("clear_record")

            # 检查保存的设置是否有效
            if 0 <= saved_clear_record < self.clear_record_combo.count():
                self.clear_record_combo.setCurrentIndex(saved_clear_record)
            else:
                self.clear_record_combo.setCurrentIndex(0)
                self._write_setting("clear_record", 0)

            # 恢复信号
            self.clear_record_combo.blockSignals(False)

            # 根据具体模式设置half_repeat_spin
            if draw_mode_index == 1:  # 不重复抽取模式
                # 设置half_repeat_spin为1并禁用
                self.half_repeat_spin.setEnabled(False)
                self.half_repeat_spin.setRange(1, 1)
                self.half_repeat_spin.setValue(1)
                # 更新设置
                self._write_setting("half_repeat", 1)
            else:  # 半重复抽取模式（索引2）
                # 设置half_repeat_spin为2-100范围并启用
                self.half_repeat_spin.setEnabled(True)
                self.half_repeat_spin.setRange(2, 100)
                # 如果当前值小于2，则设置为2
                if self.half_repeat_spin.value() < 2:
                    self.half_repeat_spin.setValue(2)
                    # 更新设置
                    self._write_setting("half_repeat", 2)


class roll_call_display_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None, list_name: str | None = None):
        super().__init__(parent)
        self._list_name = list_name
        self.setTitle(get_content_name_async("roll_call_settings", "display_settings"))
        self.setBorderRadius(8)

        # 字体大小输入框
        self.font_size_spin = SpinBox()
        self.font_size_spin.setFixedWidth(WIDTH_SPINBOX)
        self.font_size_spin.setRange(10, 1000)
        self.font_size_spin.setSuffix("px")
        if self._list_name:
            self.font_size_spin.setValue(
                get_safe_font_size_list_specific(
                    "roll_call_settings",
                    "roll_call_list_specific_settings",
                    self._list_name,
                    "font_size",
                )
            )
        else:
            self.font_size_spin.setValue(
                get_safe_font_size("roll_call_settings", "font_size")
            )
        self.font_size_spin.valueChanged.connect(
            lambda: self._write_setting("font_size", self.font_size_spin.value())
        )

        # 是否使用全局字体下拉框
        self.use_global_font_combo = ComboBox()
        self.use_global_font_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "use_global_font")
        )
        self.use_global_font_combo.setCurrentIndex(
            self._read_setting("use_global_font")
        )
        self.use_global_font_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "use_global_font", self.use_global_font_combo.currentIndex()
            )
        )

        # 自定义字体下拉框
        self.custom_font_combo = ComboBox()
        self.custom_font_combo.addItems(QFontDatabase.families())
        current_custom_font = self._read_setting("custom_font")
        try:
            index = self.custom_font_combo.findText(current_custom_font)
            if index >= 0:
                self.custom_font_combo.setCurrentIndex(index)
            else:
                self.custom_font_combo.setCurrentIndex(0)
        except:
            self.custom_font_combo.setCurrentIndex(0)
        self.custom_font_combo.currentTextChanged.connect(
            lambda: self._write_setting(
                "custom_font", self.custom_font_combo.currentText()
            )
        )

        # 结果显示格式下拉框
        self.display_format_combo = ComboBox()
        self.display_format_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "display_format")
        )
        self.display_format_combo.setCurrentIndex(self._read_setting("display_format"))
        self.display_format_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "display_format", self.display_format_combo.currentIndex()
            )
        )

        # 结果显示样式下拉框
        self.display_style_combo = ComboBox()
        self.display_style_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "display_style")
        )
        self.display_style_combo.setCurrentIndex(self._read_setting("display_style"))
        self.display_style_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "display_style", self.display_style_combo.currentIndex()
            )
        )

        # 显示随机组员格式下拉框
        self.show_random_format_combo = ComboBox()
        self.show_random_format_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "show_random")
        )
        self.show_random_format_combo.setCurrentIndex(self._read_setting("show_random"))
        self.show_random_format_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "show_random", self.show_random_format_combo.currentIndex()
            )
        )

        self.show_tags_switch = SwitchButton()
        self.show_tags_switch.setOffText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "show_tags", "disable"
            )
        )
        self.show_tags_switch.setOnText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "show_tags", "enable"
            )
        )
        self.show_tags_switch.setChecked(bool(self._read_setting("show_tags")))
        self.show_tags_switch.checkedChanged.connect(
            lambda: self._write_setting("show_tags", self.show_tags_switch.isChecked())
        )

        # 权重透明化开关
        self.show_weight_switch = SwitchButton()
        self.show_weight_switch.setOffText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "show_weight_transparency", "disable"
            )
        )
        self.show_weight_switch.setOnText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "show_weight_transparency", "enable"
            )
        )
        self.show_weight_switch.setChecked(
            bool(self._read_setting("show_weight_transparency"))
        )
        self.show_weight_switch.checkedChanged.connect(
            lambda: self._write_setting(
                "show_weight_transparency", self.show_weight_switch.isChecked()
            )
        )

        # 提示语输入框
        self.reminder_text_edit = LineEdit()
        self.reminder_text_edit.setFixedWidth(200)
        self.reminder_text_edit.setText(self._read_setting("reminder_text", "别紧张"))
        self.reminder_text_edit.textChanged.connect(
            lambda: self._write_setting("reminder_text", self.reminder_text_edit.text())
        )

        # 提示语字体大小输入框
        self.reminder_font_size_spin = SpinBox()
        self.reminder_font_size_spin.setFixedWidth(WIDTH_SPINBOX)
        self.reminder_font_size_spin.setRange(10, 500)
        self.reminder_font_size_spin.setSuffix("px")
        self.reminder_font_size_spin.setValue(
            get_safe_font_size("roll_call_settings", "reminder_font_size", 30)
            if not self._list_name
            else get_safe_font_size_list_specific(
                "roll_call_settings",
                "roll_call_list_specific_settings",
                self._list_name,
                "reminder_font_size",
                30,
            )
        )
        self.reminder_font_size_spin.valueChanged.connect(
            lambda: self._write_setting(
                "reminder_font_size", self.reminder_font_size_spin.value()
            )
        )

        self.reminder_color_button = ColorConfigItem(
            "Theme",
            "Color",
            self._read_setting("reminder_text_color", "#808080"),
        )
        self.reminder_color_button.valueChanged.connect(
            lambda color: self._write_setting("reminder_text_color", color.name())
        )

        self.reminder_opacity_spin = SpinBox()
        self.reminder_opacity_spin.setFixedWidth(WIDTH_SPINBOX)
        self.reminder_opacity_spin.setRange(0, 100)
        self.reminder_opacity_spin.setSuffix("%")
        self.reminder_opacity_spin.setValue(
            int(self._read_setting("reminder_text_opacity", 50))
        )
        self.reminder_opacity_spin.valueChanged.connect(
            lambda: self._write_setting(
                "reminder_text_opacity", self.reminder_opacity_spin.value()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_size_20_filled"),
            get_content_name_async("roll_call_settings", "use_global_font"),
            get_content_description_async("roll_call_settings", "use_global_font"),
            self.use_global_font_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_20_filled"),
            get_content_name_async("roll_call_settings", "custom_font"),
            get_content_description_async("roll_call_settings", "custom_font"),
            self.custom_font_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_20_filled"),
            get_content_name_async("roll_call_settings", "font_size"),
            get_content_description_async("roll_call_settings", "font_size"),
            self.font_size_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_sparkle_20_filled"),
            get_content_name_async("roll_call_settings", "display_format"),
            get_content_description_async("roll_call_settings", "display_format"),
            self.display_format_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_style_guide_20_filled"),
            get_content_name_async("roll_call_settings", "display_style"),
            get_content_description_async("roll_call_settings", "display_style"),
            self.display_style_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_group_list_20_filled"),
            get_content_name_async("roll_call_settings", "show_random"),
            get_content_description_async("roll_call_settings", "show_random"),
            self.show_random_format_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_tag_20_filled"),
            get_content_name_async("roll_call_settings", "show_tags"),
            get_content_description_async("roll_call_settings", "show_tags"),
            self.show_tags_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_eye_20_filled"),
            get_content_name_async("roll_call_settings", "show_weight_transparency"),
            get_content_description_async(
                "roll_call_settings", "show_weight_transparency"
            ),
            self.show_weight_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_size_20_filled"),
            get_content_name_async("roll_call_settings", "reminder_font_size"),
            get_content_description_async("roll_call_settings", "reminder_font_size"),
            self.reminder_font_size_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_size_20_filled"),
            get_content_name_async("roll_call_settings", "reminder_text_opacity"),
            get_content_description_async(
                "roll_call_settings", "reminder_text_opacity"
            ),
            self.reminder_opacity_spin,
        )

        self.reminderColorCard = ColorSettingCard(
            self.reminder_color_button,
            get_theme_icon("ic_fluent_text_color_20_filled"),
            self.tr(
                get_content_name_async("roll_call_settings", "reminder_text_color")
            ),
            self.tr(
                get_content_description_async(
                    "roll_call_settings", "reminder_text_color"
                )
            ),
            self,
        )
        self.vBoxLayout.addWidget(self.reminderColorCard)

    def _read_setting(self, key: str, default=None):
        if self._list_name:
            return read_roll_call_setting(self._list_name, key, default)
        return readme_settings_async("roll_call_settings", key, default)

    def _write_setting(self, key: str, value):
        if self._list_name:
            set_roll_call_setting_override(self._list_name, key, value)
            return
        update_settings("roll_call_settings", key, value)


class roll_call_animation_settings(QWidget):
    def __init__(self, parent=None, list_name: str | None = None):
        super().__init__(parent)
        self._list_name = list_name
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加基础动画设置组件
        self.basic_animation_widget = roll_call_basic_animation_settings(
            self, list_name=self._list_name
        )
        self.vBoxLayout.addWidget(self.basic_animation_widget)

        # 添加颜色主题设置组件
        self.color_theme_widget = roll_call_color_theme_settings(
            self, list_name=self._list_name
        )
        self.vBoxLayout.addWidget(self.color_theme_widget)

        # 添加学生图片设置组件
        self.student_image_widget = roll_call_student_image_settings(
            self, list_name=self._list_name
        )
        self.vBoxLayout.addWidget(self.student_image_widget)


class roll_call_basic_animation_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None, list_name: str | None = None):
        super().__init__(parent)
        self._list_name = list_name
        self.setTitle(
            get_content_name_async("roll_call_settings", "basic_animation_settings")
        )
        self.setBorderRadius(8)

        # 动画模式下拉框
        self.animation_combo = ComboBox()
        self.animation_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "animation")
        )
        self.animation_combo.setCurrentIndex(self._read_setting("animation"))
        self.animation_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "animation", self.animation_combo.currentIndex()
            )
        )

        # 动画间隔输入框
        self.animation_interval_spin = SpinBox()
        self.animation_interval_spin.setFixedWidth(WIDTH_SPINBOX)
        self.animation_interval_spin.setRange(1, 1000)
        self.animation_interval_spin.setSuffix("ms")
        self.animation_interval_spin.setValue(self._read_setting("animation_interval"))
        self.animation_interval_spin.valueChanged.connect(
            lambda: self._write_setting(
                "animation_interval", self.animation_interval_spin.value()
            )
        )

        # 自动播放次数输入框
        self.autoplay_count_spin = SpinBox()
        self.autoplay_count_spin.setFixedWidth(WIDTH_SPINBOX)
        self.autoplay_count_spin.setRange(1, 1000)
        self.autoplay_count_spin.setValue(self._read_setting("autoplay_count"))
        self.autoplay_count_spin.valueChanged.connect(
            lambda: self._write_setting(
                "autoplay_count", self.autoplay_count_spin.value()
            )
        )

        self.result_flow_animation_style_switch = SwitchButton()
        self.result_flow_animation_style_switch.setOffText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "result_flow_animation_style", "disable"
            )
        )
        self.result_flow_animation_style_switch.setOnText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "result_flow_animation_style", "enable"
            )
        )
        result_flow_animation_style = self._read_setting("result_flow_animation_style")
        self.result_flow_animation_style_switch.setChecked(result_flow_animation_style)
        self.result_flow_animation_style_switch.checkedChanged.connect(
            lambda: self._write_setting(
                "result_flow_animation_style",
                self.result_flow_animation_style_switch.isChecked(),
            )
        )

        self.result_flow_animation_duration_spin = SpinBox()
        self.result_flow_animation_duration_spin.setFixedWidth(WIDTH_SPINBOX)
        self.result_flow_animation_duration_spin.setRange(0, 2000)
        self.result_flow_animation_duration_spin.setSuffix("ms")
        self.result_flow_animation_duration_spin.setValue(
            self._read_setting("result_flow_animation_duration")
        )
        self.result_flow_animation_duration_spin.valueChanged.connect(
            lambda: self._write_setting(
                "result_flow_animation_duration",
                self.result_flow_animation_duration_spin.value(),
            )
        )

        self.addGroup(
            get_theme_icon("ic_fluent_sanitize_20_filled"),
            get_content_name_async("roll_call_settings", "animation"),
            get_content_description_async("roll_call_settings", "animation"),
            self.animation_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_timeline_20_filled"),
            get_content_name_async("roll_call_settings", "animation_interval"),
            get_content_description_async("roll_call_settings", "animation_interval"),
            self.animation_interval_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_play_20_filled"),
            get_content_name_async("roll_call_settings", "autoplay_count"),
            get_content_description_async("roll_call_settings", "autoplay_count"),
            self.autoplay_count_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_sanitize_20_filled"),
            get_content_name_async("roll_call_settings", "result_flow_animation_style"),
            get_content_description_async(
                "roll_call_settings", "result_flow_animation_style"
            ),
            self.result_flow_animation_style_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_timeline_20_filled"),
            get_content_name_async(
                "roll_call_settings", "result_flow_animation_duration"
            ),
            get_content_description_async(
                "roll_call_settings", "result_flow_animation_duration"
            ),
            self.result_flow_animation_duration_spin,
        )

    def _read_setting(self, key: str, default=None):
        if self._list_name:
            return read_roll_call_setting(self._list_name, key, default)
        return readme_settings_async("roll_call_settings", key, default)

    def _write_setting(self, key: str, value):
        if self._list_name:
            set_roll_call_setting_override(self._list_name, key, value)
            return
        update_settings("roll_call_settings", key, value)


class roll_call_color_theme_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None, list_name: str | None = None):
        super().__init__(parent)
        self._list_name = list_name
        self.setTitle(
            get_content_name_async("roll_call_settings", "color_theme_settings")
        )
        self.setBorderRadius(8)

        # 颜色主题下拉框
        self.animation_color_theme_combo = ComboBox()
        self.animation_color_theme_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "animation_color_theme")
        )
        self.animation_color_theme_combo.setCurrentIndex(
            self._read_setting("animation_color_theme")
        )
        self.animation_color_theme_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "animation_color_theme", self.animation_color_theme_combo.currentIndex()
            )
        )

        # 动画固定颜色
        self.animation_fixed_color_button = ColorConfigItem(
            "Theme",
            "Color",
            self._read_setting("animation_fixed_color"),
        )
        self.animation_fixed_color_button.valueChanged.connect(
            lambda color: self._write_setting("animation_fixed_color", color.name())
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_color_20_filled"),
            get_content_name_async("roll_call_settings", "animation_color_theme"),
            get_content_description_async(
                "roll_call_settings", "animation_color_theme"
            ),
            self.animation_color_theme_combo,
        )

        self.animationColorCard = ColorSettingCard(
            self.animation_fixed_color_button,
            get_theme_icon("ic_fluent_text_color_20_filled"),
            self.tr(
                get_content_name_async("roll_call_settings", "animation_fixed_color")
            ),
            self.tr(
                get_content_description_async(
                    "roll_call_settings", "animation_fixed_color"
                )
            ),
            self,
        )

        self.vBoxLayout.addWidget(self.animationColorCard)

    def _read_setting(self, key: str, default=None):
        if self._list_name:
            return read_roll_call_setting(self._list_name, key, default)
        return readme_settings_async("roll_call_settings", key, default)

    def _write_setting(self, key: str, value):
        if self._list_name:
            set_roll_call_setting_override(self._list_name, key, value)
            return
        update_settings("roll_call_settings", key, value)


class roll_call_student_image_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None, list_name: str | None = None):
        super().__init__(parent)
        self._list_name = list_name
        self.setTitle(
            get_content_name_async("roll_call_settings", "student_image_settings")
        )
        self.setBorderRadius(8)

        # 学生图片开关
        self.student_image_switch = SwitchButton()
        self.student_image_switch.setOffText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "student_image", "disable"
            )
        )
        self.student_image_switch.setOnText(
            get_content_switchbutton_name_async(
                "roll_call_settings", "student_image", "enable"
            )
        )
        self.student_image_switch.setChecked(self._read_setting("student_image"))
        self.student_image_switch.checkedChanged.connect(
            lambda: self._write_setting(
                "student_image", self.student_image_switch.isChecked()
            )
        )

        self.student_image_position_combo = ComboBox()
        self.student_image_position_combo.addItems(
            get_content_combo_name_async("roll_call_settings", "student_image_position")
        )
        self.student_image_position_combo.setCurrentIndex(
            self._read_setting("student_image_position")
        )
        self.student_image_position_combo.currentIndexChanged.connect(
            lambda: self._write_setting(
                "student_image_position",
                self.student_image_position_combo.currentIndex(),
            )
        )

        # 打开学生图片文件夹按钮
        self.open_student_image_folder_button = PushButton(
            get_content_name_async("roll_call_settings", "open_student_image_folder")
        )
        self.open_student_image_folder_button.clicked.connect(
            lambda: self.open_student_image_folder()
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_image_circle_20_filled"),
            get_content_name_async("roll_call_settings", "student_image"),
            get_content_description_async("roll_call_settings", "student_image"),
            self.student_image_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_image_circle_20_filled"),
            get_content_name_async("roll_call_settings", "student_image_position"),
            get_content_description_async(
                "roll_call_settings", "student_image_position"
            ),
            self.student_image_position_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_folder_open_20_filled"),
            get_content_name_async("roll_call_settings", "open_student_image_folder"),
            get_content_description_async(
                "roll_call_settings", "open_student_image_folder"
            ),
            self.open_student_image_folder_button,
        )

    def _read_setting(self, key: str, default=None):
        if self._list_name:
            return read_roll_call_setting(self._list_name, key, default)
        return readme_settings_async("roll_call_settings", key, default)

    def _write_setting(self, key: str, value):
        if self._list_name:
            set_roll_call_setting_override(self._list_name, key, value)
            return
        update_settings("roll_call_settings", key, value)

    def open_student_image_folder(self):
        """打开学生图片文件夹"""
        folder_path = get_data_path(STUDENT_IMAGE_FOLDER)
        if not folder_path.exists():
            os.makedirs(folder_path, exist_ok=True)
        if folder_path:
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(folder_path)))
        else:
            logger.exception("无法获取学生图片文件夹路径")


class RollCallListSpecificSettingsWindow(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        from app.tools.settings_access import get_settings_signals

        self._dirty = False
        self._suppress_dirty = False
        self._last_overrides_map = None

        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        self.select_list_card = GroupHeaderCardWidget(self)
        self.select_list_card.setTitle(
            get_content_name_async(
                "roll_call_settings", "list_specific_settings_select_list"
            )
        )
        self.select_list_card.setBorderRadius(8)

        self.list_combo = ComboBox()
        self._refresh_list_combo()
        self.list_combo.currentTextChanged.connect(self._on_list_changed)

        self.sync_button = PushButton(
            get_content_pushbutton_name_async(
                "roll_call_settings", "list_specific_settings_sync_button"
            )
        )
        self.sync_button.setEnabled(False)
        self.sync_button.clicked.connect(self._sync_to_global)

        self.select_list_card.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async(
                "roll_call_settings", "list_specific_settings_select_list"
            ),
            get_content_description_async(
                "roll_call_settings", "list_specific_settings_select_list"
            ),
            self.list_combo,
        )
        self.select_list_card.addGroup(
            get_theme_icon("ic_fluent_arrow_sync_circle_20_filled"),
            get_content_name_async(
                "roll_call_settings", "list_specific_settings_sync_button"
            ),
            get_content_description_async(
                "roll_call_settings", "list_specific_settings_sync_button"
            ),
            self.sync_button,
        )
        self.vBoxLayout.addWidget(self.select_list_card)

        self.content_widget = QWidget(self)
        self.content_layout = QVBoxLayout(self.content_widget)
        self.content_layout.setContentsMargins(0, 0, 0, 0)
        self.content_layout.setSpacing(10)
        self.vBoxLayout.addWidget(self.content_widget)

        settings_signals = get_settings_signals()
        settings_signals.settingChanged.connect(self._on_setting_changed)

        self._rebuild_content()

    def _refresh_list_combo(self):
        current = self.list_combo.currentText() if hasattr(self, "list_combo") else ""
        self.list_combo.blockSignals(True)
        self.list_combo.clear()
        class_list = get_class_name_list()
        self.list_combo.addItems(class_list)
        if current and current in class_list:
            self.list_combo.setCurrentText(current)
        elif class_list:
            self.list_combo.setCurrentIndex(0)
        else:
            self.list_combo.setCurrentIndex(-1)
            self.list_combo.setPlaceholderText(
                get_content_name_async("roll_call_settings", "default_class")
            )
        self.list_combo.blockSignals(False)

    def _on_list_changed(self, _text: str):
        self._rebuild_content()

    def _on_setting_changed(self, first_level_key, second_level_key, value):
        if self._suppress_dirty:
            return
        if first_level_key != "roll_call_list_specific_settings":
            return
        if second_level_key != "overrides":
            return

        list_name = (self.list_combo.currentText() or "").strip()
        if not list_name:
            return

        new_map = value if isinstance(value, dict) else {}
        current_overrides = new_map.get(list_name)
        self.sync_button.setEnabled(
            isinstance(current_overrides, dict) and len(current_overrides) > 0
        )
        self._last_overrides_map = new_map

    def _sync_to_global(self):
        list_name = (self.list_combo.currentText() or "").strip()
        if not list_name:
            return

        self._suppress_dirty = True
        try:
            clear_list_specific_overrides("roll_call_list_specific_settings", list_name)
        finally:
            self._suppress_dirty = False

        self._dirty = False
        self.sync_button.setEnabled(False)
        self._rebuild_content()

    def _rebuild_content(self):
        while self.content_layout.count():
            item = self.content_layout.takeAt(0)
            w = item.widget()
            if w is not None:
                w.deleteLater()

        list_name = (self.list_combo.currentText() or "").strip()
        self._dirty = False
        self.sync_button.setEnabled(False)
        self._last_overrides_map = readme_settings_async(
            "roll_call_list_specific_settings", "overrides", {}
        )
        if not list_name:
            return
        current_overrides = (
            self._last_overrides_map.get(list_name)
            if isinstance(self._last_overrides_map, dict)
            else None
        )
        self.sync_button.setEnabled(
            isinstance(current_overrides, dict) and len(current_overrides) > 0
        )

        self.content_layout.addWidget(
            roll_call_extraction_function(
                self,
                list_name=list_name,
                show_default_class=False,
                enable_file_watcher=False,
            )
        )
        self.content_layout.addWidget(
            roll_call_display_settings(self, list_name=list_name)
        )
        self.content_layout.addWidget(
            roll_call_animation_settings(self, list_name=list_name)
        )
