# ==================================================
# 导入库
# ==================================================

from PySide6.QtWidgets import QWidget, QVBoxLayout
from PySide6.QtGui import QFontDatabase
from qfluentwidgets import (
    GroupHeaderCardWidget,
    SwitchButton,
    ComboBox,
    PushButton,
    ColorConfigItem,
    ColorSettingCard,
    Theme,
    setTheme,
    setThemeColor,
)

from app.tools.personalised import get_theme_icon
from app.tools.settings_access import readme_settings_async, update_settings
from app.Language.obtain_language import (
    get_all_languages_name,
    get_content_combo_name_async,
    get_content_description_async,
    get_content_name_async,
    get_content_pushbutton_name_async,
    get_content_switchbutton_name_async,
)


# ==================================================
# 基本设置
# ==================================================
class basic_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加基本功能设置组件
        self.basic_function_widget = basic_settings_function(self)
        self.vBoxLayout.addWidget(self.basic_function_widget)

        # 添加个性化设置组件
        self.personalised_widget = basic_settings_personalised(self)
        self.vBoxLayout.addWidget(self.personalised_widget)

        # 添加数据管理组件
        self.data_management_widget = basic_settings_data_management(self)
        self.vBoxLayout.addWidget(self.data_management_widget)


class basic_settings_function(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("basic_settings", "basic_function"))
        self.setBorderRadius(8)

        # 开机自启设置
        self.autostart_switch = SwitchButton()
        self.autostart_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "autostart", "disable"
            )
        )
        self.autostart_switch.setOnText(
            get_content_switchbutton_name_async("basic_settings", "autostart", "enable")
        )
        self.autostart_switch.setChecked(
            readme_settings_async("basic_settings", "autostart")
        )
        self.autostart_switch.checkedChanged.connect(
            lambda: update_settings(
                "basic_settings", "autostart", self.autostart_switch.isChecked()
            )
        )

        # 自动检查更新设置
        self.check_update_switch = SwitchButton()
        self.check_update_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "check_update", "disable"
            )
        )
        self.check_update_switch.setOnText(
            get_content_switchbutton_name_async(
                "basic_settings", "check_update", "enable"
            )
        )
        self.check_update_switch.setChecked(
            readme_settings_async("basic_settings", "check_update")
        )
        self.check_update_switch.checkedChanged.connect(
            lambda: update_settings(
                "basic_settings", "check_update", self.check_update_switch.isChecked()
            )
        )

        # 显示启动窗口设置
        self.show_startup_window_switch = SwitchButton()
        self.show_startup_window_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "show_startup_window", "disable"
            )
        )
        self.show_startup_window_switch.setOnText(
            get_content_switchbutton_name_async(
                "basic_settings", "show_startup_window", "enable"
            )
        )
        self.show_startup_window_switch.setChecked(
            readme_settings_async("basic_settings", "show_startup_window")
        )
        self.show_startup_window_switch.checkedChanged.connect(
            lambda: update_settings(
                "basic_settings",
                "show_startup_window",
                self.show_startup_window_switch.isChecked(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_sync_20_filled"),
            get_content_name_async("basic_settings", "autostart"),
            get_content_description_async("basic_settings", "autostart"),
            self.autostart_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_cloud_sync_20_filled"),
            get_content_name_async("basic_settings", "check_update"),
            get_content_description_async("basic_settings", "check_update"),
            self.check_update_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_window_play_20_filled"),
            get_content_name_async("basic_settings", "show_startup_window"),
            get_content_description_async("basic_settings", "show_startup_window"),
            self.show_startup_window_switch,
        )


class basic_settings_personalised(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("basic_settings", "personalised"))
        self.setBorderRadius(8)

        # 主题设置卡片
        self.theme = ComboBox()
        self.theme.addItems(get_content_combo_name_async("basic_settings", "theme"))
        theme_to_index = {"LIGHT": 0, "DARK": 1, "AUTO": 2}
        current_theme = readme_settings_async("basic_settings", "theme")
        self.theme.setCurrentIndex(theme_to_index.get(current_theme, 2))
        index_to_theme = {0: Theme.LIGHT, 1: Theme.DARK, 2: Theme.AUTO}
        self.theme.currentIndexChanged.connect(
            lambda index: update_settings(
                "basic_settings", "theme", ["LIGHT", "DARK", "AUTO"][index]
            )
        )
        self.theme.currentIndexChanged.connect(
            lambda index: setTheme(index_to_theme.get(index))
        )

        # 主题色设置卡片
        self.themeColor = ColorConfigItem(
            "basic_settings",
            "theme_color",
            readme_settings_async("basic_settings", "theme_color"),
        )
        self.themeColor.valueChanged.connect(
            lambda color: update_settings("basic_settings", "theme_color", color.name())
        )
        self.themeColor.valueChanged.connect(lambda color: setThemeColor(color))

        # 语言设置卡片
        self.language = ComboBox()
        self.language.addItems(get_all_languages_name())
        self.language.setCurrentText(
            readme_settings_async("basic_settings", "language")
        )
        self.language.currentTextChanged.connect(
            lambda language: update_settings("basic_settings", "language", language)
        )

        # 字体设置卡片
        self.fontComboBox = ComboBox()
        self.fontComboBox.addItems(
            ["HarmonyOS Sans SC"] + sorted(QFontDatabase.families())
        )
        self.fontComboBox.setCurrentText(
            readme_settings_async("basic_settings", "font")
        )
        self.fontComboBox.currentTextChanged.connect(
            lambda font: update_settings("basic_settings", "font", font)
        )

        # 界面缩放设置卡片
        self.dpiScale = ComboBox()
        self.dpiScale.addItems(
            get_content_combo_name_async("basic_settings", "dpiScale")
        )
        self.dpiScale.setCurrentText(
            readme_settings_async("basic_settings", "dpiScale")
        )
        self.dpiScale.currentTextChanged.connect(
            lambda scale: update_settings("basic_settings", "dpiScale", scale)
        )

        # 主题色设置卡片
        self.themeColorCard = ColorSettingCard(
            self.themeColor,
            get_theme_icon("ic_fluent_color_20_filled"),
            self.tr(get_content_name_async("basic_settings", "theme_color")),
            self.tr(get_content_description_async("basic_settings", "theme_color")),
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_dark_theme_20_filled"),
            get_content_name_async("basic_settings", "theme"),
            get_content_description_async("basic_settings", "theme"),
            self.theme,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_local_language_20_filled"),
            get_content_name_async("basic_settings", "language"),
            get_content_description_async("basic_settings", "language"),
            self.language,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_text_font_20_filled"),
            get_content_name_async("basic_settings", "font"),
            get_content_description_async("basic_settings", "font"),
            self.fontComboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_zoom_fit_20_filled"),
            get_content_name_async("basic_settings", "dpiScale"),
            get_content_description_async("basic_settings", "dpiScale"),
            self.dpiScale,
        )
        # 添加卡片到布局
        self.vBoxLayout.addWidget(self.themeColorCard)


class basic_settings_data_management(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("basic_settings", "data_management"))
        self.setBorderRadius(8)

        # 导出诊断数据按钮
        self.export_diagnostic_data_button = PushButton(
            get_content_pushbutton_name_async(
                "basic_settings", "export_diagnostic_data"
            )
        )
        self.export_diagnostic_data_button.clicked.connect(
            lambda: self.export_diagnostic_data()
        )

        # 导出设置按钮
        self.export_settings_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "export_settings")
        )
        self.export_settings_button.clicked.connect(lambda: self.export_settings())

        # 导入设置按钮
        self.import_settings_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "import_settings")
        )
        self.import_settings_button.clicked.connect(lambda: self.import_settings())

        # 导出软件所有数据按钮
        self.export_all_data_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "export_all_data")
        )
        self.export_all_data_button.clicked.connect(lambda: self.export_all_data())

        # 导入软件所有数据按钮
        self.import_all_data_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "import_all_data")
        )
        self.import_all_data_button.clicked.connect(lambda: self.import_all_data())

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_database_arrow_down_20_filled"),
            get_content_name_async("basic_settings", "export_diagnostic_data"),
            get_content_description_async("basic_settings", "export_diagnostic_data"),
            self.export_diagnostic_data_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_clockwise_dashes_settings_20_filled"),
            get_content_name_async("basic_settings", "export_settings"),
            get_content_description_async("basic_settings", "export_settings"),
            self.export_settings_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_clockwise_dashes_settings_20_filled"),
            get_content_name_async("basic_settings", "import_settings"),
            get_content_description_async("basic_settings", "import_settings"),
            self.import_settings_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_database_window_20_filled"),
            get_content_name_async("basic_settings", "export_all_data"),
            get_content_description_async("basic_settings", "export_all_data"),
            self.export_all_data_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_database_window_20_filled"),
            get_content_name_async("basic_settings", "import_all_data"),
            get_content_description_async("basic_settings", "import_all_data"),
            self.import_all_data_button,
        )
