import os
import json
import glob
import shutil
import zipfile
from typing import Optional, Union
from loguru import logger
from pathlib import Path
from datetime import datetime
import platform
import sys
import psutil

from PySide6.QtWidgets import QWidget, QFileDialog
from PySide6.QtCore import Qt
from qfluentwidgets import InfoBar, InfoBarPosition, FluentIcon, InfoBarIcon, MessageBox
from app.common.safety.verify_ops import require_and_run

# 平台特定导入
if sys.platform.startswith("linux"):
    try:
        import pulsectl
    except ImportError:
        pulsectl = None

from app.tools.path_utils import (
    get_app_root,
    get_data_path,
    get_settings_path,
    get_path,
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
    LOG_COMPRESSION,
    APPLY_NAME,
)
from app.Language.obtain_language import (
    get_content_pushbutton_name_async,
    get_any_position_value_async,
)


# ======= 日志配置函数 =======
def configure_logging():
    """配置日志系统"""
    # 确保日志目录存在
    log_dir = get_path(LOG_DIR)
    log_dir.mkdir(exist_ok=True)

    # 获取日志等级设置，默认为INFO
    log_level = (
        readme_settings_async("basic_settings", "log_level")
        if readme_settings_async("basic_settings", "log_level")
        else "INFO"
    )

    # 配置日志格式 - 文件输出（包含详细的调试信息）
    logger.add(
        log_dir / LOG_FILENAME_FORMAT,
        rotation=LOG_ROTATION_SIZE,
        retention=LOG_RETENTION_DAYS,
        compression=LOG_COMPRESSION,
        backtrace=True,
        diagnose=True,
        level=log_level,
    )

    # 配置日志格式 - 终端输出（仅当stdout可用时）
    if sys.stdout is not None:
        logger.add(
            sys.stdout,
            format="<green>{time:YYYY-MM-DD HH:mm:ss}</green> | <level>{level: <8}</level> | <cyan>{name}</cyan>:<cyan>{function}</cyan>:<cyan>{line}</cyan> - <level>{message}</level>",
            level=log_level,
            colorize=True,
        )

    logger.debug(f"日志系统已配置，当前日志等级: {log_level}")


# ======= 通知工具函数 =======
def show_success_notification(
    title: str,
    content: str,
    parent: Optional[QWidget] = None,
    duration: int = 3000,
    position: Union[InfoBarPosition, str] = InfoBarPosition.TOP,
    is_closable: bool = True,
    orient: Qt.Orientation = Qt.Orientation.Horizontal,
) -> InfoBar:
    """
    显示成功通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为顶部
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平

    Returns:
        InfoBar实例
    """
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
    """
    显示警告通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为底部
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平

    Returns:
        InfoBar实例
    """
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
    """
    显示错误通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为右下角
        is_closable: 是否可关闭
        orient: 布局方向，默认为垂直(适合长内容)

    Returns:
        InfoBar实例
    """
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
    """
    显示信息通知

    Args:
        title: 通知标题
        content: 通知内容
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为左下角
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平

    Returns:
        InfoBar实例
    """
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
    """
    显示自定义通知

    Args:
        title: 通知标题
        content: 通知内容
        icon: 通知图标
        parent: 父窗口组件
        duration: 显示时长(毫秒)，-1表示永不消失
        position: 显示位置，默认为顶部
        is_closable: 是否可关闭
        orient: 布局方向，默认为水平
        background_color: 背景颜色
        text_color: 文本颜色

    Returns:
        InfoBar实例
    """
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


class NotificationType:
    """预定义的通知类型"""

    SUCCESS = "success"
    WARNING = "warning"
    ERROR = "error"
    INFO = "info"
    CUSTOM = "custom"


