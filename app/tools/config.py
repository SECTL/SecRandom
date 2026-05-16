import os
import json
import shutil
import zipfile
import re
from typing import Optional, Union, Callable
from loguru import logger
from pathlib import Path
from datetime import datetime
import platform
import sys
import time
import psutil
import requests

from PySide6.QtWidgets import QWidget, QFileDialog
from PySide6.QtCore import Qt
from qfluentwidgets import InfoBar, InfoBarPosition, FluentIcon, InfoBarIcon, MessageBox

if sys.platform.startswith("linux"):
    try:
        import pulsectl
    except ImportError:
        pulsectl = None

from app.tools.path_utils import (
    get_app_root,
    get_audio_path,
    get_data_path,
    get_settings_path,
    get_path,
    atomic_write_json,
)
from app.tools.personalised import get_theme_icon
from app.tools.settings_access import readme_settings_async
from app.common.data.list import get_student_list, get_group_list
from app.tools.variable import (
    SPECIAL_VERSION,
    LOG_DIR,
    LOG_FILENAME_FORMAT,
    LOG_ROTATION_SIZE,
    LOG_RETENTION_DAYS,
    APPLY_NAME,
)
from app.Language.obtain_language import (
    get_content_pushbutton_name_async,
    get_any_position_value_async,
)


# ==================== 日志配置模块 ====================


def configure_logging():
    """配置日志系统"""
    log_dir = get_path(LOG_DIR)
    log_dir.mkdir(parents=True, exist_ok=True)

    log_level = "DEBUG"

    logger.add(
        log_dir / LOG_FILENAME_FORMAT,
        rotation=LOG_ROTATION_SIZE,
        retention=LOG_RETENTION_DAYS,
        compression=None,
        backtrace=True,
        diagnose=True,
        level=log_level,
    )

    if sys.stdout is not None:
        logger.add(
            sys.stdout,
            format="<green>{time:YYYY-MM-DD HH:mm:ss}</green> | <level>{level: <8}</level> | <cyan>{name}</cyan>:<cyan>{function}</cyan>:<cyan>{line}</cyan> - <level>{message}</level>",
            level=log_level,
            colorize=True,
        )

    logger.debug(f"日志系统已配置，当前日志等级: {log_level}")


def create_sentry_before_send_filter():
    """创建 Sentry 事件过滤器

    过滤掉不需要上报的错误，如第三方库错误和常见的无害错误
    """

    _NETWORK_ERROR_TYPES = {
        "ClientConnectorError",
        "ClientConnectorDNSError",
        "ClientConnectorCertificateError",
        "ClientConnectionResetError",
        "ClientOSError",
        "ConnectionTimeoutError",
        "SocketTimeoutError",
    }

    def _is_benign_oserror(module: str, value: str) -> bool:
        message = str(value)
        if "Bad file descriptor" in message:
            return True
        if "WinError 10022" in message or "无效的参数" in message:
            return True
        return module.startswith(("socket", "asyncio", "selectors"))

    def before_send(event, hint):
        # 1. 检查是否有堆栈信息
        has_stacktrace = False
        if "exception" in event:
            values = event.get("exception", {}).get("values", [])
            for val in values:
                if val.get("stacktrace"):
                    has_stacktrace = True
                    break

        if not has_stacktrace and "threads" in event:
            values = event.get("threads", {}).get("values", [])
            for val in values:
                if val.get("stacktrace"):
                    has_stacktrace = True
                    break

        # 检查 loguru 的 log_record
        log_record = hint.get("log_record")
        if log_record:
            if getattr(log_record, "exception", None):
                has_stacktrace = True
            elif hasattr(log_record, "extra") and log_record.extra.get("exc_info"):
                has_stacktrace = True

        # 如果没有堆栈信息，且是错误/严重级别，则丢弃 (logger.info 等低级别不会被丢弃，除非 event_level 设置)
        if not has_stacktrace and event.get("level") in ("error", "fatal"):
            return None

        # 2. 过滤特定的错误类型或模块
        if "exception" in event:
            exceptions = event.get("exception", {}).get("values", [])
            for exc in exceptions:
                module = exc.get("module", "")
                type_ = exc.get("type", "")
                value = exc.get("value", "")

                # 过滤 Qt 常见无害错误 (通常是由于对象在 C++ 侧已销毁但 Python 侧仍在尝试访问)
                if type_ == "RuntimeError" and (
                    "Internal C++ object" in str(value)
                    or "has been deleted" in str(value)
                ):
                    return None

                # 过滤 COM 相关
                if type_ == "COMError" and "没有注册类" in str(value):
                    return None

                if type_ == "KeyboardInterrupt":
                    return None

                if type_ == "ModuleNotFoundError" and "PySide6.QtMultimedia" in str(
                    value
                ):
                    return None

                if type_ == "OSError" and _is_benign_oserror(module, value):
                    return None

                if type_ == "ValueError" and "generator already executing" in str(
                    value
                ):
                    return None

                if type_ == "error" and "GetCursorPos" in str(value):
                    return None

                # 过滤网络连接相关错误（预期的网络故障，非产品缺陷）
                if type_ in _NETWORK_ERROR_TYPES:
                    return None

                # 过滤 aiohttp 超时和限流错误
                if type_ == "TimeoutError" and module in (
                    "aiohttp.helpers",
                    "aiohttp.streams",
                ):
                    return None
                if type_ == "ClientResponseError" and "429" in str(value):
                    return None

        return event

    return before_send


posthog_client = None


def set_posthog_client(client):
    """设置全局 PostHog 客户端"""
    global posthog_client
    posthog_client = client


def track_event(event_name: str):
    """上报事件到 PostHog

    Args:
        event_name: 事件名称
    """
    if posthog_client is None:
        return
    from app.tools.settings_access import get_or_create_user_id

    user_id = get_or_create_user_id()
    posthog_client.capture(
        distinct_id=user_id,
        event=event_name,
    )


_GEOIP_COUNTRY_NAME_KEY = "$geoip_country_name"
_GEOIP_SUBDIVISION_1_NAME_KEY = "$geoip_subdivision_1_name"
_GEOIP_CITY_NAME_KEY = "$geoip_city_name"
_GEOIP_IP_KEY = "$geoip_ip"
_GEOIP_ISP_KEY = "$geoip_isp"


def get_geoip_properties_zh_cn(timeout_seconds: float = 1.5) -> dict:
    headers = {"User-Agent": f"SecRandom/{SPECIAL_VERSION}"}
    timeout_seconds = max(0.2, float(timeout_seconds or 0))

    try:
        response = requests.get(
            "https://ip9.com.cn/get",
            headers=headers,
            timeout=timeout_seconds,
        )
        response.raise_for_status()
        data = response.json()
    except Exception:
        return {}

    if not isinstance(data, dict) or data.get("ret") != 200:
        return {}

    geo_data = data.get("data")
    if not isinstance(geo_data, dict):
        return {}

    properties = {}

    country = (geo_data.get("country") or "").strip()
    if country:
        properties[_GEOIP_COUNTRY_NAME_KEY] = country

    prov = (geo_data.get("prov") or "").strip()
    if prov:
        properties[_GEOIP_SUBDIVISION_1_NAME_KEY] = prov

    city = (geo_data.get("city") or "").strip()
    if city:
        properties[_GEOIP_CITY_NAME_KEY] = city

    ip = (geo_data.get("ip") or "").strip()
    if ip:
        properties[_GEOIP_IP_KEY] = ip

    isp = (geo_data.get("isp") or "").strip()
    if isp:
        properties[_GEOIP_ISP_KEY] = isp

    if properties:
        logger.debug(f"获取到地理位置信息: {properties}")

    return properties


