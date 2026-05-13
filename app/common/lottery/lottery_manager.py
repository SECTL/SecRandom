from PySide6.QtCore import QObject, Signal, QTimer, QEasingCurve, QFileSystemWatcher
from PySide6.QtGui import QFont
from dataclasses import dataclass
from loguru import logger
from random import SystemRandom

from app.common.data.list import (
    get_pool_list,
    get_pool_name_list,
    get_class_name_list,
    get_group_list,
    get_gender_list,
)
from app.common.history import save_lottery_history
from app.common.display.result_display import ResultDisplayUtils
from app.common.lottery.lottery_utils import LotteryUtils
from app.common.roll_call.roll_call_utils import RollCallUtils
from app.common.music.music_player import music_player
from app.common.voice.voice import TTSHandler
from app.common.extraction.extract import _is_non_class_time
from app.common.safety.verify_ops import require_and_run
from app.page_building.another_window import create_remaining_list_window
from app.tools.path_utils import get_data_path
from app.tools.personalised import load_custom_font
from app.tools.config import (
    record_drawn_prize,
    reset_drawn_prize_record,
)
from app.tools.settings_access import readme_settings_async
from app.tools.platform_report import record_lottery_metric_async
from app.tools.list_specific_settings_access import (
    read_lottery_setting,
    get_safe_font_size_list_specific,
)
from app.Language.obtain_language import (
    get_content_combo_name_async,
    get_content_name_async,
    get_content_pushbutton_name_async,
)
from app.tools.variable import APP_INIT_DELAY
from app.tools.config import track_event

system_random = SystemRandom()


@dataclass(frozen=True, slots=True)
class LotteryDrawContext:
    pool_name: str
    class_name: str | None = None
    group_filter: str | None = None
    gender_filter: str | None = None
    group_index: int = 0
    gender_index: int = 0


@dataclass(frozen=True, slots=True)
class LotteryDrawPlan:
    animation: int
    autoplay_count: int
    animation_interval: int
    animation_music: str | None
    result_music: str | None


