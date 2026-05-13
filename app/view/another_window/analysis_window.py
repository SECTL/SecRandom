# ==================================================
# 导入库
# ==================================================

from PySide6.QtCore import Signal
from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout
from qfluentwidgets import (
    BodyLabel,
    CheckBox,
    MessageBox,
    PrimaryPushButton,
    PushButton,
)

from app.tools.settings_access import update_settings
from app.Language.obtain_language import (
    get_content_name_async,
    get_any_position_value_async,
)


SETTING_NAME_KEYS = {
    ("roll_call_settings", "draw_type"): ("extraction_settings", "draw_type"),
    ("lottery_settings", "draw_type"): ("extraction_settings", "draw_type"),
    ("quick_draw_settings", "draw_type"): ("extraction_settings", "draw_type"),
    ("fair_draw_settings", "enable_avg_gap_protection"): (
        "fair_draw_settings",
        "enable_avg_gap_protection",
    ),
    ("fair_draw_settings", "fair_draw_time"): ("fair_draw_settings", "fair_draw_time"),
    ("fair_draw_settings", "cold_start_enabled"): (
        "fair_draw_settings",
        "cold_start_enabled",
    ),
    ("fair_draw_settings", "shield_enabled"): ("fair_draw_settings", "shield_enabled"),
    ("fair_draw_settings", "show_weight_transparency"): (
        "fair_draw_settings",
        "show_weight_transparency",
    ),
    ("fair_draw_settings", "base_weight"): ("fair_draw_settings", "base_weight"),
    ("fair_draw_settings", "min_weight"): ("fair_draw_settings", "min_weight"),
    ("fair_draw_settings", "max_weight"): ("fair_draw_settings", "max_weight"),
    ("fair_draw_settings", "frequency_function"): (
        "fair_draw_settings",
        "frequency_function",
    ),
    ("fair_draw_settings", "frequency_weight"): (
        "fair_draw_settings",
        "frequency_weight",
    ),
    ("fair_draw_settings", "group_weight"): ("fair_draw_settings", "group_weight"),
    ("fair_draw_settings", "gender_weight"): ("fair_draw_settings", "gender_weight"),
    ("fair_draw_settings", "time_weight"): ("fair_draw_settings", "time_weight"),
    ("fair_draw_settings", "cold_start_rounds"): (
        "fair_draw_settings",
        "cold_start_rounds",
    ),
    ("fair_draw_settings", "gap_threshold"): ("fair_draw_settings", "gap_threshold"),
    ("fair_draw_settings", "min_pool_size"): ("fair_draw_settings", "min_pool_size"),
    ("fair_draw_settings", "shield_time"): ("fair_draw_settings", "shield_time"),
    ("fair_draw_settings", "shield_time_unit"): (
        "fair_draw_settings",
        "shield_time_unit",
    ),
}

TYPE_LABELS = {
    ("extraction_settings", "draw_type"): {0: 0, 1: 1},
}

SCOPE_LABEL = {
    "roll_call_settings": "点名",
    "lottery_settings": "抽奖",
    "quick_draw_settings": "闪抽",
    "fair_draw_settings": "全局",
}


def _get_setting_name(group, key):
    module_key = SETTING_NAME_KEYS.get((group, key))
    if module_key:
        name = get_content_name_async(*module_key)
        if name:
            return name
    return key


def _get_value_label(group, key, value):
    type_key = SETTING_NAME_KEYS.get((group, key))
    if type_key and type_key in TYPE_LABELS and value in TYPE_LABELS[type_key]:
        items = get_any_position_value_async(type_key[0], type_key[1], "combo_items", 0)
        if isinstance(items, list) and value < len(items):
            return items[value]
        return str(value)
    if isinstance(value, bool):
        return "ON" if value else "OFF"
    if isinstance(value, float) and value == int(value):
        return str(int(value))
    return str(value)


