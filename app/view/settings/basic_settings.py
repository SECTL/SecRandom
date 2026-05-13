# ==================================================
# 导入库
# ==================================================

from loguru import logger
import os
import ctypes
from PySide6.QtWidgets import QWidget, QVBoxLayout, QApplication, QHBoxLayout
from PySide6.QtGui import QFontDatabase
from PySide6.QtCore import QSignalBlocker
from qfluentwidgets import (
    GroupHeaderCardWidget,
    HeaderCardWidget,
    SwitchButton,
    ComboBox,
    PushButton,
    ColorConfigItem,
    ColorSettingCard,
    Theme,
    setTheme,
    setThemeColor,
    MessageBox,
    BodyLabel,
    IconWidget,
    InfoBarIcon,
)

from app.tools.variable import EXIT_CODE_RESTART

from app.tools.personalised import get_theme_icon
from app.tools.settings_access import readme_settings_async, update_settings
from app.Language.obtain_language import (
    get_all_languages_name,
    get_any_position_value_async,
    get_content_combo_name_async,
    get_content_description_async,
    get_content_name_async,
    get_content_pushbutton_name_async,
    get_content_switchbutton_name_async,
)
from app.tools.config import (
    export_diagnostic_data,
    export_settings,
    import_settings,
    export_all_data,
    import_all_data,
    set_autostart,
    show_notification,
    NotificationType,
    NotificationConfig,
)
from app.tools.online_status import (
    start_online_status_reporter,
    stop_online_status_reporter,
)
from app.common.IPC_URL import URLIPCHandler
from app.common.windows.uiaccess import is_uiaccess_process
from app.page_building.another_window import (
    create_log_viewer_window,
    create_backup_manager_window,
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

        # 添加版本通知卡片
        self.version_notice_widget = VersionNoticeCard(self)
        self.vBoxLayout.addWidget(self.version_notice_widget)

        # 添加基本功能设置组件
        self.basic_function_widget = basic_settings_function(self)
        self.vBoxLayout.addWidget(self.basic_function_widget)

        # 添加个性化设置组件
        self.personalised_widget = basic_settings_personalised(self)
        self.vBoxLayout.addWidget(self.personalised_widget)

        # 添加数据管理组件
        self.data_management_widget = basic_settings_data_management(self)
        self.vBoxLayout.addWidget(self.data_management_widget)

        # 添加遥测与隐私设置组件
        self.telemetry_widget = basic_settings_telemetry(self)
        self.vBoxLayout.addWidget(self.telemetry_widget)


class VersionNoticeCard(HeaderCardWidget):
    """版本通知卡片"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle("版本公告")

        self._visible = readme_settings_async(
            "basic_settings", "show_version_notice", True
        )
        if not self._visible:
            self.setVisible(False)
            return

        self.infoLabel = BodyLabel(
            "当前处于 Ava 重构阶段，暂不新增功能，重心放在性能优化与问题修复。\n敬请期待基于 Ava 架构的 v3 版本！\nbug报 GitHub Issue 或 SECTL QQ组织群 - 833875216",
            self,
        )
        self.infoIcon = IconWidget(InfoBarIcon.INFORMATION, self)
        self.closeButton = PushButton("不再显示", self)
        self.closeButton.setFixedWidth(100)
        self.closeButton.clicked.connect(self._on_close_clicked)

        self.vBoxLayout = QVBoxLayout()
        self.hBoxLayout = QHBoxLayout()

        self.infoIcon.setFixedSize(16, 16)
        self.hBoxLayout.setSpacing(10)
        self.vBoxLayout.setSpacing(8)
        self.hBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)

        self.hBoxLayout.addWidget(self.infoIcon)
        self.hBoxLayout.addWidget(self.infoLabel, 1)
        self.vBoxLayout.addLayout(self.hBoxLayout)
        self.vBoxLayout.addWidget(self.closeButton, 0)

        self.viewLayout.addLayout(self.vBoxLayout)

    def _on_close_clicked(self):
        update_settings("basic_settings", "show_version_notice", False)
        self.setVisible(False)


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
        self.autostart_switch.checkedChanged.connect(self.__on_autostart_changed)

        # 启动显示主窗口设置
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
            self.__on_show_startup_window_changed
        )

        # 自动保存窗口大小设置
        self.auto_save_window_size_switch = SwitchButton()
        self.auto_save_window_size_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "auto_save_window_size", "disable"
            )
        )
        self.auto_save_window_size_switch.setOnText(
            get_content_switchbutton_name_async(
                "basic_settings", "auto_save_window_size", "enable"
            )
        )
        _auto_save = readme_settings_async("basic_settings", "auto_save_window_size")
        self.auto_save_window_size_switch.setChecked(
            True if _auto_save is None else _auto_save
        )
        self.auto_save_window_size_switch.checkedChanged.connect(
            self.__on_auto_save_window_size_changed
        )

        # 主窗口置顶设置
        self.main_window_topmost_mode_combo_box = ComboBox()
        self.main_window_topmost_mode_combo_box.addItems(
            get_content_combo_name_async("basic_settings", "main_window_topmost_mode")
        )
        topmost_mode = readme_settings_async(
            "basic_settings", "main_window_topmost_mode"
        )
        self.main_window_topmost_mode_combo_box.setCurrentIndex(int(topmost_mode or 0))
        self.main_window_topmost_mode_combo_box.currentIndexChanged.connect(
            self.__on_main_window_topmost_mode_changed
        )

        # 后台驻留设置
        self.resident_switch = SwitchButton()
        self.resident_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "background_resident", "disable"
            )
        )
        self.resident_switch.setOnText(
            get_content_switchbutton_name_async(
                "basic_settings", "background_resident", "enable"
            )
        )
        _resident = readme_settings_async("basic_settings", "background_resident")
        self.resident_switch.setChecked(True if _resident is None else _resident)
        self.resident_switch.checkedChanged.connect(self.__on_resident_changed)

        # URL协议注册设置
        self.url_protocol_switch = SwitchButton()
        self.url_protocol_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "url_protocol", "disable"
            )
        )
        self.url_protocol_switch.setOnText(
            get_content_switchbutton_name_async(
                "basic_settings", "url_protocol", "enable"
            )
        )

        # 初始化URL IPC处理器
        self.url_ipc_handler = URLIPCHandler("SecRandom", "secrandom")

        # 检查协议是否已注册
        is_protocol_registered = self.url_ipc_handler.is_protocol_registered()
        self.url_protocol_switch.setChecked(is_protocol_registered)
        self.url_protocol_switch.checkedChanged.connect(self.__on_url_protocol_changed)

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_sync_20_filled"),
            get_content_name_async("basic_settings", "autostart"),
            get_content_description_async("basic_settings", "autostart"),
            self.autostart_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_window_20_filled"),
            get_content_name_async("basic_settings", "show_startup_window"),
            get_content_description_async("basic_settings", "show_startup_window"),
            self.show_startup_window_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_save_20_filled"),
            get_content_name_async("basic_settings", "auto_save_window_size"),
            get_content_description_async("basic_settings", "auto_save_window_size"),
            self.auto_save_window_size_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_pin_20_filled"),
            get_content_name_async("basic_settings", "main_window_topmost_mode"),
            get_content_description_async("basic_settings", "main_window_topmost_mode"),
            self.main_window_topmost_mode_combo_box,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_resize_20_filled"),
            get_content_name_async("basic_settings", "background_resident"),
            get_content_description_async("basic_settings", "background_resident"),
            self.resident_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_link_20_filled"),
            get_content_name_async("basic_settings", "url_protocol"),
            get_content_description_async("basic_settings", "url_protocol"),
            self.url_protocol_switch,
        )

    def __on_autostart_changed(self, checked):
        update_settings("basic_settings", "autostart", checked)
        ok = set_autostart(checked)
        if ok:
            if checked:
                show_notification(
                    NotificationType.SUCCESS,
                    NotificationConfig(
                        title=get_content_name_async("basic_settings", "autostart"),
                        content=get_any_position_value_async(
                            "basic_settings", "autostart_notification", "enable"
                        ),
                    ),
                    parent=self.window(),
                )
            else:
                show_notification(
                    NotificationType.INFO,
                    NotificationConfig(
                        title=get_content_name_async("basic_settings", "autostart"),
                        content=get_any_position_value_async(
                            "basic_settings", "autostart_notification", "disable"
                        ),
                    ),
                    parent=self.window(),
                )
        else:
            show_notification(
                NotificationType.ERROR,
                NotificationConfig(
                    title=get_content_name_async("basic_settings", "autostart"),
                    content=get_any_position_value_async(
                        "basic_settings", "autostart_notification", "failure"
                    ),
                ),
                parent=self.window(),
            )

    def __on_show_startup_window_changed(self, checked):
        update_settings("basic_settings", "show_startup_window", checked)
        if checked:
            show_notification(
                NotificationType.SUCCESS,
                NotificationConfig(
                    title=get_content_name_async(
                        "basic_settings", "show_startup_window"
                    ),
                    content=get_content_name_async(
                        "basic_settings", "success_enable_content"
                    ),
                ),
                parent=self.window(),
            )
        else:
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title=get_content_name_async(
                        "basic_settings", "show_startup_window"
                    ),
                    content=get_content_name_async(
                        "basic_settings", "info_disable_content"
                    ),
                ),
                parent=self.window(),
            )

    def __on_resident_changed(self, checked):
        update_settings("basic_settings", "background_resident", checked)
        try:
            app = QApplication.instance()
            if app:
                app.setQuitOnLastWindowClosed(not checked)
        except Exception:
            pass
        if checked:
            show_notification(
                NotificationType.SUCCESS,
                NotificationConfig(
                    title=get_content_name_async(
                        "basic_settings", "background_resident"
                    ),
                    content=get_any_position_value_async(
                        "basic_settings", "background_resident_notification", "enable"
                    ),
                ),
                parent=self.window(),
            )
        else:
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title=get_content_name_async(
                        "basic_settings", "background_resident"
                    ),
                    content=get_any_position_value_async(
                        "basic_settings", "background_resident_notification", "disable"
                    ),
                ),
                parent=self.window(),
            )

    def __on_auto_save_window_size_changed(self, checked):
        update_settings("basic_settings", "auto_save_window_size", checked)
        if checked:
            show_notification(
                NotificationType.SUCCESS,
                NotificationConfig(
                    title=get_content_name_async(
                        "basic_settings", "auto_save_window_size"
                    ),
                    content=get_any_position_value_async(
                        "basic_settings", "auto_save_window_size_notification", "enable"
                    ),
                ),
                parent=self.window(),
            )
        else:
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title=get_content_name_async(
                        "basic_settings", "auto_save_window_size"
                    ),
                    content=get_any_position_value_async(
                        "basic_settings",
                        "auto_save_window_size_notification",
                        "disable",
                    ),
                ),
                parent=self.window(),
            )

    def __on_main_window_topmost_mode_changed(self, index):
        previous_index = int(
            readme_settings_async("basic_settings", "main_window_topmost_mode") or 0
        )

        # 如果当前已经是 UIA 进程，切换到 UIA 模式不需要重启
        if index == 2 and is_uiaccess_process():
            update_settings("basic_settings", "main_window_topmost_mode", index)
            return

        # 如果当前是 UIA 进程，且浮窗也是 UIA 模式，切换回非 UIA 模式不需要重启（因为进程必须保持 UIA）
        if previous_index == 2 and index != 2 and is_uiaccess_process():
            floating_mode = int(
                readme_settings_async(
                    "floating_window_management", "floating_window_topmost_mode"
                )
                or 0
            )
            if floating_mode == 2:
                update_settings("basic_settings", "main_window_topmost_mode", index)
                return

        if previous_index == 2 and index != 2:
            dialog = MessageBox(
                get_content_name_async(
                    "basic_settings", "uia_topmost_restart_dialog_title"
                ),
                get_content_name_async(
                    "basic_settings",
                    "uia_topmost_disable_restart_dialog_content",
                ),
                self.window(),
            )
            dialog.yesButton.setText(
                get_content_name_async(
                    "basic_settings",
                    "uia_topmost_disable_restart_dialog_ok_btn",
                )
            )
            dialog.cancelButton.setText(
                get_content_name_async(
                    "basic_settings",
                    "uia_topmost_restart_dialog_cancel_btn",
                )
            )
            if dialog.exec():
                update_settings("basic_settings", "main_window_topmost_mode", index)
            else:
                blocker = QSignalBlocker(self.main_window_topmost_mode_combo_box)
                self.main_window_topmost_mode_combo_box.setCurrentIndex(previous_index)
                del blocker
            return

        if index == 2 and previous_index != 2:
            dialog = MessageBox(
                get_content_name_async(
                    "basic_settings", "uia_topmost_restart_dialog_title"
                ),
                get_content_name_async(
                    "basic_settings", "uia_topmost_restart_dialog_content"
                ),
                self.window(),
            )
            dialog.yesButton.setText(
                get_content_name_async(
                    "basic_settings",
                    "uia_topmost_restart_dialog_restart_btn",
                )
            )
            dialog.cancelButton.setText(
                get_content_name_async(
                    "basic_settings",
                    "uia_topmost_restart_dialog_cancel_btn",
                )
            )
            if dialog.exec():
                update_settings("basic_settings", "main_window_topmost_mode", index)
                try:
                    from app.common.windows.uiaccess import (
                        ELEVATE_RESTART_ENV,
                        UIACCESS_RESTART_ENV,
                    )

                    os.environ[str(UIACCESS_RESTART_ENV)] = "1"
                    try:
                        is_admin = bool(ctypes.windll.shell32.IsUserAnAdmin())
                    except Exception:
                        is_admin = False
                    if not is_admin:
                        os.environ[str(ELEVATE_RESTART_ENV)] = "1"
                except Exception:
                    pass
                QApplication.exit(EXIT_CODE_RESTART)
            else:
                blocker = QSignalBlocker(self.main_window_topmost_mode_combo_box)
                self.main_window_topmost_mode_combo_box.setCurrentIndex(previous_index)
                del blocker
            return

        update_settings("basic_settings", "main_window_topmost_mode", index)

    def __on_url_protocol_changed(self, checked):
        """URL协议开关变化处理"""
        # 临时断开信号连接，避免递归
        self.url_protocol_switch.checkedChanged.disconnect(
            self.__on_url_protocol_changed
        )

        try:
            if checked:
                # 注册URL协议
                success = self.url_ipc_handler.register_url_protocol()
                if success:
                    update_settings("basic_settings", "url_protocol", True)
                    show_notification(
                        NotificationType.SUCCESS,
                        NotificationConfig(
                            title=get_content_name_async(
                                "basic_settings", "url_protocol"
                            ),
                            content=get_any_position_value_async(
                                "basic_settings", "url_protocol_notification", "enable"
                            ),
                        ),
                        parent=self.window(),
                    )
                    # 更新开关状态为成功状态
                    self.url_protocol_switch.setChecked(True)
                else:
                    # 注册失败，保持原状态
                    self.url_protocol_switch.setChecked(False)
                    show_notification(
                        NotificationType.WARNING,
                        NotificationConfig(
                            title=get_content_name_async(
                                "basic_settings", "url_protocol"
                            ),
                            content=get_any_position_value_async(
                                "basic_settings",
                                "url_protocol_notification",
                                "register_failure",
                            ),
                        ),
                        parent=self.window(),
                    )
            else:
                # 注销URL协议
                success = self.url_ipc_handler.unregister_url_protocol()
                if success:
                    update_settings("basic_settings", "url_protocol", False)
                    show_notification(
                        NotificationType.INFO,
                        NotificationConfig(
                            title=get_content_name_async(
                                "basic_settings", "url_protocol"
                            ),
                            content=get_any_position_value_async(
                                "basic_settings", "url_protocol_notification", "disable"
                            ),
                        ),
                        parent=self.window(),
                    )
                    # 更新开关状态为成功状态
                    self.url_protocol_switch.setChecked(False)
                else:
                    # 注销失败，保持原状态
                    self.url_protocol_switch.setChecked(True)
                    show_notification(
                        NotificationType.WARNING,
                        NotificationConfig(
                            title=get_content_name_async(
                                "basic_settings", "url_protocol"
                            ),
                            content=get_any_position_value_async(
                                "basic_settings",
                                "url_protocol_notification",
                                "unregister_failure",
                            ),
                        ),
                        parent=self.window(),
                    )
        except Exception as e:
            logger.exception(f"URL协议设置错误: {e}")
            # 发生错误时恢复原状态
            self.url_protocol_switch.setChecked(not checked)

            # 获取错误提示文本，如果获取失败则使用默认文本
            error_text = get_any_position_value_async(
                "basic_settings", "url_protocol_notification", "error"
            )
            if error_text:
                content = error_text.format(error=str(e))

            show_notification(
                NotificationType.ERROR,
                NotificationConfig(
                    title=get_content_name_async("basic_settings", "url_protocol"),
                    content=content,
                ),
                parent=self.window(),
            )
        finally:
            # 重新连接信号
            self.url_protocol_switch.checkedChanged.connect(
                self.__on_url_protocol_changed
            )


class basic_settings_telemetry(GroupHeaderCardWidget):
    """遥测与隐私设置"""

    _MODES = ["full", "anonymous", "off"]

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("basic_settings", "telemetry_category"))
        self.setBorderRadius(8)

        # 遥测开关（主开关）
        self.telemetry_switch = SwitchButton()
        self.telemetry_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_settings", "telemetry", "disable"
            )
        )
        self.telemetry_switch.setOnText(
            get_content_switchbutton_name_async("basic_settings", "telemetry", "enable")
        )
        self.telemetry_switch.setChecked(
            readme_settings_async("basic_settings", "telemetry_enabled") is not False
        )
        self.telemetry_switch.checkedChanged.connect(self.__on_telemetry_changed)

        self.addGroup(
            get_theme_icon("ic_fluent_pulse_20_filled"),
            get_content_name_async("basic_settings", "telemetry"),
            get_content_description_async("basic_settings", "telemetry"),
            self.telemetry_switch,
        )

        # 在线状态上报模式
        self.telemetry_mode_combo = ComboBox()
        self.telemetry_mode_combo.addItems(
            get_content_combo_name_async("basic_settings", "telemetry_mode")
        )
        current_mode = (
            readme_settings_async("basic_settings", "telemetry_mode") or "full"
        )
        try:
            self.telemetry_mode_combo.setCurrentIndex(self._MODES.index(current_mode))
        except ValueError:
            self.telemetry_mode_combo.setCurrentIndex(0)
        self.telemetry_mode_combo.currentIndexChanged.connect(
            self.__on_telemetry_mode_changed
        )

        self.addGroup(
            get_theme_icon("ic_fluent_globe_20_filled"),
            get_content_name_async("basic_settings", "telemetry_mode"),
            get_content_description_async("basic_settings", "telemetry_mode"),
            self.telemetry_mode_combo,
        )

    def __on_telemetry_changed(self, checked):
        update_settings("basic_settings", "telemetry_enabled", checked)
        if checked:
            show_notification(
                NotificationType.SUCCESS,
                NotificationConfig(
                    title=get_content_name_async("basic_settings", "telemetry"),
                    content=get_any_position_value_async(
                        "basic_settings", "telemetry_notification", "enable"
                    ),
                ),
                parent=self.window(),
            )
        else:
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title=get_content_name_async("basic_settings", "telemetry"),
                    content=get_any_position_value_async(
                        "basic_settings", "telemetry_notification", "disable"
                    ),
                ),
                parent=self.window(),
            )

    def __on_telemetry_mode_changed(self, index):
        mode = self._MODES[index]
        update_settings("basic_settings", "telemetry_mode", mode)
        try:
            if mode == "off":
                stop_online_status_reporter()
            else:
                start_online_status_reporter()
        except Exception:
            pass


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

        # 字体粗细设置卡片
        self.fontWeightComboBox = ComboBox()
        font_weight_items = get_content_combo_name_async(
            "basic_settings", "font_weight"
        )
        self.fontWeightComboBox.addItems(font_weight_items)
        self.fontWeightComboBox.setCurrentIndex(
            int(readme_settings_async("basic_settings", "font_weight"))
        )
        self.fontWeightComboBox.currentIndexChanged.connect(
            lambda index: update_settings("basic_settings", "font_weight", index)
        )

        # 界面缩放设置卡片
        self.dpiScale = ComboBox()
        dpi_scale_items = get_content_combo_name_async("basic_settings", "dpiScale")
        self.dpiScale.addItems(dpi_scale_items)
        # 如果设置值是"Auto"，则显示最后一个选项；否则显示对应的值
        current_dpi_scale = readme_settings_async("basic_settings", "dpiScale")
        if current_dpi_scale == "Auto":
            self.dpiScale.setCurrentText(dpi_scale_items[-1])  # "自动"是最后一个选项
        else:
            self.dpiScale.setCurrentText(current_dpi_scale)
        self.dpiScale.currentTextChanged.connect(
            lambda scale: self.update_dpi_scale_setting(scale, dpi_scale_items)
        )

        # 主题色设置卡片
        self.themeColorCard = ColorSettingCard(
            self.themeColor,
            get_theme_icon("ic_fluent_color_20_filled"),
            self.tr(get_content_name_async("basic_settings", "theme_color")),
            self.tr(get_content_description_async("basic_settings", "theme_color")),
        )

        self.open_theme_management_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "open_theme_management")
        )
        self.open_theme_management_button.clicked.connect(
            self.__on_open_theme_management
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_dark_theme_20_filled"),
            get_content_name_async("basic_settings", "theme"),
            get_content_description_async("basic_settings", "theme"),
            self.theme,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_paint_brush_20_filled"),
            get_content_name_async("basic_settings", "open_theme_management"),
            get_content_description_async("basic_settings", "open_theme_management"),
            self.open_theme_management_button,
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
            get_theme_icon("ic_fluent_text_bold_20_filled"),
            get_content_name_async("basic_settings", "font_weight"),
            get_content_description_async("basic_settings", "font_weight"),
            self.fontWeightComboBox,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_zoom_fit_20_filled"),
            get_content_name_async("basic_settings", "dpiScale"),
            get_content_description_async("basic_settings", "dpiScale"),
            self.dpiScale,
        )
        # 添加卡片到布局
        self.vBoxLayout.addWidget(self.themeColorCard)

    def __on_open_theme_management(self):
        try:
            w = self.window()
            if hasattr(w, "showMainPageRequested"):
                w.showMainPageRequested.emit("settings_theme")
                return
            if hasattr(w, "_handle_settings_page_request"):
                w._handle_settings_page_request("themeManagementInterface")
        except Exception as e:
            logger.exception(f"打开主题管理失败: {e}")

    def update_dpi_scale_setting(self, scale, dpi_scale_items):
        """更新DPI缩放设置"""
        # 如果选择的是最后一个选项("自动")，则保存为"Auto"
        if scale == dpi_scale_items[-1]:
            update_settings("basic_settings", "dpiScale", "Auto")
        else:
            update_settings("basic_settings", "dpiScale", scale)


class basic_settings_data_management(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent = parent
        self.setTitle(get_content_name_async("basic_settings", "data_management"))
        self.setBorderRadius(8)

        # 导出诊断数据按钮
        self.export_diagnostic_data_button = PushButton(
            get_content_pushbutton_name_async(
                "basic_settings", "export_diagnostic_data"
            )
        )
        self.export_diagnostic_data_button.clicked.connect(
            lambda: export_diagnostic_data(self.window())
        )

        # 导出设置按钮
        self.export_settings_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "export_settings")
        )
        self.export_settings_button.clicked.connect(
            lambda: export_settings(self.window())
        )

        # 导入设置按钮
        self.import_settings_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "import_settings")
        )
        self.import_settings_button.clicked.connect(
            lambda: import_settings(self.window())
        )

        # 导出软件所有数据按钮
        self.export_all_data_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "export_all_data")
        )
        self.export_all_data_button.clicked.connect(
            lambda: export_all_data(self.window())
        )

        # 导入软件所有数据按钮
        self.import_all_data_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "import_all_data")
        )
        self.import_all_data_button.clicked.connect(
            lambda: import_all_data(self.window())
        )

        # 日志查看按钮
        self.log_viewer_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "log_viewer")
        )
        self.log_viewer_button.clicked.connect(self.open_log_viewer)

        self.backup_manager_button = PushButton(
            get_content_pushbutton_name_async("basic_settings", "backup_manager")
        )
        self.backup_manager_button.clicked.connect(self.open_backup_manager)

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_document_20_filled"),
            get_content_name_async("basic_settings", "log_viewer"),
            get_content_description_async("basic_settings", "log_viewer"),
            self.log_viewer_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_save_20_filled"),
            get_content_name_async("basic_settings", "backup_manager"),
            get_content_description_async("basic_settings", "backup_manager"),
            self.backup_manager_button,
        )
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

    def open_log_viewer(self):
        """打开日志查看窗口"""
        try:
            create_log_viewer_window()
        except Exception as e:
            logger.exception(f"打开日志查看窗口失败: {e}")
            show_notification(
                NotificationType.ERROR,
                NotificationConfig(
                    title=get_content_name_async("basic_settings", "log_viewer"),
                    content=f"打开日志查看窗口失败: {str(e)}",
                ),
                parent=self.window(),
            )

    def open_backup_manager(self):
        try:
            create_backup_manager_window()
        except Exception as e:
            logger.exception(f"打开备份管理窗口失败: {e}")
            show_notification(
                NotificationType.ERROR,
                NotificationConfig(
                    title=get_content_name_async("basic_settings", "backup_manager"),
                    content=f"打开备份管理窗口失败: {str(e)}",
                ),
                parent=self.window(),
            )
