# ==================================================
# 导入模块
# ==================================================
from qfluentwidgets import *
from PySide6.QtGui import *
from PySide6.QtWidgets import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *

import json
from loguru import logger

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.settings_access import *


# ==================================================
# 字体设置相关函数
# ==================================================
def load_custom_font():
    """加载自定义字体，根据用户设置决定是否加载字体

    Returns:
        str: 字体家族名称，如果加载失败则返回 None
    """
    if hasattr(load_custom_font, "_font_cache") and load_custom_font._font_cache:
        return load_custom_font._font_cache

    try:
        custom_settings_path = get_settings_path()
        font_family_setting = _get_font_family_setting(custom_settings_path)

        # 根据字体设置加载相应字体
        if font_family_setting:
            font_family = _load_font_by_setting(font_family_setting)
            if font_family:
                load_custom_font._font_cache = font_family
                return font_family

        font_family = _load_default_font()
        load_custom_font._font_cache = font_family
        return font_family
    except Exception as e:
        logger.error(f"读取自定义设置失败，使用默认字体: {e}")
        font_family = _load_default_font()
        load_custom_font._font_cache = font_family
        return font_family


def _get_font_family_setting(settings_path):
    """从设置文件中获取字体家族设置

    Args:
        settings_path: 设置文件路径

    Returns:
        str: 字体家族设置，如果文件不存在或读取失败则返回空字符串
    """
    if not settings_path.is_file():
        return ""

    try:
        with open_file(settings_path, "r", encoding="utf-8") as f:
            settings = json.load(f)
            personal_settings = settings.get("personal", {})
            return personal_settings.get("font_family", "")
    except Exception as e:
        logger.error(f"读取字体设置失败: {e}")
        return ""


def _load_font_by_setting(font_family_setting):
    """根据字体设置加载对应的字体

    Args:
        font_family_setting: 字体家族设置

    Returns:
        str: 加载成功的字体家族名称，失败则返回 None
    """
    font_map = {
        "汉仪文黑-85W": "汉仪文黑-85W.ttf",
        "HarmonyOS Sans SC": "HarmonyOS_Sans_SC_Bold.ttf",
    }

    if font_family_setting in font_map:
        font_file = font_map[font_family_setting]
        font_path = get_resources_path("font", font_file)
        font_id = QFontDatabase.addApplicationFont(str(font_path))

        if font_id < 0:
            logger.error(f"加载自定义字体失败: {font_path}")
            return None

        font_family = QFontDatabase.applicationFontFamilies(font_id)[0]
        return font_family
    else:
        # 使用系统默认字体
        return font_family_setting


def _load_default_font():
    """加载默认字体

    Returns:
        str: 加载成功的字体家族名称
    """
    font_path = get_resources_path("font", "HarmonyOS_Sans_SC_Bold.ttf")
    font_id = QFontDatabase.addApplicationFont(str(font_path))

    if font_id < 0:
        logger.error(f"加载默认字体失败: {font_path}")
        return None

    font_family = QFontDatabase.applicationFontFamilies(font_id)[0]
    return font_family


# ==================================================
# 图标相关类和函数
# ==================================================
class FluentSystemIcons(FluentFontIconBase):
    """Fluent System Icons 字体图标类"""

    def __init__(self, char):
        """初始化字体图标

        Args:
            char: 图标字符
        """
        super().__init__(char)

    def path(self, theme=Theme.AUTO):
        """返回字体文件路径"""
        return str(get_resources_path("assets", "FluentSystemIcons-Filled.ttf"))

    def iconNameMapPath(self):
        """返回图标名称到图标码点的映射表文件路径"""
        return str(get_resources_path("assets", "FluentSystemIcons-Filled.json"))


