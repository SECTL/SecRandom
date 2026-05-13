# ==================================================
# 导入库
# ==================================================

from PySide6.QtCore import Signal
from qfluentwidgets import (
    GroupHeaderCardWidget,
    MessageBox,
    PushButton,
)

from app.tools.personalised import get_theme_icon
from app.tools.settings_access import readme_settings_async, update_settings
from app.tools.settings_default_storage import DEFAULT_SETTINGS
from app.Language.obtain_language import (
    get_content_name_async,
    get_content_description_async,
)


def _get_fair_draw_defaults():
    return {
        k: v["default_value"]
        for k, v in DEFAULT_SETTINGS.get("fair_draw_settings", {}).items()
    }


class BestConfigCard(GroupHeaderCardWidget):
    analyze_clicked = Signal()
    reset_applied = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("best_config", "title"))
        self.setBorderRadius(8)

        self._init_buttons()
        self._update_states()

    @staticmethod
    def _tr(key):
        try:
            name = get_content_name_async("best_config", key)
            return name if name else key
        except Exception:
            return key

    @staticmethod
    def _td(key):
        try:
            desc = get_content_description_async("best_config", key)
            return desc if desc else ""
        except Exception:
            return ""

    def _init_buttons(self):
        self.reset_btn = PushButton(self._tr("reset_defaults_btn"))
        self.reset_btn.clicked.connect(self._on_reset_defaults)

        self.analyze_btn = PushButton(self._tr("analyze_btn"))
        self.analyze_btn.clicked.connect(self.analyze_clicked.emit)

        self.addGroup(
            get_theme_icon("ic_fluent_arrow_undo_20_filled"),
            self._tr("reset_defaults_btn"),
            self._td("reset_defaults_btn"),
            self.reset_btn,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_brain_circuit_20_filled"),
            self._tr("analyze_btn"),
            self._td("analyze_btn"),
            self.analyze_btn,
        )

    def _update_states(self):
        draw_count = self._get_draw_count()
        self.analyze_btn.setEnabled(draw_count >= 200)

    @staticmethod
    def _get_draw_count():
        try:
            count = readme_settings_async("user_info", "roll_call_total_count")
            return int(count) if count is not None else 0
        except Exception:
            return 0

    def _on_reset_defaults(self):
        dialog = MessageBox(
            self._tr("reset_confirm_title"),
            self._tr("reset_confirm_content"),
            self.window(),
        )
        dialog.yesButton.setText(self._tr("confirm_btn"))
        dialog.cancelButton.setText(self._tr("cancel_btn"))

        if dialog.exec():
            for key, value in _get_fair_draw_defaults().items():
                try:
                    update_settings("fair_draw_settings", key, value)
                except Exception:
                    pass
            self.reset_applied.emit()
