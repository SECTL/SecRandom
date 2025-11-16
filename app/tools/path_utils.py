# ====================== 1. 基础路径操作 ======================
# - get_path()       - 获取绝对路径
# - ensure_dir()     - 确保目录存在
# - get_app_root()   - 获取应用程序根目录

# ====================== 2. 文件操作便捷函数 ======================
# - file_exists()    - 检查文件是否存在
# - open_file()      - 打开文件
# - remove_file()    - 删除文件

# ====================== 3. 特定路径获取便捷函数 ======================
# - get_settings_path() - 获取设置文件路径
# - get_resources_path() - 获取资源文件路径
# - get_config_path()   - 获取配置文件路径
# - get_temp_path()     - 获取临时文件路径
# - get_audio_path()    - 获取音频文件路径
# - get_font_path()     - 获取字体文件路径

# ==================================================
# 导入模块
# ==================================================
import sys
from pathlib import Path
from typing import Union
from loguru import logger

from app.tools.variable import *


# ==================================================
# 路径管理器类
# ==================================================
class PathManager:
    """路径管理器 - 统一管理应用程序中的所有路径"""

    def __init__(self):
        """初始化路径管理器"""
        self._app_root = self._get_app_root()
        logger.debug(f"应用程序根目录: {self._app_root}")

    def _get_app_root(self) -> Path:
        """获取应用程序根目录

        Returns:
            Path: 应用程序根目录路径
        """
        if getattr(sys, "frozen", False):
            # 打包后的可执行文件
            # PyInstaller 会设置 sys._MEIPASS 指向临时解压目录
            if hasattr(sys, "_MEIPASS"):
                return Path(sys._MEIPASS)
            # Nuitka 打包的可执行文件，使用可执行文件所在目录
            else:
                return Path(sys.executable).parent
        else:
            # 开发环境
            return Path(__file__).parent.parent.parent

    def get_absolute_path(self, relative_path: Union[str, Path]) -> Path:
        """将相对路径转换为绝对路径

        Args:
            relative_path: 相对于app目录的路径，如 'app/config/file.json'

        Returns:
            Path: 绝对路径
        """
        if isinstance(relative_path, str):
            relative_path = Path(relative_path)

        # 如果已经是绝对路径，直接返回
        if relative_path.is_absolute():
            return relative_path

        # 拼接为绝对路径
        absolute_path = self._app_root / relative_path
        return absolute_path

    def ensure_directory_exists(self, path: Union[str, Path]) -> Path:
        """确保目录存在，如果不存在则创建

        Args:
            path: 目录路径（相对或绝对）

        Returns:
            Path: 绝对路径

        Raises:
            FileExistsError: 如果路径已存在且为文件
        """
        absolute_path = self.get_absolute_path(path)
        # 检查路径是否已存在且为文件，如果是文件则抛出错误
        if absolute_path.exists() and absolute_path.is_file():
            raise FileExistsError(f"路径已存在且为文件: {absolute_path}")
        absolute_path.mkdir(parents=True, exist_ok=True)
        return absolute_path


# ==================================================
# 路径获取相关函数
# ==================================================
class PathGetter:
    """路径获取器 - 提供各类特定路径的获取方法"""

    def __init__(self, path_manager: PathManager):
        """初始化路径获取器

        Args:
            path_manager: 路径管理器实例
        """
        self._path_manager = path_manager

    def get_settings_path(self, filename: str = DEFAULT_SETTINGS_FILENAME) -> Path:
        """获取设置文件路径

        Args:
            filename: 设置文件名，默认为DEFAULT_SETTINGS_FILENAME

        Returns:
            Path: 设置文件的绝对路径
        """
        return self._path_manager.get_absolute_path(f"config/{filename}")

    def get_resources_path(self, resource_type: str, filename: str = "") -> Path:
        """获取资源文件路径

        Args:
            resource_type: 资源类型，如 'assets' 'icon'等
            filename: 文件名

        Returns:
            Path: 资源文件的绝对路径
        """
        if filename:
            return self._path_manager.get_absolute_path(
                f"app/resources/{resource_type}/{filename}"
            )
        else:
            return self._path_manager.get_absolute_path(
                f"app/resources/{resource_type}"
            )

    def get_config_path(self, config_type: str, filename: str = "") -> Path:
        """获取配置文件路径

        Args:
            config_type: 配置类型，如 'reward', 'list'等
            filename: 文件名

        Returns:
            Path: 配置文件的绝对路径
        """
        if filename:
            return self._path_manager.get_absolute_path(
                f"app/config/{config_type}/{filename}"
            )
        else:
            return self._path_manager.get_absolute_path(f"app/config/{config_type}")

    def get_temp_path(self, filename: str = "") -> Path:
        """获取临时文件路径

        Args:
            filename: 临时文件名

        Returns:
            Path: 临时文件的绝对路径
        """
        if filename:
            return self._path_manager.get_absolute_path(f"app/Temp/{filename}")
        else:
            return self._path_manager.get_absolute_path("app/Temp")

    def get_audio_path(self, filename: str) -> Path:
        """获取音频文件路径

        Args:
            filename: 音频文件名

        Returns:
            Path: 音频文件的绝对路径
        """
        return self._path_manager.get_absolute_path(f"app/resources/audio/{filename}")

    def get_font_path(self, filename: str = DEFAULT_FONT_FILENAME_PRIMARY) -> Path:
        """获取字体文件路径

        Args:
            filename: 字体文件名，默认为DEFAULT_FONT_FILENAME_PRIMARY

        Returns:
            Path: 字体文件的绝对路径
        """
        return self._path_manager.get_absolute_path(f"app/resources/font/{filename}")


