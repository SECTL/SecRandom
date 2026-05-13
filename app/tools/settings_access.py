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
import uuid
import copy
import threading
from loguru import logger
from typing import Any

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.settings_default import *


_UNSET = object()
_settings_cache_lock = threading.RLock()
_settings_cache_data = None
_settings_cache_signature = None


def _get_file_signature(path):
    try:
        stat = path.stat()
        return (stat.st_mtime_ns, stat.st_size)
    except Exception:
        return None


def _read_settings_data():
    """读取设置文件并按文件签名缓存，避免高频重复 JSON I/O。"""
    global _settings_cache_data, _settings_cache_signature

    settings_path = get_settings_path()
    signature = _get_file_signature(settings_path)
    if signature is None:
        with _settings_cache_lock:
            _settings_cache_data = None
            _settings_cache_signature = None
        return {}

    with _settings_cache_lock:
        if _settings_cache_signature == signature and isinstance(
            _settings_cache_data, dict
        ):
            return _settings_cache_data

        try:
            with open_file(settings_path, "r", encoding="utf-8") as f:
                content = f.read()
            if not content or not content.strip():
                logger.warning(f"设置文件为空: {settings_path}")
                data = {}
            else:
                data = json.loads(content)
                if not isinstance(data, dict):
                    data = {}
        except Exception as e:
            logger.warning(f"读取设置缓存失败: {e}")
            data = {}

        _settings_cache_data = data
        _settings_cache_signature = signature
        return data


def _replace_settings_cache(settings_data):
    global _settings_cache_data, _settings_cache_signature

    settings_path = get_settings_path()
    with _settings_cache_lock:
        _settings_cache_data = settings_data if isinstance(settings_data, dict) else {}
        _settings_cache_signature = _get_file_signature(settings_path)


def clear_settings_cache():
    """清空设置缓存，供外部批量导入/重建设置文件后调用。"""
    global _settings_cache_data, _settings_cache_signature

    with _settings_cache_lock:
        _settings_cache_data = None
        _settings_cache_signature = None


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
            logger.exception(f"读取设置失败: {e}")
            default_value = self._get_default_value()
            self.finished.emit(default_value)

    def _read_setting_value(self):
        """从设置文件或默认设置中读取值"""
        settings_data = _read_settings_data()
        if (
            self.first_level_key in settings_data
            and self.second_level_key in settings_data[self.first_level_key]
        ):
            return copy.deepcopy(
                settings_data[self.first_level_key][self.second_level_key]
            )
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


def get_setting(first_level_key: str, second_level_key: str, default=_UNSET):
    """同步读取设置。

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键
        default: 设置文件和默认设置中都不存在该项时使用的回退值

    Returns:
        设置值；未找到且未传入 default 时返回 None
    """
    try:
        settings_data = _read_settings_data()
        if (
            first_level_key in settings_data
            and second_level_key in settings_data[first_level_key]
        ):
            value = settings_data[first_level_key][second_level_key]
            # logger.debug(f"从设置文件读取: {first_level_key}.{second_level_key} = {value}")
            return copy.deepcopy(value)

        default_setting = _get_default_setting(first_level_key, second_level_key)
        default_value = default_setting
        if default_value is None and default is not _UNSET:
            default_value = default
        # logger.debug(f"使用默认设置: {first_level_key}.{second_level_key} = {default_value}")
        return copy.deepcopy(default_value)
    except Exception as e:
        logger.warning(f"读取设置失败: {e}")
        default_setting = _get_default_setting(first_level_key, second_level_key)
        if default_setting is None and default is not _UNSET:
            return copy.deepcopy(default)
        return copy.deepcopy(default_setting)


def readme_settings(first_level_key: str, second_level_key: str):
    """读取设置。

    兼容旧接口；新代码请使用 get_setting()，语义更清晰。
    """
    return get_setting(first_level_key, second_level_key)


def readme_settings_async(first_level_key: str, second_level_key: str, default=_UNSET):
    """兼容旧名称的同步设置读取函数。

    该函数不会创建异步任务；历史上名称带有 async，但实际已改为同步读取。
    第三个参数保留为默认值回退，不再表示 timeout。

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键
        default: 设置文件和默认设置中都不存在该项时使用的回退值

    Returns:
        设置值
    """
    return get_setting(first_level_key, second_level_key, default)


def get_bool_setting(
    first_level_key: str, second_level_key: str, default: bool = False
) -> bool:
    """读取布尔设置，并对常见字符串/数字形式做安全转换。"""
    value = get_setting(first_level_key, second_level_key, default)
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in {"1", "true", "yes", "on", "y"}:
            return True
        if normalized in {"0", "false", "no", "off", "n", ""}:
            return False
    logger.warning(
        f"布尔设置值类型无效: {first_level_key}.{second_level_key} = {value} (类型: {type(value)})"
    )
    return default