def get_theme_icon(icon_name):
    """获取主题相关的图标

    Args:
        icon_name: 图标名称或码点

    Returns:
        QIcon: 图标对象
    """
    try:
        # 尝试使用名称获取图标
        if isinstance(icon_name, str) and not icon_name.startswith("\\u"):
            # 尝试从JSON文件中直接获取码点
            try:
                map_path = get_resources_path("assets", "FluentSystemIcons-Filled.json")
                with open(map_path, "r", encoding="utf-8") as f:
                    icon_map = json.load(f)

                if icon_name in icon_map:
                    # 获取图标码点并转换为字符串
                    code_point = icon_map[icon_name]
                    char = chr(code_point)
                    icon = FluentSystemIcons(char)
                    return icon
                else:
                    raise ValueError(f"图标名称 '{icon_name}' 未在图标映射表中找到")
            except Exception as json_error:
                logger.error(f"从JSON加载图标'{icon_name}'也失败: {str(json_error)}")
                raise
        else:
            # 处理码点输入
            char = _convert_icon_name_to_char(icon_name)
            icon = FluentSystemIcons(char)
            return icon
    except Exception as e:
        logger.error(f"加载图标{icon_name}出错: {str(e)}")
        # 返回默认图标
        try:
            # 尝试使用码点创建默认图标
            default_char = chr(DEFAULT_ICON_CODEPOINT)  # 使用info图标的码点
            return FluentSystemIcons(default_char)
        except Exception as default_error:
            logger.error(f"加载默认图标也失败: {str(default_error)}")
            # 返回空的QIcon作为最后备选
            return QIcon()


def _convert_icon_name_to_char(icon_name):
    """将图标名称或码点转换为字符

    Args:
        icon_name: 图标名称或码点

    Returns:
        str: 图标字符
    """
    if isinstance(icon_name, str) and icon_name.startswith("\\u"):
        # 将Unicode字符串转换为字符
        code_point = int(icon_name[2:], 16)
        return chr(code_point)
    elif isinstance(icon_name, int):
        # 将整数码点转换为字符
        return chr(icon_name)
    else:
        # 直接使用字符
        return icon_name


# ==================================================
# 主题相关函数
# ==================================================
def is_dark_theme(qconfig):
    """判断当前是否为深色主题

    Args:
        qconfig: 配置对象

    Returns:
        bool: 是否为深色主题
    """
    if qconfig.theme == Theme.AUTO:
        lightness = QApplication.palette().color(QPalette.Window).lightness()
        return lightness <= 127
    else:
        return qconfig.theme == Theme.DARK


def setThemeColor(color):
    """设置主题色

    Args:
        color: 主题色，可以是 QColor、Qt.GlobalColor 或字符串（十六进制颜色或颜色名称）
    """
    hex_color = _convert_color_to_hex(color)
    if hex_color is None:
        return

    # 设置主题色
    themeColor.value = hex_color
    # 保存配置
    update_settings("basic_settings", "theme_color", hex_color)


def themeColor():
    """获取当前主题色

    Returns:
        str: 十六进制格式的主题色
    """
    try:
        # 获取当前主题色
        return themeColor.value
    except Exception as e:
        logger.error(f"获取主题色失败: {e}")
        # 返回默认主题色
        return FALLBACK_THEME_COLOR


def _convert_color_to_hex(color):
    """将各种格式的颜色转换为十六进制格式

    Args:
        color: 颜色对象，可以是 QColor、Qt.GlobalColor 或字符串

    Returns:
        str: 十六进制格式的颜色，转换失败则返回 None
    """
    if isinstance(color, QColor):
        return color.name()
    elif isinstance(color, Qt.GlobalColor):
        return QColor(color).name()
    elif isinstance(color, str):
        # 检查是否是十六进制颜色
        if color.startswith("#"):
            return color
        else:
            # 尝试解析颜色名称
            qcolor = QColor(color)
            if qcolor.isValid():
                return qcolor.name()
            else:
                logger.error(f"无效的颜色名称: {color}")
                return None
    else:
        logger.error(f"不支持的颜色类型: {type(color)}")
        return None
