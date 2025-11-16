# ==================================================
# 默认内容文本配置
# ==================================================
"""
该模块包含所有应用程序内容文本的默认值配置
使用层级结构组织内容文本项，第一层为分类，第二层为具体内容文本项

便捷函数总结：
1. 同步函数：
   - get_content_name(first_level_key, second_level_key): 获取内容文本项的名称
   - get_content_description(first_level_key, second_level_key): 获取内容文本项的描述
   - get_content_pushbutton_name(first_level_key, second_level_key): 获取内容文本项的按钮名称
   - get_content_switchbutton_name_async(first_level_key, second_level_key, is_enable): 获取内容文本项的开关按钮名称
   - get_content_combo_name_async(first_level_key, second_level_key): 获取内容文本项的下拉框内容
   - get_any_position_value(first_level_key, second_level_key, *keys): 根据层级键获取任意位置的值

2. 异步函数：
   - get_content_name_async(first_level_key, second_level_key, timeout=1000): 异步获取内容文本项的名称
   - get_content_description_async(first_level_key, second_level_key, timeout=1000): 异步获取内容文本项的描述
   - get_content_pushbutton_name_async(first_level_key, second_level_key, timeout=1000): 异步获取内容文本项的按钮名称
   - get_content_switchbutton_name_async(first_level_key, second_level_key, is_enable, timeout=1000): 异步获取内容文本项的开关按钮名称
   - get_content_combo_name_async(first_level_key, second_level_key, timeout=1000): 异步获取内容文本项的下拉框内容
   - get_any_position_value_async(first_level_key, second_level_key, *keys, timeout=1000): 异步根据层级键获取任意位置的值

注意：所有异步函数在失败时会自动回退到对应的同步方法，确保功能稳定性。
"""

from app.tools.language_manager import *
from app.tools.settings_access import *

from loguru import logger

# 获取语言数据
Language = get_current_language_data()


# ==================================================
# 异步语言读取工作线程
# ==================================================
class LanguageReaderWorker(QObject):
    """语言读取工作线程"""

    finished = Signal(object)  # 信号，传递读取结果

    def __init__(
        self,
        first_level_key: str,
        second_level_key: str,
        value_type: str = "name",
        *keys,
    ):
        super().__init__()
        self.first_level_key = first_level_key
        self.second_level_key = second_level_key
        self.value_type = (
            value_type  # 可以是 "name", "description", "pushbutton_name" 等
        )
        self.keys = keys  # 后续任意层级的键

    def run(self):
        """执行语言读取操作"""
        try:
            value = self._read_language_value()
            self.finished.emit(value)
        except Exception as e:
            logger.error(f"读取语言内容失败: {e}")
            self.finished.emit(None)

    def _read_language_value(self):
        """从语言数据中读取值"""
        # 获取最新的语言数据
        language_data = get_current_language_data()

        # 如果是获取任意位置的值
        if self.value_type == "any_position":
            return self._get_any_position_value(language_data)

        # 检查键是否存在
        if self.first_level_key in language_data:
            if self.second_level_key in language_data[self.first_level_key]:
                item_data = language_data[self.first_level_key][self.second_level_key]

                # 根据类型返回不同的值
                if self.value_type == "name":
                    return item_data.get("name")
                elif self.value_type == "description":
                    return item_data.get("description")
                elif self.value_type == "pushbutton_name":
                    return item_data.get("pushbutton_name")
                elif self.value_type == "combo_items":
                    return item_data.get("combo_items")
                elif self.value_type.startswith("switchbutton_name"):
                    return item_data.get("switchbutton_name", {})
                else:
                    return item_data

        return None

    def _get_any_position_value(self, language_data):
        """获取任意位置的值"""
        if self.first_level_key in language_data:
            current = language_data[self.first_level_key]
            if self.second_level_key in current:
                current = current[self.second_level_key]
                for key in self.keys:
                    if isinstance(current, dict) and key in current:
                        current = current[key]
                    else:
                        return None
                return current
        return None


