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
from app.Language.obtain_language import *


# ==================================================
# 抽奖设置
# ==================================================
class lottery_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加抽取功能设置组件
        self.extraction_function_widget = lottery_extraction_function(self)
        self.vBoxLayout.addWidget(self.extraction_function_widget)

        # 添加显示设置组件
        self.display_settings_widget = lottery_display_settings(self)
        self.vBoxLayout.addWidget(self.display_settings_widget)

        # 添加动画设置组件
        self.animation_settings_widget = lottery_animation_settings(self)
        self.vBoxLayout.addWidget(self.animation_settings_widget)


class lottery_extraction_function(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("lottery_settings", "extraction_function"))
        self.setBorderRadius(8)

        # 抽取模式下拉框
        self.draw_mode_combo = ComboBox()
        self.draw_mode_combo.currentIndexChanged.connect(self.on_draw_mode_changed)

        self.clear_record_combo = ComboBox()
        self.clear_record_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_settings",
                "clear_record",
                self.clear_record_combo.currentIndex(),
            )
        )

        # 半重复抽取次数输入框
        self.half_repeat_spin = SpinBox()
        self.half_repeat_spin.setFixedWidth(WIDTH_SPINBOX)
        self.half_repeat_spin.setRange(0, 100)
        self.half_repeat_spin.setSuffix("次")
        self.half_repeat_spin.setValue(
            readme_settings_async("lottery_settings", "half_repeat")
        )
        self.half_repeat_spin.valueChanged.connect(
            lambda: update_settings(
                "lottery_settings", "half_repeat", self.half_repeat_spin.value()
            )
        )

        self.draw_type_combo = ComboBox()
        self.draw_type_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_settings", "draw_type", self.draw_type_combo.currentIndex()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_document_bullet_list_cube_20_filled"),
            get_content_name_async("lottery_settings", "draw_mode"),
            get_content_description_async("lottery_settings", "draw_mode"),
            self.draw_mode_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_clear_formatting_20_filled"),
            get_content_name_async("lottery_settings", "clear_record"),
            get_content_description_async("lottery_settings", "clear_record"),
            self.clear_record_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_clipboard_bullet_list_20_filled"),
            get_content_name_async("lottery_settings", "half_repeat"),
            get_content_description_async("lottery_settings", "half_repeat"),
            self.half_repeat_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_drawer_add_20_filled"),
            get_content_name_async("lottery_settings", "draw_type"),
            get_content_description_async("lottery_settings", "draw_type"),
            self.draw_type_combo,
        )

        # 初始化时在后台加载选项并回填
        QTimer.singleShot(0, self._start_background_load)

    def _start_background_load(self):
        class _Signals(QObject):
            loaded = Signal(dict)

        class _Loader(QRunnable):
            def __init__(self, fn, signals):
                super().__init__()
                self.fn = fn
                self.signals = signals

            def run(self):
                try:
                    data = self.fn()
                    self.signals.loaded.emit(data)
                except Exception as e:
                    logger.error(f"后台加载 lottery_settings 数据失败: {e}")

        def _collect():
            data = {}
            try:
                data["draw_mode_items"] = get_content_combo_name_async(
                    "lottery_settings", "draw_mode"
                )
                data["draw_mode_index"] = readme_settings_async(
                    "lottery_settings", "draw_mode"
                )
                data["clear_record_items"] = get_content_combo_name_async(
                    "lottery_settings", "clear_record"
                )
                data["clear_record_index"] = readme_settings_async(
                    "lottery_settings", "clear_record"
                )
                data["half_repeat_value"] = readme_settings_async(
                    "lottery_settings", "half_repeat"
                )
                data["draw_type_items"] = get_content_combo_name_async(
                    "lottery_settings", "draw_type"
                )
                data["draw_type_index"] = readme_settings_async(
                    "lottery_settings", "draw_type"
                )
            except Exception as e:
                logger.error(f"收集 lottery_settings 初始数据失败: {e}")
            return data

        signals = _Signals()
        signals.loaded.connect(self._on_background_loaded)
        runnable = _Loader(_collect, signals)
        QThreadPool.globalInstance().start(runnable)

    def _on_background_loaded(self, data: dict):
        try:
            if "draw_mode_items" in data:
                self.draw_mode_combo.addItems(data.get("draw_mode_items", []))
                self.draw_mode_combo.setCurrentIndex(data.get("draw_mode_index", 0))
            if "clear_record_items" in data:
                self.clear_record_combo.addItems(data.get("clear_record_items", []))
                self.clear_record_combo.setCurrentIndex(
                    data.get("clear_record_index", 0)
                )
            if "half_repeat_value" in data:
                self.half_repeat_spin.setValue(data.get("half_repeat_value", 0))
            if "draw_type_items" in data:
                self.draw_type_combo.addItems(data.get("draw_type_items", []))
                self.draw_type_combo.setCurrentIndex(data.get("draw_type_index", 0))

            self.on_draw_mode_changed()
        except Exception as e:
            logger.error(f"回填 lottery_settings 数据失败: {e}")

    def on_draw_mode_changed(self):
        """当抽取模式改变时的处理逻辑"""
        # 更新设置值
        update_settings(
            "lottery_settings", "draw_mode", self.draw_mode_combo.currentIndex()
        )

        # 获取当前抽取模式索引
        draw_mode_index = self.draw_mode_combo.currentIndex()

        # 根据抽取模式设置不同的控制逻辑
        if draw_mode_index == 0:  # 重复抽取模式
            # 禁用清除抽取记录方式下拉框
            self.clear_record_combo.setEnabled(False)
            # 清空当前选项
            self.clear_record_combo.clear()
            self.clear_record_combo.addItems(
                get_any_position_value_async(
                    "lottery_settings", "clear_record", "combo_items_other"
                )
            )
            # 强制设置为"无需清除"（索引2）
            self.clear_record_combo.setCurrentIndex(2)
            # 更新设置
            update_settings("lottery_settings", "clear_record", 2)

            # 设置half_repeat_spin为0并禁用
            self.half_repeat_spin.setEnabled(False)
            self.half_repeat_spin.setRange(0, 0)
            self.half_repeat_spin.setValue(0)
            # 更新设置
            update_settings("lottery_settings", "half_repeat", 0)

        else:  # 不重复抽取模式或半重复抽取模式
            # 启用清除抽取记录方式下拉框
            self.clear_record_combo.setEnabled(True)

            # 清空当前选项
            self.clear_record_combo.clear()

            # 添加前两个选项（不包含"无需清除"）
            self.clear_record_combo.addItems(
                get_content_combo_name_async("lottery_settings", "clear_record")
            )

            # 设置默认选择第一个选项
            self.clear_record_combo.setCurrentIndex(0)
            # 更新设置
            update_settings("lottery_settings", "clear_record", 0)

            # 根据具体模式设置half_repeat_spin
            if draw_mode_index == 1:  # 不重复抽取模式
                # 设置half_repeat_spin为1并禁用
                self.half_repeat_spin.setEnabled(False)
                self.half_repeat_spin.setRange(1, 1)
                self.half_repeat_spin.setValue(1)
                # 更新设置
                update_settings("lottery_settings", "half_repeat", 1)
            else:  # 半重复抽取模式（索引2）
                # 设置half_repeat_spin为2-100范围并启用
                self.half_repeat_spin.setEnabled(True)
                self.half_repeat_spin.setRange(2, 100)
                # 如果当前值小于2，则设置为2
                if self.half_repeat_spin.value() < 2:
                    self.half_repeat_spin.setValue(2)
                    # 更新设置
                    update_settings("lottery_settings", "half_repeat", 2)