class LotteryManager(QObject):
    # 信号定义
    data_loaded = Signal(bool)
    error_occurred = Signal(str)

    def __init__(self):
        super().__init__()
        self.prizes = []
        self.current_pool_name = ""
        self.current_class_name = ""
        self.current_group_filter = ""
        self.current_gender_filter = ""
        self.current_group_index = 0
        self.current_gender_index = 0
        self.enable_student_assignment = False
        self._total_count_cache_pool = None
        self._total_count_cache_value = None
        self._render_settings_cache_pool = None
        self._render_settings_cache = None
        self._notification_settings_cache_pool = None
        self._notification_settings_cache = None

    def _format_prize_student_text(self, prize_name, group_name, student_name, mode):
        prize_name = str(prize_name or "")
        group_name = str(group_name or "")
        student_name = str(student_name or "")

        def format_default():
            if group_name and student_name:
                return f"{prize_name}\n{group_name} - {student_name}"
            if group_name:
                return f"{prize_name}\n{group_name}"
            if student_name:
                return f"{prize_name}\n{student_name}"
            return prize_name

        try:
            mode = int(mode)
        except Exception:
            mode = 0

        if mode == 0:
            return format_default()

        mode_spec = {
            1: ("\n", ("prize", "group", "student")),
            2: (" - ", ("prize", "group", "student")),
            3: ("\n", ("prize", "student")),
            4: (" - ", ("prize", "student")),
            5: ("\n", ("prize", "group")),
            6: (" - ", ("prize", "group")),
        }

        spec = mode_spec.get(mode)
        if not spec:
            return format_default()

        sep, order = spec
        value_map = {"prize": prize_name, "group": group_name, "student": student_name}
        parts = [value_map[key] for key in order if value_map.get(key)]
        return sep.join(parts) if parts else prize_name

    def load_data(
        self,
        pool_name,
        class_name=None,
        group_filter=None,
        gender_filter=None,
        group_index=0,
        gender_index=0,
        invalid_class_options=None,
    ):
        """
        加载抽奖池数据（用于动画缓存）
        """
        try:
            self.current_pool_name = pool_name
            self.current_class_name = class_name
            self.current_group_filter = group_filter
            self.current_gender_filter = gender_filter
            self.current_group_index = group_index
            self.current_gender_index = gender_index

            invalid_options = set(invalid_class_options or [])
            invalid_options.update(
                get_content_combo_name_async("lottery", "list_combobox") or []
            )
            invalid_options.add(
                get_content_name_async("roll_call_list", "select_class_name")
            )

            if invalid_options and class_name in invalid_options:
                class_name = None
                self.current_class_name = None

            class_text = str(class_name).strip() if class_name else ""
            self.enable_student_assignment = bool(
                class_text and class_text not in invalid_options
            )

            self.prizes = get_pool_list(pool_name)
            self.prizes = [p for p in self.prizes if p.get("exist", True)]
            logger.info(f"加载 {len(self.prizes)} 个奖品在这个奖池中 {pool_name}")

            self.data_loaded.emit(True)
            return True

        except Exception as e:
            logger.exception(f"加载抽奖池数据时出错: {e}")
            self.error_occurred.emit(str(e))
            self.data_loaded.emit(False)
            return False

    def invalidate_settings_cache(self):
        self._render_settings_cache_pool = None
        self._render_settings_cache = None
        self._notification_settings_cache_pool = None
        self._notification_settings_cache = None

    def invalidate_total_count_cache(self):
        self._total_count_cache_pool = None
        self._total_count_cache_value = None
        self._total_count_cache_draw_type = None

    def get_pool_total_count(self, pool_name: str, refresh: bool = False) -> int:
        if not pool_name:
            self._total_count_cache_pool = pool_name
            self._total_count_cache_value = 0
            self._total_count_cache_draw_type = None
            return 0

        try:
            draw_type = int(read_lottery_setting(pool_name, "draw_type", 0) or 0)
        except Exception:
            draw_type = 0

        if (
            not refresh
            and self._total_count_cache_pool == pool_name
            and getattr(self, "_total_count_cache_draw_type", None) == draw_type
            and isinstance(self._total_count_cache_value, int)
        ):
            return self._total_count_cache_value

        total_count = LotteryUtils.get_prize_total_count(pool_name)
        self._total_count_cache_pool = pool_name
        self._total_count_cache_value = int(total_count or 0)
        self._total_count_cache_draw_type = draw_type
        return self._total_count_cache_value

    def get_render_settings(self, pool_name: str | None = None, refresh: bool = False):
        if pool_name is None:
            pool_name = self.current_pool_name

        if (
            refresh
            or self._render_settings_cache is None
            or self._render_settings_cache_pool != pool_name
        ):
            self._render_settings_cache_pool = pool_name
            self._render_settings_cache = {
                "font_size": get_safe_font_size_list_specific(
                    "lottery_settings",
                    "lottery_list_specific_settings",
                    pool_name,
                    "font_size",
                ),
                "animation_color": read_lottery_setting(
                    pool_name, "animation_color_theme"
                ),
                "display_format": read_lottery_setting(pool_name, "display_format"),
                "display_style": read_lottery_setting(pool_name, "display_style"),
                "show_student_image": read_lottery_setting(pool_name, "lottery_image"),
                "image_position": read_lottery_setting(
                    pool_name, "lottery_image_position"
                ),
                "show_random": read_lottery_setting(pool_name, "show_random"),
                "show_tags": read_lottery_setting(pool_name, "show_tags"),
            }

        return self._render_settings_cache

    def get_notification_settings(
        self, pool_name: str | None = None, refresh: bool = False
    ):
        if refresh:
            self._notification_settings_cache = None

        if pool_name is None:
            pool_name = self.current_pool_name

        call_notification_service = readme_settings_async(
            "lottery_notification_settings", "call_notification_service"
        )
        if not call_notification_service:
            self._notification_settings_cache = None
            return None

        if (
            self._notification_settings_cache is None
            or self._notification_settings_cache_pool != pool_name
        ):
            self._notification_settings_cache_pool = pool_name
            self._notification_settings_cache = (
                LotteryUtils.prepare_notification_settings(pool_name)
            )

        return self._notification_settings_cache

    def build_draw_context(
        self,
        pool_name: str,
        class_name: str | None,
        group_filter: str | None,
        gender_filter: str | None,
        group_index: int,
        gender_index: int,
        *,
        invalid_class_options=None,
    ) -> LotteryDrawContext:
        if invalid_class_options and class_name in invalid_class_options:
            class_name = None
        return LotteryDrawContext(
            pool_name=pool_name,
            class_name=class_name,
            group_filter=group_filter,
            gender_filter=gender_filter,
            group_index=group_index,
            gender_index=gender_index,
        )

    def prepare_draw(
        self,
        context: LotteryDrawContext,
        *,
        invalid_class_options=None,
        refresh_settings: bool = True,
        refresh_total_count: bool = False,
    ) -> LotteryDrawPlan:
        self.load_data(
            context.pool_name,
            context.class_name,
            context.group_filter,
            context.gender_filter,
            context.group_index,
            context.gender_index,
            invalid_class_options=invalid_class_options,
        )
        self.get_render_settings(context.pool_name, refresh=refresh_settings)
        self.get_notification_settings(context.pool_name, refresh=refresh_settings)
        self.get_pool_total_count(context.pool_name, refresh=refresh_total_count)

        pool_name = context.pool_name
        animation = read_lottery_setting(pool_name, "animation")
        autoplay_count = read_lottery_setting(pool_name, "autoplay_count")
        animation_interval = read_lottery_setting(pool_name, "animation_interval")
        animation_music = read_lottery_setting(pool_name, "animation_music")
        result_music = read_lottery_setting(pool_name, "result_music")

        try:
            animation = int(animation or 0)
        except Exception:
            animation = 0

        try:
            autoplay_count = int(autoplay_count or 0)
        except Exception:
            autoplay_count = 0

        try:
            animation_interval = int(animation_interval or 0)
        except Exception:
            animation_interval = 0

        return LotteryDrawPlan(
            animation=animation,
            autoplay_count=autoplay_count,
            animation_interval=animation_interval,
            animation_music=animation_music or None,
            result_music=result_music or None,
        )

    def finalize_draw(
        self,
        count: int,
        *,
        group_filter: str = "",
        gender_filter: str = "",
        parent=None,
    ):
        result = self.draw_final_items(count)
        if isinstance(result, dict) and result.get("reset_required"):
            reset_drawn_prize_record(parent, self.current_pool_name)
            if (
                result.get("reset_scope") == "students"
                and self.enable_student_assignment
                and self.current_class_name
            ):
                RollCallUtils.reset_drawn_records(
                    parent,
                    self.current_class_name,
                    self.current_gender_filter,
                    self.current_group_filter,
                )
            return {"reset_required": True, "pool_name": self.current_pool_name}

        selected_items_dict = (
            (result or {}).get("selected_prizes_dict")
            or (result or {}).get("selected_students_dict")
            or []
        )

        threshold = LotteryUtils._get_prize_draw_threshold(self.current_pool_name)
        save_temp = (
            threshold is not None
            or LotteryUtils._get_prize_draw_type(self.current_pool_name) == 1
        )

        self.save_result(
            selected_items_dict,
            group_filter=group_filter,
            gender_filter=gender_filter,
            save_temp=save_temp,
        )

        result = dict(result or {})
        result["save_temp"] = save_temp
        return result

    def reset_all_records(self, *, parent=None):
        reset_drawn_prize_record(parent, self.current_pool_name)
        if self.enable_student_assignment and self.current_class_name:
            RollCallUtils.reset_drawn_records(
                parent,
                self.current_class_name,
                self.current_gender_filter,
                self.current_group_filter,
            )

    def get_random_items(self, count):
        """
        获取随机项（用于动画帧）
        """
        if not self.prizes:
            return []

        # 简单的随机选择用于动画效果
        try:
            # 允许重复用于动画效果
            selected_prizes = [system_random.choice(self.prizes) for _ in range(count)]

            if not self.enable_student_assignment or not self.current_class_name:
                prizes_with_meta = []
                for prize in selected_prizes:
                    prize_copy = dict(prize)
                    prize_name = prize_copy.get("name", "")
                    prize_copy["ipc_lottery_name"] = str(prize_name or "")
                    prize_copy["ipc_group_name"] = ""
                    prize_copy["ipc_student_name"] = ""
                    prize_copy["ipc_display_text"] = str(prize_name or "")
                    prizes_with_meta.append(prize_copy)
                return prizes_with_meta

            candidates = RollCallUtils._get_filtered_candidates(
                self.current_class_name,
                self.current_group_index,
                self.current_group_filter,
                self.current_gender_index,
                self.current_gender_filter,
            )
            if not candidates:
                return selected_prizes

            show_random = read_lottery_setting(self.current_pool_name, "show_random")
            try:
                show_random = int(show_random or 0)
            except Exception:
                show_random = 0

            from app.common.data.list import get_group_members

            prizes_with_students = []
            for prize in selected_prizes:
                prize_copy = dict(prize)
                prize_name = prize_copy.get("name", "")
                prize_copy["ipc_lottery_name"] = str(prize_name or "")

                group_name = ""
                student_name = ""
                student_id = 0
                if self.current_group_index == 1:
                    selected_candidate = system_random.choice(candidates)
                    raw_group = selected_candidate.get("name", "")
                    include_group = show_random in (0, 1, 2, 5, 6, 7, 8, 9)
                    include_name = show_random in (0, 1, 2, 3, 4, 7, 8, 9, 10, 11)

                    group_name = raw_group if include_group else ""

                    if include_name and raw_group:
                        group_members = get_group_members(
                            self.current_class_name, raw_group
                        )
                        if group_members:
                            selected_member = system_random.choice(group_members)
                            student_name = (selected_member or {}).get("name", "")
                            try:
                                student_id = int(
                                    (selected_member or {}).get("id", 0) or 0
                                )
                            except Exception:
                                student_id = 0
                        if not student_name:
                            student_name = raw_group
                else:
                    selected_candidate = system_random.choice(candidates)
                    student_name = selected_candidate.get("name", "")
                    try:
                        student_id = int(selected_candidate.get("id", 0) or 0)
                    except Exception:
                        student_id = 0

                prize_copy["ipc_group_name"] = str(group_name or "")
                prize_copy["ipc_student_name"] = str(student_name or "")
                prize_copy["student_id"] = student_id
                if group_name or student_name:
                    prize_copy["name"] = self._format_prize_student_text(
                        prize_name, group_name, student_name, show_random
                    )
                prize_copy["ipc_display_text"] = str(prize_copy.get("name", "") or "")

                prizes_with_students.append(prize_copy)

            return prizes_with_students
        except Exception:
            return []

    def draw_final_items(self, count):
        """
        执行最终抽取
        """
        result = LotteryUtils.draw_random_prizes(self.current_pool_name, count)
        if not isinstance(result, dict):
            return {
                "selected_prizes": [],
                "pool_name": self.current_pool_name,
                "selected_prizes_dict": [],
            }

        if result.get("reset_required"):
            return result

        if not self.enable_student_assignment or not self.current_class_name:
            return result

        selected_prizes_dict = result.get("selected_prizes_dict") or []
        prize_names = [
            p.get("name", "") for p in selected_prizes_dict if isinstance(p, dict)
        ]
        draw_count = len(prize_names)
        if draw_count <= 0:
            return result

        threshold = LotteryUtils._get_prize_draw_threshold(self.current_pool_name)
        if threshold is None:
            half_repeat = 0
        else:
            try:
                half_repeat = int(threshold)
            except Exception:
                half_repeat = 1
        students_result = LotteryUtils.draw_random_students(
            self.current_class_name,
            self.current_group_index,
            self.current_group_filter,
            self.current_gender_index,
            self.current_gender_filter,
            draw_count,
            half_repeat,
            pool_name=self.current_pool_name,
            prize_list=prize_names,
        )

        if isinstance(students_result, dict) and students_result.get("reset_required"):
            return {
                "reset_required": True,
                "reset_scope": "students",
                "pool_name": self.current_pool_name,
                "class_name": self.current_class_name,
                "group_filter": self.current_group_filter,
                "gender_filter": self.current_gender_filter,
            }

        selected_students = (students_result or {}).get("selected_students") or []
        selected_students_dict = (students_result or {}).get(
            "selected_students_dict"
        ) or []

        selected_prizes_with_students = []
        updated_prizes_dict = []
        show_random = read_lottery_setting(self.current_pool_name, "show_random")
        try:
            show_random = int(show_random or 0)
        except Exception:
            show_random = 0

        include_group = show_random in (0, 1, 2, 5, 6, 7, 8, 9)
        from app.common.data.list import get_group_members

        for idx, prize in enumerate(selected_prizes_dict):
            if not isinstance(prize, dict):
                continue

            prize_id = prize.get("id", "")
            prize_name = prize.get("name", "")
            prize_exist = prize.get("exist", True)

            student_tuple = (
                selected_students[idx] if idx < len(selected_students) else None
            )
            student_dict = (
                selected_students_dict[idx]
                if idx < len(selected_students_dict)
                else None
            )

            display_name = prize_name
            ipc_group_name = ""
            ipc_student_name = ""
            ipc_display_text = str(prize_name or "")
            if student_tuple and len(student_tuple) >= 2 and student_tuple[1]:
                group_name = ""
                student_name = ""

                if self.current_group_index == 1:
                    raw_group = str(student_tuple[1])
                    include_group = show_random in (0, 1, 2, 5, 6, 7, 8, 9)
                    include_name = show_random in (0, 1, 2, 3, 4, 7, 8, 9, 10, 11)

                    group_name = raw_group if include_group else ""

                    if include_name and raw_group:
                        group_members = get_group_members(
                            self.current_class_name, raw_group
                        )
                        if group_members:
                            selected_member = system_random.choice(group_members)
                            student_name = (selected_member or {}).get("name", "")
                        if not student_name:
                            student_name = raw_group
                else:
                    student_name = str(student_tuple[1])

                ipc_group_name = str(group_name or "")
                ipc_student_name = str(student_name or "")
                display_name = self._format_prize_student_text(
                    prize_name, group_name, student_name, show_random
                )
                ipc_display_text = str(display_name or "")

            selected_prizes_with_students.append((prize_id, display_name, prize_exist))

            prize_copy = dict(prize)
            prize_copy["ipc_lottery_name"] = str(prize_name or "")
            prize_copy["ipc_group_name"] = ipc_group_name if include_group else ""
            prize_copy["ipc_student_name"] = ipc_student_name
            prize_copy["ipc_display_text"] = ipc_display_text
            if isinstance(student_dict, dict):
                prize_copy["student"] = student_dict
                prize_copy["student_id"] = student_dict.get("id", "")
                prize_copy["student_name"] = student_dict.get("name", "")
                prize_copy["student_exist"] = student_dict.get("exist", True)
                if include_group and not prize_copy["ipc_group_name"]:
                    prize_copy["ipc_group_name"] = str(
                        student_dict.get("group", "") or ""
                    )
            updated_prizes_dict.append(prize_copy)

        result["selected_prizes"] = selected_prizes_with_students
        result["selected_prizes_dict"] = updated_prizes_dict
        result["selected_students_dict"] = selected_students_dict
        return result

    def save_result(
        self, selected_items, group_filter="", gender_filter="", save_temp=False
    ):
        """
        保存抽奖结果

        Args:
            selected_items: 选中的奖品/学生列表 (dict列表)
            group_filter: 班级/小组过滤器
            gender_filter: 性别过滤器
            save_temp: 是否保存临时记录 (用于不重复/半重复模式)
        """
        if save_temp:
            prize_names = [
                item.get("name", "")
                for item in (selected_items or [])
                if isinstance(item, dict)
            ]
            record_drawn_prize(self.current_pool_name, prize_names)

        saved = save_lottery_history(
            self.current_pool_name, selected_items, group_filter, gender_filter
        )
        if saved:
            record_lottery_metric_async()

        if self.enable_student_assignment and self.current_class_name:
            student_dicts = []
            for item in selected_items or []:
                if not isinstance(item, dict):
                    continue
                student = item.get("student")
                if isinstance(student, dict):
                    student_dicts.append(student)

            selected_names = [
                s.get("name", "") for s in student_dicts if isinstance(s, dict)
            ]
            threshold = LotteryUtils._get_prize_draw_threshold(self.current_pool_name)
            if save_temp and threshold is not None:
                try:
                    half_repeat = int(threshold)
                except Exception:
                    half_repeat = 1
            else:
                half_repeat = 0
            RollCallUtils.record_drawn_students(
                self.current_class_name,
                selected_names,
                student_dicts,
                self.current_gender_filter,
                self.current_group_filter,
                half_repeat,
                report_metric=False,
            )

    def reset_records(self, parent=None):
        """重置抽取记录"""
        reset_drawn_prize_record(parent, self.current_pool_name)
        if self.enable_student_assignment and self.current_class_name:
            RollCallUtils.reset_drawn_records(
                parent,
                self.current_class_name,
                self.current_gender_filter,
                self.current_group_filter,
            )


