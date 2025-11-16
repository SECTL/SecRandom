# ==================================================
# 导入模块
# ==================================================
from qfluentwidgets import *
from PySide6.QtGui import *
from PySide6.QtWidgets import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *

import json
from typing import Dict
from loguru import logger
from PySide6.QtCore import QDateTime

from app.tools.path_utils import *


# ==================================================
# 判断当前时间是否在非上课时间段
# ==================================================
def _is_non_class_time() -> bool:
    """检测当前时间是否在非上课时间段

    当'课间禁用'开关启用时，用于判断是否需要安全验证

    Returns:
        bool: 如果当前时间在非上课时间段内返回True，否则返回False
    """
    try:
        # 1. 检查课间禁用开关是否启用
        if not _is_instant_draw_disable_enabled():
            return False

        # 2. 获取非上课时间段配置
        non_class_times = _get_non_class_times_config()
        if not non_class_times:
            return False

        # 3. 获取当前时间并转换为总秒数
        current_total_seconds = _get_current_time_in_seconds()

        # 4. 检查当前时间是否在任何非上课时间段内
        return _is_time_in_ranges(current_total_seconds, non_class_times)

    except Exception as e:
        logger.error(f"检测非上课时间失败: {e}")
        return False


def _is_instant_draw_disable_enabled() -> bool:
    """检查课间禁用开关是否启用

    Returns:
        bool: 如果课间禁用开关启用返回True，否则返回False
    """
    try:
        settings_path = get_settings_path()
        if not file_exists(settings_path):
            return False

        with open_file(settings_path, "r", encoding="utf-8") as f:
            settings = json.load(f)

        program_functionality = settings.get("program_functionality", {})
        return program_functionality.get("instant_draw_disable", False)

    except Exception as e:
        logger.error(f"读取课间禁用设置失败: {e}")
        return False


def _get_non_class_times_config() -> Dict[str, str]:
    """获取非上课时间段配置

    Returns:
        Dict[str, str]: 非上课时间段配置字典，如果获取失败返回空字典
    """
    try:
        time_settings_path = get_settings_path()
        if not file_exists(time_settings_path):
            return {}

        with open_file(time_settings_path, "r", encoding="utf-8") as f:
            time_settings = json.load(f)

        return time_settings.get("non_class_times", {})

    except Exception as e:
        logger.error(f"读取时间设置失败: {e}")
        return {}


def _get_current_time_in_seconds() -> int:
    """获取当前时间并转换为总秒数

    Returns:
        int: 当前时间的总秒数（从午夜开始计算）
    """
    current_time = QDateTime.currentDateTime()
    current_hour = current_time.time().hour()
    current_minute = current_time.time().minute()
    current_second = current_time.time().second()

    return current_hour * 3600 + current_minute * 60 + current_second


def _is_time_in_ranges(current_seconds: int, time_ranges: Dict[str, str]) -> bool:
    """检查当前时间是否在任何一个时间范围内

    Args:
        current_seconds: 当前时间的总秒数
        time_ranges: 时间范围字典，格式为 {"name": "HH:MM:SS-HH:MM:SS"}

    Returns:
        bool: 如果当前时间在任何一个时间范围内返回True，否则返回False
    """
    for range_name, time_range in time_ranges.items():
        try:
            start_end = time_range.split("-")
            if len(start_end) != 2:
                logger.warning(f"时间范围格式错误: {range_name} = {time_range}")
                continue

            start_time_str, end_time_str = start_end

            # 解析开始时间
            start_total_seconds = _parse_time_string_to_seconds(start_time_str)

            # 解析结束时间
            end_total_seconds = _parse_time_string_to_seconds(end_time_str)

            # 检查当前时间是否在该非上课时间段内
            if start_total_seconds <= current_seconds < end_total_seconds:
                return True

        except Exception as e:
            logger.error(
                f"解析非上课时间段失败: {range_name} = {time_range}, 错误: {e}"
            )
            continue

    return False


def _parse_time_string_to_seconds(time_str: str) -> int:
    """将时间字符串转换为总秒数

    Args:
        time_str: 时间字符串，格式为 "HH:MM:SS" 或 "HH:MM"

    Returns:
        int: 时间的总秒数

    Raises:
        ValueError: 如果时间字符串格式不正确
    """
    time_parts = list(map(int, time_str.split(":")))

    if len(time_parts) < 2 or len(time_parts) > 3:
        raise ValueError(f"时间字符串格式不正确: {time_str}")

    hours = time_parts[0]
    minutes = time_parts[1]
    seconds = time_parts[2] if len(time_parts) > 2 else 0

    return hours * 3600 + minutes * 60 + seconds
