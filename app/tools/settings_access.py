# ==================================================
# 导入模块
# ==================================================
from qfluentwidgets import *
from PySide6.QtGui import *
from PySide6.QtWidgets import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *

import json
import asyncio
from loguru import logger
from typing import Any

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.settings_default import *


# ==================================================
# 设置访问函数
# ==================================================
class SettingsReaderWorker(QObject):
    """设置读取工作线程"""

    finished = Signal(object)  # 信号，传递读取结果

    def __init__(self, first_level_key: str, second_level_key: str):
        super().__init__()
        self.first_level_key = first_level_key
        self.second_level_key = second_level_key

    def run(self):
        """执行设置读取操作"""
        try:
            value = self._read_setting_value()
            # logger.debug(f"读取设置: {self.first_level_key}.{self.second_level_key} = {value}")
            self.finished.emit(value)
        except Exception as e:
            logger.error(f"读取设置失败: {e}")
            default_value = self._get_default_value()
            self.finished.emit(default_value)

    def _read_setting_value(self):
        """从设置文件或默认设置中读取值"""
        settings_path = get_settings_path()
        if file_exists(settings_path):
            try:
                with open_file(settings_path, "r", encoding="utf-8") as f:
                    settings_data = json.load(f)
                    if (
                        self.first_level_key in settings_data
                        and self.second_level_key in settings_data[self.first_level_key]
                    ):
                        return settings_data[self.first_level_key][
                            self.second_level_key
                        ]
            except (json.JSONDecodeError, KeyError):
                pass
        return self._get_default_value()

    def _get_default_value(self):
        """获取默认设置值"""
        default_setting = _get_default_setting(
            self.first_level_key, self.second_level_key
        )
        return (
            default_setting["default_value"]
            if isinstance(default_setting, dict) and "default_value" in default_setting
            else default_setting
        )


class AsyncSettingsReader(QObject):
    """异步设置读取器，提供简洁的异步读取方式"""

    finished = Signal(object)  # 读取完成信号，携带结果
    error = Signal(str)  # 错误信号

    def __init__(self, first_level_key: str, second_level_key: str):
        super().__init__()
        self.first_level_key = first_level_key
        self.second_level_key = second_level_key
        self.thread = None
        self.worker = None
        self._result = None
        self._completed = False
        self._future = None

    def read_async(self):
        """异步读取设置，返回Future对象"""
        self.thread = QThread()
        self.worker = SettingsReaderWorker(self.first_level_key, self.second_level_key)
        self.worker.moveToThread(self.thread)
        self.thread.started.connect(self.worker.run)
        self.worker.finished.connect(self._handle_result)
        self.worker.finished.connect(self.worker.deleteLater)
        self.thread.finished.connect(self.thread.deleteLater)
        self._future = asyncio.Future()
        self.thread.start()
        return self._future

    def result(self, timeout=None):
        """等待并返回结果，类似Future的result()方法"""
        if self._completed:
            return self._result
        if self.thread and self.thread.isRunning():
            if timeout is not None:
                self.thread.wait(timeout)
            else:
                self.thread.wait()
        return self._result

    def is_done(self):
        """检查是否已完成"""
        return self._completed

    def _handle_result(self, value):
        """处理设置读取结果"""
        self._result = value
        self._completed = True
        if self._future and not self._future.done():
            self._future.set_result(value)
        self.finished.emit(value)
        self._cleanup_thread()

    def _cleanup_thread(self):
        """安全地清理线程资源"""
        if self.thread and self.thread.isRunning():
            self.thread.quit()
            self.thread.wait(1000)


def readme_settings(first_level_key: str, second_level_key: str):
    """读取设置

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        返回设置值
    """
    try:
        settings_path = get_settings_path()
        if file_exists(settings_path):
            with open_file(settings_path, "r", encoding="utf-8") as f:
                settings_data = json.load(f)
                if (
                    first_level_key in settings_data
                    and second_level_key in settings_data[first_level_key]
                ):
                    value = settings_data[first_level_key][second_level_key]
                    # logger.debug(f"从设置文件读取: {first_level_key}.{second_level_key} = {value}")
                    return value

        default_setting = _get_default_setting(first_level_key, second_level_key)
        if isinstance(default_setting, dict) and "default_value" in default_setting:
            default_value = default_setting["default_value"]
        else:
            default_value = default_setting
        # logger.debug(f"使用默认设置: {first_level_key}.{second_level_key} = {default_value}")
        return default_value
    except Exception as e:
        logger.error(f"读取设置失败: {e}")
        default_setting = _get_default_setting(first_level_key, second_level_key)
        if isinstance(default_setting, dict) and "default_value" in default_setting:
            return default_setting["default_value"]
        return default_setting


def readme_settings_async(first_level_key: str, second_level_key: str, timeout=1000):
    """异步读取设置（简化版：直接调用同步方法）

    为保持 API 兼容性而保留，但在 Nuitka 环境下 QTimer 有兼容性问题，
    因此直接使用同步方法。实际测试表明同步方法性能已足够好。

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 设置值
    """
    return readme_settings(first_level_key, second_level_key)


def update_settings(first_level_key: str, second_level_key: str, value: Any):
    """更新设置

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键
        value: 要写入的值（可以是任何类型）

    Returns:
        bool: 更新是否成功
    """
    try:
        # 获取设置文件路径
        settings_path = get_settings_path()

        # 确保设置目录存在
        ensure_dir(settings_path.parent)

        # 读取现有设置
        settings_data = {}
        if file_exists(settings_path):
            with open_file(settings_path, "r", encoding="utf-8") as f:
                settings_data = json.load(f)

        # 更新设置
        if first_level_key not in settings_data:
            settings_data[first_level_key] = {}

        # 直接保存值，不保存嵌套结构
        settings_data[first_level_key][second_level_key] = value

        # 写入设置文件
        with open_file(settings_path, "w", encoding="utf-8") as f:
            json.dump(settings_data, f, ensure_ascii=False, indent=4)

        logger.debug(f"设置更新成功: {first_level_key}.{second_level_key} = {value}")
    except Exception as e:
        logger.error(f"设置更新失败: {e}")


def _get_default_setting(first_level_key: str, second_level_key: str):
    """获取默认设置值

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        默认设置值
    """
    # 从settings_default模块获取默认值
    default_settings = get_default_settings()

    # 检查设置是否存在
    if first_level_key in default_settings:
        if second_level_key in default_settings[first_level_key]:
            setting_info = default_settings[first_level_key][second_level_key]
            # 如果是嵌套结构，提取 default_value
            if isinstance(setting_info, dict) and "default_value" in setting_info:
                return setting_info["default_value"]
            # 否则直接返回值
            return setting_info

    return None
