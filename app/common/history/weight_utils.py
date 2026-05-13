# ==================================================
# 导入库
# ==================================================
import math
from random import SystemRandom
from loguru import logger

from app.tools.settings_access import (
    get_bool_setting,
    get_float_setting,
    get_int_setting,
)
from app.Language.obtain_language import get_content_combo_name_async
from app.common.history.file_utils import load_history_data
from app.common.history.history_reader import filter_roll_call_history_by_subject

system_random = SystemRandom()


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


def _load_weight_settings() -> dict:
    """加载权重设置"""
    return {
        "fair_draw_enabled": get_bool_setting("fair_draw_settings", "fair_draw"),
        "fair_draw_group_enabled": get_bool_setting(
            "fair_draw_settings", "fair_draw_group"
        ),
        "fair_draw_gender_enabled": get_bool_setting(
            "fair_draw_settings", "fair_draw_gender"
        ),
        "fair_draw_time_enabled": get_bool_setting(
            "fair_draw_settings", "fair_draw_time"
        ),
        "base_weight": get_float_setting("fair_draw_settings", "base_weight", 1.0),
        "min_weight": get_float_setting("fair_draw_settings", "min_weight", 0.1),
        "max_weight": get_float_setting("fair_draw_settings", "max_weight", 5.0),
        "frequency_function": get_int_setting(
            "fair_draw_settings", "frequency_function", 1
        ),
        "frequency_weight": get_float_setting(
            "fair_draw_settings", "frequency_weight", 1.0
        ),
        "group_weight": get_float_setting("fair_draw_settings", "group_weight", 1.0),
        "gender_weight": get_float_setting("fair_draw_settings", "gender_weight", 1.0),
        "time_weight": get_float_setting("fair_draw_settings", "time_weight", 1.0),
        "cold_start_enabled": get_bool_setting(
            "fair_draw_settings", "cold_start_enabled"
        ),
        "cold_start_rounds": get_int_setting(
            "fair_draw_settings", "cold_start_rounds", 10
        ),
        "shield_enabled": get_bool_setting("advanced_settings", "shield_enabled"),
        "shield_time": get_int_setting("advanced_settings", "shield_time", 0),
        "shield_time_unit": get_int_setting("advanced_settings", "shield_time_unit", 0),
    }


def _process_history_for_weights(students_data: list, history_data: dict) -> dict:
    """处理历史记录以获取权重计算所需数据"""
    weight_data = {}

    # 初始化
    for student in students_data:
        student_id = student.get("id", student.get("name", ""))
        weight_data[student_id] = {
            "total_count": 0,
            "group_count": 0,
            "gender_count": 0,
            "last_drawn_time": None,
            "rounds_missed": 0,
        }

    # 从历史记录填充数据
    if isinstance(history_data, dict) and "students" in history_data:
        students_history = history_data.get("students", {})
        if isinstance(students_history, dict):
            for student_name, student_info in students_history.items():
                if student_name in weight_data and isinstance(student_info, dict):
                    weight_data[student_name]["total_count"] = student_info.get(
                        "total_count", 0
                    )
                    weight_data[student_name]["rounds_missed"] = student_info.get(
                        "rounds_missed", 0
                    )
                    weight_data[student_name]["last_drawn_time"] = student_info.get(
                        "last_drawn_time", ""
                    )

                    # 统计小组和性别计数
                    history = student_info.get("history", [])
                    if isinstance(history, list):
                        all_group_opt = get_content_combo_name_async(
                            "roll_call", "range_combobox"
                        )[0]
                        all_gender_opt = get_content_combo_name_async(
                            "roll_call", "gender_combobox"
                        )[0]

                        for record in history:
                            if isinstance(record, dict):
                                draw_group = record.get("draw_group", "")
                                if draw_group and draw_group != all_group_opt:
                                    weight_data[student_name]["group_count"] += 1

                                draw_gender = record.get("draw_gender", "")
                                if draw_gender and draw_gender != all_gender_opt:
                                    weight_data[student_name]["gender_count"] += 1

    return weight_data


def _calculate_frequency_factor(settings, total_count, max_total_count, is_cold_start):
    """计算频率因子"""
    if not settings["fair_draw_enabled"]:
        return 0.0

    func_type = settings["frequency_function"]

    if func_type == 0:  # 线性
        factor = (max_total_count - total_count + 1) / (max_total_count + 1)
    elif func_type == 1:  # 平方根
        factor = math.sqrt(max_total_count + 1) / math.sqrt(total_count + 1)
    elif func_type == 2:  # 指数
        if max_total_count == 0:
            factor = 1.0
        else:
            factor = math.exp((max_total_count - total_count) / max_total_count)
    else:
        factor = math.sqrt(max_total_count + 1) / math.sqrt(total_count + 1)

    if is_cold_start:
        factor = min(0.8 + (factor * 0.2), factor)

    return factor * settings["frequency_weight"]


def _calculate_balance_factor(
    enabled, item_value, item_stats, settings_weight, weight_data, count_key
):
    """计算平衡因子 (通用用于小组和性别)"""
    if not enabled:
        return 0.0

    valid_stats = [v for v in item_stats.values() if v > 0]

    if len(valid_stats) > 3:
        history_val = max(item_stats.get(item_value, 0), 0)
        factor = 1.0 / (history_val * 0.2 + 1)
        return factor * settings_weight
    else:
        all_counts = [data[count_key] for data in weight_data.values()]
        max_count = max(all_counts) if all_counts else 0

        current_count = 0
        # 这里的weight_data是一个字典，我们需要找到当前条目的计数
        # 但传入的是整个weight_data，我们需要知道当前是哪个学生
        # 由于这个函数设计得比较通用，我们需要在外部获取当前学生的计数
        return 0.0  # Placeholder, logic handled inside main loop for now


