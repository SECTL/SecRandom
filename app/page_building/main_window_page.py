# 导入库
from PySide6.QtWidgets import QFrame
from PySide6.QtCore import QTimer, Qt

# 导入页面模板
from app.page_building.page_template import PageTemplate, PivotPageTemplate

# 导入默认设置
from app.tools.settings_default import *
from app.Language.obtain_language import *
from app.tools.settings_access import get_settings_signals

# 导入自定义页面内容组件
from app.view.main.roll_call import roll_call
from app.view.main.lottery import Lottery
from app.tools.theme_loader import ThemeLoader


class roll_call_page(PageTemplate):
    """创建班级点名页面"""

    def __init__(self, parent: QFrame = None):
        widget_class = ThemeLoader.load_theme_widget("roll_call", roll_call)
        super().__init__(content_widget_class=widget_class, parent=parent)
        self.roll_call_widget = None
        get_settings_signals().settingChanged.connect(self._on_global_setting_changed)

    def _on_global_setting_changed(self, group, key, value):
        if group == "theme_management" and key in (
            "roll_call_theme_id",
            "roll_call_theme_type",
        ):
            self.content_widget_class = ThemeLoader.load_theme_widget(
                "roll_call", roll_call
            )
            self.handle_settings_change()

    def create_content(self):
        """后台创建内容组件，避免堵塞进程"""
        super().create_content()
        # 获取点名组件实例并连接信号
        if hasattr(self, "contentWidget"):
            self.roll_call_widget = self.contentWidget
            if self.roll_call_widget and self.roll_call_widget.property(
                "theme_html_wrapper"
            ):
                if hasattr(self, "_inner_layout_lazy") and self._inner_layout_lazy:
                    self._inner_layout_lazy.setAlignment(Qt.AlignmentFlag.AlignTop)
                    self._inner_layout_lazy.setContentsMargins(0, 0, 0, 0)
                    self._inner_layout_lazy.setSpacing(0)
                if hasattr(self, "_main_layout_lazy") and self._main_layout_lazy:
                    self._main_layout_lazy.setContentsMargins(0, 0, 0, 0)
                    self._main_layout_lazy.setSpacing(0)
            # 连接设置变化信号
            if hasattr(self.roll_call_widget, "settingsChanged"):
                self.roll_call_widget.settingsChanged.connect(
                    self.handle_settings_change
                )

    def handle_settings_change(self):
        """处理设置变化信号"""
        # 清除页面缓存并重新创建
        self.clear_content()
        QTimer.singleShot(0, self._recreate_content)

    def _recreate_content(self):
        """重新创建内容"""
        self.create_content()

    def clear_content(self):
        """清除内容"""
        if hasattr(self, "_inner_layout_lazy") and self._inner_layout_lazy.count() > 0:
            item = self._inner_layout_lazy.takeAt(0)
            if item and item.widget():
                widget = item.widget()
                widget.deleteLater()
        self.content_created = False
        self.roll_call_widget = None
        self.contentWidget = None


class lottery_page(PageTemplate):
    """创建班级点名页面"""

    def __init__(self, parent: QFrame = None):
        widget_class = ThemeLoader.load_theme_widget("lottery", Lottery)
        super().__init__(content_widget_class=widget_class, parent=parent)
        self.lottery_widget = None
        get_settings_signals().settingChanged.connect(self._on_global_setting_changed)

    def _on_global_setting_changed(self, group, key, value):
        if group == "theme_management" and key in (
            "lottery_theme_id",
            "lottery_theme_type",
        ):
            self.content_widget_class = ThemeLoader.load_theme_widget(
                "lottery", Lottery
            )
            self.handle_settings_change()

    def create_content(self):
        """后台创建内容组件，避免堵塞进程"""
        super().create_content()
        # 获取奖池组件实例并连接信号
        if hasattr(self, "contentWidget"):
            self.lottery_widget = self.contentWidget
            if self.lottery_widget and self.lottery_widget.property(
                "theme_html_wrapper"
            ):
                if hasattr(self, "_inner_layout_lazy") and self._inner_layout_lazy:
                    self._inner_layout_lazy.setAlignment(Qt.AlignmentFlag.AlignTop)
                    self._inner_layout_lazy.setContentsMargins(0, 0, 0, 0)
                    self._inner_layout_lazy.setSpacing(0)
                if hasattr(self, "_main_layout_lazy") and self._main_layout_lazy:
                    self._main_layout_lazy.setContentsMargins(0, 0, 0, 0)
                    self._main_layout_lazy.setSpacing(0)
            # 连接设置变化信号
            if hasattr(self.lottery_widget, "settingsChanged"):
                self.lottery_widget.settingsChanged.connect(self.handle_settings_change)

    def handle_settings_change(self):
        """处理设置变化信号"""
        # 清除页面缓存并重新创建
        self.clear_content()
        QTimer.singleShot(0, self._recreate_content)

    def _recreate_content(self):
        """重新创建内容"""
        self.create_content()

    def clear_content(self):
        """清除内容"""
        if hasattr(self, "_inner_layout_lazy") and self._inner_layout_lazy.count() > 0:
            item = self._inner_layout_lazy.takeAt(0)
            if item and item.widget():
                widget = item.widget()
                widget.deleteLater()
        self.content_created = False
        self.lottery_widget = None
        self.contentWidget = None


class history_page(PivotPageTemplate):
    """创建历史记录页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "roll_call_history_table": get_content_name_async(
                "roll_call_history_table", "title"
            ),
            "lottery_history_table": get_content_name_async(
                "lottery_history_table", "title"
            ),
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.history")
