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
from app.tools.config import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from app.common.history import *
from app.common.history.history_reader import (
    get_lottery_pool_list,
    get_lottery_history_data,
    get_lottery_prizes_data,
    get_lottery_session_data,
    get_lottery_prize_stats_data,
)
from .export_utils import export_history_table_data


# ==================================================
# 点名历史记录表格
# ==================================================
class lottery_history_table(GroupHeaderCardWidget):
    """点名历史记录表格卡片"""

    refresh_signal = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent = parent
        self.setTitle(get_content_name_async("lottery_history_table", "title"))
        self.setBorderRadius(8)

        # 初始化数据加载器
        pool_history = get_all_history_names("lottery")
        self.data_loader = None
        self.current_pool_name = pool_history[0] if pool_history else ""
        self.current_mode = 0
        self.current_subject = ""  # 当前选择的课程
        self.batch_size = 30  # 每次加载的行数
        self.current_row = 0  # 当前加载到的行数
        self.total_rows = 0  # 总行数
        self.is_loading = False  # 是否正在加载数据
        self.has_class_record = False  # 是否有课程记录
        self.available_subjects = []  # 可用的课程列表
        self.cached_lotterys_data = []  # 缓存的奖品数据列表
        self.cached_sessions_data = []  # 缓存的会话数据列表
        self.cached_stats_data = []  # 缓存的统计数据列表
        self.force_load_all = False

        # 创建奖池选择区域
        QTimer.singleShot(APPLY_DELAY, self.create_pool_selection)

        # 创建表格区域
        QTimer.singleShot(APPLY_DELAY, self.create_table)

        # 初始化奖池列表
        QTimer.singleShot(APPLY_DELAY, self.refresh_pool_history)

        # 设置文件系统监视器
        QTimer.singleShot(APPLY_DELAY, self.setup_file_watcher)

        # 初始化数据
        QTimer.singleShot(APPLY_DELAY, self.refresh_data)

        # 连接信号
        self.refresh_signal.connect(self.refresh_data)

    def create_pool_selection(self):
        """创建奖池选择区域"""
        self.pool_comboBox = ComboBox()

        # 获取奖池历史列表并填充下拉框
        pool_history = get_all_history_names("lottery")
        self.pool_comboBox.addItems(pool_history)

        # 设置默认选择
        if pool_history:
            saved_index = readme_settings_async(
                "lottery_history_table", "select_pool_name"
            )
            self.pool_comboBox.setCurrentIndex(0)
            self.current_pool_name = pool_history[0]
        else:
            # 如果没有奖池历史，设置占位符
            self.pool_comboBox.setCurrentIndex(-1)
            self.pool_comboBox.setPlaceholderText(
                get_content_name_async("lottery_history_table", "select_pool_name")
            )
            self.current_pool_name = ""

        self.pool_comboBox.currentIndexChanged.connect(self.on_pool_changed)
        self.pool_comboBox.currentTextChanged.connect(lambda: self.on_pool_changed(-1))

        # 选择查看模式
        self.all_names = get_all_names("lottery", self.pool_comboBox.currentText())
        self.mode_comboBox = ComboBox()
        self.mode_comboBox.addItems(
            get_content_combo_name_async("lottery_history_table", "select_mode")
            + self.all_names
        )
        self.mode_comboBox.setCurrentIndex(0)
        self.mode_comboBox.currentIndexChanged.connect(self.refresh_data)

        # 选择课程
        self.subject_comboBox = ComboBox()
        self.subject_comboBox.addItems(
            get_content_combo_name_async("lottery_history_table", "select_subject")
        )
        self.subject_comboBox.setCurrentIndex(0)
        self.subject_comboBox.currentIndexChanged.connect(self.on_subject_changed)

        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("lottery_history_table", "select_pool_name"),
            get_content_description_async("lottery_history_table", "select_pool_name"),
            self.pool_comboBox,
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
            get_content_name_async("lottery_history_table", "select_mode"),
            get_content_description_async("lottery_history_table", "select_mode"),
            self.mode_subject_widget,
        )

        # 创建导出按钮
        self.export_button = PushButton(
            get_content_name_async("lottery_history_table", "export_button")
        )
        self.export_button.setFixedWidth(120)
        self.export_button.clicked.connect(self.export_history_data)

        self.addGroup(
            get_theme_icon("ic_fluent_document_arrow_down_20_filled"),
            get_content_name_async("lottery_history_table", "export"),
            get_content_description_async("lottery_history_table", "export"),
            self.export_button,
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

        page_step = self.table.verticalScrollBar().pageStep()
        trigger_distance = max(page_step * 2, max_value * 0.3)
        if current_value >= max_value - trigger_distance:
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
        self.cached_lotterys_data = []
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
                # 在模式0（奖品数据）中：没有课程列
                #   - 权重列索引为3
                # 在模式1（会话数据）中：
                #   - 有课程时：权重列索引为4
                #   - 无课程时：权重列索引为3
                # 在模式2（统计数据）中：
                #   - 有课程时：权重列索引为4
                #   - 无课程时：权重列索引为3
                if self.current_mode == 0:
                    # 模式0：奖品数据，权重列索引为3
                    if self.sort_column == 3:
                        weight_str = row[self.sort_column]
                        weight_str = weight_str.lstrip("0")
                        if not weight_str:
                            return 0.0
                        return float(weight_str)
                elif self.current_mode in [1, 2]:
                    # 模式1和2：会话数据和统计数据
                    if self.sort_column == 3:
                        # 列3可能是课程或权重
                        if self.has_class_record:
                            # 有课程，列3是课程，列4是权重
                            return row[self.sort_column]
                        else:
                            # 无课程，列3是权重
                            weight_str = row[self.sort_column]
                            weight_str = weight_str.lstrip("0")
                            if not weight_str:
                                return 0.0
                            return float(weight_str)
                    elif self.sort_column == 4 and self.has_class_record:
                        # 有课程时，列4是权重
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
            self._load_more_lotterys_data()
        elif self.current_mode == 1:
            self._load_more_sessions_data()
        else:
            # 当模式值大于等于2时，表示选择了特定的奖品名称
            if hasattr(self, "mode_comboBox"):
                self.current_lottery_name = self.mode_comboBox.currentText()
            else:
                # 如果没有mode_comboBox，从设置中获取奖品名称
                self.current_lottery_name = readme_settings_async(
                    "lottery_history_table", "select_lottery_name"
                )
            self._load_more_stats_data(self.current_lottery_name)

        # 数据加载完成后启用排序
        if self.current_row >= self.total_rows:
            self.table.setSortingEnabled(True)
            if self.sort_column >= 0:
                self.table.horizontalHeader().setSortIndicator(
                    self.sort_column, self.sort_order
                )
                self.table.horizontalHeader().setSortIndicatorShown(True)

        self.is_loading = False

    def _load_more_lotterys_data(self):
        """加载更多奖品数据"""
        if not self.current_pool_name:
            return
        try:
            # 如果是第一次加载（current_row == 0），获取并排序数据
            if self.current_row == 0:
                cleaned_lotterys = get_lottery_pool_list(self.current_pool_name)
                history_data = get_lottery_history_data(self.current_pool_name)

                lotterys_data = get_lottery_prizes_data(cleaned_lotterys, history_data)

                format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

                if self.sort_column >= 0:

                    def sort_key(lottery):
                        if self.sort_column == 0:
                            return lottery.get("id", "")
                        elif self.sort_column == 1:
                            return lottery.get("name", "")
                        elif self.sort_column == 2:
                            return lottery.get("total_count", 0)
                        elif self.sort_column == 3:
                            return lottery.get("weight", "")
                        return ""

                    reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                    lotterys_data.sort(key=sort_key, reverse=reverse_order)

                # 缓存排序后的数据
                self.cached_lotterys_data = lotterys_data
                self.cached_lotterys_format_weight = format_weight
            else:
                # 使用缓存的数据
                lotterys_data = self.cached_lotterys_data
                format_weight = self.cached_lotterys_format_weight

            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            for i in range(start_row, end_row):
                if i >= len(lotterys_data):
                    break

                lottery = lotterys_data[i]
                row = i

                id_item = create_table_item(lottery.get("id", str(row + 1)))
                self.table.setItem(row, 0, id_item)

                name_item = create_table_item(lottery.get("name", ""))
                self.table.setItem(row, 1, name_item)

                total_count_item = create_table_item(
                    str(lottery.get("total_count_str", lottery.get("total_count", 0)))
                )
                self.table.setItem(row, 2, total_count_item)

                weight_item = create_table_item(format_weight(lottery.get("weight", 0)))
                self.table.setItem(row, 3, weight_item)

            self.current_row = end_row

        except Exception as e:
            logger.exception(f"加载奖品数据失败: {e}")
            Dialog("错误", f"加载奖品数据失败: {e}", self).exec()

    def _load_more_sessions_data(self):
        """加载更多会话数据"""
        if not self.current_pool_name:
            return
        try:
            # 如果是第一次加载（current_row == 0），获取并排序数据
            if self.current_row == 0:
                cleaned_lotterys = get_lottery_pool_list(self.current_pool_name)
                history_data = get_lottery_history_data(self.current_pool_name)

                lotterys_data = get_lottery_session_data(
                    cleaned_lotterys, history_data, self.current_subject
                )

                self.has_class_record = any(
                    lottery.get("class_name", "") for lottery in lotterys_data
                )

                self.update_table_headers()

                format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

                if self.sort_column >= 0:

                    def sort_key(lottery):
                        if self.sort_column == 0:
                            return lottery.get("draw_time", "")
                        elif self.sort_column == 1:
                            return lottery.get("id", "")
                        elif self.sort_column == 2:
                            return lottery.get("name", "")
                        elif self.sort_column == 3:
                            return lottery.get("class_name", "")
                        elif self.sort_column == 4:
                            return lottery.get("weight", "")
                        return ""

                    reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                    lotterys_data.sort(key=sort_key, reverse=reverse_order)
                else:
                    lotterys_data.sort(
                        key=lambda x: x.get("draw_time", ""), reverse=True
                    )

                # 缓存排序后的数据
                self.cached_sessions_data = lotterys_data
                self.cached_sessions_format_weight = format_weight
            else:
                # 使用缓存的数据
                lotterys_data = self.cached_sessions_data
                format_weight = self.cached_sessions_format_weight

            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            for i in range(start_row, end_row):
                if i >= len(lotterys_data):
                    break

                lottery = lotterys_data[i]
                row = i

                draw_time_item = create_table_item(lottery.get("draw_time", ""))
                self.table.setItem(row, 0, draw_time_item)

                id_item = create_table_item(lottery.get("id", str(row + 1)))
                self.table.setItem(row, 1, id_item)

                name_item = create_table_item(lottery.get("name", ""))
                self.table.setItem(row, 2, name_item)

                if self.has_class_record:
                    class_name = lottery.get("class_name", "")
                    class_item = create_table_item(
                        str(class_name) if class_name else ""
                    )
                    self.table.setItem(row, 3, class_item)
                    col = 4
                else:
                    col = 3

                weight_item = create_table_item(format_weight(lottery.get("weight", 0)))
                self.table.setItem(row, col, weight_item)

            self.current_row = end_row

        except Exception as e:
            logger.exception(f"加载会话数据失败: {e}")
            Dialog("错误", f"加载会话数据失败: {e}", self).exec()

    def _load_more_stats_data(self, lottery_name):
        """加载更多统计数据"""
        if not self.current_pool_name:
            return
        try:
            # 如果是第一次加载（current_row == 0），获取并排序数据
            if self.current_row == 0:
                cleaned_lotterys = get_lottery_pool_list(self.current_pool_name)
                history_data = get_lottery_history_data(self.current_pool_name)

                lotterys_data = get_lottery_prize_stats_data(
                    cleaned_lotterys, history_data, lottery_name, self.current_subject
                )

                self.has_class_record = any(
                    lottery.get("class_name", "") for lottery in lotterys_data
                )

                self.update_table_headers()

                format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

                if self.sort_column >= 0:

                    def sort_key(lottery):
                        if self.sort_column == 0:
                            return lottery.get("draw_time", "")
                        elif self.sort_column == 1:
                            return int(lottery.get("draw_lottery_numbers", 0))
                        elif self.sort_column == 2:
                            return lottery.get("class_name", "")
                        elif self.sort_column == 3:
                            return float(lottery.get("weight", ""))
                        return ""

                    reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                    lotterys_data.sort(key=sort_key, reverse=reverse_order)
                else:
                    lotterys_data.sort(
                        key=lambda x: x.get("draw_time", ""), reverse=True
                    )

                # 缓存排序后的数据
                self.cached_stats_data = lotterys_data
                self.cached_stats_format_weight = format_weight
            else:
                # 使用缓存的数据
                lotterys_data = self.cached_stats_data
                format_weight = self.cached_stats_format_weight

            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            for i in range(start_row, end_row):
                if i >= len(lotterys_data):
                    break

                lottery = lotterys_data[i]
                row = i

                time_item = create_table_item(lottery.get("draw_time", ""))
                self.table.setItem(row, 0, time_item)

                draw_lottery_numbers_item = create_table_item(
                    str(lottery.get("draw_lottery_numbers", 0))
                )
                self.table.setItem(row, 1, draw_lottery_numbers_item)

                if self.has_class_record:
                    class_name = lottery.get("class_name", "")
                    class_item = create_table_item(
                        str(class_name) if class_name else ""
                    )
                    self.table.setItem(row, 2, class_item)
                    col = 3
                else:
                    col = 2

                weight_item = create_table_item(
                    format_weight(lottery.get("weight", ""))
                )
                self.table.setItem(row, col, weight_item)

            self.current_row = end_row

        except Exception as e:
            logger.exception(f"加载统计数据失败: {e}")
            Dialog("错误", f"加载统计数据失败: {e}", self).exec()

    def setup_file_watcher(self):
        """设置文件系统监视器，监控奖池历史记录文件夹的变化"""
        lottery_history_dir = get_data_path("history/lottery_history")
        if not lottery_history_dir.exists():
            logger.warning(f"奖池历史记录文件夹不存在: {lottery_history_dir}")
            return
        self.file_watcher = QFileSystemWatcher()
        self.file_watcher.addPath(str(lottery_history_dir))
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)
        # logger.debug(f"已设置文件监视器，监控目录: {lottery_history_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        QTimer.singleShot(1000, self.refresh_pool_history)

    def refresh_pool_history(self):
        """刷新奖池下拉框列表"""
        if not hasattr(self, "pool_comboBox"):
            return

        # 保存当前选择的奖池名称和索引
        current_pool_name = self.pool_comboBox.currentText()
        current_index = self.pool_comboBox.currentIndex()

        # 获取最新的奖池历史列表
        pool_history = get_all_history_names("lottery")

        # 清空并重新填充下拉框
        self.pool_comboBox.clear()
        self.pool_comboBox.addItems(pool_history)

        # 如果之前选择的奖池还在列表中，则重新选择它
        if current_pool_name and current_pool_name in pool_history:
            index = pool_history.index(current_pool_name)
            self.pool_comboBox.setCurrentIndex(index)
            # 更新current_pool_name
            self.current_pool_name = current_pool_name
        elif pool_history and current_index >= 0 and current_index < len(pool_history):
            # 如果之前选择的索引仍然有效，使用相同的索引
            self.pool_comboBox.setCurrentIndex(current_index)
            # 更新current_pool_name
            self.current_pool_name = pool_history[current_index]
        elif pool_history:
            # 如果之前选择的奖池不在列表中，选择第一个奖池
            self.pool_comboBox.setCurrentIndex(0)
            # 更新current_pool_name
            self.current_pool_name = pool_history[0]
        else:
            # 如果没有奖池历史，设置占位符
            self.pool_comboBox.setCurrentIndex(-1)
            self.pool_comboBox.setPlaceholderText(
                get_content_name_async("lottery_history_table", "select_pool_name")
            )
            # 更新current_pool_name
            self.current_pool_name = ""

        if hasattr(self, "clear_button"):
            self.clear_button.setEnabled(bool(self.current_pool_name))

    def on_pool_changed(self, index):
        """奖池选择变化时刷新表格数据"""
        if not hasattr(self, "pool_comboBox"):
            return

        # 启用或禁用清除按钮
        if hasattr(self, "clear_button"):
            self.clear_button.setEnabled(self.pool_comboBox.currentIndex() >= 0)

        # 更新当前奖池名称
        self.current_pool_name = self.pool_comboBox.currentText()

        # 刷新表格数据
        self.refresh_data()

    def refresh_data(self):
        """刷新表格数据"""
        if not hasattr(self, "table"):
            return
        if not hasattr(self, "pool_comboBox"):
            return
        pool_name = self.pool_comboBox.currentText()
        if not pool_name:
            self.table.setRowCount(0)
            return
        self.current_pool_name = pool_name

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
                    # 获取奖品记录数量
                    lotterys_count = get_name_history("lottery", pool_name)
                    if lotterys_count:
                        self.total_rows = lotterys_count
                        # 设置初始行数为批次大小或总行数，取较小值
                        initial_rows = (
                            self.total_rows
                            if self.force_load_all
                            else min(self.batch_size, self.total_rows)
                        )
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_lotterys_data()
                elif self.current_mode == 1:
                    # 获取会话记录数量
                    sessions_count = get_draw_sessions_history("lottery", pool_name)
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
                    # 当模式值大于等于2时，从设置中获取奖品名称
                    self.current_lottery_name = readme_settings_async(
                        "lottery_history_table", "select_lottery_name"
                    )
                    # 获取个人统计记录数量
                    stats_count = get_individual_statistics(
                        "lottery", pool_name, self.current_lottery_name
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
                        self._load_more_stats_data(self.current_lottery_name)
                    self._ensure_scrollable_rows()
                return

            self.current_mode = self.mode_comboBox.currentIndex()
            if self.current_mode == 0:
                # 获取奖品记录数量
                lotterys_count = get_name_history("lottery", pool_name)
                if lotterys_count:
                    self.total_rows = lotterys_count
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = (
                        self.total_rows
                        if self.force_load_all
                        else min(self.batch_size, self.total_rows)
                    )
                    self.table.setRowCount(initial_rows)
                    # 加载第一批数据
                    self._load_more_lotterys_data()
                    self._ensure_scrollable_rows()
            elif self.current_mode == 1:
                # 获取会话记录数量
                sessions_count = get_draw_sessions_history("lottery", pool_name)
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
                # 获取个人统计记录数量
                stats_count = get_individual_statistics(
                    "lottery", pool_name, self.mode_comboBox.currentText()
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
                    self.current_lottery_name = self.mode_comboBox.currentText()
                    # 加载第一批数据
                    self._load_more_stats_data(self.current_lottery_name)
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
                "lottery_history_table", "HeaderLabels_all_weight"
            )
        elif self.current_mode == 1:
            headers = get_content_name_async(
                "lottery_history_table", "HeaderLabels_time_weight"
            )
        else:
            headers = get_content_name_async(
                "lottery_history_table", "HeaderLabels_Individual_weight"
            )

        # 如果没有课程记录，移除课程列（在权重列之前）
        if not self.has_class_record and self.current_mode >= 1:
            headers = headers[:-2] + headers[-1:]

        self.table.setColumnCount(len(headers))
        self.table.setHorizontalHeaderLabels(headers)

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
        if not self.current_pool_name:
            return

        try:
            history_file = get_data_path(
                "history/lottery_history", f"{self.current_pool_name}.json"
            )

            if not file_exists(history_file):
                self.available_subjects = []
                return

            with open_file(history_file, "r", encoding="utf-8") as f:
                history_data = json.load(f)

            # 收集所有课程名称
            subjects = set()
            lotterys = history_data.get("lotterys", {})
            for lottery_info in lotterys.values():
                history = lottery_info.get("history", [])
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
                        "lottery_history_table", "select_subject"
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

    def export_history_data(self):
        if not self.current_pool_name:
            return

        if self.current_row < self.total_rows:
            self.force_load_all = True
            self.refresh_data()

        export_history_table_data(
            table_widget=self.table,
            current_mode=self.current_mode,
            i18n_domain="lottery",
            current_name=self.current_pool_name,
            parent_widget=self,
        )
