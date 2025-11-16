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
from app.tools.list import *


# ==================================================
# 抽奖名单表格
# ==================================================
class lottery_table(GroupHeaderCardWidget):
    """抽奖名单表格卡片"""

    refresh_signal = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent = parent
        self.setTitle(get_content_name_async("lottery_table", "title"))
        self.setBorderRadius(8)
        # 创建抽奖名单选择区域
        QTimer.singleShot(APPLY_DELAY, self.create_lottery_selection)

        # 创建表格区域
        QTimer.singleShot(APPLY_DELAY, self.create_table)

        # 初始化抽奖名单列表
        QTimer.singleShot(APPLY_DELAY, self.refresh_lottery_list)

        # 设置文件系统监视器
        QTimer.singleShot(APPLY_DELAY, self.setup_file_watcher)

        # 初始化数据
        QTimer.singleShot(APPLY_DELAY, self.refresh_data)

        # 连接信号
        self.refresh_signal.connect(self.refresh_data)

    def create_lottery_selection(self):
        """创建抽奖名单选择区域"""
        self.lottery_comboBox = ComboBox()
        self.lottery_comboBox.setCurrentIndex(
            readme_settings_async("lottery_table", "select_pool_name")
        )
        if not get_pool_name_list():
            self.lottery_comboBox.setCurrentIndex(-1)
            self.lottery_comboBox.setPlaceholderText(
                get_content_name_async("lottery_table", "select_pool_name")
            )
        self.lottery_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "lottery_table",
                "select_pool_name",
                self.lottery_comboBox.currentIndex(),
            )
        )
        self.lottery_comboBox.currentTextChanged.connect(self.refresh_data)

        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("lottery_table", "select_pool_name"),
            get_content_description_async("lottery_table", "select_pool_name"),
            self.lottery_comboBox,
        )

    def create_table(self):
        """创建表格区域"""
        # 创建表格
        self.table = TableWidget()
        self.table.setBorderVisible(True)
        self.table.setBorderRadius(8)
        self.table.setWordWrap(False)
        self.table.setColumnCount(4)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.DoubleClicked)
        self.table.setSortingEnabled(True)
        self.table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.verticalHeader().hide()

        self.table.setHorizontalHeaderLabels(
            get_content_name_async("lottery_table", "HeaderLabels")
        )
        self.table.horizontalHeader().resizeSection(0, 80)
        # 设置表格属性
        self.table.horizontalHeader().setSectionResizeMode(
            0, QHeaderView.ResizeMode.ResizeToContents
        )
        for i in range(1, 4):
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
        """设置文件系统监视器，监控班级名单文件夹的变化"""
        # 获取抽奖名单文件夹路径
        lottery_list_dir = get_path("app/resources/list/lottery_list")

        # 确保目录存在
        if not lottery_list_dir.exists():
            logger.warning(f"奖池文件夹不存在: {lottery_list_dir}")
            return

        # 创建文件系统监视器
        self.file_watcher = QFileSystemWatcher()

        # 监视目录
        self.file_watcher.addPath(str(lottery_list_dir))

        # 连接信号
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)
        # logger.debug(f"已设置文件监视器，监控目录: {lottery_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成
        QTimer.singleShot(1000, self.refresh_lottery_list)

    def refresh_lottery_list(self):
        """刷新抽奖名单下拉框列表"""
        # 保存当前选中的抽奖名单名称
        current_lottery_name = self.lottery_comboBox.currentText()

        # 获取最新的抽奖名单列表
        lottery_list = get_pool_name_list()

        # 清空并重新添加抽奖名单列表
        self.lottery_comboBox.clear()
        self.lottery_comboBox.addItems(lottery_list)

        # 尝试恢复之前选中的抽奖池
        if current_lottery_name and current_lottery_name in lottery_list:
            index = lottery_list.index(current_lottery_name)
            self.lottery_comboBox.setCurrentIndex(index)
        elif not lottery_list:
            self.lottery_comboBox.setCurrentIndex(-1)
            self.lottery_comboBox.setPlaceholderText(
                get_content_name_async("lottery_list", "select_pool_name")
            )

        # logger.debug(f"抽奖名单列表已刷新，共 {len(lottery_list)} 个抽奖名单")
        # 只有在表格已经创建时才刷新数据
        if hasattr(self, "table"):
            self.refresh_data()

    def refresh_data(self):
        """刷新抽奖名单数据"""
        # 确保表格已经创建
        if not hasattr(self, "table"):
            return

        pool_name = self.lottery_comboBox.currentText()
        if not pool_name:
            self.table.setRowCount(0)
            return

        # 临时阻止信号，避免初始化时触发保存操作
        self.table.blockSignals(True)

        try:
            # 获取抽奖池数据
            pool = get_pool_data(pool_name)
            if not pool:
                self.table.setRowCount(0)
                return

            # 设置表格行数
            self.table.setRowCount(len(pool))

            # 填充表格数据
            for row, item in enumerate(pool):
                # 是否存在勾选框
                checkbox_item = QTableWidgetItem()
                checkbox_item.setCheckState(
                    Qt.CheckState.Checked
                    if item.get("exist", True)
                    else Qt.CheckState.Unchecked
                )
                checkbox_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 0, checkbox_item)

                # 奖品ID
                id_item = QTableWidgetItem(str(item.get("id", row + 1)))
                id_item.setFlags(
                    id_item.flags() & ~Qt.ItemFlag.ItemIsEditable
                )  # 学号不可编辑
                id_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 1, id_item)

                # 奖品名称
                name_item = QTableWidgetItem(item.get("name", ""))
                name_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 2, name_item)

                # 奖品权重
                weight_item = QTableWidgetItem(str(item.get("weight", 1)))
                weight_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.table.setItem(row, 3, weight_item)

            # 调整列宽
            self.table.horizontalHeader().resizeSection(0, 80)
            self.table.horizontalHeader().setSectionResizeMode(
                0, QHeaderView.ResizeMode.ResizeToContents
            )
            for i in range(1, 4):
                self.table.horizontalHeader().setSectionResizeMode(
                    i, QHeaderView.ResizeMode.Stretch
                )

        except Exception as e:
            logger.error(f"刷新抽奖名单表格数据失败: {str(e)}")
        finally:
            # 恢复信号
            self.table.blockSignals(False)

    def save_table_data(self, row, col):
        """保存表格编辑的数据"""
        pool_name = self.lottery_comboBox.currentText()
        if not pool_name:
            return

        # 获取当前单元格
        item = self.table.item(row, col)
        if not item:
            return

        # 获取当前行的奖品ID和名称
        id_item = self.table.item(row, 1)
        name_item = self.table.item(row, 2)
        if not id_item or not name_item:
            return
        item_id = id_item.text()
        item_name = name_item.text()

        # 加载当前抽奖池数据
        pool_file = get_path("app/resources/list/lottery_list") / f"{pool_name}.json"
        try:
            with open_file(pool_file, "r", encoding="utf-8") as f:
                pool_data = json.load(f, object_pairs_hook=OrderedDict)
        except Exception as e:
            logger.error(f"加载抽奖池数据失败: {str(e)}")
            return

        # 通过奖品ID找到对应的奖品键
        matched_key = None
        for key, value in pool_data.items():
            stored_id = value.get("id")
            if str(stored_id).lstrip("0") == str(item_id).lstrip("0") or str(
                stored_id
            ) == str(item_id):
                matched_key = key
                break

        if not matched_key:
            logger.error(f"未找到奖品ID为 {item_id} 的奖品，奖品名称: {item_name}")
            return

        # 根据列索引更新相应的字段
        new_value = item.text()
        if col == 2:  # 奖品名称列
            # 直接更新奖品名称，不再使用括号标记
            if new_value != matched_key:
                new_pool_data = OrderedDict()
                for key, value in pool_data.items():
                    if key == matched_key:
                        new_pool_data[new_value] = value
                    else:
                        new_pool_data[key] = value
                pool_data = new_pool_data
        elif col == 3:  # 奖品权重列
            pool_data[matched_key]["weight"] = int(new_value)
        elif col == 0:  # "存在"勾选框列
            checkbox_item = self.table.item(row, 0)
            if checkbox_item:
                is_checked = checkbox_item.checkState() == Qt.CheckState.Checked
                pool_data[matched_key]["exist"] = is_checked

        # 保存更新后的数据
        try:
            # 暂时禁用文件监视器，避免保存时触发刷新循环
            if hasattr(self, "file_watcher"):
                self.file_watcher.removePath(str(pool_file))

            with open_file(pool_file, "w", encoding="utf-8") as f:
                json.dump(pool_data, f, ensure_ascii=False, indent=4)
            # logger.debug(f"抽奖池数据更新成功: {pool_name}")

            # 保存成功后设置列宽
            self.table.blockSignals(True)
            for i in range(1, 4):
                self.table.horizontalHeader().setSectionResizeMode(
                    i, QHeaderView.ResizeMode.Stretch
                )
            self.table.blockSignals(False)

            # 重新启用文件监视器
            if hasattr(self, "file_watcher"):
                self.file_watcher.addPath(str(pool_file))
        except Exception as e:
            logger.error(f"保存抽奖池数据失败: {str(e)}")
            # 如果保存失败，恢复原来的值
            self.table.blockSignals(True)  # 阻止信号，避免递归调用
            if col == 2:  # 奖品名称列
                item.setText(str(matched_key) if matched_key else item_name)
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            else:
                original_value = ""
                if matched_key:
                    original_value = (
                        pool_data[matched_key]["weight"] if col == 3 else ""
                    )
                item.setText(str(original_value))
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.table.blockSignals(False)  # 恢复信号

            # 即使保存失败也要重新启用文件监视器
            if hasattr(self, "file_watcher"):
                self.file_watcher.addPath(str(pool_file))
