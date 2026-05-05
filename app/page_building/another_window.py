# 导入页面模板
from PySide6.QtCore import QTimer
from loguru import logger
from app.page_building.page_template import PageTemplate
from app.page_building.window_template import SimpleWindowTemplate
from app.view.another_window.contributor import contributor_page
from app.view.another_window.student.import_student_name import ImportStudentNameWindow
from app.view.another_window.student.set_class_name import SetClassNameWindow
from app.view.another_window.student.name_setting import NameSettingWindow
from app.view.another_window.student.gender_setting import GenderSettingWindow
from app.view.another_window.student.group_setting import GroupSettingWindow
from app.view.another_window.student.tag_setting import StudentTagSettingWindow
from app.view.another_window.prize.import_prize_name import ImportPrizeNameWindow
from app.view.another_window.prize.set_pool_name import SetPoolNameWindow
from app.view.another_window.prize.prize_name_setting import PrizeNameSettingWindow
from app.view.another_window.prize.prize_weight_setting import PrizeWeightSettingWindow
from app.view.another_window.prize.prize_count_setting import PrizeCountSettingWindow
from app.view.another_window.prize.tag_setting import PrizeTagSettingWindow
from app.view.another_window.remaining_list import RemainingListPage
from app.view.another_window.current_config_viewer import CurrentConfigViewerWindow
from app.view.another_window.log_viewer import LogViewerWindow
from app.view.another_window.backup_manager import BackupManagerWindow
from app.view.another_window.countdown_timer import CountdownTimerPage
from app.view.another_window.weight_formula import WeightFormulaPage
from app.Language.obtain_language import *
from app.tools.variable import *

# 全局变量，用于保持窗口引用，防止被垃圾回收
_window_instances = {}


def _try_activate_window(window_key: str):
    window = _window_instances.get(window_key)
    if window is None:
        return None
    try:
        window.raise_()
        window.activateWindow()
        return window
    except Exception as e:
        logger.exception(f"激活{window_key}窗口失败: {e}")
        _window_instances.pop(window_key, None)
        return None


def _create_page_loader(window, page_key: str, on_ready):
    page_holder = {"page": None}

    def setup_page():
        page_template = window.get_page(page_key)
        content_widget = (
            getattr(page_template, "contentWidget", None)
            if page_template is not None
            else None
        )
        if content_widget is None:
            QTimer.singleShot(50, setup_page)
            return
        page_holder["page"] = content_widget
        on_ready(content_widget)

    QTimer.singleShot(APP_INIT_DELAY, setup_page)

    def get_page_callback(callback):
        def check_page():
            page = page_holder["page"]
            if page is not None:
                callback(page)
            else:
                QTimer.singleShot(50, check_page)

        check_page()

    return get_page_callback


def _create_reusable_window(
    window_key: str,
    title_key: tuple,
    template_class,
    width: int,
    height: int,
    parent=None,
):
    window = _try_activate_window(window_key)
    if window is not None:
        return window, False
    title = get_content_name_async(*title_key)
    window = SimpleWindowTemplate(title, width=width, height=height, parent=parent)
    window.add_page_from_template(window_key, template_class)
    window.switch_to_page(window_key)
    _window_instances[window_key] = window
    window.windowClosed.connect(lambda: _window_instances.pop(window_key, None))
    window.show()
    return window, True


# ==================================================
# 班级名称设置窗口
# ==================================================
class set_class_name_window_template(PageTemplate):
    """班级名称设置窗口类
    使用PageTemplate创建班级名称设置页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=SetClassNameWindow, parent=parent)


def create_set_class_name_window(parent=None):
    """
    创建班级名称设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "set_class_name",
        ("set_class_name", "title"),
        set_class_name_window_template,
        800,
        600,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 导入学生名单导入窗口
