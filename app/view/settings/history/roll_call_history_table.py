# ==================================================
# 导入库
# ==================================================
import json

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
from app.common.history import *
from app.common.history.history_reader import (
    get_roll_call_student_list,
    get_roll_call_history_data,
    filter_roll_call_history_by_subject,
    get_roll_call_students_data,
    get_roll_call_session_data,
    get_roll_call_student_stats_data,
    check_class_has_gender_or_group,
)


# ==================================================
# 点名历史记录表格
# ==================================================
class roll_call_history_table(GroupHeaderCardWidget):
    """点名历史记录表格卡片"""

    refresh_signal = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent = parent
        self.setTitle(get_content_name_async("roll_call_history_table", "title"))
        self.setBorderRadius(8)

        # 初始化数据加载器
        class_history = get_all_history_names("roll_call")
        self.data_loader = None
        self.current_class_name = class_history[0] if class_history else ""
        self.current_mode = 0
        self.current_subject = ""  # 当前选择的课程
        self.batch_size = 30  # 每次加载的行数
        self.current_row = 0  # 当前加载到的行数
        self.total_rows = 0  # 总行数
        self.is_loading = False  # 是否正在加载数据
        self.has_class_record = False  # 是否有课程记录
        self.available_subjects = []  # 可用的课程列表
        self.cached_students_data = []  # 缓存的学生数据列表
        self.cached_sessions_data = []  # 缓存的会话数据列表
        self.cached_stats_data = []  # 缓存的统计数据列表
        self.force_load_all = False

        # 创建班级选择区域
        QTimer.singleShot(APPLY_DELAY, self.create_class_selection)

        # 创建表格区域
        QTimer.singleShot(APPLY_DELAY, self.create_table)

        # 初始化班级列表
        QTimer.singleShot(APPLY_DELAY, self.refresh_class_history)

        # 设置文件系统监视器
        QTimer.singleShot(APPLY_DELAY, self.setup_file_watcher)

        # 初始化数据
        QTimer.singleShot(APPLY_DELAY, self.refresh_data)

        # 连接信号
        self.refresh_signal.connect(self.refresh_data)

    def create_class_selection(self):
        """创建班级选择区域"""
        self.class_comboBox = ComboBox()

        # 获取班级历史列表并填充下拉框
        class_history = get_all_history_names("roll_call")
        self.class_comboBox.addItems(class_history)

        # 设置默认选择
        if class_history:
            saved_index = readme_settings_async(
                "roll_call_history_table", "select_class_name"
            )
            self.class_comboBox.setCurrentIndex(0)
            self.current_class_name = class_history[0]
        else:
            # 如果没有班级历史，设置占位符
            self.class_comboBox.setCurrentIndex(-1)
            self.class_comboBox.setPlaceholderText(
                get_content_name_async("roll_call_history_table", "select_class_name")
            )
            self.current_class_name = ""

        self.class_comboBox.currentIndexChanged.connect(self.on_class_changed)
        self.class_comboBox.currentTextChanged.connect(
            lambda: self.on_class_changed(-1)
        )

        # 选择查看模式
        self.all_names = get_all_names("roll_call", self.class_comboBox.currentText())
        self.mode_comboBox = ComboBox()
        self.mode_comboBox.addItems(
            get_content_combo_name_async("roll_call_history_table", "select_mode")
            + self.all_names
        )
        self.mode_comboBox.setCurrentIndex(0)
        self.mode_comboBox.currentIndexChanged.connect(self.refresh_data)

        # 选择课程
        self.subject_comboBox = ComboBox()
        self.subject_comboBox.addItems(
            get_content_combo_name_async("roll_call_history_table", "select_subject")
        )
        self.subject_comboBox.setCurrentIndex(0)
        self.subject_comboBox.currentIndexChanged.connect(self.on_subject_changed)

        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("roll_call_history_table", "select_class_name"),
            get_content_description_async(
                "roll_call_history_table", "select_class_name"
            ),
            self.class_comboBox,
        )

        # 创建一个容器来放置查看模式和课程选择下拉框
        self.mode_subject_widget = QWidget()
        mode_subject_layout = QHBoxLayout(self.mode_subject_widget)
        mode_subject_layout.setContentsMargins(0, 0, 0, 0)
        mode_subject_layout.setSpacing(10)
        mode_subject_layout.addWidget(self.mode_comboBox)
        mode_subject_layout.addWidget(self.subject_comboBox)

        self.addGroup(
            get_theme_icon("ic_fluent_reading_mode_mobile_20_filled"),
            get_content_name_async("roll_call_history_table", "select_mode"),
            get_content_description_async("roll_call_history_table", "select_mode"),
            self.mode_subject_widget,
        )

    def create_table(self):
        """创建表格区域"""
        # 创建表格
        self.table = TableWidget()
        self.table.setBorderVisible(True)
        self.table.setBorderRadius(8)
        self.table.setWordWrap(False)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        # 暂时禁用排序，在数据加载完成后再启用
        self.table.setSortingEnabled(False)
        self.table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.verticalHeader().hide()

        # 初始化排序状态
        self.sort_column = -1
        self.sort_order = Qt.SortOrder.AscendingOrder

        # 根据当前选择的模式设置表格头
        self.update_table_headers()

        # 设置表格属性
        for i in range(self.table.columnCount()):
            self.table.horizontalHeader().setSectionResizeMode(
                i, QHeaderView.ResizeMode.Stretch
            )
            self.table.horizontalHeader().setDefaultAlignment(
                Qt.AlignmentFlag.AlignCenter
            )
            self.table.horizontalHeader().setSectionsClickable(True)

        # 初始状态下不显示排序指示器
        self.table.horizontalHeader().setSortIndicatorShown(False)

        # 连接滚动事件，用于分段加载
        self.table.verticalScrollBar().valueChanged.connect(self._on_scroll)

        # 连接排序信号，在排序时重新加载数据
        self.table.horizontalHeader().sectionClicked.connect(self._on_header_clicked)

        self.layout().addWidget(self.table)

    def _on_scroll(self, value):
        """处理表格滚动事件，实现分段加载

        Args:
            value: 滚动条当前位置
        """
        # 如果正在加载或没有更多数据，直接返回
        if self.is_loading or self.current_row >= self.total_rows:
            return

        # 获取滚动条最大值和当前值
        max_value = self.table.verticalScrollBar().maximum()
        current_value = self.table.verticalScrollBar().value()

        # 使用更精确的滚动检测，确保在滚动到底部时触发
        scroll_threshold = max(20, max_value * 0.1)  # 至少20像素或10%的位置
        if current_value >= max_value - scroll_threshold:
            self._load_more_data()

    def _ensure_scrollable_rows(self):
        if not hasattr(self, "table"):
            return
        if self.current_row >= self.total_rows:
            return
        max_value = self.table.verticalScrollBar().maximum()
        attempts = 0
        while self.current_row < self.total_rows and max_value == 0 and attempts < 20:
            self._load_more_data()
            max_value = self.table.verticalScrollBar().maximum()
            attempts += 1

    def _on_header_clicked(self, column):
        """处理表头点击事件，实现排序

        Args:
            column: 被点击的列索引
        """
        # 如果正在加载数据，不处理排序
        if self.is_loading:
            return

        # 获取当前排序状态，优先使用我们自己的状态变量
        current_sort_column = self.sort_column if self.sort_column >= 0 else -1
        current_sort_order = (
            self.sort_order if self.sort_column >= 0 else Qt.SortOrder.AscendingOrder
        )

        # 如果点击的是同一列，则切换排序顺序；否则设置为升序
        if column == current_sort_column:
            # 切换排序顺序
            if current_sort_order == Qt.SortOrder.AscendingOrder:
                new_sort_order = Qt.SortOrder.DescendingOrder
            else:
                new_sort_order = Qt.SortOrder.AscendingOrder
        else:
            # 点击不同列，设置为升序
            new_sort_order = Qt.SortOrder.AscendingOrder

        # 更新排序状态
        self.sort_column = column
        self.sort_order = new_sort_order

        # 设置排序指示器
        self.table.horizontalHeader().setSortIndicator(column, new_sort_order)
        self.table.horizontalHeader().setSortIndicatorShown(True)

        self.force_load_all = True

        # 重置数据加载状态
        self.current_row = 0
        self.table.setRowCount(0)

        # 清空缓存，确保排序时重新获取数据
        self.cached_students_data = []
        self.cached_sessions_data = []
        self.cached_stats_data = []

        # 重新加载数据
        self.refresh_data()

    def _sort_current_data(self):
        """对已加载的数据进行排序，不重新加载数据"""
        # 获取当前表格中的所有数据
        table_data = []
        for row in range(self.table.rowCount()):
            row_data = []
            for col in range(self.table.columnCount()):
                item = self.table.item(row, col)
                if item:
                    row_data.append(item.text())
                else:
                    row_data.append("")
            table_data.append(row_data)

        # 如果没有数据，直接返回
        if not table_data:
            return

        # 根据排序状态对数据进行排序
        def sort_key(row):
            # 尝试将数据转换为数字，如果失败则使用字符串比较
            try:
                # 对于权重列，需要特殊处理
                # 在模式0（学生数据）中：
                #   - 有课程时：权重列索引为6
                #   - 无课程时：权重列索引为5
                # 在模式1（会话数据）中：
                #   - 有课程时：权重列索引为6
                #   - 无课程时：权重列索引为5
                # 在模式2（统计数据）中：
                #   - 有课程时：权重列索引为6
                #   - 无课程时：权重列索引为5
                if self.current_mode == 0:
                    # 模式0：学生数据
                    if self.sort_column == 5:
                        # 列5可能是课程或权重
                        if self.has_class_record:
                            # 有课程，列5是课程，列6是权重
                            return row[self.sort_column]
                        else:
                            # 无课程，列5是权重
                            weight_str = row[self.sort_column]
                            weight_str = weight_str.lstrip("0")
                            if not weight_str:
                                return 0.0
                            return float(weight_str)
                    elif self.sort_column == 6 and self.has_class_record:
                        # 有课程时，列6是权重
                        weight_str = row[self.sort_column]
                        weight_str = weight_str.lstrip("0")
                        if not weight_str:
                            return 0.0
                        return float(weight_str)
                elif self.current_mode in [1, 2]:
                    # 模式1和2：会话数据和统计数据
                    if self.sort_column == 5:
                        # 有课程，列5是课程
                        return row[self.sort_column]
                    elif self.sort_column == 6 and self.has_class_record:
                        # 有课程时，列6是权重
                        weight_str = row[self.sort_column]
                        weight_str = weight_str.lstrip("0")
                        if not weight_str:
                            return 0.0
                        return float(weight_str)
                return float(row[self.sort_column])
            except (ValueError, IndexError):
                return row[self.sort_column]

        # 应用排序
        reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
        table_data.sort(key=sort_key, reverse=reverse_order)

        # 清空表格
        self.table.setRowCount(0)

        # 重新填充排序后的数据
        self.table.setRowCount(len(table_data))
        for row_idx, row_data in enumerate(table_data):
            for col_idx, cell_data in enumerate(row_data):
                item = create_table_item(cell_data)
                self.table.setItem(row_idx, col_idx, item)

    def _load_more_data(self):
        """加载更多数据"""
        if self.is_loading or self.current_row >= self.total_rows:
            return

        self.is_loading = True

        # 计算新的行数
        new_row_count = min(self.current_row + self.batch_size, self.total_rows)

        # 增加表格行数
        self.table.setRowCount(new_row_count)

        # 根据当前模式加载数据，与refresh_data方法保持一致
        if hasattr(self, "mode_comboBox"):
            self.current_mode = self.mode_comboBox.currentIndex()
        else:
            self.current_mode = 0

        if self.current_mode == 0:
            self._load_more_students_data()
        elif self.current_mode == 1:
            self._load_more_sessions_data()
        else:
            # 当模式值大于等于2时，表示选择了特定的学生姓名
            if hasattr(self, "mode_comboBox"):
                self.current_student_name = self.mode_comboBox.currentText()
            else:
                # 如果没有mode_comboBox，从设置中获取学生姓名
                self.current_student_name = readme_settings_async(
                    "roll_call_history_table", "select_student_name"
                )
            self._load_more_stats_data(self.current_student_name)

        # 数据加载完成后启用排序
        if self.current_row >= self.total_rows:
            self.table.setSortingEnabled(True)
            if self.sort_column >= 0:
                self.table.horizontalHeader().setSortIndicator(
                    self.sort_column, self.sort_order
                )
                self.table.horizontalHeader().setSortIndicatorShown(True)

        self.is_loading = False

    def _load_more_students_data(self):
        """加载更多学生数据"""
        if not self.current_class_name:
            return
        try:
            # 如果是第一次加载（current_row == 0），获取并排序数据
            if self.current_row == 0:
                cleaned_students = get_roll_call_student_list(self.current_class_name)
                history_data = get_roll_call_history_data(self.current_class_name)

                if self.current_subject:
                    history_data = filter_roll_call_history_by_subject(
                        history_data, self.current_subject
                    )

                students_data = get_roll_call_students_data(
                    cleaned_students, history_data, self.current_subject
                )

                students_weight_data = calculate_weight(
                    students_data, self.current_class_name, self.current_subject
                )

                format_weight, _, _ = format_weight_for_display(
                    students_weight_data, "next_weight"
                )

                if self.sort_column >= 0:

                    def sort_key(student):
                        if self.sort_column == 0:
                            return student.get("id", "")
                        elif self.sort_column == 1:
                            return student.get("name", "")
                        elif self.sort_column == 2:
                            return student.get("gender", "")
                        elif self.sort_column == 3:
                            return student.get("group", "")
                        elif self.sort_column == 4:
                            return student.get("total_count", 0)
                        elif self.sort_column == 5:
                            for weight_student in students_weight_data:
                                if weight_student.get("id") == student.get(
                                    "id"
                                ) and weight_student.get("name") == student.get("name"):
                                    return weight_student.get("next_weight", 1.0)
                            return 1.0
                        return ""

                    reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                    students_data.sort(key=sort_key, reverse=reverse_order)
                    sorted_weight_data = []
                    for student in students_data:
                        for weight_student in students_weight_data:
                            if weight_student.get("id") == student.get(
                                "id"
                            ) and weight_student.get("name") == student.get("name"):
                                sorted_weight_data.append(weight_student)
                                break
                    students_weight_data = sorted_weight_data

                # 缓存排序后的数据
                self.cached_students_data = students_data
                self.cached_students_weight_data = students_weight_data
                self.cached_format_weight = format_weight
            else:
                # 使用缓存的数据
                students_data = self.cached_students_data
                students_weight_data = self.cached_students_weight_data
                format_weight = self.cached_format_weight

            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            has_gender, has_group = check_class_has_gender_or_group(
                self.current_class_name
            )

            for i in range(start_row, end_row):
                if i >= len(students_data):
                    break

                student = students_data[i]
                row = i
                col = 0

                id_item = create_table_item(student.get("id", str(row + 1)))
                self.table.setItem(row, col, id_item)
                col += 1

                name_item = create_table_item(student.get("name", ""))
                self.table.setItem(row, col, name_item)
                col += 1

                if has_gender:
                    gender_item = create_table_item(student.get("gender", ""))
                    self.table.setItem(row, col, gender_item)
                    col += 1

                if has_group:
                    group_item = create_table_item(student.get("group", ""))
                    self.table.setItem(row, col, group_item)
                    col += 1

                total_count_item = create_table_item(
                    str(student.get("total_count_str", student.get("total_count", 0)))
                )
                self.table.setItem(row, col, total_count_item)
                col += 1

                if self.table.columnCount() > col:
                    if i < len(students_weight_data):
                        weight_item = create_table_item(
                            str(
                                format_weight(
                                    students_weight_data[i].get("next_weight", "")
                                )
                            )
                        )
                        self.table.setItem(row, col, weight_item)

            self.current_row = end_row

        except Exception as e:
            logger.exception(f"加载学生数据失败: {e}")

    def _load_more_sessions_data(self):
        """加载更多会话数据"""
        if not self.current_class_name:
            return
        try:
            # 如果是第一次加载（current_row == 0），获取并排序数据
            if self.current_row == 0:
                cleaned_students = get_roll_call_student_list(self.current_class_name)
                history_data = get_roll_call_history_data(self.current_class_name)

                students_data = get_roll_call_session_data(
                    cleaned_students, history_data, self.current_subject
                )

                self.has_class_record = any(
                    student.get("class_name", "") for student in students_data
                )

                self.update_table_headers()

                format_weight, _, _ = format_weight_for_display(students_data, "weight")

                if self.sort_column >= 0:

                    def sort_key(student):
                        if self.sort_column == 0:
                            return student.get("draw_time", "")
                        elif self.sort_column == 1:
                            return student.get("id", "")
                        elif self.sort_column == 2:
                            return student.get("name", "")
                        elif self.sort_column == 3:
                            return student.get("gender", "")
                        elif self.sort_column == 4:
                            return student.get("group", "")
                        elif self.sort_column == 5:
                            return student.get("class_name", "")
                        elif self.sort_column == 6:
                            return student.get("weight", "")
                        return ""

                    reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                    students_data.sort(key=sort_key, reverse=reverse_order)
                else:
                    students_data.sort(
                        key=lambda x: x.get("draw_time", ""), reverse=True
                    )

                # 缓存排序后的数据
                self.cached_sessions_data = students_data
                self.cached_sessions_format_weight = format_weight
            else:
                # 使用缓存的数据
                students_data = self.cached_sessions_data
                format_weight = self.cached_sessions_format_weight

            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            has_gender, has_group = check_class_has_gender_or_group(
                self.current_class_name
            )

            for i in range(start_row, end_row):
                if i >= len(students_data):
                    break

                student = students_data[i]
                row = i
                col = 0

                draw_time_item = create_table_item(student.get("draw_time", ""))
                self.table.setItem(row, col, draw_time_item)
                col += 1

                id_item = create_table_item(student.get("id", str(row + 1)))
                self.table.setItem(row, col, id_item)
                col += 1

                name_item = create_table_item(student.get("name", ""))
                self.table.setItem(row, col, name_item)
                col += 1

                if has_gender:
                    gender = student.get("gender", "")
                    gender_item = create_table_item(str(gender) if gender else "")
                    self.table.setItem(row, col, gender_item)
                    col += 1

                if has_group:
                    group = student.get("group", "")
                    group_item = create_table_item(str(group) if group else "")
                    self.table.setItem(row, col, group_item)
                    col += 1

                if self.has_class_record:
                    class_name = student.get("class_name", "")
                    class_item = create_table_item(
                        str(class_name) if class_name else ""
                    )
                    self.table.setItem(row, col, class_item)
                    col += 1

                if self.table.columnCount() > col:
                    weight_item = create_table_item(
                        str(format_weight(student.get("weight", "")))
                    )
                    self.table.setItem(row, col, weight_item)

            self.current_row = end_row

        except Exception as e:
            logger.exception(f"加载会话数据失败: {e}")

    def _load_more_stats_data(self, student_name):
        """加载更多统计数据"""
        if not self.current_class_name:
            return
        try:
            # 如果是第一次加载（current_row == 0），获取并排序数据
            if self.current_row == 0:
                cleaned_students = get_roll_call_student_list(self.current_class_name)
                history_data = get_roll_call_history_data(self.current_class_name)

                students_data = get_roll_call_student_stats_data(
                    cleaned_students, history_data, student_name, self.current_subject
                )

                self.has_class_record = any(
                    student.get("class_name", "") for student in students_data
                )

                self.update_table_headers()

                format_weight, _, _ = format_weight_for_display(students_data, "weight")

                if self.sort_column >= 0:

                    def sort_key(student):
                        if self.sort_column == 0:
                            return student.get("draw_time", "")
                        elif self.sort_column == 1:
                            return str(student.get("draw_method", ""))
                        elif self.sort_column == 2:
                            return int(student.get("draw_people_numbers", 0))
                        elif self.sort_column == 3:
                            return str(student.get("draw_gender", ""))
                        elif self.sort_column == 4:
                            return str(student.get("draw_group", ""))
                        elif self.sort_column == 5:
                            return str(student.get("class_name", ""))
                        elif self.sort_column == 6:
                            return float(student.get("weight", ""))
                        return ""

                    reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                    students_data.sort(key=sort_key, reverse=reverse_order)
                else:
                    students_data.sort(
                        key=lambda x: x.get("draw_time", ""), reverse=True
                    )

                # 缓存排序后的数据
                self.cached_stats_data = students_data
                self.cached_stats_format_weight = format_weight
            else:
                # 使用缓存的数据
                students_data = self.cached_stats_data
                format_weight = self.cached_stats_format_weight

            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            has_gender, has_group = check_class_has_gender_or_group(
                self.current_class_name
            )

            for i in range(start_row, end_row):
                if i >= len(students_data):
                    break

                student = students_data[i]
                row = i
                col = 0

                time_item = create_table_item(student.get("draw_time", ""))
                self.table.setItem(row, col, time_item)
                col += 1

                draw_method = student.get("draw_method", "")
                if draw_method == "0":
                    mode_text = get_content_name_async(
                        "roll_call_history_table", "draw_method_random"
                    )
                elif draw_method == "1":
                    mode_text = get_content_name_async(
                        "roll_call_history_table", "draw_method_weight"
                    )
                else:
                    mode_text = str(draw_method)
                mode_item = create_table_item(mode_text)
                self.table.setItem(row, col, mode_item)
                col += 1

                draw_people_numbers_item = create_table_item(
                    str(student.get("draw_people_numbers", 0))
                )
                self.table.setItem(row, col, draw_people_numbers_item)
                col += 1

                if has_gender:
                    draw_gender = student.get("draw_gender", "")
                    gender_item = create_table_item(draw_gender if draw_gender else "")
                    self.table.setItem(row, col, gender_item)
                    col += 1

                if has_group:
                    draw_group = student.get("draw_group", "")
                    group_item = create_table_item(draw_group if draw_group else "")
                    self.table.setItem(row, col, group_item)
                    col += 1

                if self.has_class_record:
                    class_name = student.get("class_name", "")
                    class_item = create_table_item(
                        str(class_name) if class_name else ""
                    )
                    self.table.setItem(row, col, class_item)
                    col += 1

                if self.table.columnCount() > col:
                    weight_item = create_table_item(
                        str(format_weight(student.get("weight", 0)))
                    )
                    self.table.setItem(row, col, weight_item)

            self.current_row = end_row

        except Exception as e:
            logger.exception(f"加载统计数据失败: {e}")

    def setup_file_watcher(self):
        """设置文件系统监视器，监控班级历史记录文件夹的变化"""
        roll_call_history_dir = get_data_path("history/roll_call_history")
        if not roll_call_history_dir.exists():
            logger.warning(f"班级历史记录文件夹不存在: {roll_call_history_dir}")
            return
        self.file_watcher = QFileSystemWatcher()
        self.file_watcher.addPath(str(roll_call_history_dir))
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)
        # logger.debug(f"已设置文件监视器，监控目录: {roll_call_history_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        QTimer.singleShot(1000, self.refresh_class_history)

    def refresh_class_history(self):
        """刷新班级下拉框列表"""
        if not hasattr(self, "class_comboBox"):
            return

        # 保存当前选择的班级名称和索引
        current_class_name = self.class_comboBox.currentText()
        current_index = self.class_comboBox.currentIndex()

        # 获取最新的班级历史列表
        class_history = get_all_history_names("roll_call")

        # 清空并重新填充下拉框
        self.class_comboBox.clear()
        self.class_comboBox.addItems(class_history)

        # 如果之前选择的班级还在列表中，则重新选择它
        if current_class_name and current_class_name in class_history:
            index = class_history.index(current_class_name)
            self.class_comboBox.setCurrentIndex(index)
            # 更新current_class_name
            self.current_class_name = current_class_name
        elif (
            class_history and current_index >= 0 and current_index < len(class_history)
        ):
            # 如果之前选择的索引仍然有效，使用相同的索引
            self.class_comboBox.setCurrentIndex(current_index)
            # 更新current_class_name
            self.current_class_name = class_history[current_index]
        elif class_history:
            # 如果之前选择的班级不在列表中，选择第一个班级
            self.class_comboBox.setCurrentIndex(0)
            # 更新current_class_name
            self.current_class_name = class_history[0]
        else:
            # 如果没有班级历史，设置占位符
            self.class_comboBox.setCurrentIndex(-1)
            self.class_comboBox.setPlaceholderText(
                get_content_name_async("roll_call_history_table", "select_class_name")
            )
            # 更新current_class_name
            self.current_class_name = ""

        if hasattr(self, "clear_button"):
            self.clear_button.setEnabled(bool(self.current_class_name))

    def on_class_changed(self, index):
        """班级选择变化时刷新表格数据"""
        if not hasattr(self, "class_comboBox"):
            return

        # 启用或禁用清除按钮
        if hasattr(self, "clear_button"):
            self.clear_button.setEnabled(self.class_comboBox.currentIndex() >= 0)

        # 更新当前班级名称
        self.current_class_name = self.class_comboBox.currentText()

        # 更新课程列表
        self._update_subject_list()

        # 刷新表格数据
        self.refresh_data()

    def on_subject_changed(self, index):
        """课程选择变化时刷新表格数据"""
        if not hasattr(self, "subject_comboBox"):
            return

        # 获取选择的课程
        if index == 0:
            self.current_subject = ""
        else:
            self.current_subject = self.subject_comboBox.currentText()

        # 刷新表格数据
        self.refresh_data()

    def _update_subject_list(self):
        """更新课程列表"""
        if not self.current_class_name:
            return

        try:
            history_file = get_data_path(
                "history/roll_call_history", f"{self.current_class_name}.json"
            )

            if not file_exists(history_file):
                self.available_subjects = []
                return

            with open_file(history_file, "r", encoding="utf-8") as f:
                history_data = json.load(f)

            # 收集所有课程名称
            subjects = set()
            students = history_data.get("students", {})
            for student_info in students.values():
                history = student_info.get("history", [])
                for record in history:
                    class_name = record.get("class_name", "")
                    if class_name:
                        subjects.add(class_name)

            self.available_subjects = sorted(list(subjects))

            # 更新课程下拉框
            if hasattr(self, "subject_comboBox"):
                # 保存当前选择的课程
                current_subject = self.current_subject
                current_index = self.subject_comboBox.currentIndex()

                self.subject_comboBox.blockSignals(True)
                self.subject_comboBox.clear()
                self.subject_comboBox.addItems(
                    get_content_combo_name_async(
                        "roll_call_history_table", "select_subject"
                    )
                    + self.available_subjects
                )

                # 恢复之前选择的课程
                if current_subject:
                    # 尝试找到之前选择的课程
                    items = self.subject_comboBox.count()
                    for i in range(items):
                        if self.subject_comboBox.itemText(i) == current_subject:
                            self.subject_comboBox.setCurrentIndex(i)
                            break
                else:
                    self.subject_comboBox.setCurrentIndex(0)

                self.subject_comboBox.blockSignals(False)

                # 根据是否有课程记录显示或隐藏课程选择框
                if not self.available_subjects:
                    self.subject_comboBox.hide()
                else:
                    self.subject_comboBox.show()

        except Exception as e:
            logger.exception(f"更新课程列表失败: {e}")
            self.available_subjects = []

    def refresh_data(self):
        """刷新表格数据"""
        if not hasattr(self, "table"):
            return
        if not hasattr(self, "class_comboBox"):
            return
        class_name = self.class_comboBox.currentText()
        if not class_name:
            self.table.setRowCount(0)
            return
        self.current_class_name = class_name

        # 更新课程列表
        self._update_subject_list()

        # 重置课程记录标志
        self.has_class_record = False

        self.update_table_headers()

        # 重置数据加载状态
        self.current_row = 0
        self.is_loading = False
        self.table.setRowCount(0)
        self.table.setSortingEnabled(False)
        self.table.blockSignals(True)

        try:
            if not hasattr(self, "mode_comboBox"):
                self.current_mode = 0
                if self.current_mode == 0:
                    # 获取学生记录数量
                    students_count = get_name_history("roll_call", class_name)
                    if students_count:
                        self.total_rows = students_count
                        # 设置初始行数为批次大小或总行数，取较小值
                        initial_rows = (
                            self.total_rows
                            if self.force_load_all
                            else min(self.batch_size, self.total_rows)
                        )
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_students_data()
                elif self.current_mode == 1:
                    # 获取会话记录数量
                    sessions_count = get_draw_sessions_history("roll_call", class_name)
                    if sessions_count:
                        self.total_rows = sessions_count
                        # 设置初始行数为批次大小或总行数，取较小值
                        initial_rows = (
                            self.total_rows
                            if self.force_load_all
                            else min(self.batch_size, self.total_rows)
                        )
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_sessions_data()
                    self._ensure_scrollable_rows()
                else:
                    # 当模式值大于等于2时，从设置中获取学生姓名
                    self.current_student_name = readme_settings_async(
                        "roll_call_history_table", "select_student_name"
                    )
                    # 获取个人统计记录数量
                    stats_count = get_individual_statistics(
                        "roll_call", class_name, self.current_student_name
                    )
                    if stats_count:
                        self.total_rows = stats_count
                        # 设置初始行数为批次大小或总行数，取较小值
                        initial_rows = (
                            self.total_rows
                            if self.force_load_all
                            else min(self.batch_size, self.total_rows)
                        )
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_stats_data(self.current_student_name)
                    self._ensure_scrollable_rows()
                return

            self.current_mode = self.mode_comboBox.currentIndex()
            if self.current_mode == 0:
                # 获取学生记录数量
                students_count = get_name_history("roll_call", class_name)
                if students_count:
                    self.total_rows = students_count
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = (
                        self.total_rows
                        if self.force_load_all
                        else min(self.batch_size, self.total_rows)
                    )
                    self.table.setRowCount(initial_rows)
                    # 加载第一批数据
                    self._load_more_students_data()
                    self._ensure_scrollable_rows()
            elif self.current_mode == 1:
                # 获取会话记录数量
                sessions_count = get_draw_sessions_history("roll_call", class_name)
                if sessions_count:
                    self.total_rows = sessions_count
                    self.total_rows = sessions_count
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = (
                        self.total_rows
                        if self.force_load_all
                        else min(self.batch_size, self.total_rows)
                    )
                    self.table.setRowCount(initial_rows)
                    # 加载第一批数据
                    self._load_more_sessions_data()
                    self._ensure_scrollable_rows()
            else:
                stats = get_individual_statistics(
                    "roll_call", class_name, self.mode_comboBox.currentText()
                )
                if stats:
                    self.total_rows = stats
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = (
                        self.total_rows
                        if self.force_load_all
                        else min(self.batch_size, self.total_rows)
                    )
                    self.table.setRowCount(initial_rows)
                    self.current_student_name = self.mode_comboBox.currentText()
                    # 加载第一批数据
                    self._load_more_stats_data(self.current_student_name)
                    self._ensure_scrollable_rows()

            # 设置表格列属性
            for i in range(self.table.columnCount()):
                self.table.horizontalHeader().setSectionResizeMode(
                    i, QHeaderView.ResizeMode.Stretch
                )
                self.table.horizontalHeader().setDefaultAlignment(
                    Qt.AlignmentFlag.AlignCenter
                )

            # 如果有排序设置，应用排序
            if self.sort_column >= 0:
                self.table.horizontalHeader().setSortIndicator(
                    self.sort_column, self.sort_order
                )
                self.table.horizontalHeader().setSortIndicatorShown(True)

        except Exception as e:
            logger.exception(f"刷新表格数据失败: {str(e)}")
        finally:
            self.table.blockSignals(False)
            self.force_load_all = False

    def update_table_headers(self):
        """更新表格标题"""
        if not hasattr(self, "table"):
            return

        if hasattr(self, "mode_comboBox"):
            self.current_mode = self.mode_comboBox.currentIndex()
        else:
            self.current_mode = 0

        if self.current_mode == 0:
            headers = get_content_name_async(
                "roll_call_history_table", "HeaderLabels_all_weight"
            )
        elif self.current_mode == 1:
            headers = get_content_name_async(
                "roll_call_history_table", "HeaderLabels_time_weight"
            )
        else:
            headers = get_content_name_async(
                "roll_call_history_table", "HeaderLabels_Individual_weight"
            )

        if isinstance(headers, dict):
            headers = list(headers.values())

        has_gender, has_group = False, False
        if self.current_class_name:
            has_gender, has_group = check_class_has_gender_or_group(
                self.current_class_name
            )

        if self.current_mode == 0:
            if not has_gender and not has_group:
                headers = headers[:2] + headers[4:]
            elif not has_gender:
                headers = headers[:2] + headers[3:]
            elif not has_group:
                headers = headers[:3] + headers[4:]
        elif self.current_mode == 1:
            if not has_gender and not has_group:
                headers = headers[:2] + headers[4:]
            elif not has_gender:
                headers = headers[:2] + headers[3:]
            elif not has_group:
                headers = headers[:3] + headers[4:]
            if not self.has_class_record:
                headers = headers[:-2] + headers[-1:]
        elif self.current_mode >= 2:
            if not has_gender and not has_group:
                headers = headers[:3] + headers[5:]
            elif not has_gender:
                headers = headers[:3] + headers[4:]
            elif not has_group:
                headers = headers[:4] + headers[5:]
            if not self.has_class_record:
                headers = headers[:-2] + headers[-1:]

        self.table.setColumnCount(len(headers))
        self.table.setHorizontalHeaderLabels(headers)