class lottery_display_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("lottery_settings", "display_settings"))
        self.setBorderRadius(8)

        # 字体大小输入框
        self.font_size_spin = SpinBox()
        self.font_size_spin.setFixedWidth(WIDTH_SPINBOX)
        self.font_size_spin.setRange(10, 1000)
        self.font_size_spin.setSuffix("px")
        self.font_size_spin.setValue(
            readme_settings_async("lottery_settings", "font_size")
        )
        self.font_size_spin.valueChanged.connect(
            lambda: update_settings(
                "lottery_settings", "font_size", self.font_size_spin.value()
            )
        )

        # 结果显示格式下拉框
        self.display_format_combo = ComboBox()
        self.display_format_combo.addItems(
            get_content_combo_name_async("lottery_settings", "display_format")
        )
        self.display_format_combo.setCurrentIndex(
            readme_settings_async("lottery_settings", "display_format")
        )
        self.display_format_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_settings",
                "display_format",
                self.display_format_combo.currentIndex(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_20_filled"),
            get_content_name_async("lottery_settings", "font_size"),
            get_content_description_async("lottery_settings", "font_size"),
            self.font_size_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_sparkle_20_filled"),
            get_content_name_async("lottery_settings", "display_format"),
            get_content_description_async("lottery_settings", "display_format"),
            self.display_format_combo,
        )