# ==================== 通知模块 ====================


class NotificationType:
    """预定义的通知类型"""

    SUCCESS = "success"
    WARNING = "warning"
    ERROR = "error"
    INFO = "info"
    CUSTOM = "custom"


class NotificationConfig:
    """通知配置类，用于定义通知的各种参数"""

    def __init__(
        self,
        title: str = "",
        content: str = "",
        icon: Union[FluentIcon, InfoBarIcon, str] = None,
        duration: int = 3000,
        position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
        is_closable: bool = True,
        orient: Qt.Orientation = Qt.Orientation.Horizontal,
        background_color: Optional[str] = None,
        text_color: Optional[str] = None,
    ):
        self.title = title
        self.content = content
        self.icon = icon
        self.duration = duration
        self.position = position
        self.is_closable = is_closable
        self.orient = orient
        self.background_color = background_color
        self.text_color = text_color


def show_success_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = 3000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """显示成功通知"""
    return InfoBar.success(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_warning_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = -1,
    position: Union[InfoBarPosition, str] = InfoBarPosition.BOTTOM,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """显示警告通知"""
    return InfoBar.warning(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_error_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = 5000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.BOTTOM_RIGHT,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Vertical,
) -> InfoBar:
    """显示错误通知"""
    return InfoBar.error(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_info_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = -1,
    position: Union[InfoBarPosition, str] = InfoBarPosition.BOTTOM_LEFT,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """显示信息通知"""
    return InfoBar.info(
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )


def show_custom_notification(
    title: str,
    content: str,
    icon: Union[FluentIcon, InfoBarIcon, str] = InfoBarIcon.INFORMATION,
    parent: Optional[QWidget] = None,
    duration: int = 3000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
    background_color: Optional[str] = None,
    text_color: Optional[str] = None,
) -> InfoBar:
    """显示自定义通知"""
    info_bar = InfoBar.new(
        icon=icon,
        title=title,
        content=content,
        orient=orient,
        isClosable=is_closable,
        position=position,
        duration=duration,
        parent=parent,
    )

    if background_color and text_color:
        info_bar.setCustomBackgroundColor(background_color, text_color)

    return info_bar


def show_notification(
    notification_type: str, config: NotificationConfig, parent: Optional[QWidget] = None
) -> InfoBar:
    """显示通知

    Args:
        notification_type: 通知类型，值为NotificationType中定义的常量
        config: 通知配置对象
        parent: 父窗口组件

    Returns:
        InfoBar实例
    """
    if parent is not None and not isinstance(parent, QWidget):
        parent = None
    type_handlers = {
        NotificationType.SUCCESS: lambda: InfoBar.success(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        ),
        NotificationType.WARNING: lambda: InfoBar.warning(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        ),
        NotificationType.ERROR: lambda: InfoBar.error(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        ),
        NotificationType.INFO: lambda: InfoBar.info(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        ),
        NotificationType.CUSTOM: lambda: _create_custom_notification(config, parent),
    }

    handler = type_handlers.get(notification_type)
    if handler:
        return handler()

    raise ValueError(f"不支持的通知类型: {notification_type}")


def _create_custom_notification(
    config: NotificationConfig, parent: Optional[QWidget]
) -> InfoBar:
    """创建自定义通知"""
    info_bar = InfoBar.new(
        icon=config.icon or InfoBarIcon.INFORMATION,
        title=config.title,
        content=config.content,
        orient=config.orient,
        isClosable=config.is_closable,
        position=config.position,
        duration=config.duration,
        parent=parent,
    )

    if config.background_color and config.text_color:
        info_bar.setCustomBackgroundColor(config.background_color, config.text_color)

    return info_bar


def send_system_notification(title: str, content: str, url: str = None) -> bool:
    """发送系统通知

    Args:
        title: 通知标题
        content: 通知内容
        url: 点击通知后跳转的URL

    Returns:
        bool: 通知发送是否成功
    """
    try:
        icon_path = str(get_data_path("assets", "icon/secrandom-icon-paper.ico"))

        def on_notification_click():
            """点击通知时执行的函数"""
            try:
                if url:
                    import webbrowser

                    webbrowser.open(url)
                    logger.debug(f"已打开通知链接: {url}")
                else:
                    logger.warning("通知未配置URL，无法打开链接")
            except Exception as e:
                logger.exception(f"打开通知链接失败: {e}")

        if sys.platform == "win32":
            return _send_windows_notification(
                title, content, icon_path, on_notification_click
            )
        elif sys.platform.startswith("linux"):
            return _send_linux_notification(title, content, icon_path, url)
        else:
            logger.warning(f"当前平台不支持系统通知: {sys.platform}")
            return False
    except Exception as e:
        logger.exception(f"发送系统通知时发生意外错误: {e}")
        return False


def _send_windows_notification(
    title: str, content: str, icon_path: str, callback
) -> bool:
    """发送Windows平台通知"""
    try:
        from win10toast import ToastNotifier

        toaster = ToastNotifier()
        toaster.show_toast(
            title,
            content,
            icon_path=icon_path,
            duration=0,
            threaded=True,
            callback_on_click=callback,
        )
        logger.debug(f"已发送Windows通知: {title}")
        return True
    except ImportError:
        try:
            from plyer import notification

            notification.notify(
                title=title,
                message=content,
                app_name=APPLY_NAME,
                app_icon=icon_path,
                timeout=0,
            )
            logger.debug(f"已发送Windows通知(使用plyer): {title}")
            return True
        except Exception as e:
            logger.warning(f"发送Windows通知失败: {e}")
            return False


def _send_linux_notification(
    title: str, content: str, icon_path: str, url: str
) -> bool:
    """发送Linux平台通知"""
    try:
        import subprocess

        if url:
            subprocess.run(
                [
                    "notify-send",
                    "--icon",
                    icon_path,
                    "--action",
                    f"default={url}",
                    title,
                    content,
                ],
                check=True,
                timeout=0,
            )
            logger.debug(f"已发送Linux通知(包含URL): {title}")
        else:
            subprocess.run(
                ["notify-send", "--icon", icon_path, title, content],
                check=True,
                timeout=0,
            )
            logger.debug(f"已发送Linux通知(不包含URL): {title}")
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        try:
            from plyer import notification

            notification.notify(
                title=title,
                message=content,
                app_name=APPLY_NAME,
                app_icon=icon_path,
                timeout=0,
            )
            logger.debug(f"已发送Linux通知(使用plyer): {title}")
            return True
        except Exception as e:
            logger.warning(f"发送Linux通知失败: {e}")
            return False


# ==================== 系统功能模块 ====================


def restore_volume(volume_value: int) -> None:
    """跨平台音量控制

    Args:
        volume_value: 音量值 (0-100)
    """
    if sys.platform == "win32":
        _restore_windows_volume(volume_value)
    elif sys.platform.startswith("linux"):
        _restore_linux_volume(volume_value)
    else:
        logger.warning(f"不支持的平台: {sys.platform}，音量控制功能不可用")


def _restore_windows_volume(volume_value: int) -> None:
    """Windows平台音量控制"""
    try:
        from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume
        import comtypes
        from comtypes import POINTER, CLSCTX_ALL

        comtypes.CoInitialize()

        try:
            devices = AudioUtilities.GetSpeakers()
            if hasattr(devices, "Activate"):
                interface = devices.Activate(
                    IAudioEndpointVolume._iid_, CLSCTX_ALL, None
                )
                volume = comtypes.cast(interface, POINTER(IAudioEndpointVolume))
                volume.SetMute(0, None)
                volume.SetMasterVolumeLevelScalar(volume_value / 100.0, None)
                actual_volume = volume.GetMasterVolumeLevelScalar() * 100
                logger.info(
                    f"Windows音量设置为: {volume_value}%，实际设置值: {actual_volume:.1f}%"
                )
            else:
                device_enumerator = AudioUtilities.GetDeviceEnumerator()
                if hasattr(device_enumerator, "GetDefaultAudioEndpoint"):
                    speakers = device_enumerator.GetDefaultAudioEndpoint(0, 1)
                    if hasattr(speakers, "Activate"):
                        volume_interface = speakers.Activate(
                            IAudioEndpointVolume._iid_, CLSCTX_ALL, None
                        )
                        volume = comtypes.cast(
                            volume_interface, POINTER(IAudioEndpointVolume)
                        )
                        volume.SetMute(0, None)
                        volume.SetMasterVolumeLevelScalar(volume_value / 100.0, None)
                        actual_volume = volume.GetMasterVolumeLevelScalar() * 100
                        logger.info(
                            f"Windows音量设置为: {volume_value}%，实际设置值: {actual_volume:.1f}%"
                        )
        finally:
            comtypes.CoUninitialize()
    except Exception as e:
        logger.warning(f"Windows音量控制失败: {e}")


def _restore_linux_volume(volume_value: int) -> None:
    """Linux平台音量控制"""
    try:
        if pulsectl is None:
            logger.warning("pulsectl未安装，无法控制音量")
            return

        with pulsectl.Pulse("secrandom-volume-control") as pulse:
            sinks = pulse.sink_list()
            if not sinks:
                logger.warning("未找到音频输出设备")
                return

            default_sink = None
            for sink in sinks:
                if sink.name == pulse.server_info().default_sink_name:
                    default_sink = sink
                    break

            if default_sink is None:
                default_sink = sinks[0]

            pulse.sink_mute(default_sink.index, 0)
            pulse.volume_set_all_chans(default_sink, volume_value / 100.0)
            logger.info(f"Linux音量设置为: {volume_value}%")
    except Exception as e:
        logger.warning(f"Linux音量控制失败: {e}")


def set_autostart(enabled: bool) -> bool:
    """设置开机自启动

    Args:
        enabled: 是否启用自启动

    Returns:
        bool: 设置是否成功
    """
    try:
        if sys.platform == "win32":
            return _set_windows_autostart(enabled)
        elif sys.platform.startswith("linux"):
            return _set_linux_autostart(enabled)
        else:
            return False
    except Exception as e:
        logger.exception(f"设置开机自启动失败: {e}")
        return False


def _set_windows_autostart(enabled: bool) -> bool:
    """设置Windows开机自启动"""
    try:
        import winreg
    except Exception as e:
        logger.warning(f"无法加载 winreg: {e}")
        return False

    key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
    try:
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE
        )
    except FileNotFoundError:
        key = winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path)

    name = "SecRandom"
    if enabled:
        if getattr(sys, "frozen", False):
            cmd = f'"{sys.executable}"'
        else:
            root = Path(__file__).resolve().parents[2]
            main_py = root / "main.py"
            cmd = f'"{sys.executable}" "{str(main_py)}"'
        winreg.SetValueEx(key, name, 0, winreg.REG_SZ, cmd)
    else:
        try:
            winreg.DeleteValue(key, name)
        except FileNotFoundError:
            pass

    winreg.CloseKey(key)
    return True