def start_lottery_draw(widget):
    track_event("lottery_draw")

    manager = widget.manager
    pool_name = widget.pool_list_combobox.currentText()
    class_name = widget.list_combobox.currentText()
    group_filter = widget.range_combobox.currentText()
    gender_filter = widget.gender_combobox.currentText()
    group_index = widget.range_combobox.currentIndex()
    gender_index = widget.gender_combobox.currentIndex()

    list_base_options = get_content_combo_name_async("lottery", "list_combobox")
    context = manager.build_draw_context(
        pool_name,
        class_name,
        group_filter,
        gender_filter,
        group_index,
        gender_index,
        invalid_class_options=list_base_options,
    )
    widget._draw_plan = manager.prepare_draw(
        context,
        invalid_class_options=list_base_options,
        refresh_settings=True,
        refresh_total_count=False,
    )

    widget.start_button.setText(
        get_content_pushbutton_name_async("lottery", "start_button")
    )
    widget.start_button.setEnabled(True)
    try:
        widget.start_button.clicked.disconnect()
    except Exception as e:
        logger.exception("Error disconnecting start_button clicked (ignored): {}", e)

    widget.draw_random()
    plan = widget._draw_plan
    animation = plan.animation if plan else 0
    autoplay_count = plan.autoplay_count if plan else 0
    animation_interval = plan.animation_interval if plan else 0
    animation_music = plan.animation_music if plan else None

    if animation == 0:
        if animation_music:
            loop = readme_settings_async("music_settings", "background_music_loop")
            loop = True if loop is None else loop
            music_player.play_music(
                music_file=animation_music,
                settings_group="lottery_settings",
                loop=loop,
                fade_in=True,
            )

        widget.start_button.setText(
            get_content_pushbutton_name_async("lottery", "stop_button")
        )
        widget.is_animating = True
        widget.animation_timer = QTimer()
        widget.animation_timer.timeout.connect(widget.animate_result)
        widget.animation_timer.start(animation_interval)
        widget.start_button.clicked.connect(lambda: widget.stop_animation())
    elif animation == 1:
        if animation_music:
            loop = readme_settings_async("music_settings", "background_music_loop")
            loop = True if loop is None else loop
            music_player.play_music(
                music_file=animation_music,
                settings_group="lottery_settings",
                loop=loop,
                fade_in=True,
            )

        widget.animation_count = 0
        widget.target_animation_count = autoplay_count
        widget.is_animating = True
        widget.animation_timer = QTimer()
        widget.animation_timer.timeout.connect(widget.animate_result)
        widget.animation_timer.start(animation_interval)
        widget.start_button.setEnabled(False)
        QTimer.singleShot(
            autoplay_count * animation_interval,
            lambda: [
                widget.animation_timer.stop(),
                widget.stop_animation(),
                widget.start_button.setEnabled(True),
            ],
        )
        widget.start_button.clicked.connect(lambda: widget.start_draw())
    elif animation == 2:
        widget.stop_animation()
        widget.start_button.clicked.connect(lambda: widget.start_draw())


