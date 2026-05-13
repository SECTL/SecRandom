from PySide6.QtWidgets import (
    QVBoxLayout,
    QHBoxLayout,
    QHeaderView,
    QFrame,
    QAbstractItemView,
)
from PySide6.QtGui import QColor, QStandardItemModel, QStandardItem
from PySide6.QtCore import Qt, QPropertyAnimation, QEasingCurve
from qfluentwidgets import TableView, ToolButton, BodyLabel
from loguru import logger

from app.tools.personalised import get_theme_icon
from app.Language.obtain_language import get_content_name_async


_COLUMNS = [
    "wp_col_student",
    "wp_col_base",
    "wp_col_freq",
    "wp_col_group",
    "wp_col_gender",
    "wp_col_time",
    "wp_col_shield",
    "wp_col_total",
]


class WeightPanel(QFrame):
    _DEFAULT_SETTINGS_GROUP = "lottery_settings"
    _HEADER_HEIGHT = 34
    _EXPANDED_MAX = 300

    def __init__(self, parent=None, settings_group=None):
        super().__init__(parent)
        self._collapsed = True
        self._settings_group = settings_group or self._DEFAULT_SETTINGS_GROUP
        self._anim = None
        self._model = None
        self.setMinimumHeight(0)
        self.setMaximumHeight(self._HEADER_HEIGHT)
        self._init_ui()

    def _t(self, key):
        return get_content_name_async(self._settings_group, key)

    def _init_ui(self):
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(6, 2, 6, 2)
        main_layout.setSpacing(2)

        header = QHBoxLayout()
        header.setContentsMargins(0, 0, 0, 0)

        self._collapse_btn = ToolButton(
            get_theme_icon("ic_fluent_chevron_right_20_filled"), self
        )
        self._collapse_btn.setFixedSize(28, 28)
        self._collapse_btn.setToolTip(self._t("wp_expand"))
        self._collapse_btn.clicked.connect(self.toggle_collapse)
        header.addWidget(self._collapse_btn)

        self._title_label = BodyLabel(self._t("wp_title"), self)
        header.addWidget(self._title_label)
        header.addStretch()

        self._help_btn = ToolButton(
            get_theme_icon("ic_fluent_question_circle_20_filled"), self
        )
        self._help_btn.setFixedSize(28, 28)
        self._help_btn.setToolTip(self._t("wp_help"))
        self._help_btn.clicked.connect(self._show_formula_dialog)
        header.addWidget(self._help_btn)

        main_layout.addLayout(header)

        self._table = TableView(self)
        self._table.setBorderVisible(True)
        self._table.setBorderRadius(6)
        self._table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)

        self._model = QStandardItemModel(0, len(_COLUMNS), self)
        self._model.setHorizontalHeaderLabels([self._t(c) for c in _COLUMNS])
        self._table.setModel(self._model)

        hh = self._table.horizontalHeader()
        hh.setSectionResizeMode(QHeaderView.Stretch)
        vh = self._table.verticalHeader()
        vh.setVisible(True)
        vh.setSectionResizeMode(QHeaderView.Fixed)
        vh.setDefaultSectionSize(24)

        self._table.setVisible(False)
        self._table.setMinimumHeight(0)
        main_layout.addWidget(self._table)

    def set_students(self, students_data: list):
        self._model.removeRows(0, self._model.rowCount())
        for student in students_data:
            if not isinstance(student, dict):
                continue
            details = student.get("weight_details", {})
            if not details:
                continue
            name = student.get("name", student.get("id", "?"))

            row_items = [
                str(name),
                f"{details.get('base_weight', 0):.2f}",
                f"{details.get('frequency_penalty', 0):.2f}",
                f"{details.get('group_balance', 0):.2f}",
                f"{details.get('gender_balance', 0):.2f}",
                f"{details.get('time_factor', 0):.2f}",
                self._t("wp_shielded")
                if details.get("is_shielded")
                else self._t("wp_normal"),
                f"{details.get('total_weight', student.get('next_weight', 0)):.2f}",
            ]

            row = []
            for text in row_items:
                item = QStandardItem(text)
                item.setTextAlignment(Qt.AlignCenter)
                item.setEditable(False)
                row.append(item)

            if details.get("is_shielded"):
                row[-1].setForeground(QColor("red"))

            self._model.appendRow(row)

    def toggle_collapse(self):
        self._collapsed = not self._collapsed
        self._table.setVisible(not self._collapsed)
        if self._collapsed:
            self._collapse_btn.setIcon(
                get_theme_icon("ic_fluent_chevron_right_20_filled")
            )
            self._collapse_btn.setToolTip(self._t("wp_expand"))
        else:
            self._collapse_btn.setIcon(
                get_theme_icon("ic_fluent_chevron_down_20_filled")
            )
            self._collapse_btn.setToolTip(self._t("wp_collapse"))
        self._animate()

    def _animate(self):
        if (
            self._anim is not None
            and self._anim.state() == QPropertyAnimation.State.Running
        ):
            self._anim.stop()
        target = self._HEADER_HEIGHT if self._collapsed else self._EXPANDED_MAX
        self._anim = QPropertyAnimation(self, b"maximumHeight")
        self._anim.setDuration(250)
        self._anim.setStartValue(self.height())
        self._anim.setEndValue(target)
        self._anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self._anim.start()

    def _show_formula_dialog(self):
        logger.debug("打开权重计算规则弹窗")
        from app.page_building.another_window import create_weight_formula_window

        create_weight_formula_window(parent=None, settings_group=self._settings_group)

    def clear(self):
        self._model.removeRows(0, self._model.rowCount())
        if not self._collapsed:
            self.toggle_collapse()


def create_weight_panel(
    students_data: list, parent=None, settings_group=None
) -> WeightPanel:
    panel = WeightPanel(parent, settings_group=settings_group)
    panel.set_students(students_data)
    return panel
