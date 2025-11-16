# 导入库
from PySide6.QtWidgets import QFrame

# 导入页面模板
from app.page_building.page_template import PageTemplate, PivotPageTemplate

# 导入自定义页面内容组件
# 为了延迟导入，传入字符串路径，实际类将在 PageTemplate.create_content 动态导入
# content path format: 'module.submodule:ClassName' 或 'module.submodule.ClassName'
HOME_PATH = "app.view.settings.home:home"
BASIC_SETTINGS_PATH = "app.view.settings.basic_settings:basic_settings"
ABOUT_PATH = "app.view.settings.about:about"

# 导入默认设置
from app.tools.settings_default import *
from app.Language.obtain_language import *


class home_page(PageTemplate):
    """创建主页页面"""

    def __init__(self, parent: QFrame = None):
        super().__init__(content_widget_class=HOME_PATH, parent=parent)


class basic_settings_page(PageTemplate):
    """创建基础设置页面"""

    def __init__(self, parent: QFrame = None):
        super().__init__(content_widget_class=BASIC_SETTINGS_PATH, parent=parent)


class list_management_page(PivotPageTemplate):
    """创建名单管理页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "roll_call_list": get_content_name_async("roll_call_list", "title"),
            "roll_call_table": get_content_name_async("roll_call_table", "title"),
            "lottery_list": get_content_name_async("lottery_list", "title"),
            "lottery_table": get_content_name_async("lottery_table", "title"),
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.list_management")


class extraction_settings_page(PivotPageTemplate):
    """创建抽取设置页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "roll_call_settings": get_content_name_async("roll_call_settings", "title"),
            "quick_draw_settings": get_content_name_async(
                "quick_draw_settings", "title"
            ),
            "instant_draw_settings": get_content_name_async(
                "instant_draw_settings", "title"
            ),
            "custom_draw_settings": get_content_name_async(
                "custom_draw_settings", "title"
            ),
            "lottery_settings": get_content_name_async("lottery_settings", "title"),
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.extraction_settings")


class notification_settings_page(PivotPageTemplate):
    """创建通知服务页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "roll_call_notification_settings": get_content_name_async(
                "roll_call_notification_settings", "title"
            ),
            "quick_draw_notification_settings": get_content_name_async(
                "quick_draw_notification_settings", "title"
            ),
            "instant_draw_notification_settings": get_content_name_async(
                "instant_draw_notification_settings", "title"
            ),
            "custom_draw_notification_settings": get_content_name_async(
                "custom_draw_notification_settings", "title"
            ),
            "lottery_notification_settings": get_content_name_async(
                "lottery_notification_settings", "title"
            ),
            # "more_notification_settings": get_content_name_async("more_notification_settings", "title")
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.notification_settings")


class safety_settings_page(PivotPageTemplate):
    """创建安全设置页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "basic_safety_settings": get_content_name_async(
                "basic_safety_settings", "title"
            ),
            # "advanced_safety_settings": get_content_name_async("advanced_safety_settings", "title")
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.safety_settings")


class custom_settings_page(PivotPageTemplate):
    """创建个性设置页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "page_management": get_content_name_async("page_management", "title"),
            "floating_window_management": get_content_name_async(
                "floating_window_management", "title"
            ),
            "sidebar_tray_management": get_content_name_async(
                "sidebar_tray_management", "title"
            ),
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.custom_settings")


class voice_settings_page(PivotPageTemplate):
    """创建语音设置页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "basic_voice_settings": get_content_name_async(
                "basic_voice_settings", "title"
            ),
            # "specific_announcements": get_content_name_async("specific_announcements", "title")
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.voice_settings")


class history_page(PivotPageTemplate):
    """创建历史记录页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "history_management": get_content_name_async("history_management", "title"),
            "roll_call_history_table": get_content_name_async(
                "roll_call_history_table", "title"
            ),
            "lottery_history_table": get_content_name_async(
                "lottery_history_table", "title"
            ),
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.history")


class more_settings_page(PivotPageTemplate):
    """创建更多设置页面"""

    def __init__(self, parent: QFrame = None):
        page_config = {
            "advanced_settings": get_content_name_async("advanced_settings", "title"),
            # "debug": get_content_name_async("debug", "title"),
            # "experimental_features": get_content_name_async("experimental_features", "title"),
        }
        super().__init__(page_config, parent)
        self.set_base_path("app.view.settings.more_settings")


class about_page(PageTemplate):
    """创建关于页面"""

    def __init__(self, parent: QFrame = None):
        super().__init__(content_widget_class=ABOUT_PATH, parent=parent)
