# ==================================================
# 导入库
# ==================================================

from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from loguru import logger
import time


# ==================================================
# 页面管理
# ==================================================
class page_management(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 延迟创建子组件：先插入占位容器并注册创建工厂，避免一次性阻塞
        self._deferred_factories = {}

        def make_placeholder(attr_name: str):
            w = QWidget()
            w.setObjectName(attr_name)
            layout = QVBoxLayout(w)
            layout.setContentsMargins(0, 0, 0, 0)
            self.vBoxLayout.addWidget(w)
            return w

        # create placeholders
        self.page_management_roll_call = make_placeholder("page_management_roll_call")
        self._deferred_factories["page_management_roll_call"] = (
            lambda parent=self: page_management_roll_call(parent)
        )

        self.page_management_lottery = make_placeholder("page_management_lottery")
        self._deferred_factories["page_management_lottery"] = (
            lambda parent=self: page_management_lottery(parent)
        )

        self.page_management_custom = make_placeholder("page_management_custom")
        self._deferred_factories["page_management_custom"] = (
            lambda parent=self: page_management_custom(parent)
        )

        # 分批异步创建真实子组件，间隔以减少主线程瞬时负载
        try:
            for i, name in enumerate(list(self._deferred_factories.keys())):
                QTimer.singleShot(150 * i, lambda n=name: self._create_deferred(n))
        except Exception as e:
            logger.error(f"调度延迟创建子组件失败: {e}")

    def _create_deferred(self, name: str):
        """按需创建延迟注册的子组件并替换占位容器"""
        try:
            factories = getattr(self, "_deferred_factories", {})
            if name not in factories:
                return
            factory = factories.pop(name)
            start = time.perf_counter()
            real_widget = factory()
            elapsed = time.perf_counter() - start
            # 找到占位容器
            placeholder = getattr(self, name, None)
            if placeholder is None:
                # 没有占位则直接插入
                self.vBoxLayout.addWidget(real_widget)
            else:
                # 替换属性引用为真实 widget
                # 保证 placeholder 有 layout
                layout = placeholder.layout()
                if layout is not None:
                    layout.addWidget(real_widget)
                # 更新属性引用，方便后续直接访问
                setattr(self, name, real_widget)

            logger.debug(f"延迟创建子组件 {name} 耗时: {elapsed:.3f}s")
        except Exception as e:
            logger.error(f"创建子组件 {name} 失败: {e}")


class page_management_roll_call(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("page_management", "roll_call"))
        self.setBorderRadius(8)

        # 点名控制面板位置下拉框
        self.roll_call_method_combo = ComboBox()
        self.roll_call_method_combo.addItems(
            get_content_combo_name_async("page_management", "roll_call_method")
        )
        self.roll_call_method_combo.setCurrentIndex(
            readme_settings_async("page_management", "roll_call_method")
        )
        self.roll_call_method_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_method",
                self.roll_call_method_combo.currentIndex(),
            )
        )

        # 姓名设置按钮是否显示开关
        self.show_name_button_switch = SwitchButton()
        self.show_name_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "show_name", "disable"
            )
        )
        self.show_name_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "show_name", "enable"
            )
        )
        self.show_name_button_switch.setChecked(
            readme_settings_async("page_management", "show_name")
        )
        self.show_name_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management", "show_name", self.show_name_button_switch.isChecked()
            )
        )

        # 重置已抽取名单按钮是否显示开关
        self.reset_roll_call_button_switch = SwitchButton()
        self.reset_roll_call_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "reset_roll_call", "disable"
            )
        )
        self.reset_roll_call_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "reset_roll_call", "enable"
            )
        )
        self.reset_roll_call_button_switch.setChecked(
            readme_settings_async("page_management", "reset_roll_call")
        )
        self.reset_roll_call_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "reset_roll_call",
                self.reset_roll_call_button_switch.isChecked(),
            )
        )

        # 增加/减少抽取数量控制条是否显示开关
        self.roll_call_quantity_control_switch = SwitchButton()
        self.roll_call_quantity_control_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_quantity_control", "disable"
            )
        )
        self.roll_call_quantity_control_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_quantity_control", "enable"
            )
        )
        self.roll_call_quantity_control_switch.setChecked(
            readme_settings_async("page_management", "roll_call_quantity_control")
        )
        self.roll_call_quantity_control_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_quantity_control",
                self.roll_call_quantity_control_switch.isChecked(),
            )
        )

        # 开始按钮是否显示开关
        self.roll_call_start_button_switch = SwitchButton()
        self.roll_call_start_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_start_button", "disable"
            )
        )
        self.roll_call_start_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_start_button", "enable"
            )
        )
        self.roll_call_start_button_switch.setChecked(
            readme_settings_async("page_management", "roll_call_start_button")
        )
        self.roll_call_start_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_start_button",
                self.roll_call_start_button_switch.isChecked(),
            )
        )

        # 名单切换下拉框是否显示开关
        self.roll_call_list_combo_switch = SwitchButton()
        self.roll_call_list_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_list", "disable"
            )
        )
        self.roll_call_list_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_list", "enable"
            )
        )
        self.roll_call_list_combo_switch.setChecked(
            readme_settings_async("page_management", "roll_call_list")
        )
        self.roll_call_list_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_list",
                self.roll_call_list_combo_switch.isChecked(),
            )
        )

        # 抽取范围下拉框是否显示开关
        self.roll_call_range_combo_switch = SwitchButton()
        self.roll_call_range_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_range", "disable"
            )
        )
        self.roll_call_range_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_range", "enable"
            )
        )
        self.roll_call_range_combo_switch.setChecked(
            readme_settings_async("page_management", "roll_call_range")
        )
        self.roll_call_range_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_range",
                self.roll_call_range_combo_switch.isChecked(),
            )
        )

        # 抽取性别下拉框是否显示开关
        self.roll_call_gender_combo_switch = SwitchButton()
        self.roll_call_gender_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_gender", "disable"
            )
        )
        self.roll_call_gender_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_gender", "enable"
            )
        )
        self.roll_call_gender_combo_switch.setChecked(
            readme_settings_async("page_management", "roll_call_gender")
        )
        self.roll_call_gender_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_gender",
                self.roll_call_gender_combo_switch.isChecked(),
            )
        )

        # 班级人数/组数标签是否显示开关
        self.roll_call_quantity_label_switch = SwitchButton()
        self.roll_call_quantity_label_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_quantity_label", "disable"
            )
        )
        self.roll_call_quantity_label_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "roll_call_quantity_label", "enable"
            )
        )
        self.roll_call_quantity_label_switch.setChecked(
            readme_settings_async("page_management", "roll_call_quantity_label")
        )
        self.roll_call_quantity_label_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "roll_call_quantity_label",
                self.roll_call_quantity_label_switch.isChecked(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_multiple_swap_20_filled"),
            get_content_name_async("page_management", "roll_call_method"),
            get_content_description_async("page_management", "roll_call_method"),
            self.roll_call_method_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_edit_20_filled"),
            get_content_name_async("page_management", "show_name"),
            get_content_description_async("page_management", "show_name"),
            self.show_name_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_reset_20_filled"),
            get_content_name_async("page_management", "reset_roll_call"),
            get_content_description_async("page_management", "reset_roll_call"),
            self.reset_roll_call_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_autofit_content_20_filled"),
            get_content_name_async("page_management", "roll_call_quantity_control"),
            get_content_description_async(
                "page_management", "roll_call_quantity_control"
            ),
            self.roll_call_quantity_control_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_play_20_filled"),
            get_content_name_async("page_management", "roll_call_start_button"),
            get_content_description_async("page_management", "roll_call_start_button"),
            self.roll_call_start_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_notepad_person_20_filled"),
            get_content_name_async("page_management", "roll_call_list"),
            get_content_description_async("page_management", "roll_call_list"),
            self.roll_call_list_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_convert_range_20_filled"),
            get_content_name_async("page_management", "roll_call_range"),
            get_content_description_async("page_management", "roll_call_range"),
            self.roll_call_range_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_video_person_sparkle_20_filled"),
            get_content_name_async("page_management", "roll_call_gender"),
            get_content_description_async("page_management", "roll_call_gender"),
            self.roll_call_gender_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_person_20_filled"),
            get_content_name_async("page_management", "roll_call_quantity_label"),
            get_content_description_async(
                "page_management", "roll_call_quantity_label"
            ),
            self.roll_call_quantity_label_switch,
        )