def init_file_watcher(widget):
    widget.file_watcher = QFileSystemWatcher()
    setup_file_watcher(widget)


def init_long_press(widget):
    widget.press_timer = QTimer()
    widget.press_timer.timeout.connect(widget.handle_long_press)
    widget.long_press_interval = 100
    widget.long_press_delay = 500
    widget.is_long_pressing = False
    widget.long_press_direction = 0


def init_tts(widget):
    widget.tts_handler = TTSHandler()


def init_animation_state(widget):
    widget.is_animating = False
    widget._draw_plan = None


def handle_long_press(widget):
    if widget.is_long_pressing:
        widget.press_timer.setInterval(widget.long_press_interval)
        widget.update_count(widget.long_press_direction)


def start_long_press(widget, direction):
    widget.long_press_direction = direction
    widget.is_long_pressing = True
    widget.press_timer.setInterval(widget.long_press_delay)
    widget.press_timer.start()


def stop_long_press(widget):
    widget.is_long_pressing = False
    widget.press_timer.stop()


def on_filter_changed(widget, *_):
    try:
        widget.update_many_count_label()
        total_count = widget.manager.get_pool_total_count(
            widget.pool_list_combobox.currentText()
        )
        LotteryUtils.update_start_button_state(widget.start_button, total_count)

        if (
            hasattr(widget, "remaining_list_page")
            and widget.remaining_list_page is not None
        ):
            QTimer.singleShot(APP_INIT_DELAY, widget._update_remaining_list_delayed)
    except Exception as e:
        logger.exception(f"切换筛选条件时发生错误: {e}")


def _update_remaining_list_delayed(widget):
    try:
        if (
            hasattr(widget, "remaining_list_page")
            and widget.remaining_list_page is not None
        ):
            (
                pool_name,
                group_filter,
                gender_filter,
                half_repeat,
                group_index,
                gender_index,
            ) = widget._get_remaining_list_args()

            if hasattr(widget.remaining_list_page, "update_remaining_list"):
                widget.remaining_list_page.update_remaining_list(
                    pool_name,
                    group_filter,
                    gender_filter,
                    half_repeat,
                    group_index,
                    gender_index,
                    emit_signal=False,
                    source="lottery",
                )
            else:
                if hasattr(widget.remaining_list_page, "pool_name"):
                    widget.remaining_list_page.pool_name = pool_name
                if hasattr(widget.remaining_list_page, "group_filter"):
                    widget.remaining_list_page.group_filter = group_filter
                if hasattr(widget.remaining_list_page, "gender_filter"):
                    widget.remaining_list_page.gender_filter = gender_filter
                if hasattr(widget.remaining_list_page, "group_index"):
                    widget.remaining_list_page.group_index = group_index
                if hasattr(widget.remaining_list_page, "gender_index"):
                    widget.remaining_list_page.gender_index = gender_index
                if hasattr(widget.remaining_list_page, "half_repeat"):
                    widget.remaining_list_page.half_repeat = half_repeat

                if hasattr(widget.remaining_list_page, "count_changed"):
                    widget.remaining_list_page.count_changed.emit(
                        widget.remaining_count
                    )
    except Exception as e:
        logger.exception(f"延迟更新剩余名单时发生错误: {e}")


