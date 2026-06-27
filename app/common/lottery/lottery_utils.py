# ==================================================
# 抽奖工具类
# ==================================================
from random import SystemRandom
from loguru import logger

from app.common.data.list import (
    get_group_list,
    get_student_list,
    filter_students_data,
    get_pool_list,
)
from app.common.roll_call.roll_call_utils import RollCallUtils
from app.common.history import calculate_weight
from app.common.behind_scenes.behind_scenes_utils import BehindScenesUtils
from app.tools.config import (
    calculate_remaining_count,
    read_drawn_record,
    read_drawn_record_simple,
    reset_drawn_record,
)
from app.tools.settings_access import readme_settings_async
from app.tools.list_specific_settings_access import (
    read_lottery_setting,
    read_roll_call_setting,
    get_safe_font_size_list_specific,
)

from app.Language.obtain_language import get_any_position_value

system_random = SystemRandom()


class LotteryUtils:
    """抽奖工具类，提供通用的抽奖相关功能"""

    @staticmethod
    def _prefer_drawable_candidates(candidates, weights, required_count):
        """优先排除仍处于屏蔽期的候选人，人数不足时才降级补回。"""
        drawable_pairs = [
            (candidate, weights[index] if index < len(weights) else 1.0)
            for index, candidate in enumerate(candidates)
            if candidate.get("is_drawable", True)
        ]

        if len(drawable_pairs) >= required_count:
            filtered_candidates, filtered_weights = zip(*drawable_pairs, strict=True)
            return list(filtered_candidates), list(filtered_weights)

        shielded_count = len(candidates) - len(drawable_pairs)
        if shielded_count > 0:
            logger.warning(
                f"抽奖分配学生可抽取候选人不足，临时补回 {shielded_count} 个屏蔽期候选人"
            )

        return list(candidates), list(weights)

    @staticmethod
    def _get_prize_draw_type(pool_name: str | None = None) -> int:
        try:
            if not pool_name:
                return 0
            return int(read_lottery_setting(pool_name, "draw_type", 0) or 0)
        except Exception:
            return 0

    @staticmethod
    def get_total_count(list_combobox_text, range_combobox_index, range_combobox_text):
        """
        根据当前选择的范围计算实际人数

        Args:
            list_combobox_text: 班级下拉框当前文本
            range_combobox_index: 范围下拉框当前索引
            range_combobox_text: 范围下拉框当前文本

        Returns:
            int: 总人数
        """
        if range_combobox_index == 0:  # 全班
            students = get_student_list(list_combobox_text)
            total_count = len([s for s in students if s.get("exist", True)])
        elif range_combobox_index == 1:  # 小组模式 - 计算小组数量
            total_count = len(get_group_list(list_combobox_text))
        else:  # 特定小组 - 计算该小组的学生数量
            students = get_student_list(list_combobox_text)
            total_count = len(
                [
                    s
                    for s in students
                    if s["group"] == range_combobox_text and s.get("exist", True)
                ]
            )
        return total_count

    @staticmethod
    def update_many_count_label_text(
        list_combobox_text,
        range_combobox_index,
        range_combobox_text,
        gender_combobox_text,
        half_repeat_setting,
    ):
        """
        更新多数量显示标签的文本

        Args:
            list_combobox_text: 班级下拉框当前文本
            range_combobox_index: 范围下拉框当前索引
            range_combobox_text: 范围下拉框当前文本
            gender_combobox_text: 性别下拉框当前文本
            half_repeat_setting: 半重复设置值

        Returns:
            tuple: (总人数, 剩余人数, 格式化文本)
        """
        # 根据范围计算实际人数
        total_count = LotteryUtils.get_total_count(
            list_combobox_text, range_combobox_index, range_combobox_text
        )

        remaining_count = calculate_remaining_count(
            half_repeat=half_repeat_setting,
            class_name=list_combobox_text,
            gender_filter=gender_combobox_text,
            group_index=range_combobox_index,
            group_filter=range_combobox_text,
            total_count=total_count,
        )

        if remaining_count == 0:
            remaining_count = total_count

        # 根据是否为小组模式选择不同的文本模板
        if range_combobox_index == 1:  # 小组模式
            text_template = get_any_position_value(
                "roll_call", "many_count_label", "text_3"
            )
        else:  # 学生模式
            text_template = get_any_position_value(
                "roll_call", "many_count_label", "text_0"
            )

        formatted_text = text_template.format(
            total_count=total_count, remaining_count=remaining_count
        )

        return total_count, remaining_count, formatted_text

    @staticmethod
    def draw_random_students(
        class_name,
        group_index,
        group_filter,
        gender_index,
        gender_filter,
        current_count,
        half_repeat,
        pool_name=None,
        prize_list=None,
    ):
        """
        抽取随机学生

        Args:
            class_name: 班级名称
            group_index: 小组索引
            group_filter: 小组过滤器
            gender_index: 性别索引
            gender_filter: 性别过滤器
            current_count: 当前抽取数量
            half_repeat: 半重复设置
            pool_name: 奖池名称（用于抽奖模式下应用内幕设置）
            prize_list: 奖品列表（用于提高指定该奖品的学生的权重）

        Returns:
            dict: 包含抽取结果的字典
        """
        data = get_student_list(class_name)

        students_data = filter_students_data(
            data, group_index, group_filter, gender_index, gender_filter
        )

        if group_index == 1:
            # 小组模式下，按小组名称排序
            students_data = sorted(students_data, key=lambda x: x[3])  # x[3]是小组名称

            # 首先将学生数据转换为字典列表
            students_dict_list = []
            for student_tuple in students_data:
                student_dict = {
                    "id": student_tuple[0],
                    "name": student_tuple[1],
                    "gender": student_tuple[2],
                    "group": student_tuple[3],
                    "exist": student_tuple[4],
                }
                students_dict_list.append(student_dict)

            draw_count = int(current_count or 0)
            if draw_count <= 0:
                return {
                    "selected_students": [],
                    "class_name": class_name,
                    "selected_students_dict": [],
                    "group_filter": group_filter,
                    "gender_filter": gender_filter,
                }

            if not students_dict_list:
                return {"reset_required": True}

            draw_type = read_roll_call_setting(class_name, "draw_type")
            unique_draw = min(draw_count, len(students_dict_list))
            selected_groups = RollCallUtils.draw_random_groups(
                students_dict_list, unique_draw, draw_type
            )

            if len(selected_groups) < draw_count:
                all_groups = [
                    (g.get("id", ""), g.get("name", ""), g.get("exist", True))
                    for g in students_dict_list
                ]
                while len(selected_groups) < draw_count and all_groups:
                    selected_groups.append(system_random.choice(all_groups))

            return {
                "selected_students": selected_groups,
                "class_name": class_name,
                "selected_students_dict": [],  # 小组模式下不存储学生字典
                "group_filter": group_filter,
                "gender_filter": gender_filter,
            }

        # 首先将学生数据转换为字典列表
        students_dict_list = []
        for student_tuple in students_data:
            student_dict = {
                "id": student_tuple[0],
                "name": student_tuple[1],
                "gender": student_tuple[2],
                "group": student_tuple[3],
                "exist": student_tuple[4],
            }
            students_dict_list.append(student_dict)

        if half_repeat > 0:
            drawn_records = read_drawn_record(class_name, gender_filter, group_filter)
            drawn_counts = {name: count for name, count in drawn_records}

            filtered_students = []
            for student in students_dict_list:
                student_name = student["name"]
                if (
                    student_name not in drawn_counts
                    or drawn_counts[student_name] < half_repeat
                ):
                    filtered_students.append(student)

            students_dict_list = filtered_students

        if not students_dict_list:
            # 注意：这里我们返回一个特殊的标记，让调用者处理
            return {"reset_required": True}

        draw_type = read_roll_call_setting(class_name, "draw_type")
        if draw_type == 1:
            students_with_weight = calculate_weight(students_dict_list, class_name)
            weights = []
            for student in students_with_weight:
                weights.append(student.get("next_weight", student.get("weight", 1.0)))
        else:
            students_with_weight = students_dict_list
            weights = [1.0] * len(students_dict_list)

        # 应用内幕设置（抽奖模式下按奖池应用权重）
        guaranteed_students = None
        if pool_name:
            students_with_weight, behind_scenes_weights = (
                BehindScenesUtils.apply_probability_weights(
                    students_with_weight, 1, class_name, pool_name, prize_list
                )
            )

            # 检查是否有必中人员
            guaranteed_students = BehindScenesUtils.ensure_guaranteed_selection(
                students_with_weight, behind_scenes_weights, class_name, pool_name
            )

            # 公平权重与内幕权重相乘，避免任一机制覆盖另一方
            weights = [
                (
                    student.get("next_weight", student.get("weight", 1.0))
                    if draw_type == 1
                    else 1.0
                )
                * (behind_scenes_weights[i] if i < len(behind_scenes_weights) else 1.0)
                for i, student in enumerate(students_with_weight)
            ]

        if draw_type == 1:
            students_with_weight, weights = LotteryUtils._prefer_drawable_candidates(
                students_with_weight, weights, current_count
            )

        draw_count = int(current_count or 0)
        if draw_count <= 0:
            return {
                "selected_students": [],
                "class_name": class_name,
                "selected_students_dict": [],
                "group_filter": group_filter,
                "gender_filter": gender_filter,
            }

        allow_repeat = half_repeat <= 0 and draw_count > len(students_with_weight)

        selected_students = []
        selected_students_dict = []
        if guaranteed_students:
            for student in guaranteed_students:
                selected_students.append(
                    (
                        student.get("id"),
                        student.get("name"),
                        student.get("exist", True),
                    )
                )
                selected_students_dict.append(student)

            if not allow_repeat:
                guaranteed_ids = {s.get("id") for s in guaranteed_students}
                filtered_candidates = []
                filtered_weights = []
                for idx, s in enumerate(students_with_weight):
                    if s.get("id") in guaranteed_ids:
                        continue
                    filtered_candidates.append(s)
                    filtered_weights.append(weights[idx] if idx < len(weights) else 1.0)
                students_with_weight = filtered_candidates
                weights = filtered_weights

        remaining_to_draw = draw_count - len(selected_students_dict)
        if remaining_to_draw > 0:
            if allow_repeat:
                pick_candidates = students_with_weight
                pick_weights = weights
                if not pick_candidates and selected_students_dict:
                    pick_candidates = selected_students_dict
                    pick_weights = [1.0] * len(selected_students_dict)

                for _ in range(remaining_to_draw):
                    if not pick_candidates:
                        break
                    total_weight = sum(pick_weights)
                    if total_weight <= 0:
                        random_index = system_random.randint(
                            0, len(pick_candidates) - 1
                        )
                    else:
                        rand_value = system_random.uniform(0, total_weight)
                        cumulative_weight = 0
                        random_index = 0
                        for i, weight in enumerate(pick_weights):
                            cumulative_weight += weight
                            if rand_value <= cumulative_weight:
                                random_index = i
                                break

                    selected_student = pick_candidates[random_index]
                    student_id = selected_student.get("id", "")
                    random_name = selected_student.get("name", "")
                    exist = selected_student.get("exist", True)
                    selected_students.append((student_id, random_name, exist))
                    selected_students_dict.append(selected_student)
            else:
                remaining_to_draw = min(remaining_to_draw, len(students_with_weight))
                for _ in range(remaining_to_draw):
                    if not students_with_weight:
                        break
                    total_weight = sum(weights)
                    if total_weight <= 0:
                        random_index = system_random.randint(
                            0, len(students_with_weight) - 1
                        )
                    else:
                        rand_value = system_random.uniform(0, total_weight)
                        cumulative_weight = 0
                        random_index = 0
                        for i, weight in enumerate(weights):
                            cumulative_weight += weight
                            if rand_value <= cumulative_weight:
                                random_index = i
                                break

                    selected_student = students_with_weight[random_index]
                    student_id = selected_student.get("id", "")
                    random_name = selected_student.get("name", "")
                    exist = selected_student.get("exist", True)
                    selected_students.append((student_id, random_name, exist))
                    selected_students_dict.append(selected_student)

                    students_with_weight.pop(random_index)
                    weights.pop(random_index)

        return {
            "selected_students": selected_students,
            "class_name": class_name,
            "selected_students_dict": selected_students_dict,
            "group_filter": group_filter,
            "gender_filter": gender_filter,
        }

    @staticmethod
    def get_prize_total_count(pool_name: str) -> int:
        """获取奖池奖品总数"""
        try:
            from app.common.data.list import get_pool_list

            items = [
                item for item in get_pool_list(pool_name) if item.get("exist", True)
            ]
            draw_type = LotteryUtils._get_prize_draw_type(pool_name)
            if draw_type == 1:
                total = 0
                for item in items:
                    try:
                        cnt = int(item.get("count", 1) or 0)
                    except Exception:
                        cnt = 1
                    if cnt > 0:
                        total += cnt
                return total
            return len(items)
        except Exception:
            return 0

    @staticmethod
    def update_prize_many_count_label_text(pool_name: str, display_mode=None):
        """生成奖品总数/剩余显示文本

        Args:
            pool_name: 奖池名称
            display_mode: 显示模式 (0: 总+剩余, 1: 总数, 2: 剩余数, 3: 不显示)
        """
        total_count = LotteryUtils.get_prize_total_count(pool_name)
        remaining_count = LotteryUtils.calculate_prize_remaining_count(pool_name)
        if remaining_count == 0:
            remaining_count = total_count

        # 如果未指定显示模式，从设置中获取
        if display_mode is None:
            display_mode = readme_settings_async(
                "page_management", "lottery_quantity_label"
            )

        # 根据显示模式选择不同的文本模板
        if display_mode == 0:
            text_template = get_any_position_value(
                "lottery", "many_count_label", "text_0"
            )
        elif display_mode == 1:
            text_template = get_any_position_value(
                "lottery", "many_count_label", "text_1"
            )
        elif display_mode == 2:
            text_template = get_any_position_value(
                "lottery", "many_count_label", "text_2"
            )
        else:  # display_mode == 3, 不显示
            text_template = ""

        if text_template:
            formatted_text = text_template.format(
                total_count=total_count, remaining_count=remaining_count
            )
        else:
            formatted_text = ""

        return total_count, remaining_count, formatted_text

    @staticmethod
    def draw_random_prizes(pool_name: str, current_count: int):
        """按权重抽取奖品"""
        try:
            items = get_pool_list(pool_name)
            if not items:
                return {
                    "selected_prizes": [],
                    "pool_name": pool_name,
                    "selected_prizes_dict": [],
                }
            draw = int(current_count or 0)
            if draw <= 0:
                return {
                    "selected_prizes": [],
                    "pool_name": pool_name,
                    "selected_prizes_dict": [],
                }
            draw_type = LotteryUtils._get_prize_draw_type(pool_name)
            drawn_records = read_drawn_record_simple(pool_name)
            drawn_counts = {name: cnt for name, cnt in drawn_records}

            if draw_type == 1:
                candidates = []
                remaining_map = {}
                for item in items:
                    if not item.get("exist", True):
                        continue
                    name = str(item.get("name", "") or "")
                    try:
                        base_limit = int(item.get("count", 1) or 0)
                    except Exception:
                        base_limit = 1
                    if base_limit <= 0:
                        continue
                    drawn = 0
                    try:
                        drawn = int(drawn_counts.get(name, 0) or 0)
                    except Exception:
                        drawn = 0
                    remaining = base_limit - drawn
                    if remaining > 0:
                        candidates.append(item)
                        remaining_map[name] = remaining

                if not candidates:
                    return {"reset_required": True}
                items = candidates
            else:
                threshold = LotteryUtils._get_prize_draw_threshold(pool_name)
                if threshold is not None:
                    available = []
                    for i in items:
                        name = i.get("name", "")
                        cnt = int(drawn_counts.get(name, 0))
                        if cnt < threshold and i.get("exist", True):
                            available.append(i)
                    items = available
                    if not items:
                        return {"reset_required": True}

            # 应用内幕设置
            items, behind_scenes_weights = (
                BehindScenesUtils.apply_probability_weights_to_items(
                    items, 1, pool_name
                )
            )

            # 检查是否有必中奖品
            guaranteed_items = BehindScenesUtils.ensure_guaranteed_selection(
                items, behind_scenes_weights, pool_name
            )
            bs_weights = list(behind_scenes_weights or [])
            selected = []
            selected_dict = []
            guaranteed_names = set()
            if guaranteed_items is not None:
                for item in guaranteed_items:
                    if len(selected_dict) >= draw:
                        break
                    selected.append(
                        (item.get("id"), item.get("name"), item.get("exist", True))
                    )
                    selected_dict.append(item)
                    guaranteed_names.add(str(item.get("name", "") or ""))

            # 准备权重
            weights = []
            remaining_counts = []
            for i, item in enumerate(items):
                base_weight = float(item.get("weight", 1))
                behind_scenes_weight = behind_scenes_weights[i]
                name = str(item.get("name", "") or "")
                remaining = 1
                if draw_type == 1:
                    remaining = int(remaining_map.get(name, 1) or 1)
                remaining_counts.append(remaining)
                weights.append(base_weight * behind_scenes_weight * max(0, remaining))

            if guaranteed_names:
                filtered_items = []
                filtered_weights = []
                filtered_remaining = []
                filtered_bs_weights = []
                for i, item in enumerate(items):
                    name = str(item.get("name", "") or "")
                    if name in guaranteed_names:
                        if draw_type == 1:
                            remaining_counts[i] = max(0, remaining_counts[i] - 1)
                        continue
                    filtered_items.append(item)
                    filtered_weights.append(weights[i])
                    filtered_remaining.append(remaining_counts[i])
                    filtered_bs_weights.append(
                        bs_weights[i] if i < len(bs_weights) else 1.0
                    )
                items = filtered_items
                weights = filtered_weights
                remaining_counts = filtered_remaining
                bs_weights = filtered_bs_weights

            remaining_to_draw = draw - len(selected_dict)
            for _ in range(max(0, remaining_to_draw)):
                if not items:
                    break
                total_weight = sum(weights)
                if total_weight <= 0:
                    idx = system_random.randint(0, len(items) - 1)
                else:
                    rv = system_random.uniform(0, total_weight)
                    cum = 0
                    idx = 0
                    for i, w in enumerate(weights):
                        cum += w
                        if rv <= cum:
                            idx = i
                            break
                chosen = items[idx]
                selected.append(
                    (chosen.get("id"), chosen.get("name"), chosen.get("exist", True))
                )
                selected_dict.append(chosen)
                if draw_type == 1:
                    remaining_counts[idx] = max(0, int(remaining_counts[idx] or 0) - 1)
                    if remaining_counts[idx] <= 0:
                        items.pop(idx)
                        weights.pop(idx)
                        remaining_counts.pop(idx)
                        bs_weights.pop(idx)
                    else:
                        base_weight = float(chosen.get("weight", 1))
                        weights[idx] = (
                            base_weight
                            * float(bs_weights[idx] or 1.0)
                            * remaining_counts[idx]
                        )
                else:
                    items.pop(idx)
                    weights.pop(idx)
                    bs_weights.pop(idx)
            return {
                "selected_prizes": selected,
                "pool_name": pool_name,
                "selected_prizes_dict": selected_dict,
            }
        except Exception:
            return {
                "selected_prizes": [],
                "pool_name": pool_name,
                "selected_prizes_dict": [],
            }

    @staticmethod
    def _get_prize_draw_threshold(pool_name: str | None = None):
        """获取奖品抽取阈值：None 表示可重复；1 表示不重复；半重复返回次数阈值"""
        try:
            mode = read_lottery_setting(pool_name, "draw_mode")
            if mode == 1:
                return 1
            elif mode == 2:
                hr = read_lottery_setting(pool_name, "half_repeat")
                try:
                    return int(hr) if hr else 1
                except Exception:
                    return 1
            else:
                return None
        except Exception:
            return None

    @staticmethod
    def calculate_prize_remaining_count(pool_name: str) -> int:
        """计算剩余可抽奖品数量，考虑不重复/半重复设置"""
        try:
            from app.common.data.list import get_pool_list

            items = [
                item for item in get_pool_list(pool_name) if item.get("exist", True)
            ]
            draw_type = LotteryUtils._get_prize_draw_type(pool_name)
            drawn_records = read_drawn_record_simple(pool_name)
            drawn_counts = {name: cnt for name, cnt in drawn_records}
            if draw_type == 1:
                remain = 0
                for i in items:
                    name = str(i.get("name", "") or "")
                    try:
                        base_limit = int(i.get("count", 1) or 0)
                    except Exception:
                        base_limit = 1
                    if base_limit <= 0:
                        continue
                    try:
                        drawn = int(drawn_counts.get(name, 0) or 0)
                    except Exception:
                        drawn = 0
                    remain += max(0, base_limit - drawn)
                return remain

            threshold = LotteryUtils._get_prize_draw_threshold(pool_name)
            if threshold is None:
                return len(items)
            remain = 0
            for i in items:
                name = i.get("name", "")
                cnt = int(drawn_counts.get(name, 0))
                if cnt < threshold:
                    remain += 1
            return remain
        except Exception:
            return 0

    @staticmethod
    def draw_random_groups(students_dict_list, current_count, draw_type):
        """
        抽取随机小组
        """
        # 小组模式下，students_dict_list已经只包含小组信息
        weights = [1.0] * len(students_dict_list)

        selected_groups, _ = LotteryUtils._perform_weighted_draw(
            students_dict_list, current_count, weights
        )

        # format: (id, name, exist) -> (None, name, True)
        # _perform_weighted_draw returns (id, name, exist)
        # But for groups, we want (None, name, True)
        # Check what _perform_weighted_draw returns based on input.
        # Input students_dict_list has id, name, exist.
        # So it should be fine.

        return selected_groups

    @staticmethod
    def prepare_notification_settings(pool_name: str | None = None):
        """
        准备通知设置参数

        Returns:
            dict: 通知设置参数
        """
        # 读取所有相关设置并传递给通知服务
        settings = {
            # 点名设置
            "font_size": get_safe_font_size_list_specific(
                "lottery_settings",
                "lottery_list_specific_settings",
                pool_name,
                "font_size",
            ),
            "animation_color_theme": read_lottery_setting(
                pool_name, "animation_color_theme"
            ),
            "display_format": read_lottery_setting(pool_name, "display_format"),
            "display_style": read_lottery_setting(pool_name, "display_style"),
            "student_image": read_lottery_setting(pool_name, "lottery_image"),
            "image_position": read_lottery_setting(pool_name, "lottery_image_position"),
            "show_random": read_lottery_setting(pool_name, "show_random"),
            # 浮窗设置
            "animation": readme_settings_async(
                "lottery_notification_settings", "animation"
            ),
            "transparency": readme_settings_async(
                "lottery_notification_settings", "floating_window_transparency"
            ),
            "auto_close_time": readme_settings_async(
                "lottery_notification_settings", "floating_window_auto_close_time"
            ),
            "enabled_monitor": readme_settings_async(
                "lottery_notification_settings", "floating_window_enabled_monitor"
            ),
            "window_position": readme_settings_async(
                "lottery_notification_settings", "floating_window_position"
            ),
            "horizontal_offset": readme_settings_async(
                "lottery_notification_settings", "floating_window_horizontal_offset"
            ),
            "vertical_offset": readme_settings_async(
                "lottery_notification_settings", "floating_window_vertical_offset"
            ),
            # 通知设置
            "notification_display_duration": readme_settings_async(
                "lottery_notification_settings", "notification_display_duration"
            ),
        }

        return settings

    @staticmethod
    def reset_drawn_records(window, class_name, gender_filter, group_filter):
        """
        重置已抽取记录

        Args:
            window: 窗口对象（用于传递self参数）
            class_name: 班级名称
            gender_filter: 性别过滤器
            group_filter: 小组过滤器
        """
        reset_drawn_record(window, class_name, gender_filter, group_filter)

    @staticmethod
    def update_start_button_state(button, total_count):
        """
        根据总人数更新开始按钮的状态

        Args:
            button: 开始按钮对象
            total_count: 总人数
        """
        if total_count == 0:
            button.setEnabled(False)
        else:
            button.setEnabled(True)
