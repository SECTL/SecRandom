# 导入页面模板
from PySide6.QtCore import QTimer
from app.page_building.page_template import PageTemplate
from app.page_building.window_template import SimpleWindowTemplate
from app.view.another_window.contributor import contributor_page
from app.view.another_window.import_student_name import ImportStudentNameWindow
from app.view.another_window.set_class_name import SetClassNameWindow
from app.view.another_window.name_setting import NameSettingWindow
from app.view.another_window.gender_setting import GenderSettingWindow
from app.view.another_window.group_setting import GroupSettingWindow
from app.view.another_window.remaining_list import RemainingListPage
from app.Language.obtain_language import *

# 全局变量，用于保持窗口引用，防止被垃圾回收
_window_instances = {}


# ==================================================
# 班级名称设置窗口
# ==================================================
class set_class_name_window_template(PageTemplate):
    """班级名称设置窗口类
    使用PageTemplate创建班级名称设置页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=SetClassNameWindow, parent=parent)


def create_set_class_name_window():
    """
    创建班级名称设置窗口

    Returns:
        创建的窗口实例
    """
    title = get_content_name_async("set_class_name", "title")
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template("set_class_name", set_class_name_window_template)
    window.switch_to_page("set_class_name")
    _window_instances["set_class_name"] = window
    window.windowClosed.connect(lambda: _window_instances.pop("set_class_name", None))
    window.show()
    return


# ==================================================
# 导入学生名单导入窗口
# ==================================================
class import_student_name_window_template(PageTemplate):
    """学生名单导入窗口类
    使用PageTemplate创建学生名单导入页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=ImportStudentNameWindow, parent=parent)


def create_import_student_name_window():
    """
    创建学生名单导入窗口

    Returns:
        创建的窗口实例
    """
    title = get_content_name_async("import_student_name", "title")
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template(
        "import_student_name", import_student_name_window_template
    )
    window.switch_to_page("import_student_name")
    _window_instances["import_student_name"] = window
    window.windowClosed.connect(
        lambda: _window_instances.pop("import_student_name", None)
    )
    window.show()
    return


# ==================================================
# 姓名设置窗口
# ==================================================
class name_setting_window_template(PageTemplate):
    """姓名设置窗口类
    使用PageTemplate创建姓名设置页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=NameSettingWindow, parent=parent)


def create_name_setting_window():
    """
    创建姓名设置窗口

    Returns:
        创建的窗口实例
    """
    title = get_content_name_async("name_setting", "title")
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template("name_setting", name_setting_window_template)
    window.switch_to_page("name_setting")
    _window_instances["name_setting"] = window
    window.windowClosed.connect(lambda: _window_instances.pop("name_setting", None))
    window.show()
    return


# ==================================================
# 性别设置窗口
# ==================================================
class gender_setting_window_template(PageTemplate):
    """性别设置窗口类
    使用PageTemplate创建性别设置页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=GenderSettingWindow, parent=parent)


def create_gender_setting_window():
    """
    创建性别设置窗口

    Returns:
        创建的窗口实例
    """
    title = get_content_name_async("gender_setting", "title")
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template("gender_setting", gender_setting_window_template)
    window.switch_to_page("gender_setting")
    _window_instances["gender_setting"] = window
    window.windowClosed.connect(lambda: _window_instances.pop("gender_setting", None))
    window.show()
    return


# ==================================================
# 小组设置窗口
# ==================================================
class group_setting_window_template(PageTemplate):
    """小组设置窗口类
    使用PageTemplate创建小组设置页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=GroupSettingWindow, parent=parent)


def create_group_setting_window():
    """
    创建小组设置窗口

    Returns:
        创建的窗口实例
    """
    title = get_content_name_async("group_setting", "title")
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template("group_setting", group_setting_window_template)
    window.switch_to_page("group_setting")
    _window_instances["group_setting"] = window
    window.windowClosed.connect(lambda: _window_instances.pop("group_setting", None))
    window.show()
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
    title = get_content_name_async("about", "contributor")
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template("contributor", contributor_window_template)
    window.switch_to_page("contributor")
    _window_instances["contributor"] = window
    window.windowClosed.connect(lambda: _window_instances.pop("contributor", None))
    window.show()
    return


# ==================================================
# 剩余名单窗口
# ==================================================
class remaining_list_window_template(PageTemplate):
    """剩余名单窗口类
    使用PageTemplate创建剩余名单页面"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=RemainingListPage, parent=parent)


def create_remaining_list_window(
    class_name: str,
    group_filter: str,
    gender_filter: str,
    half_repeat: int = 0,
    group_index: int = 0,
    gender_index: int = 0,
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

    Returns:
        创建的窗口实例和页面实例
    """
    # 检查是否已存在剩余名单窗口
    if "remaining_list" in _window_instances:
        window = _window_instances["remaining_list"]
        try:
            # 激活窗口并置于前台
            window.raise_()
            window.activateWindow()
            
            # 获取页面实例并更新数据
            page = None
            
            def setup_page():
                nonlocal page
                page_template = window.get_page("remaining_list")
                if page_template and hasattr(page_template, "contentWidget"):
                    page = page_template.contentWidget
                    if hasattr(page, "update_remaining_list"):
                        page.update_remaining_list(
                            class_name,
                            group_filter,
                            gender_filter,
                            half_repeat,
                            group_index,
                            gender_index,
                        )
            
            # 使用延迟调用确保内容控件已创建
            QTimer.singleShot(100, setup_page)
            
            # 创建一个回调函数，用于在页面设置完成后获取页面实例
            def get_page_callback(callback):
                def check_page():
                    if page is not None:
                        callback(page)
                    else:
                        QTimer.singleShot(50, check_page)

                check_page()
            
            return window, get_page_callback
        except Exception as e:
            # 如果窗口已损坏，从字典中移除并创建新窗口
            logger.error(f"激活剩余名单窗口失败: {e}")
            _window_instances.pop("remaining_list", None)
    
    # 创建新窗口
    title = "剩余名单"
    window = SimpleWindowTemplate(title, width=800, height=600)
    window.add_page_from_template("remaining_list", remaining_list_window_template)
    window.switch_to_page("remaining_list")

    # 获取页面实例并更新数据
    page = None

    def setup_page():
        nonlocal page
        page_template = window.get_page("remaining_list")
        if page_template and hasattr(page_template, "contentWidget"):
            page = page_template.contentWidget
            if hasattr(page, "update_remaining_list"):
                page.update_remaining_list(
                    class_name,
                    group_filter,
                    gender_filter,
                    half_repeat,
                    group_index,
                    gender_index,
                )

    # 使用延迟调用确保内容控件已创建
    QTimer.singleShot(100, setup_page)

    _window_instances["remaining_list"] = window
    window.windowClosed.connect(lambda: _window_instances.pop("remaining_list", None))
    window.show()

    # 创建一个回调函数，用于在页面设置完成后获取页面实例
    def get_page_callback(callback):
        def check_page():
            if page is not None:
                callback(page)
            else:
                QTimer.singleShot(50, check_page)

        check_page()

    return window, get_page_callback