def start_draw(widget):
    if _is_non_class_time():
        if readme_settings_async("linkage_settings", "verification_required"):
            logger.info("当前时间在非上课时间段内，需要密码验证")
            require_and_run("lottery_start", widget, lambda: start_lottery_draw(widget))
        else:
            logger.info("当前时间在非上课时间段内，禁止抽取")
            return
    else:
        start_lottery_draw(widget)


def animate_result(widget):
    widget.draw_random()


def reset_count(widget):
    if _is_non_class_time():
        if readme_settings_async("linkage_settings", "verification_required"):
            logger.info("当前时间在非上课时间段内，需要密码验证")
            require_and_run("lottery_reset", widget, widget._do_reset_count)
        else:
            logger.info("当前时间在非上课时间段内，禁止重置")
            return
    else:
        widget._do_reset_count()


def stop_animation(widget):
    if hasattr(widget, "animation_timer") and widget.animation_timer.isActive():
        widget.animation_timer.stop()
    widget.start_button.setText(
        get_content_pushbutton_name_async("lottery", "start_button")
    )
    widget.start_button.setEnabled(True)
    widget.is_animating = False
    try:
        widget.start_button.clicked.disconnect()
    except Exception as e:
        logger.exception(
            "Error disconnecting start_button clicked during stop_animation (ignored): {}",
            e,
        )

    music_player.stop_music(fade_out=True)

    result = widget.manager.finalize_draw(
        widget.current_count,
        group_filter=widget.range_combobox.currentText(),
        gender_filter=widget.gender_combobox.currentText(),
        parent=widget,
    )

    if isinstance(result, dict) and result.get("reset_required"):
        update_many_count_label(widget)
        if (
            hasattr(widget, "remaining_list_page")
            and widget.remaining_list_page is not None
            and hasattr(widget.remaining_list_page, "count_changed")
        ):
            widget.remaining_list_page.count_changed.emit(widget.remaining_count)
        QTimer.singleShot(APP_INIT_DELAY, widget._update_remaining_list_delayed)
        _reconnect_after_voice(widget)
        return

    widget.final_selected_students = result.get("selected_prizes") or result.get(
        "selected_students"
    )
    widget.final_pool_name = result["pool_name"]
    widget.final_selected_students_dict = result.get(
        "selected_prizes_dict"
    ) or result.get("selected_students_dict")

    widget.final_group_filter = widget.range_combobox.currentText()
    widget.final_gender_filter = widget.gender_combobox.currentText()

    plan = widget._draw_plan
    result_music = plan.result_music if plan else None
    if result_music:
        music_player.play_music(
            music_file=result_music,
            settings_group="lottery_settings",
            loop=False,
            fade_in=True,
        )

    if result.get("save_temp"):
        update_many_count_label(widget)

        if (
            hasattr(widget, "remaining_list_page")
            and widget.remaining_list_page is not None
            and hasattr(widget.remaining_list_page, "count_changed")
        ):
            widget.remaining_list_page.count_changed.emit(widget.remaining_count)

        QTimer.singleShot(APP_INIT_DELAY, widget._update_remaining_list_delayed)

    if widget.final_selected_students is not None:
        actual_draw_count = (
            len(widget.final_selected_students) if widget.final_selected_students else 0
        )
        if actual_draw_count <= 0:
            actual_draw_count = widget.current_count
        display_result(
            widget,
            widget.final_selected_students,
            widget.final_pool_name,
            draw_count=actual_draw_count,
            selected_students_dict=widget.final_selected_students_dict,
        )

        _update_weight_panel(widget)

        settings = widget.manager.get_notification_settings(
            widget.final_pool_name, refresh=True
        )
        if settings is not None:
            settings_for_notify = (
                dict(settings) if isinstance(settings, dict) else settings
            )
            show_random = None
            if isinstance(settings_for_notify, dict):
                show_random = settings_for_notify.get("show_random")
            if show_random is None:
                show_random = read_lottery_setting(
                    widget.final_pool_name, "show_random"
                )
            try:
                show_random = int(show_random or 0)
            except Exception:
                show_random = 0
            include_group = show_random in (0, 1, 2, 5, 6, 7, 8, 9)
            ipc_selected_students = []
            for prize in widget.final_selected_students_dict or []:
                if not isinstance(prize, dict):
                    continue
                student = prize.get("student")
                if not isinstance(student, dict):
                    student = None
                try:
                    student_id = int(prize.get("student_id", 0) or 0)
                except Exception:
                    student_id = 0
                if not student_id and student is not None:
                    try:
                        student_id = int(student.get("id", 0) or 0)
                    except Exception:
                        student_id = 0
                student_name = str(
                    prize.get("ipc_student_name", "")
                    or prize.get("student_name", "")
                    or ""
                )
                display_text = str(
                    prize.get("ipc_display_text", "") or prize.get("name", "") or ""
                )
                group_name = str(prize.get("ipc_group_name", "") or "")
                if not include_group:
                    group_name = ""
                lottery_name = str(
                    prize.get("ipc_lottery_name", "") or prize.get("name", "") or ""
                )
                exists = bool(prize.get("student_exist", prize.get("exist", True)))
                ipc_selected_students.append(
                    {
                        "student_id": student_id,
                        "student_name": student_name,
                        "display_text": display_text,
                        "exists": exists,
                        "group_name": group_name,
                        "lottery_name": lottery_name,
                    }
                )

            if ipc_selected_students and isinstance(settings_for_notify, dict):
                settings_for_notify["ipc_selected_students"] = ipc_selected_students

            ResultDisplayUtils.show_notification_if_enabled(
                widget.final_pool_name,
                widget.final_selected_students,
                actual_draw_count,
                settings_for_notify,
                settings_group="lottery_notification_settings",
            )

        play_voice_result(widget)

    _reconnect_after_voice(widget)


def _reconnect_after_voice(widget):
    try:
        voice_wait_enabled = readme_settings_async(
            "basic_voice_settings", "voice_wait_complete"
        )
    except Exception:
        voice_wait_enabled = True

    if not voice_wait_enabled:
        widget.start_button.clicked.connect(lambda: widget.start_draw())
        return

    try:
        voice_enabled = readme_settings_async("basic_voice_settings", "voice_enable")
    except Exception:
        voice_enabled = False

    if not voice_enabled:
        widget.start_button.clicked.connect(lambda: widget.start_draw())
        return

    if not hasattr(widget, "tts_handler") or widget.tts_handler is None:
        widget.start_button.clicked.connect(lambda: widget.start_draw())
        return

    QTimer.singleShot(200, lambda: _poll_voice_completion(widget))