# ==================================================
class import_student_name_window_template(PageTemplate):
    """学生名单导入窗口类
    使用PageTemplate创建学生名单导入页面"""

    def __init__(self, parent=None, class_name=None):
        def factory(parent):
            return ImportStudentNameWindow(parent=parent, class_name=class_name)

        factory.__name__ = "ImportStudentNameWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_import_student_name_window(class_name=None, parent=None):
    """
    创建学生名单导入窗口

    Args:
        class_name: 要导入的班级名称

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "import_student_name",
        ("import_student_name", "title"),
        lambda page_parent: import_student_name_window_template(
            parent=page_parent, class_name=class_name
        ),
        800,
        600,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 姓名设置窗口
# ==================================================
class name_setting_window_template(PageTemplate):
    """姓名设置窗口类
    使用PageTemplate创建姓名设置页面"""

    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return NameSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "NameSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_name_setting_window(list_name=None, parent=None):
    """
    创建姓名设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "name_setting",
        ("name_setting", "title"),
        lambda page_parent: name_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        800,
        600,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 性别设置窗口
# ==================================================
class gender_setting_window_template(PageTemplate):
    """性别设置窗口类
    使用PageTemplate创建性别设置页面"""

    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return GenderSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "GenderSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_gender_setting_window(list_name=None, parent=None):
    """
    创建性别设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "gender_setting",
        ("gender_setting", "title"),
        lambda page_parent: gender_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        800,
        600,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 小组设置窗口
# ==================================================
class group_setting_window_template(PageTemplate):
    """小组设置窗口类
    使用PageTemplate创建小组设置页面"""

    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return GroupSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "GroupSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_group_setting_window(list_name=None, parent=None):
    """
    创建小组设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "group_setting",
        ("group_setting", "title"),
        lambda page_parent: group_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        800,
        600,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 标签设置窗口
# ==================================================
class student_tag_setting_window_template(PageTemplate):
    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return StudentTagSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "StudentTagSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_student_tag_setting_window(list_name=None, parent=None):
    window, _ = _create_reusable_window(
        "student_tag_setting",
        ("tag_setting", "title"),
        lambda page_parent: student_tag_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        900,
        650,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 奖池名称设置窗口
# ==================================================
class set_pool_name_window_template(PageTemplate):
    """奖池名称设置窗口类
    使用PageTemplate创建奖池名称设置页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=SetPoolNameWindow, parent=parent)


def create_set_pool_name_window():
    """
    创建奖池名称设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "set_prize_name",
        ("set_prize_name", "title"),
        set_pool_name_window_template,
        800,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 导入奖品名单导入窗口
# ==================================================
class import_prize_name_window_template(PageTemplate):
    """奖品名单导入窗口类
    使用PageTemplate创建奖品名单导入页面"""

    def __init__(self, parent=None, pool_name=None):
        def factory(parent):
            return ImportPrizeNameWindow(parent=parent, pool_name=pool_name)

        factory.__name__ = "ImportPrizeNameWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_import_prize_name_window(pool_name=None):
    """
    创建奖品名单导入窗口

    Args:
        pool_name: 要导入的奖池名称

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "import_prize_name",
        ("import_prize_name", "title"),
        lambda page_parent: import_prize_name_window_template(
            parent=page_parent, pool_name=pool_name
        ),
        800,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 奖品名称设置窗口
# ==================================================
class prize_name_setting_window_template(PageTemplate):
    """奖品名称设置窗口类
    使用PageTemplate创建奖品名称设置页面"""

    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return PrizeNameSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "PrizeNameSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_prize_setting_window(list_name=None):
    """
    创建奖品名称设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "prize_name_setting",
        ("prize_name_setting", "title"),
        lambda page_parent: prize_name_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        800,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 权重设置窗口
# ==================================================
class prize_weight_setting_window_template(PageTemplate):
    """奖品权重设置窗口类
    使用PageTemplate创建奖品权重设置页面"""

    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return PrizeWeightSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "PrizeWeightSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_prize_weight_setting_window(list_name=None):
    """
    创建奖品权重设置窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "prize_weight_setting",
        ("prize_weight_setting", "title"),
        lambda page_parent: prize_weight_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        800,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 奖品标签设置窗口
# ==================================================
class prize_tag_setting_window_template(PageTemplate):
    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return PrizeTagSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "PrizeTagSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_prize_tag_setting_window(list_name=None):
    window, _ = _create_reusable_window(
        "prize_tag_setting",
        ("prize_tag_setting", "title"),
        lambda page_parent: prize_tag_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        900,
        650,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 数量设置窗口
# ==================================================
class prize_count_setting_window_template(PageTemplate):
    def __init__(self, parent=None, list_name=None):
        def factory(parent):
            return PrizeCountSettingWindow(parent=parent, list_name=list_name)

        factory.__name__ = "PrizeCountSettingWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_prize_count_setting_window(list_name=None):
    window, _ = _create_reusable_window(
        "prize_count_setting",
        ("count_setting", "title"),
        lambda page_parent: prize_count_setting_window_template(
            parent=page_parent, list_name=list_name
        ),
        800,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 贡献者窗口
# ==================================================
class contributor_window_template(PageTemplate):
    """贡献者窗口类
    使用PageTemplate创建贡献者页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=contributor_page, parent=parent)


