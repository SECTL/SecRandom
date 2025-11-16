# ==================================================
# 导入库
# ==================================================

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


# ==================================================
# 页面管理
# ==================================================
class sidebar_tray_management(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加侧边栏管理主界面组件
        self.sidebar_management_window = sidebar_management_window(self)
        self.vBoxLayout.addWidget(self.sidebar_management_window)

        # 添加侧边栏管理设置组件
        self.sidebar_management_settings = sidebar_management_settings(self)
        self.vBoxLayout.addWidget(self.sidebar_management_settings)

        # 添加托盘管理组件
        self.tray_management = tray_management(self)
        self.vBoxLayout.addWidget(self.tray_management)


class sidebar_management_window(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("sidebar_management_window", "title"))
        self.setBorderRadius(8)

        # 点名侧边栏位置下拉框
        self.roll_call_sidebar_position_comboBox = ComboBox(self)
        self.roll_call_sidebar_position_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_window", "roll_call_sidebar_position"
            )
        )
        self.roll_call_sidebar_position_comboBox.setCurrentIndex(
            readme_settings_async(
                "sidebar_management_window", "roll_call_sidebar_position"
            )
        )
        self.roll_call_sidebar_position_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_window",
                "roll_call_sidebar_position",
                self.roll_call_sidebar_position_comboBox.currentIndex(),
            )
        )

        # 自定义抽侧边栏位置下拉框
        self.custom_roll_call_sidebar_position_comboBox = ComboBox(self)
        self.custom_roll_call_sidebar_position_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_window", "custom_roll_call_sidebar_position"
            )
        )
        self.custom_roll_call_sidebar_position_comboBox.setCurrentIndex(
            readme_settings_async(
                "sidebar_management_window", "custom_roll_call_sidebar_position"
            )
        )
        self.custom_roll_call_sidebar_position_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_window",
                "custom_roll_call_sidebar_position",
                self.custom_roll_call_sidebar_position_comboBox.currentIndex(),
            )
        )

        # 抽奖侧边栏位置下拉框
        self.lottery_sidebar_position_comboBox = ComboBox(self)
        self.lottery_sidebar_position_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_window", "lottery_sidebar_position"
            )
        )
        self.lottery_sidebar_position_comboBox.setCurrentIndex(
            readme_settings_async(
                "sidebar_management_window", "lottery_sidebar_position"
            )
        )
        self.lottery_sidebar_position_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_window",
                "lottery_sidebar_position",
                self.lottery_sidebar_position_comboBox.currentIndex(),
            )
        )

        # 主窗口历史记录下拉框
        self.main_window_history_comboBox = ComboBox(self)
        self.main_window_history_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_window", "main_window_history"
            )
        )
        self.main_window_history_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_window", "main_window_history")
        )
        self.main_window_history_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_window",
                "main_window_history",
                self.main_window_history_comboBox.currentIndex(),
            )
        )

        # 设置图标下拉框
        self.settings_icon_comboBox = ComboBox(self)
        self.settings_icon_comboBox.addItems(
            get_content_combo_name_async("sidebar_management_window", "settings_icon")
        )
        self.settings_icon_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_window", "settings_icon")
        )
        self.settings_icon_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_window",
                "settings_icon",
                self.settings_icon_comboBox.currentIndex(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_people_community_20_filled"),
            get_content_name_async(
                "sidebar_management_window", "roll_call_sidebar_position"
            ),
            get_content_description_async(
                "sidebar_management_window", "roll_call_sidebar_position"
            ),
            self.roll_call_sidebar_position_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_receipt_20_filled"),
            get_content_name_async(
                "sidebar_management_window", "custom_roll_call_sidebar_position"
            ),
            get_content_description_async(
                "sidebar_management_window", "custom_roll_call_sidebar_position"
            ),
            self.custom_roll_call_sidebar_position_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_reward_20_filled"),
            get_content_name_async(
                "sidebar_management_window", "lottery_sidebar_position"
            ),
            get_content_description_async(
                "sidebar_management_window", "lottery_sidebar_position"
            ),
            self.lottery_sidebar_position_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_chat_history_20_filled"),
            get_content_name_async("sidebar_management_window", "main_window_history"),
            get_content_description_async(
                "sidebar_management_window", "main_window_history"
            ),
            self.main_window_history_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_settings_20_filled"),
            get_content_name_async("sidebar_management_window", "settings_icon"),
            get_content_description_async("sidebar_management_window", "settings_icon"),
            self.settings_icon_comboBox,
        )


