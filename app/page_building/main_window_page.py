# 导入库
from PySide6.QtWidgets import QFrame

# 导入页面模板
from app.page_building.page_template import PageTemplate

# 导入自定义页面内容组件
from app.view.main.roll_call import roll_call


class roll_call_page(PageTemplate):
    """创建班级点名页面"""

    def __init__(self, parent: QFrame = None):
        super().__init__(content_widget_class=roll_call, parent=parent)
