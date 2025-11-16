# ==================================================
# 导入库
# ==================================================
import json

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
from app.tools.history import *
from app.tools.result_display import *
from app.tools.config import *

from app.page_building.another_window import *

from random import SystemRandom
system_random = SystemRandom()

# ==================================================
# 班级点名类
# ==================================================
class roll_call(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.file_watcher = QFileSystemWatcher()
        self.setup_file_watcher()
        
        # 长按功能相关变量
        self.press_timer = QTimer()
        self.press_timer.timeout.connect(self.handle_long_press)
        self.long_press_interval = 100  # 长按时连续触发的间隔时间(毫秒)
        self.long_press_delay = 500     # 开始长按前的延迟时间(毫秒)
        self.is_long_pressing = False   # 是否正在长按
        self.long_press_direction = 0   # 长按方向：1为增加，-1为减少
        
        self.initUI()

    def handle_long_press(self):
        """处理长按事件"""
        if self.is_long_pressing:
            # 更新定时器间隔为连续触发间隔
            self.press_timer.setInterval(self.long_press_interval)
            # 执行更新计数
            self.update_count(self.long_press_direction)
    
    def start_long_press(self, direction):
        """开始长按
        
        Args:
            direction (int): 长按方向，1为增加，-1为减少
        """
        self.long_press_direction = direction
        self.is_long_pressing = True
        # 设置初始延迟
        self.press_timer.setInterval(self.long_press_delay)
        self.press_timer.start()
    
    def stop_long_press(self):
        """停止长按"""
        self.is_long_pressing = False
        self.press_timer.stop()

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
            logger.error(f"清理文件监控器失败: {e}")
        super().closeEvent(event)

    def initUI(self):
        """初始化UI"""
        container = QWidget()
        roll_call_container = QVBoxLayout(container)
        roll_call_container.setContentsMargins(0, 0, 0, 0)

        self.result_widget = QWidget()
        self.result_layout = QVBoxLayout(self.result_widget)
        self.result_grid = QGridLayout()
        self.result_layout.addStretch()
        self.result_layout.addLayout(self.result_grid)
        self.result_layout.addStretch()
        roll_call_container.addWidget(self.result_widget)

        self.reset_button = PushButton(
            get_content_pushbutton_name_async("roll_call", "reset_button")
        )
        self.reset_button.setFont(QFont(load_custom_font(), 15))
        self.reset_button.setFixedSize(165, 45)
        self.reset_button.clicked.connect(lambda: self.reset_count())

        self.minus_button = PushButton("-")
        self.minus_button.setFont(QFont(load_custom_font(), 20))
        self.minus_button.setFixedSize(45, 45)
        self.minus_button.clicked.connect(lambda: self.update_count(-1))
        
        # 添加长按连续减功能
        self.minus_button.pressed.connect(lambda: self.start_long_press(-1))
        self.minus_button.released.connect(self.stop_long_press)

        self.count_label = BodyLabel("1")
        self.count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.count_label.setFont(QFont(load_custom_font(), 20))
        self.count_label.setFixedSize(65, 45)
        self.current_count = 1

        self.plus_button = PushButton("+")
        self.plus_button.setFont(QFont(load_custom_font(), 20))
        self.plus_button.setFixedSize(45, 45)
        self.plus_button.clicked.connect(lambda: self.update_count(1))
        
        # 添加长按连续加功能
        self.plus_button.pressed.connect(lambda: self.start_long_press(1))
        self.plus_button.released.connect(self.stop_long_press)

        self.minus_button.setEnabled(False)
        self.plus_button.setEnabled(True)

        count_widget = QWidget()
        horizontal_layout = QHBoxLayout()
        horizontal_layout.setContentsMargins(0, 0, 0, 0)
        horizontal_layout.addWidget(self.minus_button, 0, Qt.AlignmentFlag.AlignLeft)
        horizontal_layout.addWidget(self.count_label, 0, Qt.AlignmentFlag.AlignLeft)
        horizontal_layout.addWidget(self.plus_button, 0, Qt.AlignmentFlag.AlignLeft)
        count_widget.setLayout(horizontal_layout)

        self.start_button = PrimaryPushButton(
            get_content_pushbutton_name_async("roll_call", "start_button")
        )
        self.start_button.setFont(QFont(load_custom_font(), 15))
        self.start_button.setFixedSize(165, 45)
        self.start_button.clicked.connect(lambda: self.start_draw())

        self.list_combobox = ComboBox()
        self.list_combobox.setFont(QFont(load_custom_font(), 12))
        self.list_combobox.setFixedSize(165, 45)
        # 延迟填充班级列表，避免启动时进行文件IO
        self.list_combobox.currentTextChanged.connect(self.on_class_changed)

        self.range_combobox = ComboBox()
        self.range_combobox.setFont(QFont(load_custom_font(), 12))
        self.range_combobox.setFixedSize(165, 45)
        # 延迟填充范围选项
        self.range_combobox.currentTextChanged.connect(self.on_filter_changed)

        self.gender_combobox = ComboBox()
        self.gender_combobox.setFont(QFont(load_custom_font(), 12))
        self.gender_combobox.setFixedSize(165, 45)
        # 延迟填充性别选项
        self.gender_combobox.currentTextChanged.connect(self.on_filter_changed)

        self.remaining_button = PushButton(
            get_content_pushbutton_name_async("roll_call", "remaining_button")
        )
        self.remaining_button.setFont(QFont(load_custom_font(), 12))
        self.remaining_button.setFixedSize(165, 45)
        self.remaining_button.clicked.connect(lambda: self.show_remaining_list())

        # 初始时不进行昂贵的数据加载，改为延迟填充
        self.total_count = 0
        self.remaining_count = 0

        text_template = get_any_position_value(
            "roll_call", "many_count_label", "text_0"
        )
        # 使用占位值，实际文本将在 populate_lists 中更新
        formatted_text = text_template.format(total_count=0, remaining_count=0)
        self.many_count_label = BodyLabel(formatted_text)
        self.many_count_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.many_count_label.setFont(QFont(load_custom_font(), 10))
        self.many_count_label.setFixedWidth(165)

        control_widget = QWidget()
        control_layout = QVBoxLayout(control_widget)
        control_layout.setContentsMargins(0, 0, 0, 0)
        control_layout.addStretch()
        control_layout.addWidget(
            self.reset_button, alignment=Qt.AlignmentFlag.AlignCenter
        )
        control_layout.addWidget(count_widget, alignment=Qt.AlignmentFlag.AlignCenter)
        control_layout.addWidget(
            self.start_button, alignment=Qt.AlignmentFlag.AlignCenter
        )
        control_layout.addWidget(
            self.list_combobox, alignment=Qt.AlignmentFlag.AlignCenter
        )
        control_layout.addWidget(
            self.range_combobox, alignment=Qt.AlignmentFlag.AlignCenter
        )
        control_layout.addWidget(
            self.gender_combobox, alignment=Qt.AlignmentFlag.AlignCenter
        )
        control_layout.addWidget(
            self.remaining_button, alignment=Qt.AlignmentFlag.AlignCenter
        )
        control_layout.addWidget(
            self.many_count_label, alignment=Qt.AlignmentFlag.AlignCenter
        )

        scroll = SmoothScrollArea()
        scroll.setWidget(container)
        scroll.setWidgetResizable(True)

        main_layout = QHBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.addWidget(scroll, 1)
        main_layout.addWidget(control_widget)

        # 在事件循环中延迟填充下拉框和初始统计，减少启动阻塞
        QTimer.singleShot(0, self.populate_lists)

    def on_class_changed(self):
        """当班级选择改变时，更新范围选择、性别选择和人数显示"""
        self.range_combobox.blockSignals(True)
        self.gender_combobox.blockSignals(True)

        try:
            self.range_combobox.clear()
            # 范围
            base_options = get_content_combo_name_async("roll_call", "range_combobox")
            group_list = get_group_list(self.list_combobox.currentText())
            # 如果有小组，才添加"抽取全部小组"选项
            if group_list and group_list != [""]:
                # 添加基础选项和小组列表
                self.range_combobox.addItems(base_options + group_list)
            else:
                # 只添加基础选项，跳过"抽取全部小组"
                self.range_combobox.addItems(base_options[:1])  # 只添加"抽取全部学生"

            # 性别
            self.gender_combobox.clear()
            gender_options = get_content_combo_name_async("roll_call", "gender_combobox")
            gender_list = get_gender_list(self.list_combobox.currentText())
            # 如果有性别，才添加"抽取全部性别"选项
            if gender_list and gender_list != [""]:
                # 添加基础选项和性别列表
                self.gender_combobox.addItems(gender_options + gender_list)
            else:
                # 只添加基础选项，跳过"抽取全部性别"
                self.gender_combobox.addItems(gender_options[:1])  # 只添加"抽取全部性别"

            # 根据当前选择的范围计算实际的总人数
            group_index = self.range_combobox.currentIndex()
            group_filter = self.range_combobox.currentText()
            
            # 使用统一的方法更新剩余人数显示
            self.update_many_count_label()
            
            # 获取当前选择的小组/性别
            group_index = self.range_combobox.currentIndex()
            
            # 根据范围计算实际人数
            if group_index == 0:  # 全班
                total_count = len(get_student_list(self.list_combobox.currentText()))
            elif group_index == 1:  # 小组模式 - 计算小组数量
                total_count = len(get_group_list(self.list_combobox.currentText()))
            else:  # 特定小组 - 计算该小组的学生数量
                group_filter = self.range_combobox.currentText()
                students = get_student_list(self.list_combobox.currentText())
                total_count = len([s for s in students if s["group"] == group_filter])
            
            # 根据总人数是否为0，启用或禁用开始按钮
            if total_count == 0:
                self.start_button.setEnabled(False)
            else:
                self.start_button.setEnabled(True)
            
            # 根据总人数是否为0，启用或禁用开始按钮
            if total_count == 0:
                self.start_button.setEnabled(False)
            else:
                self.start_button.setEnabled(True)
            
            # 更新剩余名单窗口
            if (
                hasattr(self, "remaining_list_page")
                and self.remaining_list_page is not None
            ):
                QTimer.singleShot(100, self._update_remaining_list_delayed)
        except Exception as e:
            logger.error(f"切换班级时发生错误: {e}")
        finally:
            self.range_combobox.blockSignals(False)
            self.gender_combobox.blockSignals(False)

    def on_filter_changed(self):
        """当范围或性别选择改变时，更新人数显示"""
        try:
            # 使用统一的方法更新剩余人数显示
            self.update_many_count_label()
            
            # 获取当前选择的小组/性别
            group_index = self.range_combobox.currentIndex()
            
            # 根据范围计算实际人数
            if group_index == 0:  # 全班
                total_count = len(get_student_list(self.list_combobox.currentText()))
            elif group_index == 1:  # 小组模式 - 计算小组数量
                total_count = len(get_group_list(self.list_combobox.currentText()))
            else:  # 特定小组 - 计算该小组的学生数量
                group_filter = self.range_combobox.currentText()
                students = get_student_list(self.list_combobox.currentText())
                total_count = len([s for s in students if s["group"] == group_filter])
            
            # 根据总人数是否为0，启用或禁用开始按钮
            if total_count == 0:
                self.start_button.setEnabled(False)
            else:
                self.start_button.setEnabled(True)

            if (
                hasattr(self, "remaining_list_page")
                and self.remaining_list_page is not None
            ):
                QTimer.singleShot(100, self._update_remaining_list_delayed)
        except Exception as e:
            logger.error(f"切换筛选条件时发生错误: {e}")

    def _update_remaining_list_delayed(self):
        """延迟更新剩余名单窗口的方法"""
        try:
            if (
                hasattr(self, "remaining_list_page")
                and self.remaining_list_page is not None
            ):
                class_name = self.list_combobox.currentText()
                group_filter = self.range_combobox.currentText()
                gender_filter = self.gender_combobox.currentText()
                group_index = self.range_combobox.currentIndex()
                gender_index = self.gender_combobox.currentIndex()
                half_repeat = readme_settings_async("roll_call_settings", "half_repeat")

                if hasattr(self.remaining_list_page, "update_remaining_list"):
                    self.remaining_list_page.update_remaining_list(
                        class_name,
                        group_filter,
                        gender_filter,
                        half_repeat,
                        group_index,
                        gender_index,
                        emit_signal=False,
                    )
                else:
                    if hasattr(self.remaining_list_page, "class_name"):
                        self.remaining_list_page.class_name = class_name
                    if hasattr(self.remaining_list_page, "group_filter"):
                        self.remaining_list_page.group_filter = group_filter
                    if hasattr(self.remaining_list_page, "gender_filter"):
                        self.remaining_list_page.gender_filter = gender_filter
                    if hasattr(self.remaining_list_page, "group_index"):
                        self.remaining_list_page.group_index = group_index
                    if hasattr(self.remaining_list_page, "gender_index"):
                        self.remaining_list_page.gender_index = gender_index
                    if hasattr(self.remaining_list_page, "half_repeat"):
                        self.remaining_list_page.half_repeat = half_repeat

                    if hasattr(self.remaining_list_page, "count_changed"):
                        self.remaining_list_page.count_changed.emit(
                            self.remaining_count
                        )
        except Exception as e:
            logger.error(f"延迟更新剩余名单时发生错误: {e}")

    def start_draw(self):
        """开始抽取"""
        self.start_button.setText(
            get_content_pushbutton_name_async("roll_call", "start_button")
        )
        self.start_button.setEnabled(True)
        try:
            self.start_button.clicked.disconnect()
        except:
            pass
        self.draw_random()
        animation = readme_settings_async("roll_call_settings", "animation")
        autoplay_count = readme_settings_async("roll_call_settings", "autoplay_count")
        animation_interval = readme_settings_async(
            "roll_call_settings", "animation_interval"
        )
        if animation == 0:
            self.start_button.setText(
                get_content_pushbutton_name_async("roll_call", "stop_button")
            )
            self.is_animating = True
            self.animation_timer = QTimer()
            self.animation_timer.timeout.connect(self.animate_result)
            self.animation_timer.start(animation_interval)
            self.start_button.clicked.connect(lambda: self.stop_animation())
        elif animation == 1:
            self.is_animating = True
            self.animation_timer = QTimer()
            self.animation_timer.timeout.connect(self.animate_result)
            self.animation_timer.start(animation_interval)
            self.start_button.setEnabled(False)
            QTimer.singleShot(
                autoplay_count * animation_interval,
                lambda: [
                    self.animation_timer.stop(),
                    self.stop_animation(),
                    self.start_button.setEnabled(True),
                ],
            )
            self.start_button.clicked.connect(lambda: self.start_draw())
        elif animation == 2:
            if hasattr(self, "final_selected_students") and hasattr(
                self, "final_class_name"
            ):
                save_roll_call_history(
                    class_name=self.final_class_name,
                    selected_students=self.final_selected_students,
                    students_dict_list=self.final_students_dict_list,
                    group_filter=self.final_group_filter,
                    gender_filter=self.final_gender_filter,
                )
            self.start_button.clicked.connect(lambda: self.start_draw())

    def stop_animation(self):
        """停止动画"""
        if hasattr(self, "animation_timer") and self.animation_timer.isActive():
            self.animation_timer.stop()
        self.start_button.setText(
            get_content_pushbutton_name_async("roll_call", "start_button")
        )
        self.is_animating = False
        try:
            self.start_button.clicked.disconnect()
        except:
            pass
        self.start_button.clicked.connect(lambda: self.start_draw())

        half_repeat = readme_settings_async("roll_call_settings", "half_repeat")
        if half_repeat > 0:
            record_drawn_student(
                class_name=self.final_class_name,
                gender=self.final_gender_filter,
                group=self.final_group_filter,
                student_name=self.final_selected_students,
            )

            self.update_many_count_label()

            if (
                hasattr(self, "remaining_list_page")
                and self.remaining_list_page is not None
                and hasattr(self.remaining_list_page, "count_changed")
            ):
                self.remaining_list_page.count_changed.emit(self.remaining_count)
            
            # 更新剩余名单窗口
            QTimer.singleShot(100, self._update_remaining_list_delayed)

        if hasattr(self, "final_selected_students") and hasattr(
            self, "final_class_name"
        ):
            save_roll_call_history(
                class_name=self.final_class_name,
                selected_students=self.final_selected_students_dict,
                group_filter=self.final_group_filter,
                gender_filter=self.final_gender_filter,
            )

        if hasattr(self, "final_selected_students"):
            self.display_result(self.final_selected_students, self.final_class_name)

    def animate_result(self):
        """动画过程中更新显示"""
        self.draw_random()

    def draw_random(self):
        """抽取随机结果"""
        class_name = self.list_combobox.currentText()
        group_index = self.range_combobox.currentIndex()
        group_filter = self.range_combobox.currentText()
        gender_index = self.gender_combobox.currentIndex()
        gender_filter = self.gender_combobox.currentText()

        student_file = get_resources_path("list/roll_call_list", f"{class_name}.json")
        with open_file(student_file, "r", encoding="utf-8") as f:
            data = json.load(f)

        students_data = filter_students_data(
            data, group_index, group_filter, gender_index, gender_filter
        )
        if group_index == 1:
            # 小组模式下，按小组名称排序
            students_data = sorted(students_data, key=lambda x: x[3])  # x[3]是小组名称

        # 首先将学生数据转换为字典列表
        students_dict_list = []
        for student_tuple in students_data:
            student_dict = {
                "id": student_tuple[0],
                "name": student_tuple[1],
                "gender": student_tuple[2],
                "group": student_tuple[3],
                "exist": student_tuple[4],
            }
            students_dict_list.append(student_dict)
        
        # 获取抽取类型
        draw_type = readme_settings_async("roll_call_settings", "draw_type")
        
        # 处理小组模式下的特殊逻辑
        if group_index == 1:
            # 小组模式下，students_data已经只包含小组信息
            # 直接使用小组数据进行抽取
            draw_count = min(self.current_count, len(students_dict_list))
            
            selected_groups = []
            if draw_type == 1:
                # 权重抽取模式下，所有小组权重相同
                weights = [1.0] * len(students_dict_list)
                
                # 根据权重抽取小组
                for _ in range(draw_count):
                    if not students_dict_list:
                        break
                    total_weight = sum(weights)
                    if total_weight <= 0:
                        random_index = system_random.randint(0, len(students_dict_list) - 1)
                    else:
                        rand_value = system_random.uniform(0, total_weight)
                        cumulative_weight = 0
                        random_index = 0
                        for i, weight in enumerate(weights):
                            cumulative_weight += weight
                            if rand_value <= cumulative_weight:
                                random_index = i
                                break
                    
                    selected_group = students_dict_list[random_index]
                    selected_groups.append((None, selected_group["name"], True))  # (id, name, exist)
                    
                    students_dict_list.pop(random_index)
                    weights.pop(random_index)
            else:
                # 随机抽取模式
                for _ in range(draw_count):
                    if not students_dict_list:
                        break
                    random_index = system_random.randint(0, len(students_dict_list) - 1)
                    selected_group = students_dict_list[random_index]
                    selected_groups.append((None, selected_group["name"], True))  # (id, name, exist)
                    
                    students_dict_list.pop(random_index)
            
            self.final_selected_students = selected_groups
            self.final_class_name = class_name
            self.final_selected_students_dict = []  # 小组模式下不存储学生字典
            self.final_group_filter = group_filter
            self.final_gender_filter = gender_filter
            
            self.display_result(selected_groups, class_name)
            return

        half_repeat = readme_settings_async("roll_call_settings", "half_repeat")
        if half_repeat > 0:
            drawn_records = read_drawn_record(class_name, gender_filter, group_filter)
            drawn_counts = {name: count for name, count in drawn_records}

            filtered_students = []
            for student in students_dict_list:
                student_name = student["name"]
                if (
                    student_name not in drawn_counts
                    or drawn_counts[student_name] < half_repeat
                ):
                    filtered_students.append(student)

            students_dict_list = filtered_students

        if not students_dict_list:
            reset_drawn_record(self, class_name, gender_filter, group_filter)

        draw_type = readme_settings_async("roll_call_settings", "draw_type")
        if draw_type == 1:
            students_with_weight = calculate_weight(students_dict_list, class_name)
            weights = []
            for student in students_with_weight:
                weights.append(student.get("weight", 1.0))
        else:
            students_with_weight = students_dict_list
            weights = [1.0] * len(students_dict_list)

        draw_count = self.current_count
        draw_count = min(draw_count, len(students_with_weight))

        selected_students = []
        selected_students_dict = []
        for _ in range(draw_count):
            if not students_with_weight:
                break
            total_weight = sum(weights)
            if total_weight <= 0:
                random_index = system_random.randint(0, len(students_with_weight) - 1)
            else:
                rand_value = system_random.uniform(0, total_weight)
                cumulative_weight = 0
                random_index = 0
                for i, weight in enumerate(weights):
                    cumulative_weight += weight
                    if rand_value <= cumulative_weight:
                        random_index = i
                        break

            selected_student = students_with_weight[random_index]
            id = selected_student.get("id", "")
            random_name = selected_student.get("name", "")
            exist = selected_student.get("exist", True)
            selected_students.append((id, random_name, exist))
            selected_students_dict.append(selected_student)

            students_with_weight.pop(random_index)
            weights.pop(random_index)

        self.final_selected_students = selected_students
        self.final_class_name = class_name
        self.final_selected_students_dict = selected_students_dict
        self.final_group_filter = group_filter
        self.final_gender_filter = gender_filter

        self.display_result(selected_students, class_name)

    def display_result(self, selected_students, class_name):
        """显示抽取结果"""
        group_index = self.range_combobox.currentIndex()
        student_labels = ResultDisplayUtils.create_student_label(
            class_name=class_name,
            selected_students=selected_students,
            draw_count=self.current_count,
            font_size=readme_settings_async("roll_call_settings", "font_size"),
            animation_color=readme_settings_async(
                "roll_call_settings", "animation_color_theme"
            ),
            display_format=readme_settings_async(
                "roll_call_settings", "display_format"
            ),
            show_student_image=readme_settings_async(
                "roll_call_settings", "student_image"
            ),
            group_index=group_index,
            show_random=readme_settings_async(
                "roll_call_settings", "show_random"
            ),
        )
        ResultDisplayUtils.display_results_in_grid(self.result_grid, student_labels)

    def reset_count(self):
        """重置人数"""
        self.current_count = 1
        self.count_label.setText("1")
        self.minus_button.setEnabled(False)
        self.plus_button.setEnabled(True)
        class_name = self.list_combobox.currentText()
        gender = self.gender_combobox.currentText()
        group = self.range_combobox.currentText()
        reset_drawn_record(self, class_name, gender, group)
        self.clear_result()
        self.update_many_count_label()

        # 更新剩余名单窗口
        if (
            hasattr(self, "remaining_list_page")
            and self.remaining_list_page is not None
        ):
            QTimer.singleShot(100, self._update_remaining_list_delayed)

        if (
            hasattr(self, "remaining_list_page")
            and self.remaining_list_page is not None
            and hasattr(self.remaining_list_page, "count_changed")
        ):
            self.remaining_list_page.count_changed.emit(self.remaining_count)

    def clear_result(self):
        """清空结果显示"""
        ResultDisplayUtils.clear_grid(self.result_grid)

    def update_count(self, change):
        """更新人数

        Args:
            change (int): 变化量，正数表示增加，负数表示减少
        """
        try:
            self.total_count = self.get_total_count()
            self.current_count = max(1, int(self.count_label.text()) + change)
            self.count_label.setText(str(self.current_count))
            self.minus_button.setEnabled(self.current_count > 1)
            self.plus_button.setEnabled(self.current_count < self.total_count)
        except (ValueError, TypeError):
            self.count_label.setText("1")
            self.minus_button.setEnabled(False)
            self.plus_button.setEnabled(True)

    def get_total_count(self):
        """获取总人数"""
        # 获取当前选择的范围和性别
        group_index = self.range_combobox.currentIndex()
        group_filter = self.range_combobox.currentText()
        
        # 根据范围计算实际人数
        if group_index == 0:  # 全班
            total_count = len(get_student_list(self.list_combobox.currentText()))
        elif group_index == 1:  # 小组模式 - 计算小组数量
            total_count = len(get_group_list(self.list_combobox.currentText()))
        else:  # 特定小组 - 计算该小组的学生数量
            students = get_student_list(self.list_combobox.currentText())
            total_count = len([s for s in students if s["group"] == group_filter])
        return total_count

    def update_many_count_label(self):
        """更新多数量显示标签"""
        # 获取当前选择的小组/性别
        group_index = self.range_combobox.currentIndex()
        group_filter = self.range_combobox.currentText()
        gender_filter = self.gender_combobox.currentText()
        
        # 根据范围计算实际人数
        if group_index == 0:  # 全班
            total_count = len(get_student_list(self.list_combobox.currentText()))
        elif group_index == 1:  # 小组模式 - 计算小组数量
            total_count = len(get_group_list(self.list_combobox.currentText()))
        else:  # 特定小组 - 计算该小组的学生数量
            students = get_student_list(self.list_combobox.currentText())
            total_count = len([s for s in students if s["group"] == group_filter])

        self.remaining_count = calculate_remaining_count(
            half_repeat=readme_settings_async("roll_call_settings", "half_repeat"),
            class_name=self.list_combobox.currentText(),
            gender_filter=gender_filter,
            group_index=group_index,
            group_filter=group_filter,
            total_count=total_count,
        )
        if self.remaining_count == 0:
            self.remaining_count = total_count
        
        # 根据是否为小组模式选择不同的文本模板
        if group_index == 1:  # 小组模式
            text_template = get_any_position_value(
                "roll_call", "many_count_label", "text_3"
            )
        else:  # 学生模式
            text_template = get_any_position_value(
                "roll_call", "many_count_label", "text_0"
            )
        formatted_text = text_template.format(
            total_count=total_count, remaining_count=self.remaining_count
        )
        self.many_count_label.setText(formatted_text)
        
        # 根据总人数是否为0，启用或禁用开始按钮
        if total_count == 0:
            self.start_button.setEnabled(False)
        else:
            self.start_button.setEnabled(True)

    def update_remaining_list_window(self):
        """更新剩余名单窗口的内容"""
        if hasattr(self, "remaining_list_page") and self.remaining_list_page is not None:
            try:
                class_name = self.list_combobox.currentText()
                group_filter = self.range_combobox.currentText()
                gender_filter = self.gender_combobox.currentText()
                group_index = self.range_combobox.currentIndex()
                gender_index = self.gender_combobox.currentIndex()
                half_repeat = readme_settings_async("roll_call_settings", "half_repeat")
                
                # 更新剩余名单页面内容
                if hasattr(self.remaining_list_page, "update_remaining_list"):
                    self.remaining_list_page.update_remaining_list(
                        class_name,
                        group_filter,
                        gender_filter,
                        half_repeat,
                        group_index,
                        gender_index,
                        emit_signal=False,  # 不发出信号，避免循环更新
                    )
            except Exception as e:
                logger.error(f"更新剩余名单窗口内容失败: {e}")

    def show_remaining_list(self):
        """显示剩余名单窗口"""
        # 如果窗口已存在，则激活该窗口并更新内容
        if hasattr(self, "remaining_list_page") and self.remaining_list_page is not None:
            try:
                # 获取窗口实例
                window = self.remaining_list_page.window()
                if window is not None:
                    # 激活窗口并置于前台
                    window.raise_()
                    window.activateWindow()
                    # 更新窗口内容
                    self.update_remaining_list_window()
                    return
            except Exception as e:
                logger.error(f"激活剩余名单窗口失败: {e}")
                # 如果激活失败，继续创建新窗口
        
        # 创建新窗口
        class_name = self.list_combobox.currentText()
        group_filter = self.range_combobox.currentText()
        gender_filter = self.gender_combobox.currentText()
        group_index = self.range_combobox.currentIndex()
        gender_index = self.gender_combobox.currentIndex()
        half_repeat = readme_settings_async("roll_call_settings", "half_repeat")

        window, get_page = create_remaining_list_window(
            class_name,
            group_filter,
            gender_filter,
            half_repeat,
            group_index,
            gender_index,
        )

        def on_page_ready(page):
            self.remaining_list_page = page

            if page and hasattr(page, "count_changed"):
                page.count_changed.connect(self.update_many_count_label)
                self.update_many_count_label()

        get_page(on_page_ready)

        window.windowClosed.connect(lambda: setattr(self, "remaining_list_page", None))

        window.show()

    def setup_file_watcher(self):
        """设置文件监控器，监控名单文件夹的变化"""
        try:
            list_dir = get_path("app/resources/list/roll_call_list")

            if not list_dir.exists():
                list_dir.mkdir(parents=True, exist_ok=True)

            self.file_watcher.addPath(str(list_dir))

            self.file_watcher.directoryChanged.connect(self.on_directory_changed)
            self.file_watcher.fileChanged.connect(self.on_file_changed)

        except Exception as e:
            logger.error(f"设置文件监控器失败: {e}")

    def on_directory_changed(self, path):
        """当文件夹内容发生变化时触发"""
        try:
            QTimer.singleShot(500, self.refresh_class_list)
        except Exception as e:
            logger.error(f"处理文件夹变化事件失败: {e}")

    def on_file_changed(self, path):
        """当文件内容发生变化时触发"""
        try:
            QTimer.singleShot(500, self.refresh_class_list)
        except Exception as e:
            logger.error(f"处理文件变化事件失败: {e}")

    def refresh_class_list(self):
        """刷新班级列表下拉框"""
        try:
            current_class = self.list_combobox.currentText()

            new_class_list = get_class_name_list()

            self.list_combobox.blockSignals(True)

            self.list_combobox.clear()
            self.list_combobox.addItems(new_class_list)

            if current_class in new_class_list:
                index = self.list_combobox.findText(current_class)
                if index >= 0:
                    self.list_combobox.setCurrentIndex(index)
            elif new_class_list:
                self.list_combobox.setCurrentIndex(0)

            self.list_combobox.blockSignals(False)

            self.on_class_changed()

        except Exception as e:
            logger.error(f"刷新班级列表失败: {e}")

    def populate_lists(self):
        """在后台填充班级/范围/性别下拉框并更新人数统计"""
        try:
            # 填充班级列表
            class_list = get_class_name_list()
            self.list_combobox.blockSignals(True)
            self.list_combobox.clear()
            if class_list:
                self.list_combobox.addItems(class_list)
                self.list_combobox.setCurrentIndex(0)
            self.list_combobox.blockSignals(False)

            # 填充范围和性别选项
            self.range_combobox.blockSignals(True)
            self.range_combobox.clear()
            
            # 获取基础选项
            base_options = get_content_combo_name_async("roll_call", "range_combobox")
            
            # 获取小组列表
            group_list = get_group_list(self.list_combobox.currentText())
            
            # 如果有小组，才添加"抽取全部小组"选项
            if group_list:
                # 添加基础选项和小组列表
                self.range_combobox.addItems(base_options + group_list)
            else:
                # 只添加基础选项，跳过"抽取全部小组"
                self.range_combobox.addItems(base_options[:1])  # 只添加"抽取全部学生"
                
            self.range_combobox.blockSignals(False)

            self.gender_combobox.blockSignals(True)
            self.gender_combobox.clear()
            self.gender_combobox.addItems(
                get_content_combo_name_async("roll_call", "gender_combobox")
                + get_gender_list(self.list_combobox.currentText())
            )
            self.gender_combobox.blockSignals(False)

            # 根据当前选择的范围计算实际的总人数
            group_index = self.range_combobox.currentIndex()
            group_filter = self.range_combobox.currentText()
            gender_filter = self.gender_combobox.currentText()
            
            # 根据范围计算实际人数
            if group_index == 0:  # 全班
                total_count = len(get_student_list(self.list_combobox.currentText()))
            elif group_index == 1:  # 小组模式 - 计算小组数量
                total_count = len(get_group_list(self.list_combobox.currentText()))
            else:  # 特定小组 - 计算该小组的学生数量
                students = get_student_list(self.list_combobox.currentText())
                total_count = len([s for s in students if s["group"] == group_filter])

            self.remaining_count = calculate_remaining_count(
                half_repeat=readme_settings("roll_call_settings", "half_repeat"),
                class_name=self.list_combobox.currentText(),
                gender_filter=gender_filter,
                group_index=group_index,
                group_filter=group_filter,
                total_count=total_count,
            )

            # 根据是否为小组模式选择不同的文本模板
            if group_index == 1:  # 小组模式
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_3"
                )
            else:  # 学生模式
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_0"
                )
            formatted_text = text_template.format(
                total_count=total_count, remaining_count=self.remaining_count
            )
            self.many_count_label.setText(formatted_text)
            
            # 根据总人数是否为0，启用或禁用开始按钮
            if total_count == 0:
                self.start_button.setEnabled(False)
            else:
                self.start_button.setEnabled(True)

        except Exception as e:
            logger.error(f"延迟填充列表失败: {e}")