def _set_linux_autostart(enabled: bool) -> bool:
    """设置Linux开机自启动"""
    autostart_dir = Path.home() / ".config" / "autostart"
    autostart_dir.mkdir(parents=True, exist_ok=True)
    desktop = autostart_dir / "secrandom.desktop"

    if enabled:
        if getattr(sys, "frozen", False):
            exec_cmd = f'"{sys.executable}"'
        else:
            root = Path(__file__).resolve().parents[2]
            main_py = root / "main.py"
            exec_cmd = f'{sys.executable} "{str(main_py)}"'
        content = f"[Desktop Entry]\nType=Application\nName=SecRandom\nExec={exec_cmd}\nX-GNOME-Autostart-enabled=true\n"
        desktop.write_text(content, encoding="utf-8")
    else:
        if desktop.exists():
            desktop.unlink()

    return True


# ==================== 平台信息模块 ====================


def _get_operating_system() -> str:
    """获取操作系统信息，特别优化Win11识别"""
    if sys.platform == "win32":
        try:
            version = sys.getwindowsversion()
            release = platform.release()

            # Windows 11 Build number starts from 22000
            if version.build >= 22000:
                return f"Windows 11 (Build {version.build})"

            return f"Windows {release} (Build {platform.version()})"
        except Exception:
            return f"Windows ({platform.system()})"
    else:
        return f"{platform.system()} {platform.release()}"


def _get_platform_release() -> str:
    """获取平台发行版本"""
    return platform.release()


def _get_platform_version() -> str:
    """获取平台详细版本"""
    return platform.version()


# ==================== 设置导入导出模块 ====================


def export_settings(parent: Optional[QWidget] = None) -> None:
    """导出设置到文件"""
    try:
        settings_path = get_settings_path()

        file_path, _ = QFileDialog.getSaveFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "export_settings"),
            "settings.json",
            "JSON Files (*.json);;All Files (*)",
        )

        if file_path:
            Path(file_path).write_text(
                Path(settings_path).read_text(encoding="utf-8"), encoding="utf-8"
            )

            dialog = MessageBox(
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "export_success_title",
                    "name",
                ),
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "export_success_content",
                    "name",
                ).format(path=file_path),
                parent,
            )
            dialog.yesButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "export_success_button",
                    "name",
                )
            )
            dialog.cancelButton.hide()
            dialog.buttonLayout.insertStretch(1)
            dialog.exec()

    except Exception as e:
        logger.exception(f"导出设置失败: {e}")
        dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings",
                "settings_import_export",
                "export_failure_title",
                "name",
            ),
            get_any_position_value_async(
                "basic_settings",
                "settings_import_export",
                "export_failure_content",
                "name",
            ).format(error=str(e)),
            parent,
        )
        dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings",
                "settings_import_export",
                "export_success_button",
                "name",
            )
        )
        dialog.cancelButton.hide()
        dialog.buttonLayout.insertStretch(1)
        dialog.exec()


