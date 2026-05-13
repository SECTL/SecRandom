# ==================================================
# 导入库
# ==================================================
import os
import json
from typing import Dict, List, Any
from concurrent.futures import ThreadPoolExecutor

from loguru import logger
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
from app.common.data.list import get_duplicate_names, make_unique_names


class ImportStudentNameWindow(QWidget):
    """学生姓名导入窗口"""

    # 定义信号，用于后台加载文件完成后通知UI线程
    fileLoaded = Signal(object, list)  # 参数：数据，列名列表
    fileLoadError = Signal(str)  # 参数：错误信息

    def __init__(self, parent=None, class_name=None):
        """初始化学生姓名导入窗口"""
        # 调用父类初始化方法
        super().__init__(parent)

        # 初始化变量
        self.file_path = None
        self.data = None
        self.columns = []
        self.column_mapping = {}
        self.preview_data = []
        self.class_name = class_name
        # 线程池用于后台加载文件
        self.executor = ThreadPoolExecutor(max_workers=2)

        # 创建UI
        self.__init_ui()

        # 连接信号
        self.__connect_signals()

    def __init_ui(self):
        """初始化UI组件"""
        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(20, 20, 20, 20)
        self.main_layout.setSpacing(5)

        # 创建标题
        self.title_label = TitleLabel(
            get_content_name_async("import_student_name", "title")
        )
        self.main_layout.addWidget(self.title_label)

        # 创建当前导入班级的提示标签
        display_class_name = (
            self.class_name
            if self.class_name
            else readme_settings_async("roll_call_list", "select_class_name")
        )
        self.class_name_label = SubtitleLabel(
            get_content_name_async("import_student_name", "initial_subtitle")
            + display_class_name
        )
        self.main_layout.addWidget(self.class_name_label)

        # 创建文件选择区域
        self.__create_file_selection_area()

        # 创建列映射区域
        self.__create_column_mapping_area()

        # 创建预览区域
        self.__create_preview_area()

        # 创建按钮区域
        self.__create_button_area()

        # 添加伸缩项
        self.main_layout.addStretch(1)

        # 初始状态下禁用部分控件
        self.__update_ui_state()

    def __create_file_selection_area(self):
        """创建文件选择区域"""
        # 创建卡片容器
        file_card = CardWidget()
        file_layout = QVBoxLayout(file_card)

        # 创建文件选择区域标题
        file_title = SubtitleLabel(
            get_content_name_async("import_student_name", "file_selection_title")
        )
        file_layout.addWidget(file_title)

        # 创建文件选择行
        file_row = QHBoxLayout()

        # 文件路径标签
        self.file_path_label = BodyLabel(
            get_content_name_async("import_student_name", "no_file_selected")
        )
        self.file_path_label.setWordWrap(True)
        file_row.addWidget(self.file_path_label, 1)

        # 选择文件按钮
        self.select_file_btn = PrimaryPushButton(
            get_content_name_async("import_student_name", "select_file")
        )
        self.select_file_btn.setIcon(FluentIcon.FOLDER)
        self.select_file_btn.setFixedWidth(120)
        file_row.addWidget(self.select_file_btn)

        file_layout.addLayout(file_row)

        # 支持格式提示
        format_hint = CaptionLabel(
            get_content_name_async("import_student_name", "supported_formats")
        )
        file_layout.addWidget(format_hint)

        # 添加到主布局
        self.main_layout.addWidget(file_card)

    def __create_column_mapping_area(self):
        """创建列映射区域"""
        # 创建卡片容器
        mapping_card = CardWidget()
        mapping_layout = QVBoxLayout(mapping_card)

        # 创建列映射区域标题
        mapping_title = SubtitleLabel(
            get_content_name_async("import_student_name", "column_mapping_title")
        )
        mapping_layout.addWidget(mapping_title)

        # 创建列映射说明
        mapping_desc = BodyLabel(
            get_content_name_async("import_student_name", "column_mapping_description")
        )
        mapping_layout.addWidget(mapping_desc)

        # 创建列映射表单布局
        mapping_form = QVBoxLayout()

        # 学号列选择（必选项，第一个）
        id_row = QHBoxLayout()
        id_label = BodyLabel(
            get_content_name_async("import_student_name", "column_mapping_id_column")
        )
        self.id_column_combo = ComboBox()
        self.id_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        id_row.addWidget(id_label)
        id_row.addWidget(self.id_column_combo, 1)
        mapping_form.addLayout(id_row)

        # 姓名列选择（必选项，第二个）
        name_row = QHBoxLayout()
        name_label = BodyLabel(
            get_content_name_async("import_student_name", "column_mapping_name_column")
        )
        self.name_column_combo = ComboBox()
        self.name_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        name_row.addWidget(name_label)
        name_row.addWidget(self.name_column_combo, 1)
        mapping_form.addLayout(name_row)

        # 性别列选择（可选，第三个）
        gender_row = QHBoxLayout()
        gender_label = BodyLabel(
            get_content_name_async(
                "import_student_name", "column_mapping_gender_column"
            )
        )
        self.gender_column_combo = ComboBox()
        self.gender_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        gender_row.addWidget(gender_label)
        gender_row.addWidget(self.gender_column_combo, 1)
        mapping_form.addLayout(gender_row)

        # 小组列选择（可选，第四个）
        group_row = QHBoxLayout()
        group_label = BodyLabel(
            get_content_name_async("import_student_name", "column_mapping_group_column")
        )
        self.group_column_combo = ComboBox()
        self.group_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        group_row.addWidget(group_label)
        group_row.addWidget(self.group_column_combo, 1)
        mapping_form.addLayout(group_row)

        # 标签列选择（可选，第五个）
        tags_row = QHBoxLayout()
        tags_label = BodyLabel(
            get_content_name_async("import_student_name", "column_mapping_tags_column")
        )
        self.tags_column_combo = ComboBox()
        self.tags_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        tags_row.addWidget(tags_label)
        tags_row.addWidget(self.tags_column_combo, 1)
        mapping_form.addLayout(tags_row)

        mapping_layout.addLayout(mapping_form)

        # 添加到主布局
        self.main_layout.addWidget(mapping_card)

    def __create_preview_area(self):
        """创建预览区域"""
        # 创建卡片容器
        preview_card = CardWidget()
        preview_layout = QVBoxLayout(preview_card)

        # 创建预览区域标题
        preview_title = SubtitleLabel(
            get_content_name_async("import_student_name", "data_preview_title")
        )
        preview_layout.addWidget(preview_title)

        # 创建预览表格
        self.preview_table = TableWidget()
        self.preview_table.setWordWrap(True)
        self.preview_table.verticalHeader().setVisible(False)
        # 限制预览表格高度
        self.preview_table.setMaximumHeight(150)
        # 设置固定行数显示
        self.preview_table.setRowCount(0)
        preview_layout.addWidget(self.preview_table)

        # 添加到主布局
        self.main_layout.addWidget(preview_card)

    def __create_button_area(self):
        """创建按钮区域"""
        # 创建按钮布局
        button_layout = QHBoxLayout()

        # 伸缩项
        button_layout.addStretch(1)

        # 导入按钮
        self.import_btn = PrimaryPushButton(
            get_content_name_async("import_student_name", "buttons_import")
        )
        self.import_btn.setIcon(FluentIcon.DOWNLOAD)
        self.import_btn.setEnabled(False)
        button_layout.addWidget(self.import_btn)

        # 添加到主布局
        self.main_layout.addLayout(button_layout)

    def __connect_signals(self):
        """连接信号"""
        # 选择文件按钮
        self.select_file_btn.clicked.connect(self.__select_file)

        # 列映射变化
        self.id_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        self.name_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        self.gender_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        self.group_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )
        self.tags_column_combo.currentIndexChanged.connect(
            self.__on_column_mapping_changed
        )

        # 按钮事件
        self.import_btn.clicked.connect(self.__import_data)

        # 后台文件加载完成信号
        self.fileLoaded.connect(self.__on_file_loaded)
        self.fileLoadError.connect(self.__on_file_load_error)

    def __show_loading_animation(self):
        """显示加载动画占位实现。"""

    def __hide_loading_animation(self):
        """隐藏加载动画占位实现。"""

    def __update_ui_state(self):
        """更新UI状态"""
        has_file = self.file_path is not None
        id_column = self.id_column_combo.currentText()
        name_column = self.name_column_combo.currentText()

        # 检查是否选择了"无"选项
        if id_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            id_column = ""
        if name_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            name_column = ""

        has_id = bool(id_column)
        has_name = bool(name_column)

        logger.debug(
            f"has_file: {has_file}, has_id: {id_column}, has_name: {name_column}"
        )

        # 更新控件状态
        self.id_column_combo.setEnabled(has_file)
        self.name_column_combo.setEnabled(has_file)
        self.gender_column_combo.setEnabled(has_file)
        self.group_column_combo.setEnabled(has_file)
        self.tags_column_combo.setEnabled(has_file)

        # 导入按钮需要同时有文件、学号映射和姓名映射
        if hasattr(self, "import_btn"):
            self.import_btn.setEnabled(has_file and has_id and has_name)

    def __on_column_mapping_changed(self):
        """列映射变化时的处理"""
        # 检查UI组件是否已初始化
        if not hasattr(self, "preview_table"):
            return

        # 更新预览
        self.__update_preview()
        # 更新UI状态
        self.__update_ui_state()

    def __select_file(self):
        """选择文件"""
        # 定义支持的文件过滤器
        file_filter = get_content_name_async("import_student_name", "file_filter")

        # 打开文件对话框
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            get_content_name_async("import_student_name", "dialog_title"),
            "",
            file_filter,
        )

        if file_path:
            # 更新文件路径标签
            self.file_path_label.setText(os.path.basename(file_path))
            self.__show_loading_animation()

            # 使用线程池在后台加载文件
            self.executor.submit(self.__load_file, file_path)

    def __on_file_loaded(self, data, columns):
        """文件加载完成后的处理"""
        self.__hide_loading_animation()

        # 更新UI数据
        self.data = data
        self.columns = columns
        self.file_path = self.file_path_label.text()

        # 更新列映射下拉框
        self.__update_column_combos()

        # 尝试自动映射列
        self.__auto_map_columns()

        # 更新预览
        self.__update_preview()

        # 更新UI状态
        self.__update_ui_state()

        # 显示成功消息
        config = NotificationConfig(
            title=get_content_name_async(
                "import_student_name", "file_loaded_notification_title"
            ),
            content=get_content_name_async(
                "import_student_name", "file_loaded_notification_content"
            ),
            duration=3000,
        )
        show_notification(NotificationType.SUCCESS, config, parent=self)

    def __on_file_load_error(self, error_msg):
        """文件加载失败后的处理"""
        # 隐藏加载动画
        self.__hide_loading_animation()

        # 显示错误消息
        config = NotificationConfig(
            title=get_content_name_async(
                "import_student_name", "load_failed_notification_title"
            ),
            content=get_content_name_async(
                "import_student_name", "load_failed_notification_content"
            )
            + f": {error_msg}",
            duration=3000,
        )
        show_notification(NotificationType.ERROR, config, parent=self)

    def __load_file(self, file_path: str):
        """加载文件 - 在后台线程中执行"""
        try:
            # 根据文件扩展名选择加载方法
            file_ext = os.path.splitext(file_path)[1].lower()
            data = None

            if file_ext in [".xlsx", ".xls"]:
                # 延迟导入 pandas，避免在模块导入时加载大型 C 扩展
                import pandas as pd

                # 加载Excel文件，使用更高效的引擎
                data = pd.read_excel(
                    file_path, engine="openpyxl" if file_ext == ".xlsx" else "xlrd"
                )
            elif file_ext == ".csv":
                # 延迟导入 pandas，避免在模块导入时加载大型 C 扩展
                import pandas as pd

                # 加载CSV文件，使用更高效的参数
                data = pd.read_csv(file_path, engine="c", low_memory=False)
            else:
                raise ValueError(
                    get_content_name_async("import_student_name", "unsupported_format")
                )

            # 将列名转换为字符串，避免整数列名与UI层字符串不匹配
            data.columns = [str(col) for col in data.columns]

            # 获取列名
            columns = list(data.columns)

            # 通过信号通知UI线程文件加载完成
            self.fileLoaded.emit(data, columns)

        except Exception as e:
            if isinstance(
                e,
                (ValueError, FileNotFoundError, UnicodeDecodeError, ImportError),
            ):
                logger.warning(f"加载文件失败: {e}")
            else:
                logger.exception(f"加载文件失败: {e}")
            # 通过信号通知UI线程文件加载失败
            self.fileLoadError.emit(str(e))

    def __update_column_combos(self):
        """更新列映射下拉框"""
        # 清空现有选项
        self.name_column_combo.clear()
        self.id_column_combo.clear()
        self.gender_column_combo.clear()
        self.group_column_combo.clear()
        self.tags_column_combo.clear()

        # 为所有列添加"无"选项
        none_text = get_content_name_async("import_student_name", "column_mapping_none")
        self.gender_column_combo.addItem(none_text)
        self.group_column_combo.addItem(none_text)
        self.tags_column_combo.addItem(none_text)

        # 添加所有列
        for column in self.columns:
            column_text = str(column)
            self.name_column_combo.addItem(column_text)
            self.id_column_combo.addItem(column_text)
            self.gender_column_combo.addItem(column_text)
            self.group_column_combo.addItem(column_text)
            self.tags_column_combo.addItem(column_text)

    def __auto_map_columns(self):
        """自动映射列"""
        # 学号列可能的关键词（优先级从高到低）
        id_keywords = [
            "学号",
            "学籍号",
            "学籍番号",
            "学生番号",
            "学生ID",
            "student id",
            "student_id",
            "student number",
            "student no",
            "roll no",
            "roll number",
            "学员编号",
            "编号",
            "座号",
            "座位号",
            "出席番号",
            "sid",
            "id",
            "no",
        ]

        # 姓名列可能的关键词（优先级从高到低）
        name_keywords = [
            "姓名",
            "学生姓名",
            "名字",
            "氏名",
            "名前",
            "生徒名",
            "学生名",
            "student name",
            "student_name",
            "full name",
            "fullname",
            "名",
            "name",
        ]

        # 性别列可能的关键词（优先级从高到低）
        gender_keywords = [
            "性别",
            "性別",
            "男女",
            "ジェンダー",
            "gender",
            "sex",
            "male",
            "female",
            "m/f",
            "男",
            "女",
        ]

        # 小组列可能的关键词（优先级从高到低）
        group_keywords = [
            "小组",
            "分组",
            "组别",
            "队伍",
            "班",
            "组",
            "グループ",
            "グループ名",
            "チーム",
            "team",
            "team name",
            "group",
            "group name",
        ]

        # 标签列可能的关键词（优先级从高到低）
        tags_keywords = [
            "标签",
            "tag",
            "tags",
            "タグ",
            "ラベル",
            "分類",
            "类别",
            "分类",
            "カテゴリ",
            "カテゴリー",
            "category",
        ]

        # 使用更精确的匹配方法
        def find_best_match(keywords, columns):
            """找到最佳匹配的列"""
            best_match = None
            best_score = 0

            for column in columns:
                column_lower = str(column).lower()
                for i, keyword in enumerate(keywords):
                    # 完全匹配得分最高
                    if column_lower == keyword.lower():
                        score = 100 - i  # 根据关键词优先级计算得分
                        if score > best_score:
                            best_score = score
                            best_match = column
                    # 包含关键词得分次之
                    elif keyword.lower() in column_lower:
                        score = 50 - i  # 根据关键词优先级计算得分
                        if score > best_score:
                            best_score = score
                            best_match = column

            return best_match

        # 自动映射学号列
        id_match = find_best_match(id_keywords, self.columns)
        if id_match:
            index = self.id_column_combo.findText(id_match)
            if index >= 0:
                self.id_column_combo.setCurrentIndex(index)

        # 自动映射姓名列
        name_match = find_best_match(name_keywords, self.columns)
        if name_match:
            index = self.name_column_combo.findText(name_match)
            if index >= 0:
                self.name_column_combo.setCurrentIndex(index)

        # 自动映射性别列
        gender_match = find_best_match(gender_keywords, self.columns)
        if gender_match:
            index = self.gender_column_combo.findText(gender_match)
            if index >= 0:
                self.gender_column_combo.setCurrentIndex(index)

        # 自动映射小组列
        group_match = find_best_match(group_keywords, self.columns)
        if group_match:
            index = self.group_column_combo.findText(group_match)
            if index >= 0:
                self.group_column_combo.setCurrentIndex(index)

        tags_match = find_best_match(tags_keywords, self.columns)
        if tags_match:
            index = self.tags_column_combo.findText(tags_match)
            if index >= 0:
                self.tags_column_combo.setCurrentIndex(index)

    def __update_preview(self):
        """更新预览"""
        if self.data is None:
            return

        id_column = self.id_column_combo.currentText()
        name_column = self.name_column_combo.currentText()
        gender_column = self.gender_column_combo.currentText()
        group_column = self.group_column_combo.currentText()
        tags_column = self.tags_column_combo.currentText()

        # 检查是否选择了"无"选项（空字符串）
        if not id_column and not name_column:
            return
        # 如果选择了"无"选项，将其设为None
        if id_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            id_column = None
        if name_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            name_column = None
        if gender_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            gender_column = None
        if group_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            group_column = None
        if tags_column == get_content_name_async(
            "import_student_name", "column_mapping_none"
        ):
            tags_column = None

        if not id_column and not name_column:
            return

        # 创建预览数据
        preview_columns = []
        preview_headers = []

        # 按照顺序添加列：学号、姓名、性别、小组
        if id_column:
            preview_columns.append(id_column)
            preview_headers.append(
                get_content_name_async("import_student_name", "student_id")
            )

        if name_column:
            preview_columns.append(name_column)
            preview_headers.append(
                get_content_name_async("import_student_name", "name")
            )

        if gender_column:
            preview_columns.append(gender_column)
            preview_headers.append(
                get_content_name_async("import_student_name", "gender")
            )

        if group_column:
            preview_columns.append(group_column)
            preview_headers.append(
                get_content_name_async("import_student_name", "group")
            )

        if tags_column:
            preview_columns.append(tags_column)
            preview_headers.append(
                get_content_name_async("import_student_name", "tags")
            )

        # 限制预览行数
        max_rows = min(3, len(self.data))
        preview_df = self.data[preview_columns].head(max_rows).reset_index(drop=True)

        # 更新表格
        self.preview_table.setRowCount(max_rows)
        self.preview_table.setColumnCount(len(preview_columns))
        self.preview_table.setHorizontalHeaderLabels(preview_headers)

        # 填充数据
        for i in range(max_rows):
            for j, column in enumerate(preview_columns):
                item = QTableWidgetItem(str(preview_df.iloc[i, j]))
                self.preview_table.setItem(i, j, item)

        # 调整列宽
        self.preview_table.resizeColumnsToContents()

    def __import_data(self):
        """导入数据"""
        try:
            id_column = self.id_column_combo.currentText()
            name_column = self.name_column_combo.currentText()
            gender_column = self.gender_column_combo.currentText()
            group_column = self.group_column_combo.currentText()
            tags_column = self.tags_column_combo.currentText()

            none_text = get_content_name_async(
                "import_student_name", "column_mapping_none"
            )
            if id_column == none_text:
                id_column = None
            if name_column == none_text:
                name_column = None

            # 验证必选项：学号和姓名列都必须选择
            if not id_column:
                raise ValueError(
                    get_content_name_async("import_student_name", "no_id_column")
                )

            if not name_column:
                raise ValueError(
                    get_content_name_async("import_student_name", "no_name_column")
                )

            # 检查是否选择了"无"选项（空字符串）
            if gender_column == none_text:
                gender_column = None
            if group_column == none_text:
                group_column = None
            if tags_column == none_text:
                tags_column = None

            # 提取数据
            student_data = []
            for _, row in self.data.iterrows():
                if tags_column:
                    raw_tags = str(row[tags_column]).strip()
                    if raw_tags.lower() == "nan":
                        raw_tags = ""
                else:
                    raw_tags = ""
                for sep in ["，", ",", "；", ";", "|", "/", "\\", "\n", "\t"]:
                    raw_tags = raw_tags.replace(sep, " ")
                tags = []
                for item in raw_tags.split(" "):
                    item = item.strip()
                    if item and item not in tags:
                        tags.append(item)
                student_info = {
                    "id": str(row[id_column]).strip(),
                    "name": str(row[name_column]).strip(),
                    "gender": str(row[gender_column]).strip() if gender_column else "",
                    "group": str(row[group_column]).strip() if group_column else "",
                    "tags": tags,
                    "exist": True,
                }

                # 验证姓名不为空
                if student_info["name"]:
                    student_data.append(student_info)

            duplicate_names = get_duplicate_names(
                [item["name"] for item in student_data]
            )
            if duplicate_names:
                dialog = Dialog(
                    get_content_name_async(
                        "import_student_name", "duplicate_names_title"
                    ),
                    get_content_name_async(
                        "import_student_name", "duplicate_names_message"
                    ).format(
                        count=len(duplicate_names),
                        names="\n".join(duplicate_names),
                    ),
                    self,
                )
                dialog.yesButton.setText(
                    get_content_name_async(
                        "import_student_name", "duplicate_names_rename_button"
                    )
                )
                dialog.cancelButton.setText(
                    get_content_name_async(
                        "import_student_name", "duplicate_names_edit_button"
                    )
                )
                if dialog.exec():
                    unique_names, _ = make_unique_names(
                        [item["name"] for item in student_data]
                    )
                    for i, item in enumerate(student_data):
                        item["name"] = unique_names[i]
                else:
                    return

            # 获取班级名称并进行有效性检查
            class_name = readme_settings_async("roll_call_list", "select_class_name")
            self.__save_student_data(class_name, student_data)

            # 显示成功消息
            config = NotificationConfig(
                title=get_content_name_async(
                    "import_student_name", "import_success_notification_title"
                ),
                content=get_content_name_async(
                    "import_student_name",
                    "import_success_notification_content_template",
                ).format(count=len(student_data), class_name=class_name),
                duration=3000,
            )
            show_notification(NotificationType.SUCCESS, config, parent=self)

        except Exception as e:
            # 显示错误消息
            config = NotificationConfig(
                title=get_content_name_async(
                    "import_student_name", "import_failed_notification_title"
                ),
                content=f"{get_content_name_async('import_student_name', 'import_failed_notification_content')}: {str(e)}",
                duration=3000,
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.exception(f"导入数据失败: {e}")

    def __save_student_data(self, class_name: str, student_data: List[Dict[str, Any]]):
        """保存学生数据到班级名单文件"""
        # 确保班级名单目录存在
        roll_call_list_dir = get_data_path("list", "roll_call_list")
        roll_call_list_dir.mkdir(parents=True, exist_ok=True)

        # 创建班级名单文件路径
        class_file = roll_call_list_dir / f"{class_name}.json"

        # 如果文件已存在，读取现有数据
        existing_data = {}
        if class_file.exists():
            with open_file(class_file, "r", encoding="utf-8") as f:
                existing_data = json.load(f)

        # 如果有现有数据，让用户选择处理方式
        if existing_data:
            # 创建选择对话框
            dialog = MessageBox(
                get_content_name_async("import_student_name", "existing_data_title"),
                get_content_name_async(
                    "import_student_name", "existing_data_prompt"
                ).format(class_name=class_name, count=len(existing_data)),
                self,
            )

            dialog.yesButton.setText(
                get_content_name_async(
                    "import_student_name", "existing_data_option_overwrite"
                )
            )
            dialog.cancelButton.setText(
                get_content_name_async(
                    "import_student_name", "existing_data_option_cancel"
                )
            )

            # 显示对话框并获取用户选择
            if dialog.exec():
                all_students = {}
                for student in student_data:
                    all_students[student["name"]] = {
                        "id": int(student["id"])
                        if str(student["id"]).isdigit()
                        else student["id"],
                        "gender": student["gender"] if student["gender"] else "",
                        "group": student["group"] if student["group"] else "",
                        "exist": student.get("exist", True),
                        "tags": student.get("tags", []),
                    }
                action = "overwrite"
            else:
                # 用户取消导入
                return
        else:
            # 没有现有数据，直接保存
            # 将列表结构转换为字典结构，符合学生名单文件格式
            all_students = {}
            for student in student_data:
                # 确保数据格式与现有学生名单文件一致
                all_students[student["name"]] = {
                    "id": int(student["id"])
                    if str(student["id"]).isdigit()
                    else student["id"],
                    "gender": student["gender"] if student["gender"] else "",
                    "group": student["group"] if student["group"] else "",
                    "exist": student.get("exist", True),
                    "tags": student.get("tags", []),
                }
            action = "new"

        # 保存到文件
        with open_file(class_file, "w", encoding="utf-8") as f:
            json.dump(all_students, f, ensure_ascii=False, indent=4)

        if action == "overwrite":
            logger.info(
                f"已覆盖班级 '{class_name}' 的数据，共 {len(all_students)} 名学生"
            )
        else:
            logger.info(f"已保存 {len(all_students)} 名学生到新班级 '{class_name}'")
