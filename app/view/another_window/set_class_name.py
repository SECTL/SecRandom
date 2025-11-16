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


class SetClassNameWindow(QWidget):
    """班级名称设置窗口"""

    def __init__(self, parent=None):
        """初始化班级名称设置窗口"""
        super().__init__(parent)

        # 初始化变量
        self.saved = False
        self.initial_class_names = []  # 保存初始加载的班级列表

        # 初始化UI
        self.init_ui()

        # 连接信号
        self.__connect_signals()

    def init_ui(self):
        """初始化UI"""
        # 设置窗口标题
        self.setWindowTitle(get_content_name_async("set_class_name", "title"))

        # 创建主布局
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(20, 20, 20, 20)
        self.main_layout.setSpacing(15)

        # 创建标题
        self.title_label = TitleLabel(get_content_name_async("set_class_name", "title"))
        self.main_layout.addWidget(self.title_label)

        # 创建说明标签
        self.description_label = BodyLabel(
            get_content_name_async("set_class_name", "description")
        )
        self.description_label.setWordWrap(True)
        self.main_layout.addWidget(self.description_label)

        # 创建班级名称输入区域
        self.__create_class_name_input_area()

        # 创建按钮区域
        self.__create_button_area()

        # 添加伸缩项
        self.main_layout.addStretch(1)

    def __create_class_name_input_area(self):
        """创建班级名称输入区域"""
        # 创建卡片容器
        input_card = CardWidget()
        input_layout = QVBoxLayout(input_card)

        # 创建输入区域标题
        input_title = SubtitleLabel(
            get_content_name_async("set_class_name", "input_title")
        )
        input_layout.addWidget(input_title)

        # 创建文本编辑框
        self.text_edit = PlainTextEdit()
        self.text_edit.setPlaceholderText(
            get_content_name_async("set_class_name", "input_placeholder")
        )

        # 加载现有班级名称
        try:
            class_names = get_class_name_list()
            if class_names:
                self.text_edit.setPlainText("\n".join(class_names))
                self.initial_class_names = class_names.copy()  # 保存初始班级列表
        except Exception as e:
            logger.error(f"加载班级名称失败: {str(e)}")
            self.initial_class_names = []  # 出错时设为空列表

        input_layout.addWidget(self.text_edit)

        # 添加到主布局
        self.main_layout.addWidget(input_card)

    def __create_button_area(self):
        """创建按钮区域"""
        # 创建按钮布局
        button_layout = QHBoxLayout()

        # 伸缩项
        button_layout.addStretch(1)

        # 保存按钮
        self.save_button = PrimaryPushButton(
            get_content_name_async("set_class_name", "save_button")
        )
        self.save_button.setIcon(FluentIcon.SAVE)
        button_layout.addWidget(self.save_button)

        # 取消按钮
        self.cancel_button = PushButton(
            get_content_name_async("set_class_name", "cancel_button")
        )
        self.cancel_button.setIcon(FluentIcon.CANCEL)
        button_layout.addWidget(self.cancel_button)

        # 添加到主布局
        self.main_layout.addLayout(button_layout)

    def __connect_signals(self):
        """连接信号与槽"""
        self.save_button.clicked.connect(self.__save_class_names)
        self.cancel_button.clicked.connect(self.__cancel)
        self.text_edit.textChanged.connect(self.__on_text_changed)  # 添加文本变化监听

    def __on_text_changed(self):
        """检测文本变化，提示班级消失"""
        try:
            # 获取当前文本编辑框中的班级名称
            current_text = self.text_edit.toPlainText().strip()
            current_class_names = (
                [name.strip() for name in current_text.split("\n") if name.strip()]
                if current_text
                else []
            )

            # 检查是否有班级消失（存在于初始列表但不存在于当前列表）
            disappeared_classes = [
                name
                for name in self.initial_class_names
                if name not in current_class_names
            ]

            # 如果有班级消失，显示提示
            if disappeared_classes:
                if len(disappeared_classes) == 1:
                    # 单个班级消失
                    message = get_content_name_async(
                        "set_class_name", "class_disappeared_message"
                    ).format(class_name=disappeared_classes[0])
                else:
                    # 多个班级消失
                    message = get_content_name_async(
                        "set_class_name", "multiple_classes_disappeared_message"
                    ).format(
                        count=len(disappeared_classes),
                        class_names="\n".join(disappeared_classes),
                    )

                # 显示提示
                config = NotificationConfig(
                    title=get_content_name_async(
                        "set_class_name", "class_disappeared_title"
                    ),
                    content=message,
                    duration=3000,
                )
                show_notification(NotificationType.WARNING, config, parent=self)

                # 更新初始班级列表为当前列表，避免重复提示
                self.initial_class_names = current_class_names.copy()
        except Exception as e:
            logger.error(f"检测班级变化失败: {e}")

    def __save_class_names(self):
        """保存班级名称"""
        try:
            # 获取输入的班级名称
            class_names_text = self.text_edit.toPlainText().strip()
            if not class_names_text:
                # 显示错误消息
                config = NotificationConfig(
                    title=get_content_name_async("set_class_name", "error_title"),
                    content=get_content_name_async(
                        "set_class_name", "no_class_names_error"
                    ),
                    duration=3000,
                )
                show_notification(NotificationType.ERROR, config, parent=self)
                return

            # 分割班级名称
            class_names = [
                name.strip() for name in class_names_text.split("\n") if name.strip()
            ]

            # 验证班级名称
            invalid_names = []
            for name in class_names:
                # 检查是否包含非法字符
                if re.search(r'[\/:*?"<>|]', name):
                    invalid_names.append(name)
                # 检查是否为保留字
                elif name.lower() == "class":
                    invalid_names.append(name)

            if invalid_names:
                # 显示错误消息
                config = NotificationConfig(
                    title=get_content_name_async("set_class_name", "error_title"),
                    content=get_content_name_async(
                        "set_class_name", "invalid_names_error"
                    ).format(names=", ".join(invalid_names)),
                    duration=5000,
                )
                show_notification(NotificationType.ERROR, config, parent=self)
                return

            # 获取班级名单目录
            roll_call_list_dir = get_path("app/resources/list/roll_call_list")
            roll_call_list_dir.mkdir(parents=True, exist_ok=True)

            # 获取现有的班级名称
            existing_class_names = get_class_name_list()

            # 检查是否有被删除的班级
            deleted_classes = [
                name for name in existing_class_names if name not in class_names
            ]

            # 如果有被删除的班级，询问用户是否确认删除
            if deleted_classes:
                # 创建确认对话框
                if len(deleted_classes) == 1:
                    # 单个班级删除
                    dialog = MessageBox(
                        get_content_name_async("set_class_name", "delete_class_title"),
                        get_content_name_async(
                            "set_class_name", "delete_class_message"
                        ).format(class_name=deleted_classes[0]),
                        self,
                    )

                    dialog.yesButton.setText(
                        get_content_name_async("set_class_name", "delete_class_button")
                    )
                    dialog.cancelButton.setText(
                        get_content_name_async("set_class_name", "delete_cancel_button")
                    )
                else:
                    # 多个班级删除
                    dialog = MessageBox(
                        get_content_name_async(
                            "set_class_name", "delete_multiple_classes_title"
                        ),
                        get_content_name_async(
                            "set_class_name", "delete_multiple_classes_message"
                        ).format(
                            count=len(deleted_classes),
                            class_names="\n".join(deleted_classes),
                        ),
                        self,
                    )

                    dialog.yesButton.setText(
                        get_content_name_async("set_class_name", "delete_class_button")
                    )
                    dialog.cancelButton.setText(
                        get_content_name_async("set_class_name", "delete_cancel_button")
                    )

                # 显示对话框并获取用户选择
                if dialog.exec():
                    # 用户确认删除，删除班级文件
                    deleted_count = 0
                    for class_name in deleted_classes:
                        class_file = roll_call_list_dir / f"{class_name}.json"
                        if class_file.exists():
                            class_file.unlink()
                            deleted_count += 1

                    # 显示删除成功消息
                    if deleted_count > 0:
                        config = NotificationConfig(
                            title=get_content_name_async(
                                "set_class_name", "delete_success_title"
                            ),
                            content=get_content_name_async(
                                "set_class_name", "delete_success_message"
                            ).format(count=deleted_count),
                            duration=3000,
                        )
                        show_notification(NotificationType.SUCCESS, config, parent=self)
                else:
                    # 用户取消删除，不执行任何操作
                    return

            # 创建或更新班级文件
            created_count = 0
            for class_name in class_names:
                class_file = roll_call_list_dir / f"{class_name}.json"
                if not class_file.exists():
                    # 创建空的班级文件
                    with open_file(class_file, "w", encoding="utf-8") as f:
                        json.dump({}, f, ensure_ascii=False, indent=4)
                    created_count += 1

            # 显示成功消息
            if created_count > 0:
                config = NotificationConfig(
                    title=get_content_name_async("set_class_name", "success_title"),
                    content=get_content_name_async(
                        "set_class_name", "success_message"
                    ).format(count=created_count),
                    duration=3000,
                )
                show_notification(NotificationType.SUCCESS, config, parent=self)
            elif not deleted_classes:
                # 没有创建新班级也没有删除班级
                config = NotificationConfig(
                    title=get_content_name_async("set_class_name", "info_title"),
                    content=get_content_name_async(
                        "set_class_name", "no_new_classes_message"
                    ),
                    duration=3000,
                )
                show_notification(NotificationType.INFO, config, parent=self)

            # 标记为已保存
            self.saved = True

        except Exception as e:
            # 显示错误消息
            config = NotificationConfig(
                title=get_content_name_async("set_class_name", "error_title"),
                content=f"{get_content_name_async('set_class_name', 'save_error')}: {str(e)}",
                duration=3000,
            )
            show_notification(NotificationType.ERROR, config, parent=self)
            logger.error(f"保存班级名称失败: {e}")

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
                get_content_name_async("set_class_name", "unsaved_changes_title"),
                get_content_name_async("set_class_name", "unsaved_changes_message"),
                self,
            )

            dialog.yesButton.setText(
                get_content_name_async("set_class_name", "discard_button")
            )
            dialog.cancelButton.setText(
                get_content_name_async("set_class_name", "continue_editing_button")
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