class lottery_animation_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加基础动画设置组件
        self.basic_animation_widget = lottery_basic_animation_settings(self)
        self.vBoxLayout.addWidget(self.basic_animation_widget)

        # 添加颜色主题设置组件
        self.color_theme_widget = lottery_color_theme_settings(self)
        self.vBoxLayout.addWidget(self.color_theme_widget)

        # 添加奖品图片设置组件
        self.lottery_image_widget = lottery_lottery_image_settings(self)
        self.vBoxLayout.addWidget(self.lottery_image_widget)


class lottery_basic_animation_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("lottery_settings", "basic_animation_settings")
        )
        self.setBorderRadius(8)

        # 动画模式下拉框
        self.animation_combo = ComboBox()
        self.animation_combo.addItems(
            get_content_combo_name_async("lottery_settings", "animation")
        )
        self.animation_combo.setCurrentIndex(
            readme_settings_async("lottery_settings", "animation")
        )
        self.animation_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_settings", "animation", self.animation_combo.currentIndex()
            )
        )

        # 动画间隔输入框
        self.animation_interval_spin = SpinBox()
        self.animation_interval_spin.setFixedWidth(WIDTH_SPINBOX)
        self.animation_interval_spin.setRange(1, 2000)
        self.animation_interval_spin.setSuffix("ms")
        self.animation_interval_spin.setValue(
            readme_settings_async("lottery_settings", "animation_interval")
        )
        self.animation_interval_spin.valueChanged.connect(
            lambda: update_settings(
                "lottery_settings",
                "animation_interval",
                self.animation_interval_spin.value(),
            )
        )

        # 自动播放次数输入框
        self.autoplay_count_spin = SpinBox()
        self.autoplay_count_spin.setFixedWidth(WIDTH_SPINBOX)
        self.autoplay_count_spin.setRange(0, 100)
        self.autoplay_count_spin.setSuffix("次")
        self.autoplay_count_spin.setValue(
            readme_settings_async("lottery_settings", "autoplay_count")
        )
        self.autoplay_count_spin.valueChanged.connect(
            lambda: update_settings(
                "lottery_settings", "autoplay_count", self.autoplay_count_spin.value()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_sanitize_20_filled"),
            get_content_name_async("lottery_settings", "animation"),
            get_content_description_async("lottery_settings", "animation"),
            self.animation_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_timeline_20_filled"),
            get_content_name_async("lottery_settings", "animation_interval"),
            get_content_description_async("lottery_settings", "animation_interval"),
            self.animation_interval_spin,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_play_20_filled"),
            get_content_name_async("lottery_settings", "autoplay_count"),
            get_content_description_async("lottery_settings", "autoplay_count"),
            self.autoplay_count_spin,
        )


class lottery_color_theme_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("lottery_settings", "color_theme_settings")
        )
        self.setBorderRadius(8)

        # 动画颜色主题下拉框
        self.animation_color_theme_combo = ComboBox()
        self.animation_color_theme_combo.addItems(
            get_content_combo_name_async("lottery_settings", "animation_color_theme")
        )
        self.animation_color_theme_combo.setCurrentIndex(
            readme_settings_async("lottery_settings", "animation_color_theme")
        )
        self.animation_color_theme_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_settings",
                "animation_color_theme",
                self.animation_color_theme_combo.currentIndex(),
            )
        )

        # 结果颜色主题下拉框
        self.result_color_theme_combo = ComboBox()
        self.result_color_theme_combo.addItems(
            get_content_combo_name_async("lottery_settings", "result_color_theme")
        )
        self.result_color_theme_combo.setCurrentIndex(
            readme_settings_async("lottery_settings", "result_color_theme")
        )
        self.result_color_theme_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_settings",
                "result_color_theme",
                self.result_color_theme_combo.currentIndex(),
            )
        )

        # 动画固定颜色
        self.animation_fixed_color_button = ColorConfigItem(
            "Theme",
            "Color",
            readme_settings_async("lottery_settings", "animation_fixed_color"),
        )
        self.animation_fixed_color_button.valueChanged.connect(
            lambda color: update_settings(
                "lottery_settings", "animation_fixed_color", color.name()
            )
        )

        # 结果固定颜色
        self.result_fixed_color_button = ColorConfigItem(
            "Theme",
            "Color",
            readme_settings_async("lottery_settings", "result_fixed_color"),
        )
        self.result_fixed_color_button.valueChanged.connect(
            lambda color: update_settings(
                "lottery_settings", "result_fixed_color", color.name()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_color_20_filled"),
            get_content_name_async("lottery_settings", "animation_color_theme"),
            get_content_description_async("lottery_settings", "animation_color_theme"),
            self.animation_color_theme_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_color_20_filled"),
            get_content_name_async("lottery_settings", "result_color_theme"),
            get_content_description_async("lottery_settings", "result_color_theme"),
            self.result_color_theme_combo,
        )

        self.animationColorCard = ColorSettingCard(
            self.animation_fixed_color_button,
            get_theme_icon("ic_fluent_text_color_20_filled"),
            self.tr(
                get_content_name_async("lottery_settings", "animation_fixed_color")
            ),
            self.tr(
                get_content_description_async(
                    "lottery_settings", "animation_fixed_color"
                )
            ),
            self,
        )
        self.resultColorCard = ColorSettingCard(
            self.result_fixed_color_button,
            get_theme_icon("ic_fluent_text_color_20_filled"),
            self.tr(get_content_name_async("lottery_settings", "result_fixed_color")),
            self.tr(
                get_content_description_async("lottery_settings", "result_fixed_color")
            ),
            self,
        )

        self.vBoxLayout.addWidget(self.animationColorCard)
        self.vBoxLayout.addWidget(self.resultColorCard)


class lottery_lottery_image_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("lottery_settings", "lottery_image_settings")
        )
        self.setBorderRadius(8)

        # 奖品图片开关
        self.lottery_image_switch = SwitchButton()
        self.lottery_image_switch.setOffText(
            get_content_switchbutton_name_async(
                "lottery_settings", "lottery_image", "disable"
            )
        )
        self.lottery_image_switch.setOnText(
            get_content_switchbutton_name_async(
                "lottery_settings", "lottery_image", "enable"
            )
        )
        self.lottery_image_switch.setChecked(
            readme_settings_async("lottery_settings", "lottery_image")
        )
        self.lottery_image_switch.checkedChanged.connect(
            lambda: update_settings(
                "lottery_settings",
                "lottery_image",
                self.lottery_image_switch.isChecked(),
            )
        )

        # 打开奖品图片文件夹按钮
        self.open_lottery_image_folder_button = PushButton(
            get_content_name_async("lottery_settings", "open_lottery_image_folder")
        )
        self.open_lottery_image_folder_button.clicked.connect(
            lambda: self.open_lottery_image_folder()
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_image_circle_20_filled"),
            get_content_name_async("lottery_settings", "lottery_image"),
            get_content_description_async("lottery_settings", "lottery_image"),
            self.lottery_image_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_folder_open_20_filled"),
            get_content_name_async("lottery_settings", "open_lottery_image_folder"),
            get_content_description_async(
                "lottery_settings", "open_lottery_image_folder"
            ),
            self.open_lottery_image_folder_button,
        )

    def open_lottery_image_folder(self):
        """打开奖品图片文件夹"""
        folder_path = get_resources_path(PRIZE_IMAGE_FOLDER)
        if not folder_path.exists():
            os.makedirs(folder_path, exist_ok=True)
        if folder_path:
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(folder_path)))
        else:
            logger.error("无法获取奖品图片文件夹路径")