class sidebar_management_settings(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("sidebar_management_settings", "title"))
        self.setBorderRadius(8)

        # 主页下拉框
        self.home_comboBox = ComboBox(self)
        self.home_comboBox.addItems(
            get_content_combo_name_async("sidebar_management_settings", "home")
        )
        self.home_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "home")
        )
        self.home_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings", "home", self.home_comboBox.currentIndex()
            )
        )

        # 基础设置下拉框
        self.base_settings_comboBox = ComboBox(self)
        self.base_settings_comboBox.addItems(
            get_content_combo_name_async("sidebar_management_settings", "base_settings")
        )
        self.base_settings_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "base_settings")
        )
        self.base_settings_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "base_settings",
                self.base_settings_comboBox.currentIndex(),
            )
        )

        # 名单管理下拉框
        self.name_management_comboBox = ComboBox(self)
        self.name_management_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_settings", "name_management"
            )
        )
        self.name_management_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "name_management")
        )
        self.name_management_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "name_management",
                self.name_management_comboBox.currentIndex(),
            )
        )

        # 抽取设置下拉框
        self.draw_settings_comboBox = ComboBox(self)
        self.draw_settings_comboBox.addItems(
            get_content_combo_name_async("sidebar_management_settings", "draw_settings")
        )
        self.draw_settings_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "draw_settings")
        )
        self.draw_settings_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "draw_settings",
                self.draw_settings_comboBox.currentIndex(),
            )
        )

        # 通知服务下拉框
        self.notification_service_comboBox = ComboBox(self)
        self.notification_service_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_settings", "notification_service"
            )
        )
        self.notification_service_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "notification_service")
        )
        self.notification_service_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "notification_service",
                self.notification_service_comboBox.currentIndex(),
            )
        )

        # 安全设置下拉框
        self.security_settings_comboBox = ComboBox(self)
        self.security_settings_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_settings", "security_settings"
            )
        )
        self.security_settings_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "security_settings")
        )
        self.security_settings_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "security_settings",
                self.security_settings_comboBox.currentIndex(),
            )
        )

        # 个性设置下拉框
        self.personal_settings_comboBox = ComboBox(self)
        self.personal_settings_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_settings", "personal_settings"
            )
        )
        self.personal_settings_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "personal_settings")
        )
        self.personal_settings_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "personal_settings",
                self.personal_settings_comboBox.currentIndex(),
            )
        )

        # 语音设置下拉框
        self.voice_settings_comboBox = ComboBox(self)
        self.voice_settings_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_settings", "voice_settings"
            )
        )
        self.voice_settings_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "voice_settings")
        )
        self.voice_settings_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "voice_settings",
                self.voice_settings_comboBox.currentIndex(),
            )
        )

        # 设置界面历史记录下拉框
        self.settings_history_comboBox = ComboBox(self)
        self.settings_history_comboBox.addItems(
            get_content_combo_name_async(
                "sidebar_management_settings", "settings_history"
            )
        )
        self.settings_history_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "settings_history")
        )
        self.settings_history_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "settings_history",
                self.settings_history_comboBox.currentIndex(),
            )
        )

        # 更多设置下拉框
        self.more_settings_comboBox = ComboBox(self)
        self.more_settings_comboBox.addItems(
            get_content_combo_name_async("sidebar_management_settings", "more_settings")
        )
        self.more_settings_comboBox.setCurrentIndex(
            readme_settings_async("sidebar_management_settings", "more_settings")
        )
        self.more_settings_comboBox.currentIndexChanged.connect(
            lambda: update_settings(
                "sidebar_management_settings",
                "more_settings",
                self.more_settings_comboBox.currentIndex(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_home_20_filled"),
            get_content_name_async("sidebar_management_settings", "home"),
            get_content_description_async("sidebar_management_settings", "home"),
            self.home_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_wrench_settings_20_filled"),
            get_content_name_async("sidebar_management_settings", "base_settings"),
            get_content_description_async(
                "sidebar_management_settings", "base_settings"
            ),
            self.base_settings_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_list_20_filled"),
            get_content_name_async("sidebar_management_settings", "name_management"),
            get_content_description_async(
                "sidebar_management_settings", "name_management"
            ),
            self.name_management_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_archive_20_filled"),
            get_content_name_async("sidebar_management_settings", "draw_settings"),
            get_content_description_async(
                "sidebar_management_settings", "draw_settings"
            ),
            self.draw_settings_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_service_bell_20_filled"),
            get_content_name_async(
                "sidebar_management_settings", "notification_service"
            ),
            get_content_description_async(
                "sidebar_management_settings", "notification_service"
            ),
            self.notification_service_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_shield_20_filled"),
            get_content_name_async("sidebar_management_settings", "security_settings"),
            get_content_description_async(
                "sidebar_management_settings", "security_settings"
            ),
            self.security_settings_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_person_edit_20_filled"),
            get_content_name_async("sidebar_management_settings", "personal_settings"),
            get_content_description_async(
                "sidebar_management_settings", "personal_settings"
            ),
            self.personal_settings_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_person_voice_20_filled"),
            get_content_name_async("sidebar_management_settings", "voice_settings"),
            get_content_description_async(
                "sidebar_management_settings", "voice_settings"
            ),
            self.voice_settings_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_chat_history_20_filled"),
            get_content_name_async("sidebar_management_settings", "settings_history"),
            get_content_description_async(
                "sidebar_management_settings", "settings_history"
            ),
            self.settings_history_comboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_more_horizontal_20_filled"),
            get_content_name_async("sidebar_management_settings", "more_settings"),
            get_content_description_async(
                "sidebar_management_settings", "more_settings"
            ),
            self.more_settings_comboBox,
        )


class tray_management(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("tray_management", "title"))
        self.setBorderRadius(8)

        # 暂时显示/隐藏主界面 按钮开关
        self.show_hide_main_window_switch = SwitchButton(self)
        self.show_hide_main_window_switch.setOffText(
            get_content_switchbutton_name_async(
                "tray_management", "show_hide_main_window", "disable"
            )
        )
        self.show_hide_main_window_switch.setOnText(
            get_content_switchbutton_name_async(
                "tray_management", "show_hide_main_window", "enable"
            )
        )
        self.show_hide_main_window_switch.setChecked(
            readme_settings_async("tray_management", "show_hide_main_window")
        )
        self.show_hide_main_window_switch.checkedChanged.connect(
            lambda: update_settings(
                "tray_management",
                "show_hide_main_window",
                self.show_hide_main_window_switch.isChecked(),
            )
        )

        # 打开设置界面 按钮开关
        self.open_settings_switch = SwitchButton(self)
        self.open_settings_switch.setOffText(
            get_content_switchbutton_name_async(
                "tray_management", "open_settings", "disable"
            )
        )
        self.open_settings_switch.setOnText(
            get_content_switchbutton_name_async(
                "tray_management", "open_settings", "enable"
            )
        )
        self.open_settings_switch.setChecked(
            readme_settings_async("tray_management", "open_settings")
        )
        self.open_settings_switch.checkedChanged.connect(
            lambda: update_settings(
                "tray_management",
                "open_settings",
                self.open_settings_switch.isChecked(),
            )
        )

        # 暂时显示/隐藏浮窗 按钮开关
        self.show_hide_float_window_switch = SwitchButton(self)
        self.show_hide_float_window_switch.setOffText(
            get_content_switchbutton_name_async(
                "tray_management", "show_hide_float_window", "disable"
            )
        )
        self.show_hide_float_window_switch.setOnText(
            get_content_switchbutton_name_async(
                "tray_management", "show_hide_float_window", "enable"
            )
        )
        self.show_hide_float_window_switch.setChecked(
            readme_settings_async("tray_management", "show_hide_float_window")
        )
        self.show_hide_float_window_switch.checkedChanged.connect(
            lambda: update_settings(
                "tray_management",
                "show_hide_float_window",
                self.show_hide_float_window_switch.isChecked(),
            )
        )

        # 重启 按钮开关
        self.restart_switch = SwitchButton(self)
        self.restart_switch.setOffText(
            get_content_switchbutton_name_async("tray_management", "restart", "disable")
        )
        self.restart_switch.setOnText(
            get_content_switchbutton_name_async("tray_management", "restart", "enable")
        )
        self.restart_switch.setChecked(
            readme_settings_async("tray_management", "restart")
        )
        self.restart_switch.checkedChanged.connect(
            lambda: update_settings(
                "tray_management", "restart", self.restart_switch.isChecked()
            )
        )

        # 退出 按钮开关
        self.exit_switch = SwitchButton(self)
        self.exit_switch.setOffText(
            get_content_switchbutton_name_async("tray_management", "exit", "disable")
        )
        self.exit_switch.setOnText(
            get_content_switchbutton_name_async("tray_management", "exit", "enable")
        )
        self.exit_switch.setChecked(readme_settings_async("tray_management", "exit"))
        self.exit_switch.checkedChanged.connect(
            lambda: update_settings(
                "tray_management", "exit", self.exit_switch.isChecked()
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_text_20_filled"),
            get_content_name_async("tray_management", "show_hide_main_window"),
            get_content_description_async("tray_management", "show_hide_main_window"),
            self.show_hide_main_window_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_settings_20_filled"),
            get_content_name_async("tray_management", "open_settings"),
            get_content_description_async("tray_management", "open_settings"),
            self.open_settings_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_window_ad_20_filled"),
            get_content_name_async("tray_management", "show_hide_float_window"),
            get_content_description_async("tray_management", "show_hide_float_window"),
            self.show_hide_float_window_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_reset_20_filled"),
            get_content_name_async("tray_management", "restart"),
            get_content_description_async("tray_management", "restart"),
            self.restart_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_exit_20_filled"),
            get_content_name_async("tray_management", "exit"),
            get_content_description_async("tray_management", "exit"),
            self.exit_switch,
        )