def _poll_voice_completion(widget, poll_count=0):
    if not widget.tts_handler.playback_system.is_playing():
        widget.start_button.clicked.connect(lambda: widget.start_draw())
        return

    if poll_count > 300:
        logger.warning("Voice playback timeout, reconnecting start button anyway")
        widget.start_button.clicked.connect(lambda: widget.start_draw())
        return

    QTimer.singleShot(100, lambda: _poll_voice_completion(widget, poll_count + 1))


def play_voice_result(widget):
    try:
        voice_settings = {
            "voice_volume": readme_settings_async(
                "basic_voice_settings", "volume_size"
            ),
            "voice_speed": readme_settings_async("basic_voice_settings", "speech_rate"),
            "system_voice_name": readme_settings_async(
                "basic_voice_settings", "system_voice_name"
            ),
        }

        prize_names = [prize[1] for prize in widget.final_selected_students]

        voice_engine = readme_settings_async("basic_voice_settings", "voice_engine")
        engine_type = 1 if voice_engine == "Edge TTS" else 0

        edge_tts_voice_name = readme_settings_async(
            "basic_voice_settings", "edge_tts_voice_name"
        )

        widget.tts_handler.voice_play(
            config=voice_settings,
            student_names=prize_names,
            engine_type=engine_type,
            voice_name=edge_tts_voice_name,
            class_name=widget.pool_list_combobox.currentText(),
        )
    except Exception as e:
        logger.exception(f"播放语音失败: {e}", exc_info=True)


def draw_random(widget):
    if widget.is_animating:
        display_count = widget.current_count
        try:
            remaining_count = int(getattr(widget, "remaining_count", 0) or 0)
        except Exception:
            remaining_count = 0
        if remaining_count > 0:
            display_count = min(display_count, remaining_count)

        prizes = widget.manager.get_random_items(display_count)
        ipc_selected_students = []
        for p in prizes or []:
            if not isinstance(p, dict):
                continue
            try:
                sid = int(p.get("student_id", 0) or 0)
            except Exception:
                sid = 0
            ipc_selected_students.append(
                {
                    "student_id": sid,
                    "student_name": str(p.get("ipc_student_name", "") or ""),
                    "display_text": str(
                        p.get("ipc_display_text", p.get("name", "")) or ""
                    ),
                    "exists": bool(p.get("exist", True)),
                    "group_name": str(p.get("ipc_group_name", "") or ""),
                    "lottery_name": str(
                        p.get("ipc_lottery_name", p.get("name", "")) or ""
                    ),
                }
            )
        selected_prizes = []
        for p in prizes:
            try:
                display_num = int(p.get("student_id", 0) or 0)
            except Exception:
                display_num = 0
            if not display_num:
                try:
                    display_num = int(p.get("id", 0) or 0)
                except Exception:
                    display_num = 0
            selected_prizes.append((display_num, p["name"], p.get("exist", True)))

        display_result_animated(
            widget,
            selected_prizes,
            widget.manager.current_pool_name,
            draw_count=display_count,
            ipc_selected_students=ipc_selected_students,
            selected_students_dict=prizes,
        )


def display_result(
    widget, selected_students, pool_name, draw_count=None, selected_students_dict=None
):
    render_settings = widget.manager.get_render_settings(pool_name, refresh=True)
    if draw_count is None:
        draw_count = widget.current_count
    student_labels = ResultDisplayUtils.create_student_label(
        pool_name,
        selected_students=selected_students,
        selected_students_dict=selected_students_dict,
        draw_count=draw_count,
        font_size=render_settings["font_size"],
        animation_color=render_settings["animation_color"],
        display_format=render_settings["display_format"],
        display_style=render_settings.get("display_style", 0),
        show_student_image=render_settings["show_student_image"],
        image_position=render_settings.get("image_position"),
        group_index=0,
        show_random=render_settings["show_random"],
        settings_group="lottery_settings",
        show_tags=bool(render_settings.get("show_tags")),
    )
    cached_widgets = ResultDisplayUtils.collect_grid_widgets(widget.result_grid)
    if cached_widgets and len(cached_widgets) == len(student_labels):
        updated = ResultDisplayUtils.update_grid_labels(
            widget.result_grid, student_labels, cached_widgets
        )
        if updated:
            ResultDisplayUtils.dispose_widgets(student_labels)
        else:
            ResultDisplayUtils.display_results_in_grid(
                widget.result_grid, student_labels
            )
    else:
        ResultDisplayUtils.display_results_in_grid(widget.result_grid, student_labels)


def display_result_animated(
    widget,
    selected_students,
    pool_name,
    draw_count=None,
    ipc_selected_students=None,
    selected_students_dict=None,
):
    render_settings = widget.manager.get_render_settings(pool_name, refresh=False)
    if draw_count is None:
        draw_count = widget.current_count

    student_labels = ResultDisplayUtils.create_student_label(
        class_name=pool_name,
        selected_students=selected_students,
        selected_students_dict=selected_students_dict,
        draw_count=draw_count,
        font_size=render_settings["font_size"],
        animation_color=render_settings["animation_color"],
        display_format=render_settings["display_format"],
        display_style=render_settings.get("display_style", 0),
        show_student_image=render_settings["show_student_image"],
        image_position=render_settings.get("image_position"),
        group_index=0,
        show_random=render_settings["show_random"],
        settings_group="lottery_settings",
        show_tags=bool(render_settings.get("show_tags")),
    )
    cached_widgets = ResultDisplayUtils.collect_grid_widgets(widget.result_grid)
    if cached_widgets and len(cached_widgets) == len(student_labels):
        updated = ResultDisplayUtils.update_grid_labels(
            widget.result_grid, student_labels, cached_widgets
        )
        if updated:
            ResultDisplayUtils.dispose_widgets(student_labels)
        else:
            ResultDisplayUtils.display_results_in_grid(
                widget.result_grid, student_labels
            )
    else:
        ResultDisplayUtils.display_results_in_grid(widget.result_grid, student_labels)

    settings = widget.manager.get_notification_settings(pool_name, refresh=False)
    if settings is not None:
        settings_for_notify = dict(settings) if isinstance(settings, dict) else settings
        if (
            ipc_selected_students
            and isinstance(settings_for_notify, dict)
            and isinstance(ipc_selected_students, list)
        ):
            settings_for_notify["ipc_selected_students"] = ipc_selected_students
        ResultDisplayUtils.show_notification_if_enabled(
            pool_name,
            selected_students,
            draw_count,
            settings_for_notify,
            settings_group="lottery_notification_settings",
            is_animating=True,
        )