# ==================================================
# 文件操作相关函数
# ==================================================
class FileOperations:
    """文件操作器 - 提供文件相关的操作方法"""

    def __init__(self, path_manager: PathManager):
        """初始化文件操作器

        Args:
            path_manager: 路径管理器实例
        """
        self._path_manager = path_manager

    def file_exists(self, path: Union[str, Path]) -> bool:
        """检查文件是否存在

        Args:
            path: 文件路径（相对或绝对）

        Returns:
            bool: 文件是否存在
        """
        absolute_path = self._path_manager.get_absolute_path(path)
        return absolute_path.exists()

    def open_file(
        self,
        path: Union[str, Path],
        mode: str = "r",
        encoding: str = DEFAULT_FILE_ENCODING,
    ):
        """打开文件

        Args:
            path: 文件路径（相对或绝对）
            mode: 文件打开模式
            encoding: 文件编码，默认为DEFAULT_FILE_ENCODING

        Returns:
            文件对象
        """
        absolute_path = self._path_manager.get_absolute_path(path)
        # 二进制模式下不传递encoding参数
        if "b" in mode:
            return open(absolute_path, mode)
        return open(absolute_path, mode, encoding=encoding)

    def remove_file(self, path: Union[str, Path]) -> bool:
        """删除文件

        Args:
            path: 文件路径（相对或绝对）

        Returns:
            bool: 删除是否成功
        """
        try:
            absolute_path = self._path_manager.get_absolute_path(path)
            if absolute_path.exists():
                absolute_path.unlink()
                return True
            return False
        except Exception as e:
            logger.error(f"删除文件失败: {path}, 错误: {e}")
            return False


# ==================================================
# 全局实例和便捷函数
# ==================================================
# 创建全局路径管理器实例
path_manager = PathManager()

# 创建路径获取器和文件操作器实例
path_getter = PathGetter(path_manager)
file_operations = FileOperations(path_manager)


# ==================================================
# 路径处理便捷函数列表
# ==================================================
# 1. 基础路径操作
def get_path(relative_path: Union[str, Path]) -> Path:
    """获取绝对路径的便捷函数

    Args:
        relative_path: 相对路径

    Returns:
        Path: 绝对路径
    """
    return path_manager.get_absolute_path(relative_path)


def ensure_dir(path: Union[str, Path]) -> Path:
    """确保目录存在的便捷函数

    Args:
        path: 目录路径

    Returns:
        Path: 绝对路径
    """
    return path_manager.ensure_directory_exists(path)


def get_app_root() -> Path:
    """获取应用程序根目录的便捷函数

    Returns:
        Path: 应用程序根目录路径
    """
    return path_manager._app_root


# 2. 文件操作便捷函数
def file_exists(path: Union[str, Path]) -> bool:
    """检查文件是否存在的便捷函数

    Args:
        path: 文件路径

    Returns:
        bool: 文件是否存在
    """
    return file_operations.file_exists(path)


def open_file(
    path: Union[str, Path], mode: str = "r", encoding: str = DEFAULT_FILE_ENCODING
):
    """打开文件的便捷函数

    Args:
        path: 文件路径
        mode: 文件打开模式
        encoding: 文件编码，默认为DEFAULT_FILE_ENCODING

    Returns:
        文件对象
    """
    return file_operations.open_file(path, mode, encoding)


def remove_file(path: Union[str, Path]) -> bool:
    """删除文件的便捷函数

    Args:
        path: 文件路径

    Returns:
        bool: 删除是否成功
    """
    return file_operations.remove_file(path)


# 3. 特定路径获取便捷函数
def get_settings_path(filename: str = DEFAULT_SETTINGS_FILENAME) -> Path:
    """获取设置文件路径的便捷函数

    Args:
        filename: 设置文件名，默认为DEFAULT_SETTINGS_FILENAME

    Returns:
        Path: 设置文件的绝对路径
    """
    return path_getter.get_settings_path(filename)


def get_resources_path(config_type: str, filename: str = "") -> Path:
    """获取资源文件路径的便捷函数

    Args:
        config_type: 资源类型，如 'assets', 'icon'等
        filename: 文件名

    Returns:
        Path: 资源文件的绝对路径
    """
    return path_getter.get_resources_path(config_type, filename)


def get_config_path(config_type: str, filename: str = "") -> Path:
    """获取配置文件路径的便捷函数

    Args:
        config_type: 配置类型，如 'reward', 'list'等
        filename: 文件名

    Returns:
        Path: 配置文件的绝对路径
    """
    return path_getter.get_config_path(config_type, filename)


def get_temp_path(filename: str = "") -> Path:
    """获取临时文件路径的便捷函数

    Args:
        filename: 临时文件名

    Returns:
        Path: 临时文件的绝对路径
    """
    return path_getter.get_temp_path(filename)


def get_audio_path(filename: str) -> Path:
    """获取音频文件路径的便捷函数

    Args:
        filename: 音频文件名

    Returns:
        Path: 音频文件的绝对路径
    """
    return path_getter.get_audio_path(filename)


def get_font_path(filename: str = DEFAULT_FONT_FILENAME_PRIMARY) -> Path:
    """获取字体文件路径的便捷函数

    Args:
        filename: 字体文件名，默认为DEFAULT_FONT_FILENAME_PRIMARY

    Returns:
        Path: 字体文件的绝对路径
    """
    return path_getter.get_font_path(filename)