def create_contributor_window():
    """
    创建贡献者窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "contributor",
        ("about", "contributor"),
        contributor_window_template,
        900,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 计时器窗口
# ==================================================
class countdown_timer_window_template(PageTemplate):
    """计时器窗口类
    使用PageTemplate创建计时器页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=CountdownTimerPage, parent=parent)


def create_countdown_timer_window():
    """
    创建计时器窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "countdown_timer",
        ("countdown_timer", "title"),
        countdown_timer_window_template,
        980,
        650,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 剩余名单窗口
# ==================================================
class remaining_list_window_template(PageTemplate):
    """剩余名单窗口类
    使用PageTemplate创建剩余名单页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=RemainingListPage, parent=parent)
        self._source = "roll_call"

    def set_source(self, source: str) -> None:
        """设置source参数"""
        self._source = source

    def create_content_widget(self):
        """创建内容控件时传递source参数"""
        return RemainingListPage(parent=self, source=self._source)


def create_remaining_list_window(
    class_name: str,
    group_filter: str,
    gender_filter: str,
    half_repeat: int = 0,
    group_index: int = 0,
    gender_index: int = 0,
    source: str = "roll_call",
):
    """
    创建剩余名单窗口

    Args:
        class_name: 班级名称
        group_filter: 分组筛选条件
        gender_filter: 性别筛选条件
        half_repeat: 重复抽取次数
        group_index: 分组索引
        gender_index: 性别索引
        source: 来源页面，"roll_call"或"lottery"

    Returns:
        创建的窗口实例和页面实例
    """
    window, created = _create_reusable_window(
        "remaining_list",
        ("remaining_list", "windows_title"),
        remaining_list_window_template,
        900,
        600,
    )
    if created:
        page_template = window.get_page("remaining_list")
        if hasattr(page_template, "set_source"):
            page_template.set_source(source)

    def on_ready(page):
        if hasattr(page, "update_remaining_list"):
            page.update_remaining_list(
                class_name,
                group_filter,
                gender_filter,
                half_repeat,
                group_index,
                gender_index,
                source=source,
            )
        if created:
            try:
                window.windowClosed.connect(
                    lambda: getattr(page, "stop_loader", lambda: None)()
                )
            except Exception:
                pass

    get_page_callback = _create_page_loader(window, "remaining_list", on_ready)

    return window, get_page_callback


# ==================================================
# 权重计算规则窗口
# ==================================================
class weight_formula_window_template(PageTemplate):
    def __init__(self, parent=None, settings_group=None):
        self._sr_settings_group = settings_group

        def factory(parent):
            return WeightFormulaPage(
                parent=parent, settings_group=self._sr_settings_group
            )

        factory.__name__ = "WeightFormulaPage"
        super().__init__(content_widget_class=factory, parent=parent)


def create_weight_formula_window(parent=None, settings_group=None):
    title = get_content_name_async(
        settings_group or "lottery_settings", "wp_formula_title"
    )
    window = SimpleWindowTemplate(title, width=550, height=500, parent=parent)
    window.add_page_from_template(
        "weight_formula",
        weight_formula_window_template,
        settings_group=settings_group,
    )
    window.switch_to_page("weight_formula")
    window.show()
    return window


