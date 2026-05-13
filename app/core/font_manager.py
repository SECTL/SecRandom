import os
from typing import Optional

from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QFont, QFontDatabase
from PySide6.QtWidgets import QApplication, QWidget
from loguru import logger

from app.tools.settings_access import readme_settings_async
from app.tools.variable import FONT_APPLY_DELAY
from app.core.utils import safe_execute
from app.tools.path_utils import get_data_path


FONT_FAMILY_DEFAULT = "HarmonyOS Sans SC"
FONT_WEIGHT_MAP = {
    0: (QFont.Weight.Thin, "HarmonyOS_Sans_SC_Light.ttf"),
    1: (QFont.Weight.ExtraLight, "HarmonyOS_Sans_SC_Light.ttf"),
    2: (QFont.Weight.Light, "HarmonyOS_Sans_SC_Light.ttf"),
    3: (QFont.Weight.Normal, "HarmonyOS_Sans_SC_Medium.ttf"),
    4: (QFont.Weight.Medium, "HarmonyOS_Sans_SC_Medium.ttf"),
    5: (QFont.Weight.DemiBold, "HarmonyOS_Sans_SC_Medium.ttf"),
    6: (QFont.Weight.Bold, "HarmonyOS_Sans_SC_Bold.ttf"),
    7: (QFont.Weight.ExtraBold, "HarmonyOS_Sans_SC_Bold.ttf"),
    8: (QFont.Weight.Black, "HarmonyOS_Sans_SC_Bold.ttf"),
}


def get_font_weight_file(weight_value: int) -> str:
    """根据字体粗细数值获取对应的字体文件名

    Args:
        weight_value: 字体粗细数值 (0-8)

    Returns:
        str: 对应的字体文件名
    """
    return FONT_WEIGHT_MAP.get(
        weight_value, (QFont.Weight.Normal, "HarmonyOS_Sans_SC_Medium.ttf")
    )[1]


def get_font_weight_value(weight_value: int) -> QFont.Weight:
    """根据字体粗细数值获取对应的 QFont.Weight 枚举值

    Args:
        weight_value: 字体粗细数值 (0-8)

    Returns:
        QFont.Weight: 对应的字体粗细枚举值
    """
    return FONT_WEIGHT_MAP.get(
        weight_value, (QFont.Weight.Normal, "HarmonyOS_Sans_SC_Medium.ttf")
    )[0]


def load_font_by_weight(font_family: str, font_weight: int) -> str:
    """根据字体家族和粗细加载字体

    Args:
        font_family: 字体家族名称
        font_weight: 字体粗细数值 (0-8)

    Returns:
        str: 加载成功的字体家族名称
    """
    if font_family == FONT_FAMILY_DEFAULT:
        return _load_harmonyos_font(font_weight)

    return _apply_system_font(font_family, font_weight)


def _load_harmonyos_font(font_weight: int) -> str:
    """加载 HarmonyOS Sans SC 字体

    Args:
        font_weight: 字体粗细数值 (0-8)

    Returns:
        str: 加载成功的字体家族名称
    """
    font_file = get_font_weight_file(font_weight)
    logger.debug(f"根据粗细 {font_weight} 加载字体文件: {font_file}")
    font_path = get_data_path("font/HarmonyOS_Sans_SC", font_file)
    font_id = QFontDatabase.addApplicationFont(str(font_path))

    if font_id < 0:
        logger.warning(f"加载字体文件失败: {font_path}")
        return FONT_FAMILY_DEFAULT

    font_family = QFontDatabase.applicationFontFamilies(font_id)[0]
    logger.debug(f"已加载字体: {font_family} (粗细: {font_weight})")
    return font_family


def _apply_system_font(font_family: str, font_weight: int) -> str:
    """应用系统字体设置

    Args:
        font_family: 字体家族名称
        font_weight: 字体粗细数值 (0-8)

    Returns:
        str: 字体家族名称
    """
    app_font = QApplication.font()
    app_font.setFamily(font_family)
    app_font.setWeight(get_font_weight_value(font_weight))

    for widget in QApplication.allWidgets():
        if isinstance(widget, QWidget):
            current_font = widget.font()
            if (
                current_font.family() != font_family
                or current_font.weight() != app_font.weight()
            ):
                new_font = QFont(app_font)  # 创建新的字体对象，保留字重
                new_font.setBold(current_font.bold())
                new_font.setItalic(current_font.italic())
                widget.setFont(new_font)

    logger.debug(f"已应用字体粗细: {font_family} (粗细值: {font_weight})")
    return font_family