def get_int_setting(
    first_level_key: str, second_level_key: str, default: int = 0
) -> int:
    """读取整数设置。"""
    value = get_setting(first_level_key, second_level_key, default)
    try:
        if isinstance(value, bool):
            return int(value)
        if isinstance(value, (int, float)):
            return int(value)
        if isinstance(value, str) and value.strip():
            return int(float(value.strip()))
    except (ValueError, TypeError):
        pass
    logger.warning(
        f"整数设置值类型无效: {first_level_key}.{second_level_key} = {value} (类型: {type(value)})"
    )
    return default


def get_float_setting(
    first_level_key: str, second_level_key: str, default: float = 0.0
) -> float:
    """读取浮点数设置。"""
    value = get_setting(first_level_key, second_level_key, default)
    try:
        if isinstance(value, bool):
            return float(value)
        if isinstance(value, (int, float)):
            return float(value)
        if isinstance(value, str) and value.strip():
            return float(value.strip())
    except (ValueError, TypeError):
        pass
    logger.warning(
        f"浮点设置值类型无效: {first_level_key}.{second_level_key} = {value} (类型: {type(value)})"
    )
    return default


def get_str_setting(
    first_level_key: str, second_level_key: str, default: str = ""
) -> str:
    """读取字符串设置。"""
    value = get_setting(first_level_key, second_level_key, default)
    if value is None:
        return default
    return str(value)


class SettingsSignals(QObject):
    """设置变化信号类"""

    settingChanged = Signal(
        str, str, object
    )  # (first_level_key, second_level_key, value)


# 创建全局信号实例
_settings_signals = SettingsSignals()


def get_settings_signals():
    """获取设置信号实例"""
    global _settings_signals
    return _settings_signals


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
        settings_data = copy.deepcopy(_read_settings_data())

        # 更新设置
        if first_level_key not in settings_data:
            settings_data[first_level_key] = {}

        # 直接保存值，不保存嵌套结构
        settings_data[first_level_key][second_level_key] = value

        # 写入设置文件
        with open_file(settings_path, "w", encoding="utf-8") as f:
            json.dump(settings_data, f, ensure_ascii=False, indent=4)
        _replace_settings_cache(settings_data)

        if not (
            first_level_key == "user_info"
            and second_level_key == "total_runtime_seconds"
        ):
            logger.debug(
                f"设置更新成功: {first_level_key}.{second_level_key} = {value}"
            )

        # 发送设置变化信号
        get_settings_signals().settingChanged.emit(
            first_level_key, second_level_key, value
        )
    except Exception as e:
        logger.exception(f"设置更新失败: {e}")


def get_or_create_user_id():
    try:
        user_id = readme_settings("basic_settings", "offline_user_id")
        if isinstance(user_id, str) and user_id.strip():
            try:
                uuid.UUID(user_id)
                return user_id
            except ValueError:
                pass
        user_id = str(uuid.uuid4())
        update_settings("basic_settings", "offline_user_id", user_id)
        return user_id
    except Exception as e:
        logger.exception(f"获取用户ID失败: {e}")
        return str(uuid.uuid4())


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


def get_safe_font_size(
    first_level_key: str, second_level_key: str, default_size: int = 12
) -> int:
    """安全地获取字体大小设置值

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键
        default_size: 默认字体大小

    Returns:
        int: 有效的字体大小值（1-200）
    """
    try:
        # 获取设置值
        font_size = readme_settings(first_level_key, second_level_key)

        # 验证设置值的有效性
        if font_size is None:
            return default_size

        # 尝试转换为整数
        if isinstance(font_size, str):
            if font_size.isdigit():
                font_size = int(font_size)
            else:
                logger.warning(
                    f"字体大小设置值无效（非数字字符串）: {first_level_key}.{second_level_key} = {font_size}"
                )
                return default_size
        elif isinstance(font_size, (int, float)):
            font_size = int(font_size)
        else:
            logger.warning(
                f"字体大小设置值类型无效: {first_level_key}.{second_level_key} = {font_size} (类型: {type(font_size)})"
            )
            return default_size

        # 验证范围
        if font_size <= 0 or font_size > 200:
            logger.warning(
                f"字体大小设置值超出有效范围: {first_level_key}.{second_level_key} = {font_size}"
            )
            return default_size

        return font_size

    except (ValueError, TypeError) as e:
        logger.exception(f"获取字体大小设置失败: {e}")
        return default_size
    except Exception as e:
        logger.exception(f"获取字体大小设置时发生未知错误: {e}")
        return default_size
