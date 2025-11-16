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
from app.tools.history import *

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
        self.batch_size = 30  # 每次加载的行数
        self.current_row = 0  # 当前加载到的行数
        self.total_rows = 0  # 总行数
        self.is_loading = False  # 是否正在加载数据

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

        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("lottery_history_table", "select_pool_name"),
            get_content_description_async("lottery_history_table", "select_pool_name"),
            self.pool_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_reading_mode_mobile_20_filled"),
            get_content_name_async("lottery_history_table", "select_mode"),
            get_content_description_async("lottery_history_table", "select_mode"),
            self.mode_comboBox,
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

        # 重置数据加载状态
        self.current_row = 0
        self.table.setRowCount(0)

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
                # 对于权重列（列索引5），需要特殊处理
                if self.sort_column == 5 and self.current_mode == 0:
                    # 权重列可能包含非数字字符，尝试提取数字部分
                    weight_str = row[self.sort_column]
                    # 移除可能的前导零
                    weight_str = weight_str.lstrip("0")
                    if not weight_str:
                        return 0.0
                    return float(weight_str)
                else:
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
            lottery_file = get_resources_path(
                "list/lottery_list", f"{self.current_pool_name}.json"
            )
            history_file = get_resources_path(
                "history/lottery_history", f"{self.current_pool_name}.json"
            )
            with open_file(lottery_file, "r", encoding="utf-8") as f:
                pool_data = json.load(f)

            cleaned_lotterys = []
            for name, info in pool_data.items():
                if isinstance(info, dict) and info.get("exist", True):
                    cleaned_lotterys.append(
                        (
                            info.get("id", ""),
                            name,
                            info.get("weight", ""),
                        )
                    )

            history_data = {}
            if file_exists(history_file):
                try:
                    with open_file(history_file, "r", encoding="utf-8") as f:
                        history_data = json.load(f)
                except json.JSONDecodeError:
                    pass

            max_id_length = (
                max(len(str(lottery[0])) for lottery in cleaned_lotterys)
                if cleaned_lotterys
                else 0
            )
            max_total_count_length = (
                max(
                    len(
                        str(
                            history_data.get("lotterys", {})
                            .get(name, {})
                            .get("total_count", 0)
                        )
                    )
                    for _, name, _ in cleaned_lotterys
                )
                if cleaned_lotterys
                else 0
            )

            lotterys_data = []
            for lottery_id, name, weight in cleaned_lotterys:
                total_count = int(
                    history_data.get("lotterys", {}).get(name, {}).get("total_count", 0)
                )
                lotterys_data.append(
                    {
                        "id": str(lottery_id).zfill(max_id_length),
                        "name": name,
                        "weight": weight,
                        "total_count": total_count,
                        "total_count_str": str(total_count).zfill(
                            max_total_count_length
                        ),
                    }
                )

            # 使用权重格式化函数
            format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

            # 根据排序状态对数据进行排序
            if self.sort_column >= 0:
                # 定义排序键函数
                def sort_key(lottery):
                    if self.sort_column == 0:  # 序号
                        return lottery.get("id", "")
                    elif self.sort_column == 1:  # 名称
                        return lottery.get("name", "")
                    elif self.sort_column == 2:  # 总次数
                        return lottery.get("total_count", 0)
                    elif self.sort_column == 3:  # 权重
                        return lottery.get("weight", "")
                    return ""

                # 应用排序
                reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                lotterys_data.sort(key=sort_key, reverse=reverse_order)

            # 计算本次加载的行范围
            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            # 填充表格数据
            for i in range(start_row, end_row):
                if i >= len(lotterys_data):
                    break

                lottery = lotterys_data[i]
                row = i

                # 序号
                id_item = create_table_item(lottery.get("id", str(row + 1)))
                self.table.setItem(row, 0, id_item)

                # 名称
                name_item = create_table_item(lottery.get("name", ""))
                self.table.setItem(row, 1, name_item)

                # 总次数
                total_count_item = create_table_item(
                    str(lottery.get("total_count_str", lottery.get("total_count", 0)))
                )
                self.table.setItem(row, 2, total_count_item)

                # 权重
                weight_item = create_table_item(format_weight(lottery.get("weight", 0)))
                self.table.setItem(row, 3, weight_item)

            # 更新当前行数
            self.current_row = end_row

        except Exception as e:
            logger.error(f"加载奖品数据失败: {e}")
            Dialog("错误", f"加载奖品数据失败: {e}", self).exec()

    def _load_more_sessions_data(self):
        """加载更多会话数据"""
        if not self.current_pool_name:
            return
        try:
            lottery_file = get_resources_path(
                "list/lottery_list", f"{self.current_pool_name}.json"
            )
            history_file = get_resources_path(
                "history/lottery_history", f"{self.current_pool_name}.json"
            )
            with open_file(lottery_file, "r", encoding="utf-8") as f:
                pool_data = json.load(f)

            cleaned_lotterys = []
            for name, info in pool_data.items():
                if isinstance(info, dict) and info.get("exist", True):
                    cleaned_lotterys.append(
                        (info.get("id", ""), name, info.get("weight", ""))
                    )

            history_data = {}
            if file_exists(history_file):
                try:
                    with open_file(history_file, "r", encoding="utf-8") as f:
                        history_data = json.load(f)
                except json.JSONDecodeError:
                    pass

            max_id_length = (
                max(len(str(lottery[0])) for lottery in cleaned_lotterys)
                if cleaned_lotterys
                else 0
            )

            lotterys_data = []
            for lottery_id, name, weight in cleaned_lotterys:
                time_records = (
                    history_data.get("lotterys", {}).get(name, {}).get("history", [{}])
                )
                for record in time_records:
                    draw_time = record.get("draw_time", "")
                    if draw_time:
                        lotterys_data.append(
                            {
                                "draw_time": draw_time,
                                "id": str(lottery_id).zfill(max_id_length),
                                "name": name,
                                "weight": record.get("weight", ""),
                            }
                        )

            # 使用权重格式化函数
            format_weight, _, _ = format_weight_for_display(lotterys_data, "weight")

            # 根据排序状态对数据进行排序
            if self.sort_column >= 0:
                # 定义排序键函数
                def sort_key(lottery):
                    if self.sort_column == 0:  # 时间
                        return lottery.get("draw_time", "")
                    elif self.sort_column == 1:  # 序号
                        return lottery.get("id", "")
                    elif self.sort_column == 2:  # 名称
                        return lottery.get("name", "")
                    elif self.sort_column == 3:  # 权重
                        return lottery.get("weight", "")
                    return ""

                # 应用排序
                reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                lotterys_data.sort(key=sort_key, reverse=reverse_order)
            else:
                # 默认按时间降序排序
                lotterys_data.sort(key=lambda x: x.get("draw_time", ""), reverse=True)

            # 计算本次加载的行范围
            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            # 填充表格数据
            for i in range(start_row, end_row):
                if i >= len(lotterys_data):
                    break

                lottery = lotterys_data[i]
                row = i

                # 时间
                draw_time_item = create_table_item(lottery.get("draw_time", ""))
                self.table.setItem(row, 0, draw_time_item)

                # 序号
                id_item = create_table_item(lottery.get("id", str(row + 1)))
                self.table.setItem(row, 1, id_item)

                # 名称
                name_item = create_table_item(lottery.get("name", ""))
                self.table.setItem(row, 2, name_item)

                # 权重
                weight_item = create_table_item(format_weight(lottery.get("weight", 0)))
                self.table.setItem(row, 3, weight_item)

            # 更新当前行数
            self.current_row = end_row

        except Exception as e:
            logger.error(f"加载会话数据失败: {e}")
            Dialog("错误", f"加载会话数据失败: {e}", self).exec()

    def _load_more_stats_data(self, lottery_name):
        """加载更多统计数据"""
        if not self.current_pool_name:
            return
        try:
            lottery_file = get_resources_path(
                "list/lottery_list", f"{self.current_pool_name}.json"
            )
            history_file = get_resources_path(
                "history/lottery_history", f"{self.current_pool_name}.json"
            )
            with open_file(lottery_file, "r", encoding="utf-8") as f:
                pool_data = json.load(f)

            cleaned_lotterys = []
            for name, info in pool_data.items():
                if (
                    isinstance(info, dict)
                    and info.get("exist", True)
                    and name == lottery_name
                ):
                    cleaned_lotterys.append(
                        (info.get("id", ""), name, info.get("weight", ""))
                    )

            history_data = {}
            if file_exists(history_file):
                try:
                    with open_file(history_file, "r", encoding="utf-8") as f:
                        history_data = json.load(f)
                except json.JSONDecodeError:
                    pass

            lotterys_data = []
            for lottery_id, name, weight in cleaned_lotterys:
                time_records = (
                    history_data.get("lotterys", {}).get(name, {}).get("history", [{}])
                )
                for record in time_records:
                    draw_time = record.get("draw_time", "")
                    if draw_time:
                        lotterys_data.append(
                            {
                                "draw_time": draw_time,
                                "draw_method": str(record.get("draw_method", "")),
                                "draw_lottery_numbers": str(
                                    record.get("draw_lottery_numbers", 0)
                                ),
                                "weight": record.get("weight", ""),
                            }
                        )

            max_weight_length = max(
                len(str(lottery.get("weight", ""))) for lottery in lotterys_data
            )

            # 根据排序状态对数据进行排序
            if self.sort_column >= 0:
                # 定义排序键函数
                def sort_key(lottery):
                    if self.sort_column == 0:  # 时间
                        return lottery.get("draw_time", "")
                    elif self.sort_column == 1:  # 模式
                        return str(lottery.get("draw_method", ""))
                    elif self.sort_column == 2:  # 数量
                        return int(lottery.get("draw_lottery_numbers", 0))
                    elif self.sort_column == 3:  # 权重
                        return float(lottery.get("weight", ""))
                    return ""

                # 应用排序
                reverse_order = self.sort_order == Qt.SortOrder.DescendingOrder
                lotterys_data.sort(key=sort_key, reverse=reverse_order)
            else:
                # 默认按时间降序排序
                lotterys_data.sort(key=lambda x: x.get("draw_time", ""), reverse=True)

            # 计算本次加载的行范围
            start_row = self.current_row
            end_row = min(start_row + self.batch_size, self.total_rows)

            # 填充表格数据
            for i in range(start_row, end_row):
                if i >= len(lotterys_data):
                    break

                lottery = lotterys_data[i]
                row = i

                # 时间
                time_item = create_table_item(lottery.get("draw_time", ""))
                self.table.setItem(row, 0, time_item)

                # 模式
                mode_item = create_table_item(lottery.get("draw_method", ""))
                self.table.setItem(row, 1, mode_item)

                # 数量
                draw_lottery_numbers_item = create_table_item(
                    str(lottery.get("draw_lottery_numbers", 0))
                )
                self.table.setItem(row, 2, draw_lottery_numbers_item)

                # 权重
                weight_item = create_table_item(
                    str(lottery.get("weight", "")).zfill(max_weight_length)
                )
                self.table.setItem(row, 3, weight_item)

            # 更新当前行数
            self.current_row = end_row

        except Exception as e:
            logger.error(f"加载统计数据失败: {e}")
            Dialog("错误", f"加载统计数据失败: {e}", self).exec()

    def setup_file_watcher(self):
        """设置文件系统监视器，监控奖池历史记录文件夹的变化"""
        lottery_history_dir = get_path("app/resources/history/lottery_history")
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
        self.update_table_headers()

        # 重置数据加载状态
        self.current_row = 0
        self.is_loading = False
        self.table.setRowCount(0)
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
                        initial_rows = min(self.batch_size, self.total_rows)
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_lotterys_data()
                elif self.current_mode == 1:
                    # 获取会话记录数量
                    sessions_count = get_draw_sessions_history("lottery", pool_name)
                    if sessions_count:
                        self.total_rows = sessions_count
                        # 设置初始行数为批次大小或总行数，取较小值
                        initial_rows = min(self.batch_size, self.total_rows)
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_sessions_data()
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
                        initial_rows = min(self.batch_size, self.total_rows)
                        self.table.setRowCount(initial_rows)
                        # 加载第一批数据
                        self._load_more_stats_data(self.current_lottery_name)
                return

            self.current_mode = self.mode_comboBox.currentIndex()
            if self.current_mode == 0:
                # 获取奖品记录数量
                lotterys_count = get_name_history("lottery", pool_name)
                if lotterys_count:
                    self.total_rows = lotterys_count
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = min(self.batch_size, self.total_rows)
                    self.table.setRowCount(initial_rows)
                    # 加载第一批数据
                    self._load_more_lotterys_data()
            elif self.current_mode == 1:
                # 获取会话记录数量
                sessions_count = get_draw_sessions_history("lottery", pool_name)
                if sessions_count:
                    self.total_rows = sessions_count
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = min(self.batch_size, self.total_rows)
                    self.table.setRowCount(initial_rows)
                    # 加载第一批数据
                    self._load_more_sessions_data()
            else:
                # 获取个人统计记录数量
                stats_count = get_individual_statistics(
                    "lottery", pool_name, self.mode_comboBox.currentText()
                )
                if stats_count:
                    self.total_rows = stats_count
                    # 设置初始行数为批次大小或总行数，取较小值
                    initial_rows = min(self.batch_size, self.total_rows)
                    self.table.setRowCount(initial_rows)
                    self.current_lottery_name = self.mode_comboBox.currentText()
                    # 加载第一批数据
                    self._load_more_stats_data(self.current_lottery_name)

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
            logger.error(f"刷新表格数据失败: {str(e)}")
        finally:
            self.table.blockSignals(False)

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
            self.table.setColumnCount(len(headers))
            self.table.setHorizontalHeaderLabels(headers)
        elif self.current_mode == 1:
            headers = get_content_name_async(
                "lottery_history_table", "HeaderLabels_time_weight"
            )
            self.table.setColumnCount(len(headers))
            self.table.setHorizontalHeaderLabels(headers)
        else:
            headers = get_content_name_async(
                "lottery_history_table", "HeaderLabels_Individual_weight"
            )
            self.table.setColumnCount(len(headers))
            self.table.setHorizontalHeaderLabels(headers)
