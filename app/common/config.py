# ==================================================
# 导入模块
# ==================================================
import sys
from qfluentwidgets import *
from PySide6.QtGui import *
from PySide6.QtWidgets import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *

from loguru import logger

# 平台特定导入
if sys.platform == "win32":
    try:
        from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume
        import comtypes
        from comtypes import POINTER
    except ImportError:
        pass  # Windows libraries not available
elif sys.platform.startswith("linux"):
    try:
        import pulsectl
    except ImportError:
        pulsectl = None

from app.tools.variable import *
from app.tools.path_utils import *


# ==================================================
# 系统功能相关函数
# ==================================================
def restore_volume(volume_value):
    """跨平台音量恢复函数

    Args:
        volume_value (int): 音量值 (0-100)
    """
    if sys.platform == "win32":
        # Windows音频控制
        try:
            # 初始化COM库
            comtypes.CoInitialize()

            # 获取默认音频设备
            devices = AudioUtilities.GetSpeakers()
            interface = devices.Activate(
                IAudioEndpointVolume._iid_, comtypes.CLSCTX_ALL, None
            )
            volume = comtypes.cast(interface, POINTER(IAudioEndpointVolume))

            try:
                # 取消静音
                volume.SetMute(0, None)

                # 设置音量
                volume.SetMasterVolumeLevelScalar(volume_value / 100.0, None)
                logger.info(f"Windows音量设置为: {volume_value}%")
            finally:
                # 确保COM对象在释放COM库前被正确释放
                # 通过将对象设置为None来减少引用计数
                volume = None
                interface = None
                devices = None

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