class page_management_lottery(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("page_management", "lottery"))
        self.setBorderRadius(8)

        # 抽奖控制面板位置下拉框
        self.lottery_method_combo = ComboBox()
        self.lottery_method_combo.addItems(
            get_content_combo_name_async("page_management", "lottery_method")
        )
        self.lottery_method_combo.setCurrentIndex(
            readme_settings_async("page_management", "lottery_method")
        )
        self.lottery_method_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "page_management",
                "lottery_method",
                self.lottery_method_combo.currentIndex(),
            )
        )

        # 奖品名称设置按钮是否显示开关
        self.show_lottery_name_button_switch = SwitchButton()
        self.show_lottery_name_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "show_lottery_name", "disable"
            )
        )
        self.show_lottery_name_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "show_lottery_name", "enable"
            )
        )
        self.show_lottery_name_button_switch.setChecked(
            readme_settings_async("page_management", "show_lottery_name")
        )
        self.show_lottery_name_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "show_lottery_name",
                self.show_lottery_name_button_switch.isChecked(),
            )
        )

        # 重置已抽取名单按钮是否显示开关
        self.reset_lottery_button_switch = SwitchButton()
        self.reset_lottery_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "reset_lottery", "disable"
            )
        )
        self.reset_lottery_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "reset_lottery", "enable"
            )
        )
        self.reset_lottery_button_switch.setChecked(
            readme_settings_async("page_management", "reset_lottery")
        )
        self.reset_lottery_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "reset_lottery",
                self.reset_lottery_button_switch.isChecked(),
            )
        )

        # 增加/减少抽取数量控制条是否显示开关
        self.lottery_quantity_control_switch = SwitchButton()
        self.lottery_quantity_control_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_quantity_control", "disable"
            )
        )
        self.lottery_quantity_control_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_quantity_control", "enable"
            )
        )
        self.lottery_quantity_control_switch.setChecked(
            readme_settings_async("page_management", "lottery_quantity_control")
        )
        self.lottery_quantity_control_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "lottery_quantity_control",
                self.lottery_quantity_control_switch.isChecked(),
            )
        )

        # 开始按钮是否显示开关
        self.lottery_start_button_switch = SwitchButton()
        self.lottery_start_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_start_button", "disable"
            )
        )
        self.lottery_start_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_start_button", "enable"
            )
        )
        self.lottery_start_button_switch.setChecked(
            readme_settings_async("page_management", "lottery_start_button")
        )
        self.lottery_start_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "lottery_start_button",
                self.lottery_start_button_switch.isChecked(),
            )
        )

        # 名单切换下拉框是否显示开关
        self.lottery_list_combo_switch = SwitchButton()
        self.lottery_list_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_list", "disable"
            )
        )
        self.lottery_list_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_list", "enable"
            )
        )
        self.lottery_list_combo_switch.setChecked(
            readme_settings_async("page_management", "lottery_list")
        )
        self.lottery_list_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "lottery_list",
                self.lottery_list_combo_switch.isChecked(),
            )
        )

        # 班级人数/组数标签是否显示开关
        self.lottery_quantity_label_switch = SwitchButton()
        self.lottery_quantity_label_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_quantity_label", "disable"
            )
        )
        self.lottery_quantity_label_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "lottery_quantity_label", "enable"
            )
        )
        self.lottery_quantity_label_switch.setChecked(
            readme_settings_async("page_management", "lottery_quantity_label")
        )
        self.lottery_quantity_label_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "lottery_quantity_label",
                self.lottery_quantity_label_switch.isChecked(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_multiple_swap_20_filled"),
            get_content_name_async("page_management", "lottery_method"),
            get_content_description_async("page_management", "lottery_method"),
            self.lottery_method_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_edit_20_filled"),
            get_content_name_async("page_management", "show_lottery_name"),
            get_content_description_async("page_management", "show_lottery_name"),
            self.show_lottery_name_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_reset_20_filled"),
            get_content_name_async("page_management", "reset_lottery"),
            get_content_description_async("page_management", "reset_lottery"),
            self.reset_lottery_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_autofit_content_20_filled"),
            get_content_name_async("page_management", "lottery_quantity_control"),
            get_content_description_async(
                "page_management", "lottery_quantity_control"
            ),
            self.lottery_quantity_control_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_play_20_filled"),
            get_content_name_async("page_management", "lottery_start_button"),
            get_content_description_async("page_management", "lottery_start_button"),
            self.lottery_start_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_notepad_person_20_filled"),
            get_content_name_async("page_management", "lottery_list"),
            get_content_description_async("page_management", "lottery_list"),
            self.lottery_list_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_person_20_filled"),
            get_content_name_async("page_management", "lottery_quantity_label"),
            get_content_description_async("page_management", "lottery_quantity_label"),
            self.lottery_quantity_label_switch,
        )


