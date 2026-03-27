# ==================================================
# 导入库
# ==================================================

from PySide6.QtCore import QTimer, Signal, QObject
from loguru import logger

from app.common.data.list import *
from app.common.display.result_display import *
from app.common.history import *
from app.tools.settings_access import *
from app.common.music.music_player import music_player
from app.common.roll_call.roll_call_utils import RollCallUtils
from app.Language.obtain_language import (
    get_content_combo_name_async,
    get_any_position_value,
)
from qfluentwidgets import FluentIcon
from app.tools.config import (
    show_notification,
    NotificationType,
    NotificationConfig,
    calculate_remaining_count,
)
from app.tools.personalised import get_theme_icon
from app.tools.list_specific_settings_access import read_quick_draw_setting


# ==================================================
# 闪抽动画类
# ==================================================
class QuickDrawAnimation(QObject):
    """闪抽动画类，封装闪抽动画逻辑"""

    # 动画完成信号
    animation_finished = Signal()

    def __init__(self, roll_call_widget):
        """初始化闪抽动画类

        Args:
            roll_call_widget: 点名控件实例
        """
        super().__init__()
        self.roll_call_widget = roll_call_widget
        self.is_animating = False
        self.animation_timer = None
        self.final_selected_students = None
        self.final_class_name = None
        self.final_selected_students_dict = None
        self.final_ipc_selected_students = None
        self.final_group_filter = None
        self.final_gender_filter = None
        self.active_class_name = None

    def _get_default_filters(self):
        class_name = readme_settings_async("quick_draw_settings", "default_class")
        group_index = 0
        gender_index = 0

        try:
            extend_enabled = bool(
                readme_settings_async(
                    "floating_window_management", "extend_quick_draw_component"
                )
            )
        except Exception:
            extend_enabled = False

        if extend_enabled:
            saved_class_name = str(
                readme_settings_async(
                    "floating_window_management", "quick_draw_class_name"
                )
                or ""
            )
            if saved_class_name and saved_class_name in get_class_name_list():
                class_name = saved_class_name

        self.active_class_name = class_name

        base_group_items = get_content_combo_name_async("roll_call", "range_combobox")
        group_items = base_group_items
        if class_name:
            group_list = get_group_list(class_name)
            group_items = (
                base_group_items + group_list
                if group_list and group_list != [""]
                else base_group_items[:1]
            )
        else:
            group_items = base_group_items[:1]

        group_filter = group_items[0] if group_items else ""
        if extend_enabled:
            saved_group_filter = str(
                readme_settings_async(
                    "floating_window_management", "quick_draw_group_filter"
                )
                or ""
            )
            if saved_group_filter and saved_group_filter in group_items:
                group_index = group_items.index(saved_group_filter)
                group_filter = saved_group_filter

        base_gender_items = get_content_combo_name_async("roll_call", "gender_combobox")
        gender_items = base_gender_items
        if class_name:
            gender_list = get_gender_list(class_name)
            gender_items = (
                base_gender_items + gender_list
                if gender_list and gender_list != [""]
                else base_gender_items[:1]
            )
        else:
            gender_items = base_gender_items[:1]

        gender_filter = gender_items[0] if gender_items else ""
        if extend_enabled:
            saved_gender_filter = str(
                readme_settings_async(
                    "floating_window_management", "quick_draw_gender_filter"
                )
                or ""
            )
            if saved_gender_filter and saved_gender_filter in gender_items:
                gender_index = gender_items.index(saved_gender_filter)
                gender_filter = saved_gender_filter

        return class_name, group_index, group_filter, gender_index, gender_filter

    def _build_selected_students(self, students):
        selected_students = []
        for s in students:
            exist = s[4] if len(s) > 4 else True
            selected_students.append((s[0], s[1], exist))
        return selected_students

    def _set_final_result(self, result):
        self.final_selected_students = result["selected_students"]
        self.final_class_name = result["class_name"]
        self.final_selected_students_dict = result["selected_students_dict"]
        self.final_ipc_selected_students = result.get("ipc_selected_students")
        self.final_group_filter = result["group_filter"]
        self.final_gender_filter = result["gender_filter"]

    def _sync_final_result_to_widget(self):
        self.roll_call_widget.final_selected_students = self.final_selected_students
        self.roll_call_widget.final_class_name = self.final_class_name
        self.roll_call_widget.final_selected_students_dict = (
            self.final_selected_students_dict
        )
        self.roll_call_widget.final_ipc_selected_students = (
            self.final_ipc_selected_students
        )
        self.roll_call_widget.final_group_filter = self.final_group_filter
        self.roll_call_widget.final_gender_filter = self.final_gender_filter

    def _reset_records(self, class_name):
        _, _, group_filter, _, gender_filter = self._get_default_filters()

        self.roll_call_widget.manager.current_class_name = class_name
        self.roll_call_widget.manager.current_gender_filter = gender_filter
        self.roll_call_widget.manager.current_group_filter = group_filter
        self.roll_call_widget.manager.reset_records()

        clear_record = readme_settings_async("roll_call_settings", "clear_record")
        if clear_record in [0, 1]:
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title="提示",
                    content=f"已重置{class_name}已抽取记录",
                    icon=FluentIcon.INFO,
                ),
                parent=self.roll_call_widget,
            )
        else:
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title="提示",
                    content=f"当前处于重复抽取状态，无需清除{class_name}已抽取记录",
                    icon=get_theme_icon("ic_fluent_warning_20_filled"),
                ),
                parent=self.roll_call_widget,
            )

    def start_animation(self, quick_draw_settings):
        """开始闪抽动画

        Args:
            quick_draw_settings: 闪抽设置字典
        """
        logger.debug(f"start_animation: 开始闪抽动画，设置: {quick_draw_settings}")

        self.roll_call_widget.is_quick_draw = True

        class_name, group_index, group_filter, gender_index, gender_filter = (
            self._get_default_filters()
        )
        current_count = read_quick_draw_setting(class_name, "draw_count")
        half_repeat = read_quick_draw_setting(class_name, "half_repeat")

        # 加载数据到管理器
        self.roll_call_widget.manager.load_data(
            class_name,
            group_filter,
            gender_filter,
            group_index,
            gender_index,
            half_repeat,
        )

        animation_music = read_quick_draw_setting(class_name, "animation_music")
        if animation_music:
            music_player.play_music(
                music_file=animation_music,
                settings_group="quick_draw_settings",
                loop=True,
                fade_in=True,
            )

        animation_mode = quick_draw_settings["animation"]
        animation_interval = quick_draw_settings["animation_interval"]
        autoplay_count = quick_draw_settings["autoplay_count"]
        if animation_mode in [0, 1]:
            self.roll_call_widget.manager.start_precompute_final(current_count)

        if animation_mode == 1:
            logger.debug(
                f"start_animation: 自动停止模式，动画间隔: {animation_interval}ms, 运行次数: {autoplay_count}"
            )
            self.is_animating = True
            self.animation_timer = QTimer()
            self.animation_timer.timeout.connect(self._animate_result)
            self.animation_timer.start(animation_interval)
            QTimer.singleShot(
                autoplay_count * animation_interval,
                lambda: [
                    self.stop_animation(),
                    self.animation_timer.stop(),
                ],
            )
        elif animation_mode == 2:
            logger.debug("start_animation: 无动画模式，直接停止动画")
            self.stop_animation()

    def stop_animation(self):
        """停止闪抽动画"""
        logger.debug("stop_animation: 停止闪抽动画")
        if self.animation_timer and self.animation_timer.isActive():
            self.animation_timer.stop()
        self.is_animating = False
        self.roll_call_widget.is_quick_draw = False

        # 执行最终抽取
        class_name = self.active_class_name
        current_count = read_quick_draw_setting(class_name, "draw_count")
        result = self.roll_call_widget.manager.draw_final_students(current_count)

        if result.get("reset_required"):
            self._reset_records(
                class_name
                or readme_settings_async("quick_draw_settings", "default_class")
            )
            self.final_selected_students = []
        else:
            self._set_final_result(result)
            self._sync_final_result_to_widget()

        from app.common.behind_scenes.behind_scenes_utils import BehindScenesUtils

        BehindScenesUtils.clear_cache()

        music_player.stop_music(fade_out=False)

        result_music = read_quick_draw_setting(class_name, "result_music")
        if result_music:
            music_player.play_music(
                music_file=result_music,
                settings_group="quick_draw_settings",
                loop=False,
                fade_in=True,
            )

        # 立即显示最终结果，不等待 signal 触发
        if (
            self.final_selected_students
            and self.final_selected_students is not None
            and len(self.final_selected_students) > 0
            and self.final_class_name
        ):
            draw_count = read_quick_draw_setting(class_name, "draw_count")
            RollCallUtils.show_notification_if_enabled(
                class_name=self.final_class_name,
                selected_students=self.final_selected_students,
                draw_count=draw_count,
                settings_group="quick_draw_notification_settings",
                display_settings=self.quick_draw_settings,
                ipc_selected_students=self.final_ipc_selected_students,
                is_animating=False,
            )

        # 立即显示结果，不等待 animation_finished signal
        self._display_final_result_immediate()

    def _animate_result(self):
        """动画过程中更新显示"""
        if not self.is_animating:
            return
        self.draw_random_students()

        # 检查是否成功抽取到学生，如果没有则停止动画
        if not self.final_selected_students:
            logger.warning("_animate_result: 未抽取到学生，停止动画")
            if self.animation_timer and self.animation_timer.isActive():
                self.animation_timer.stop()
            self.stop_animation()
            return

        class_name = self.active_class_name
        draw_count = read_quick_draw_setting(class_name, "draw_count")
        self.display_result_animated(
            self.final_selected_students,
            self.final_class_name,
            self.quick_draw_settings,
            draw_count,
        )

        # 更新浮窗通知
        self._update_floating_notification()

    def is_animation_active(self):
        """检查动画是否正在运行

        Returns:
            bool: 动画是否正在运行
        """
        return self.is_animating

    def draw_random_students(self):
        """独立的随机学生抽取逻辑，不依赖roll_call_widget的状态"""
        class_name = self.active_class_name or readme_settings_async(
            "quick_draw_settings", "default_class"
        )
        if not class_name:
            # 未设置默认班级，初始化为空结果并停止动画
            logger.warning(
                "draw_random_students: 未设置默认抽取名单，请在设置中配置默认班级"
            )
            self.final_selected_students = []
            self.final_class_name = None
            # 停止动画计时器
            if self.animation_timer and self.animation_timer.isActive():
                self.animation_timer.stop()
            self.stop_animation()
            return False

        current_count = read_quick_draw_setting(class_name, "draw_count")

        if self.is_animating:
            # 动画过程中，使用管理器快速获取随机学生
            students = self.roll_call_widget.manager.get_random_students(current_count)

            self.final_selected_students = self._build_selected_students(students)
            self.final_selected_students_dict = []
            tags_by_id = getattr(self.roll_call_widget.manager, "tags_by_id", {}) or {}
            for s in students or []:
                try:
                    sid = int(s[0] or 0)
                except Exception:
                    sid = 0
                exist = s[4] if len(s) > 4 else True
                self.final_selected_students_dict.append(
                    {
                        "id": sid,
                        "name": s[1],
                        "exist": exist,
                        "tags": tags_by_id.get(sid, []),
                    }
                )
            self.final_class_name = self.roll_call_widget.manager.current_class_name
        else:
            # 非动画状态（直接抽取），执行最终抽取
            result = self.roll_call_widget.manager.draw_final_students(current_count)

            if result.get("reset_required"):
                self._reset_records(class_name)
                return False

            self._set_final_result(result)
            self._sync_final_result_to_widget()

        return True

    def execute_quick_draw(self, quick_draw_settings):
        """执行完整的闪抽流程

        Args:
            quick_draw_settings: 闪抽设置字典
        """
        logger.debug("execute_quick_draw: 执行完整闪抽流程")

        self._get_default_filters()

        # 保存闪抽设置，用于动画过程中更新显示和浮窗通知
        self.quick_draw_settings = quick_draw_settings

        try:
            self.animation_finished.connect(
                lambda: self.display_final_result(quick_draw_settings)
            )

            # 根据动画模式执行不同逻辑
            animation_mode = quick_draw_settings["animation"]

            if animation_mode in [0, 1]:
                # 有动画模式，启动动画
                self.start_animation(quick_draw_settings)
            else:
                # 无动画模式，直接抽取
                self.roll_call_widget.is_quick_draw = True
                # 使用独立的抽取逻辑
                success = self.draw_random_students()
                if success:
                    # 使用闪抽设置更新显示结果
                    self.roll_call_widget.display_result(
                        self.final_selected_students,
                        self.final_class_name,
                        quick_draw_settings,
                    )
                self.roll_call_widget.is_quick_draw = False
                self.animation_finished.emit()

        except Exception as e:
            logger.exception(f"execute_quick_draw: 执行闪抽流程失败: {e}")
            self.stop_animation()

    def display_final_result(self, quick_draw_settings):
        """显示最终的闪抽结果

        Args:
            quick_draw_settings: 闪抽设置字典
        """
        try:
            if self.final_selected_students and self.final_class_name:
                draw_count = read_quick_draw_setting(
                    self.active_class_name, "draw_count"
                )

                RollCallUtils.display_result(
                    result_grid=self.roll_call_widget.result_grid,
                    class_name=self.final_class_name,
                    selected_students=self.final_selected_students,
                    draw_count=draw_count,
                    group_index=getattr(
                        self.roll_call_widget.range_combobox, "currentIndex", lambda: 0
                    )(),
                    settings_group="quick_draw_settings",
                    display_settings=quick_draw_settings,
                    selected_students_dict=self.final_selected_students_dict,
                )

                self._record_drawn_student(quick_draw_settings)

                self._update_quick_draw_count_label()

                from app.tools.variable import APP_INIT_DELAY
                from PySide6.QtCore import QTimer

                QTimer.singleShot(
                    APP_INIT_DELAY, self.roll_call_widget._update_remaining_list_delayed
                )

                self._sync_final_result_to_widget()
                self.roll_call_widget.play_voice_result()

                RollCallUtils.show_notification_if_enabled(
                    class_name=self.final_class_name,
                    selected_students=self.final_selected_students,
                    draw_count=draw_count,
                    settings_group="quick_draw_notification_settings",
                    display_settings=quick_draw_settings,
                    ipc_selected_students=self.final_ipc_selected_students,
                )

        except Exception as e:
            logger.exception(f"display_final_result: 显示最终结果失败: {e}")

    def _display_final_result_immediate(self):
        """立即显示最终结果，用于手动停止时直接显示"""
        try:
            if not self.final_selected_students or not self.final_class_name:
                return

            draw_count = read_quick_draw_setting(self.active_class_name, "draw_count")

            RollCallUtils.display_result(
                result_grid=self.roll_call_widget.result_grid,
                class_name=self.final_class_name,
                selected_students=self.final_selected_students,
                draw_count=draw_count,
                group_index=getattr(
                    self.roll_call_widget.range_combobox, "currentIndex", lambda: 0
                )(),
                settings_group="quick_draw_settings",
                display_settings=self.quick_draw_settings,
                selected_students_dict=self.final_selected_students_dict,
            )

            self._record_drawn_student(self.quick_draw_settings)

            self._update_quick_draw_count_label()

            from app.tools.variable import APP_INIT_DELAY
            from PySide6.QtCore import QTimer

            QTimer.singleShot(
                APP_INIT_DELAY, self.roll_call_widget._update_remaining_list_delayed
            )

            self._sync_final_result_to_widget()
            self.roll_call_widget.play_voice_result()

        except Exception as e:
            logger.exception(
                f"_display_final_result_immediate: 立即显示最终结果失败: {e}"
            )

    def _record_drawn_student(self, quick_draw_settings):
        """记录已抽取的学生

        Args:
            quick_draw_settings: 闪抽设置字典
        """
        try:
            half_repeat = quick_draw_settings.get("half_repeat", 0)

            RollCallUtils.record_drawn_students(
                class_name=self.final_class_name,
                selected_students=self.final_selected_students,
                selected_students_dict=self.final_selected_students_dict,
                gender_filter=self.final_gender_filter,
                group_filter=self.final_group_filter,
                half_repeat=half_repeat,
            )

        except Exception as e:
            logger.exception(f"_record_drawn_student: 记录已抽取学生失败: {e}")

    def _update_quick_draw_count_label(self):
        """更新闪抽的剩余人数显示"""
        try:
            class_name = self.final_class_name
            half_repeat = read_quick_draw_setting(class_name, "half_repeat")
            group_index = 0
            group_filter = self.final_group_filter or ""
            gender_filter = self.final_gender_filter or ""

            total_count = RollCallUtils.get_total_count(
                class_name, group_index, group_filter
            )

            remaining_count = calculate_remaining_count(
                half_repeat=half_repeat,
                class_name=class_name,
                gender_filter=gender_filter,
                group_index=group_index,
                group_filter=group_filter,
                total_count=total_count,
            )

            if remaining_count == 0:
                self._reset_records(class_name)
                remaining_count = total_count

            display_mode = readme_settings_async(
                "page_management", "roll_call_quantity_label"
            )

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
            else:
                text_template = ""

            if text_template:
                formatted_text = text_template.format(
                    total_count=total_count, remaining_count=remaining_count
                )
            else:
                formatted_text = ""

            self.roll_call_widget.remaining_count = remaining_count
            self.roll_call_widget.many_count_label.setText(formatted_text)

        except Exception as e:
            logger.exception(
                f"_update_quick_draw_count_label: 更新剩余人数显示失败: {e}"
            )

    def _update_floating_notification(self):
        """更新浮窗通知内容

        在动画过程中实时更新浮窗通知的内容
        """
        try:
            if self.final_selected_students and self.final_class_name:
                draw_count = read_quick_draw_setting(
                    self.active_class_name, "draw_count"
                )

                RollCallUtils.show_notification_if_enabled(
                    class_name=self.final_class_name,
                    selected_students=self.final_selected_students,
                    draw_count=draw_count,
                    settings_group="quick_draw_notification_settings",
                    display_settings=self.quick_draw_settings,
                    ipc_selected_students=self.final_ipc_selected_students,
                    is_animating=True,
                )

        except Exception as e:
            logger.exception(f"_update_floating_notification: 更新浮窗通知失败: {e}")

    def display_result_animated(
        self,
        selected_students,
        class_name,
        display_settings,
        draw_count,
        selected_students_dict=None,
    ):
        """动画过程中显示结果

        Args:
            selected_students: 选中的学生列表
            class_name: 班级名称
            display_settings: 显示设置字典
            draw_count: 抽取人数
        """
        if selected_students_dict is None:
            selected_students_dict = getattr(self, "final_selected_students_dict", None)
        show_tags = bool(
            (display_settings or {}).get(
                "show_tags", readme_settings_async("quick_draw_settings", "show_tags")
            )
        )
        student_labels = ResultDisplayUtils.create_student_label(
            class_name=class_name,
            selected_students=selected_students,
            selected_students_dict=selected_students_dict,
            draw_count=draw_count,
            font_size=display_settings["font_size"],
            animation_color=display_settings["animation_color_theme"],
            display_format=display_settings["display_format"],
            display_style=0,
            show_student_image=display_settings["student_image"],
            group_index=getattr(
                self.roll_call_widget.range_combobox, "currentIndex", lambda: 0
            )(),
            show_random=display_settings["show_random"],
            settings_group="quick_draw_settings",
            show_tags=show_tags,
        )

        cached_widgets = ResultDisplayUtils.collect_grid_widgets(
            self.roll_call_widget.result_grid
        )
        if cached_widgets and len(cached_widgets) == len(student_labels):
            updated = ResultDisplayUtils.update_grid_labels(
                self.roll_call_widget.result_grid, student_labels, cached_widgets
            )
            if updated:
                ResultDisplayUtils.dispose_widgets(student_labels)
            else:
                ResultDisplayUtils.display_results_in_grid(
                    self.roll_call_widget.result_grid, student_labels
                )
        else:
            ResultDisplayUtils.display_results_in_grid(
                self.roll_call_widget.result_grid, student_labels
            )