def show_notification(
    notification_type: str, config: NotificationConfig, parent: Optional[QWidget] = None
) -> InfoBar:
    """
    显示通知

    Args:
        notification_type: 通知类型，值为NotificationType中定义的常量
        config: 通知配置对象
        parent: 父窗口组件

    Returns:
        InfoBar实例
    """
    if notification_type == NotificationType.SUCCESS:
        return InfoBar.success(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.WARNING:
        return InfoBar.warning(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.ERROR:
        return InfoBar.error(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.INFO:
        return InfoBar.info(
            title=config.title,
            content=config.content,
            orient=config.orient,
            isClosable=config.is_closable,
            position=config.position,
            duration=config.duration,
            parent=parent,
        )
    elif notification_type == NotificationType.CUSTOM:
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
            info_bar.setCustomBackgroundColor(
                config.background_color, config.text_color
            )

        return info_bar
    else:
        raise ValueError(f"不支持的通知类型: {notification_type}")


def send_system_notification(title: str, content: str, url: str = None) -> bool:
    """
    发送系统通知

    Args:
        title: 通知标题
        content: 通知内容
        url: 点击通知后跳转的URL，默认为下载链接

    Returns:
        bool: 通知发送是否成功
    """
    try:
        # 获取软件图标路径
        icon_path = str(get_data_path("assets", "icon/secrandom-icon-paper.ico"))

        # 定义点击通知的回调函数
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
                logger.error(f"打开通知链接失败: {e}")

        if sys.platform == "win32":
            # Windows平台
            try:
                # 尝试使用win10toast发送通知
                from win10toast import ToastNotifier

                toaster = ToastNotifier()
                toaster.show_toast(
                    title,
                    content,
                    icon_path=icon_path,
                    duration=0,
                    threaded=True,
                    callback_on_click=on_notification_click,
                )
                logger.debug(f"已发送Windows通知: {title}")
                return True
            except ImportError:
                # 如果win10toast不可用，尝试使用plyer
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
        elif sys.platform.startswith("linux"):
            # Linux平台
            try:
                # 尝试使用notify-send命令
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
            except subprocess.CalledProcessError as e:
                # 如果notify-send不可用，尝试使用plyer
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
        else:
            # 其他平台，不支持系统通知
            logger.warning(f"当前平台不支持系统通知: {sys.platform}")
            return False
    except Exception as e:
        logger.error(f"发送系统通知时发生意外错误: {e}")
        return False


# ======= 系统功能相关函数 =======
def restore_volume(volume_value):
    """跨平台音量函数

    Args:
        volume_value (int): 音量值 (0-100)
    """
    if sys.platform == "win32":
        # Windows音频控制
        try:
            # 确保所有必要模块都已导入
            from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume
            import comtypes
            from comtypes import POINTER, CLSCTX_ALL

            # 初始化COM库
            comtypes.CoInitialize()

            try:
                # 直接获取系统主音量控制器（这是正确设置系统音量的方法）
                try:
                    # 获取默认音频设备
                    devices = AudioUtilities.GetSpeakers()
                    if hasattr(devices, "Activate"):
                        # 激活音量接口
                        interface = devices.Activate(
                            IAudioEndpointVolume._iid_, CLSCTX_ALL, None
                        )
                        # 转换为音量接口
                        volume = comtypes.cast(interface, POINTER(IAudioEndpointVolume))
                        # 取消静音
                        volume.SetMute(0, None)
                        # 设置主音量（0.0-1.0范围）
                        volume.SetMasterVolumeLevelScalar(volume_value / 100.0, None)
                        # 获取实际设置的音量值进行验证
                        actual_volume = volume.GetMasterVolumeLevelScalar() * 100
                        logger.info(
                            f"Windows音量设置为: {volume_value}%，实际设置值: {actual_volume:.1f}%"
                        )
                    else:
                        # 如果Activate方法不可用，尝试使用设备枚举器
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
                                volume.SetMasterVolumeLevelScalar(
                                    volume_value / 100.0, None
                                )
                                actual_volume = (
                                    volume.GetMasterVolumeLevelScalar() * 100
                                )
                                logger.info(
                                    f"Windows音量设置为: {volume_value}%，实际设置值: {actual_volume:.1f}%"
                                )
                            else:
                                logger.error("音频设备没有Activate方法")
                        else:
                            logger.error("无法获取音频设备接口")
                except Exception as e:
                    logger.error(f"获取系统主音量控制器失败: {e}")
                    # 作为最后尝试，遍历所有会话并设置音量
                    sessions = AudioUtilities.GetAllSessions()
                    for session in sessions:
                        try:
                            session_volume = session.SimpleAudioVolume
                            session_volume.SetMasterVolume(volume_value / 100.0, None)
                            logger.info(f"设置会话音量为: {volume_value}%")
                        except Exception:
                            continue
            finally:
                # 释放COM库
                comtypes.CoUninitialize()
        except Exception as e:
            logger.error(f"Windows音量控制失败: {e}")
    elif sys.platform.startswith("linux"):
        # Linux音频控制 (使用PulseAudio)
        try:
            if pulsectl is None:
                logger.warning("pulsectl未安装，无法控制音量")
                return

            with pulsectl.Pulse("secrandom-volume-control") as pulse:
                # 获取默认sink（输出设备）
                sinks = pulse.sink_list()
                if not sinks:
                    logger.warning("未找到音频输出设备")
                    return

                # 获取默认sink或第一个可用的sink
                default_sink = None
                for sink in sinks:
                    if sink.name == pulse.server_info().default_sink_name:
                        default_sink = sink
                        break

                if default_sink is None:
                    default_sink = sinks[0]

                # 取消静音
                pulse.sink_mute(default_sink.index, 0)

                # 设置音量 (PulseAudio使用0.0-1.0范围)
                pulse.volume_set_all_chans(default_sink, volume_value / 100.0)
                logger.info(f"Linux音量设置为: {volume_value}%")
        except Exception as e:
            logger.error(f"Linux音量控制失败: {e}")
    else:
        logger.warning(f"不支持的平台: {sys.platform}，音量控制功能不可用")


# ======= 设置导入/导出功能 =======
def export_settings(parent: Optional[QWidget] = None) -> None:
    """导出设置到文件

    Args:
        parent: 父窗口组件，用于对话框的模态显示
    """
    try:
        # 获取设置文件路径
        settings_path = get_settings_path()

        # 打开文件保存对话框
        file_path, _ = QFileDialog.getSaveFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "export_settings"),
            "settings.json",
            "JSON Files (*.json);;All Files (*)",
        )

        if file_path:
            # 复制设置文件到用户选择的位置
            Path(file_path).write_text(
                Path(settings_path).read_text(encoding="utf-8"), encoding="utf-8"
            )

            # 显示成功消息
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
        logger.error(f"导出设置失败: {e}")
        # 显示错误消息
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
    """从文件导入设置

    Args:
        parent: 父窗口组件，用于对话框的模态显示
    """
    try:
        # 打开文件选择对话框
        file_path, _ = QFileDialog.getOpenFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "import_settings"),
            "",
            "JSON Files (*.json);;All Files (*)",
        )

        if file_path:
            # 读取选定的设置文件
            with open(file_path, "r", encoding="utf-8") as f:
                imported_settings = json.load(f)

            # 显示确认对话框
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
                # 获取当前设置文件路径
                settings_path = get_settings_path()

                # 写入新设置
                with open(settings_path, "w", encoding="utf-8") as f:
                    json.dump(imported_settings, f, ensure_ascii=False, indent=4)

                # 显示成功消息
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
        logger.error(f"导入设置失败: {e}")
        # 显示错误消息
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
    """导出诊断数据

    Args:
        parent: 父窗口组件，用于对话框的模态显示
    """
    try:
        # 先显示导出警告对话框
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
            return  # 用户取消操作

        # 获取软件安装路径
        app_dir = get_app_root()

        # 获取版本信息
        version_text = SPECIAL_VERSION

        # 打开文件保存对话框
        file_path, _ = QFileDialog.getSaveFileName(
            parent,
            get_content_pushbutton_name_async(
                "basic_settings", "export_diagnostic_data"
            ),
            f"SecRandom_{version_text}_diagnostic_{datetime.now().strftime('%Y%m%d_%H%M%S')}.zip",
            "ZIP Files (*.zip);;All Files (*)",
        )

        if file_path:
            # 确保文件路径以.zip结尾
            if not file_path.endswith(".zip"):
                file_path += ".zip"

            # 创建需要导出的目录列表
            export_folders = [
                Path("config"),
                Path("app") / "data" / "list",
                Path("app") / "data" / "Language",
                Path("app") / "data" / "history",
                Path("logs"),
            ]

            # 统计导出的文件数量
            exported_count = 0

            with zipfile.ZipFile(file_path, "w", zipfile.ZIP_DEFLATED) as zipf:
                # 添加各个文件夹中的文件
                for folder_path in export_folders:
                    if folder_path.exists():
                        for file_path_obj in folder_path.rglob("*"):
                            if file_path_obj.is_file():
                                # 计算在zip中的路径
                                try:
                                    arc_path = str(
                                        file_path_obj.relative_to(folder_path.parent)
                                    )
                                    zipf.write(str(file_path_obj), arc_path)
                                except Exception as e:
                                    logger.warning(
                                        f"添加文件到ZIP失败 {file_path_obj}: {e}"
                                    )
                                    continue
                                exported_count += 1

                # 写入诊断信息JSON文件（在所有文件添加完后写入，避免重复）
                # 获取磁盘使用情况
                disk_usage = psutil.disk_usage(str(app_dir))

                # 获取内存信息
                memory_info = psutil.virtual_memory()
                swap_info = psutil.swap_memory()

                # 获取CPU信息
                cpu_count_logical = psutil.cpu_count(logical=True)
                cpu_count_physical = psutil.cpu_count(logical=False)
                cpu_percent = psutil.cpu_percent(interval=1, percpu=True)
                cpu_freq = psutil.cpu_freq()

                system_info = {
                    # 【导出元数据】基础信息记录
                    "export_metadata": {
                        "software": "SecRandom",  # 软件名称
                        "export_time": datetime.now().strftime(
                            "%Y-%m-%d %H:%M:%S"
                        ),  # 人类可读时间
                        "export_timestamp": datetime.now().isoformat(),  # ISO标准时间戳
                        "version": version_text,  # 当前软件版本
                        "export_type": "diagnostic",  # 导出类型（诊断数据）
                    },
                    # 【系统环境信息】详细的运行环境数据
                    "system_info": {
                        "software_path": str(app_dir),  # 软件安装路径
                        "operating_system": _get_operating_system(),  # 操作系统版本（正确识别Win11）
                        "platform_details": {  # 平台详细信息
                            "system": platform.system(),  # 系统类型 (Windows/Linux/Darwin)
                            "release": _get_platform_release(),  # 系统发行版本（正确识别Win11）
                            "version": _get_platform_version(),  # 完整系统版本（正确识别Win11）
                            "machine": platform.machine(),  # 机器架构 (AMD64/x86_64)
                            "processor": platform.processor(),  # 处理器信息
                        },
                        "python_version": sys.version,  # Python完整版本信息
                        "python_executable": sys.executable,  # Python可执行文件路径
                        "current_working_directory": os.getcwd(),  # 当前工作目录
                        "environment_variables": dict(os.environ),  # 环境变量
                    },
                    # 【硬件信息】系统硬件资源信息
                    "hardware_info": {
                        "cpu": {
                            "logical_count": cpu_count_logical,  # 逻辑CPU核心数
                            "physical_count": cpu_count_physical,  # 物理CPU核心数
                            "usage_per_core": cpu_percent,  # 每个核心的使用率
                            "frequency": {
                                "current": cpu_freq.current
                                if cpu_freq
                                else None,  # 当前频率
                                "min": cpu_freq.min if cpu_freq else None,  # 最小频率
                                "max": cpu_freq.max if cpu_freq else None,  # 最大频率
                            }
                            if cpu_freq
                            else None,
                        },
                        "memory": {
                            "virtual_memory": {
                                "total": memory_info.total,  # 总内存
                                "available": memory_info.available,  # 可用内存
                                "used": memory_info.used,  # 已使用内存
                                "free": memory_info.free,  # 空闲内存
                                "percentage": memory_info.percent,  # 内存使用百分比
                                "buffers": getattr(
                                    memory_info, "buffers", None
                                ),  # 缓冲区内存
                                "cached": getattr(
                                    memory_info, "cached", None
                                ),  # 缓存内存
                                "shared": getattr(
                                    memory_info, "shared", None
                                ),  # 共享内存
                            },
                            "swap_memory": {
                                "total": swap_info.total,  # 交换区内存总量
                                "used": swap_info.used,  # 交换区已使用
                                "free": swap_info.free,  # 交换区剩余
                                "percentage": swap_info.percent,  # 交换区使用百分比
                                "sin": getattr(
                                    swap_info, "sin", None
                                ),  # 从磁盘交换进来的内存
                                "sout": getattr(
                                    swap_info, "sout", None
                                ),  # 交换到磁盘的内存
                            }
                            if swap_info
                            else None,
                        },
                        "disk": {
                            "usage": {
                                "total": disk_usage.total,  # 磁盘总空间
                                "used": disk_usage.used,  # 磁盘已使用空间
                                "free": disk_usage.free,  # 磁盘剩余空间
                                "percentage": disk_usage.percent,  # 磁盘使用百分比
                            },
                            "partitions": [],  # 磁盘分区信息
                        },
                    },
                    # 【网络信息】网络配置信息
                    "network_info": {
                        "interfaces": {},  # 网络接口信息
                        "connections": len(psutil.net_connections()),  # 当前网络连接数
                        "io_counters": {},  # 网络IO统计
                    },
                    # 【进程信息】当前进程相关信息
                    "process_info": {
                        "pid": os.getpid(),  # 当前进程ID
                        "process_count": len(psutil.pids()),  # 系统进程总数
                        "current_process": {},  # 当前进程详细信息
                    },
                    # 【导出摘要】统计信息和导出详情
                    "export_summary": {
                        "total_files_exported": exported_count,  # 成功导出的文件总数
                        "export_folders": [
                            str(folder) for folder in export_folders
                        ],  # 导出的文件夹列表（转换为字符串）
                        "export_location": str(file_path),  # 导出压缩包的完整路径
                    },
                }

                # 添加磁盘分区信息
                try:
                    disk_partitions = psutil.disk_partitions()
                    for partition in disk_partitions:
                        partition_info = {
                            "device": partition.device,
                            "mountpoint": partition.mountpoint,
                            "file_system_type": partition.fstype,
                            "options": partition.opts,
                        }

                        # 获取分区使用情况
                        try:
                            partition_usage = psutil.disk_usage(partition.mountpoint)
                            partition_info["usage"] = {
                                "total": partition_usage.total,
                                "used": partition_usage.used,
                                "free": partition_usage.free,
                                "percentage": partition_usage.percent,
                            }
                        except PermissionError:
                            # 某些分区可能没有访问权限
                            partition_info["usage"] = None

                        system_info["hardware_info"]["disk"]["partitions"].append(
                            partition_info
                        )
                except Exception as e:
                    logger.warning(f"获取磁盘分区信息失败: {e}")

                # 添加网络接口信息
                try:
                    net_if_addrs = psutil.net_if_addrs()
                    for interface_name, interface_addresses in net_if_addrs.items():
                        system_info["network_info"]["interfaces"][interface_name] = []
                        for address in interface_addresses:
                            system_info["network_info"]["interfaces"][
                                interface_name
                            ].append(
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

                # 添加网络统计信息
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

                # 添加当前进程详细信息
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

                    # 获取子进程信息
                    try:
                        children = current_process.children(recursive=True)
                        for child in children:
                            system_info["process_info"]["current_process"][
                                "children"
                            ].append(
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

                # 添加开机时间信息
                try:
                    boot_time = psutil.boot_time()
                    system_info["system_info"]["boot_time"] = boot_time
                    system_info["system_info"]["uptime"] = (
                        datetime.now().timestamp() - boot_time
                    )
                except Exception as e:
                    logger.warning(f"获取开机时间信息失败: {e}")

                # 添加用户信息
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

                # 写入诊断信息文件
                try:
                    zipf.writestr(
                        "diagnostic.json",
                        json.dumps(system_info, ensure_ascii=False, indent=2),
                    )
                except Exception as e:
                    logger.error(f"写入诊断信息文件失败: {e}")

                    # 尝试将所有Path对象转换为字符串
                    class PathEncoder(json.JSONEncoder):
                        def default(self, obj):
                            if isinstance(obj, Path):
                                return str(obj)
                            return super().default(obj)

                    zipf.writestr(
                        "diagnostic.json",
                        json.dumps(
                            system_info, cls=PathEncoder, ensure_ascii=False, indent=2
                        ),
                    )

            # 显示成功消息
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
        logger.error(f"导出诊断数据失败: {e}")
        # 显示错误消息
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


def export_all_data(parent: Optional[QWidget] = None) -> None:
    """导出所有数据到文件

    Args:
        parent: 父窗口组件，用于对话框的模态显示
    """
    try:
        cancelled = False

        def _apply_export_all_warning():
            nonlocal cancelled
            warning_dialog = MessageBox(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "export_warning_title",
                    "name",
                ),
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "export_warning_content",
                    "name",
                ),
                parent,
            )
            warning_dialog.yesButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "export_confirm_button",
                    "name",
                )
            )
            warning_dialog.cancelButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "export_cancel_button",
                    "name",
                )
            )
            if not warning_dialog.exec():
                cancelled = True

        _apply_export_all_warning()
        if cancelled:
            return

        file_path, _ = QFileDialog.getSaveFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "export_all_data"),
            f"SecRandom_{SPECIAL_VERSION}_all_data.zip",
            "ZIP Files (*.zip);;All Files (*)",
        )
        if not file_path:
            return
        import zipfile

        if not file_path.endswith(".zip"):
            file_path += ".zip"
        dirs_to_backup = [
            ("config", Path("config")),
            ("list", Path("app") / "data" / "list"),
            ("Language", Path("app") / "data" / "Language"),
            ("history", Path("app") / "data" / "history"),
            ("logs", Path("logs")),
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
        logger.error(f"导出所有数据失败: {e}")
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


def import_all_data(parent: Optional[QWidget] = None) -> None:
    """从文件导入所有数据

    Args:
        parent: 父窗口组件，用于对话框的模态显示
    """
    try:
        # 打开文件选择对话框
        file_path, _ = QFileDialog.getOpenFileName(
            parent,
            get_content_pushbutton_name_async("basic_settings", "import_all_data"),
            "",
            "ZIP Files (*.zip);;All Files (*)",
        )

        if file_path:
            import zipfile

            # 检查版本信息
            version_info = {}
            try:
                with zipfile.ZipFile(file_path, "r") as zipf:
                    if "version.json" in zipf.namelist():
                        with zipf.open("version.json") as vf:
                            version_info = json.load(vf)
            except Exception as e:
                logger.warning(f"读取版本信息失败: {e}")

            # 检查版本兼容性
            if version_info:
                software_name = version_info.get("software_name", "")
                version = version_info.get("version", "")
                current_version = SPECIAL_VERSION
                if software_name != "SecRandom" or version != current_version:
                    _mismatch_cancelled = False

                    def _apply_version_mismatch():
                        nonlocal _mismatch_cancelled
                        warning_dialog = MessageBox(
                            get_any_position_value_async(
                                "basic_settings",
                                "data_import_export",
                                "version_mismatch_title",
                                "name",
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
                                "basic_settings",
                                "data_import_export",
                                "import_confirm_button",
                                "name",
                            )
                        )
                        warning_dialog.cancelButton.setText(
                            get_any_position_value_async(
                                "basic_settings",
                                "data_import_export",
                                "import_cancel_button",
                                "name",
                            )
                        )
                        if not warning_dialog.exec():
                            _mismatch_cancelled = True

                    require_and_run(
                        "import_version_mismatch", parent, _apply_version_mismatch
                    )
                    if _mismatch_cancelled:
                        return

            # 检查zip文件中的内容
            existing_files = []
            with zipfile.ZipFile(file_path, "r") as zipf:
                for member in zipf.namelist():
                    # 跳过版本信息文件
                    if member == "version.json":
                        continue

                    # 解析目录结构
                    parts = Path(member).parts
                    if len(parts) > 1:
                        dir_name = parts[0]
                        relative_path = Path(*parts[1:])

                        # 映射目录到实际路径
                        target_dirs = {
                            "config": Path("config"),
                            "list": Path("data/list"),
                            "Language": Path("data/Language"),
                            "history": Path("data/history"),
                            "logs": Path("logs"),
                        }

                        if dir_name in target_dirs:
                            target_path = target_dirs[dir_name] / relative_path
                            if target_path.exists():
                                existing_files.append(str(target_path))

            # 如果有已存在的文件，询问用户是否覆盖
            if existing_files:
                _overwrite_cancelled = False

                def _apply_overwrite():
                    nonlocal _overwrite_cancelled
                    files_list = "\n".join(existing_files[:10])
                    if len(existing_files) > 10:
                        files_list += get_any_position_value_async(
                            "basic_settings",
                            "data_import_export",
                            "existing_files_count",
                            "name",
                        ).format(len=len(existing_files) - 10)
                    dialog = MessageBox(
                        get_any_position_value_async(
                            "basic_settings",
                            "data_import_export",
                            "existing_files_title",
                            "name",
                        ),
                        get_any_position_value_async(
                            "basic_settings",
                            "data_import_export",
                            "existing_files_content",
                            "name",
                        ).format(files=files_list),
                        parent,
                    )
                    dialog.yesButton.setText(
                        get_any_position_value_async(
                            "basic_settings",
                            "data_import_export",
                            "import_confirm_button",
                            "name",
                        )
                    )
                    dialog.cancelButton.setText(
                        get_any_position_value_async(
                            "basic_settings",
                            "data_import_export",
                            "import_cancel_button",
                            "name",
                        )
                    )
                    if not dialog.exec():
                        _overwrite_cancelled = True

                require_and_run("import_overwrite", parent, _apply_overwrite)
                if _overwrite_cancelled:
                    return

            # 显示导入确认对话框
            confirm_dialog = MessageBox(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "import_confirm_title",
                    "name",
                ),
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "import_confirm_content",
                    "name",
                ),
                parent,
            )
            confirm_dialog.yesButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "import_confirm_button",
                    "name",
                )
            )
            confirm_dialog.cancelButton.setText(
                get_any_position_value_async(
                    "basic_settings",
                    "data_import_export",
                    "import_cancel_button",
                    "name",
                )
            )

            if confirm_dialog.exec():
                # 解压文件
                with zipfile.ZipFile(file_path, "r") as zipf:
                    # 映射目录到实际路径
                    target_dirs = {
                        "config": Path("config"),
                        "list": Path("data/list"),
                        "Language": Path("data/Language"),
                        "history": Path("data/history"),
                        "logs": Path("logs"),
                    }

                    for member in zipf.namelist():
                        # 跳过版本信息文件
                        if member == "version.json":
                            continue

                        parts = Path(member).parts
                        if len(parts) > 1:
                            dir_name = parts[0]
                            relative_path = Path(*parts[1:])

                            if dir_name in target_dirs:
                                target_path = target_dirs[dir_name] / relative_path
                                # 确保目标目录存在
                                target_path.parent.mkdir(parents=True, exist_ok=True)
                                # 提取文件
                                with (
                                    zipf.open(member) as source,
                                    open(target_path, "wb") as target,
                                ):
                                    shutil.copyfileobj(source, target)

                # 显示成功消息
                success_dialog = MessageBox(
                    get_any_position_value_async(
                        "basic_settings",
                        "data_import_export",
                        "import_success_title",
                        "name",
                    ),
                    get_any_position_value_async(
                        "basic_settings",
                        "data_import_export",
                        "import_success_content",
                        "name",
                    ),
                    parent,
                )
                success_dialog.yesButton.setText(
                    get_any_position_value_async(
                        "basic_settings",
                        "data_import_export",
                        "import_success_button",
                        "name",
                    )
                )
                success_dialog.cancelButton.hide()
                success_dialog.buttonLayout.insertStretch(1)
                success_dialog.exec()

    except Exception as e:
        logger.error(f"导入所有数据失败: {e}")
        # 显示错误消息
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


