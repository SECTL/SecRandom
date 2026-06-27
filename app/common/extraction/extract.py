import json
from typing import Dict, Tuple

from PySide6.QtCore import QDateTime
from PySide6.QtGui import *
from PySide6.QtNetwork import *
from PySide6.QtWidgets import *
from loguru import logger
from qfluentwidgets import *

from app.Language.obtain_language import get_content_name_async
from app.common.IPC_URL.csharp_ipc_handler import CSharpIPCHandler
from app.common.extraction.cses_parser import CSESParser
from app.tools.path_utils import *
from app.tools.settings_access import readme_settings_async


LINKAGE_MIN_CHECK_DELAY_MS = 1000
LINKAGE_FALLBACK_CHECK_DELAY_MS = 120 * 1000
LINKAGE_MAX_CHECK_DELAY_MS = 24 * 60 * 60 * 1000


def _coerce_int(value, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def _seconds_to_delay_ms(seconds: int, fallback_seconds: int = 120) -> int:
    seconds = _coerce_int(seconds, 0)
    if seconds <= 0:
        seconds = fallback_seconds
    delay_ms = seconds * 1000
    return max(
        LINKAGE_MIN_CHECK_DELAY_MS,
        min(delay_ms, LINKAGE_MAX_CHECK_DELAY_MS),
    )


def _iter_class_time_ranges(max_days: int = 7):
    current_day_of_week = _get_current_day_of_week()

    for day_offset in range(max(1, max_days)):
        day_of_week = ((current_day_of_week - 1 + day_offset) % 7) + 1
        class_times = _get_class_times_by_day(day_of_week)
        if not class_times or not isinstance(class_times, dict):
            continue

        time_ranges = []
        for range_name, time_range in class_times.items():
            try:
                start_end = time_range.split("-")
                if len(start_end) != 2:
                    continue
                start_seconds = _parse_time_string_to_seconds(start_end[0])
                end_seconds = _parse_time_string_to_seconds(start_end[1])
                time_ranges.append((start_seconds, end_seconds, range_name))
            except Exception as e:
                logger.error(f"解析时间段失败: {range_name} = {time_range}, 错误: {e}")
                continue

        for start_seconds, end_seconds, range_name in sorted(
            time_ranges, key=lambda item: item[0]
        ):
            yield day_offset, start_seconds, end_seconds, range_name


def get_next_cses_class_start_seconds(max_days: int = 7) -> int:
    """获取距离下一节 CSES 课程开始的秒数。"""
    current_total_seconds = _get_current_time_in_seconds()

    for day_offset, start_seconds, _, _ in _iter_class_time_ranges(max_days):
        target_seconds = day_offset * 24 * 60 * 60 + start_seconds
        seconds_left = target_seconds - current_total_seconds
        if seconds_left > 0:
            return seconds_left

    return 0


def get_next_cses_linkage_check_delay_ms(fallback_seconds: int = 120) -> int:
    """按 CSES 课程边界计算下一次联动状态检查时间。"""
    current_total_seconds = _get_current_time_in_seconds()
    pre_class_enable_time = _coerce_int(
        readme_settings_async("linkage_settings", "pre_class_enable_time", 0), 0
    )
    post_class_disable_delay = _coerce_int(
        readme_settings_async("linkage_settings", "post_class_disable_delay", 0), 0
    )

    candidates = []
    for day_offset, start_seconds, end_seconds, _ in _iter_class_time_ranges(7):
        day_base_seconds = day_offset * 24 * 60 * 60
        start_left = day_base_seconds + start_seconds - current_total_seconds
        end_left = day_base_seconds + end_seconds - current_total_seconds

        if pre_class_enable_time > 0:
            candidates.append(start_left - pre_class_enable_time)
        candidates.append(start_left)
        candidates.append(end_left + max(0, post_class_disable_delay))

    future_candidates = [seconds for seconds in candidates if seconds > 0]
    if not future_candidates:
        return _seconds_to_delay_ms(0, fallback_seconds)

    return _seconds_to_delay_ms(min(future_candidates), fallback_seconds)


def get_next_linkage_check_delay_ms(
    data_source: int | None = None,
    fallback_seconds: int = 120,
) -> int:
    """计算下一次联动状态检查延迟，优先在课程边界触发。"""
    if data_source is None:
        data_source = readme_settings_async("linkage_settings", "data_source", 0)
    data_source = _coerce_int(data_source, 0)

    if data_source == 1:
        return get_next_cses_linkage_check_delay_ms(fallback_seconds)

    if data_source == 2:
        try:
            ipc_handler = CSharpIPCHandler.instance()
            if not ipc_handler.is_running or not ipc_handler.is_connected:
                return _seconds_to_delay_ms(0, fallback_seconds)

            candidates = []
            on_class_left_time = _coerce_int(ipc_handler.get_on_class_left_time(), 0)
            pre_class_enable_time = _coerce_int(
                readme_settings_async("linkage_settings", "pre_class_enable_time", 0),
                0,
            )
            if on_class_left_time > 0:
                if pre_class_enable_time > 0:
                    candidates.append(on_class_left_time - pre_class_enable_time)
                candidates.append(on_class_left_time)

            current_time_point_left_time = _coerce_int(
                ipc_handler.get_current_time_point_left_time(),
                0,
            )
            if current_time_point_left_time > 0:
                candidates.append(current_time_point_left_time)

            post_class_disable_delay = _coerce_int(
                readme_settings_async(
                    "linkage_settings", "post_class_disable_delay", 0
                ),
                0,
            )
            if post_class_disable_delay > 0:
                elapsed_seconds = _coerce_int(
                    ipc_handler.get_elapsed_since_previous_time_point_end_seconds(),
                    0,
                )
                if 0 < elapsed_seconds < post_class_disable_delay:
                    candidates.append(post_class_disable_delay - elapsed_seconds)

            future_candidates = [seconds for seconds in candidates if seconds > 0]
            if future_candidates:
                return _seconds_to_delay_ms(min(future_candidates), fallback_seconds)
        except Exception as e:
            logger.warning(f"计算 ClassIsland 联动检查时间失败: {e}")

    return _seconds_to_delay_ms(0, fallback_seconds)


def get_next_pre_class_reset_check_delay_ms(
    data_source: int,
    pre_class_reset_time: int,
    fallback_seconds: int = 600,
) -> int:
    """计算下一次课前重置检查延迟，尽量在重置窗口开始时触发。"""
    data_source = _coerce_int(data_source, 0)
    pre_class_reset_time = max(1, _coerce_int(pre_class_reset_time, 120))

    seconds_to_class = 0
    if data_source == 1:
        seconds_to_class = get_next_cses_class_start_seconds(7)
    elif data_source == 2:
        try:
            seconds_to_class = _coerce_int(
                CSharpIPCHandler.instance().get_on_class_left_time(), 0
            )
        except Exception as e:
            logger.warning(f"计算 ClassIsland 课前重置检查时间失败: {e}")

    if seconds_to_class <= 0:
        return _seconds_to_delay_ms(0, fallback_seconds)
    if seconds_to_class <= pre_class_reset_time:
        return LINKAGE_MIN_CHECK_DELAY_MS

    return _seconds_to_delay_ms(
        seconds_to_class - pre_class_reset_time, fallback_seconds
    )


def _get_break_assignment_class_info() -> Dict:
    try:
        data_source = readme_settings_async("linkage_settings", "data_source")
        assignment = readme_settings_async(
            "linkage_settings", "subject_history_break_assignment"
        )
        if assignment is None:
            assignment = 2

        if assignment == 0:
            return {
                "name": get_content_name_async("linkage_settings", "break_subject_name")
            }

        if data_source == 2:
            if assignment == 1:
                class_info = CSharpIPCHandler.instance().get_previous_class_info()
                if class_info:
                    return class_info
            else:
                logger.debug("尝试从 ClassIsland 获取课间归属课程信息")
                class_info = CSharpIPCHandler.instance().get_next_class_info()
                if class_info:
                    return class_info
            logger.debug("从 ClassIsland 获取课间归属课程信息失败，回退到 CSES 文件")

        if data_source == 0:
            logger.debug("未启用数据源，无法获取课间归属课程信息")
            return {}

        parser = _get_cses_parser()
        if not parser:
            return {}

        current_day_of_week = _get_current_day_of_week()
        current_total_seconds = _get_current_time_in_seconds()

        class_info_list = parser.get_class_info()

        if assignment == 1:
            best_end_seconds = None
            best_class_name = ""
            for class_info in class_info_list:
                if class_info.get("day_of_week") != current_day_of_week:
                    continue
                end_time_str = class_info.get("end_time", "")
                if not end_time_str:
                    continue
                end_seconds = _parse_time_string_to_seconds(end_time_str)
                if end_seconds <= current_total_seconds and (
                    best_end_seconds is None or end_seconds > best_end_seconds
                ):
                    best_end_seconds = end_seconds
                    best_class_name = class_info.get("name", "") or ""
            if best_class_name:
                logger.info(f"课间归属到上节课: {best_class_name}")
                return {"name": best_class_name}
        else:
            for class_info in class_info_list:
                if class_info.get("day_of_week") != current_day_of_week:
                    continue
                start_time_str = class_info.get("start_time", "")
                if start_time_str:
                    start_seconds = _parse_time_string_to_seconds(start_time_str)
                    if start_seconds > current_total_seconds:
                        class_name = class_info.get("name", "")
                        logger.info(f"课间归属到下节课: {class_name}")
                        return {"name": class_name}

        logger.debug("无法获取课间归属课程信息")
        return {}

    except Exception as e:
        logger.error(f"获取课间归属课程信息失败: {e}")
        return {}


def _is_non_class_time() -> bool:
    """检测当前时间是否在非上课时间段

    当'课间禁用'开关启用时，用于判断是否需要安全验证

    Returns:
        bool: 如果当前时间在非上课时间段内返回True，否则返回False
             当数据源连接失败时返回False，允许抽取
    """
    try:
        instant_draw_disable = readme_settings_async(
            "linkage_settings", "instant_draw_disable"
        )
        logger.debug(f"课间禁用开关是否启用: {instant_draw_disable}")
        if not instant_draw_disable:
            return False

        pre_class_enable_time = readme_settings_async(
            "linkage_settings", "pre_class_enable_time"
        )
        logger.debug(f"上课前提前解禁时间: {pre_class_enable_time}秒")
        post_class_disable_delay = readme_settings_async(
            "linkage_settings", "post_class_disable_delay"
        )
        if post_class_disable_delay is None:
            post_class_disable_delay = 0

        data_source = readme_settings_async("linkage_settings", "data_source")
        logger.debug(f"数据源选择: {data_source}")

        if data_source == 0:
            logger.warning("未启用数据源，跳过课间禁用判断，允许抽取")
            return False

        if data_source == 2:
            ipc_handler = CSharpIPCHandler.instance()

            if not ipc_handler.is_running or not ipc_handler.is_connected:
                logger.warning(
                    "ClassIsland IPC 连接未建立或已断开，跳过课间禁用判断，允许抽取"
                )
                return False

            try:
                is_breaking = ipc_handler.is_breaking()
                on_class_left_time = ipc_handler.get_on_class_left_time()

                logger.debug(
                    f"ClassIsland状态 - 是否下课: {is_breaking}, 距离上课: {on_class_left_time}秒"
                )

                if (
                    on_class_left_time > 0
                    and on_class_left_time <= pre_class_enable_time
                ):
                    logger.debug(
                        f"距离上课{on_class_left_time}秒，小于等于提前解禁时间{pre_class_enable_time}秒，提前解禁"
                    )
                    return False

                if (
                    is_breaking
                    and post_class_disable_delay > 0
                    and on_class_left_time > 0
                ):
                    try:
                        elapsed_seconds = ipc_handler.get_elapsed_since_previous_time_point_end_seconds()
                        if 0 < elapsed_seconds <= post_class_disable_delay:
                            return False
                    except Exception as e:
                        logger.warning(f"获取课间经过时间失败: {e}，跳过此判断")

                return is_breaking

            except Exception as e:
                logger.error(
                    f"ClassIsland IPC 调用失败: {e}，跳过课间禁用判断，允许抽取"
                )
                return False

        current_day_of_week = _get_current_day_of_week()
        class_times = _get_class_times_by_day(current_day_of_week)
        if not class_times or not isinstance(class_times, dict):
            return False

        current_total_seconds = _get_current_time_in_seconds()
        logger.debug(f"当前时间总秒数: {current_total_seconds}")

        is_in_class_time = _is_time_in_ranges(current_total_seconds, class_times)
        logger.debug(f"当前时间是否在上课时间段内: {is_in_class_time}")

        # 获取距离下一节课的时间
        seconds_to_next_class = _get_seconds_to_next_class()
        logger.debug(f"距离下一节课时间: {seconds_to_next_class}秒")

        # 如果距离上课时间小于等于提前解禁时间，则提前解禁
        if seconds_to_next_class > 0 and seconds_to_next_class <= pre_class_enable_time:
            logger.debug(
                f"距离上课{seconds_to_next_class}秒，小于等于提前解禁时间{pre_class_enable_time}秒，提前解禁"
            )
            return False

        if (
            not is_in_class_time
            and seconds_to_next_class > 0
            and post_class_disable_delay > 0
            and 0
            < _get_seconds_since_last_class_end(current_day_of_week)
            <= post_class_disable_delay
        ):
            return False

        return not is_in_class_time

    except Exception as e:
        logger.exception(f"检测非上课时间失败: {e}")
        return False


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


def _get_current_day_of_week() -> int:
    """获取当前是星期几

    Returns:
        int: 星期几（1=星期一，7=星期日）
    """
    current_time = QDateTime.currentDateTime()
    day_of_week = current_time.date().dayOfWeek()
    return day_of_week


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

            start_total_seconds = _parse_time_string_to_seconds(start_time_str)
            end_total_seconds = _parse_time_string_to_seconds(end_time_str)

            if start_total_seconds <= current_seconds < end_total_seconds:
                return True

        except Exception as e:
            logger.error(f"解析时间段失败: {range_name} = {time_range}, 错误: {e}")
            continue

    return False


def _get_cses_parser() -> CSESParser | None:
    """获取CSES解析器实例

    Returns:
        CSESParser | None: 成功返回解析器实例，失败返回None
    """
    try:
        cses_dir = get_data_path("CSES")
        if not cses_dir.exists():
            logger.info("CSES目录不存在")
            return None

        cses_file_path = cses_dir / "cses_schedule.yml"

        if not cses_file_path.exists():
            logger.info("CSES文件不存在")
            return None

        parser = CSESParser()
        if not parser.load_from_file(str(cses_file_path)):
            logger.error(f"加载CSES文件失败: {str(cses_file_path)}")
            return None

        return parser

    except Exception as e:
        logger.exception(f"获取CSES解析器失败: {e}")
        return None


def _get_class_times_by_day(day_of_week: int) -> Dict[str, str]:
    """获取指定星期几的上课时间段

    Args:
        day_of_week: 星期几（1=星期一，7=星期日）

    Returns:
        Dict[str, str]: 上课时间段字典，格式为 {"name": "HH:MM:SS-HH:MM:SS"}
    """
    parser = _get_cses_parser()
    if not parser:
        return {}

    try:
        class_times = parser.get_class_times_by_day_with_week(day_of_week, "all")
        if class_times:
            return class_times
    except Exception:
        pass

    return parser.get_class_times_by_day(day_of_week)


def _get_current_class_info() -> Dict:
    """获取当前时间段对应的课程信息

    Returns:
        Dict: 课程信息字典，包含 name, start_time, end_time, teacher, location, day_of_week
              如果当前时间不在任何上课时间段内，返回空字典
    """
    try:
        data_source = readme_settings_async("linkage_settings", "data_source")

        if data_source == 2:
            logger.debug("尝试从 ClassIsland 获取当前课程信息")
            class_info = CSharpIPCHandler.instance().get_current_class_info()
            if class_info:
                return class_info
            else:
                logger.debug("从 ClassIsland 获取课程信息失败，回退到 CSES 文件")

        if data_source == 0:
            logger.debug("未启用数据源，无法获取当前课程信息")
            return {}

        # 从 CSES 文件获取课程信息
        parser = _get_cses_parser()
        if not parser:
            return {}

        current_day_of_week = _get_current_day_of_week()
        current_total_seconds = _get_current_time_in_seconds()

        class_info_list = parser.get_class_info()

        for class_info in class_info_list:
            if class_info.get("day_of_week") == current_day_of_week:
                start_time_str = class_info.get("start_time", "")
                end_time_str = class_info.get("end_time", "")

                if start_time_str and end_time_str:
                    start_seconds = _parse_time_string_to_seconds(start_time_str)
                    end_seconds = _parse_time_string_to_seconds(end_time_str)

                    if start_seconds <= current_total_seconds < end_seconds:
                        class_name = class_info.get("name", "")
                        logger.info(f"当前课程: {class_name}")
                        return {"name": class_name}

        logger.debug("当前时间不在任何上课时间段内")
        return {}

    except Exception as e:
        logger.exception(f"获取当前课程信息失败: {e}")
        return {}


def _get_seconds_to_next_class() -> int:
    """获取距离下一节课的剩余时间（秒）

    Returns:
        int: 距离下一节课的剩余秒数，如果没有下一节课则返回0
    """
    try:
        current_day_of_week = _get_current_day_of_week()
        class_times = _get_class_times_by_day(current_day_of_week)

        if not class_times or not isinstance(class_times, dict):
            return 0

        current_total_seconds = _get_current_time_in_seconds()

        # 将上课时间段按开始时间排序
        time_ranges = []
        for range_name, time_range in class_times.items():
            try:
                start_end = time_range.split("-")
                if len(start_end) != 2:
                    continue

                start_time_str, end_time_str = start_end
                start_total_seconds = _parse_time_string_to_seconds(start_time_str)
                time_ranges.append((start_total_seconds, range_name))
            except Exception as e:
                logger.exception(
                    f"解析时间段失败: {range_name} = {time_range}, 错误: {e}"
                )
                continue

        # 按开始时间排序
        time_ranges.sort(key=lambda x: x[0])

        # 找到下一个上课时间段
        for start_seconds, range_name in time_ranges:
            if start_seconds > current_total_seconds:
                return start_seconds - current_total_seconds

        # 如果当天没有下一节课，返回0
        return 0
    except Exception as e:
        logger.exception(f"计算距离下一节课时间失败: {e}")
        return 0


def _get_seconds_since_last_class_end(day_of_week: int) -> int:
    class_times = _get_class_times_by_day(day_of_week)
    if not class_times or not isinstance(class_times, dict):
        return 0

    current_total_seconds = _get_current_time_in_seconds()
    best_end_seconds = None
    for _, time_range in class_times.items():
        try:
            start_end = time_range.split("-")
            if len(start_end) != 2:
                continue
            _, end_time_str = start_end
            end_seconds = _parse_time_string_to_seconds(end_time_str)
            if end_seconds <= current_total_seconds and (
                best_end_seconds is None or end_seconds > best_end_seconds
            ):
                best_end_seconds = end_seconds
        except Exception:
            continue

    if best_end_seconds is None:
        return 0
    return max(0, current_total_seconds - best_end_seconds)


def _get_non_class_times_config() -> Dict[str, str]:
    """获取非上课时间段配置

    Returns:
        Dict[str, str]: 非上课时间段配置字典，如果获取失败返回空字典
    """
    parser = _get_cses_parser()
    if not parser:
        return {}

    non_class_times = parser.get_non_class_times()
    logger.info(f"成功从CSES文件生成{len(non_class_times)}个非上课时间段")
    return non_class_times


def _save_non_class_times_to_settings(non_class_times: Dict[str, str]) -> bool:
    """保存非上课时间段到设置文件

    Args:
        non_class_times: 非上课时间段字典

    Returns:
        bool: 保存成功返回True，否则返回False
    """
    try:
        settings_path = get_settings_path()

        if file_exists(settings_path):
            with open_file(settings_path, "r", encoding="utf-8") as f:
                settings = json.load(f)
        else:
            settings = {}

        settings["non_class_times"] = non_class_times

        with open_file(settings_path, "w", encoding="utf-8") as f:
            json.dump(settings, f, ensure_ascii=False, indent=2)

        logger.info(f"成功保存{len(non_class_times)}个非上课时间段到设置文件")
        return True

    except Exception as e:
        logger.exception(f"保存非上课时间段失败: {e}")
        return False


def import_cses_schedule(file_path: str) -> Tuple[bool, str]:
    """从CSES文件导入课程表

    Args:
        file_path: CSES文件路径

    Returns:
        Tuple[bool, str]: (是否成功, 结果消息)
    """
    try:
        parser = CSESParser()

        if not parser.load_from_file(file_path):
            return False, get_content_name_async(
                "linkage_settings", "cses_file_format_error"
            )

        non_class_times = parser.get_non_class_times()
        if not non_class_times:
            return False, get_content_name_async(
                "linkage_settings", "no_valid_time_periods"
            )

        cses_data_path = get_data_path("CSES", "cses_schedule.yml")
        ensure_dir(get_data_path("CSES"))
        import shutil

        shutil.copy2(get_path(file_path), cses_data_path)
        logger.info(f"已将CSES文件保存到: {cses_data_path}")

        summary = parser.get_summary()
        import_success_msg = get_content_name_async(
            "linkage_settings", "import_success"
        )
        if "{}" in import_success_msg:
            return True, import_success_msg.format(summary)
        else:
            return True, import_success_msg

    except Exception as e:
        logger.exception(f"导入CSES文件失败: {e}")
        return False, get_content_name_async(
            "linkage_settings", "import_failed"
        ).format(str(e))


def import_cses_schedule_from_content(content: str) -> Tuple[bool, str]:
    """从CSES内容字符串导入课程表

    Args:
        content: CSES格式的YAML内容

    Returns:
        Tuple[bool, str]: (是否成功, 结果消息)
    """
    try:
        parser = CSESParser()

        if not parser.load_from_content(content):
            return False, get_content_name_async(
                "linkage_settings", "cses_content_format_error"
            )

        non_class_times = parser.get_non_class_times()
        if not non_class_times:
            return False, get_content_name_async(
                "linkage_settings", "no_valid_time_periods"
            )

        summary = parser.get_summary()
        return True, get_content_name_async(
            "linkage_settings", "import_success"
        ).format(summary)

    except Exception as e:
        logger.exception(f"导入CSES内容失败: {e}")
        return False, get_content_name_async(
            "linkage_settings", "import_failed"
        ).format(str(e))


__all__ = ["import_cses_schedule", "import_cses_schedule_from_content"]
