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

from app.common.data.list import *
from app.common.history import *
from app.common.display.result_display import *
from app.tools.config import *
from app.common.lottery.lottery_utils import LotteryUtils
from app.common.lottery.lottery_manager import LotteryManager
from app.common.lottery import lottery_manager
from app.tools.variable import *

from app.page_building.another_window import *

from random import SystemRandom

system_random = SystemRandom()


# ==================================================
# 奖池点名类
# ==================================================
class Lottery(QWidget):
    # 添加一个信号，当设置发生变化时发出
    settingsChanged = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._init_file_watcher()
        self._init_long_press()
        self._init_tts()
        self._init_animation_state()
        self.manager = LotteryManager()

        self.initUI()
        self._setup_settings_listener()

    def _init_file_watcher(self):
        return lottery_manager.init_file_watcher(self)

    def _init_long_press(self):
        return lottery_manager.init_long_press(self)

    def _init_tts(self):
        return lottery_manager.init_tts(self)

    def _init_animation_state(self):
        return lottery_manager.init_animation_state(self)

    def _setup_settings_listener(self):
        return lottery_manager.setup_settings_listener(self)

    def handle_long_press(self):
        """处理长按事件"""
        return lottery_manager.handle_long_press(self)

    def start_long_press(self, direction):
        """开始长按

        Args:
            direction (int): 长按方向，1为增加，-1为减少
        """
        return lottery_manager.start_long_press(self, direction)

    def stop_long_press(self):
        """停止长按"""
        return lottery_manager.stop_long_press(self)

    def closeEvent(self, event):
        """窗口关闭事件，清理资源"""
        try:
            if hasattr(self, "file_watcher"):
                self.file_watcher.removePaths(self.file_watcher.directories())
                self.file_watcher.removePaths(self.file_watcher.files())
            # 停止长按定时器
            if hasattr(self, "press_timer"):
                self.press_timer.stop()
        except Exception as e:
            logger.exception(f"清理文件监控器失败: {e}")
        super().closeEvent(event)

    def initUI(self):
        """初始化UI"""
        container = QWidget()
        lottery_container = QVBoxLayout(container)
        lottery_container.setContentsMargins(0, 0, 0, 0)

        from app.view.components.center_flow_layout import (
            CenterFlowLayout as FlowLayout,
        )

        self.result_widget = QWidget()
        self.result_layout = QVBoxLayout(self.result_widget)
        self.result_grid = FlowLayout()
        self.result_grid.setContentsMargins(0, 0, 0, 0)
        self.result_grid.setVerticalSpacing(GRID_ITEM_SPACING)
        self.result_grid.setHorizontalSpacing(GRID_ITEM_SPACING)
        self.result_grid.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.result_grid.setAnimation(
            readme_settings_async("lottery_settings", "result_flow_animation_duration"),
            QEasingCurve.OutQuad,
        )
        self.result_grid.setAnimationStyle(
            readme_settings_async("lottery_settings", "result_flow_animation_style")
        )
        # 移除拉伸，让内容可以自由扩展，确保滚动正常
        self.result_layout.addLayout(self.result_grid)
        lottery_container.addWidget(self.result_widget)

        # 权重透明化面板（底部，默认隐藏）
        from app.common.display.weight_panel import WeightPanel

        self.weight_panel = WeightPanel(self, settings_group="lottery_settings")
        self.weight_panel.setVisible(False)
        lottery_container.addWidget(self.weight_panel)

        self.reset_button = PushButton(
            get_content_pushbutton_name_async("lottery", "reset_button")
        )
        self._set_widget_font(self.reset_button, 15)
        self.reset_button.setFixedHeight(45)
        self.reset_button.clicked.connect(lambda: self.reset_count())

        # 自定义的鼠标事件处理方法，将右键事件转换为左键事件
        def custom_mouse_press_event(widget, event):
            if event.button() == Qt.MouseButton.RightButton:
                # 将右键按下事件转换为左键按下事件
                new_event = QMouseEvent(
                    QEvent.Type.MouseButtonPress,
                    event.position(),
                    Qt.MouseButton.LeftButton,
                    Qt.MouseButton.LeftButton,
                    Qt.KeyboardModifier.NoModifier,
                )
                QApplication.sendEvent(widget, new_event)
            else:
                # 其他事件正常处理
                PushButton.mousePressEvent(widget, event)

        def custom_mouse_release_event(widget, event):
            if event.button() == Qt.MouseButton.RightButton:
                # 将右键释放事件转换为左键释放事件
                new_event = QMouseEvent(
                    QEvent.Type.MouseButtonRelease,
                    event.position(),
                    Qt.MouseButton.LeftButton,
                    Qt.MouseButton.NoButton,
                    Qt.KeyboardModifier.NoModifier,
                )
                QApplication.sendEvent(widget, new_event)
            else:
                # 其他事件正常处理
                PushButton.mouseReleaseEvent(widget, event)

        self.minus_button = PushButton("-")
        self._set_widget_font(self.minus_button, 20)
        self.minus_button.setFixedSize(45, 45)
        self.minus_button.clicked.connect(lambda: self.update_count(-1))
        # 禁用右键菜单，兼容触屏
        self.minus_button.setContextMenuPolicy(Qt.ContextMenuPolicy.NoContextMenu)

        def minus_press_wrapper(event):
            custom_mouse_press_event(self.minus_button, event)

        def minus_release_wrapper(event):
            custom_mouse_release_event(self.minus_button, event)

        self.minus_button.mousePressEvent = minus_press_wrapper
        self.minus_button.mouseReleaseEvent = minus_release_wrapper

        # 添加长按连续减功能
        self.minus_button.pressed.connect(lambda: self.start_long_press(-1))
        self.minus_button.released.connect(self.stop_long_press)

        self.count_label = BodyLabel("1")
        self.count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._set_widget_font(self.count_label, 20)
        self.count_label.setFixedSize(65, 45)
        self.current_count = 1

        self.plus_button = PushButton("+")
        self._set_widget_font(self.plus_button, 20)
        self.plus_button.setFixedSize(45, 45)
        self.plus_button.clicked.connect(lambda: self.update_count(1))
        # 禁用右键菜单，兼容触屏
        self.plus_button.setContextMenuPolicy(Qt.ContextMenuPolicy.NoContextMenu)

        def plus_press_wrapper(event):
            custom_mouse_press_event(self.plus_button, event)

        def plus_release_wrapper(event):
            custom_mouse_release_event(self.plus_button, event)

        self.plus_button.mousePressEvent = plus_press_wrapper
        self.plus_button.mouseReleaseEvent = plus_release_wrapper

        # 添加长按连续加功能
        self.plus_button.pressed.connect(lambda: self.start_long_press(1))
        self.plus_button.released.connect(self.stop_long_press)

        self.minus_button.setEnabled(False)
        self.plus_button.setEnabled(True)

        self.count_widget = QWidget()
        horizontal_layout = QHBoxLayout()
        horizontal_layout.setContentsMargins(0, 0, 0, 0)
        horizontal_layout.setSpacing(0)
        horizontal_layout.addWidget(self.minus_button)
        horizontal_layout.addStretch()
        horizontal_layout.addWidget(self.count_label)
        horizontal_layout.addStretch()
        horizontal_layout.addWidget(self.plus_button)
        self.count_widget.setLayout(horizontal_layout)

        self.start_button = PrimaryPushButton(
            get_content_pushbutton_name_async("lottery", "start_button")
        )
        self._set_widget_font(self.start_button, 15)
        self.start_button.setFixedHeight(45)
        self.start_button.clicked.connect(lambda: self.start_draw())

        self.pool_list_combobox = ComboBox()
        self._set_widget_font(self.pool_list_combobox, 12)
        self.pool_list_combobox.setFixedHeight(45)
        self.pool_list_combobox.setPlaceholderText(
            get_content_name_async("lottery", "default_empty_item")
        )
        # 延迟填充奖池列表，避免启动时进行文件IO
        self.pool_list_combobox.currentTextChanged.connect(self.on_pool_changed)

        self.list_combobox = ComboBox()
        self._set_widget_font(self.list_combobox, 12)
        self.list_combobox.setFixedHeight(45)
        # 延迟填充班级列表，避免启动时进行文件IO
        self.list_combobox.currentTextChanged.connect(self.on_class_changed)

        self.range_combobox = ComboBox()
        self._set_widget_font(self.range_combobox, 12)
        self.range_combobox.setFixedHeight(45)
        # 延迟填充范围选项
        self.range_combobox.currentTextChanged.connect(self.on_filter_changed)

        self.gender_combobox = ComboBox()
        self._set_widget_font(self.gender_combobox, 12)
        self.gender_combobox.setFixedHeight(45)
        # 延迟填充性别选项
        self.gender_combobox.currentTextChanged.connect(self.on_filter_changed)

        self.remaining_button = PushButton(
            get_content_pushbutton_name_async("lottery", "remaining_button")
        )
        self._set_widget_font(self.remaining_button, 12)
        self.remaining_button.setFixedHeight(45)
        self.remaining_button.clicked.connect(lambda: self.show_remaining_list())

        # 初始时不进行昂贵的数据加载，改为延迟填充
        self.total_count = 0
        self.remaining_count = 0

        text_template = get_any_position_value("lottery", "many_count_label", "text_0")
        # 使用占位值，实际文本将在 populate_lists 中更新
        formatted_text = text_template.format(total_count=0, remaining_count=0)
        self.many_count_label = BodyLabel(formatted_text)
        self.many_count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._set_widget_font(self.many_count_label, 10)

        self.control_widget = QWidget()
        self.control_layout = QVBoxLayout(self.control_widget)
        self.control_layout.setContentsMargins(0, 0, 0, 0)
        self.control_layout.addStretch()

        # 根据页面管理设置决定是否添加控件
        self.add_control_widget_if_enabled(
            self.control_layout,
            self.reset_button,
            "page_management",
            "lottery_reset_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.count_widget,
            "page_management",
            "lottery_quantity_control",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.start_button,
            "page_management",
            "lottery_start_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.pool_list_combobox,
            "page_management",
            "lottery_list",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.list_combobox,
            "page_management",
            "lottery_roll_call_list",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.range_combobox,
            "page_management",
            "lottery_roll_call_range",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.gender_combobox,
            "page_management",
            "lottery_roll_call_gender",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.remaining_button,
            "page_management",
            "lottery_remaining_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.many_count_label,
            "page_management",
            "lottery_quantity_label",
        )

        scroll = SingleDirectionScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet(
            """
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
            """
        )
        QScroller.grabGesture(
            scroll.viewport(),
            QScroller.ScrollerGestureType.LeftMouseButtonGesture,
        )
        scroll.setWidget(container)

        main_layout = QHBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)

        # 根据页面管理设置决定控制面板位置
        lottery_method = readme_settings_async("page_management", "lottery_method")

        if lottery_method == 0:  # 左侧
            main_layout.addWidget(self.control_widget)
            main_layout.addWidget(scroll, 1)
        else:  # 右侧
            main_layout.addWidget(scroll, 1)
            main_layout.addWidget(self.control_widget)

        # 统一调整控件宽度以适应文本内容
        self._adjustControlWidgetWidths()

        # 在事件循环中延迟填充下拉框和初始统计，减少启动阻塞
        QTimer.singleShot(0, self.populate_lists)
        QTimer.singleShot(500, self._show_reminder)

    def _get_remaining_list_args(self):
        return (
            self.pool_list_combobox.currentText(),
            self.range_combobox.currentText(),
            self.gender_combobox.currentText(),
            readme_settings_async("lottery_settings", "half_repeat"),
            self.range_combobox.currentIndex(),
            self.gender_combobox.currentIndex(),
        )

    def add_control_widget_if_enabled(
        self, layout, widget, settings_group, setting_name
    ):
        """根据设置决定是否添加控件到布局"""
        try:
            # 对于数量标签，需要特殊处理显示模式
            if setting_name in ["roll_call_quantity_label", "lottery_quantity_label"]:
                display_mode = readme_settings_async(settings_group, setting_name)
                # display_mode == 3 表示不显示
                if display_mode != 3:
                    layout.addWidget(widget, alignment=Qt.AlignmentFlag.AlignCenter)
            else:
                is_enabled = readme_settings_async(settings_group, setting_name)
                if is_enabled:
                    layout.addWidget(widget, alignment=Qt.AlignmentFlag.AlignCenter)
        except Exception as e:
            logger.exception(f"添加控件 {setting_name} 时出错: {e}")
            # 出错时默认添加控件
            layout.addWidget(widget, alignment=Qt.AlignmentFlag.AlignCenter)

    def _adjustControlWidgetWidths(self):
        """统一调整控件宽度以适应文本内容"""
        try:
            # 收集所有需要调整宽度的控件
            widgets_to_adjust = [
                self.reset_button,
                self.count_widget,
                self.start_button,
                self.pool_list_combobox,
                self.list_combobox,
                self.range_combobox,
                self.gender_combobox,
                self.remaining_button,
                self.many_count_label,
            ]

            # 计算所有控件文本所需的最大宽度
            max_text_width = 0
            for widget in widgets_to_adjust:
                fm = widget.fontMetrics()
                # 检查按钮/标签文本
                if hasattr(widget, "text") and widget.text():
                    text_width = fm.horizontalAdvance(widget.text())
                    max_text_width = max(max_text_width, text_width)
                # 检查占位符文本
                if hasattr(widget, "placeholderText") and widget.placeholderText():
                    text_width = fm.horizontalAdvance(widget.placeholderText())
                    max_text_width = max(max_text_width, text_width)
                # 检查下拉框所有选项的宽度
                if hasattr(widget, "count"):
                    for i in range(widget.count()):
                        item_text = widget.itemText(i)
                        if item_text:
                            text_width = fm.horizontalAdvance(item_text)
                            max_text_width = max(max_text_width, text_width)

            # 计算统一宽度（文本宽度 + 边距 + 下拉框箭头空间）
            padding = 60  # 左右边距 + 下拉箭头空间
            min_width = 200  # 最小宽度
            unified_width = max(min_width, max_text_width + padding)

            # 设置所有控件的固定宽度
            for widget in widgets_to_adjust:
                widget.setFixedWidth(int(unified_width))

        except Exception as e:
            logger.debug(f"调整控件宽度时出错: {e}")

    def on_pool_changed(self, *_):
        """当奖池选择改变时，更新奖数显示"""
        try:
            self.manager.invalidate_total_count_cache()
            self.update_many_count_label()
            total_count = self.manager.get_pool_total_count(
                self.pool_list_combobox.currentText()
            )
            LotteryUtils.update_start_button_state(self.start_button, total_count)

            # 更新剩余名单窗口
            if (
                hasattr(self, "remaining_list_page")
                and self.remaining_list_page is not None
            ):
                QTimer.singleShot(APP_INIT_DELAY, self._update_remaining_list_delayed)
        except Exception as e:
            logger.exception(f"切换奖池时发生错误: {e}")

    def on_class_changed(self, *_):
        return lottery_manager.on_class_changed(self, *_)

    def on_filter_changed(self, *_):
        """当范围或性别选择改变时，更新奖数显示"""
        return lottery_manager.on_filter_changed(self, *_)

    def _update_remaining_list_delayed(self):
        """延迟更新剩余名单窗口的方法"""
        return lottery_manager._update_remaining_list_delayed(self)

    def start_draw(self):
        """开始抽取"""
        return lottery_manager.start_draw(self)

    def stop_animation(self):
        return lottery_manager.stop_animation(self)

    def play_voice_result(self):
        return lottery_manager.play_voice_result(self)

    def animate_result(self):
        """动画过程中更新显示"""
        return lottery_manager.animate_result(self)

    def draw_random(self):
        return lottery_manager.draw_random(self)

    def display_result(self, selected_students, pool_name, selected_students_dict=None):
        return lottery_manager.display_result(
            self,
            selected_students,
            pool_name,
            selected_students_dict=selected_students_dict,
        )

    def display_result_animated(
        self, selected_students, pool_name, selected_students_dict=None
    ):
        return lottery_manager.display_result_animated(
            self,
            selected_students,
            pool_name,
            selected_students_dict=selected_students_dict,
        )

    def _do_reset_count(self):
        result = lottery_manager.do_reset_count(self)
        self._show_reminder()
        return result

    def _show_reminder(self):
        reminder_text = readme_settings_async("lottery_settings", "reminder_text", "")
        if not reminder_text.strip():
            return
        font_size = get_safe_font_size("lottery_settings", "reminder_font_size", 30)
        color_hex = readme_settings_async(
            "lottery_settings", "reminder_text_color", "#808080"
        )
        opacity_raw = readme_settings_async(
            "lottery_settings", "reminder_text_opacity", 50
        )
        try:
            opacity_pct = int(opacity_raw)
        except (ValueError, TypeError):
            opacity_pct = 50
        alpha = max(0, min(255, int(opacity_pct * 255 / 100)))
        color = QColor(color_hex)

        ResultDisplayUtils.clear_grid(self.result_grid)
        label = QLabel(reminder_text)
        label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        font = label.font()
        font.setPointSize(font_size)
        label.setFont(font)
        label.setStyleSheet(
            f"color: rgba({color.red()}, {color.green()}, {color.blue()}, {alpha});"
        )
        self.result_grid.addWidget(label)

    def reset_count(self):
        """重置奖数"""
        return lottery_manager.reset_count(self)

    def clear_result(self):
        """清空结果显示"""
        ResultDisplayUtils.clear_grid(self.result_grid)
        if hasattr(self, "weight_panel"):
            self.weight_panel.clear()
            self.weight_panel.setVisible(False)

    def update_count(self, change):
        return lottery_manager.update_count(self, change)

    def get_total_count(self):
        return lottery_manager.get_total_count(self)

    def update_many_count_label(self):
        return lottery_manager.update_many_count_label(self)

    def update_remaining_list_window(self):
        return lottery_manager.update_remaining_list_window(self)

    def show_remaining_list(self):
        return lottery_manager.show_remaining_list(self)

    def setup_file_watcher(self):
        return lottery_manager.setup_file_watcher(self)

    def on_directory_changed(self, path):
        return lottery_manager.on_directory_changed(self, path)

    def on_file_changed(self, path):
        return lottery_manager.on_file_changed(self, path)

    def _sync_watcher_files(self):
        """同步奖池目录下的文件到文件监控器"""
        try:
            # 先移除已监控的文件，避免重复
            files = (
                list(self.file_watcher.files())
                if hasattr(self.file_watcher, "files")
                else []
            )
            if files:
                try:
                    self.file_watcher.removePaths(files)
                except Exception:
                    pass

            # 重新添加当前目录中的所有JSON文件
            lot_dir = get_data_path("list", "lottery_list")
            if lot_dir.exists():
                for fp in lot_dir.glob("*.json"):
                    try:
                        self.file_watcher.addPath(str(fp))
                    except Exception:
                        pass
        except Exception as e:
            logger.exception(f"同步奖池文件监控失败: {e}")

    def refresh_pool_list(self):
        """刷新奖池列表下拉框"""
        try:
            self.manager.invalidate_total_count_cache()
            current_pool = self.pool_list_combobox.currentText()

            new_pool_list = get_pool_name_list()

            self.pool_list_combobox.blockSignals(True)

            self.pool_list_combobox.clear()
            self.pool_list_combobox.addItems(new_pool_list)

            if current_pool in new_pool_list:
                index = self.pool_list_combobox.findText(current_pool)
                if index >= 0:
                    self.pool_list_combobox.setCurrentIndex(index)
            elif new_pool_list:
                self.pool_list_combobox.setCurrentIndex(0)

            self.pool_list_combobox.blockSignals(False)

            self.on_pool_changed()
            self.on_class_changed()

        except Exception as e:
            logger.exception(f"刷新奖池列表失败: {e}")

    def populate_lists(self):
        return lottery_manager.populate_lists(self)

    def setupSettingsListener(self):
        return lottery_manager.setup_settings_listener(self)

    def onSettingsChanged(self, first_level_key, second_level_key, value):
        return lottery_manager.on_settings_changed(
            self, first_level_key, second_level_key, value
        )

    def updateUI(self):
        """更新UI控件的可见性"""
        # 清除现有布局中的控件
        self.clearLayout(self.control_layout)

        # 根据页面管理设置决定是否添加控件
        self.add_control_widget_if_enabled(
            self.control_layout,
            self.reset_button,
            "page_management",
            "lottery_reset_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.count_widget,
            "page_management",
            "lottery_quantity_control",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.start_button,
            "page_management",
            "lottery_start_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.pool_list_combobox,
            "page_management",
            "lottery_list",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.list_combobox,
            "page_management",
            "lottery_roll_call_list",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.range_combobox,
            "page_management",
            "lottery_roll_call_range",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.gender_combobox,
            "page_management",
            "lottery_roll_call_gender",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.remaining_button,
            "page_management",
            "lottery_remaining_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.many_count_label,
            "page_management",
            "lottery_quantity_label",
        )

    def clearLayout(self, layout):
        """清除布局中的所有控件"""
        while layout.count():
            child = layout.takeAt(0)
            if child.widget():
                child.widget().setParent(None)

    def eventFilter(self, obj, event):
        """事件过滤器，处理触屏长按事件"""
        if obj in (self.minus_button, self.plus_button):
            if event.type() == QEvent.Type.MouseButtonPress:
                # 处理左/右键点击，确保长按功能正常
                return super().eventFilter(obj, event)
            # 其他事件正常处理
        return super().eventFilter(obj, event)

    def _set_widget_font(self, widget, font_size):
        return lottery_manager.set_widget_font(widget, font_size)