class AsyncLanguageReader(QObject):
    """异步语言读取器，提供简洁的异步读取方式"""

    # 定义信号
    finished = Signal(object)  # 读取完成信号，携带结果
    error = Signal(str)  # 错误信号

    def __init__(
        self,
        first_level_key: str,
        second_level_key: str,
        value_type: str = "name",
        switch_state: str = None,
        *keys,
    ):
        super().__init__()
        self.first_level_key = first_level_key
        self.second_level_key = second_level_key
        self.value_type = value_type
        self.switch_state = switch_state  # 用于开关按钮的状态
        self.keys = keys  # 后续任意层级的键
        self.thread = None
        self.worker = None
        self._result = None
        self._completed = False
        self._future = None

    def read_async(self):
        """异步读取语言内容，返回Future对象"""
        # 创建工作线程
        self.thread = QThread()
        self.worker = LanguageReaderWorker(
            self.first_level_key, self.second_level_key, self.value_type, *self.keys
        )
        self.worker.moveToThread(self.thread)

        # 连接信号
        self.thread.started.connect(self.worker.run)
        self.worker.finished.connect(self._handle_result)
        self.worker.finished.connect(self.worker.deleteLater)
        self.thread.finished.connect(self.thread.deleteLater)

        # 创建Future对象
        self._future = asyncio.Future()

        # 启动线程
        self.thread.start()

        # 返回Future对象
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
        """处理语言读取结果"""
        # 对于开关按钮，需要进一步处理
        if (
            self.value_type.startswith("switchbutton_name")
            and self.switch_state
            and isinstance(value, dict)
        ):
            self._result = value.get(self.switch_state)
        else:
            self._result = value

        self._completed = True

        # 设置Future结果
        if self._future and not self._future.done():
            self._future.set_result(self._result)

        # 发出完成信号
        self.finished.emit(self._result)

        # 安全地清理线程
        self._cleanup_thread()

    def _cleanup_thread(self):
        """安全地清理线程资源"""
        if self.thread and self.thread.isRunning():
            self.thread.quit()
            self.thread.wait(1000)  # 最多等待1秒线程结束


# ==================================================
# 便捷函数
# ==================================================
def get_content_name(first_level_key: str, second_level_key: str):
    """根据键获取内容文本项的名称

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        内容文本项的名称，如果不存在则返回None
    """
    if first_level_key in Language:
        if second_level_key in Language[first_level_key]:
            # logger.debug(f"获取内容文本项: {first_level_key}.{second_level_key}")
            return Language[first_level_key][second_level_key]["name"]
    return None


def get_content_description(first_level_key: str, second_level_key: str):
    """根据键获取内容文本项的描述

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        内容文本项的描述，如果不存在则返回None
    """
    if first_level_key in Language:
        if second_level_key in Language[first_level_key]:
            # logger.debug(f"获取内容文本项描述: {first_level_key}.{second_level_key}")
            return Language[first_level_key][second_level_key]["description"]
    return None


def get_content_pushbutton_name(first_level_key: str, second_level_key: str):
    """根据键获取内容文本项的按钮名称

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        内容文本项的按钮名称，如果不存在则返回None
    """
    if first_level_key in Language:
        if second_level_key in Language[first_level_key]:
            # logger.debug(f"获取内容文本项按钮名称: {first_level_key}.{second_level_key}")
            return Language[first_level_key][second_level_key]["pushbutton_name"]
    return None


def get_content_switchbutton_name(
    first_level_key: str, second_level_key: str, is_enable: str
):
    """根据键获取内容文本项的开关按钮名称

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键
        is_enable: 是否启用开关按钮("enable"或"disable")

    Returns:
        内容文本项的开关按钮名称，如果不存在则返回None
    """
    if first_level_key in Language:
        if second_level_key in Language[first_level_key]:
            # logger.debug(f"获取内容文本项开关按钮名称: {first_level_key}.{second_level_key}")
            return Language[first_level_key][second_level_key]["switchbutton_name"][
                is_enable
            ]
    return None