def ensure_application_font_point_size(default_point_size: int = 9) -> None:
    """确保应用程序字体具有有效的 pointSize，避免 pointSize 为 -1/0 的情况"""
    app_font = QApplication.font()
    try:
        point_size = int(app_font.pointSize())
    except Exception:
        point_size = -1

    if point_size > 0:
        return

    try:
        pixel_size = int(app_font.pixelSize())
    except Exception:
        pixel_size = 0

    resolved_point_size = int(default_point_size) if int(default_point_size) > 0 else 9
    if pixel_size > 0:
        try:
            screen = QApplication.primaryScreen()
            dpi = float(screen.logicalDotsPerInch()) if screen is not None else 96.0
            if dpi > 0:
                resolved_point_size = max(1, int(round(pixel_size * 72.0 / dpi)))
        except Exception:
            pass

    if resolved_point_size <= 0:
        resolved_point_size = 9

    try:
        app_font.setPointSize(int(resolved_point_size))
        QApplication.setFont(app_font)
        logger.debug(f"已修正应用字体 pointSize: {resolved_point_size}")
    except Exception as e:
        logger.warning(f"修正应用字体 pointSize 失败: {e}")


def configure_dpi_scale() -> None:
    """在创建QApplication之前配置DPI缩放模式"""
    try:
        dpi_scale = readme_settings_async("basic_settings", "dpiScale")
        if dpi_scale == "Auto":
            _set_auto_dpi()
        else:
            _set_manual_dpi(dpi_scale)
    except Exception as e:
        logger.warning(f"读取DPI设置失败，使用默认设置: {e}")
        _set_auto_dpi()


def _set_auto_dpi() -> None:
    """设置自动DPI缩放"""
    QApplication.setHighDpiScaleFactorRoundingPolicy(
        Qt.HighDpiScaleFactorRoundingPolicy.PassThrough
    )
    os.environ["QT_ENABLE_HIGHDPI_SCALING"] = "1"
    logger.debug("DPI缩放已设置为自动模式")


def _set_manual_dpi(scale: str) -> None:
    """设置手动DPI缩放

    Args:
        scale: 缩放倍数
    """
    os.environ["QT_ENABLE_HIGHDPI_SCALING"] = "0"
    os.environ["QT_SCALE_FACTOR"] = str(scale)
    logger.debug(f"DPI缩放已设置为{scale}倍")


def apply_font_settings() -> None:
    """应用字体设置 - 优化版本，使用字体管理器异步加载"""
    font_family = readme_settings_async("basic_settings", "font")
    font_weight_value = readme_settings_async("basic_settings", "font_weight")

    from qfluentwidgets import setFontFamilies

    font_weight_int = int(font_weight_value) if font_weight_value else 3
    actual_font_family = load_font_by_weight(font_family, font_weight_int)
    setFontFamilies([actual_font_family])
    app_font = QApplication.font()
    app_font.setFamily(actual_font_family)
    app_font.setWeight(get_font_weight_value(font_weight_int))
    QApplication.setFont(app_font)
    ensure_application_font_point_size()
    QTimer.singleShot(
        FONT_APPLY_DELAY,
        lambda: safe_execute(
            apply_font_to_application, actual_font_family, error_message="应用字体失败"
        ),
    )


def apply_font_to_application(font_family: str) -> None:
    """应用字体设置到整个应用程序，优化版本使用字体管理器

    Args:
        font_family: 字体家族名称
    """
    current_font = QApplication.font()
    app_font = QFont(current_font)  # 创建新的字体对象，保留原有属性
    app_font.setFamily(font_family)

    widgets_updated = 0
    widgets_skipped = 0

    for widget in QApplication.allWidgets():
        if isinstance(widget, QWidget):
            if update_widget_fonts(widget, app_font, font_family):
                widgets_updated += 1
            else:
                widgets_skipped += 1

    logger.debug(
        f"已应用字体: {font_family}, 更新了{widgets_updated}个控件字体, "
        f"跳过了{widgets_skipped}个已有相同字体的控件"
    )


def update_widget_fonts(widget: Optional[QWidget], font, font_family: str) -> bool:
    """更新控件及其子控件的字体，优化版本减少内存占用，特别处理ComboBox等控件

    Args:
        widget: 要更新字体的控件
        font: 要应用的字体
        font_family: 目标字体家族名称

    Returns:
        bool: 是否更新了控件的字体
    """
    if widget is None:
        return False

    if not hasattr(widget, "font") or not hasattr(widget, "setFont"):
        return False

    current_widget_font = widget.font()
    if current_widget_font.family() == font_family:
        return False

    new_font = QFont(font)  # 创建新的字体对象，保留字重等属性
    new_font.setBold(current_widget_font.bold())
    new_font.setItalic(current_widget_font.italic())
    widget.setFont(new_font)

    if isinstance(widget, QWidget):
        children = widget.children()
        for child in children:
            if isinstance(child, QWidget):
                update_widget_fonts(child, font, font_family)

    return True