def _get_operating_system() -> str:
    """获取操作系统信息，特别优化Win11识别"""
    if sys.platform == "win32":
        try:
            # 尝试获取更详细的Windows版本信息
            version = platform.version()
            release = platform.release()

            # 检查是否为Windows 11 (build 22000及以上)
            if release == "10":  # Windows 10/11都显示为release 10
                build_number = int(version.split(".")[2])
                if build_number >= 22000:
                    return f"Windows 11 (Build {build_number})"

            return f"Windows {release} (Build {version})"
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


# ======= 获取清除记录前缀 =======
def check_clear_record():
    """检查是否需要清除已抽取记录"""
    clear_record = readme_settings_async("roll_call_settings", "clear_record")
    if clear_record == 0:  # 重启后清除
        prefix = "all"
    elif clear_record == 1:  # 直至全部抽取完
        prefix = "until"
    else:  # 不清除记录或其他情况
        prefix = None
    return prefix


# ======= 记录已抽取的学生 =======
def record_drawn_student(class_name: str, gender: str, group: str, student_name):
    """记录已抽取的学生名称和次数

    Args:
        class_name: 班级名称
        gender: 性别
        group: 分组
        student_name: 学生名称或学生列表
    """
    # 构建文件路径，与remove_record保持一致
    file_path = get_data_path("TEMP", f"draw_until_{class_name}_{gender}_{group}.json")

    # 确保目录存在
    os.makedirs(os.path.dirname(file_path), exist_ok=True)

    # 读取现有记录
    drawn_records = _load_drawn_records(file_path)

    # 提取学生名称列表
    students_to_add = _extract_student_names(student_name)

    # 更新学生抽取次数
    updated_students = []
    for name in students_to_add:
        if name in drawn_records:
            # 学生已存在，增加抽取次数
            drawn_records[name] += 1
            updated_students.append(f"{name}(第{drawn_records[name]}次)")
        else:
            # 新学生，初始化抽取次数为1
            drawn_records[name] = 1
            updated_students.append(f"{name}(第1次)")

    # 保存更新后的记录
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

        # 处理不同的数据结构
        if isinstance(data, dict):
            # 新格式：字典，键为学生名称，值为抽取次数
            for name, count in data.items():
                if isinstance(name, str) and isinstance(count, int):
                    drawn_records[name] = count
        elif isinstance(data, list):
            # 旧格式：列表，只包含学生名称
            for item in data:
                if isinstance(item, str):
                    # 兼容旧格式，初始化抽取次数为1
                    drawn_records[item] = 1
                elif isinstance(item, dict) and "name" in item:
                    # 兼容可能的字典格式
                    name = item["name"]
                    count = item.get("count", 1)  # 默认次数为1
                    if isinstance(name, str) and isinstance(count, int):
                        drawn_records[name] = count
        elif isinstance(data, dict) and "drawn_names" in data:
            # 兼容可能的字典格式
            for item in data["drawn_names"]:
                if isinstance(item, str):
                    drawn_records[item] = 1
                elif isinstance(item, dict) and "name" in item:
                    name = item["name"]
                    count = item.get("count", 1)
                    if isinstance(name, str) and isinstance(count, int):
                        drawn_records[name] = count

        return drawn_records
    except (json.JSONDecodeError, IOError) as e:
        logger.error(f"读取已抽取记录失败: {e}")
        return {}