# ==================================================
# 当前配置查看窗口
# ==================================================
class current_config_viewer_window_template(PageTemplate):
    """当前配置查看窗口类
    使用PageTemplate创建当前配置查看页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=CurrentConfigViewerWindow, parent=parent)


def create_current_config_viewer_window():
    """
    创建当前配置查看窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "current_config_viewer",
        ("linkage_settings", "cses_import_settings", "name"),
        current_config_viewer_window_template,
        800,
        600,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 日志查看窗口
# ==================================================
class log_viewer_window_template(PageTemplate):
    """日志查看窗口类
    使用PageTemplate创建日志查看页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=LogViewerWindow, parent=parent)


def create_log_viewer_window():
    """
    创建日志查看窗口

    Returns:
        创建的窗口实例
    """
    window, _ = _create_reusable_window(
        "log_viewer", ("log_viewer", "name"), log_viewer_window_template, 900, 600
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 备份管理窗口
# ==================================================
class backup_manager_window_template(PageTemplate):
    def __init__(self, parent=None):
        super().__init__(content_widget_class=BackupManagerWindow, parent=parent)


def create_backup_manager_window():
    window, _ = _create_reusable_window(
        "backup_manager",
        ("basic_settings", "backup_manager"),
        backup_manager_window_template,
        900,
        650,
    )


# ==================================================
# 智能分析窗口
# ==================================================
class analysis_window_template(PageTemplate):
    def __init__(self, parent=None):
        def factory(parent):
            from app.view.another_window.analysis_window import AnalysisWindow

            return AnalysisWindow(parent=parent)

        factory.__name__ = "AnalysisWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_analysis_window():
    window, _ = _create_reusable_window(
        "best_config_analysis",
        ("best_config", "analysis_window_title"),
        analysis_window_template,
        680,
        540,
        parent=None,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 点名独立名单配置窗口
# ==================================================
class roll_call_list_specific_settings_window_template(PageTemplate):
    def __init__(self, parent=None):
        def factory(parent):
            from app.view.settings.extraction_settings.roll_call_settings import (
                RollCallListSpecificSettingsWindow,
            )

            return RollCallListSpecificSettingsWindow(parent=parent)

        factory.__name__ = "RollCallListSpecificSettingsWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_roll_call_list_specific_settings_window(parent=None):
    window, _ = _create_reusable_window(
        "roll_call_list_specific_settings",
        ("roll_call_settings", "list_specific_settings_window"),
        roll_call_list_specific_settings_window_template,
        900,
        700,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 闪抽独立名单配置窗口
# ==================================================
class quick_draw_list_specific_settings_window_template(PageTemplate):
    def __init__(self, parent=None):
        def factory(parent):
            from app.view.settings.extraction_settings.quick_draw_settings import (
                QuickDrawListSpecificSettingsWindow,
            )

            return QuickDrawListSpecificSettingsWindow(parent=parent)

        factory.__name__ = "QuickDrawListSpecificSettingsWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_quick_draw_list_specific_settings_window(parent=None):
    window, _ = _create_reusable_window(
        "quick_draw_list_specific_settings",
        ("quick_draw_settings", "list_specific_settings_window"),
        quick_draw_list_specific_settings_window_template,
        900,
        700,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return


# ==================================================
# 抽奖独立名单配置窗口
# ==================================================
class lottery_list_specific_settings_window_template(PageTemplate):
    def __init__(self, parent=None):
        def factory(parent):
            from app.view.settings.extraction_settings.lottery_settings import (
                LotteryListSpecificSettingsWindow,
            )

            return LotteryListSpecificSettingsWindow(parent=parent)

        factory.__name__ = "LotteryListSpecificSettingsWindow"
        super().__init__(content_widget_class=factory, parent=parent)


def create_lottery_list_specific_settings_window(parent=None):
    window, _ = _create_reusable_window(
        "lottery_list_specific_settings",
        ("lottery_settings", "list_specific_settings_window"),
        lottery_list_specific_settings_window_template,
        900,
        700,
        parent=parent,
    )
    try:
        if hasattr(window, "enable_close_guard"):
            window.enable_close_guard(True)
        else:
            window.setProperty("srCloseGuard", True)
    except Exception:
        pass
    return
