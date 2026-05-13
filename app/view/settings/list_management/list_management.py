# ==================================================
# 导入库
# ==================================================

from loguru import logger
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
from app.tools.config import *
from app.common.data.list import *

from app.page_building.another_window import *
from .shared_file_watcher import get_shared_file_watcher


# ==================================================
# 名单管理
# ==================================================
class list_management(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 学生名单
        self.roll_call_list = roll_call_list(self)
        self.vBoxLayout.addWidget(self.roll_call_list)

        # 奖品名单
        self.lottery_list = lottery_list(self)
        self.vBoxLayout.addWidget(self.lottery_list)


class roll_call_list(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("roll_call_list", "title"))
        self.setBorderRadius(8)

        # 设置班级名称按钮
        self.class_name_button = PushButton(
            get_content_name_async("roll_call_list", "set_class_name")
        )
        self.class_name_button.clicked.connect(lambda: self.set_class_name())

        # 选择班级下拉框
        self.class_name_combo = ComboBox()

        # 导入学生名单按钮
        self.import_student_button = PushButton(
            get_content_name_async("roll_call_list", "import_student_name")
        )
        self.import_student_button.clicked.connect(lambda: self.import_student_name())

        # 姓名设置按钮
        self.name_setting_button = PushButton(
            get_content_name_async("roll_call_list", "name_setting")
        )
        self.name_setting_button.clicked.connect(lambda: self.name_setting())

        # 性别设置按钮
        self.gender_setting_button = PushButton(
            get_content_name_async("roll_call_list", "gender_setting")
        )
        self.gender_setting_button.clicked.connect(lambda: self.gender_setting())

        # 小组设置按钮
        self.group_setting_button = PushButton(
            get_content_name_async("roll_call_list", "group_setting")
        )
        self.group_setting_button.clicked.connect(lambda: self.group_setting())

        self.tag_setting_button = PushButton(
            get_content_name_async("roll_call_list", "tag_setting")
        )
        self.tag_setting_button.clicked.connect(lambda: self.tag_setting())

        # 导出学生名单按钮
        self.export_student_button = PushButton(
            get_content_name_async("roll_call_list", "export_student_name")
        )
        self.export_student_button.clicked.connect(lambda: self.export_student_list())

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_edit_20_filled"),
            get_content_name_async("roll_call_list", "set_class_name"),
            get_content_description_async("roll_call_list", "set_class_name"),
            self.class_name_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("roll_call_list", "select_class_name"),
            get_content_description_async("roll_call_list", "select_class_name"),
            self.class_name_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("roll_call_list", "import_student_name"),
            get_content_description_async("roll_call_list", "import_student_name"),
            self.import_student_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_rename_20_filled"),
            get_content_name_async("roll_call_list", "name_setting"),
            get_content_description_async("roll_call_list", "name_setting"),
            self.name_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_person_board_20_filled"),
            get_content_name_async("roll_call_list", "gender_setting"),
            get_content_description_async("roll_call_list", "gender_setting"),
            self.gender_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_tab_group_20_filled"),
            get_content_name_async("roll_call_list", "group_setting"),
            get_content_description_async("roll_call_list", "group_setting"),
            self.group_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_tag_20_filled"),
            get_content_name_async("roll_call_list", "tag_setting"),
            get_content_description_async("roll_call_list", "tag_setting"),
            self.tag_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("roll_call_list", "export_student_name"),
            get_content_description_async("roll_call_list", "export_student_name"),
            self.export_student_button,
        )

        # 初始化班级列表（在所有按钮都创建后）
        try:
            self.refresh_class_list()
            if not get_class_name_list():
                if (
                    hasattr(self, "class_name_combo")
                    and self.class_name_combo is not None
                ):
                    self.class_name_combo.setCurrentIndex(-1)
                    self.class_name_combo.setPlaceholderText(
                        get_content_name_async("roll_call_list", "select_class_name")
                    )
            else:
                if (
                    hasattr(self, "class_name_combo")
                    and self.class_name_combo is not None
                ):
                    self.class_name_combo.setCurrentText(
                        readme_settings_async("roll_call_list", "select_class_name")
                    )
        except RuntimeError as e:
            logger.exception(f"初始化班级列表时发生错误: {e}")
        except Exception as e:
            logger.exception(f"初始化班级列表时发生未知错误: {e}")
        if hasattr(self, "class_name_combo") and self.class_name_combo is not None:
            try:
                self.class_name_combo.currentIndexChanged.connect(
                    lambda: update_settings(
                        "roll_call_list",
                        "select_class_name",
                        self.class_name_combo.currentText(),
                    )
                )
            except RuntimeError as e:
                logger.exception(f"连接班级下拉框信号时发生错误: {e}")
            except Exception as e:
                logger.exception(f"连接班级下拉框信号时发生未知错误: {e}")

        # 设置文件系统监视器
        self.setup_file_watcher()

        # 初始化按钮状态
        self.update_button_states()

    # 班级名称设置
    def set_class_name(self):
        create_set_class_name_window(parent=self._get_window_parent())
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "roll_call",
                "class_name_setting",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "roll_call",
                "class_name_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 学生名单导入功能
    def import_student_name(self):
        class_name = self.class_name_combo.currentText()
        create_import_student_name_window(
            class_name=class_name, parent=self._get_window_parent()
        )
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "roll_call",
                "import_student_name",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "roll_call",
                "import_student_name",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 姓名设置
    def name_setting(self):
        create_name_setting_window(
            list_name=self.class_name_combo.currentText(),
            parent=self._get_window_parent(),
        )
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", "roll_call", "name_setting", "title", "name"
            ),
            content=get_any_position_value_async(
                "notification",
                "roll_call",
                "name_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 性别设置
    def gender_setting(self):
        create_gender_setting_window(
            list_name=self.class_name_combo.currentText(),
            parent=self._get_window_parent(),
        )
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "roll_call",
                "gender_setting",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "roll_call",
                "gender_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 小组设置
    def group_setting(self):
        create_group_setting_window(
            list_name=self.class_name_combo.currentText(),
            parent=self._get_window_parent(),
        )
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", "roll_call", "group_setting", "title", "name"
            ),
            content=get_any_position_value_async(
                "notification",
                "roll_call",
                "group_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    def tag_setting(self):
        create_student_tag_setting_window(
            list_name=self.class_name_combo.currentText(),
            parent=self._get_window_parent(),
        )
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", "roll_call", "tag_setting", "title", "name"
            ),
            content=get_any_position_value_async(
                "notification",
                "roll_call",
                "tag_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    def _get_window_parent(self):
        return None

    # 学生名单导出功能
    def export_student_list(self):
        class_name = self.class_name_combo.currentText()
        if not class_name:
            config = NotificationConfig(
                title="导出失败", content="请先选择要导出的班级", duration=3000
            )
            show_notification(NotificationType.WARNING, config, parent=self)
            return

        file_path, selected_filter = QFileDialog.getSaveFileName(
            self,
            get_any_position_value_async(
                "qfiledialog",
                "roll_call",
                "export_student_list",
                "caption",
                "name",
            ),
            f"{class_name}_学生名单-SecRandom",
            get_any_position_value_async(
                "qfiledialog",
                "roll_call",
                "export_student_list",
                "filter",
                "name",
            ),
        )

        if not file_path:
            return

        export_type = (
            "excel"
            if "Excel 文件 (*.xlsx)" in selected_filter
            else "csv"
            if "CSV 文件 (*.csv)" in selected_filter
            else "txt"
        )

        if export_type == "excel" and not file_path.endswith(".xlsx"):
            file_path += ".xlsx"
        elif export_type == "csv" and not file_path.endswith(".csv"):
            file_path += ".csv"
        elif export_type == "txt" and not file_path.endswith(".txt"):
            file_path += ".txt"

        success, message = export_student_data(class_name, file_path, export_type)

        if success:
            config = NotificationConfig(
                title=get_any_position_value_async(
                    "notification",
                    "roll_call",
                    "export",
                    "title",
                    "success",
                    "name",
                ),
                content=get_any_position_value_async(
                    "notification",
                    "roll_call",
                    "export",
                    "content",
                    "success",
                    "name",
                ).format(path=file_path),
                duration=3000,
            )
            show_notification(NotificationType.SUCCESS, config, parent=self)
            logger.info(f"学生名单导出成功: {file_path}")
        else:
            config = NotificationConfig(
                title=get_any_position_value_async(
                    "notification",
                    "roll_call",
                    "export",
                    "title",
                    "failure",
                    "name",
                ),
                content=get_any_position_value_async(
                    "notification",
                    "roll_call",
                    "export",
                    "content",
                    "error",
                    "name",
                ).format(message=message),
                duration=3000,
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.warning(f"学生名单导出失败: {message}")

    def setup_file_watcher(self):
        """设置文件系统监视器，监控班级名单文件夹的变化 - 使用共享监视器"""
        roll_call_list_dir = get_data_path("list", "roll_call_list")

        # 确保目录存在
        if not roll_call_list_dir.exists():
            logger.warning(f"班级名单文件夹不存在: {roll_call_list_dir}")
            return

        # 使用共享文件系统监视器管理器
        self._shared_watcher = get_shared_file_watcher()
        self._shared_watcher.add_watcher(
            str(roll_call_list_dir), self.on_directory_changed
        )

        # logger.debug(f"已设置共享文件监视器，监控目录: {roll_call_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成
        QTimer.singleShot(500, self.refresh_class_list)

    def update_button_states(self):
        """根据班级列表状态更新按钮禁用状态"""
        try:
            class_list = get_class_name_list()
            has_class = len(class_list) > 0

            # 为每个按钮单独保护，避免因为某个按钮被删除导致整个方法失败
            if (
                hasattr(self, "import_student_button")
                and self.import_student_button is not None
            ):
                try:
                    self.import_student_button.setEnabled(has_class)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "name_setting_button")
                and self.name_setting_button is not None
            ):
                try:
                    self.name_setting_button.setEnabled(has_class)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "gender_setting_button")
                and self.gender_setting_button is not None
            ):
                try:
                    self.gender_setting_button.setEnabled(has_class)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "group_setting_button")
                and self.group_setting_button is not None
            ):
                try:
                    self.group_setting_button.setEnabled(has_class)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "tag_setting_button")
                and self.tag_setting_button is not None
            ):
                try:
                    self.tag_setting_button.setEnabled(has_class)
                except RuntimeError:
                    pass

            if (
                hasattr(self, "export_student_button")
                and self.export_student_button is not None
            ):
                try:
                    self.export_student_button.setEnabled(has_class)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置
        except RuntimeError as e:
            logger.exception(f"更新按钮状态时发生错误: {e}")
        except Exception as e:
            logger.exception(f"更新按钮状态时发生未知错误: {e}")

    def cleanup_file_watcher(self):
        """清理文件系统监视器"""
        if hasattr(self, "_shared_watcher"):
            roll_call_list_dir = get_data_path("list", "roll_call_list")
            if roll_call_list_dir.exists():
                self._shared_watcher.remove_watcher(
                    str(roll_call_list_dir), self.on_directory_changed
                )

    def __del__(self):
        """析构函数，确保清理文件监视器"""
        try:
            self.cleanup_file_watcher()
        except Exception:
            pass

    def refresh_class_list(self):
        """刷新班级下拉框列表"""
        # 检查班级下拉框是否仍然有效
        if not hasattr(self, "class_name_combo") or self.class_name_combo is None:
            logger.debug("班级下拉框已被销毁，跳过刷新")
            return

        try:
            # 保存当前选中的班级名称
            current_class_name = None
            try:
                current_class_name = self.class_name_combo.currentText()
            except RuntimeError:
                logger.debug("班级下拉框已销毁，跳过获取当前文本")
                return

            # 获取最新的班级列表
            class_list = get_class_name_list()

            # 清空并重新添加班级列表
            try:
                self.class_name_combo.clear()
            except RuntimeError:
                logger.debug("班级下拉框已销毁，跳过清空操作")
                return

            try:
                self.class_name_combo.addItems(class_list)
            except RuntimeError:
                logger.debug("班级下拉框已销毁，跳过添加项目")
                return

            # 尝试恢复之前选中的班级
            try:
                if current_class_name and current_class_name in class_list:
                    index = class_list.index(current_class_name)
                    self.class_name_combo.setCurrentIndex(index)
                elif not class_list:
                    self.class_name_combo.setCurrentIndex(-1)
                    self.class_name_combo.setPlaceholderText(
                        get_content_name_async("roll_call_list", "select_class_name")
                    )
            except RuntimeError:
                logger.debug("班级下拉框已销毁，跳过设置选中项")
                return

            # 更新按钮状态
            self.update_button_states()

            # logger.debug(f"班级列表已刷新，共 {len(class_list)} 个班级")
        except RuntimeError as e:
            logger.exception(f"刷新班级列表时发生错误: {e}")
        except Exception as e:
            logger.exception(f"刷新班级列表时发生未知错误: {e}")


