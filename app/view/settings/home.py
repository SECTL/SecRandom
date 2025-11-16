# ==================================================
# 导入库
# ==================================================

from loguru import logger
from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout
from qfluentwidgets import SearchLineEdit


# ==================================================
# 主页
# ==================================================
class home(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)

        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(20, 20, 20, 20)
        self.main_layout.setSpacing(10)

        # 创建顶部布局
        self.top_layout = QHBoxLayout()
        self.top_layout.setContentsMargins(0, 0, 0, 0)

        # 创建搜索框
        self.search_line_edit = SearchLineEdit()
        self.search_line_edit.setPlaceholderText("搜索...")
        self.search_line_edit.setFixedWidth(300)
        self.search_line_edit.searchSignal.connect(self.on_search)

        # 将搜索框添加到顶部布局的中间
        self.top_layout.addStretch()
        self.top_layout.addWidget(self.search_line_edit)
        self.top_layout.addStretch()

        # 将顶部布局添加到主布局
        self.main_layout.addLayout(self.top_layout)

        # 添加一个弹性空间，使搜索框位于顶部
        self.main_layout.addStretch()

        # 设置窗口布局
        self.setLayout(self.main_layout)

    def on_search(self, text):
        """搜索框的回调函数"""
        logger.debug(f"搜索内容: {text}")
        # 这里可以添加搜索功能的实现
