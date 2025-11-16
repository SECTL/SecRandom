# ==================================================
# 导入库
# ==================================================
import random
import colorsys

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
from app.tools.list import *

from random import SystemRandom
system_random = SystemRandom()

# ==================================================
# 结果显示工具类
# ==================================================
class ResultDisplayUtils:
    """结果显示工具类，提供通用的结果显示功能"""

    _color_cache = {}

    @staticmethod
    def _clear_color_cache():
        """清除颜色缓存"""
        ResultDisplayUtils._color_cache.clear()

    @staticmethod
    def _init_theme_listener():
        """初始化主题变化监听器"""
        if not hasattr(ResultDisplayUtils, "_theme_listener_initialized"):
            qconfig.themeChanged.connect(ResultDisplayUtils._clear_color_cache)
            ResultDisplayUtils._theme_listener_initialized = True

    @staticmethod
    def _create_avatar_widget(image_path, name, font_size):
        """
        创建头像组件

        参数:
            image_path: 图片路径
            name: 学生姓名
            font_size: 字体大小

        返回:
            AvatarWidget: 创建的头像组件
        """
        if image_path is not None:
            avatar = AvatarWidget(image_path)
        else:
            avatar = AvatarWidget()
            avatar.setText(name)

        return avatar

    @staticmethod
    def _format_student_text(class_name, display_format, student_id_str, name, draw_count, is_group_mode=False, show_random=0):
        """
        格式化学生显示文本

        参数:
            display_format: 显示格式 (0:学号+姓名, 1:仅姓名, 2:仅学号)
            student_id_str: 学号字符串
            name: 学生姓名或小组名称
            draw_count: 抽取人数
            is_group_mode: 是否为小组模式
            show_random: 随机组员显示格式 (0:不显示, 1:组名[换行]姓名, 2:组名[短横杠]姓名)

        返回:
            str: 格式化后的文本
        """
        # 小组模式下，根据show_random设置显示格式
        if is_group_mode:
            # 获取小组成员列表
            group_members = get_group_members(class_name, name)
            
            if show_random == 1:  # 组名[换行]随机选择的成员
                if group_members:
                    # 随机选择一个成员
                    selected_member = system_random.choice(group_members)
                    selected_name = selected_member["name"]
                    return f"{name}\n{selected_name}"
                else:
                    return name
            elif show_random == 2:  # 组名[短横杠]随机选择的成员
                if group_members:
                    # 随机选择一个成员
                    selected_member = system_random.choice(group_members)
                    selected_name = selected_member["name"]
                    return f"{name} - {selected_name}"
                else:
                    return name
            else:  # 不显示特殊格式 - 只显示组名
                return f"{name}"
            
        if display_format == 1:  # 仅显示姓名
            return f"{name}"
        elif display_format == 2:  # 仅显示学号
            return f"{student_id_str}"
        else:  # 显示学号+姓名
            if draw_count == 1:
                return f"{student_id_str}\n{name}"
            else:
                return f"{student_id_str} {name}"

    @staticmethod
    def _create_student_label_with_avatar(
        image_path, name, font_size, draw_count, text
    ):
        """
        创建带头像的学生标签

        参数:
            image_path: 图片路径
            name: 学生姓名
            font_size: 字体大小
            draw_count: 抽取人数
            text: 显示文本

        返回:
            QWidget: 包含头像和文本的容器组件
        """
        # 创建水平布局
        h_layout = QHBoxLayout()
        h_layout.setSpacing(AVATAR_LABEL_SPACING)
        h_layout.setContentsMargins(0, 0, 0, 0)

        # 创建容器widget
        container = QWidget()
        container.setLayout(h_layout)

        # 创建头像
        avatar = ResultDisplayUtils._create_avatar_widget(image_path, name, font_size)
        avatar.setRadius(font_size * 2 if draw_count == 1 else font_size // 2)

        # 创建文本标签
        text_label = BodyLabel(text)

        # 添加到布局
        h_layout.addWidget(avatar)
        h_layout.addWidget(text_label)

        return container

    @staticmethod
    def _apply_label_style(label, font_size, animation_color):
        """
        应用标签样式

        参数:
            label: 标签组件
            font_size: 字体大小
            animation_color: 动画颜色模式
        """
        fixed_color = readme_settings_async(
            "roll_call_settings", "animation_fixed_color"
        )
        if (
            isinstance(label, QWidget)
            and hasattr(label, "layout")
            and label.layout() is not None
        ):
            layout = label.layout()
            if layout:
                for i in range(layout.count()):
                    item = layout.itemAt(i)
                    widget = item.widget()
                    if isinstance(widget, BodyLabel):
                        widget.setAlignment(Qt.AlignmentFlag.AlignCenter)
                        style_sheet = f"font-size: {font_size}pt; "

                        if animation_color == 1:
                            style_sheet += f"color: {ResultDisplayUtils._generate_vibrant_color()};"
                        elif animation_color == 2:
                            style_sheet += f"color: {fixed_color};"

                        widget.setStyleSheet(style_sheet)
        else:
            label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            style_sheet = f"font-size: {font_size}pt; "
            fixed_color = readme_settings_async(
                "roll_call_settings", "animation_fixed_color"
            )
            if animation_color == 1:
                style_sheet += f"color: {ResultDisplayUtils._generate_vibrant_color()};"
            elif animation_color == 2:
                style_sheet += f"color: {fixed_color};"

            label.setStyleSheet(style_sheet)

    @staticmethod
    def create_student_label(
        class_name,
        selected_students,
        draw_count=1,
        font_size=50,
        animation_color=0,
        display_format=0,
        show_student_image=False,
        group_index=0,
        show_random=0,
    ):
        """
        创建学生显示标签

        参数:
            selected_students: 选中的学生列表 [(num, selected, exist), ...]
            draw_count: 抽取人数
            font_size: 字体大小
            animation_color: 动画颜色模式 (0:默认, 1:随机颜色, 2:固定颜色)
            display_format: 显示格式 (0:学号+姓名, 1:仅姓名, 2:仅学号)
            show_student_image: 是否显示学生头像
            group_index: 小组索引 (0:全班, 1:随机小组, >1:指定小组)
            show_random: 随机组员显示格式 (0:不显示, 1:组名[换行]姓名, 2:组名[短横杠]姓名)

        返回:
            list: 创建的标签列表
        """
        student_labels = []

        for num, selected, exist in selected_students:
            current_image_path = None
            # 在小组模式下，尝试使用小组名称作为图片文件名
            if show_student_image:
                image_name = str(selected)
                for ext in SUPPORTED_IMAGE_EXTENSIONS:
                    temp_path = get_resources_path(
                        "images", f"students/{image_name}{ext}"
                    )
                    if file_exists(temp_path):
                        current_image_path = str(temp_path)
                        break
                    else:
                        current_image_path = None
                        continue

            # 处理学号格式化
            if num is not None:
                student_id_str = STUDENT_ID_FORMAT.format(num=num)
            else:
                student_id_str = ""
                
            # 处理不同模式下的名称显示
            if len(str(selected)) == 2 and group_index == 0:
                name = f"{str(selected)[0]}{NAME_SPACING}{str(selected)[1]}"
            else:
                name = str(selected)
                
            text = ResultDisplayUtils._format_student_text(
                class_name, display_format, student_id_str, name, draw_count, is_group_mode=(group_index == 1), show_random=show_random
            )
            
            # 在小组模式下，只在没有成员显示时才显示头像
            if show_student_image:
                label = ResultDisplayUtils._create_student_label_with_avatar(
                    current_image_path, name, font_size, draw_count, text
                )
            else:
                label = BodyLabel(text)
                
            ResultDisplayUtils._apply_label_style(label, font_size, animation_color)
            student_labels.append(label)

        return student_labels

    @staticmethod
    def _generate_vibrant_color(
        min_saturation=DEFAULT_MIN_SATURATION,
        max_saturation=DEFAULT_MAX_SATURATION,
        min_value=DEFAULT_MIN_VALUE,
        max_value=DEFAULT_MAX_VALUE,
        use_cache=True,
    ):
        """生成鲜艳直观的颜色

        根据当前主题自动调整颜色明亮程度：
        - 浅色主题：降低亮度范围，避免颜色过于明亮导致看不清
        - 深色主题：使用正常亮度范围，确保颜色在深色背景上清晰可见

        参数:
            min_saturation: 最小饱和度 (默认DEFAULT_MIN_SATURATION)
            max_saturation: 最大饱和度 (默认DEFAULT_MAX_SATURATION)
            min_value: 最小亮度值 (默认DEFAULT_MIN_VALUE)
            max_value: 最大亮度值 (默认DEFAULT_MAX_VALUE)
            use_cache: 是否使用颜色缓存 (默认True)

        返回:
            str: RGB格式的颜色字符串，如"rgb(255,100,50)"
        """
        ResultDisplayUtils._init_theme_listener()
        if qconfig.theme == Theme.LIGHT:  # 浅色主题
            adjusted_min_value = min(
                min_value * LIGHT_VALUE_MULTIPLIER, LIGHT_THEME_MAX_VALUE
            )
            adjusted_max_value = min(
                max_value * LIGHT_MAX_VALUE_MULTIPLIER, LIGHT_THEME_ADJUSTED_MAX_VALUE
            )
        elif qconfig.theme == Theme.AUTO:  # 自动主题
            lightness = QApplication.palette().color(QPalette.Window).lightness()
            if lightness > LIGHTNESS_THRESHOLD:  # 浅色主题
                adjusted_min_value = min(
                    min_value * LIGHT_VALUE_MULTIPLIER, LIGHT_THEME_MAX_VALUE
                )
                adjusted_max_value = min(
                    max_value * LIGHT_MAX_VALUE_MULTIPLIER,
                    LIGHT_THEME_ADJUSTED_MAX_VALUE,
                )
            else:  # 深色主题
                adjusted_min_value = min(
                    min_value * DARK_VALUE_MULTIPLIER, DARK_THEME_MIN_VALUE
                )
                adjusted_max_value = max(
                    max_value * DARK_MAX_VALUE_MULTIPLIER, DARK_THEME_MAX_VALUE
                )
        else:  # 深色主题或其他主题
            adjusted_min_value = min(
                min_value * DARK_VALUE_MULTIPLIER, DARK_THEME_MIN_VALUE
            )
            adjusted_max_value = max(
                max_value * DARK_MAX_VALUE_MULTIPLIER, DARK_THEME_MAX_VALUE
            )
        h = random.random()
        s = random.uniform(min_saturation, max_saturation)
        v = random.uniform(adjusted_min_value, adjusted_max_value)
        r, g, b = (int(c * 255) for c in colorsys.hsv_to_rgb(h, s, v))
        color_str = RGB_COLOR_FORMAT.format(r=r, g=g, b=b)
        return color_str

    @staticmethod
    def display_results_in_grid(result_grid, student_labels, alignment=None):
        """
        在网格布局中显示结果

        参数:
            result_grid: QGridLayout 网格布局
            student_labels: 学生标签列表
            alignment: 对齐方式 (默认为居中)
        """
        if alignment is None:
            alignment = Qt.AlignmentFlag.AlignCenter
        result_grid.setAlignment(alignment)
        while result_grid.count():
            item = result_grid.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        container = QWidget()
        grid_layout = QGridLayout()
        grid_layout.setSpacing(GRID_LAYOUT_SPACING)  # 设置统一的间距
        grid_layout.setHorizontalSpacing(GRID_HORIZONTAL_SPACING)  # 设置水平间距
        grid_layout.setVerticalSpacing(GRID_VERTICAL_SPACING)  # 设置垂直间距
        container.setLayout(grid_layout)
        if student_labels:
            parent_widget = result_grid.parentWidget()
            if parent_widget:
                available_width = parent_widget.width() - GRID_ITEM_MARGIN
            else:
                available_width = DEFAULT_AVAILABLE_WIDTH
            total_width = (
                sum(label.sizeHint().width() for label in student_labels)
                + len(student_labels) * GRID_ITEM_SPACING
            )
            if total_width > available_width:
                avg_label_width = total_width / len(student_labels)
                max_columns = max(1, int(available_width // avg_label_width))
            else:
                max_columns = len(student_labels)
        else:
            max_columns = 1
        for i, label in enumerate(student_labels):
            row = i // max_columns
            col = i % max_columns
            grid_layout.addWidget(label, row, col)
        result_grid.addWidget(container)

    @staticmethod
    def clear_grid(result_grid):
        """
        清空网格布局中的所有小部件

        参数:
            result_grid: QGridLayout 网格布局
        """
        while result_grid.count():
            item = result_grid.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