def do_reset_count(widget):
    widget.current_count = 1
    widget.count_label.setText("1")
    widget.minus_button.setEnabled(False)
    widget.plus_button.setEnabled(True)
    pool_name = widget.pool_list_combobox.currentText()

    class_name = widget.list_combobox.currentText()
    group_filter = widget.range_combobox.currentText()
    gender_filter = widget.gender_combobox.currentText()
    group_index = widget.range_combobox.currentIndex()
    gender_index = widget.gender_combobox.currentIndex()

    list_base_options = get_content_combo_name_async("lottery", "list_combobox")
    widget.manager.load_data(
        pool_name,
        class_name,
        group_filter,
        gender_filter,
        group_index,
        gender_index,
        invalid_class_options=list_base_options,
    )
    widget.manager.reset_all_records(parent=widget)

    widget.clear_result()
    update_many_count_label(widget)

    if (
        hasattr(widget, "remaining_list_page")
        and widget.remaining_list_page is not None
    ):
        QTimer.singleShot(APP_INIT_DELAY, widget._update_remaining_list_delayed)

    if (
        hasattr(widget, "remaining_list_page")
        and widget.remaining_list_page is not None
        and hasattr(widget.remaining_list_page, "count_changed")
    ):
        widget.remaining_list_page.count_changed.emit(widget.remaining_count)


def update_count(widget, change):
    try:
        widget.total_count = widget.manager.get_pool_total_count(
            widget.pool_list_combobox.currentText()
        )
        widget.current_count = max(1, int(widget.count_label.text()) + change)
        widget.count_label.setText(str(widget.current_count))
        widget.minus_button.setEnabled(widget.current_count > 1)
        widget.plus_button.setEnabled(widget.current_count < widget.total_count)
    except (ValueError, TypeError):
        widget.count_label.setText("1")
        widget.minus_button.setEnabled(False)
        widget.plus_button.setEnabled(True)


def get_total_count(widget):
    return LotteryUtils.get_prize_total_count(widget.pool_list_combobox.currentText())


def update_many_count_label(widget):
    total_count, remaining_count, formatted_text = (
        LotteryUtils.update_prize_many_count_label_text(
            widget.pool_list_combobox.currentText()
        )
    )

    widget.remaining_count = remaining_count
    widget.many_count_label.setText(formatted_text)

    LotteryUtils.update_start_button_state(widget.start_button, total_count)


def update_remaining_list_window(widget):
    if (
        hasattr(widget, "remaining_list_page")
        and widget.remaining_list_page is not None
    ):
        try:
            (
                pool_name,
                group_filter,
                gender_filter,
                half_repeat,
                group_index,
                gender_index,
            ) = widget._get_remaining_list_args()

            if hasattr(widget.remaining_list_page, "update_remaining_list"):
                widget.remaining_list_page.update_remaining_list(
                    pool_name,
                    group_filter,
                    gender_filter,
                    half_repeat,
                    group_index,
                    gender_index,
                    emit_signal=False,
                    source="lottery",
                )
        except Exception as e:
            logger.exception(f"更新剩余名单窗口内容失败: {e}")


def show_remaining_list(widget):
    if (
        hasattr(widget, "remaining_list_page")
        and widget.remaining_list_page is not None
    ):
        try:
            window = widget.remaining_list_page.window()
            if window is not None:
                window.raise_()
                window.activateWindow()
                update_remaining_list_window(widget)
                return
        except Exception as e:
            logger.exception(f"激活剩余名单窗口失败: {e}")

    (
        pool_name,
        group_filter,
        gender_filter,
        half_repeat,
        group_index,
        gender_index,
    ) = widget._get_remaining_list_args()

    window, get_page = create_remaining_list_window(
        pool_name,
        group_filter,
        gender_filter,
        half_repeat,
        group_index,
        gender_index,
        "lottery",
    )

    def on_page_ready(page):
        widget.remaining_list_page = page

        if page and hasattr(page, "count_changed"):
            page.count_changed.connect(widget.update_many_count_label)
            widget.update_many_count_label()

    get_page(on_page_ready)

    window.windowClosed.connect(lambda: setattr(widget, "remaining_list_page", None))

    window.show()


def setup_file_watcher(widget):
    try:
        list_dir = get_data_path("list", "lottery_list")

        if not list_dir.exists():
            list_dir.mkdir(parents=True, exist_ok=True)

        widget.file_watcher.addPath(str(list_dir))

        widget._sync_watcher_files()

        widget.file_watcher.directoryChanged.connect(widget.on_directory_changed)
        widget.file_watcher.fileChanged.connect(widget.on_file_changed)

    except Exception as e:
        logger.exception(f"设置文件监控器失败: {e}")


def on_directory_changed(widget, path):
    try:
        widget._sync_watcher_files()
        QTimer.singleShot(500, widget.refresh_pool_list)
    except Exception as e:
        logger.exception(f"处理文件夹变化事件失败: {e}")


def on_file_changed(widget, path):
    try:
        QTimer.singleShot(500, widget.refresh_pool_list)
    except Exception as e:
        logger.exception(f"处理文件变化事件失败: {e}")


def populate_lists(widget):
    try:
        widget.manager.invalidate_total_count_cache()
        widget.manager.invalidate_settings_cache()
        pool_list = get_pool_name_list()
        widget.pool_list_combobox.blockSignals(True)
        widget.pool_list_combobox.clear()
        if pool_list:
            widget.pool_list_combobox.addItems(pool_list)
            default_pool = readme_settings_async("lottery_settings", "default_pool")
            if default_pool and default_pool in pool_list:
                index = pool_list.index(default_pool)
                widget.pool_list_combobox.setCurrentIndex(index)
                logger.debug(f"应用默认抽取奖池: {default_pool}")
            else:
                widget.pool_list_combobox.setCurrentIndex(0)
        widget.pool_list_combobox.blockSignals(False)

        list_base_options = get_content_combo_name_async("lottery", "list_combobox")
        class_list = get_class_name_list()
        widget.list_combobox.blockSignals(True)
        widget.list_combobox.clear()
        if class_list is not None:
            try:
                combo_items = (list_base_options or []) + (class_list or [])
            except Exception:
                combo_items = class_list or []
            widget.list_combobox.addItems(combo_items)
            widget.list_combobox.setCurrentIndex(0)
            try:
                first_text = widget.list_combobox.currentText()
                if first_text in (list_base_options or []):
                    widget.range_combobox.setEnabled(False)
                    widget.gender_combobox.setEnabled(False)
                else:
                    widget.range_combobox.setEnabled(True)
                    widget.gender_combobox.setEnabled(True)
            except Exception:
                pass
        widget.list_combobox.blockSignals(False)

        widget.range_combobox.blockSignals(True)
        try:
            widget.range_combobox.clear()

            base_options = get_content_combo_name_async("lottery", "range_combobox")
            list_base_options = get_content_combo_name_async("lottery", "list_combobox")
            selected_text = widget.list_combobox.currentText()
            if selected_text in (list_base_options or []):
                widget.range_combobox.addItems(base_options[:1])
                widget.gender_combobox.blockSignals(True)
                widget.gender_combobox.clear()
                widget.gender_combobox.addItems(
                    get_content_combo_name_async("lottery", "gender_combobox")[:1]
                )
                widget.gender_combobox.blockSignals(False)
            else:
                group_list = get_group_list(selected_text)
                if group_list:
                    widget.range_combobox.addItems(base_options + group_list)
                else:
                    widget.range_combobox.addItems(base_options[:1])

                widget.gender_combobox.blockSignals(True)
                widget.gender_combobox.clear()
                widget.gender_combobox.addItems(
                    get_content_combo_name_async("lottery", "gender_combobox")
                    + get_gender_list(selected_text)
                )
                widget.gender_combobox.blockSignals(False)
        finally:
            widget.range_combobox.blockSignals(False)

        total_count, remaining_count, formatted_text = (
            LotteryUtils.update_prize_many_count_label_text(
                widget.pool_list_combobox.currentText()
            )
        )

        widget.remaining_count = remaining_count
        widget.many_count_label.setText(formatted_text)

        LotteryUtils.update_start_button_state(widget.start_button, total_count)

        widget._adjustControlWidgetWidths()

    except Exception as e:
        logger.exception(f"延迟填充列表失败: {e}")