def import_settings(parent: Optional[QWidget] = None) -> None:
    """从文件导入设置"""
    try:
        file_path, _ = QFileDialog.getOpenFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "import_settings"),
            "",
            "JSON Files (*.json);;All Files (*)",
        )

        if file_path:
            with open(file_path, "r", encoding="utf-8") as f:
                imported_settings = json.load(f)

            dialog = MessageBox(
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "import_confirm_title",
                    "name",
                ),
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "import_confirm_content",
                    "name",
                ),
                parent,
            )
            dialog.yesButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "import_confirm_button",
                    "name",
                )
            )
            dialog.cancelButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "settings_import_export",
                    "import_cancel_button",
                    "name",
                )
            )

            if dialog.exec():
                settings_path = get_settings_path()
                atomic_write_json(settings_path, imported_settings)

                success_dialog = MessageBox(
                    get_any_position_value_async(
                        "basic_settings",
                        "settings_import_export",
                        "import_success_title",
                        "name",
                    ),
                    get_any_position_value_async(
                        "basic_settings",
                        "settings_import_export",
                        "import_success_content",
                        "name",
                    ),
                    parent,
                )
                success_dialog.yesButton.setText(
                    get_any_position_value_async(
                        "basic_settings",
                        "settings_import_export",
                        "import_success_button",
                        "name",
                    )
                )
                success_dialog.cancelButton.hide()
                success_dialog.buttonLayout.insertStretch(1)
                success_dialog.exec()

    except Exception as e:
        logger.exception(f"导入设置失败: {e}")
        dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings",
                "settings_import_export",
                "import_failure_title",
                "name",
            ),
            get_any_position_value_async(
                "basic_settings",
                "settings_import_export",
                "import_failure_content",
                "name",
            ).format(error=str(e)),
            parent,
        )
        dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings",
                "settings_import_export",
                "import_success_button",
                "name",
            )
        )
        dialog.cancelButton.hide()
        dialog.buttonLayout.insertStretch(1)
        dialog.exec()


def export_diagnostic_data(parent: Optional[QWidget] = None) -> None:
    """导出诊断数据"""
    try:
        warning_dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings",
                "diagnostic_data_export",
                "export_warning_title",
                "name",
            ),
            get_any_position_value_async(
                "basic_settings",
                "diagnostic_data_export",
                "export_warning_content",
                "name",
            ),
            parent,
        )
        warning_dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings",
                "diagnostic_data_export",
                "export_confirm_button",
                "name",
            )
        )
        warning_dialog.cancelButton.setText(
            get_any_position_value_async(
                "basic_settings",
                "diagnostic_data_export",
                "export_cancel_button",
                "name",
            )
        )

        if not warning_dialog.exec():
            return

        app_dir = get_app_root()
        version_text = SPECIAL_VERSION

        file_path, _ = QFileDialog.getSaveFileName(
            parent,
            get_content_pushbutton_name_async(
                "basic_settings", "export_diagnostic_data"
            ),
            f"SecRandom_{version_text}_diagnostic_{datetime.now().strftime('%Y%m%d_%H%M%S')}.zip",
            "ZIP Files (*.zip);;All Files (*)",
        )

        if file_path:
            if not file_path.endswith(".zip"):
                file_path += ".zip"

            exported_count = _export_diagnostic_files(file_path, app_dir)

            system_info = _collect_system_info(
                app_dir, version_text, exported_count, file_path
            )
            _write_diagnostic_info(file_path, system_info)

            dialog = MessageBox(
                get_any_position_value_async(
                    "basic_settings",
                    "diagnostic_data_export",
                    "export_success_title",
                    "name",
                ),
                get_any_position_value_async(
                    "basic_settings",
                    "diagnostic_data_export",
                    "export_success_content",
                    "name",
                ).format(path=file_path),
                parent,
            )
            dialog.yesButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "import_success_button",
                    "name",
                )
            )
            dialog.cancelButton.hide()
            dialog.buttonLayout.insertStretch(1)
            dialog.exec()

    except Exception as e:
        logger.exception(f"导出诊断数据失败: {e}")
        dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings",
                "diagnostic_data_export",
                "export_failure_title",
                "name",
            ),
            get_any_position_value_async(
                "basic_settings",
                "diagnostic_data_export",
                "export_failure_content",
                "name",
            ).format(error=str(e)),
            parent,
        )
        dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_success_button", "name"
            )
        )
        dialog.cancelButton.hide()
        dialog.buttonLayout.insertStretch(1)
        dialog.exec()


def _export_diagnostic_files(file_path: str, app_dir: Path) -> int:
    """导出诊断数据文件

    Returns:
        导出的文件数量
    """
    export_folders = [
        get_path("config"),
        get_data_path("list"),
        get_data_path("Language"),
        get_data_path("history"),
        get_audio_path(),
        get_data_path("CSES"),
        get_data_path("images"),
        get_data_path("themes"),
        get_path(LOG_DIR),
    ]

    exported_count = 0

    with zipfile.ZipFile(file_path, "w", zipfile.ZIP_DEFLATED) as zipf:
        for folder_path in export_folders:
            if folder_path.exists():
                for file_path_obj in folder_path.rglob("*"):
                    if file_path_obj.is_file():
                        try:
                            try:
                                arc_path = str(file_path_obj.relative_to(app_dir))
                            except Exception:
                                arc_path = str(
                                    file_path_obj.relative_to(folder_path.parent)
                                )
                            zipf.write(str(file_path_obj), arc_path)
                            exported_count += 1
                        except Exception as e:
                            logger.warning(f"添加文件到ZIP失败 {file_path_obj}: {e}")

    return exported_count


def _collect_system_info(
    app_dir: Path, version_text: str, exported_count: int, file_path: str
) -> dict:
    """收集系统信息"""
    disk_usage = psutil.disk_usage(str(app_dir))
    memory_info = psutil.virtual_memory()
    swap_info = psutil.swap_memory()
    cpu_count_logical = psutil.cpu_count(logical=True)
    cpu_count_physical = psutil.cpu_count(logical=False)
    cpu_percent = psutil.cpu_percent(interval=1, percpu=True)
    cpu_freq = psutil.cpu_freq()

    system_info = {
        "export_metadata": {
            "software": "SecRandom",
            "export_time": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            "export_timestamp": datetime.now().isoformat(),
            "version": version_text,
            "export_type": "diagnostic",
        },
        "system_info": {
            "software_path": str(app_dir),
            "operating_system": _get_operating_system(),
            "platform_details": {
                "system": platform.system(),
                "release": _get_platform_release(),
                "version": _get_platform_version(),
                "machine": platform.machine(),
                "processor": platform.processor(),
            },
            "python_version": sys.version,
            "python_executable": sys.executable,
            "current_working_directory": os.getcwd(),
            "environment_variables": dict(os.environ),
        },
        "hardware_info": {
            "cpu": {
                "logical_count": cpu_count_logical,
                "physical_count": cpu_count_physical,
                "usage_per_core": cpu_percent,
                "frequency": {
                    "current": cpu_freq.current if cpu_freq else None,
                    "min": cpu_freq.min if cpu_freq else None,
                    "max": cpu_freq.max if cpu_freq else None,
                }
                if cpu_freq
                else None,
            },
            "memory": {
                "virtual_memory": {
                    "total": memory_info.total,
                    "available": memory_info.available,
                    "used": memory_info.used,
                    "free": memory_info.free,
                    "percentage": memory_info.percent,
                    "buffers": getattr(memory_info, "buffers", None),
                    "cached": getattr(memory_info, "cached", None),
                    "shared": getattr(memory_info, "shared", None),
                },
                "swap_memory": {
                    "total": swap_info.total,
                    "used": swap_info.used,
                    "free": swap_info.free,
                    "percentage": swap_info.percent,
                    "sin": getattr(swap_info, "sin", None),
                    "sout": getattr(swap_info, "sout", None),
                }
                if swap_info
                else None,
            },
            "disk": {
                "usage": {
                    "total": disk_usage.total,
                    "used": disk_usage.used,
                    "free": disk_usage.free,
                    "percentage": disk_usage.percent,
                },
                "partitions": [],
            },
        },
        "network_info": {
            "interfaces": {},
            "connections": len(psutil.net_connections()),
            "io_counters": {},
        },
        "process_info": {
            "pid": os.getpid(),
            "process_count": len(psutil.pids()),
            "current_process": {},
        },
        "export_summary": {
            "total_files_exported": exported_count,
            "export_folders": [
                str(folder)
                for folder in [
                    get_path("config"),
                    get_data_path("list"),
                    get_data_path("Language"),
                    get_data_path("history"),
                    get_audio_path(),
                    get_data_path("CSES"),
                    get_data_path("images"),
                    get_path(LOG_DIR),
                ]
            ],
            "export_location": str(file_path),
        },
    }

    _add_disk_partitions_info(system_info)
    _add_network_info(system_info)
    _add_process_info(system_info)
    _add_boot_time_info(system_info)
    _add_users_info(system_info)

    return system_info