class AnalysisWindow(QWidget):
    applied = Signal()

    def __init__(self, parent=None, recommendations=None):
        super().__init__(parent)
        self._checkboxes = []
        self._recommendations = recommendations

        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(16, 12, 16, 12)
        self.vBoxLayout.setSpacing(8)

        self.vBoxLayout.addWidget(self._build_stats_section())
        self.vBoxLayout.addWidget(self._build_suggestions_section())
        self.vBoxLayout.addWidget(self._build_action_bar())
        self.vBoxLayout.addStretch()

    def _tr(self, key):
        try:
            val = get_content_name_async("best_config", key)
            return str(val) if val else key
        except Exception:
            return key

    def _build_stats_section(self):
        container = QWidget()
        layout = QVBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(2)

        title = BodyLabel(self._tr("stats_section_title"))
        title.setStyleSheet("font-size: 14px; font-weight: bold;")
        layout.addWidget(title)

        metrics = {}
        try:
            from app.common.fair_draw.config_advisor import (
                load_roll_call_history,
                compute_fairness_metrics,
            )

            stats = load_roll_call_history()
            metrics = compute_fairness_metrics(stats)
        except Exception:
            pass

        if not metrics:
            layout.addWidget(BodyLabel("暂无历史记录可供分析"))
            return container

        lines = [
            f"点名 {metrics.get('total_draws', 0)} 次  ·  {metrics.get('total_students', 0)} 人  ·  平均 {metrics.get('mean', 0)} 次",
            f"最大-最小差距 {metrics.get('gap', 0)}  ·  标准差 {metrics.get('std_dev', 0)}",
            f"从未被抽中 {metrics.get('zeros', 0)} 人  ·  单人最高 {metrics.get('max_one_pct', 0)}%",
        ]
        for line in lines:
            label = BodyLabel(line)
            label.setWordWrap(True)
            layout.addWidget(label)
        return container

    def _build_suggestions_section(self):
        container = QWidget()
        layout = QVBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(4)

        title = BodyLabel(self._tr("suggestions_section_title"))
        title.setStyleSheet("font-size: 14px; font-weight: bold;")
        layout.addWidget(title)

        if self._recommendations is None:
            try:
                from app.common.fair_draw.config_advisor import get_recommendations

                self._recommendations = get_recommendations()
            except Exception:
                self._recommendations = []

        if not self._recommendations:
            layout.addWidget(BodyLabel(f"  {self._tr('no_recommendations')}"))
            return container

        btn_row = QHBoxLayout()
        btn_row.setSpacing(8)
        select_all_btn = PushButton(self._tr("select_all_btn"))
        deselect_all_btn = PushButton(self._tr("deselect_all_btn"))
        select_all_btn.clicked.connect(lambda: self._toggle_all(True))
        deselect_all_btn.clicked.connect(lambda: self._toggle_all(False))
        btn_row.addWidget(select_all_btn)
        btn_row.addWidget(deselect_all_btn)
        btn_row.addStretch()
        layout.addLayout(btn_row)

        self._checkboxes = []
        for rec in self._recommendations:
            group = rec["group"]
            key = rec["key"]
            scope = SCOPE_LABEL.get(group, group)
            setting_name = _get_setting_name(group, key)
            current_label = _get_value_label(group, key, rec["current"])
            recommended_label = _get_value_label(group, key, rec["recommended"])
            reason = rec.get("reason_text", "") or self._tr(rec.get("reason_key", ""))

            row_layout = QHBoxLayout()
            row_layout.setContentsMargins(0, 2, 0, 2)
            row_layout.setSpacing(6)

            cb = CheckBox()
            cb.setChecked(True)

            left = QVBoxLayout()
            left.setSpacing(0)

            header = BodyLabel(f"{setting_name}（{scope}）")
            header.setStyleSheet("font-weight: bold; font-size: 13px;")
            change = BodyLabel(f"{current_label} → {recommended_label}")
            change.setStyleSheet("color: #0078D4; font-size: 12px;")
            detail = BodyLabel(reason)
            detail.setStyleSheet("color: #888888; font-size: 12px;")
            detail.setWordWrap(True)

            left.addWidget(header)
            left.addWidget(change)
            left.addWidget(detail)

            row_layout.addWidget(cb)
            row_layout.addLayout(left, 1)

            layout.addLayout(row_layout)
            self._checkboxes.append((cb, rec))

        return container

    def _toggle_all(self, checked):
        for cb, _ in self._checkboxes:
            cb.setChecked(checked)

    def _build_action_bar(self):
        btn = PrimaryPushButton(self._tr("apply_selected_btn"))
        btn.setFixedWidth(220)
        btn.clicked.connect(self._apply_selected)

        container = QWidget()
        layout = QHBoxLayout(container)
        layout.setContentsMargins(0, 8, 0, 0)
        layout.addStretch()
        layout.addWidget(btn)
        layout.addStretch()
        return container

    def _apply_selected(self):
        applied_count = 0
        for cb, rec in self._checkboxes:
            if not cb.isChecked():
                continue
            try:
                update_settings(rec["group"], rec["key"], rec["recommended"])
                applied_count += 1
            except Exception:
                pass

        if applied_count > 0:
            dialog = MessageBox(
                self._tr("apply_success"),
                f"{applied_count} 项配置已应用",
                self.window(),
            )
            dialog.yesButton.setText(self._tr("confirm_btn"))
            dialog.cancelButton.setText(self._tr("cancel_btn"))
            dialog.exec()
            self.applied.emit()
        self.window().close()