def _extract_student_names(student_name) -> list:
    """从不同类型的学生名称参数中提取学生名称列表

    Args:
        student_name: 学生名称或学生列表

    Returns:
        学生名称列表
    """
    if isinstance(student_name, str):
        # 单个学生名称
        return [student_name]

    if isinstance(student_name, list):
        # 学生列表，可能是元组列表或字符串列表
        names = []
        for item in student_name:
            if isinstance(item, str):
                names.append(item)
            elif isinstance(item, tuple) and len(item) >= 2:
                names.append(item[1])
        return names

    if isinstance(student_name, tuple) and len(student_name) >= 2:
        # 单个元组，提取第二个元素（名称）
        return [student_name[1]]

    return []


def _save_drawn_records(file_path: str, drawn_records: dict) -> None:
    """保存已抽取的学生记录到文件

    Args:
        file_path: 记录文件路径
        drawn_records: 已抽取的学生记录字典，键为学生名称，值为抽取次数
    """
    try:
        with open(file_path, "w", encoding="utf-8") as file:
            json.dump(drawn_records, file, ensure_ascii=False, indent=2)
    except IOError as e:
        logger.error(f"保存已抽取记录失败: {e}")


# ======= 读取已抽取记录 =======
def read_drawn_record(class_name: str, gender: str, group: str):
    """读取已抽取记录"""
    file_path = get_data_path("TEMP", f"draw_until_{class_name}_{gender}_{group}.json")
    if os.path.exists(file_path):
        try:
            with open(file_path, "r", encoding="utf-8") as file:
                data = json.load(file)

            # 处理不同的数据结构
            if isinstance(data, dict):
                # 转换为列表格式，每个元素为(名称, 次数)元组
                drawn_records = [(name, count) for name, count in data.items()]
            elif isinstance(data, list):
                # 转换为列表格式，每个元素为(名称, 1)元组
                drawn_records = []
                for item in data:
                    if isinstance(item, str):
                        drawn_records.append((item, 1))
                    elif isinstance(item, dict) and "name" in item:
                        name = item["name"]
                        count = item.get("count", 1)
                        drawn_records.append((name, count))
            elif isinstance(data, dict) and "drawn_names" in data:
                # 兼容可能的字典格式
                drawn_records = []
                for item in data["drawn_names"]:
                    if isinstance(item, str):
                        drawn_records.append((item, 1))
                    elif isinstance(item, dict) and "name" in item:
                        name = item["name"]
                        count = item.get("count", 1)
                        drawn_records.append((name, count))
            else:
                drawn_records = []

            logger.debug(f"已读取{class_name}_{gender}_{group}已抽取记录")
            return drawn_records
        except (json.JSONDecodeError, IOError) as e:
            logger.error(f"读取已抽取记录失败: {e}")
            return []
    else:
        logger.debug(f"文件 {file_path} 不存在")
        return []


