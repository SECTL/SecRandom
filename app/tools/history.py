# ==================================================
# 导入库
# ==================================================
import json
import math
from datetime import datetime
from typing import Dict, List, Any, Optional, Union
from pathlib import Path

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
from app.tools.list import *

from random import SystemRandom

system_random = SystemRandom()

# ==================================================
# 历史记录文件路径处理函数
# ==================================================


def get_history_file_path(history_type: str, file_name: str) -> Path:
    """获取历史记录文件路径

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        file_name: 文件名（不含扩展名）

    Returns:
        Path: 历史记录文件路径
    """
    history_dir = get_path(f"app/resources/history/{history_type}_history")
    history_dir.mkdir(parents=True, exist_ok=True)
    return history_dir / f"{file_name}.json"


# ==================================================
# 历史记录数据读写函数
# ==================================================


def load_history_data(history_type: str, file_name: str) -> Dict[str, Any]:
    """加载历史记录数据

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        file_name: 文件名（不含扩展名）

    Returns:
        Dict[str, Any]: 历史记录数据
    """
    file_path = get_history_file_path(history_type, file_name)

    if not file_path.exists():
        return {}

    try:
        with open(file_path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception as e:
        logger.error(f"加载历史记录数据失败: {e}")
        return {}


def save_history_data(history_type: str, file_name: str, data: Dict[str, Any]) -> bool:
    """保存历史记录数据

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        file_name: 文件名（不含扩展名）
        data: 要保存的数据

    Returns:
        bool: 保存是否成功
    """
    file_path = get_history_file_path(history_type, file_name)
    try:
        with open(file_path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=4)
        return True
    except Exception as e:
        logger.error(f"保存历史记录数据失败: {e}")
        return False


def get_all_history_names(history_type: str) -> List[str]:
    """获取所有历史记录名称列表

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)

    Returns:
        List[str]: 历史记录名称列表
    """
    try:
        history_dir = get_path(f"app/resources/history/{history_type}_history")
        if not history_dir.exists():
            return []
        history_files = list(history_dir.glob("*.json"))
        names = [file.stem for file in history_files]
        names.sort()
        return names
    except Exception as e:
        logger.error(f"获取历史记录名称列表失败: {e}")
        return []


# ==================================================
# 历史记录统计函数
# ==================================================


def get_name_history(history_type: str, class_name: str) -> int:
    """获取指定班级的名称历史记录数量

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        class_name: 班级名称/奖池名称

    Returns:
        int: 名称历史记录数量
    """
    if history_type == "roll_call":
        student_list = get_student_list(class_name)
        return len(student_list) if student_list else 0
    elif history_type == "lottery":
        student_list = get_pool_list(class_name)
        return len(student_list) if student_list else 0
    else:
        return 0


def get_draw_sessions_history(history_type: str, class_name: str) -> int:
    """获取指定班级的抽取会话历史记录数量

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        class_name: 班级名称

    Returns:
        int: 抽取会话历史记录数量
    """
    history_data = load_history_data(history_type, class_name)
    session_count = 0
    if history_type == "roll_call":
        key = "students"
    elif history_type == "lottery":
        key = "lotterys"
    else:
        return 0
    students_dict = history_data.get(key, {})
    if isinstance(students_dict, dict):
        for student_name, student_info in students_dict.items():
            session_list = student_info.get("history", [])
            if isinstance(session_list, list):
                session_count += len(session_list)
    return session_count


def get_individual_statistics(
    history_type: str, class_name: str, students_name: str
) -> int:
    """获取指定班级的个人统计记录数量

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        class_name: 班级名称/奖池名称
        students_name: 学生姓名/奖品名称

    Returns:
        int: 个人统计记录数量
    """
    history_data = load_history_data(history_type, class_name)
    if history_type == "roll_call":
        key = "students"
    elif history_type == "lottery":
        key = "lotterys"
    else:
        return 0
    students_dict = history_data.get(key, {})
    if not isinstance(students_dict, dict):
        return 0
    student_info = students_dict.get(students_name)
    if not student_info:
        return 0
    student_history = student_info.get("history", [])
    if not isinstance(student_history, list):
        return 0
    return len(student_history)


# ==================================================
# 权重格式化函数
# ==================================================
def format_weight_for_display(weights_data: list, weight_key: str = "weight") -> tuple:
    """格式化权重显示，确保小数点对齐

    Args:
        weights_data: 包含权重数据的列表
        weight_key: 权重在数据项中的键名，默认为'weight'

    Returns:
        tuple: (格式化函数, 整数部分最大长度, 小数部分最大长度)
    """
    # 计算权重显示的最大长度，考虑小数点前后的对齐
    max_int_length = 0  # 整数部分最大长度
    max_dec_length = 2  # 固定为两位小数

    for item in weights_data:
        weight = item.get(weight_key, 0)
        weight_str = str(weight)
        if "." in weight_str:
            int_part, _ = weight_str.split(".", 1)
            max_int_length = max(max_int_length, len(int_part))
        else:
            max_int_length = max(max_int_length, len(weight_str))

    # 格式化权重显示，确保小数点对齐并保留两位小数
    def format_weight(weight):
        weight_str = str(weight)
        if "." in weight_str:
            int_part, dec_part = weight_str.split(".", 1)
            # 确保小数部分有两位
            if len(dec_part) < 2:
                dec_part = dec_part.ljust(2, "0")
            elif len(dec_part) > 2:
                dec_part = dec_part[:2]  # 截断多余的小数位
            # 整数部分右对齐，小数部分左对齐
            formatted_int = int_part.rjust(max_int_length)
            # 小数部分补齐到最大长度
            formatted_dec = dec_part.ljust(max_dec_length)
            return f"{formatted_int}.{formatted_dec}"
        else:
            # 没有小数点的情况，添加小数点和两位小数
            formatted_int = weight_str.rjust(max_int_length)
            formatted_dec = "00".ljust(max_dec_length)
            return f"{formatted_int}.{formatted_dec}"

    return format_weight, max_int_length, max_dec_length


# ==================================================
# 公平抽取权重计算函数
# ==================================================
def calculate_weight(students_data: list, class_name: str) -> list:
    """计算学生权重

    Args:
        students_data: 学生数据列表
        class_name: 班级名称

    Returns:
        list: 更新后的学生数据列表，包含权重信息
    """
    # 从设置中加载权重相关配置
    settings = {
        "fair_draw_enabled": readme_settings_async("advanced_settings", "fair_draw"),
        "fair_draw_group_enabled": readme_settings_async(
            "advanced_settings", "fair_draw_group"
        ),
        "fair_draw_gender_enabled": readme_settings_async(
            "advanced_settings", "fair_draw_gender"
        ),
        "fair_draw_time_enabled": readme_settings_async(
            "advanced_settings", "fair_draw_time"
        ),
        "base_weight": readme_settings_async("advanced_settings", "base_weight"),
        "min_weight": readme_settings_async("advanced_settings", "min_weight"),
        "max_weight": readme_settings_async("advanced_settings", "max_weight"),
    }

    # 加载历史记录数据
    history_data = load_history_data("roll_call", class_name)

    # 冷启动轮次配置
    COLD_START_ROUNDS = 10
    current_stats = history_data.get("total_stats", 0)
    is_cold_start = current_stats < COLD_START_ROUNDS

    # 初始化权重数据结构
    weight_data = {}
    for student in students_data:
        student_id = student.get("id", student.get("name", ""))
        weight_data[student_id] = {
            "total_count": 0,
            "group_count": 0,
            "gender_count": 0,
            "last_drawn_time": None,
            "rounds_missed": 0,
        }

    # 从历史记录中提取权重信息
    if isinstance(history_data, dict) and "students" in history_data:
        students_history = history_data.get("students", {})
        if isinstance(students_history, dict):
            for student_name, student_info in students_history.items():
                if student_name in weight_data and isinstance(student_info, dict):
                    # 更新总计数和最后抽取时间
                    weight_data[student_name]["total_count"] = student_info.get(
                        "total_count", 0
                    )
                    weight_data[student_name]["rounds_missed"] = student_info.get(
                        "rounds_missed", 0
                    )

                    last_drawn_time = student_info.get("last_drawn_time", "")
                    if last_drawn_time:
                        weight_data[student_name]["last_drawn_time"] = last_drawn_time

                    # 从历史记录中获取小组和性别信息
                    history = student_info.get("history", [])
                    if isinstance(history, list):
                        for record in history:
                            if isinstance(record, dict):
                                # 更新小组计数
                                draw_group = record.get("draw_group", "")
                                if (
                                    draw_group
                                    and draw_group
                                    != get_content_combo_name_async(
                                        "roll_call", "range_combobox"
                                    )[0]
                                ):
                                    weight_data[student_name]["group_count"] += 1

                                # 更新性别计数
                                draw_gender = record.get("draw_gender", "")
                                if (
                                    draw_gender
                                    and draw_gender
                                    != get_content_combo_name_async(
                                        "roll_call", "gender_combobox"
                                    )[0]
                                ):
                                    weight_data[student_name]["gender_count"] += 1

    # 获取小组和性别统计
    group_stats = history_data.get("group_stats", {})
    gender_stats = history_data.get("gender_stats", {})

    # 为每个学生计算权重
    for student in students_data:
        student_id = student.get("id", student.get("name", ""))
        if student_id not in weight_data:
            continue

        student_weight_data = weight_data[student_id]

        # 计算频率惩罚因子 - 采用更平滑的平方根倒数函数
        if settings["fair_draw_enabled"]:
            # 使用平方根倒数函数，避免除零错误，提供更平滑的权重变化
            frequency_factor = 1.0 / math.sqrt(
                student_weight_data["total_count"] * 2 + 1
            )

            # 冷启动特殊处理 - 防止新学生权重过低
            if is_cold_start:
                frequency_factor = min(0.8, frequency_factor)

            # 转换为与新实现兼容的权重值
            frequency_penalty = frequency_factor * 3.0
        else:
            frequency_penalty = 0.0

        # 计算小组平衡因子 - 采用倒数函数
        if settings["fair_draw_group_enabled"]:
            # 获取学生当前小组
            current_student_group = student.get("group", "")

            # 获取有效小组统计数量（值>0的条目）
            valid_groups = [v for v in group_stats.values() if v > 0]

            if len(valid_groups) > 3:  # 有效小组数量达标
                group_history = max(group_stats.get(current_student_group, 0), 0)
                group_factor = 1.0 / (group_history * 0.2 + 1)
                group_balance = group_factor * 0.8
            else:
                # 数据不足时，使用相对计数方法
                all_counts = [data["group_count"] for data in weight_data.values()]
                max_group_count = max(all_counts) if all_counts else 0

                if max_group_count == 0:
                    group_balance = (
                        0.2  # 给所有学生一个小组平衡的基础权重提升，保持公平
                    )
                elif student_weight_data["group_count"] == 0:
                    group_balance = 0.5  # 给从未在小组中被选中的学生一个基础权重提升
                else:
                    group_balance = 0.8 * (
                        1.0 - (student_weight_data["group_count"] / max_group_count)
                    )
        else:
            group_balance = 0.0

        # 计算性别平衡因子 - 采用倒数函数
        if settings["fair_draw_gender_enabled"]:
            # 获取学生当前性别
            current_student_gender = student.get("gender", "")

            # 获取有效性别统计数量（值>0的条目）
            valid_gender = [v for v in gender_stats.values() if v > 0]

            if len(valid_gender) > 3:  # 有效性别数量达标
                gender_history = max(gender_stats.get(current_student_gender, 0), 0)
                gender_factor = 1.0 / (gender_history * 0.2 + 1)
                gender_balance = gender_factor * 0.8
            else:
                # 数据不足时，使用相对计数方法
                all_counts = [data["gender_count"] for data in weight_data.values()]
                max_gender_count = max(all_counts) if all_counts else 0

                if max_gender_count == 0:
                    gender_balance = (
                        0.2  # 给所有学生一个性别平衡的基础权重提升，保持公平
                    )
                elif student_weight_data["gender_count"] == 0:
                    gender_balance = (
                        0.5  # 给从未在性别平衡中被选中的学生一个基础权重提升
                    )
                else:
                    gender_balance = 0.8 * (
                        1.0 - (student_weight_data["gender_count"] / max_gender_count)
                    )
        else:
            gender_balance = 0.0

        # 计算时间因子
        if (
            settings["fair_draw_time_enabled"]
            and student_weight_data["last_drawn_time"]
        ):
            try:
                from datetime import datetime

                last_time = datetime.fromisoformat(
                    student_weight_data["last_drawn_time"]
                )
                current_time = datetime.now()
                days_diff = (current_time - last_time).days
                time_factor = min(1.0, days_diff / 30.0) * 0.5
            except:
                time_factor = 0.0
        else:
            time_factor = 0.0

        # 计算总权重 - 采用旧实现的权重分配方式
        student_weights = {
            "base": settings["base_weight"] * 0.2,  # 基础权重占比20%
            "frequency": frequency_penalty,  # 频率惩罚
            "group": group_balance,  # 小组平衡
            "gender": gender_balance,  # 性别平衡
            "time": time_factor,  # 时间因子
        }

        total_weight = sum(student_weights.values())

        # 确保权重在最小和最大值之间
        total_weight = max(
            settings["min_weight"], min(settings["max_weight"], total_weight)
        )
        total_weight = round(total_weight, 2)

        # 设置学生的权重和详细信息
        student["next_weight"] = total_weight
        student["weight_details"] = {
            "base_weight": student_weights["base"],
            "frequency_penalty": student_weights["frequency"],
            "group_balance": student_weights["group"],
            "gender_balance": student_weights["gender"],
            "time_factor": student_weights["time"],
            "total_weight": total_weight,
            "is_cold_start": is_cold_start,
        }

    return students_data


# ==================================================
# 历史记录保存与更新函数
# ==================================================
def save_roll_call_history(
    class_name: str,
    selected_students: List[Dict[str, Any]],
    group_filter: Optional[str] = None,
    gender_filter: Optional[str] = None,
) -> bool:
    """保存点名历史记录

    Args:
        class_name: 班级名称
        selected_students: 被选中的学生列表
        students_dict_list: 完整的学生列表，用于计算权重
        group_filter: 小组过滤器，指定本次抽取的小组范围，None表示不限制
        gender_filter: 性别过滤器，指定本次抽取的性别范围，None表示不限制

    Returns:
        bool: 保存是否成功
    """
    try:
        # 获取当前时间
        current_time = datetime.now().strftime("%Y-%m-%d %H:%M:%S")

        # 加载现有历史记录
        history_data = load_history_data("roll_call", class_name)

        # 初始化数据结构
        if "students" not in history_data:
            history_data["students"] = {}
        if "group_stats" not in history_data:
            history_data["group_stats"] = {}
        if "gender_stats" not in history_data:
            history_data["gender_stats"] = {}
        if "total_rounds" not in history_data:
            history_data["total_rounds"] = 0
        if "total_stats" not in history_data:
            history_data["total_stats"] = 0

        # 获取被选中的学生名称列表
        selected_names = [s.get("name", "") for s in selected_students]

        # 计算权重
        students_dict_list = get_student_list(class_name)
        students_with_weight = calculate_weight(students_dict_list, class_name)

        # 更新每个被选中学生的历史记录
        for student in selected_students:
            student_name = student.get("name", "")
            if not student_name:
                continue

            # 如果学生不存在于历史记录中，创建新记录
            if student_name not in history_data["students"]:
                history_data["students"][student_name] = {
                    "total_count": 0,
                    "group_gender_count": 0,
                    "last_drawn_time": "",
                    "rounds_missed": 0,
                    "history": [],
                }

            # 更新学生的基本信息
            student_data = history_data["students"][student_name]
            student_data["total_count"] += 1
            student_data["last_drawn_time"] = current_time
            student_data["rounds_missed"] = 0  # 重置未选中次数

            draw_method = 1

            # 获取当前被选中学生的权重信息
            current_student_weight = None
            for student_with_weight in students_with_weight:
                if student_with_weight.get("name") == student_name:
                    current_student_weight = student_with_weight.get("next_weight", 0)
                    break

            history_entry = {
                "draw_method": draw_method,
                "draw_time": current_time,
                "draw_people_numbers": len(selected_students),
                "draw_group": group_filter,
                "draw_gender": gender_filter,
                "weight": current_student_weight,
            }
            student_data["history"].append(history_entry)

        # 更新未被选中的学生的未选中次数
        for student_name, student_data in history_data["students"].items():
            if student_name not in selected_names:
                student_data["rounds_missed"] += 1

        # 更新小组和性别统计
        for student in selected_students:
            group = student.get("group", "")
            gender = student.get("gender", "")

            # 更新小组统计
            if group not in history_data["group_stats"]:
                history_data["group_stats"][group] = 0
            history_data["group_stats"][group] += 1

            # 更新性别统计
            if gender not in history_data["gender_stats"]:
                history_data["gender_stats"][gender] = 0
            history_data["gender_stats"][gender] += 1

        # 更新总轮数和总统计数
        history_data["total_rounds"] += 1
        history_data["total_stats"] += len(selected_students)

        # 保存历史记录
        return save_history_data("roll_call", class_name, history_data)

    except Exception as e:
        logger.error(f"保存点名历史记录失败: {e}")
        return False


# ==================================================
# 辅助函数
# ==================================================
def get_all_names(history_type: str, list_name: str) -> list:
    """获取历史记录中所有名称

    Args:
        history_type: 历史记录类型
        list_name: 列表名称

    Returns:
        list: 所有名称列表
    """
    try:
        if history_type == "roll_call":
            list_name_data = get_student_list(list_name)
            return [
                item["name"]
                for item in list_name_data
                if "name" in item and item["name"]
            ]
        else:
            list_name_data = get_pool_list(list_name)
            return [
                item["name"]
                for item in list_name_data
                if "name" in item and item["name"]
            ]
    except Exception as e:
        logger.error(f"获取历史记录中所有名称失败: {e}")
        return []


def format_table_item(
    value: Union[str, int, float], is_percentage: bool = False
) -> str:
    """格式化表格项显示值

    Args:
        value: 要格式化的值
        is_percentage: 是否为百分比值

    Returns:
        str: 格式化后的字符串
    """
    if isinstance(value, (int, float)):
        if is_percentage:
            return f"{value:.2%}"
        else:
            return f"{value:.2f}"
    return str(value)


def create_table_item(
    value: Union[str, int, float],
    font_size: int = 12,
    is_centered: bool = True,
    is_percentage: bool = False,
) -> "QTableWidgetItem":
    """创建表格项

    Args:
        value: 要显示的值
        font_size: 字体大小
        is_centered: 是否居中
        is_percentage: 是否为百分比值

    Returns:
        QTableWidgetItem: 表格项对象
    """
    display_value = format_table_item(value, is_percentage)
    item = QTableWidgetItem(display_value)
    from app.tools.personalised import load_custom_font

    item.setFont(QFont(load_custom_font(), font_size))
    if is_centered:
        item.setTextAlignment(
            Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignVCenter
        )
    item.setFlags(item.flags() & ~Qt.ItemFlag.ItemIsEditable)
    return item
