# ==================================================
# 导入库
# ==================================================

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


# ==================================================
# 贡献者页面类
# ==================================================
class contributor_page(QWidget):
    """贡献者信息页面 - 显示项目贡献者信息，采用响应式网格布局"""

    def __init__(self, parent=None):
        """初始化贡献者页面"""
        super().__init__(parent)
        self.setObjectName("contributor_page")

        # 初始化UI组件
        self._init_ui()

        # 初始化数据
        self._init_data()

        # 延迟添加贡献者卡片
        QTimer.singleShot(100, self.create_contributor_cards)

    def _init_ui(self):
        """初始化UI组件"""
        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(10, 10, 10, 10)
        self.main_layout.setSpacing(10)

        # 创建网格布局
        self.grid_layout = QGridLayout()
        self.grid_layout.setSpacing(CONTRIBUTOR_CARD_SPACING)
        self.main_layout.addLayout(self.grid_layout)

        # 初始化卡片列表
        self.cards = []

    def _init_data(self):
        """初始化贡献者数据"""
        # 贡献者数据
        contributors = [
            {
                "name": "lzy98276",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_1"
                ),
                "github": "https://github.com/lzy98276",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor1.png")
                ),
            },
            {
                "name": "QiKeZhiCao",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_2"
                ),
                "github": "https://github.com/QiKeZhiCao",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor2.png")
                ),
            },
            {
                "name": "Fox-block-offcial",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_3"
                ),
                "github": "https://github.com/Fox-block-offcial",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor3.png")
                ),
            },
            {
                "name": "yuanbenxin",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_4"
                ),
                "github": "https://github.com/yuanbenxin",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor4.png")
                ),
            },
            {
                "name": "LeafS",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_5"
                ),
                "github": "https://github.com/LeafS825",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor5.png")
                ),
            },
            {
                "name": "Jursin",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_6"
                ),
                "github": "https://github.com/jursin",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor6.png")
                ),
            },
            {
                "name": "LHGS-github",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_7"
                ),
                "github": "https://github.com/LHGS-github",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor7.png")
                ),
            },
            {
                "name": "real01bit",
                "role": get_any_position_value_async(
                    "about", "contributor", "contributor_role_8"
                ),
                "github": "https://github.com/real01bit",
                "avatar": str(
                    get_resources_path("assets/contribution", "contributor8.png")
                ),
            },
        ]

        # 标准化职责文本长度
        self._standardize_role_text(contributors)
        self.contributors = contributors

    def _standardize_role_text(self, contributors):
        """标准化职责文本长度，使所有职责文本行数一致"""
        # 计算所有职责文本的行数
        fm = QFontMetrics(self.font())
        max_lines = 0
        role_lines = []

        # 找出最长的职责文本有多少行
        for contributor in contributors:
            role_text = contributor["role"] or ""  # 确保role_text不为None
            contributor["role"] = role_text

            # 计算文本在MAX_ROLE_WIDTH宽度下的行数
            text_rect = fm.boundingRect(
                QRect(0, 0, CONTRIBUTOR_MAX_ROLE_WIDTH, 0),
                Qt.TextFlag.TextWordWrap,
                role_text,
            )
            line_count = text_rect.height() // fm.lineSpacing()
            role_lines.append(line_count)
            max_lines = max(max_lines, line_count)

        # 为每个职责文本添加换行符，确保行数相同
        for i, contributor in enumerate(contributors):
            current_lines = role_lines[i]
            if current_lines < max_lines:
                contributor["role"] += "\n" * (max_lines - current_lines)

    def create_contributor_cards(self):
        """创建贡献者卡片"""
        if not hasattr(self, "grid_layout") or self.grid_layout is None:
            return

        # 添加贡献者卡片
        for contributor in self.contributors:
            card = self.addContributorCard(contributor)
            if card is not None:  # 只添加有效的卡片
                self.cards.append(card)

        # 延迟更新布局
        QTimer.singleShot(50, self.update_layout)

    def update_layout(self):
        """更新布局 - 根据窗口大小动态调整卡片排列"""
        # 清空网格布局
        self._clear_grid_layout()

        def calculate_columns(width):
            """根据窗口宽度和卡片尺寸动态计算列数"""
            if width <= 0:
                return 1

            # 计算可用宽度（减去左右边距）
            available_width = width - 40  # 左右各20px边距

            # 计算单个卡片实际占用的宽度（包括间距）
            card_actual_width = CONTRIBUTOR_CARD_MIN_WIDTH + CONTRIBUTOR_CARD_SPACING

            # 计算最大可能列数（不超过MAX_COLUMNS）
            cols = min(available_width // card_actual_width, CONTRIBUTOR_MAX_COLUMNS)

            # 至少显示1列
            return max(cols, 1)

        # 获取窗口实际可用宽度
        window_width = max(self.width(), self.sizeHint().width())

        # 根据窗口宽度计算列数
        cols = calculate_columns(window_width)

        # 设置网格布局的列伸缩因子，使卡片均匀分布
        for col in range(cols):
            self.grid_layout.setColumnStretch(col, 1)

        # 添加卡片到网格
        for i, card in enumerate(self.cards):
            row = i // cols
            col = i % cols
            # 设置卡片的最小宽度和最大宽度
            card.setMinimumWidth(CONTRIBUTOR_CARD_MIN_WIDTH)
            card.setMaximumWidth(
                CONTRIBUTOR_CARD_MIN_WIDTH * 1.5
            )  # 设置最大宽度，防止卡片过宽
            self.grid_layout.addWidget(card, row, col, Qt.AlignmentFlag.AlignCenter)
            card.show()

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

    def addContributorCard(self, contributor):
        """添加单个贡献者卡片"""
        if not hasattr(self, "grid_layout") or self.grid_layout is None:
            return None

        try:
            card = QWidget()
            card.setObjectName("contributorCard")
            cardLayout = QVBoxLayout(card)
            cardLayout.setContentsMargins(
                CONTRIBUTOR_CARD_MARGIN,
                CONTRIBUTOR_CARD_MARGIN,
                CONTRIBUTOR_CARD_MARGIN,
                CONTRIBUTOR_CARD_MARGIN,
            )
            cardLayout.setSpacing(10)

            # 头像
            avatar = AvatarWidget(contributor["avatar"])
            avatar.setRadius(CONTRIBUTOR_AVATAR_RADIUS)
            avatar.setAlignment(Qt.AlignmentFlag.AlignCenter)
            cardLayout.addWidget(avatar, 0, Qt.AlignmentFlag.AlignCenter)

            # 昵称作为GitHub链接
            name = HyperlinkButton(contributor["github"], contributor["name"], None)
            name.setStyleSheet(
                "text-decoration: underline; color: #0066cc; background: transparent; border: none; padding: 0;"
            )
            cardLayout.addWidget(name, 0, Qt.AlignmentFlag.AlignCenter)

            # 职责
            role_text = contributor["role"] or ""
            role = BodyLabel(role_text)
            role.setAlignment(Qt.AlignmentFlag.AlignCenter)
            role.setWordWrap(True)
            role.setMaximumWidth(CONTRIBUTOR_MAX_ROLE_WIDTH)
            cardLayout.addWidget(role, 0, Qt.AlignmentFlag.AlignCenter)

            return card
        except RuntimeError as e:
            logger.error(f"创建贡献者卡片时出错: {e}")
            return None

    def resizeEvent(self, event):
        """窗口大小变化事件"""
        # 使用QTimer延迟布局更新，避免递归调用
        if hasattr(self, "_resize_timer") and self._resize_timer is not None:
            self._resize_timer.stop()
        self._resize_timer = QTimer()
        self._resize_timer.setSingleShot(True)
        self._resize_timer.timeout.connect(self._delayed_update_layout)
        self._resize_timer.start(50)
        super().resizeEvent(event)

    def _delayed_update_layout(self):
        """延迟更新布局"""
        try:
            if hasattr(self, "grid_layout") and self.grid_layout is not None:
                if self.isVisible():
                    self.update_layout()
        except RuntimeError as e:
            logger.error(f"延迟布局更新错误: {e}")

    def closeEvent(self, event):
        """窗口关闭事件"""
        # 清理定时器
        if hasattr(self, "_resize_timer") and self._resize_timer is not None:
            self._resize_timer.stop()
            self._resize_timer = None

        super().closeEvent(event)
