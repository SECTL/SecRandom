import json
from typing import Dict, Optional

from qfluentwidgets import *
from PySide6.QtGui import *
from PySide6.QtWidgets import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from loguru import logger

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.settings_access import *
from app.core.font_manager import get_font_weight_file, FONT_FAMILY_DEFAULT


FONT_CACHE_KEY = "_font_cache"
ICON_CACHE_KEY = "_icon_cache"
ICON_MAP_CACHE_KEY = "_icon_map_cache"


def load_custom_font() -> Optional[str]:
    """加载自定义字体，根据用户设置决定是否加载字体

    Returns:
        Optional[str]: 字体家族名称，如果加载失败则返回 None
    """
    if hasattr(load_custom_font, FONT_CACHE_KEY):
        cache = getattr(load_custom_font, FONT_CACHE_KEY)
        if cache:
            return cache

    try:
        custom_settings_path = get_settings_path()
        font_family_setting = _get_font_family_setting(custom_settings_path)

        if font_family_setting:
            font_family = _load_font_by_setting(font_family_setting)
            if font_family:
                setattr(load_custom_font, FONT_CACHE_KEY, font_family)
                return font_family

        font_family = _load_default_font()
        setattr(load_custom_font, FONT_CACHE_KEY, font_family)
        return font_family
    except Exception as e:
        logger.exception(f"读取自定义设置失败，使用默认字体: {e}")
        font_family = _load_default_font()
        setattr(load_custom_font, FONT_CACHE_KEY, font_family)
        return font_family


def _get_font_family_setting(settings_path) -> str:
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
        logger.exception(f"读取字体设置失败: {e}")
        return ""


def _load_font_by_setting(font_family_setting: str) -> Optional[str]:
    """根据字体设置加载对应的字体

    Args:
        font_family_setting: 字体家族设置

    Returns:
        Optional[str]: 加载成功的字体家族名称，失败则返回 None
    """
    font_weight_value = readme_settings_async("basic_settings", "font_weight")
    font_weight_int = int(font_weight_value) if font_weight_value else 3

    if font_family_setting == FONT_FAMILY_DEFAULT:
        font_file = get_font_weight_file(font_weight_int)
        font_path = get_data_path("font/HarmonyOS_Sans_SC", font_file)
        font_id = QFontDatabase.addApplicationFont(str(font_path))

        if font_id < 0:
            logger.warning(f"加载自定义字体失败: {font_path}")
            return None

        font_family = QFontDatabase.applicationFontFamilies(font_id)[0]
        return font_family
    else:
        return font_family_setting


def _load_default_font() -> Optional[str]:
    """加载默认字体

    Returns:
        Optional[str]: 加载成功的字体家族名称
    """
    font_weight_value = readme_settings_async("basic_settings", "font_weight")
    font_weight_int = int(font_weight_value) if font_weight_value else 3

    font_file = get_font_weight_file(font_weight_int)
    font_path = get_data_path("font/HarmonyOS_Sans_SC", font_file)
    font_id = QFontDatabase.addApplicationFont(str(font_path))

    if font_id < 0:
        logger.warning(f"加载默认字体失败: {font_path}")
        return None

    font_family = QFontDatabase.applicationFontFamilies(font_id)[0]
    return font_family


_icon_cache: Dict[str, QIcon] = {}
_icon_map_cache: Optional[Dict[str, int]] = None


class FluentSystemIcons(FluentFontIconBase):
    """Fluent System Icons 字体图标类"""

    def __init__(self, char: str):
        """初始化字体图标

        Args:
            char: 图标字符
        """
        super().__init__(char)

    def path(self, theme=Theme.AUTO) -> str:
        """返回字体文件路径"""
        return str(get_data_path("assets", "FluentSystemIcons-Filled.ttf"))

    def iconNameMapPath(self) -> str:
        """返回图标名称到图标码点的映射表文件路径"""
        return str(get_data_path("assets", "FluentSystemIcons-Filled.json"))