def setup_settings_listener(widget):
    from app.tools.settings_access import get_settings_signals

    settings_signals = get_settings_signals()
    settings_signals.settingChanged.connect(widget.onSettingsChanged)


def on_settings_changed(widget, first_level_key, second_level_key, value):
    if first_level_key in ("lottery_settings", "lottery_notification_settings"):
        widget.manager.invalidate_settings_cache()

    if first_level_key == "lottery_settings" and second_level_key in (
        "result_flow_animation_duration",
        "result_flow_animation_style",
    ):
        try:
            if hasattr(widget, "result_grid") and widget.result_grid is not None:
                pool_name = ""
                try:
                    pool_name = widget.pool_list_combobox.currentText()
                except Exception:
                    pool_name = ""
                widget.result_grid.setAnimation(
                    read_lottery_setting(pool_name, "result_flow_animation_duration"),
                    QEasingCurve.OutQuad,
                )
                widget.result_grid.setAnimationStyle(
                    read_lottery_setting(pool_name, "result_flow_animation_style")
                )
        except Exception as e:
            logger.exception(f"更新结果布局动画设置失败: {e}")

    if first_level_key == "page_management" and second_level_key.startswith("lottery"):
        widget.updateUI()
        widget.settingsChanged.emit()


def set_widget_font(widget, font_size):
    try:
        if not isinstance(font_size, (int, float)):
            font_size = int(font_size) if str(font_size).isdigit() else 12

        font_size = int(font_size)
        if font_size <= 0:
            font_size = 12

        custom_font = load_custom_font()
        if custom_font:
            widget.setFont(QFont(custom_font, font_size))
    except (ValueError, TypeError) as e:
        logger.warning(f"设置字体大小失败，使用默认值: {e}")
        custom_font = load_custom_font()
        if custom_font:
            widget.setFont(QFont(custom_font, 12))


def on_class_changed(widget, *_):
    widget.range_combobox.blockSignals(True)
    widget.gender_combobox.blockSignals(True)

    try:
        widget.range_combobox.clear()
        list_base_options = get_content_combo_name_async("lottery", "list_combobox")
        selected_text = widget.list_combobox.currentText()
        if selected_text in (list_base_options or []):
            widget.range_combobox.addItems(
                get_content_combo_name_async("roll_call", "range_combobox")[:1]
            )
            widget.gender_combobox.clear()
            widget.gender_combobox.addItems(
                get_content_combo_name_async("roll_call", "gender_combobox")[:1]
            )
            widget.range_combobox.setEnabled(False)
            widget.gender_combobox.setEnabled(False)
        else:
            base_options = get_content_combo_name_async("roll_call", "range_combobox")
            group_list = get_group_list(selected_text)
            if group_list and group_list != [""]:
                widget.range_combobox.addItems(base_options + group_list)
            else:
                widget.range_combobox.addItems(base_options[:1])

            widget.gender_combobox.clear()
            gender_options = get_content_combo_name_async(
                "roll_call", "gender_combobox"
            )
            gender_list = get_gender_list(selected_text)
            if gender_list and gender_list != [""]:
                widget.gender_combobox.addItems(gender_options + gender_list)
            else:
                widget.gender_combobox.addItems(gender_options[:1])
            widget.range_combobox.setEnabled(True)
            widget.gender_combobox.setEnabled(True)
    except Exception as e:
        logger.exception(f"切换班级时发生错误: {e}")
    finally:
        widget.range_combobox.blockSignals(False)
        widget.gender_combobox.blockSignals(False)


def _update_weight_panel(widget):
    """更新权重透明化面板"""
    try:
        enabled = readme_settings_async("lottery_settings", "show_weight_transparency")
        logger.debug(f"权重透明化(Lottery)：设置={enabled}")
        if not enabled:
            if hasattr(widget, "weight_panel"):
                widget.weight_panel.clear()
                widget.weight_panel.setVisible(False)
            return

        students_with_weight = []
        for prize in widget.final_selected_students_dict or []:
            if not isinstance(prize, dict):
                continue
            student = prize.get("student")
            if isinstance(student, dict):
                students_with_weight.append(student)

        logger.debug(f"权重面板(Lottery)：抽取结果数量={len(students_with_weight)}")

        # lottery 结果中 student 来自 roll_call_utils，通常已有 weight_details
        # 如果没有，尝试计算
        if students_with_weight and "weight_details" not in (
            students_with_weight[0] or {}
        ):
            class_name = getattr(widget, "final_class_name", None) or getattr(
                widget.manager, "current_class_name", ""
            )
            if class_name:
                from app.common.history import calculate_weight

                students_with_weight = calculate_weight(
                    students_with_weight, class_name
                )
                logger.debug(
                    f"权重面板(Lottery)：已计算 {len(students_with_weight)} 个学生权重"
                )

        if hasattr(widget, "weight_panel") and students_with_weight:
            widget.weight_panel.set_students(students_with_weight)
            widget.weight_panel.setVisible(True)
            logger.debug(
                f"权重面板(Lottery)已更新，展示 {len(students_with_weight)} 个学生"
            )
        else:
            logger.debug(
                f"权重面板(Lottery)未显示: has_panel={hasattr(widget, 'weight_panel')}, "
                f"count={len(students_with_weight)}"
            )
    except Exception as e:
        logger.exception(f"更新权重面板失败: {e}")