# ======= 重置已抽取记录 =======
def remove_record(class_name: str, gender: str, group: str, _prefix: str = "0"):
    """清除已抽取记录"""
    prefix = check_clear_record()
    if prefix == "all" and _prefix == "restart":
        prefix = "restart"

    logger.debug(f"清除记录前缀: {prefix}, _prefix: {_prefix}")

    if prefix == "all":
        # 构建搜索模式，匹配所有前缀的文件夹
        search_pattern = os.path.join(
            "data", "TEMP", f"draw_*_{class_name}_{gender}_{group}.json"
        )

        # 查找所有匹配的文件
        file_list = glob.glob(search_pattern)

        # 删除找到的文件
        for file_path in file_list:
            try:
                os.remove(file_path)
                file_name = os.path.basename(os.path.dirname(file_path))
                logger.info(f"已删除记录文件夹: {file_name}")
            except OSError as e:
                logger.error(f"删除文件{file_path}失败: {e}")
    elif prefix == "until":
        # 只删除特定前缀的文件
        file_path = get_data_path(
            "TEMP", f"draw_{prefix}_{class_name}_{gender}_{group}.json"
        )
        try:
            if os.path.exists(file_path):
                os.remove(file_path)
                file_name = os.path.basename(os.path.dirname(file_path))
                logger.info(f"已删除记录文件夹: {file_name}")
        except OSError as e:
            logger.error(f"删除文件{file_path}失败: {e}")
    elif prefix == "restart":  # 重启后清除
        # 构建搜索模式，匹配所有前缀的文件夹
        search_pattern = os.path.join("data", "TEMP", "draw_*.json")
        # 查找所有匹配的文件
        file_list = glob.glob(search_pattern)
        # 删除找到的文件
        for file_path in file_list:
            try:
                os.remove(file_path)
                file_name = os.path.basename(os.path.dirname(file_path))
                logger.info(f"已删除记录文件夹: {file_name}")
            except OSError as e:
                logger.error(f"删除文件{file_path}失败: {e}")