def _add_disk_partitions_info(system_info: dict) -> None:
    """添加磁盘分区信息"""
    try:
        disk_partitions = psutil.disk_partitions()
        for partition in disk_partitions:
            partition_info = {
                "device": partition.device,
                "mountpoint": partition.mountpoint,
                "file_system_type": partition.fstype,
                "options": partition.opts,
            }

            try:
                partition_usage = psutil.disk_usage(partition.mountpoint)
                partition_info["usage"] = {
                    "total": partition_usage.total,
                    "used": partition_usage.used,
                    "free": partition_usage.free,
                    "percentage": partition_usage.percent,
                }
            except PermissionError:
                partition_info["usage"] = None

            system_info["hardware_info"]["disk"]["partitions"].append(partition_info)
    except Exception as e:
        logger.warning(f"获取磁盘分区信息失败: {e}")


def _add_network_info(system_info: dict) -> None:
    """添加网络信息"""
    try:
        net_if_addrs = psutil.net_if_addrs()
        for interface_name, interface_addresses in net_if_addrs.items():
            system_info["network_info"]["interfaces"][interface_name] = []
            for address in interface_addresses:
                system_info["network_info"]["interfaces"][interface_name].append(
                    {
                        "family": str(address.family),
                        "address": address.address,
                        "netmask": address.netmask,
                        "broadcast": address.broadcast,
                        "ptp": address.ptp,
                    }
                )
    except Exception as e:
        logger.warning(f"获取网络接口信息失败: {e}")

    try:
        net_io = psutil.net_io_counters(pernic=True)
        system_info["network_info"]["io_counters"] = {}
        for interface, stats in net_io.items():
            system_info["network_info"]["io_counters"][interface] = {
                "bytes_sent": stats.bytes_sent,
                "bytes_recv": stats.bytes_recv,
                "packets_sent": stats.packets_sent,
                "packets_recv": stats.packets_recv,
                "errin": getattr(stats, "errin", None),
                "errout": getattr(stats, "errout", None),
                "dropin": getattr(stats, "dropin", None),
                "dropout": getattr(stats, "dropout", None),
            }
    except Exception as e:
        logger.warning(f"获取网络统计信息失败: {e}")


def _add_process_info(system_info: dict) -> None:
    """添加进程信息"""
    try:
        current_process = psutil.Process(os.getpid())
        system_info["process_info"]["current_process"] = {
            "name": current_process.name(),
            "status": current_process.status(),
            "create_time": current_process.create_time(),
            "cpu_percent": current_process.cpu_percent(),
            "cpu_times": str(current_process.cpu_times()),
            "memory_info": str(current_process.memory_info()),
            "memory_percent": current_process.memory_percent(),
            "num_threads": current_process.num_threads(),
            "cmdline": current_process.cmdline(),
            "parent_pid": current_process.ppid(),
            "children": [],
        }

        try:
            children = current_process.children(recursive=True)
            for child in children:
                system_info["process_info"]["current_process"]["children"].append(
                    {
                        "pid": child.pid,
                        "name": child.name(),
                        "status": child.status(),
                    }
                )
        except Exception as e:
            logger.warning(f"获取子进程信息失败: {e}")
    except Exception as e:
        logger.warning(f"获取当前进程信息失败: {e}")


def _add_boot_time_info(system_info: dict) -> None:
    """添加开机时间信息"""
    try:
        boot_time = psutil.boot_time()
        system_info["system_info"]["boot_time"] = boot_time
        system_info["system_info"]["uptime"] = datetime.now().timestamp() - boot_time
    except Exception as e:
        logger.warning(f"获取开机时间信息失败: {e}")


def _add_users_info(system_info: dict) -> None:
    """添加用户信息"""
    try:
        users = psutil.users()
        system_info["system_info"]["users"] = []
        for user in users:
            system_info["system_info"]["users"].append(
                {
                    "name": user.name,
                    "terminal": user.terminal,
                    "host": user.host,
                    "started": user.started,
                    "pid": getattr(user, "pid", None),
                }
            )
    except Exception as e:
        logger.warning(f"获取用户信息失败: {e}")


def _write_diagnostic_info(file_path: str, system_info: dict) -> None:
    """写入诊断信息文件"""
    try:
        with zipfile.ZipFile(file_path, "a", zipfile.ZIP_DEFLATED) as zipf:
            zipf.writestr(
                "diagnostic.json", json.dumps(system_info, ensure_ascii=False, indent=2)
            )
    except Exception as e:
        logger.exception(f"写入诊断信息文件失败: {e}")

        class PathEncoder(json.JSONEncoder):
            def default(self, obj):
                if isinstance(obj, Path):
                    return str(obj)
                return super().default(obj)

        with zipfile.ZipFile(file_path, "a", zipfile.ZIP_DEFLATED) as zipf:
            zipf.writestr(
                "diagnostic.json",
                json.dumps(system_info, cls=PathEncoder, ensure_ascii=False, indent=2),
            )


def export_all_data(parent: Optional[QWidget] = None) -> None:
    """导出所有数据到文件"""
    try:
        warning_dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_warning_title", "name"
            ),
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_warning_content", "name"
            ),
            parent,
        )
        warning_dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_confirm_button", "name"
            )
        )
        warning_dialog.cancelButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_cancel_button", "name"
            )
        )

        if not warning_dialog.exec():
            return

        file_path, _ = QFileDialog.getSaveFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "export_all_data"),
            f"SecRandom_{SPECIAL_VERSION}_all_data.zip",
            "ZIP Files (*.zip);;All Files (*)",
        )

        if not file_path:
            return

        if not file_path.endswith(".zip"):
            file_path += ".zip"

        dirs_to_backup = [
            ("config", get_path("config")),
            ("list", get_data_path("list")),
            ("Language", get_data_path("Language")),
            ("history", get_data_path("history")),
            ("audio", get_audio_path()),
            ("CSES", get_data_path("CSES")),
            ("images", get_data_path("images")),
            ("logs", get_path(LOG_DIR)),
        ]

        with zipfile.ZipFile(file_path, "w", zipfile.ZIP_DEFLATED) as zipf:
            version_info = {
                "software_name": "SecRandom",
                "version": SPECIAL_VERSION,
            }
            zipf.writestr(
                "version.json", json.dumps(version_info, ensure_ascii=False, indent=2)
            )

            for dir_name, dir_path in dirs_to_backup:
                if dir_path.exists():
                    for file_path_obj in dir_path.rglob("*"):
                        if file_path_obj.is_file():
                            arc_path = str(
                                Path(dir_name) / file_path_obj.relative_to(dir_path)
                            )
                            zipf.write(str(file_path_obj), arc_path)

        dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_success_title", "name"
            ),
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_success_content", "name"
            ).format(path=file_path),
            parent,
        )
        dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_success_button", "name"
            )
        )
        dialog.cancelButton.hide()
        dialog.buttonLayout.insertStretch(1)
        dialog.exec()

    except Exception as e:
        logger.exception(f"导出所有数据失败: {e}")
        dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_failure_title", "name"
            ),
            get_any_position_value_async(
                "basic_settings", "data_import_export", "export_failure_content", "name"
            ).format(error=str(e)),
            parent,
        )
        dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_success_button", "name"
            )
        )
        dialog.cancelButton.hide()
        dialog.buttonLayout.insertStretch(1)
        dialog.exec()