def _calculate_time_factor(settings, last_drawn_time):
    """计算时间因子"""
    if not settings["fair_draw_time_enabled"] or not last_drawn_time:
        return 0.0

    try:
        from datetime import datetime

        last_time = datetime.fromisoformat(last_drawn_time)
        days_diff = (datetime.now() - last_time).days
        return min(1.0, days_diff / 30.0) * settings["time_weight"]
    except Exception as e:
        logger.exception(f"Error calculating time factor: {e}")
        return 0.0


def _check_shield_status(settings, last_drawn_time):
    """检查屏蔽状态"""
    if not settings["shield_enabled"] or not last_drawn_time:
        return False, 0

    try:
        from datetime import datetime, timedelta

        last_time = datetime.fromisoformat(last_drawn_time)

        unit = settings["shield_time_unit"]
        value = settings["shield_time"]

        if unit == 0:
            duration = timedelta(seconds=value)
        elif unit == 1:
            duration = timedelta(minutes=value)
        else:
            duration = timedelta(hours=value)

        diff = datetime.now() - last_time
        if diff < duration:
            remaining = (duration - diff).total_seconds()
            return True, remaining

    except Exception as e:
        logger.exception(f"Error checking shield status: {e}")

    return False, 0


# ==================================================
# 公平抽取权重计算函数
# ==================================================
def calculate_weight(students_data: list, class_name: str, subject: str = "") -> list:
    """计算学生权重

    Args:
        students_data: 学生数据列表
        class_name: 班级名称
        subject: 科目名称

    Returns:
        list: 更新后的学生数据列表
    """
    settings = _load_weight_settings()
    history_data = load_history_data("roll_call", class_name)

    if subject:
        history_data = filter_roll_call_history_by_subject(history_data, subject)

    group_stats = history_data.get("group_stats", {})
    gender_stats = history_data.get("gender_stats", {})

    current_stats = history_data.get("total_stats", 0)
    is_cold_start = (
        settings["cold_start_enabled"] and current_stats < settings["cold_start_rounds"]
    )

    weight_data = _process_history_for_weights(students_data, history_data)

    all_total_counts = [data["total_count"] for data in weight_data.values()]
    max_total_count = max(all_total_counts) if all_total_counts else 0

    # 为每个学生计算权重
    for student in students_data:
        student_id = student.get("id", student.get("name", ""))
        if student_id not in weight_data:
            continue

        s_data = weight_data[student_id]

        # 1. 频率因子
        frequency_penalty = _calculate_frequency_factor(
            settings, s_data["total_count"], max_total_count, is_cold_start
        )

        # 2. 小组平衡
        group_balance = 0.0
        if settings["fair_draw_group_enabled"]:
            current_group = student.get("group", "")
            valid_groups = [v for v in group_stats.values() if v > 0]

            if len(valid_groups) > 3:
                group_hist = max(group_stats.get(current_group, 0), 0)
                group_balance = (1.0 / (group_hist * 0.2 + 1)) * settings[
                    "group_weight"
                ]
            else:
                all_counts = [d["group_count"] for d in weight_data.values()]
                max_group_cnt = max(all_counts) if all_counts else 0

                if max_group_cnt == 0:
                    group_balance = 0.2 * settings["group_weight"]
                elif s_data["group_count"] == 0:
                    group_balance = 0.5 * settings["group_weight"]
                else:
                    group_balance = settings["group_weight"] * (
                        1.0 - (s_data["group_count"] / max_group_cnt)
                    )

        # 3. 性别平衡
        gender_balance = 0.0
        if settings["fair_draw_gender_enabled"]:
            current_gender = student.get("gender", "")
            valid_genders = [v for v in gender_stats.values() if v > 0]

            if len(valid_genders) > 3:
                gender_hist = max(gender_stats.get(current_gender, 0), 0)
                gender_balance = (1.0 / (gender_hist * 0.2 + 1)) * settings[
                    "gender_weight"
                ]
            else:
                all_counts = [d["gender_count"] for d in weight_data.values()]
                max_gender_cnt = max(all_counts) if all_counts else 0

                if max_gender_cnt == 0:
                    gender_balance = 0.2 * settings["gender_weight"]
                elif s_data["gender_count"] == 0:
                    gender_balance = 0.5 * settings["gender_weight"]
                else:
                    gender_balance = settings["gender_weight"] * (
                        1.0 - (s_data["gender_count"] / max_gender_cnt)
                    )

        # 4. 时间因子
        time_factor = _calculate_time_factor(settings, s_data["last_drawn_time"])

        # 5. 屏蔽检查
        is_shielded, shield_remaining = _check_shield_status(
            settings, s_data["last_drawn_time"]
        )

        # 计算总权重
        total_weight = sum(
            [
                settings["base_weight"],
                frequency_penalty,
                group_balance,
                gender_balance,
                time_factor,
            ]
        )

        if is_shielded:
            total_weight = settings["min_weight"] / 10

        total_weight = max(
            settings["min_weight"] / 10, min(settings["max_weight"], total_weight)
        )
        total_weight = round(total_weight, 2)

        student["next_weight"] = total_weight
        student["weight_details"] = {
            "base_weight": settings["base_weight"],
            "frequency_penalty": frequency_penalty,
            "group_balance": group_balance,
            "gender_balance": gender_balance,
            "time_factor": time_factor,
            "total_weight": total_weight,
            "is_cold_start": is_cold_start,
            "total_count": s_data["total_count"],
            "max_total_count": max_total_count,
            "frequency_function": settings["frequency_function"],
            "is_shielded": is_shielded,
            "shield_remaining": round(shield_remaining, 2),
            "shield_enabled": settings["shield_enabled"],
        }

    return students_data