def get_content_combo_name(first_level_key: str, second_level_key: str):
    """根据键获取内容文本项的下拉框内容

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        内容文本项的下拉框内容，如果不存在则返回None
    """
    if first_level_key in Language:
        if second_level_key in Language[first_level_key]:
            # logger.debug(f"获取内容文本项下拉框内容: {first_level_key}.{second_level_key}")
            return Language[first_level_key][second_level_key]["combo_items"]
    return None


def get_any_position_value(first_level_key: str, second_level_key: str, *keys):
    """根据层级键获取任意位置的值

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键
        *keys: 后续任意层级的键

    Returns:
        指定位置的值，如果不存在则返回None
    """
    if first_level_key in Language:
        current = Language[first_level_key]
        if second_level_key in current:
            current = current[second_level_key]
            for key in keys:
                if isinstance(current, dict) and key in current:
                    # logger.debug(f"获取内容文本项: {first_level_key}.{second_level_key}.{key}")
                    current = current[key]
                else:
                    return None
            return current
    return None


# ==================================================
# 异步版本的语言获取函数
# ==================================================
def get_content_name_async(first_level_key: str, second_level_key: str, timeout=1000):
    """异步获取内容名称（简化版：直接调用同步方法）

    为保持 API 兼容性而保留，但在 Nuitka 环境下 QTimer 有兼容性问题，
    因此直接使用同步方法。实际测试表明同步方法性能已足够好。

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 内容文本项的名称
    """
    return get_content_name(first_level_key, second_level_key)


def get_content_description_async(
    first_level_key: str, second_level_key: str, timeout=1000
):
    """异步获取内容描述（简化版：直接调用同步方法）

    为保持 API 兼容性而保留，但在 Nuitka 环境下 QTimer 有兼容性问题，
    因此直接使用同步方法。

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 内容文本项的描述
    """
    return get_content_description(first_level_key, second_level_key)


def get_content_pushbutton_name_async(
    first_level_key: str, second_level_key: str, timeout=1000
):
    """异步获取按钮名称（简化版：直接调用同步方法）

    为保持 API 兼容性而保留，但在 Nuitka 环境下 QTimer 有兼容性问题，
    因此直接使用同步方法。

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 内容文本项的按钮名称
    """
    return get_content_pushbutton_name(first_level_key, second_level_key)


def get_content_switchbutton_name_async(
    first_level_key: str, second_level_key: str, is_enable: str, timeout=1000
):
    """异步获取开关按钮名称（简化版：直接调用同步方法）

    为保持 API 兼容性而保留，但在 Nuitka 环境下 QTimer 有兼容性问题，
    因此直接使用同步方法。

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        is_enable (str): 是否启用开关按钮("enable"或"disable")
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 内容文本项的开关按钮名称
    """
    return get_content_switchbutton_name(first_level_key, second_level_key, is_enable)


def get_content_combo_name_async(
    first_level_key: str, second_level_key: str, timeout=1000
):
    """异步获取内容文本项的下拉框内容（简化版：直接调用同步方法）

    Nuitka 打包环境下 QTimer 的签名检查会导致 singleShot 抛出异常，因此
    这里直接复用同步版本。保留 timeout 参数以兼容旧调用。

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 内容文本项的下拉框内容
    """
    return get_content_combo_name(first_level_key, second_level_key)


def get_any_position_value_async(
    first_level_key: str, second_level_key: str, *keys, timeout=1000
):
    """异步获取任意层级值（简化版：直接调用同步方法）

    Args:
        first_level_key (str): 第一层的键
        second_level_key (str): 第二层的键
        *keys: 后续任意层级的键
        timeout (int, optional): 保留参数，用于兼容性

    Returns:
        Any: 指定位置的值，如果不存在则返回None
    """
    return get_any_position_value(first_level_key, second_level_key, *keys)