def _show_import_all_data_failure(parent: Optional[QWidget], e: Exception) -> None:
    logger.exception(f"导入所有数据失败: {e}")
    dialog = MessageBox(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_failure_title", "name"
        ),
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_failure_content", "name"
        ).format(error=str(e)),
        parent,
    )
    dialog.yesButton.setText(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_success_button", "name"
        )
    )
    dialog.cancelButton.hide()
    dialog.buttonLayout.insertStretch(1)
    dialog.exec()


def _show_import_all_data_success(
    parent: Optional[QWidget],
    skipped_files: list,
    on_success: Optional[Callable[[], None]],
) -> None:
    success_dialog = MessageBox(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_success_title", "name"
        ),
        (
            get_any_position_value_async(
                "basic_settings",
                "data_import_export",
                "import_success_content_skipped",
                "name",
            ).format(count=len(skipped_files))
            if skipped_files
            else get_any_position_value_async(
                "basic_settings", "data_import_export", "import_success_content", "name"
            )
        ),
        parent,
    )
    success_dialog.yesButton.setText(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_success_button", "name"
        )
    )
    success_dialog.cancelButton.hide()
    success_dialog.buttonLayout.insertStretch(1)
    success_dialog.exec()
    if on_success:
        try:
            on_success()
        except Exception:
            pass


def _format_existing_files_preview(existing_files: list) -> str:
    files_list = "\n".join(existing_files[:10])
    if len(existing_files) > 10:
        files_list += get_any_position_value_async(
            "basic_settings", "data_import_export", "existing_files_count", "name"
        ).format(len=len(existing_files) - 10)
    return files_list


def _request_import_overwrite_confirmation(
    parent: Optional[QWidget], existing_files: list, on_confirm: Callable[[], None]
) -> None:
    files_list = _format_existing_files_preview(existing_files)

    def _apply() -> None:
        dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "existing_files_title", "name"
            ),
            get_any_position_value_async(
                "basic_settings", "data_import_export", "existing_files_content", "name"
            ).format(files=files_list),
            parent,
        )
        dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_confirm_button", "name"
            )
        )
        dialog.cancelButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_cancel_button", "name"
            )
        )
        if dialog.exec():
            on_confirm()

    _apply()


def _request_import_version_mismatch_confirmation(
    parent: Optional[QWidget],
    software_name: str,
    version: str,
    current_version: str,
    on_confirm: Callable[[], None],
) -> None:
    def _apply() -> None:
        warning_dialog = MessageBox(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "version_mismatch_title", "name"
            ),
            get_any_position_value_async(
                "basic_settings",
                "data_import_export",
                "version_mismatch_content",
                "name",
            ).format(
                software_name=software_name,
                version=version,
                current_version=current_version,
            ),
            parent,
        )
        warning_dialog.yesButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_confirm_button", "name"
            )
        )
        warning_dialog.cancelButton.setText(
            get_any_position_value_async(
                "basic_settings", "data_import_export", "import_cancel_button", "name"
            )
        )
        if warning_dialog.exec():
            on_confirm()

    _apply()


def _perform_import_all_data_from_file(
    file_path: str, parent: Optional[QWidget], on_success: Optional[Callable[[], None]]
) -> None:
    if not _confirm_import(parent):
        return
    try:
        skipped_files = _extract_data_files(file_path)
    except Exception as e:
        _show_import_all_data_failure(parent, e)
        return
    _show_import_all_data_success(parent, skipped_files, on_success)


def _start_import_all_data_flow(
    file_path: str, parent: Optional[QWidget], on_success: Optional[Callable[[], None]]
) -> None:
    version_info = _check_version_info(file_path)
    current_version = SPECIAL_VERSION
    software_name = version_info.get("software_name", "") if version_info else ""
    version = version_info.get("version", "") if version_info else ""

    def _after_version_confirmed() -> None:
        existing_files = _check_existing_files(file_path)
        if existing_files:
            _request_import_overwrite_confirmation(
                parent,
                existing_files,
                lambda: _perform_import_all_data_from_file(
                    file_path, parent, on_success
                ),
            )
            return
        _perform_import_all_data_from_file(file_path, parent, on_success)

    if version_info and (software_name != "SecRandom" or version != current_version):
        _request_import_version_mismatch_confirmation(
            parent,
            software_name,
            version,
            current_version,
            _after_version_confirmed,
        )
        return

    _after_version_confirmed()


def import_all_data(
    parent: Optional[QWidget] = None, on_success: Optional[Callable[[], None]] = None
) -> bool:
    """从文件导入所有数据"""
    try:
        file_path, _ = QFileDialog.getOpenFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "import_all_data"),
            "",
            "ZIP Files (*.zip);;All Files (*)",
        )
        if not file_path:
            return False
        _start_import_all_data_flow(file_path, parent, on_success)
        return True
    except Exception as e:
        _show_import_all_data_failure(parent, e)
        return False


def import_all_data_from_file_path(
    file_path: str,
    parent: Optional[QWidget] = None,
    on_success: Optional[Callable[[], None]] = None,
) -> bool:
    """从指定文件路径导入所有数据"""
    try:
        if not file_path:
            return False
        _start_import_all_data_flow(file_path, parent, on_success)
        return True
    except Exception as e:
        _show_import_all_data_failure(parent, e)
        return False


def _check_version_info(file_path: str) -> dict:
    """检查版本信息"""
    version_info = {}
    try:
        with zipfile.ZipFile(file_path, "r") as zipf:
            if "version.json" in zipf.namelist():
                with zipf.open("version.json") as vf:
                    version_info = json.load(vf)
    except Exception as e:
        logger.warning(f"读取版本信息失败: {e}")
    return version_info


def _check_existing_files(file_path: str) -> list:
    """检查已存在的文件"""
    existing_files = []
    target_dirs = {
        "config": get_path("config"),
        "list": get_data_path("list"),
        "Language": get_data_path("Language"),
        "history": get_data_path("history"),
        "audio": get_audio_path(),
        "CSES": get_data_path("CSES"),
        "images": get_data_path("images"),
        "theme": get_data_path("themes"),
        "logs": get_path(LOG_DIR),
    }

    with zipfile.ZipFile(file_path, "r") as zipf:
        for member in zipf.namelist():
            if member == "version.json":
                continue

            parts = Path(member).parts
            if len(parts) > 1:
                dir_name = parts[0]
                relative_path = Path(*parts[1:])

                if dir_name in target_dirs:
                    target_path = target_dirs[dir_name] / relative_path
                    if target_path.exists():
                        existing_files.append(str(target_path))

    return existing_files


def _confirm_import(parent: Optional[QWidget]) -> bool:
    """确认导入"""
    dialog = MessageBox(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_confirm_title", "name"
        ),
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_confirm_content", "name"
        ),
        parent,
    )
    dialog.yesButton.setText(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_confirm_button", "name"
        )
    )
    dialog.cancelButton.setText(
        get_any_position_value_async(
            "basic_settings", "data_import_export", "import_cancel_button", "name"
        )
    )
    return dialog.exec()