def reset_drawn_record(self, class_name: str, gender: str, group: str):
    """删除已抽取记录文件"""
    clear_record = readme_settings_async("roll_call_settings", "clear_record")
    if clear_record in [0, 1]:  # 重启后清除、直至全部抽取完
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
    else:  # 重复抽取
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


# ======= 计算剩余人数 =======
def calculate_remaining_count(
    half_repeat: int,
    class_name: str,
    gender_filter: str,
    group_index: int,
    group_filter: str,
    total_count: int,
):
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
    # 根据half_repeat设置计算实际剩余人数
    if half_repeat > 0:  # 只有当设置值大于0时才计算排除后的剩余人数
        # 读取已抽取记录
        drawn_records = read_drawn_record(class_name, gender_filter, group_filter)
        # 将记录转换为字典格式，便于快速查找
        drawn_counts = {}
        for record in drawn_records:
            if isinstance(record, tuple) and len(record) >= 2:
                # 处理元组格式：(名称, 次数)
                name, count = record[0], record[1]
                drawn_counts[name] = count
            elif isinstance(record, dict) and "name" in record:
                # 处理字典格式：{'name': 名称, 'count': 次数}
                name = record["name"]
                count = record.get("count", 1)
                drawn_counts[name] = count

        # 处理小组模式
        if group_index == 1:  # 全部小组
            # 获取所有小组列表
            group_list = get_group_list(class_name)

            # 计算已被排除的小组数量
            excluded_count = 0
            for group_name in group_list:
                # 如果小组已被抽取次数达到或超过设置值，则计入排除数量
                if (
                    group_name in drawn_counts
                    and drawn_counts[group_name] >= half_repeat
                ):
                    excluded_count += 1

            # 计算实际剩余组数
            return max(0, len(group_list) - excluded_count)
        else:
            # 处理学生模式
            # 计算已被排除的学生数量
            excluded_count = 0
            # 获取当前班级的学生列表
            student_list = get_student_list(class_name)
            for student in student_list:
                # 从学生字典中提取姓名
                student_name = (
                    student["name"]
                    if isinstance(student, dict) and "name" in student
                    else student
                )

                # 如果学生已被抽取次数达到或超过设置值，则计入排除数量
                if (
                    student_name in drawn_counts
                    and drawn_counts[student_name] >= half_repeat
                ):
                    excluded_count += 1

            # 计算实际剩余人数
            return max(0, total_count - excluded_count)
    else:
        # 如果half_repeat为0，则不排除任何学生或小组
        return total_count