def _get_icon_map() -> Dict[str, int]:
    """获取图标映射表，使用缓存避免重复读取JSON

    Returns:
        Dict[str, int]: 图标名称到码点的映射表
    """
    global _icon_map_cache
    if _icon_map_cache is None:
        try:
            map_path = get_data_path("assets", "FluentSystemIcons-Filled.json")
            with open(map_path, "r", encoding="utf-8") as f:
                content = f.read()
                if not content or not content.strip():
                    logger.warning(f"图标映射表文件为空: {map_path}")
                    _icon_map_cache = {}
                else:
                    _icon_map_cache = json.loads(content)
        except Exception as e:
            logger.exception(f"加载图标映射表失败: {e}")
            _icon_map_cache = {}
    return _icon_map_cache


def get_theme_icon(icon_name) -> QIcon:
    """获取主题相关的图标，带缓存机制

    Args:
        icon_name: 图标名称或码点

    Returns:
        QIcon: 图标对象
    """
    global _icon_cache

    if icon_name in _icon_cache:
        return _icon_cache[icon_name]

    try:
        icon = _create_icon_from_name(icon_name)
        if icon:
            _icon_cache[icon_name] = icon
        return icon
    except Exception as e:
        logger.exception(f"加载图标{icon_name}出错: {str(e)}")
        return _get_default_icon(icon_name)


def _create_icon_from_name(icon_name) -> Optional[QIcon]:
    """根据图标名称或码点创建图标

    Args:
        icon_name: 图标名称或码点

    Returns:
        Optional[QIcon]: 创建的图标对象，失败则返回 None
    """
    if isinstance(icon_name, str) and not icon_name.startswith("\\u"):
        icon_map = _get_icon_map()
        if icon_name in icon_map:
            code_point = icon_map[icon_name]
            char = chr(code_point)
            return FluentSystemIcons(char)
        else:
            raise ValueError(f"图标名称 '{icon_name}' 未在图标映射表中找到")
    else:
        char = _convert_icon_name_to_char(icon_name)
        return FluentSystemIcons(char)


def _get_default_icon(icon_name) -> QIcon:
    """获取默认图标

    Args:
        icon_name: 原始图标名称（用于缓存）

    Returns:
        QIcon: 默认图标对象
    """
    try:
        default_char = chr(DEFAULT_ICON_CODEPOINT)
        default_icon = FluentSystemIcons(default_char)
        _icon_cache[icon_name] = default_icon
        return default_icon
    except Exception as default_error:
        logger.exception(f"加载默认图标也失败: {str(default_error)}")
        return QIcon()


def _convert_icon_name_to_char(icon_name) -> str:
    """将图标名称或码点转换为字符

    Args:
        icon_name: 图标名称或码点

    Returns:
        str: 图标字符
    """
    if isinstance(icon_name, str) and icon_name.startswith("\\u"):
        code_point = int(icon_name[2:], 16)
        return chr(code_point)
    elif isinstance(icon_name, int):
        return chr(icon_name)
    else:
        return icon_name


def is_dark_theme(qconfig) -> bool:
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


def setThemeColor(color) -> None:
    """设置主题色

    Args:
        color: 主题色，可以是 QColor、Qt.GlobalColor 或字符串（十六进制颜色或颜色名称）
    """
    hex_color = _convert_color_to_hex(color)
    if hex_color is None:
        return

    themeColor.value = hex_color
    update_settings("basic_settings", "theme_color", hex_color)


def themeColor() -> str:
    """获取当前主题色

    Returns:
        str: 十六进制格式的主题色
    """
    try:
        return themeColor.value
    except Exception as e:
        logger.exception(f"获取主题色失败: {e}")
        return FALLBACK_THEME_COLOR


def _convert_color_to_hex(color) -> Optional[str]:
    """将各种格式的颜色转换为十六进制格式

    Args:
        color: 颜色对象，可以是 QColor、Qt.GlobalColor 或字符串

    Returns:
        Optional[str]: 十六进制格式的颜色，转换失败则返回 None
    """
    if isinstance(color, QColor):
        return color.name()
    elif isinstance(color, Qt.GlobalColor):
        return QColor(color).name()
    elif isinstance(color, str):
        if color.startswith("#"):
            return color
        else:
            qcolor = QColor(color)
            if qcolor.isValid():
                return qcolor.name()
            else:
                logger.exception(f"无效的颜色名称: {color}")
                return None
    else:
        logger.exception(f"不支持的颜色类型: {type(color)}")
        return None
