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
from app.tools.list import *

from app.page_building.another_window import *


# ==================================================
# 点名名单
# ==================================================
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
        self.refresh_class_list()  # 初始化班级列表
        if not get_class_name_list():
            self.class_name_combo.setCurrentIndex(-1)
            self.class_name_combo.setPlaceholderText(
                get_content_name_async("roll_call_list", "select_class_name")
            )
        else:
            self.class_name_combo.setCurrentText(
                readme_settings_async("roll_call_list", "select_class_name")
            )
        self.class_name_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "roll_call_list",
                "select_class_name",
                self.class_name_combo.currentText(),
            )
        )

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
            get_theme_icon("ic_fluent_people_list_20_filled"),
            get_content_name_async("roll_call_list", "export_student_name"),
            get_content_description_async("roll_call_list", "export_student_name"),
            self.export_student_button,
        )

        # 设置文件系统监视器
        self.setup_file_watcher()

    # 班级名称设置
    def set_class_name(self):
        create_set_class_name_window()
        # 显示通知
        config = NotificationConfig(
            title="班级名称设置", content="已打开班级名称设置窗口", duration=3000
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 学生名单导入功能
    def import_student_name(self):
        create_import_student_name_window()
        # 显示通知
        config = NotificationConfig(
            title="学生名单导入", content="已打开学生名单导入窗口", duration=3000
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 姓名设置
    def name_setting(self):
        create_name_setting_window()
        # 显示通知
        config = NotificationConfig(
            title="姓名设置", content="已打开姓名设置窗口", duration=3000
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 性别设置
    def gender_setting(self):
        create_gender_setting_window()
        # 显示通知
        config = NotificationConfig(
            title="性别设置", content="已打开性别设置窗口", duration=3000
        )
        show_notification(NotificationType.INFO, config, parent=self)

    # 小组设置
    def group_setting(self):
        create_group_setting_window()
        # 显示通知
        config = NotificationConfig(
            title="小组设置", content="已打开小组设置窗口", duration=3000
        )
        show_notification(NotificationType.INFO, config, parent=self)

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
            "保存学生名单",
            f"{class_name}_学生名单-SecRandom",
            "Excel 文件 (*.xlsx);;CSV 文件 (*.csv);;TXT 文件（仅姓名） (*.txt)",
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
                title="导出成功",
                content=f"学生名单已导出到: {file_path}",
                duration=3000,
            )
            show_notification(NotificationType.SUCCESS, config, parent=self)
            logger.info(f"学生名单导出成功: {file_path}")
        else:
            config = NotificationConfig(
                title="导出失败", content=message, duration=3000
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.error(f"学生名单导出失败: {message}")

    def setup_file_watcher(self):
        """设置文件系统监视器，监控班级名单文件夹的变化"""
        roll_call_list_dir = get_path("app/resources/list/roll_call_list")

        # 确保目录存在
        if not roll_call_list_dir.exists():
            logger.warning(f"班级名单文件夹不存在: {roll_call_list_dir}")
            return

        # 创建文件系统监视器
        self.file_watcher = QFileSystemWatcher()

        # 监视目录
        self.file_watcher.addPath(str(roll_call_list_dir))

        # 连接信号
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)
        # logger.debug(f"已设置文件监视器，监控目录: {roll_call_list_dir}")

    def on_directory_changed(self, path):
        """当目录内容发生变化时调用此方法

        Args:
            path: 发生变化的目录路径
        """
        # logger.debug(f"检测到目录变化: {path}")
        # 延迟刷新，避免文件操作未完成
        QTimer.singleShot(500, self.refresh_class_list)

    def refresh_class_list(self):
        """刷新班级下拉框列表"""
        # 保存当前选中的班级名称
        current_class_name = self.class_name_combo.currentText()

        # 获取最新的班级列表
        class_list = get_class_name_list()

        # 清空并重新添加班级列表
        self.class_name_combo.clear()
        self.class_name_combo.addItems(class_list)

        # 尝试恢复之前选中的班级
        if current_class_name and current_class_name in class_list:
            index = class_list.index(current_class_name)
            self.class_name_combo.setCurrentIndex(index)
        elif not class_list:
            self.class_name_combo.setCurrentIndex(-1)
            self.class_name_combo.setPlaceholderText(
                get_content_name_async("roll_call_list", "select_class_name")
            )

        # logger.debug(f"班级列表已刷新，共 {len(class_list)} 个班级")
