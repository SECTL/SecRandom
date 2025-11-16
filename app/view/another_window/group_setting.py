# ==================================================
# 导入库
# ==================================================
import re
import json

from loguru import logger
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from qfluentwidgets import *

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from app.tools.config import *
from app.tools.list import *


class GroupSettingWindow(QWidget):
    """小组设置窗口"""

    def __init__(self, parent=None):
        """初始化小组设置窗口"""
        super().__init__(parent)

        # 初始化变量
        self.saved = False
        self.initial_groups = []  # 保存初始加载的小组列表

        # 初始化UI
        self.init_ui()

        # 连接信号
        self.__connect_signals()

    def init_ui(self):
        """初始化UI"""
        # 设置窗口标题
        self.setWindowTitle(get_content_name_async("group_setting", "title"))

        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(20, 20, 20, 20)
        self.main_layout.setSpacing(15)

        # 创建标题
        self.title_label = TitleLabel(get_content_name_async("group_setting", "title"))
        self.main_layout.addWidget(self.title_label)

        # 创建说明标签
        self.description_label = BodyLabel(
            get_content_name_async("group_setting", "description")
        )
        self.description_label.setWordWrap(True)
        self.main_layout.addWidget(self.description_label)

        # 创建小组输入区域
        self.__create_group_input_area()

        # 创建按钮区域
        self.__create_button_area()

        # 添加伸缩项
        self.main_layout.addStretch(1)

    def __create_group_input_area(self):
        """创建小组输入区域"""
        # 创建卡片容器
        input_card = CardWidget()
        input_layout = QVBoxLayout(input_card)

        # 创建输入区域标题
        input_title = SubtitleLabel(
            get_content_name_async("group_setting", "input_title")
        )
        input_layout.addWidget(input_title)

        # 创建文本编辑框
        self.text_edit = PlainTextEdit()
        self.text_edit.setPlaceholderText(
            get_content_name_async("group_setting", "input_placeholder")
        )

        # 加载现有小组
        existing_groups = self.__load_existing_groups()
        if existing_groups:
            self.text_edit.setPlainText("\n".join(existing_groups))

        input_layout.addWidget(self.text_edit)

        # 添加到主布局
        self.main_layout.addWidget(input_card)

    def __load_existing_groups(self):
        """加载现有小组"""
        try:
            # 获取班级名单目录
            roll_call_list_dir = get_path("app/resources/list/roll_call_list")

            # 从设置中获取班级名称
            class_name = readme_settings_async("roll_call_list", "select_class_name")
            list_file = roll_call_list_dir / f"{class_name}.json"

            # 如果文件不存在，返回空列表
            if not list_file.exists():
                self.initial_groups = []
                return []

            # 读取文件内容
            with open_file(list_file, "r", encoding="utf-8") as f:
                data = json.load(f)

            # 获取所有小组（从每个学生的group字段）
            groups = []
            for student_name, student_info in data.items():
                if "group" in student_info and student_info["group"]:
                    groups.append(student_info["group"])

            self.initial_groups = groups.copy()

            return groups

        except Exception as e:
            logger.error(f"加载小组失败: {str(e)}")
            self.initial_groups = []
            return []

    def __create_button_area(self):
        """创建按钮区域"""
        # 创建按钮布局
        button_layout = QHBoxLayout()

        # 伸缩项
        button_layout.addStretch(1)

        # 保存按钮
        self.save_button = PrimaryPushButton(
            get_content_name_async("group_setting", "save_button")
        )
        self.save_button.setIcon(FluentIcon.SAVE)
        button_layout.addWidget(self.save_button)

        # 取消按钮
        self.cancel_button = PushButton(
            get_content_name_async("group_setting", "cancel_button")
        )
        self.cancel_button.setIcon(FluentIcon.CANCEL)
        button_layout.addWidget(self.cancel_button)

        # 添加到主布局
        self.main_layout.addLayout(button_layout)

    def __connect_signals(self):
        """连接信号与槽"""
        self.save_button.clicked.connect(self.__save_groups)
        self.cancel_button.clicked.connect(self.__cancel)
        # 添加文本变化监听器
        self.text_edit.textChanged.connect(self.__on_text_changed)

    def __on_text_changed(self):
        """文本变化事件处理"""
        # 获取当前文本中的小组
        current_text = self.text_edit.toPlainText()
        current_groups = [
            group.strip() for group in current_text.split("\n") if group.strip()
        ]

        # 检查哪些初始小组被删除了
        deleted_groups = [
            group for group in self.initial_groups if group not in current_groups
        ]

        # 如果有小组被删除，显示提示
        if deleted_groups:
            for group in deleted_groups:
                # 显示删除提示
                config = NotificationConfig(
                    title=get_content_name_async(
                        "group_setting", "group_deleted_title"
                    ),
                    content=get_content_name_async(
                        "group_setting", "group_deleted_message"
                    ).format(group=group),
                    duration=3000,
                )
                show_notification(NotificationType.INFO, config, parent=self)

            # 更新初始小组列表
            self.initial_groups = current_groups.copy()

    def __save_groups(self):
        """保存小组"""
        try:
            # 获取输入的小组
            groups_text = self.text_edit.toPlainText().strip()
            if not groups_text:
                # 显示错误消息
                config = NotificationConfig(
                    title=get_content_name_async("group_setting", "error_title"),
                    content=get_content_name_async("group_setting", "no_groups_error"),
                    duration=3000,
                )
                show_notification(NotificationType.ERROR, config, parent=self)
                return

            # 分割小组
            groups = [
                group.strip() for group in groups_text.split("\n") if group.strip()
            ]

            # 验证小组
            invalid_groups = []
            for group in groups:
                # 检查是否包含非法字符
                if re.search(r'[\/:*?"<>|]', group):
                    invalid_groups.append(group)
                # 检查是否为保留字
                elif group.lower() == "class":
                    invalid_groups.append(group)

            if invalid_groups:
                # 显示错误消息
                config = NotificationConfig(
                    title=get_content_name_async("group_setting", "error_title"),
                    content=get_content_name_async(
                        "group_setting", "invalid_groups_error"
                    ).format(groups=", ".join(invalid_groups)),
                    duration=5000,
                )
                show_notification(NotificationType.ERROR, config, parent=self)
                return

            # 获取文件路径
            roll_call_list_dir = get_path("app/resources/list/roll_call_list")
            roll_call_list_dir.mkdir(parents=True, exist_ok=True)

            # 从设置中获取班级名称
            class_name = readme_settings_async("roll_call_list", "select_class_name")
            list_file = roll_call_list_dir / f"{class_name}.json"

            # 读取现有数据
            existing_data = {}
            if list_file.exists():
                with open_file(list_file, "r", encoding="utf-8") as f:
                    existing_data = json.load(f)

            # 如果小组没有新增
            existing_groups = []
            for student_name, student_info in existing_data.items():
                if "group" in student_info and student_info["group"]:
                    existing_groups.append(student_info["group"])

            if set(groups) == set(existing_groups):
                # 显示提示消息
                config = NotificationConfig(
                    title=get_content_name_async("group_setting", "info_title"),
                    content=get_content_name_async(
                        "group_setting", "no_new_groups_message"
                    ),
                    duration=3000,
                )
                show_notification(NotificationType.INFO, config, parent=self)
                return

            # 更新现有数据中的小组信息
            updated_data = existing_data.copy()

            # 为每个学生更新小组信息
            for student_name in updated_data:
                # 如果学生没有小组字段，则添加空字段
                if "group" not in updated_data[student_name]:
                    updated_data[student_name]["group"] = ""

            # 保存到文件
            with open_file(list_file, "w", encoding="utf-8") as f:
                json.dump(updated_data, f, ensure_ascii=False, indent=4)

            # 显示保存成功通知
            config = NotificationConfig(
                title=get_content_name_async("group_setting", "success_title"),
                content=get_content_name_async(
                    "group_setting", "success_message"
                ).format(count=len(groups)),
                duration=3000,
            )
            show_notification(NotificationType.SUCCESS, config, parent=self)

            # 更新初始小组列表
            self.initial_groups = groups.copy()

            # 标记为已保存
            self.saved = True

        except Exception as e:
            # 显示错误消息
            config = NotificationConfig(
                title=get_content_name_async("group_setting", "error_title"),
                content=f"{get_content_name_async('group_setting', 'save_error')}: {str(e)}",
                duration=3000,
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.error(f"保存小组失败: {e}")

    def __cancel(self):
        """取消操作"""
        # 获取父窗口并关闭
        parent = self.parent()
        while parent:
            # 查找SimpleWindowTemplate类型的父窗口
            if hasattr(parent, "windowClosed") and hasattr(parent, "close"):
                parent.close()
                break
            parent = parent.parent()

    def closeEvent(self, event):
        """窗口关闭事件处理"""
        if not self.saved:
            # 创建确认对话框
            dialog = Dialog(
                get_content_name_async("group_setting", "unsaved_changes_title"),
                get_content_name_async("group_setting", "unsaved_changes_message"),
                self,
            )

            dialog.yesButton.setText(
                get_content_name_async("group_setting", "discard_button")
            )
            dialog.cancelButton.setText(
                get_content_name_async("group_setting", "continue_editing_button")
            )

            # 显示对话框并获取用户选择
            if dialog.exec():
                # 用户选择放弃更改，关闭窗口
                event.accept()
            else:
                # 用户选择继续编辑，取消关闭事件
                event.ignore()
        else:
            # 已保存，直接关闭
            event.accept()