class lottery_list(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("lottery_list", "title"))
        self.setBorderRadius(8)

        # 设置班级名称按钮
        self.pool_name_button = PushButton(
            get_content_name_async("lottery_list", "set_pool_name")
        )
        self.pool_name_button.clicked.connect(lambda: self.set_pool_name())

        # 选择奖池下拉框
        self.pool_name_combo = ComboBox()

        # 导入奖品名单按钮
        self.import_prize_button = PushButton(
            get_content_name_async("lottery_list", "import_prize_name")
        )
        self.import_prize_button.clicked.connect(lambda: self.import_prize_name())

        # 奖品设置按钮
        self.prize_setting_button = PushButton(
            get_content_name_async("lottery_list", "prize_setting")
        )
        self.prize_setting_button.clicked.connect(lambda: self.prize_setting())

        # 奖品权重设置按钮
        self.prize_weight_setting_button = PushButton(
            get_content_name_async("lottery_list", "prize_weight_setting")
        )
        self.prize_weight_setting_button.clicked.connect(
            lambda: self.prize_weight_setting()
        )

        self.tag_setting_button = PushButton(
            get_content_name_async("lottery_list", "tag_setting")
        )
        self.tag_setting_button.clicked.connect(lambda: self.tag_setting())

        # 奖品数量设置按钮
        self.prize_count_setting_button = PushButton(
            get_content_name_async("lottery_list", "prize_count_setting")
        )
        self.prize_count_setting_button.clicked.connect(
            lambda: self.prize_count_setting()
        )

        # 导出奖品名单按钮
        self.export_prize_button = PushButton(
            get_content_name_async("lottery_list", "export_prize_name")
        )
        self.export_prize_button.clicked.connect(lambda: self.export_prize_name())

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_edit_20_filled"),
            get_content_name_async("lottery_list", "set_pool_name"),
            get_content_description_async("lottery_list", "set_pool_name"),
            self.pool_name_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("lottery_list", "select_pool_name"),
            get_content_description_async("lottery_list", "select_pool_name"),
            self.pool_name_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("lottery_list", "import_prize_name"),
            get_content_description_async("lottery_list", "import_prize_name"),
            self.import_prize_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_rename_20_filled"),
            get_content_name_async("lottery_list", "prize_setting"),
            get_content_description_async("lottery_list", "prize_setting"),
            self.prize_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_person_board_20_filled"),
            get_content_name_async("lottery_list", "prize_weight_setting"),
            get_content_description_async("lottery_list", "prize_weight_setting"),
            self.prize_weight_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_tag_20_filled"),
            get_content_name_async("lottery_list", "tag_setting"),
            get_content_description_async("lottery_list", "tag_setting"),
            self.tag_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_person_board_20_filled"),
            get_content_name_async("lottery_list", "prize_count_setting"),
            get_content_description_async("lottery_list", "prize_count_setting"),
            self.prize_count_setting_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("lottery_list", "export_prize_name"),
            get_content_description_async("lottery_list", "export_prize_name"),
            self.export_prize_button,
        )

        # 初始化奖池列表（在所有按钮都创建后）
        try:
            self.refresh_pool_list()
            saved_pool = readme_settings_async("lottery_list", "select_pool_name")
            try:
                if isinstance(saved_pool, int):
                    if 0 <= saved_pool < self.pool_name_combo.count():
                        self.pool_name_combo.setCurrentIndex(saved_pool)
                elif isinstance(saved_pool, str) and saved_pool:
                    self.pool_name_combo.setCurrentText(saved_pool)
            except Exception:
                pass
            if not get_pool_name_list():
                if (
                    hasattr(self, "pool_name_combo")
                    and self.pool_name_combo is not None
                ):
                    self.pool_name_combo.setCurrentIndex(-1)
                    self.pool_name_combo.setPlaceholderText(
                        get_content_name_async("lottery_list", "select_pool_name")
                    )
        except RuntimeError as e:
            logger.exception(f"初始化奖池列表时发生错误: {e}")
        except Exception as e:
            logger.exception(f"初始化奖池列表时发生未知错误: {e}")
        if hasattr(self, "pool_name_combo") and self.pool_name_combo is not None:
            try:
                self.pool_name_combo.currentIndexChanged.connect(
                    lambda: update_settings(
                        "lottery_list",
                        "select_pool_name",
                        self.pool_name_combo.currentText(),
                    )
                )
            except RuntimeError as e:
                logger.exception(f"连接奖池下拉框信号时发生错误: {e}")
            except Exception as e:
                logger.exception(f"连接奖池下拉框信号时发生未知错误: {e}")

        # 设置文件系统监视器
        self.setup_file_watcher()

        # 初始化按钮状态
        self.update_button_states()

    # 奖池名称设置
    def set_pool_name(self):
        create_set_pool_name_window()
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "lottery",
                "pool_name_setting",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "lottery",
                "pool_name_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 奖品名单导入功能
    def import_prize_name(self):
        pool_name = self.pool_name_combo.currentText()
        create_import_prize_name_window(pool_name=pool_name)
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "lottery",
                "import_prize_name",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "lottery",
                "import_prize_name",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 奖品设置
    def prize_setting(self):
        create_prize_setting_window(list_name=self.pool_name_combo.currentText())
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", "lottery", "prize_setting", "title", "name"
            ),
            content=get_any_position_value_async(
                "notification",
                "lottery",
                "prize_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 奖品权重设置
    def prize_weight_setting(self):
        create_prize_weight_setting_window(list_name=self.pool_name_combo.currentText())
        # 显示通知
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "lottery",
                "prize_weight_setting",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "lottery",
                "prize_weight_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    def tag_setting(self):
        create_prize_tag_setting_window(list_name=self.pool_name_combo.currentText())
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification", "lottery", "tag_setting", "title", "name"
            ),
            content=get_any_position_value_async(
                "notification",
                "lottery",
                "tag_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    def prize_count_setting(self):
        create_prize_count_setting_window(list_name=self.pool_name_combo.currentText())
        config = NotificationConfig(
            title=get_any_position_value_async(
                "notification",
                "lottery",
                "prize_count_setting",
                "title",
                "name",
            ),
            content=get_any_position_value_async(
                "notification",
                "lottery",
                "prize_count_setting",
                "content",
                "name",
            ),
            duration=3000,
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 奖品名单导出功能
    def export_prize_name(self):
        pool_name = self.pool_name_combo.currentText()
        if not pool_name:
            config = NotificationConfig(
                title="导出失败", content="请先选择要导出的奖池", duration=3000
            )
            show_notification(NotificationType.WARNING, config, parent=self)
            return

        file_path, selected_filter = QFileDialog.getSaveFileName(
            self,
            get_any_position_value_async(
                "qfiledialog",
                "lottery",
                "export_prize_name",
                "caption",
                "name",
            ),
            f"{pool_name}_奖品名单-SecRandom",
            get_any_position_value_async(
                "qfiledialog",
                "lottery",
                "export_prize_name",
                "filter",
                "name",
            ),
        )

        if not file_path:
            return

        export_type = (
            "excel"
            if "Excel 文件 (*.xlsx)" in selected_filter
            else "csv"
            if "CSV 文件 (*.csv)" in selected_filter
            else "txt"
        )

        if export_type == "excel" and not file_path.endswith(".xlsx"):
            file_path += ".xlsx"
        elif export_type == "csv" and not file_path.endswith(".csv"):
            file_path += ".csv"
        elif export_type == "txt" and not file_path.endswith(".txt"):
            file_path += ".txt"

        success, message = export_prize_data(pool_name, file_path, export_type)

        if success:
            config = NotificationConfig(
                title=get_any_position_value_async(
                    "notification",
                    "lottery",
                    "export",
                    "title",
                    "success",
                    "name",
                ),
                content=get_any_position_value_async(
                    "notification",
                    "lottery",
                    "export",
                    "content",
                    "success",
                    "name",
                ).format(path=file_path),
                duration=3000,
            )
            show_notification(NotificationType.SUCCESS, config, parent=self)
            logger.info(f"奖品名单导出成功: {file_path}")
        else:
            config = NotificationConfig(
                title=get_any_position_value_async(
                    "notification",
                    "lottery",
                    "export",
                    "title",
                    "failure",
                    "name",
                ),
                content=get_any_position_value_async(
                    "notification",
                    "lottery",
                    "export",
                    "content",
                    "error",
                    "name",
                ).format(message=message),
                duration=3000,
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.warning(f"奖品名单导出失败: {message}")

    def setup_file_watcher(self):
        """设置文件系统监视器，监控奖池名单文件夹的变化 - 使用共享监视器"""
        # 获取奖池名单文件夹路径
        lottery_list_dir = get_data_path("list/lottery_list")

        # 确保目录存在
        if not lottery_list_dir.exists():
            logger.warning(f"奖池名单文件夹不存在: {lottery_list_dir}")
            return

        # 使用共享文件系统监视器管理器
        self._shared_watcher = get_shared_file_watcher()
        self._shared_watcher.add_watcher(
            str(lottery_list_dir), self.on_directory_changed
        )

        # logger.debug(f"已设置共享文件监视器，监控目录: {lottery_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成导致的错误
        QTimer.singleShot(500, self.refresh_pool_list)

    def update_button_states(self):
        """根据奖池列表状态更新按钮禁用状态"""
        try:
            pool_list = get_pool_name_list()
            has_pool = len(pool_list) > 0

            # 为每个按钮单独保护，避免因为某个按钮被删除导致整个方法失败
            if (
                hasattr(self, "import_prize_button")
                and self.import_prize_button is not None
            ):
                try:
                    self.import_prize_button.setEnabled(has_pool)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "prize_setting_button")
                and self.prize_setting_button is not None
            ):
                try:
                    self.prize_setting_button.setEnabled(has_pool)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "prize_weight_setting_button")
                and self.prize_weight_setting_button is not None
            ):
                try:
                    self.prize_weight_setting_button.setEnabled(has_pool)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置

            if (
                hasattr(self, "tag_setting_button")
                and self.tag_setting_button is not None
            ):
                try:
                    self.tag_setting_button.setEnabled(has_pool)
                except RuntimeError:
                    pass

            if (
                hasattr(self, "prize_count_setting_button")
                and self.prize_count_setting_button is not None
            ):
                try:
                    self.prize_count_setting_button.setEnabled(has_pool)
                except RuntimeError:
                    pass

            if (
                hasattr(self, "export_prize_button")
                and self.export_prize_button is not None
            ):
                try:
                    self.export_prize_button.setEnabled(has_pool)
                except RuntimeError:
                    pass  # 按钮已删除，跳过设置
        except RuntimeError as e:
            logger.exception(f"更新奖池按钮状态时发生错误: {e}")
        except Exception as e:
            logger.exception(f"更新奖池按钮状态时发生未知错误: {e}")

    def cleanup_file_watcher(self):
        """清理文件系统监视器"""
        if hasattr(self, "_shared_watcher"):
            lottery_list_dir = get_data_path("list/lottery_list")
            if lottery_list_dir.exists():
                self._shared_watcher.remove_watcher(
                    str(lottery_list_dir), self.on_directory_changed
                )

    def __del__(self):
        """析构函数，确保清理文件监视器"""
        try:
            self.cleanup_file_watcher()
        except Exception:
            pass

    def refresh_pool_list(self):
        """刷新奖池下拉框列表"""
        # 检查奖池下拉框是否仍然有效
        if not hasattr(self, "pool_name_combo") or self.pool_name_combo is None:
            logger.debug("奖池下拉框已被销毁，跳过刷新")
            return

        try:
            # 保存当前选中的奖池名称
            current_pool_name = None
            try:
                current_pool_name = self.pool_name_combo.currentText()
            except RuntimeError:
                logger.debug("奖池下拉框已销毁，跳过获取当前文本")
                return

            # 获取最新的奖池列表
            pool_list = get_pool_name_list()

            # 清空并重新添加奖池列表
            try:
                self.pool_name_combo.clear()
            except RuntimeError:
                logger.debug("奖池下拉框已销毁，跳过清空操作")
                return

            try:
                self.pool_name_combo.addItems(pool_list)
            except RuntimeError:
                logger.debug("奖池下拉框已销毁，跳过添加项目")
                return

            # 尝试恢复之前选中的奖池
            try:
                if current_pool_name and current_pool_name in pool_list:
                    index = pool_list.index(current_pool_name)
                    self.pool_name_combo.setCurrentIndex(index)
                elif not pool_list:
                    self.pool_name_combo.setCurrentIndex(-1)
                    self.pool_name_combo.setPlaceholderText(
                        get_content_name_async("lottery_list", "select_pool_name")
                    )
            except RuntimeError:
                logger.debug("奖池下拉框已销毁，跳过设置选中项")
                return

            # 更新按钮状态
            self.update_button_states()

            # logger.debug(f"奖池列表已刷新，共 {len(pool_list)} 个奖池")
        except RuntimeError as e:
            logger.exception(f"刷新奖池列表时发生错误: {e}")
        except Exception as e:
            logger.exception(f"刷新奖池列表时发生未知错误: {e}")
