# ==================================================
# 导入库
# ==================================================
import json
from collections import OrderedDict

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
from app.common.data.list import *
from .shared_file_watcher import get_shared_file_watcher

# ==================================================
# 点名名单表格
# ==================================================


class roll_call_table(GroupHeaderCardWidget):
    """点名名单表格卡片"""

    refresh_signal = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent = parent
        self.setTitle(get_content_name_async("roll_call_table", "title"))
        self.setBorderRadius(8)
        self._search_timer = QTimer(self)
        self._search_timer.setSingleShot(True)
        self._search_timer.setInterval(300)
        self._search_timer.timeout.connect(self._apply_search_filter)

        # 创建班级选择区域
        QTimer.singleShot(APPLY_DELAY, self.create_class_selection)

        # 创建搜索栏
        QTimer.singleShot(APPLY_DELAY, self.create_search_bar)

        # 创建表格区域
        QTimer.singleShot(APPLY_DELAY, self.create_table)

        # 初始化班级列表
        QTimer.singleShot(APPLY_DELAY, self.refresh_class_list)

        # 设置文件系统监视器
        QTimer.singleShot(APPLY_DELAY, self.setup_file_watcher)

        # 初始化数据
        QTimer.singleShot(APPLY_DELAY, self.refresh_data)

        # 连接信号
        self.refresh_signal.connect(self.refresh_data)

    def create_class_selection(self):
        """创建班级选择区域"""
        self.class_comboBox = ComboBox()
        self.class_comboBox.setCurrentIndex(
            readme_settings_async("roll_call_table", "select_class_name")
        )
        if not get_class_name_list():
            self.class_comboBox.setCurrentIndex(-1)
            self.class_comboBox.setPlaceholderText(
                get_content_name_async("roll_call_table", "select_class_name")
            )
        self.class_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "roll_call_table",
                "select_class_name",
                self.class_comboBox.currentIndex(),
            )
        )
        self.class_comboBox.currentTextChanged.connect(self.refresh_data)

        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("roll_call_table", "select_class_name"),
            get_content_description_async("roll_call_table", "select_class_name"),
            self.class_comboBox,
        )

    def create_search_bar(self):
        """创建搜索栏"""
        self.search_line_edit = SearchLineEdit()
        self.search_line_edit.setPlaceholderText(
            get_content_name_async("roll_call_table", "search_placeholder")
        )
        self.search_line_edit.setClearButtonEnabled(True)
        self.search_line_edit.textChanged.connect(self._on_search_text_changed)

        self.addGroup(
            get_theme_icon("ic_fluent_search_20_filled"),
            get_content_name_async("roll_call_table", "search"),
            get_content_description_async("roll_call_table", "search"),
            self.search_line_edit,
        )

    def _on_search_text_changed(self):
        """搜索文本变化时启动防抖计时器"""
        self._search_timer.start()

    def _apply_search_filter(self):
        """根据搜索关键词过滤表格行"""
        if not hasattr(self, "table") or self.table is None:
            return
        if not hasattr(self, "search_line_edit"):
            return

        keyword = self.search_line_edit.text().strip().lower()

        for row in range(self.table.rowCount()):
            if not keyword:
                self.table.setRowHidden(row, False)
                continue

            matched = False
            for col in range(self.table.columnCount()):
                item = self.table.item(row, col)
                if item and keyword in item.text().lower():
                    matched = True
                    break
            self.table.setRowHidden(row, not matched)

    def create_table(self):
        """创建表格区域"""
        # 创建表格
        self.table = TableWidget()
        self.table.setBorderVisible(True)
        self.table.setBorderRadius(8)
        self.table.setWordWrap(False)
        self.table.setColumnCount(6)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.DoubleClicked)
        self.table.setSortingEnabled(True)
        self.table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.verticalHeader().hide()

        self.table.setHorizontalHeaderLabels(
            get_content_name_async("roll_call_table", "HeaderLabels")
        )
        self.table.horizontalHeader().resizeSection(0, 80)
        # 设置表格属性
        self.table.horizontalHeader().setSectionResizeMode(
            0, QHeaderView.ResizeMode.ResizeToContents
        )
        for i in range(1, self.table.columnCount()):
            self.table.horizontalHeader().setSectionResizeMode(
                i, QHeaderView.ResizeMode.Stretch
            )
        for i in range(self.table.columnCount()):
            self.table.horizontalHeader().setDefaultAlignment(
                Qt.AlignmentFlag.AlignCenter
            )
        # 连接单元格修改信号
        self.table.cellChanged.connect(self.save_table_data)
        self.layout().addWidget(self.table)

    def setup_file_watcher(self):
        """设置文件系统监视器，监控班级名单文件夹的变化 - 使用共享监视器"""
        # 获取班级名单文件夹路径
        roll_call_list_dir = get_data_path("list", "roll_call_list")

        # 确保目录存在
        if not roll_call_list_dir.exists():
            logger.warning(f"班级名单文件夹不存在: {roll_call_list_dir}")
            return

        # 使用共享文件系统监视器管理器
        self._shared_watcher = get_shared_file_watcher()
        self._shared_watcher.add_watcher(
            str(roll_call_list_dir), self.on_directory_changed
        )

        # logger.debug(f"已设置共享文件监视器，监控目录: {roll_call_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成
        QTimer.singleShot(1000, self.refresh_class_list)

    def refresh_class_list(self):
        """刷新班级下拉框列表"""
        # 检查班级下拉框是否仍然有效
        if not hasattr(self, "class_comboBox") or self.class_comboBox is None:
            logger.debug("班级下拉框已被销毁，跳过刷新")
            return

        try:
            # 保存当前选中的班级名称
            current_class_name = self.class_comboBox.currentText()

            # 获取最新的班级列表
            class_list = get_class_name_list()

            # 清空并重新添加班级列表
            self.class_comboBox.clear()
            self.class_comboBox.addItems(class_list)

            # 尝试恢复之前选中的班级
            if current_class_name and current_class_name in class_list:
                index = class_list.index(current_class_name)
                self.class_comboBox.setCurrentIndex(index)
            elif not class_list:
                self.class_comboBox.setCurrentIndex(-1)
                self.class_comboBox.setPlaceholderText(
                    get_content_name_async("roll_call_list", "select_class_name")
                )

            # logger.debug(f"班级列表已刷新，共 {len(class_list)} 个班级")
            # 只有在表格已经创建时才刷新数据
            if hasattr(self, "table") and self.table is not None:
                self.refresh_data()
        except RuntimeError as e:
            logger.error(f"刷新班级列表时发生错误: {e}")
        except Exception as e:
            logger.error(f"刷新班级列表时发生未知错误: {e}")

    def refresh_data(self):
        """刷新表格数据"""
        # 确保表格已经创建
        if not hasattr(self, "table") or self.table is None:
            return

        # 确保班级下拉框仍然有效
        if not hasattr(self, "class_comboBox") or self.class_comboBox is None:
            return

        try:
            class_name = self.class_comboBox.currentText()
        except RuntimeError:
            logger.exception("班级下拉框已被销毁")
            return

        if not class_name:
            self.table.setRowCount(0)
            return

        # 临时阻止信号，避免初始化时触发保存操作
        self.table.blockSignals(True)

        try:
            # 获取学生数据
            students = get_student_list(class_name)
            if not students:
                self.table.setRowCount(0)
                return

            # 设置表格行数
            self.table.setRowCount(len(students))

            # 填充表格数据
            for row, student in enumerate(students):
                # 是否在班级勾选框
                checkbox_item = QTableWidgetItem()
                checkbox_item.setCheckState(
                    Qt.CheckState.Checked
                    if student.get("exist", True)
                    else Qt.CheckState.Unchecked
                )
                checkbox_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 0, checkbox_item)

                # 学号
                id_item = QTableWidgetItem(str(student.get("id", row + 1)))
                id_item.setFlags(
                    id_item.flags() & ~Qt.ItemFlag.ItemIsEditable
                )  # 学号不可编辑
                id_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 1, id_item)

                # 姓名
                name_item = QTableWidgetItem(student.get("name", ""))
                name_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 2, name_item)

                # 性别
                gender_item = QTableWidgetItem(student.get("gender", ""))
                gender_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 3, gender_item)

                # 小组
                group_item = QTableWidgetItem(student.get("group", ""))
                group_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 4, group_item)

                tags_value = student.get("tags", [])
                tags_text = (
                    ", ".join([str(t).strip() for t in tags_value if str(t).strip()])
                    if isinstance(tags_value, list)
                    else str(tags_value or "")
                )
                tags_item = QTableWidgetItem(tags_text)
                tags_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 5, tags_item)

            # 调整列宽
            self.table.horizontalHeader().resizeSection(0, 80)
            self.table.horizontalHeader().setSectionResizeMode(
                0, QHeaderView.ResizeMode.ResizeToContents
            )
            for i in range(1, self.table.columnCount()):
                self.table.horizontalHeader().setSectionResizeMode(
                    i, QHeaderView.ResizeMode.Stretch
                )

        except Exception as e:
            logger.exception(f"刷新表格数据失败: {str(e)}")
        finally:
            # 恢复信号
            self.table.blockSignals(False)
            self._apply_search_filter()

    def save_table_data(self, row, col):
        """保存表格编辑的数据"""
        class_name = self.class_comboBox.currentText()
        if not class_name:
            return

        # 获取当前单元格
        item = self.table.item(row, col)
        if not item:
            return

        # 获取当前行的学号和姓名
        id_item = self.table.item(row, 1)
        name_item = self.table.item(row, 2)
        if not id_item or not name_item:
            return
        student_id = id_item.text()
        student_name = name_item.text()

        # 加载当前班级的学生数据
        roll_call_list_dir = get_data_path("list", "roll_call_list")
        student_file = roll_call_list_dir / f"{class_name}.json"
        try:
            with open_file(student_file, "r", encoding="utf-8") as f:
                student_data = json.load(f, object_pairs_hook=OrderedDict)
        except Exception as e:
            logger.exception(f"加载学生数据失败: {str(e)}")
            return

        # 通过学号找到对应的学生键
        matched_key = None
        for key, value in student_data.items():
            stored_id = value.get("id")
            if str(stored_id).lstrip("0") == str(student_id).lstrip("0") or str(
                stored_id
            ) == str(student_id):
                matched_key = key
                break

        if not matched_key:
            logger.exception(
                f"未找到学号为 {student_id} 的学生，学生姓名: {student_name}"
            )
            return

        # 根据列索引更新相应的字段
        new_value = item.text()
        if col == 2:  # 姓名列
            # 直接更新姓名，不再使用括号标记
            if new_value != matched_key:
                new_student_data = OrderedDict()
                for key, value in student_data.items():
                    if key == matched_key:
                        new_student_data[new_value] = value
                    else:
                        new_student_data[key] = value
                student_data = new_student_data
        elif col == 3:  # 性别列
            student_data[matched_key]["gender"] = new_value
        elif col == 4:  # 小组列
            student_data[matched_key]["group"] = new_value
        elif col == 5:  # 标签列
            raw = str(new_value or "").strip()
            for sep in ["，", ",", "；", ";", "|", "/", "\\", "\n", "\t"]:
                raw = raw.replace(sep, " ")
            tags = []
            for item in raw.split(" "):
                item = item.strip()
                if item and item not in tags:
                    tags.append(item)
            student_data[matched_key]["tags"] = tags
        elif col == 0:  # "是否在班级"勾选框列
            checkbox_item = self.table.item(row, 0)
            if checkbox_item:
                is_checked = checkbox_item.checkState() == Qt.CheckState.Checked
                student_data[matched_key]["exist"] = is_checked

        # 保存更新后的数据
        try:
            # 使用共享文件监视器时，不需要手动移除和重新添加路径
            # 共享监视器会自动处理多个回调，避免循环触发
            with open_file(student_file, "w", encoding="utf-8") as f:
                json.dump(student_data, f, ensure_ascii=False, indent=4)
            # logger.debug(f"学生数据更新成功: {student_name}")

            # 保存成功后设置列宽
            self.table.blockSignals(True)
            for i in range(1, self.table.columnCount()):
                self.table.horizontalHeader().setSectionResizeMode(
                    i, QHeaderView.ResizeMode.Stretch
                )
            self.table.blockSignals(False)
        except Exception as e:
            logger.exception(f"保存学生数据失败: {str(e)}")
            # 如果保存失败，恢复原来的值
            self.table.blockSignals(True)  # 阻止信号，避免递归调用
            if col == 2:  # 姓名列
                item.setText(str(matched_key) if matched_key else student_name)
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            else:
                original_value = ""
                if matched_key:
                    original_value = (
                        student_data[matched_key]["gender"]
                        if col == 3
                        else student_data[matched_key]["group"]
                        if col == 4
                        else ", ".join(student_data[matched_key].get("tags", []))
                        if col == 5
                        else ""
                    )
                item.setText(str(original_value))
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.table.blockSignals(False)  # 恢复信号

            # 共享文件监视器不需要手动重新启用

    def cleanup_file_watcher(self):
        """清理文件系统监视器"""
        if hasattr(self, "_shared_watcher"):
            roll_call_list_dir = get_data_path("list", "roll_call_list")
            if roll_call_list_dir.exists():
                self._shared_watcher.remove_watcher(
                    str(roll_call_list_dir), self.on_directory_changed
                )

    def __del__(self):
        """析构函数，确保清理文件监视器"""
        try:
            self.cleanup_file_watcher()
        except Exception:
            pass
