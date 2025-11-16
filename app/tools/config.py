"""
通知工具模块
提供便捷的InfoBar通知功能，用于在应用程序中显示重要的用户信息
"""

import os
import json
import glob
from typing import Optional, Union
from loguru import logger

from PySide6.QtWidgets import QWidget
from PySide6.QtCore import Qt
from qfluentwidgets import InfoBar, InfoBarPosition, FluentIcon, InfoBarIcon


from app.tools.path_utils import get_resources_path
from app.tools.personalised import get_theme_icon
from app.tools.settings_access import readme_settings_async
from app.tools.list import get_student_list, get_group_list


# ======= 通知工具函数 =======
def show_success_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = 3000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """
    显示成功通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为顶部
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平

    Returns:
        InfoBar实例
    """
    return InfoBar.success(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_warning_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = -1,
    position: Union[InfoBarPosition, str] = InfoBarPosition.BOTTOM,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """
    显示警告通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为底部
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平

    Returns:
        InfoBar实例
    """
    return InfoBar.warning(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_error_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = 5000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.BOTTOM_RIGHT,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Vertical,
) -> InfoBar:
    """
    显示错误通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为右下角
        is_closable: 是否可关闭
        orient: 布局方向，默认为垂直(适合长内容)

    Returns:
        InfoBar实例
    """
    return InfoBar.error(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_info_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = -1,
    position: Union[InfoBarPosition, str] = InfoBarPosition.BOTTOM_LEFT,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """
    显示信息通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为左下角
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平

    Returns:
        InfoBar实例
    """
    return InfoBar.info(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_custom_notification(
    title: str,
    content: str,
    icon: Union[FluentIcon, InfoBarIcon, str] = InfoBarIcon.INFORMATION,
    parent: Optional[QWidget] = None,
    duration: int = 3000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
    background_color: Optional[str] = None,
    text_color: Optional[str] = None,
) -> InfoBar:
    """
    显示自定义通知

    Args:
        title: 通知标题
        content: 通知内容
        icon: 通知图标
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为顶部
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平
        background_color: 背景颜色
        text_color: 文本颜色

    Returns:
        InfoBar实例
    """
    info_bar = InfoBar.new(
        icon=icon,
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )

    if background_color and text_color:
        info_bar.setCustomBackgroundColor(background_color, text_color)

    return info_bar


class NotificationConfig:
    """通知配置类，用于定义通知的各种参数"""

    def __init__(
        self,
        title: str = "",
        content: str = "",
        icon: Union[FluentIcon, InfoBarIcon, str] = None,
        duration: int = 3000,
        position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
        is_closable: bool = True,
        orient: Qt.Orientation = Qt.Orientation.Horizontal,
        background_color: Optional[str] = None,
        text_color: Optional[str] = None,
    ):
        self.title = title
        self.content = content
        self.icon = icon
        self.duration = duration
        self.position = position
        self.is_closable = is_closable
        self.orient = orient
        self.background_color = background_color
        self.text_color = text_color


class NotificationType:
    """预定义的通知类型"""

    SUCCESS = "success"
    WARNING = "warning"
    ERROR = "error"
    INFO = "info"
    CUSTOM = "custom"


def show_notification(
    notification_type: str, config: NotificationConfig, parent: Optional[QWidget] = None
) -> InfoBar:
    """
    显示通知

    Args:
        notification_type: 通知类型，值为NotificationType中定义的常量
        config: 通知配置对象
        parent: 父窗口组件

    Returns:
        InfoBar实例
    """
    if notification_type == NotificationType.SUCCESS:
        return InfoBar.success(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.WARNING:
        return InfoBar.warning(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.ERROR:
        return InfoBar.error(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.INFO:
        return InfoBar.info(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.CUSTOM:
        info_bar = InfoBar.new(
            icon=config.icon or InfoBarIcon.INFORMATION,
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )

        if config.background_color and config.text_color:
            info_bar.setCustomBackgroundColor(
                config.background_color, config.text_color
            )

        return info_bar
    else:
        raise ValueError(f"不支持的通知类型: {notification_type}")


# ======= 获取清除记录前缀 =======
def check_clear_record():
    """检查是否需要清除已抽取记录"""
    clear_record = readme_settings_async("roll_call_settings", "clear_record")
    if clear_record == 0:  # 重启后清除
        prefix = "all"
    elif clear_record == 1:  # 直至全部抽取完
        prefix = "until"
    return prefix


# ======= 记录已抽取的学生 =======
def record_drawn_student(class_name: str, gender: str, group: str, student_name):
    """记录已抽取的学生名称和次数

    Args:
        class_name: 班级名称
        gender: 性别
        group: 分组
        student_name: 学生名称或学生列表
    """
    # 构建文件路径，与remove_record保持一致
    file_path = get_resources_path(
        "TEMP", f"draw_until_{class_name}_{gender}_{group}.json"
    )

    # 确保目录存在
    os.makedirs(os.path.dirname(file_path), exist_ok=True)

    # 读取现有记录
    drawn_records = _load_drawn_records(file_path)

    # 提取学生名称列表
    students_to_add = _extract_student_names(student_name)

    # 更新学生抽取次数
    updated_students = []
    for name in students_to_add:
        if name in drawn_records:
            # 学生已存在，增加抽取次数
            drawn_records[name] += 1
            updated_students.append(f"{name}(第{drawn_records[name]}次)")
        else:
            # 新学生，初始化抽取次数为1
            drawn_records[name] = 1
            updated_students.append(f"{name}(第1次)")

    # 保存更新后的记录
    if updated_students:
        _save_drawn_records(file_path, drawn_records)
        logger.debug(f"已记录学生/小组: {', '.join(updated_students)}")
    else:
        logger.debug("没有新的学生需要记录")


def _load_drawn_records(file_path: str) -> dict:
    """从文件加载已抽取的学生记录

    Args:
        file_path: 记录文件路径

    Returns:
        已抽取的学生记录字典，键为学生名称，值为抽取次数
    """
    if not os.path.exists(file_path):
        return {}

    try:
        with open(file_path, "r", encoding="utf-8") as file:
            data = json.load(file)

        drawn_records = {}

        # 处理不同的数据结构
        if isinstance(data, dict):
            # 新格式：字典，键为学生名称，值为抽取次数
            for name, count in data.items():
                if isinstance(name, str) and isinstance(count, int):
                    drawn_records[name] = count
        elif isinstance(data, list):
            # 旧格式：列表，只包含学生名称
            for item in data:
                if isinstance(item, str):
                    # 兼容旧格式，初始化抽取次数为1
                    drawn_records[item] = 1
                elif isinstance(item, dict) and "name" in item:
                    # 兼容可能的字典格式
                    name = item["name"]
                    count = item.get("count", 1)  # 默认次数为1
                    if isinstance(name, str) and isinstance(count, int):
                        drawn_records[name] = count
        elif isinstance(data, dict) and "drawn_names" in data:
            # 兼容可能的字典格式
            for item in data["drawn_names"]:
                if isinstance(item, str):
                    drawn_records[item] = 1
                elif isinstance(item, dict) and "name" in item:
                    name = item["name"]
                    count = item.get("count", 1)
                    if isinstance(name, str) and isinstance(count, int):
                        drawn_records[name] = count

        return drawn_records
    except (json.JSONDecodeError, IOError) as e:
        logger.error(f"读取已抽取记录失败: {e}")
        return {}


def _extract_student_names(student_name) -> list:
    """从不同类型的学生名称参数中提取学生名称列表

    Args:
        student_name: 学生名称或学生列表

    Returns:
        学生名称列表
    """
    if isinstance(student_name, str):
        # 单个学生名称
        return [student_name]

    if isinstance(student_name, list):
        # 学生列表，可能是元组列表或字符串列表
        names = []
        for item in student_name:
            if isinstance(item, str):
                names.append(item)
            elif isinstance(item, tuple) and len(item) >= 2:
                names.append(item[1])
        return names

    if isinstance(student_name, tuple) and len(student_name) >= 2:
        # 单个元组，提取第二个元素（名称）
        return [student_name[1]]

    return []


def _save_drawn_records(file_path: str, drawn_records: dict) -> None:
    """保存已抽取的学生记录到文件

    Args:
        file_path: 记录文件路径
        drawn_records: 已抽取的学生记录字典，键为学生名称，值为抽取次数
    """
    try:
        with open(file_path, "w", encoding="utf-8") as file:
            json.dump(drawn_records, file, ensure_ascii=False, indent=2)
    except IOError as e:
        logger.error(f"保存已抽取记录失败: {e}")


# ======= 读取已抽取记录 =======
def read_drawn_record(class_name: str, gender: str, group: str):
    """读取已抽取记录"""
    file_path = get_resources_path(
        "TEMP", f"draw_until_{class_name}_{gender}_{group}.json"
    )
    if os.path.exists(file_path):
        try:
            with open(file_path, "r", encoding="utf-8") as file:
                data = json.load(file)

            # 处理不同的数据结构
            if isinstance(data, dict):
                # 转换为列表格式，每个元素为(名称, 次数)元组
                drawn_records = [(name, count) for name, count in data.items()]
            elif isinstance(data, list):
                # 转换为列表格式，每个元素为(名称, 1)元组
                drawn_records = []
                for item in data:
                    if isinstance(item, str):
                        drawn_records.append((item, 1))
                    elif isinstance(item, dict) and "name" in item:
                        name = item["name"]
                        count = item.get("count", 1)
                        drawn_records.append((name, count))
            elif isinstance(data, dict) and "drawn_names" in data:
                # 兼容可能的字典格式
                drawn_records = []
                for item in data["drawn_names"]:
                    if isinstance(item, str):
                        drawn_records.append((item, 1))
                    elif isinstance(item, dict) and "name" in item:
                        name = item["name"]
                        count = item.get("count", 1)
                        drawn_records.append((name, count))
            else:
                drawn_records = []

            logger.debug(f"已读取{class_name}_{gender}_{group}已抽取记录")
            return drawn_records
        except (json.JSONDecodeError, IOError) as e:
            logger.error(f"读取已抽取记录失败: {e}")
            return []
    else:
        logger.debug(f"文件 {file_path} 不存在")
        return []


# ======= 重置已抽取记录 =======
def remove_record(class_name: str, gender: str, group: str, _prefix: str = "0"):
    """清除已抽取记录"""
    prefix = check_clear_record()
    if prefix == "all" and _prefix == "restart":
        prefix = "restart"

    logger.debug(f"清除记录前缀: {prefix}, _prefix: {_prefix}")

    if prefix == "all":
        # 构建搜索模式，匹配所有前缀的文件夹
        search_pattern = os.path.join(
            "app", "resources", "TEMP", f"draw_*_{class_name}_{gender}_{group}.json"
        )

        # 查找所有匹配的文件
        file_list = glob.glob(search_pattern)

        # 删除找到的文件
        for file_path in file_list:
            try:
                os.remove(file_path)
                file_name = os.path.basename(os.path.dirname(file_path))
                logger.info(f"已删除记录文件夹: {file_name}")
            except OSError as e:
                logger.error(f"删除文件{file_path}失败: {e}")
    elif prefix == "until":
        # 只删除特定前缀的文件
        file_path = get_resources_path(
            "TEMP", f"draw_{prefix}_{class_name}_{gender}_{group}.json"
        )
        try:
            if os.path.exists(file_path):
                os.remove(file_path)
                file_name = os.path.basename(os.path.dirname(file_path))
                logger.info(f"已删除记录文件夹: {file_name}")
        except OSError as e:
            logger.error(f"删除文件{file_path}失败: {e}")
    elif prefix == "restart":  # 重启后清除
        # 构建搜索模式，匹配所有前缀的文件夹
        search_pattern = os.path.join("app", "resources", "TEMP", "draw_*.json")
        # 查找所有匹配的文件
        file_list = glob.glob(search_pattern)
        # 删除找到的文件
        for file_path in file_list:
            try:
                os.remove(file_path)
                file_name = os.path.basename(os.path.dirname(file_path))
                logger.info(f"已删除记录文件夹: {file_name}")
            except OSError as e:
                logger.error(f"删除文件{file_path}失败: {e}")


def reset_drawn_record(self, class_name: str, gender: str, group: str):
    """删除已抽取记录文件"""
    clear_record = readme_settings_async("roll_call_settings", "clear_record")
    if clear_record in [0, 1]:  # 重启后清除、直至全部抽取完
        remove_record(class_name, gender, group)
        show_notification(
            NotificationType.INFO,
            NotificationConfig(
                title="提示",
                content=f"已重置{class_name}已抽取记录",
                icon=FluentIcon.INFO,
            ),
            parent=self,
        )
        logger.info(f"已重置{class_name}_{gender}_{group}已抽取记录")
    else:  # 重复抽取
        show_notification(
            NotificationType.INFO,
            NotificationConfig(
                title="提示",
                content=f"当前处于重复抽取状态，无需清除{class_name}已抽取记录",
                icon=get_theme_icon("ic_fluent_warning_20_filled"),
            ),
            parent=self,
        )
        logger.info(
            f"当前处于重复抽取状态，无需清除{class_name}_{gender}_{group}已抽取记录"
        )


# ======= 计算剩余人数 =======
def calculate_remaining_count(
    half_repeat: int,
    class_name: str,
    gender_filter: str,
    group_index: int,
    group_filter: str,
    total_count: int,
):
    """根据half_repeat设置计算实际剩余人数或组数

    Args:
        half_repeat: 重复抽取次数
        class_name: 班级名称
        gender_filter: 性别筛选条件
        group_index: 分组索引
        group_filter: 分组筛选条件
        total_count: 总人数或总组数

    Returns:
        实际剩余人数或组数
    """
    # 根据half_repeat设置计算实际剩余人数
    if half_repeat > 0:  # 只有当设置值大于0时才计算排除后的剩余人数
        # 读取已抽取记录
        drawn_records = read_drawn_record(class_name, gender_filter, group_filter)
        # 将记录转换为字典格式，便于快速查找
        drawn_counts = {}
        for record in drawn_records:
            if isinstance(record, tuple) and len(record) >= 2:
                # 处理元组格式：(名称, 次数)
                name, count = record[0], record[1]
                drawn_counts[name] = count
            elif isinstance(record, dict) and "name" in record:
                # 处理字典格式：{'name': 名称, 'count': 次数}
                name = record["name"]
                count = record.get("count", 1)
                drawn_counts[name] = count

        # 处理小组模式
        if group_index == 1:  # 全部小组
            # 获取所有小组列表
            group_list = get_group_list(class_name)
            
            # 计算已被排除的小组数量
            excluded_count = 0
            for group_name in group_list:
                # 如果小组已被抽取次数达到或超过设置值，则计入排除数量
                if (
                    group_name in drawn_counts
                    and drawn_counts[group_name] >= half_repeat
                ):
                    excluded_count += 1
            
            # 计算实际剩余组数
            return max(0, len(group_list) - excluded_count)
        else:
            # 处理学生模式
            # 计算已被排除的学生数量
            excluded_count = 0
            # 获取当前班级的学生列表
            student_list = get_student_list(class_name)
            for student in student_list:
                # 从学生字典中提取姓名
                student_name = (
                    student["name"]
                    if isinstance(student, dict) and "name" in student
                    else student
                )
                
                # 如果学生已被抽取次数达到或超过设置值，则计入排除数量
                if (
                    student_name in drawn_counts
                    and drawn_counts[student_name] >= half_repeat
                ):
                    excluded_count += 1

            # 计算实际剩余人数
            return max(0, total_count - excluded_count)
    else:
        # 如果half_repeat为0，则不排除任何学生或小组
        return total_count
