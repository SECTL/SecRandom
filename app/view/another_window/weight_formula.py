from PySide6.QtWidgets import QVBoxLayout, QTextEdit, QWidget

from app.Language.obtain_language import get_content_name_async


class WeightFormulaPage(QWidget):
    """权重计算规则说明页"""

    _DEFAULT_SETTINGS_GROUP = "lottery_settings"

    def __init__(self, parent=None, settings_group=None):
        super().__init__(parent)
        self._settings_group = settings_group or self._DEFAULT_SETTINGS_GROUP
        self._init_ui()

    def _t(self, key):
        return get_content_name_async(self._settings_group, key)

    def _init_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(16, 12, 16, 12)

        text_edit = QTextEdit(self)
        text_edit.setReadOnly(True)
        text_edit.setMarkdown(self._t("wp_formula_content"))
        text_edit.setStyleSheet("border: none; background: transparent;")
        layout.addWidget(text_edit)