class page_management_custom(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("page_management", "custom"))
        self.setBorderRadius(8)

        # 点名控制面板位置下拉框
        self.custom_method_combo = ComboBox()
        self.custom_method_combo.addItems(
            get_content_combo_name_async("page_management", "custom_method")
        )
        self.custom_method_combo.setCurrentIndex(
            readme_settings_async("page_management", "custom_method")
        )
        self.custom_method_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_method",
                self.custom_method_combo.currentIndex(),
            )
        )

        # 重置已抽取名单按钮是否显示开关
        self.reset_custom_button_switch = SwitchButton()
        self.reset_custom_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "reset_custom", "disable"
            )
        )
        self.reset_custom_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "reset_custom", "enable"
            )
        )
        self.reset_custom_button_switch.setChecked(
            readme_settings_async("page_management", "reset_custom")
        )
        self.reset_custom_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "reset_custom",
                self.reset_custom_button_switch.isChecked(),
            )
        )

        # 增加/减少抽取数量控制条是否显示开关
        self.custom_quantity_control_switch = SwitchButton()
        self.custom_quantity_control_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "custom_quantity_control", "disable"
            )
        )
        self.custom_quantity_control_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "custom_quantity_control", "enable"
            )
        )
        self.custom_quantity_control_switch.setChecked(
            readme_settings_async("page_management", "custom_quantity_control")
        )
        self.custom_quantity_control_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_quantity_control",
                self.custom_quantity_control_switch.isChecked(),
            )
        )

        # 开始按钮是否显示开关
        self.custom_start_button_switch = SwitchButton()
        self.custom_start_button_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "custom_start_button", "disable"
            )
        )
        self.custom_start_button_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "custom_start_button", "enable"
            )
        )
        self.custom_start_button_switch.setChecked(
            readme_settings_async("page_management", "custom_start_button")
        )
        self.custom_start_button_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_start_button",
                self.custom_start_button_switch.isChecked(),
            )
        )

        # 名单切换下拉框是否显示开关
        self.custom_list_combo_switch = SwitchButton()
        self.custom_list_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "custom_list", "disable"
            )
        )
        self.custom_list_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "custom_list", "enable"
            )
        )
        self.custom_list_combo_switch.setChecked(
            readme_settings_async("page_management", "custom_list")
        )
        self.custom_list_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_list",
                self.custom_list_combo_switch.isChecked(),
            )
        )

        # 抽取范围的起始值下拉框是否显示开关
        self.custom_range_start_combo_switch = SwitchButton()
        self.custom_range_start_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "custom_range_start", "disable"
            )
        )
        self.custom_range_start_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "custom_range_start", "enable"
            )
        )
        self.custom_range_start_combo_switch.setChecked(
            readme_settings_async("page_management", "custom_range_start")
        )
        self.custom_range_start_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_range_start",
                self.custom_range_start_combo_switch.isChecked(),
            )
        )

        # 抽取范围的终止值下拉框是否显示开关
        self.custom_range_end_combo_switch = SwitchButton()
        self.custom_range_end_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "custom_range_end", "disable"
            )
        )
        self.custom_range_end_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "custom_range_end", "enable"
            )
        )
        self.custom_range_end_combo_switch.setChecked(
            readme_settings_async("page_management", "custom_range_end")
        )
        self.custom_range_end_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_range_end",
                self.custom_range_end_combo_switch.isChecked(),
            )
        )

        # 抽取模式选择下拉框是否显示开关
        self.draw_custom_method_combo_switch = SwitchButton()
        self.draw_custom_method_combo_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "`draw_custom_method`", "disable"
            )
        )
        self.draw_custom_method_combo_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "draw_custom_method", "enable"
            )
        )
        self.draw_custom_method_combo_switch.setChecked(
            readme_settings_async("page_management", "draw_custom_method")
        )
        self.draw_custom_method_combo_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "draw_custom_method",
                self.draw_custom_method_combo_switch.isChecked(),
            )
        )

        # 数量标签是否显示开关
        self.custom_quantity_label_switch = SwitchButton()
        self.custom_quantity_label_switch.setOffText(
            get_content_switchbutton_name_async(
                "page_management", "custom_quantity_label", "disable"
            )
        )
        self.custom_quantity_label_switch.setOnText(
            get_content_switchbutton_name_async(
                "page_management", "custom_quantity_label", "enable"
            )
        )
        self.custom_quantity_label_switch.setChecked(
            readme_settings_async("page_management", "custom_quantity_label")
        )
        self.custom_quantity_label_switch.checkedChanged.connect(
            lambda: update_settings(
                "page_management",
                "custom_quantity_label",
                self.custom_quantity_label_switch.isChecked(),
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_window_multiple_swap_20_filled"),
            get_content_name_async("page_management", "custom_method"),
            get_content_description_async("page_management", "custom_method"),
            self.custom_method_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_reset_20_filled"),
            get_content_name_async("page_management", "reset_custom"),
            get_content_description_async("page_management", "reset_custom"),
            self.reset_custom_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_autofit_content_20_filled"),
            get_content_name_async("page_management", "custom_quantity_control"),
            get_content_description_async("page_management", "custom_quantity_control"),
            self.custom_quantity_control_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_play_20_filled"),
            get_content_name_async("page_management", "custom_start_button"),
            get_content_description_async("page_management", "custom_start_button"),
            self.custom_start_button_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_notepad_person_20_filled"),
            get_content_name_async("page_management", "custom_list"),
            get_content_description_async("page_management", "custom_list"),
            self.custom_list_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_person_20_filled"),
            get_content_name_async("page_management", "custom_range_start"),
            get_content_description_async("page_management", "custom_range_start"),
            self.custom_range_start_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_person_20_filled"),
            get_content_name_async("page_management", "custom_range_end"),
            get_content_description_async("page_management", "custom_range_end"),
            self.custom_range_end_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_person_20_filled"),
            get_content_name_async("page_management", "draw_custom_method"),
            get_content_description_async("page_management", "draw_custom_method"),
            self.draw_custom_method_combo_switch,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_slide_text_person_20_filled"),
            get_content_name_async("page_management", "custom_quantity_label"),
            get_content_description_async("page_management", "custom_quantity_label"),
            self.custom_quantity_label_switch,
        )