def _extract_data_files(file_path: str) -> list:
    """提取数据文件"""
    target_dirs = {
        "config": get_path("config"),
        "list": get_data_path("list"),
        "Language": get_data_path("Language"),
        "history": get_data_path("history"),
        "CSES": get_data_path("CSES"),
        "images": get_data_path("images"),
        "theme": get_data_path("themes"),
        "audio": get_audio_path(),
        "logs": get_path(LOG_DIR),
    }

    skipped_files = []
    logs_root = get_path(LOG_DIR).resolve()

    with zipfile.ZipFile(file_path, "r") as zipf:
        for member in zipf.namelist():
            if member == "version.json":
                continue

            parts = Path(member).parts
            if len(parts) > 1:
                dir_name = parts[0]
                relative_path = Path(*parts[1:])

                if dir_name in target_dirs:
                    target_path = target_dirs[dir_name] / relative_path
                    target_path.parent.mkdir(parents=True, exist_ok=True)

                    tmp_path = target_path.with_name(f"{target_path.name}.import_tmp")
                    if tmp_path.exists():
                        try:
                            tmp_path.unlink()
                        except Exception:
                            pass

                    with zipf.open(member) as source, open(tmp_path, "wb") as target:
                        shutil.copyfileobj(source, target)

                    for attempt in range(3):
                        try:
                            os.replace(tmp_path, target_path)
                            break
                        except PermissionError as e:
                            if attempt < 2:
                                time.sleep(0.2)
                                continue

                            is_logs_file = False
                            try:
                                is_logs_file = target_path.resolve().is_relative_to(
                                    logs_root
                                )
                            except Exception:
                                is_logs_file = (
                                    str(target_path)
                                    .replace("\\", "/")
                                    .startswith(str(logs_root).replace("\\", "/") + "/")
                                )

                            if is_logs_file:
                                skipped_files.append(str(target_path))
                                try:
                                    tmp_path.unlink()
                                except Exception:
                                    pass
                                logger.warning(f"导入文件被占用，已跳过: {target_path}")
                                break

                            raise e

    return skipped_files


# ==================== 记录管理模块 ====================


def _normalize_clear_record_mode(clear_record) -> Optional[str]:
    try:
        clear_record = int(clear_record)
    except Exception:
        return None
    if clear_record == 0:
        return "all"
    if clear_record == 1:
        return "until"
    return None


def check_clear_record(settings_group: str) -> Optional[str]:
    """检查是否需要清除已抽取记录

    Returns:
        "all": 重启后清除
        "until": 直至全部抽取完
        None: 不清除记录
    """
    draw_mode = readme_settings_async(settings_group, "draw_mode")
    try:
        draw_mode = int(draw_mode)
    except Exception:
        draw_mode = 0
    if draw_mode == 0:
        return None
    clear_record = readme_settings_async(settings_group, "clear_record")
    normalized = _normalize_clear_record_mode(clear_record)
    return normalized or "all"


def _normalize_record_component(value, default_value: str = "unknown") -> str:
    if value is None:
        return default_value
    text = str(value).strip()
    if not text:
        return default_value
    text = re.sub(r'[\\/:*?"<>|]', "_", text)
    text = re.sub(r"\s+", "_", text)
    return text


def _build_record_file_name(prefix: str, *parts) -> str:
    normalized_parts = [_normalize_record_component(part) for part in parts]
    if normalized_parts:
        return f"{prefix}__{'__'.join(normalized_parts)}.json"
    return f"{prefix}.json"


def _get_roll_call_record_file_path(class_name: str, gender: str, group: str):
    return get_data_path(
        "TEMP",
        _build_record_file_name("roll_call_record", class_name, gender, group),
    )


def record_drawn_student(
    class_name: str, gender: str, group: str, student_name
) -> None:
    """记录已抽取的学生名称和次数

    Args:
        class_name: 班级名称
        gender: 性别
        group: 分组
        student_name: 学生名称或学生列表
    """
    file_path = _get_roll_call_record_file_path(class_name, gender, group)
    file_path.parent.mkdir(parents=True, exist_ok=True)

    drawn_records = _load_drawn_records(file_path)
    students_to_add = _extract_student_names(student_name)

    updated_students = []
    for name in students_to_add:
        if name in drawn_records:
            drawn_records[name] += 1
            updated_students.append(f"{name}(第{drawn_records[name]}次)")
        else:
            drawn_records[name] = 1
            updated_students.append(f"{name}(第1次)")

    if updated_students:
        _save_drawn_records(file_path, drawn_records)
        logger.debug(f"已记录学生/小组: {', '.join(updated_students)}")
    else:
        logger.debug("没有新的学生需要记录")


def _load_drawn_records(file_path: str) -> dict:
    """从文件加载已抽取的学生记录

    Args:
        file_path: 记录文件路径

    Returns:
        已抽取的学生记录字典，键为学生名称，值为抽取次数
    """
    if not os.path.exists(file_path):
        return {}

    try:
        with open(file_path, "r", encoding="utf-8") as file:
            data = json.load(file)

        drawn_records = {}

        if isinstance(data, dict):
            if "drawn_names" in data and isinstance(data["drawn_names"], list):
                for item in data["drawn_names"]:
                    if isinstance(item, str):
                        drawn_records[item] = 1
                    elif isinstance(item, dict) and "name" in item:
                        name = item["name"]
                        count = item.get("count", 1)
                        if isinstance(name, str) and isinstance(count, int):
                            drawn_records[name] = count
            else:
                for name, count in data.items():
                    if isinstance(name, str) and isinstance(count, int):
                        drawn_records[name] = count
        elif isinstance(data, list):
            for item in data:
                if isinstance(item, str):
                    drawn_records[item] = 1
                elif isinstance(item, dict) and "name" in item:
                    name = item["name"]
                    count = item.get("count", 1)
                    if isinstance(name, str) and isinstance(count, int):
                        drawn_records[name] = count

        return drawn_records
    except FileNotFoundError:
        return {}
    except (json.JSONDecodeError, IOError) as e:
        logger.warning(f"读取已抽取记录失败: {e}")
        return {}


def _extract_student_names(student_name) -> list:
    """从不同类型的学生名称参数中提取学生名称列表

    Args:
        student_name: 学生名称或学生列表

    Returns:
        学生名称列表
    """
    if isinstance(student_name, str):
        return [student_name]

    if isinstance(student_name, list):
        names = []
        for item in student_name:
            if isinstance(item, str):
                names.append(item)
            elif isinstance(item, tuple) and len(item) >= 2:
                names.append(item[1])
        return names

    if isinstance(student_name, tuple) and len(student_name) >= 2:
        return [student_name[1]]

    return []


def _save_drawn_records(file_path: str, drawn_records: dict) -> None:
    """保存已抽取的学生记录到文件

    Args:
        file_path: 记录文件路径
        drawn_records: 已抽取的学生记录字典
    """
    try:
        atomic_write_json(file_path, drawn_records, indent=2)
    except IOError as e:
        logger.exception(f"保存已抽取记录失败: {e}")


def read_drawn_record(class_name: str, gender: str, group: str) -> list:
    """读取已抽取记录

    Args:
        class_name: 班级名称
        gender: 性别
        group: 分组

    Returns:
        已抽取记录列表，每个元素为(名称, 次数)元组
    """
    file_path = _get_roll_call_record_file_path(class_name, gender, group)
    drawn_records = _load_drawn_records(file_path)
    return list(drawn_records.items())


