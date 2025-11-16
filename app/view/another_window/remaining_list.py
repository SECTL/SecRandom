"""
剩余名单页面
用于显示未抽取的学生名单
"""

import json
from typing import Dict, Any

from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from qfluentwidgets import *

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from app.tools.config import *
from app.tools.list import *


class RemainingListPage(QWidget):
    """剩余名单页面类"""
    
    # 定义信号，当剩余人数变化时发出
    count_changed = Signal(int)
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.class_name = ""
        self.group_filter = ""
        self.gender_filter = ""
        self.half_repeat = 0
        self.group_index = 0
        self.gender_index = 0
        self.remaining_students = []
        
        # 布局更新状态跟踪
        self._last_layout_width = 0
        self._last_card_count = 0
        self._layout_update_in_progress = False
        self._resize_timer = None
        
        self.init_ui()

        # 延迟加载学生数据
        QTimer.singleShot(100, self.load_student_data)

    def init_ui(self):
        """初始化UI"""
        # 主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(10, 10, 10, 10)
        self.main_layout.setSpacing(10)

        # 使用异步函数获取标题文本
        title_text = get_content_name_async("remaining_list", "title")
        count_text = get_any_position_value_async(
            "remaining_list", "count_label", "name"
        )

        # 标题
        self.title_label = SubtitleLabel(title_text)
        self.title_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.title_label.setFont(QFont(load_custom_font(), 18))
        self.main_layout.addWidget(self.title_label)

        # 剩余人数标签
        self.count_label = BodyLabel(count_text.format(count=0))
        self.count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.count_label.setFont(QFont(load_custom_font(), 12))
        self.main_layout.addWidget(self.count_label)

        # 创建网格布局
        self.grid_layout = QGridLayout()
        self.grid_layout.setSpacing(STUDENT_CARD_SPACING)
        self.main_layout.addLayout(self.grid_layout)

        # 初始化卡片列表
        self.cards = []

    def get_students_file(self):
        """获取学生数据文件路径"""
        # 获取班级名单文件路径
        roll_call_list_dir = get_path("app/resources/list/roll_call_list")
        students_file = roll_call_list_dir / f"{self.class_name}.json"
        return students_file

    def load_student_data(self):
        """加载学生数据"""
        self._load_and_update_students()

    def update_ui(self):
        """更新UI显示"""
        # 使用异步函数获取文本
        title_text = get_any_position_value_async(
            "remaining_list", "title_with_class", "name"
        )
        count_text = get_any_position_value_async(
            "remaining_list", "count_label", "name"
        )
        group_count_text = get_any_position_value_async(
            "remaining_list", "group_count_label", "name"
        )

        # 更新标题和人数/组数
        self.title_label.setText(title_text.format(class_name=self.class_name))
        
        # 检查是否显示小组
        is_showing_groups = any(student.get("is_group", False) for student in self.students) if self.students else False
        
        if is_showing_groups:
            # 显示组数
            group_count = len(self.students)
            self.count_label.setText(group_count_text.format(count=group_count))
        else:
            # 显示人数
            self.count_label.setText(count_text.format(count=len(self.students)))

        # 清空现有卡片
        self.cards = []
        self._clear_grid_layout()

        # 创建学生卡片
        for student in self.students:
            card = self.create_student_card(student)
            if card is not None:
                self.cards.append(card)

        # 直接更新布局，不使用延迟
        self.update_layout()

    def update_layout(self):
        """更新布局"""
        if not self.grid_layout or not self.cards:
            return
        
        # 检查是否需要更新布局
        current_width = self.width()
        current_card_count = len(self.cards)
        
        # 如果布局正在更新中，或者宽度和卡片数量都没有变化，则跳过更新
        if (self._layout_update_in_progress or 
            (current_width == self._last_layout_width and 
             current_card_count == self._last_card_count)):
            logger.debug(f"跳过布局更新: 宽度={current_width}, 卡片数={current_card_count}")
            return
            
        # 设置布局更新标志
        self._layout_update_in_progress = True
        self._last_layout_width = current_width
        self._last_card_count = current_card_count
            
        try:
            # 清空现有布局
            self._clear_grid_layout()
            
            # 计算列数
            def calculate_columns(width):
                """根据窗口宽度和卡片尺寸动态计算列数"""
                if width <= 0:
                    return 1

                # 计算可用宽度（减去左右边距）
                available_width = width - 40  # 左右各20px边距

                # 所有卡片使用相同的尺寸
                card_actual_width = STUDENT_CARD_MIN_WIDTH + STUDENT_CARD_SPACING
                max_cols = STUDENT_MAX_COLUMNS

                # 计算最大可能列数（不超过最大列数限制）
                cols = min(int(available_width // card_actual_width), max_cols)

                # 至少显示1列
                return max(cols, 1)
            
            # 获取当前窗口宽度
            window_width = max(self.width(), self.sizeHint().width())
            columns = calculate_columns(window_width)
            
            # 添加卡片到网格布局
            for i, card in enumerate(self.cards):
                row = i // columns
                col = i % columns
                self.grid_layout.addWidget(card, row, col)
                # 确保卡片可见
                card.show()
            
            # 设置列的伸缩因子，使卡片均匀分布
            for col in range(columns):
                self.grid_layout.setColumnStretch(col, 1)
                
            logger.debug(f"布局更新完成: 宽度={window_width}, 列数={columns}, 卡片数={len(self.cards)}")
        finally:
            # 清除布局更新标志
            self._layout_update_in_progress = False

    def _clear_grid_layout(self):
        """清空网格布局"""
        # 重置列伸缩因子
        for col in range(self.grid_layout.columnCount()):
            self.grid_layout.setColumnStretch(col, 0)

        while self.grid_layout.count():
            item = self.grid_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.hide()
                widget.setParent(None)

    def create_student_card(self, student: Dict[str, Any]) -> CardWidget:
        """创建学生卡片

        Args:
            student: 学生信息字典

        Returns:
            学生卡片
        """
        # 检查是否是小组卡片
        is_group = student.get("is_group", False)
        
        card = CardWidget()
        
        # 设置卡片属性，标记是否是小组卡片
        card.setProperty("is_group", is_group)
        
        if is_group:
            # 小组卡片使用与学生卡片相同的宽度，但高度自适应
            card.setMinimumSize(STUDENT_CARD_FIXED_WIDTH, 0)
            card.setMaximumSize(STUDENT_CARD_FIXED_WIDTH, 500)
            layout = QVBoxLayout(card)
            layout.setContentsMargins(STUDENT_CARD_MARGIN, STUDENT_CARD_MARGIN, 
                                      STUDENT_CARD_MARGIN, STUDENT_CARD_MARGIN)
            layout.setSpacing(8)

            # 小组名称
            name_label = BodyLabel(student["name"])
            name_label.setFont(QFont(load_custom_font(), 16, QFont.Weight.Bold))
            name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            name_label.setWordWrap(True)  # 启用自动换行
            layout.addWidget(name_label)

            # 小组成员数量
            members = student.get("members", [])
            count_label = BodyLabel(f"成员数量: {len(members)}")
            count_label.setFont(QFont(load_custom_font(), 10))
            count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            layout.addWidget(count_label)

            # 小组成员列表
            members_names = [member['name'] for member in members[:5]]  # 最多显示5个成员
            members_text = "、".join(members_names)
            if len(members) > 5:
                members_text += f" 等{len(members)-5}名成员"
            
            members_label = BodyLabel(members_text)
            members_label.setFont(QFont(load_custom_font(), 9))
            members_label.setAlignment(Qt.AlignmentFlag.AlignLeft)
            members_label.setWordWrap(True)  # 启用自动换行
            layout.addWidget(members_label)
        else:
            # 普通学生卡片
            card.setFixedSize(STUDENT_CARD_FIXED_WIDTH, STUDENT_CARD_FIXED_HEIGHT)

            layout = QVBoxLayout(card)
            layout.setContentsMargins(STUDENT_CARD_MARGIN, STUDENT_CARD_MARGIN, 
                                      STUDENT_CARD_MARGIN, STUDENT_CARD_MARGIN)
            layout.setSpacing(5)

            # 使用异步函数获取学生信息格式文本
            student_info_text = get_any_position_value_async(
                "remaining_list", "student_info", "name"
            )

            # 学生姓名
            name_label = BodyLabel(student["name"])
            name_label.setFont(QFont(load_custom_font(), 14))
            name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            layout.addWidget(name_label)

            # 学生信息
            info_text = student_info_text.format(
                id=student["id"], gender=student["gender"], group=student["group"]
            )
            info_label = BodyLabel(info_text)
            info_label.setFont(QFont(load_custom_font(), 9))
            info_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            layout.addWidget(info_label)

        return card

    def _load_and_update_students(self, count=None):
        """加载学生数据并更新UI的通用方法

        Args:
            count: 剩余人数，用于特殊处理当count为0时显示所有学生
        """
        # 设置更新标志，防止递归
        self._updating = True

        try:
            # 获取学生数据
            students_file = self.get_students_file()
            with open(students_file, "r", encoding="utf-8") as f:
                data = json.load(f)

            # 获取索引
            group_index = getattr(self, "group_index", 0)
            gender_index = getattr(self, "gender_index", 0)

            # 转换为字典格式
            students_dict_list = []
            for name, student_data in data.items():
                student_dict = {
                    "id": student_data.get("id", ""),
                    "name": name,
                    "gender": student_data.get("gender", ""),
                    "group": student_data.get("group", ""),
                    "exist": student_data.get("exist", True),
                }
                students_dict_list.append(student_dict)

            # 根据小组和性别筛选
            filtered_students = students_dict_list
            
            # 小组筛选
            if group_index > 0:
                # 获取所有可用小组
                groups = set()
                for student in students_dict_list:
                    if "group" in student and student["group"]:
                        groups.add(student["group"])
                
                # 排序小组列表
                sorted_groups = sorted(list(groups))
                
                # 处理"抽取全部小组"的情况 (group_index == 1)
                if group_index == 1:
                    # 创建小组数据结构，每个小组包含组名和成员列表
                    group_data = {}
                    for student in students_dict_list:
                        group_name = student.get("group", "")
                        if group_name:  # 只处理有组名的小组
                            if group_name not in group_data:
                                group_data[group_name] = []
                            group_data[group_name].append(student)
                    
                    # 对每个小组内的成员按姓名排序
                    for group_name in group_data:
                        group_data[group_name] = sorted(group_data[group_name], key=lambda x: x.get("name", ""))
                    
                    # 创建一个特殊的学生列表，用于显示小组信息
                    filtered_students = []
                    for group_name in sorted(group_data.keys()):
                        # 添加一个表示小组的特殊条目
                        group_info = {
                            "id": f"GROUP_{group_name}",
                            "name": f"小组 {group_name}",
                            "gender": "",
                            "group": group_name,
                            "exist": True,
                            "is_group": True,  # 标记这是一个小组
                            "members": group_data[group_name]  # 保存小组成员列表
                        }
                        filtered_students.append(group_info)
                elif group_index > 1 and sorted_groups:
                    # 选择特定小组 (索引从2开始，因为0是全部，1是全部小组)
                    group_index_adjusted = group_index - 2
                    if 0 <= group_index_adjusted < len(sorted_groups):
                        selected_group = sorted_groups[group_index_adjusted]
                        filtered_students = [
                            student for student in students_dict_list
                            if "group" in student and student["group"] == selected_group
                        ]
            
            # 根据性别筛选
            if gender_index > 0:  # 0表示全部性别
                # 获取所有可用的性别
                genders = set()
                for student in filtered_students:
                    if student["gender"]:
                        genders.add(student["gender"])
                
                # 将性别转换为排序后的列表
                sorted_genders = sorted(list(genders))
                
                # 根据索引获取选择的性别
                if gender_index <= len(sorted_genders):
                    selected_gender = sorted_genders[gender_index - 1]
                    filtered_students = [
                        student for student in filtered_students 
                        if student["gender"] == selected_gender
                    ]

            # 根据half_repeat设置获取未抽取的学生
            if self.half_repeat > 0:
                # 读取已抽取记录
                drawn_records = read_drawn_record(self.class_name, self.gender_filter, self.group_filter)
                drawn_counts = {name: count for name, count in drawn_records}

                # 过滤掉已抽取次数达到或超过设置值的学生
                remaining_students = []
                
                # 特殊处理小组模式 (group_index == 1)
                if group_index == 1:
                    # 对于小组模式，需要检查每个小组是否还有未被完全抽取的成员
                    for student in filtered_students:
                        # 只处理小组条目
                        if student.get("is_group", False):
                            group_name = student["group"]
                            members = student.get("members", [])
                            
                            # 检查小组成员是否都已被抽取
                            all_members_drawn = True
                            for member in members:
                                member_name = member["name"]
                                # 如果有成员未被抽取或抽取次数小于设置值，则小组保留
                                if (
                                    member_name not in drawn_counts
                                    or drawn_counts[member_name] < self.half_repeat
                                ):
                                    all_members_drawn = False
                                    break
                            
                            # 只有当小组不是所有成员都被抽取时才保留
                            if not all_members_drawn:
                                remaining_students.append(student)
                            # 如果当前剩余人数等于零，则显示所有小组
                            elif count is not None and count == 0:
                                remaining_students.append(student)
                        else:
                            # 非小组条目，按原逻辑处理
                            student_name = student["name"]
                            if (
                                student_name not in drawn_counts
                                or drawn_counts[student_name] < self.half_repeat
                            ):
                                remaining_students.append(student)
                            elif count is not None and count == 0:
                                remaining_students.append(student)
                else:
                    # 非小组模式，按原逻辑处理
                    for student in filtered_students:
                        student_name = student["name"]
                        # 如果学生未被抽取过，或者抽取次数小于设置值，则保留该学生
                        if (
                            student_name not in drawn_counts
                            or drawn_counts[student_name] < self.half_repeat
                        ):
                            remaining_students.append(student)
                        # 如果当前剩余人数等于零，则显示全部学生
                        elif count is not None and count == 0:
                            remaining_students.append(student)
            else:
                # 如果half_repeat为0，则所有学生都显示
                remaining_students = filtered_students

            self.students = remaining_students

            # 使用QTimer延迟更新UI，避免在数据处理过程中直接更新UI
            QTimer.singleShot(10, self.update_ui)
        finally:
            # 清除更新标志
            self._updating = False

    def update_remaining_list(
        self,
        class_name: str,
        group_filter: str,
        gender_filter: str,
        half_repeat: int = 0,
        group_index: int = 0,
        gender_index: int = 0,
        emit_signal: bool = True,
    ):
        """更新剩余名单

        Args:
            class_name: 班级名称
            group_filter: 分组筛选条件
            gender_filter: 性别筛选条件
            half_repeat: 重复抽取次数
            group_index: 分组索引
            gender_index: 性别索引
            emit_signal: 是否发出信号
        """
        # 更新属性
        self.class_name = class_name
        self.group_filter = group_filter
        self.gender_filter = gender_filter
        self.half_repeat = half_repeat
        self.group_index = group_index
        self.gender_index = gender_index

        # 重置布局状态，强制更新
        self._last_layout_width = 0
        self._last_card_count = 0

        # 重新加载学生数据
        self.load_student_data()
        
        # 如果需要发出信号，则发出count_changed信号
        if emit_signal:
            # 计算剩余人数
            remaining_count = len(self.students) if hasattr(self, 'students') else 0
            self.count_changed.emit(remaining_count)

    def refresh(self):
        """刷新页面"""
        if self.class_name:
            # 重置布局状态，强制更新
            self._last_layout_width = 0
            self._last_card_count = 0
            self.load_student_data()
    
    def on_count_changed(self, count):
        """处理剩余人数变化的槽函数
        
        Args:
            count: 剩余人数
        """
        # 重新加载学生数据
        self._load_and_update_students(count=count)

    def resizeEvent(self, event):
        """窗口大小变化事件"""
        # 检查窗口大小是否真的改变了
        new_size = event.size()
        old_size = event.oldSize()
        
        # 如果窗口大小没有改变，不触发布局更新
        if new_size == old_size:
            return
            
        # 检查宽度是否发生了显著变化（至少变化5像素才触发布局更新）
        width_change = abs(new_size.width() - self._last_layout_width)
        if width_change < 5:
            return
            
        # 使用QTimer延迟布局更新，避免递归调用
        if self._resize_timer is not None:
            self._resize_timer.stop()
        self._resize_timer = QTimer()
        self._resize_timer.setSingleShot(True)
        self._resize_timer.timeout.connect(self._delayed_update_layout)
        self._resize_timer.start(100)  # 增加延迟时间，减少频繁更新
        super().resizeEvent(event)

    def _delayed_update_layout(self):
        """延迟更新布局"""
        try:
            if hasattr(self, "grid_layout") and self.grid_layout is not None:
                if self.isVisible():
                    # 检查是否需要更新布局
                    current_width = self.width()
                    current_card_count = len(self.cards)
                    
                    # 只有当宽度或卡片数量发生变化时才更新布局
                    if (current_width != self._last_layout_width or 
                        current_card_count != self._last_card_count):
                        self.update_layout()
                        logger.debug(f"延迟布局更新完成，当前卡片数量: {len(self.cards)}")
                    else:
                        logger.debug(f"跳过布局更新: 宽度={current_width}, 卡片数={current_card_count}")
        except RuntimeError as e:
            logger.error(f"延迟布局更新错误: {e}")

    def closeEvent(self, event):
        """窗口关闭事件"""
        # 清理定时器
        if self._resize_timer is not None:
            self._resize_timer.stop()
            self._resize_timer = None

        super().closeEvent(event)