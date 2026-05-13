# ==================================================
# 标准库导入
# ==================================================
from random import SystemRandom

# ==================================================
# 第三方库导入
# ==================================================
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import PushButton, SingleDirectionScrollArea

# ==================================================
# 本地模块导入
# ==================================================
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
from app.common.roll_call.roll_call_manager import RollCallManager
from app.common.roll_call import roll_call_manager

from app.page_building.another_window import *

system_random = SystemRandom()


# ==================================================
# 班级点名类
# ==================================================
class roll_call(QWidget):
    """班级点名主界面类

    提供班级点名功能，支持：
    - 按班级、小组、性别筛选
    - 随机抽取指定人数
    - 显示剩余名单
    - 长按连续调整人数
    """

    settingsChanged = Signal()

    def __init__(self, parent=None):
        """初始化班级点名界面

        Args:
            parent: 父窗口对象
        """
        super().__init__(parent)
        self._init_file_watcher()
        self._init_long_press()
        self._init_tts()
        self._init_animation_state()
        self.manager = RollCallManager()
        self._init_ui()
        self._setup_settings_listener()

    def _init_file_watcher(self):
        """初始化文件监控器"""
        return roll_call_manager.init_file_watcher(self)

    def _init_long_press(self):
        """初始化长按功能相关属性"""
        return roll_call_manager.init_long_press(self)

    def _init_tts(self):
        """初始化文本转语音处理器"""
        return roll_call_manager.init_tts(self)

    def _init_animation_state(self):
        """初始化动画状态"""
        return roll_call_manager.init_animation_state(self)

    def _init_ui(self):
        """初始化用户界面"""
        self.initUI()

    def _setup_settings_listener(self):
        """设置设置监听器"""
        return roll_call_manager.setup_settings_listener(self)

    def handle_long_press(self):
        """处理长按事件

        当长按状态激活时，以连续触发间隔更新计数
        """
        return roll_call_manager.handle_long_press(self)

    def start_long_press(self, direction):
        """开始长按

        Args:
            direction (int): 长按方向，1为增加，-1为减少
        """
        return roll_call_manager.start_long_press(self, direction)

    def stop_long_press(self):
        """停止长按"""
        return roll_call_manager.stop_long_press(self)

    def closeEvent(self, event):
        """窗口关闭事件，清理资源

        Args:
            event: 关闭事件对象
        """
        try:
            if hasattr(self, "file_watcher"):
                self.file_watcher.removePaths(self.file_watcher.directories())
                self.file_watcher.removePaths(self.file_watcher.files())
            if hasattr(self, "press_timer"):
                self.press_timer.stop()
        except Exception as e:
            logger.exception(f"清理文件监控器失败: {e}")
        super().closeEvent(event)

    def initUI(self):
        """初始化用户界面

        创建并布局所有控件，包括：
        - 结果显示区域
        - 控制按钮（重置、开始、剩余名单）
        - 计数控制（加减按钮）
        - 筛选下拉框（班级、范围、性别）
        - 人数统计标签
        """
        container = QWidget()
        roll_call_container = QVBoxLayout(container)
        roll_call_container.setContentsMargins(0, 0, 0, 0)

        self._create_result_widget(roll_call_container)
        self._create_buttons()
        self._create_count_control()
        self._create_comboboxes()
        self._create_count_label()
        self._setup_control_widget()
        self._setup_main_layout(container)

        QTimer.singleShot(0, self.populate_lists)
        QTimer.singleShot(500, self._show_reminder)

    def _create_result_widget(self, parent_layout):
        """创建结果显示区域

        Args:
            parent_layout: 父布局对象
        """
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
            readme_settings_async(
                "roll_call_settings", "result_flow_animation_duration"
            ),
            QEasingCurve.OutQuad,
        )
        self.result_grid.setAnimationStyle(
            readme_settings_async("roll_call_settings", "result_flow_animation_style")
        )
        self.result_layout.addLayout(self.result_grid)
        parent_layout.addWidget(self.result_widget)

        # 权重透明化面板（底部，默认隐藏）
        from app.common.display.weight_panel import WeightPanel

        self.weight_panel = WeightPanel(self, settings_group="roll_call_settings")
        self.weight_panel.setVisible(False)
        parent_layout.addWidget(self.weight_panel)

    def _create_buttons(self):
        """创建按钮控件"""
        self.reset_button = self._create_button(
            "roll_call", "reset_button", 15, self.reset_count
        )
        self.start_button = self._create_button(
            "roll_call", "start_button", 15, self.start_draw, is_primary=True
        )
        self.remaining_button = self._create_button(
            "roll_call", "remaining_button", 12, self.show_remaining_list
        )

    def _create_count_control(self):
        """创建计数控制控件"""
        self.minus_button, self.plus_button, self.count_widget = (
            self._create_count_control_widget()
        )

    def _create_comboboxes(self):
        """创建下拉框控件"""
        self.list_combobox = self._create_combobox(
            "roll_call", "default_empty_item", 12, self.on_class_changed
        )
        self.range_combobox = self._create_combobox(
            "roll_call", None, 12, self.on_filter_changed
        )
        self.gender_combobox = self._create_combobox(
            "roll_call", None, 12, self.on_filter_changed
        )

    def _create_count_label(self):
        """创建人数统计标签"""
        self.total_count = 0
        self.remaining_count = 0

        text_template = get_any_position_value(
            "roll_call", "many_count_label", "text_0"
        )
        formatted_text = text_template.format(total_count=0, remaining_count=0)
        self.many_count_label = BodyLabel(formatted_text)
        self.many_count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._set_widget_font(self.many_count_label, 10)

    def _setup_control_widget(self):
        """设置控制控件区域"""
        self.control_widget = QWidget()
        self.control_layout = QVBoxLayout(self.control_widget)
        self.control_layout.setContentsMargins(0, 0, 0, 0)
        self.control_layout.addStretch()
        self._add_control_widgets()

    def _setup_main_layout(self, container):
        """设置主布局

        Args:
            container: 容器控件
        """
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

        roll_call_method = readme_settings_async("page_management", "roll_call_method")

        if roll_call_method == 0:
            main_layout.addWidget(self.control_widget)
            main_layout.addWidget(scroll, 1)
        else:
            main_layout.addWidget(scroll, 1)
            main_layout.addWidget(self.control_widget)

        self._adjustControlWidgetWidths()

    def _create_button(
        self, content_key, button_key, font_size, callback, is_primary=False
    ):
        """创建按钮

        Args:
            content_key: 语言配置中的内容键
            button_key: 语言配置中的按钮键
            font_size: 字体大小
            callback: 点击回调函数
            is_primary: 是否为主按钮（使用PrimaryPushButton）

        Returns:
            创建的按钮对象
        """
        if is_primary:
            button = PrimaryPushButton(
                get_content_pushbutton_name_async(content_key, button_key)
            )
        else:
            button = PushButton(
                get_content_pushbutton_name_async(content_key, button_key)
            )
        self._set_widget_font(button, font_size)
        button.setFixedHeight(45)
        button.clicked.connect(lambda: callback())
        return button

    def _create_combobox(self, content_key, placeholder_key, font_size, callback):
        """创建下拉框

        Args:
            content_key: 语言配置中的内容键
            placeholder_key: 语言配置中的占位符键（可为None）
            font_size: 字体大小
            callback: 文本变化回调函数

        Returns:
            创建的下拉框对象
        """
        combobox = ComboBox()
        self._set_widget_font(combobox, font_size)
        combobox.setFixedHeight(45)
        if placeholder_key:
            combobox.setPlaceholderText(
                get_content_name_async(content_key, placeholder_key)
            )
        combobox.currentTextChanged.connect(callback)
        return combobox

    def _create_count_control_widget(self):
        """创建计数控制控件

        包含减号按钮、计数标签、加号按钮

        Returns:
            tuple: (minus_button, plus_button, count_widget)
        """
        minus_button = self._create_button_with_long_press("-", 20, -1)
        plus_button = self._create_button_with_long_press("+", 20, 1)

        minus_button.setEnabled(False)
        plus_button.setEnabled(True)

        self.count_label = BodyLabel("1")
        self.count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._set_widget_font(self.count_label, 20)
        self.count_label.setFixedSize(65, 45)
        self.current_count = 1

        count_widget = QWidget()
        horizontal_layout = QHBoxLayout()
        horizontal_layout.setContentsMargins(0, 0, 0, 0)
        horizontal_layout.setSpacing(0)
        horizontal_layout.addWidget(minus_button)
        horizontal_layout.addStretch()
        horizontal_layout.addWidget(self.count_label)
        horizontal_layout.addStretch()
        horizontal_layout.addWidget(plus_button)
        count_widget.setLayout(horizontal_layout)

        return minus_button, plus_button, count_widget

    def _create_button_with_long_press(self, text, font_size, direction):
        """创建带长按功能的按钮

        Args:
            text: 按钮文本
            font_size: 字体大小
            direction: 长按方向（1为增加，-1为减少）

        Returns:
            创建的按钮对象
        """
        button = PushButton(text)
        self._set_widget_font(button, font_size)
        button.setFixedSize(45, 45)
        button.clicked.connect(lambda: self.update_count(direction))
        button.setContextMenuPolicy(Qt.ContextMenuPolicy.NoContextMenu)

        button.mousePressEvent = lambda event: self._custom_mouse_press_event(
            button, event
        )
        button.mouseReleaseEvent = lambda event: self._custom_mouse_release_event(
            button, event
        )

        button.pressed.connect(lambda: self.start_long_press(direction))
        button.released.connect(self.stop_long_press)

        return button

    def _custom_mouse_press_event(self, widget, event):
        """自定义鼠标按下事件，将右键转换为左键

        Args:
            widget: 控件对象
            event: 鼠标事件对象
        """
        if event.button() == Qt.MouseButton.RightButton:
            new_event = QMouseEvent(
                QEvent.Type.MouseButtonPress,
                event.position(),
                Qt.MouseButton.LeftButton,
                Qt.MouseButton.LeftButton,
                Qt.KeyboardModifier.NoModifier,
            )
            QApplication.sendEvent(widget, new_event)
        else:
            PushButton.mousePressEvent(widget, event)

    def _custom_mouse_release_event(self, widget, event):
        """自定义鼠标释放事件，将右键转换为左键

        Args:
            widget: 控件对象
            event: 鼠标事件对象
        """
        if event.button() == Qt.MouseButton.RightButton:
            new_event = QMouseEvent(
                QEvent.Type.MouseButtonRelease,
                event.position(),
                Qt.MouseButton.LeftButton,
                Qt.MouseButton.NoButton,
                Qt.KeyboardModifier.NoModifier,
            )
            QApplication.sendEvent(widget, new_event)
        else:
            PushButton.mouseReleaseEvent(widget, event)

    def _add_control_widgets(self):
        """添加控制控件到布局

        根据页面管理设置决定是否显示各个控件
        """
        widgets_config = [
            (self.reset_button, "page_management", "roll_call_reset_button"),
            (self.count_widget, "page_management", "roll_call_quantity_control"),
            (self.start_button, "page_management", "roll_call_start_button"),
            (self.list_combobox, "page_management", "roll_call_list"),
            (self.range_combobox, "page_management", "roll_call_range"),
            (self.gender_combobox, "page_management", "roll_call_gender"),
            (self.remaining_button, "page_management", "roll_call_remaining_button"),
            (self.many_count_label, "page_management", "roll_call_quantity_label"),
        ]

        for widget, settings_group, setting_name in widgets_config:
            self.add_control_widget_if_enabled(
                self.control_layout, widget, settings_group, setting_name
            )

    def add_control_widget_if_enabled(
        self, layout, widget, settings_group, setting_name
    ):
        """根据设置决定是否添加控件到布局

        Args:
            layout: 布局对象
            widget: 要添加的控件
            settings_group: 设置组名称
            setting_name: 设置项名称
        """
        try:
            if setting_name in ["roll_call_quantity_label", "lottery_quantity_label"]:
                display_mode = readme_settings_async(settings_group, setting_name)
                if display_mode != 3:
                    layout.addWidget(widget, alignment=Qt.AlignmentFlag.AlignCenter)
            else:
                is_enabled = readme_settings_async(settings_group, setting_name)
                if is_enabled:
                    layout.addWidget(widget, alignment=Qt.AlignmentFlag.AlignCenter)
        except Exception as e:
            logger.exception(f"添加控件 {setting_name} 时出错: {e}")
            layout.addWidget(widget, alignment=Qt.AlignmentFlag.AlignCenter)

    def _adjustControlWidgetWidths(self):
        """统一调整控件宽度以适应文本内容

        计算所有控件文本所需的最大宽度，并设置统一宽度
        """
        try:
            widgets_to_adjust = [
                self.reset_button,
                self.count_widget,
                self.start_button,
                self.list_combobox,
                self.range_combobox,
                self.gender_combobox,
                self.remaining_button,
                self.many_count_label,
            ]

            max_text_width = self._calculate_max_text_width(widgets_to_adjust)

            padding = 60
            min_width = 200
            unified_width = max(min_width, max_text_width + padding)

            for widget in widgets_to_adjust:
                widget.setFixedWidth(int(unified_width))

        except Exception as e:
            logger.debug(f"调整控件宽度时出错: {e}")

    def _calculate_max_text_width(self, widgets):
        """计算控件集合中所需的最大文本宽度

        Args:
            widgets: 控件列表

        Returns:
            int: 最大文本宽度
        """
        max_text_width = 0
        for widget in widgets:
            fm = widget.fontMetrics()
            if hasattr(widget, "text") and widget.text():
                text_width = fm.horizontalAdvance(widget.text())
                max_text_width = max(max_text_width, text_width)
            if hasattr(widget, "placeholderText") and widget.placeholderText():
                text_width = fm.horizontalAdvance(widget.placeholderText())
                max_text_width = max(max_text_width, text_width)
            if hasattr(widget, "count"):
                for i in range(widget.count()):
                    item_text = widget.itemText(i)
                    if item_text:
                        text_width = fm.horizontalAdvance(item_text)
                        max_text_width = max(max_text_width, text_width)
        return max_text_width

    def on_class_changed(self):
        return roll_call_manager.on_class_changed(self)

    def _update_range_options(self):
        """更新范围下拉框选项"""
        return roll_call_manager._update_range_options(self)

    def _update_gender_options(self):
        """更新性别下拉框选项"""
        return roll_call_manager._update_gender_options(self)

    def _update_start_button_state(self):
        """根据总人数更新开始按钮状态"""
        return roll_call_manager._update_start_button_state(self)

    def _update_remaining_list_window(self):
        """延迟更新剩余名单窗口"""
        return roll_call_manager._update_remaining_list_window(self)

    def on_filter_changed(self):
        """当范围或性别选择改变时，更新人数显示"""
        return roll_call_manager.on_filter_changed(self)

    def _update_remaining_list_delayed(self):
        """延迟更新剩余名单窗口的方法"""
        return roll_call_manager._update_remaining_list_delayed(self)

    def start_draw(self):
        """开始抽取"""
        return roll_call_manager.start_draw(self)

    def stop_animation(self):
        return roll_call_manager.stop_animation(self)

    def play_voice_result(self):
        return roll_call_manager.play_voice_result(self)

    def animate_result(self):
        """动画过程中更新显示"""
        return roll_call_manager.animate_result(self)

    def draw_random(self):
        return roll_call_manager.draw_random(self)

    def display_result(
        self,
        selected_students,
        class_name,
        display_settings=None,
        selected_students_dict=None,
    ):
        return roll_call_manager.display_result(
            self,
            selected_students,
            class_name,
            display_settings,
            selected_students_dict=selected_students_dict,
        )

    def display_result_animated(self, selected_students, class_name):
        return roll_call_manager.display_result_animated(
            self, selected_students, class_name
        )

    def _do_reset_count(self):
        result = roll_call_manager.do_reset_count(self)
        self._show_reminder()
        return result

    def _show_reminder(self):
        reminder_text = readme_settings_async("roll_call_settings", "reminder_text", "")
        if not reminder_text.strip():
            return
        font_size = get_safe_font_size("roll_call_settings", "reminder_font_size", 30)
        color_hex = readme_settings_async(
            "roll_call_settings", "reminder_text_color", "#808080"
        )
        opacity_raw = readme_settings_async(
            "roll_call_settings", "reminder_text_opacity", 50
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
        """重置人数"""
        return roll_call_manager.reset_count(self)

    def clear_result(self):
        """清空结果显示"""
        ResultDisplayUtils.clear_grid(self.result_grid)
        if hasattr(self, "weight_panel"):
            self.weight_panel.clear()
            self.weight_panel.setVisible(False)

    def update_count(self, change):
        return roll_call_manager.update_count(self, change)

    def get_total_count(self):
        return roll_call_manager.get_total_count(self)

    def update_many_count_label(self):
        return roll_call_manager.update_many_count_label(self)

    def update_remaining_list_window(self):
        return roll_call_manager.update_remaining_list_window(self)

    def show_remaining_list(self):
        return roll_call_manager.show_remaining_list(self)

    def setup_file_watcher(self):
        return roll_call_manager.setup_file_watcher(self)

    def on_directory_changed(self, path):
        return roll_call_manager.on_directory_changed(self, path)

    def on_file_changed(self, path):
        return roll_call_manager.on_file_changed(self, path)

    def refresh_class_list(self):
        return roll_call_manager.refresh_class_list(self)

    def populate_lists(self):
        return roll_call_manager.populate_lists(self)

    def _populate_class_list(self):
        return roll_call_manager._populate_class_list(self)

    def _populate_range_combobox(self):
        return roll_call_manager._populate_range_combobox(self)

    def _populate_gender_combobox(self):
        return roll_call_manager._populate_gender_combobox(self)

    def _update_count_label(self):
        return roll_call_manager._update_count_label(self)

    def setupSettingsListener(self):
        return roll_call_manager.setup_settings_listener(self)

    def onSettingsChanged(self, first_level_key, second_level_key, value):
        return roll_call_manager.on_settings_changed(
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
            "roll_call_reset_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.count_widget,
            "page_management",
            "roll_call_quantity_control",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.start_button,
            "page_management",
            "roll_call_start_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout, self.list_combobox, "page_management", "roll_call_list"
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.range_combobox,
            "page_management",
            "roll_call_range",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.gender_combobox,
            "page_management",
            "roll_call_gender",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.remaining_button,
            "page_management",
            "roll_call_remaining_button",
        )

        self.add_control_widget_if_enabled(
            self.control_layout,
            self.many_count_label,
            "page_management",
            "roll_call_quantity_label",
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
        return roll_call_manager.set_widget_font(widget, font_size)