# ======= 奖池抽取记录 =======
def record_drawn_prize(pool_name: str, prize_names):
    file_path = get_data_path("TEMP", f"draw_until_prize_{pool_name}.json")
    os.makedirs(os.path.dirname(file_path), exist_ok=True)
    drawn_records = _load_drawn_records(file_path)
    names = _extract_student_names(prize_names)
    for name in names:
        if name in drawn_records:
            drawn_records[name] += 1
        else:
            drawn_records[name] = 1
    _save_drawn_records(file_path, drawn_records)


def read_drawn_record_simple(pool_name: str):
    file_path = get_data_path("TEMP", f"draw_until_prize_{pool_name}.json")
    if os.path.exists(file_path):
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
            logger.error(f"读取奖池已抽取记录失败: {e}")
            return []
    return []


def reset_drawn_prize_record(self, pool_name: str):
    try:
        pattern = os.path.join("data", "TEMP", f"draw_*_prize_{pool_name}.json")
        for fp in glob.glob(pattern):
            try:
                os.remove(fp)
            except OSError as e:
                logger.error(f"删除文件{fp}失败: {e}")
        show_notification(
            NotificationType.INFO,
            NotificationConfig(
                title="提示",
                content=f"已重置{pool_name}奖池抽取记录",
                icon=FluentIcon.INFO,
            ),
            parent=self,
        )
    except Exception as e:
        logger.error(f"重置奖池抽取记录失败: {e}")


def set_autostart(enabled: bool) -> bool:
    try:
        if sys.platform == "win32":
            import winreg

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
        elif sys.platform.startswith("linux"):
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
        else:
            return False
    except Exception as e:
        logger.error(f"设置开机自启动失败: {e}")
        return False
