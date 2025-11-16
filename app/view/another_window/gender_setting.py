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


class GenderSettingWindow(QWidget):
    """性别设置窗口"""

    def __init__(self, parent=None):
        """初始化性别设置窗口"""
        super().__init__(parent)

        # 初始化变量
        self.saved = False
        self.initial_genders = []  # 保存初始加载的性别列表

        # 初始化UI
        self.init_ui()

        # 连接信号
        self.__connect_signals()

    def init_ui(self):
        """初始化UI"""
        # 设置窗口标题
        self.setWindowTitle(get_content_name_async("gender_setting", "title"))

        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(20, 20, 20, 20)
        self.main_layout.setSpacing(15)

        # 创建标题
        self.title_label = TitleLabel(get_content_name_async("gender_setting", "title"))
        self.main_layout.addWidget(self.title_label)

        # 创建说明标签
        self.description_label = BodyLabel(
            get_content_name_async("gender_setting", "description")
        )
        self.description_label.setWordWrap(True)
        self.main_layout.addWidget(self.description_label)

        # 创建性别输入区域
        self.__create_gender_input_area()

        # 创建按钮区域
        self.__create_button_area()

        # 添加伸缩项
        self.main_layout.addStretch(1)

    def __create_gender_input_area(self):
        """创建性别输入区域"""
        # 创建卡片容器
        input_card = CardWidget()
        input_layout = QVBoxLayout(input_card)

        # 创建输入区域标题
        input_title = SubtitleLabel(
            get_content_name_async("gender_setting", "input_title")
        )
        input_layout.addWidget(input_title)

        # 创建文本编辑框
        self.text_edit = PlainTextEdit()
        self.text_edit.setPlaceholderText(
            get_content_name_async("gender_setting", "input_placeholder")
        )

        # 加载现有性别
        existing_genders = self.__load_existing_genders()
        if existing_genders:
            self.text_edit.setPlainText("\n".join(existing_genders))

        input_layout.addWidget(self.text_edit)

        # 添加到主布局
        self.main_layout.addWidget(input_card)

    def __load_existing_genders(self):
        """加载现有性别"""
        try:
            # 获取班级名单目录
            roll_call_list_dir = get_path("app/resources/list/roll_call_list")

            # 从设置中获取班级名称
            class_name = readme_settings_async("roll_call_list", "select_class_name")
            list_file = roll_call_list_dir / f"{class_name}.json"

            # 如果文件不存在，返回空列表
            if not list_file.exists():
                self.initial_genders = []
                return []

            # 读取文件内容
            with open_file(list_file, "r", encoding="utf-8") as f:
                data = json.load(f)

            # 获取所有性别（从每个学生的gender字段）
            genders = []
            for student_name, student_info in data.items():
                if "gender" in student_info and student_info["gender"]:
                    genders.append(student_info["gender"])

            self.initial_genders = genders.copy()

            return genders

        except Exception as e:
            logger.error(f"加载性别失败: {str(e)}")
            self.initial_genders = []
            return []

    def __create_button_area(self):
        """创建按钮区域"""
        # 创建按钮布局
        button_layout = QHBoxLayout()

        # 伸缩项
        button_layout.addStretch(1)

        # 保存按钮
        self.save_button = PrimaryPushButton(
            get_content_name_async("gender_setting", "save_button")
        )
        self.save_button.setIcon(FluentIcon.SAVE)
        button_layout.addWidget(self.save_button)

        # 取消按钮
        self.cancel_button = PushButton(
            get_content_name_async("gender_setting", "cancel_button")
        )
        self.cancel_button.setIcon(FluentIcon.CANCEL)
        button_layout.addWidget(self.cancel_button)

        # 添加到主布局
        self.main_layout.addLayout(button_layout)

    def __connect_signals(self):
        """连接信号与槽"""
        self.save_button.clicked.connect(self.__save_genders)
        self.cancel_button.clicked.connect(self.__cancel)
        # 添加文本变化监听器
        self.text_edit.textChanged.connect(self.__on_text_changed)

    def __on_text_changed(self):
        """文本变化事件处理"""
        # 获取当前文本中的性别
        current_text = self.text_edit.toPlainText()
        current_genders = [
            gender.strip() for gender in current_text.split("\n") if gender.strip()
        ]

        # 检查哪些初始性别被删除了
        deleted_genders = [
            gender for gender in self.initial_genders if gender not in current_genders
        ]

        # 如果有性别被删除，显示提示
        if deleted_genders:
            for gender in deleted_genders:
                # 显示删除提示
                config = NotificationConfig(
                    title=get_content_name_async(
                        "gender_setting", "gender_deleted_title"
                    ),
                    content=get_content_name_async(
                        "gender_setting", "gender_deleted_message"
                    ).format(gender=gender),
                    duration=3000,
                )
                show_notification(NotificationType.INFO, config, parent=self)

            # 更新初始性别列表
            self.initial_genders = current_genders.copy()

    def __save_genders(self):
        """保存性别"""
        try:
            # 获取输入的性别
            genders_text = self.text_edit.toPlainText().strip()
            if not genders_text:
                # 显示错误消息
                config = NotificationConfig(
                    title=get_content_name_async("gender_setting", "error_title"),
                    content=get_content_name_async(
                        "gender_setting", "no_genders_error"
                    ),
                    duration=3000,
                )
                show_notification(NotificationType.ERROR, config, parent=self)
                return

            # 分割性别
            genders = [
                gender.strip() for gender in genders_text.split("\n") if gender.strip()
            ]

            # 验证性别
            invalid_genders = []
            for gender in genders:
                # 检查是否包含非法字符
                if re.search(r'[\/:*?"<>|]', gender):
                    invalid_genders.append(gender)
                # 检查是否为保留字
                elif gender.lower() == "class":
                    invalid_genders.append(gender)

            if invalid_genders:
                # 显示错误消息
                config = NotificationConfig(
                    title=get_content_name_async("gender_setting", "error_title"),
                    content=get_content_name_async(
                        "gender_setting", "invalid_genders_error"
                    ).format(genders=", ".join(invalid_genders)),
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

            # 如果性别没有新增
            existing_genders = []
            for student_name, student_info in existing_data.items():
                if "gender" in student_info and student_info["gender"]:
                    existing_genders.append(student_info["gender"])

            if set(genders) == set(existing_genders):
                # 显示提示消息
                config = NotificationConfig(
                    title=get_content_name_async("gender_setting", "info_title"),
                    content=get_content_name_async(
                        "gender_setting", "no_new_genders_message"
                    ),
                    duration=3000,
                )
                show_notification(NotificationType.INFO, config, parent=self)
                return

            # 更新现有数据中的性别信息
            updated_data = existing_data.copy()

            # 为每个学生更新性别信息
            for student_name in updated_data:
                # 如果学生没有性别字段，则添加空字段
                if "gender" not in updated_data[student_name]:
                    updated_data[student_name]["gender"] = ""

            # 保存到文件
            with open_file(list_file, "w", encoding="utf-8") as f:
                json.dump(updated_data, f, ensure_ascii=False, indent=4)

            # 显示保存成功通知
            config = NotificationConfig(
                title=get_content_name_async("gender_setting", "success_title"),
                content=get_content_name_async(
                    "gender_setting", "success_message"
                ).format(count=len(genders)),
                duration=3000,
            )
            show_notification(NotificationType.SUCCESS, config, parent=self)

            # 更新初始性别列表
            self.initial_genders = genders.copy()

            # 标记为已保存
            self.saved = True

        except Exception as e:
            # 显示错误消息
            config = NotificationConfig(
                title=get_content_name_async("gender_setting", "error_title"),
                content=f"{get_content_name_async('gender_setting', 'save_error')}: {str(e)}",
                duration=3000,
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.error(f"保存性别失败: {e}")

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
                get_content_name_async("gender_setting", "unsaved_changes_title"),
                get_content_name_async("gender_setting", "unsaved_changes_message"),
                self,
            )

            dialog.yesButton.setText(
                get_content_name_async("gender_setting", "discard_button")
            )
            dialog.cancelButton.setText(
                get_content_name_async("gender_setting", "continue_editing_button")
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