def remove_record(class_name: str, gender: str, group: str, _prefix: str = "0") -> None:
    """清除已抽取记录

    Args:
        class_name: 班级名称
        gender: 性别
        group: 分组
        _prefix: 前缀标识
    """
    prefix = check_clear_record("roll_call_settings")
    if _prefix == "restart":
        prefix = "restart"

    logger.debug(f"清除记录前缀: {prefix}, _prefix: {_prefix}")

    if not prefix:
        return

    temp_dir = get_data_path("TEMP")
    if not temp_dir.exists():
        return

    file_paths = []
    if prefix == "restart":
        file_paths = list(temp_dir.glob("roll_call_record__*.json"))
    elif class_name and gender and group:
        if prefix in ["all", "until"]:
            file_paths = [_get_roll_call_record_file_path(class_name, gender, group)]

    if not file_paths:
        return

    try:
        for file_path in file_paths:
            if file_path.exists() and file_path.is_file():
                file_path.unlink(missing_ok=True)
                logger.info(f"已删除记录文件: {file_path.name}")
    except OSError as e:
        logger.exception(f"删除记录文件失败: {e}")


def reset_drawn_record(self, class_name: str, gender: str, group: str) -> None:
    """删除已抽取记录文件

    Args:
        self: 父窗口组件
        class_name: 班级名称
        gender: 性别
        group: 分组
    """
    if check_clear_record("roll_call_settings") in ["all", "until"]:
        remove_record(class_name, gender, group)
        show_notification(
            NotificationType.INFO,
            NotificationConfig(
                title="提示",
                content=f"已重置{class_name}已抽取记录",
                icon=FluentIcon.INFO,
            ),
            parent=self,
        )
        logger.info(f"已重置{class_name}_{gender}_{group}已抽取记录")
    else:
        show_notification(
            NotificationType.INFO,
            NotificationConfig(
                title="提示",
                content=f"当前处于重复抽取状态，无需清除{class_name}已抽取记录",
                icon=get_theme_icon("ic_fluent_warning_20_filled"),
            ),
            parent=self,
        )
        logger.info(
            f"当前处于重复抽取状态，无需清除{class_name}_{gender}_{group}已抽取记录"
        )


def clear_temp_draw_records() -> int:
    temp_dir = get_data_path("TEMP")
    if not temp_dir.exists():
        return 0

    file_paths = list(temp_dir.glob("roll_call_record__*.json")) + list(
        temp_dir.glob("lottery_prize_record__*.json")
    )
    deleted_count = 0
    for file_path in file_paths:
        try:
            if file_path.exists() and file_path.is_file():
                file_path.unlink(missing_ok=True)
                deleted_count += 1
        except OSError as e:
            logger.exception(f"删除记录文件失败: {e}")
    if deleted_count:
        logger.info(f"已清除 {deleted_count} 个抽取临时记录文件")
    return deleted_count


def calculate_remaining_count(
    half_repeat: int,
    class_name: str,
    gender_filter: str,
    group_index: int,
    group_filter: str,
    total_count: int,
) -> int:
    """根据half_repeat设置计算实际剩余人数或组数

    Args:
        half_repeat: 重复抽取次数
        class_name: 班级名称
        gender_filter: 性别筛选条件
        group_index: 分组索引
        group_filter: 分组筛选条件
        total_count: 总人数或总组数

    Returns:
        实际剩余人数或组数
    """
    if half_repeat > 0:
        drawn_records = read_drawn_record(class_name, gender_filter, group_filter)
        drawn_counts = {}
        for record in drawn_records:
            if isinstance(record, tuple) and len(record) >= 2:
                name, count = record[0], record[1]
                drawn_counts[name] = count
            elif isinstance(record, dict) and "name" in record:
                name = record["name"]
                count = record.get("count", 1)
                drawn_counts[name] = count

        if group_index == 1:
            group_list = get_group_list(class_name)
            excluded_count = 0
            for group_name in group_list:
                if (
                    group_name in drawn_counts
                    and drawn_counts[group_name] >= half_repeat
                ):
                    excluded_count += 1
            return max(0, len(group_list) - excluded_count)
        else:
            student_list = get_student_list(class_name)
            excluded_count = 0
            for student in student_list:
                student_name = (
                    student["name"]
                    if isinstance(student, dict) and "name" in student
                    else student
                )
                if (
                    student_name in drawn_counts
                    and drawn_counts[student_name] >= half_repeat
                ):
                    excluded_count += 1
            return max(0, total_count - excluded_count)
    else:
        return total_count


def _get_lottery_prize_record_file_path(pool_name: str):
    return get_data_path(
        "TEMP",
        _build_record_file_name("lottery_prize_record", pool_name),
    )


def record_drawn_prize(pool_name: str, prize_names) -> None:
    """记录已抽取的奖品

    Args:
        pool_name: 奖池名称
        prize_names: 奖品名称或奖品列表
    """
    file_path = _get_lottery_prize_record_file_path(pool_name)
    file_path.parent.mkdir(parents=True, exist_ok=True)
    drawn_records = _load_drawn_records(file_path)
    names = _extract_student_names(prize_names)
    for name in names:
        if name in drawn_records:
            drawn_records[name] += 1
        else:
            drawn_records[name] = 1
    _save_drawn_records(file_path, drawn_records)


def read_drawn_record_simple(pool_name: str) -> list:
    """读取已抽取的奖品记录

    Args:
        pool_name: 奖池名称

    Returns:
        已抽取记录列表，每个元素为(名称, 次数)元组
    """
    file_path = _get_lottery_prize_record_file_path(pool_name)
    if file_path.exists():
        try:
            with open(file_path, "r", encoding="utf-8") as file:
                data = json.load(file)
            if isinstance(data, dict):
                return [(name, count) for name, count in data.items()]
            if isinstance(data, list):
                res = []
                for item in data:
                    if isinstance(item, str):
                        res.append((item, 1))
                    elif isinstance(item, dict) and "name" in item:
                        res.append((item["name"], int(item.get("count", 1))))
                return res
        except Exception as e:
            logger.exception(f"读取奖池已抽取记录失败: {e}")
            return []
    return []


def delete_drawn_prize_record_files(pool_name: str) -> bool:
    """删除奖池抽取记录文件

    Args:
        pool_name: 奖池名称

    Returns:
        bool: 是否成功删除
    """
    try:
        file_path = _get_lottery_prize_record_file_path(pool_name)
        if file_path.exists() and file_path.is_file():
            try:
                file_path.unlink(missing_ok=True)
            except OSError as e:
                logger.error(f"删除文件{file_path}失败: {e}")
        return True
    except Exception as e:
        logger.exception(f"重置奖池抽取记录失败: {e}")
        return False


def reset_drawn_prize_record(self, pool_name: str) -> None:
    """重置奖池抽取记录

    Args:
        self: 父窗口组件
        pool_name: 奖池名称
    """
    if check_clear_record("lottery_settings") in ["all", "until"]:
        if delete_drawn_prize_record_files(pool_name):
            show_notification(
                NotificationType.INFO,
                NotificationConfig(
                    title="提示",
                    content=f"已重置{pool_name}奖池抽取记录",
                    icon=FluentIcon.INFO,
                ),
                parent=self,
            )
    else:
        show_notification(
            NotificationType.INFO,
            NotificationConfig(
                title="提示",
                content=f"当前处于重复抽取状态，无需清除{pool_name}奖池抽取记录",
                icon=get_theme_icon("ic_fluent_warning_20_filled"),
            ),
            parent=self,
        )
