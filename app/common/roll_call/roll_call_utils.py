# ==================================================
# 点名工具类
# ==================================================
from random import SystemRandom

from app.common.data.list import get_group_list, get_student_list, filter_students_data
from app.common.history import calculate_weight
from app.common.fair_draw.avg_gap_protection import apply_avg_gap_protection
from app.common.behind_scenes.behind_scenes_utils import BehindScenesUtils
from app.tools.config import (
    calculate_remaining_count,
    read_drawn_record,
    reset_drawn_record,
    record_drawn_student,
)
from app.tools.settings_access import readme_settings_async, get_safe_font_size
from app.tools.platform_report import record_roll_call_metric_async
from app.tools.list_specific_settings_access import read_roll_call_setting
from app.common.display.result_display import ResultDisplayUtils
from app.common.history import save_roll_call_history
from app.common.extraction.extract import (
    _get_current_class_info,
    _is_non_class_time,
    _get_break_assignment_class_info,
)

from app.Language.obtain_language import get_any_position_value

system_random = SystemRandom()


class RollCallUtils:
    """点名工具类，提供通用的点名相关功能"""

    _student_data_cache = {}
    _drawn_record_cache = {}
    _behind_scenes_cache = {}

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
        display_mode=None,
    ):
        """
        更新多数量显示标签的文本

        Args:
            list_combobox_text: 班级下拉框当前文本
            range_combobox_index: 范围下拉框当前索引
            range_combobox_text: 范围下拉框当前文本
            gender_combobox_text: 性别下拉框当前文本
            half_repeat_setting: 半重复设置值
            display_mode: 显示模式 (0: 总+剩余, 1: 总数, 2: 剩余数, 3: 不显示)

        Returns:
            tuple: (总人数, 剩余人数, 格式化文本)
        """
        # 根据范围计算实际人数
        total_count = RollCallUtils.get_total_count(
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

        # 如果未指定显示模式，从设置中获取
        if display_mode is None:
            display_mode = readme_settings_async(
                "page_management", "roll_call_quantity_label"
            )

        # 根据显示模式和是否为小组模式选择不同的文本模板
        if range_combobox_index == 1:  # 小组模式
            if display_mode == 0:
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_3"
                )
            elif display_mode == 1:
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_4"
                )
            elif display_mode == 2:
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_5"
                )
            else:  # display_mode == 3, 不显示
                text_template = ""
        else:  # 学生模式
            if display_mode == 0:
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_0"
                )
            elif display_mode == 1:
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_1"
                )
            elif display_mode == 2:
                text_template = get_any_position_value(
                    "roll_call", "many_count_label", "text_2"
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
    def _get_filtered_candidates(
        class_name, group_index, group_filter, gender_index, gender_filter
    ):
        """获取并过滤候选人列表"""
        cache_key = (
            f"{class_name}_{group_index}_{group_filter}_{gender_index}_{gender_filter}"
        )

        if cache_key not in RollCallUtils._student_data_cache:
            # 使用 get_student_list 获取处理好的学生列表（List[Dict]），而不是直接加载原始JSON
            raw_students = get_student_list(class_name)

            tags_by_id = {}
            for s in raw_students or []:
                if not isinstance(s, dict):
                    continue
                try:
                    sid = int(s.get("id", 0) or 0)
                except Exception:
                    sid = 0
                if sid <= 0:
                    continue
                tags_by_id[sid] = s.get("tags") or []

            students_data = filter_students_data(
                raw_students, group_index, group_filter, gender_index, gender_filter
            )

            students_dict_list = []
            for student_tuple in students_data or []:
                student_id = student_tuple[0]
                student_dict = {
                    "id": student_id,
                    "name": student_tuple[1],
                    "gender": student_tuple[2],
                    "group": student_tuple[3],
                    "exist": student_tuple[4],
                    "tags": tags_by_id.get(student_id, []),
                }
                students_dict_list.append(student_dict)

            RollCallUtils._student_data_cache[cache_key] = students_dict_list
        else:
            students_dict_list = RollCallUtils._student_data_cache[cache_key]

        if group_index == 1:
            students_dict_list = sorted(
                students_dict_list, key=lambda x: str(x.get("group", "") or "")
            )

        return students_dict_list

    @staticmethod
    def _apply_history_filter(
        students_dict_list, half_repeat, class_name, gender_filter, group_filter
    ):
        """应用历史记录过滤（半重复模式）"""
        if half_repeat <= 0:
            return students_dict_list

        record_key = f"{class_name}_{gender_filter}_{group_filter}"
        if record_key not in RollCallUtils._drawn_record_cache:
            drawn_records = read_drawn_record(class_name, gender_filter, group_filter)
            RollCallUtils._drawn_record_cache[record_key] = drawn_records
        else:
            drawn_records = RollCallUtils._drawn_record_cache[record_key]

        drawn_counts = {name: count for name, count in drawn_records}

        filtered_list = []
        for item in students_dict_list:
            name = item["name"]
            if name not in drawn_counts or drawn_counts[name] < half_repeat:
                filtered_list.append(item)

        return filtered_list

    @staticmethod
    def _perform_weighted_draw(candidates, count, weights=None):
        """执行加权或随机抽取"""
        draw_count = min(count, len(candidates))
        selected_candidates = []
        selected_candidates_dict = []

        # Create a copy to avoid modifying the original list if it's used elsewhere
        candidates = list(candidates)

        # If weights are provided, ensure they match the candidates length
        current_weights = list(weights) if weights else [1.0] * len(candidates)

        for _ in range(draw_count):
            if not candidates:
                break

            total_weight = sum(current_weights)
            if total_weight <= 0:
                random_index = system_random.randint(0, len(candidates) - 1)
            else:
                rand_value = system_random.uniform(0, total_weight)
                cumulative_weight = 0
                random_index = 0
                for i, weight in enumerate(current_weights):
                    cumulative_weight += weight
                    if rand_value <= cumulative_weight:
                        random_index = i
                        break

            selected_candidate = candidates[random_index]

            # Extract basic info tuple
            info_tuple = (
                selected_candidate.get("id", ""),
                selected_candidate.get("name", ""),
                selected_candidate.get("exist", True),
            )

            selected_candidates.append(info_tuple)
            selected_candidates_dict.append(selected_candidate)

            candidates.pop(random_index)
            current_weights.pop(random_index)

        return selected_candidates, selected_candidates_dict

    @staticmethod
    def draw_random_students(
        class_name,
        group_index,
        group_filter,
        gender_index,
        gender_filter,
        current_count,
        half_repeat,
    ):
        """
        抽取随机学生
        """
        # 1. 获取候选人
        students_dict_list = RollCallUtils._get_filtered_candidates(
            class_name, group_index, group_filter, gender_index, gender_filter
        )

        # 2. 应用历史记录过滤
        students_dict_list = RollCallUtils._apply_history_filter(
            students_dict_list, half_repeat, class_name, gender_filter, group_filter
        )

        if not students_dict_list:
            return {"reset_required": True}

        # 3. 如果是小组模式，直接抽取小组
        if group_index == 1:
            draw_type = read_roll_call_setting(class_name, "draw_type")
            selected_groups = RollCallUtils.draw_random_groups(
                students_dict_list, current_count, draw_type
            )
            show_random = read_roll_call_setting(class_name, "show_random")
            selected_groups, ipc_selected_students = (
                RollCallUtils.render_group_display_students_and_ipc(
                    class_name, selected_groups, show_random
                )
            )
            return {
                "selected_students": selected_groups,
                "class_name": class_name,
                "selected_students_dict": [],
                "ipc_selected_students": ipc_selected_students,
                "group_filter": group_filter,
                "gender_filter": gender_filter,
            }

        # 4. 获取当前课程信息（用于科目过滤）
        current_class_info = None
        subject_history_filter_enabled = (
            readme_settings_async("linkage_settings", "subject_history_filter_enabled")
            or False
        )

        if subject_history_filter_enabled:
            data_source = readme_settings_async("linkage_settings", "data_source")
            if data_source == 2:
                from app.common.IPC_URL.csharp_ipc_handler import CSharpIPCHandler

                current_class_info = (
                    CSharpIPCHandler.instance().get_current_class_info()
                )
            elif data_source == 1:
                current_class_info = _get_current_class_info()

            if not current_class_info and _is_non_class_time():
                current_class_info = _get_break_assignment_class_info()

        subject_filter = (
            current_class_info.get("name", "") if current_class_info else ""
        )

        # 5. 应用平均间隔保护
        students_dict_list = apply_avg_gap_protection(
            students_dict_list, current_count, class_name, "roll_call", subject_filter
        )

        # 6. 应用内幕权重
        students_dict_list, behind_scenes_weights = (
            BehindScenesUtils.apply_probability_weights(
                students_dict_list, 0, class_name
            )
        )

        # 7. 检查必中人员
        guaranteed_students = BehindScenesUtils.ensure_guaranteed_selection(
            students_dict_list, behind_scenes_weights, class_name
        )
        if guaranteed_students is not None:
            selected_students = [
                (s.get("id", ""), s.get("name", ""), s.get("exist", True))
                for s in guaranteed_students
            ]
            return {
                "selected_students": selected_students,
                "class_name": class_name,
                "selected_students_dict": guaranteed_students,
                "group_filter": group_filter,
                "gender_filter": gender_filter,
            }

        # 8. 计算最终权重
        draw_type = read_roll_call_setting(class_name, "draw_type")
        weights = []
        if draw_type == 1:
            students_with_weight = calculate_weight(
                students_dict_list, class_name, subject_filter
            )
            # 重新对齐权重列表（students_with_weight 和 behind_scenes_weights 应该是一一对应的）
            for i, student in enumerate(students_with_weight):
                base_weight = student.get("weight", 1.0)
                bs_weight = (
                    behind_scenes_weights[i] if i < len(behind_scenes_weights) else 1.0
                )
                weights.append(base_weight * bs_weight)
            candidates = students_with_weight
        else:
            candidates = students_dict_list
            weights = behind_scenes_weights

        # 9. 执行抽取
        selected_students, selected_students_dict = (
            RollCallUtils._perform_weighted_draw(candidates, current_count, weights)
        )

        return {
            "selected_students": selected_students,
            "class_name": class_name,
            "selected_students_dict": selected_students_dict,
            "group_filter": group_filter,
            "gender_filter": gender_filter,
        }

    @staticmethod
    def draw_random_groups(students_dict_list, current_count, draw_type):
        """
        抽取随机小组
        """
        # 小组模式下，students_dict_list已经只包含小组信息
        # 权重抽取模式下，所有小组权重相同，所以逻辑其实是一样的
        # 为了兼容性保留 weights 参数，但在小组模式下通常是均匀分布

        weights = [1.0] * len(students_dict_list)

        selected_groups, _ = RollCallUtils._perform_weighted_draw(
            students_dict_list, current_count, weights
        )

        # format: (id, name, exist) -> (None, name, True)
        # _perform_weighted_draw already returns this format because _get_filtered_candidates sets it up

        return selected_groups

    @staticmethod
    def render_group_display_students(class_name, selected_students, show_random):
        rendered, _ = RollCallUtils.render_group_display_students_and_ipc(
            class_name, selected_students, show_random
        )
        return rendered

    @staticmethod
    def render_group_display_students_and_ipc(
        class_name, selected_students, show_random
    ):
        try:
            show_random = int(show_random or 0)
        except Exception:
            show_random = 0

        try:
            from app.common.data.list import get_group_members
        except Exception:
            get_group_members = None

        rendered = []
        ipc_selected_students = []
        for item in selected_students or []:
            if not isinstance(item, (list, tuple)) or len(item) < 3:
                continue

            student_id = item[0]
            name = str(item[1] or "")
            exist = bool(item[2])

            if student_id is not None:
                rendered.append((student_id, name, exist))
                ipc_selected_students.append(
                    {
                        "student_id": int(student_id or 0),
                        "student_name": name,
                        "display_text": name,
                        "exists": exist,
                        "group_name": "",
                        "lottery_name": "",
                    }
                )
                continue

            group_name = name
            selected_name = ""
            if (
                show_random > 0
                and get_group_members is not None
                and class_name
                and group_name
            ):
                try:
                    group_members = get_group_members(class_name, group_name) or []
                except Exception:
                    group_members = []

                if group_members:
                    try:
                        selected_member = system_random.choice(group_members)
                    except Exception:
                        selected_member = None
                    selected_name = str(
                        (selected_member or {}).get("name", "") or ""
                    ).strip()
            if selected_name:
                display_text = f"{group_name} - {selected_name}"
                rendered.append((None, display_text, exist))
                ipc_selected_students.append(
                    {
                        "student_id": 0,
                        "student_name": selected_name,
                        "display_text": display_text,
                        "exists": exist,
                        "group_name": group_name,
                        "lottery_name": "",
                    }
                )
                continue

            rendered.append((None, group_name, exist))
            ipc_selected_students.append(
                {
                    "student_id": 0,
                    "student_name": group_name,
                    "display_text": group_name,
                    "exists": exist,
                    "group_name": group_name if show_random > 0 else "",
                    "lottery_name": "",
                }
            )

        return rendered, ipc_selected_students

    @staticmethod
    def prepare_notification_settings():
        """
        准备通知设置参数

        Returns:
            dict: 通知设置参数
        """
        # 读取所有相关设置并传递给通知服务
        settings = {
            # 点名设置
            "font_size": get_safe_font_size("roll_call_settings", "font_size"),
            "animation_color_theme": readme_settings_async(
                "roll_call_settings", "animation_color_theme"
            ),
            "display_format": readme_settings_async(
                "roll_call_settings", "display_format"
            ),
            "student_image": readme_settings_async(
                "roll_call_settings", "student_image"
            ),
            # 浮窗设置
            "animation": readme_settings_async(
                "roll_call_notification_settings", "animation"
            ),
            "transparency": readme_settings_async(
                "roll_call_notification_settings", "floating_window_transparency"
            ),
            "auto_close_time": readme_settings_async(
                "roll_call_notification_settings", "floating_window_auto_close_time"
            ),
            "enabled_monitor": readme_settings_async(
                "roll_call_notification_settings", "floating_window_enabled_monitor"
            ),
            "window_position": readme_settings_async(
                "roll_call_notification_settings", "floating_window_position"
            ),
            "horizontal_offset": readme_settings_async(
                "roll_call_notification_settings", "floating_window_horizontal_offset"
            ),
            "vertical_offset": readme_settings_async(
                "roll_call_notification_settings", "floating_window_vertical_offset"
            ),
            # 通知设置
            "notification_display_duration": readme_settings_async(
                "roll_call_notification_settings", "notification_display_duration"
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
        record_key = f"{class_name}_{gender_filter}_{group_filter}"
        if record_key in RollCallUtils._drawn_record_cache:
            del RollCallUtils._drawn_record_cache[record_key]

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

    @staticmethod
    def create_display_settings(
        settings_group="roll_call_settings", display_settings=None
    ):
        """
        创建显示设置字典

        Args:
            settings_group: 设置组名称
            display_settings: 自定义显示设置字典，如果提供则合并到默认设置中

        Returns:
            dict: 显示设置字典
        """
        if display_settings:
            font_size = display_settings.get(
                "font_size", get_safe_font_size(settings_group, "font_size")
            )
            animation_color = display_settings.get(
                "animation_color_theme",
                readme_settings_async(settings_group, "animation_color_theme"),
            )
            display_format = display_settings.get(
                "display_format",
                readme_settings_async(settings_group, "display_format"),
            )
            display_style = display_settings.get(
                "display_style",
                readme_settings_async(settings_group, "display_style"),
            )
            show_student_image = display_settings.get(
                "student_image",
                readme_settings_async(settings_group, "student_image"),
            )
            image_position = display_settings.get(
                "student_image_position",
                readme_settings_async(settings_group, "student_image_position"),
            )
            show_random = display_settings.get(
                "show_random",
                readme_settings_async(settings_group, "show_random"),
            )
            show_tags = display_settings.get(
                "show_tags",
                readme_settings_async(settings_group, "show_tags"),
            )
        else:
            font_size = get_safe_font_size(settings_group, "font_size")
            animation_color = readme_settings_async(
                settings_group, "animation_color_theme"
            )
            display_format = readme_settings_async(settings_group, "display_format")
            display_style = readme_settings_async(settings_group, "display_style")
            show_student_image = readme_settings_async(settings_group, "student_image")
            image_position = readme_settings_async(
                settings_group, "student_image_position"
            )
            show_random = readme_settings_async(settings_group, "show_random")
            show_tags = readme_settings_async(settings_group, "show_tags")

        return {
            "font_size": font_size,
            "animation_color": animation_color,
            "display_format": display_format,
            "display_style": display_style,
            "show_student_image": show_student_image,
            "image_position": image_position,
            "show_random": show_random,
            "show_tags": show_tags,
        }

    @staticmethod
    def display_result(
        result_grid,
        class_name,
        selected_students,
        draw_count,
        group_index,
        settings_group="roll_call_settings",
        display_settings=None,
        selected_students_dict=None,
    ):
        """
        显示抽取结果

        Args:
            result_grid: 结果网格布局
            class_name: 班级名称
            selected_students: 选中的学生列表
            draw_count: 抽取人数
            group_index: 小组索引
            settings_group: 设置组名称
            display_settings: 自定义显示设置字典
        """
        display_dict = RollCallUtils.create_display_settings(
            settings_group, display_settings
        )

        student_labels = ResultDisplayUtils.create_student_label(
            class_name=class_name,
            selected_students=selected_students,
            selected_students_dict=selected_students_dict,
            draw_count=draw_count,
            font_size=display_dict["font_size"],
            animation_color=display_dict["animation_color"],
            display_format=display_dict["display_format"],
            display_style=display_dict["display_style"],
            show_student_image=display_dict["show_student_image"],
            image_position=display_dict.get("image_position"),
            group_index=group_index,
            show_random=display_dict["show_random"],
            settings_group=settings_group,
            show_tags=bool(display_dict.get("show_tags")),
        )
        cached_widgets = ResultDisplayUtils.collect_grid_widgets(result_grid)
        if cached_widgets and len(cached_widgets) == len(student_labels):
            updated = ResultDisplayUtils.update_grid_labels(
                result_grid, student_labels, cached_widgets
            )
            if updated:
                ResultDisplayUtils.dispose_widgets(student_labels)
            else:
                ResultDisplayUtils.display_results_in_grid(result_grid, student_labels)
        else:
            ResultDisplayUtils.display_results_in_grid(result_grid, student_labels)

    @staticmethod
    def record_drawn_students(
        class_name,
        selected_students,
        selected_students_dict,
        gender_filter,
        group_filter,
        half_repeat,
        report_metric=True,
    ):
        """
        记录已抽取的学生

        Args:
            class_name: 班级名称
            selected_students: 选中的学生列表
            selected_students_dict: 选中的学生字典列表
            gender_filter: 性别过滤器
            group_filter: 小组过滤器
            half_repeat: 半重复设置
        """
        if half_repeat > 0:
            record_drawn_student(
                class_name=class_name,
                gender=gender_filter,
                group=group_filter,
                student_name=selected_students,
            )
            record_key = f"{class_name}_{gender_filter}_{group_filter}"
            if record_key in RollCallUtils._drawn_record_cache:
                del RollCallUtils._drawn_record_cache[record_key]

        if selected_students_dict:
            saved = save_roll_call_history(
                class_name=class_name,
                selected_students=selected_students_dict,
                group_filter=group_filter,
                gender_filter=gender_filter,
            )
            if saved and report_metric:
                record_roll_call_metric_async()

    @staticmethod
    def prepare_notification_settings_by_group(
        settings_group="roll_call_notification_settings",
        display_settings=None,
    ):
        """
        根据设置组准备通知设置参数

        Args:
            settings_group: 设置组名称
            display_settings: 显示设置字典

        Returns:
            dict: 通知设置参数
        """
        if display_settings:
            font_size = display_settings.get("font_size")
            animation_color_theme = display_settings.get("animation_color_theme")
            display_format = display_settings.get("display_format")
            display_style = display_settings.get("display_style")
            student_image = display_settings.get("student_image")
            image_position = display_settings.get("student_image_position")
            show_random = display_settings.get("show_random")
        else:
            font_size = get_safe_font_size(
                settings_group.replace("_notification_settings", "_settings"),
                "font_size",
            )
            animation_color_theme = readme_settings_async(
                settings_group.replace("_notification_settings", "_settings"),
                "animation_color_theme",
            )
            display_format = readme_settings_async(
                settings_group.replace("_notification_settings", "_settings"),
                "display_format",
            )
            display_style = readme_settings_async(
                settings_group.replace("_notification_settings", "_settings"),
                "display_style",
            )
            student_image = readme_settings_async(
                settings_group.replace("_notification_settings", "_settings"),
                "student_image",
            )
            image_position = readme_settings_async(
                settings_group.replace("_notification_settings", "_settings"),
                "student_image_position",
            )
            show_random = readme_settings_async(
                settings_group.replace("_notification_settings", "_settings"),
                "show_random",
            )

        settings = {
            "font_size": font_size,
            "animation_color_theme": animation_color_theme,
            "display_format": display_format,
            "display_style": display_style,
            "student_image": student_image,
            "image_position": image_position,
            "show_random": show_random,
            "animation": readme_settings_async(settings_group, "animation"),
            "transparency": readme_settings_async(
                settings_group, "floating_window_transparency"
            ),
            "auto_close_time": readme_settings_async(
                settings_group, "floating_window_auto_close_time"
            ),
            "enabled_monitor": readme_settings_async(
                settings_group, "floating_window_enabled_monitor"
            ),
            "window_position": readme_settings_async(
                settings_group, "floating_window_position"
            ),
            "horizontal_offset": readme_settings_async(
                settings_group, "floating_window_horizontal_offset"
            ),
            "vertical_offset": readme_settings_async(
                settings_group, "floating_window_vertical_offset"
            ),
            "notification_display_duration": readme_settings_async(
                settings_group, "notification_display_duration"
            ),
        }

        return settings

    @staticmethod
    def show_notification_if_enabled(
        class_name,
        selected_students,
        draw_count,
        settings_group="roll_call_notification_settings",
        display_settings=None,
        ipc_selected_students=None,
        is_animating=False,
    ):
        """
        如果启用了通知服务，则显示抽取结果通知

        Args:
            class_name: 班级名称
            selected_students: 选中的学生列表
            draw_count: 抽取人数
            settings_group: 设置组名称
            display_settings: 显示设置字典
            is_animating: 是否在动画过程中，如果是则不启动自动关闭定时器
        """
        call_notification_service = readme_settings_async(
            settings_group, "call_notification_service"
        )

        if not call_notification_service:
            return

        settings = RollCallUtils.prepare_notification_settings_by_group(
            settings_group, display_settings
        )
        if (
            ipc_selected_students
            and isinstance(ipc_selected_students, list)
            and isinstance(settings, dict)
        ):
            settings["ipc_selected_students"] = ipc_selected_students
        ResultDisplayUtils.show_notification_if_enabled(
            class_name,
            selected_students,
            draw_count,
            settings,
            settings_group=settings_group,
            is_animating=is_animating,
        )
