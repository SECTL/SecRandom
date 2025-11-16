# ==================================================
# 导入模块
# ==================================================
import os
import json
from typing import Dict, Optional, Any, List
from loguru import logger

from app.tools.path_utils import get_path, get_resources_path
from app.tools.settings_access import readme_settings

# from app.Language.ZH_CN import ZH_CN
import glob
import importlib
import importlib.util
import pkgutil

from app.tools.variable import LANGUAGE_MODULE_DIR


# ==================================================
# 简化的语言管理器类
# ==================================================
class SimpleLanguageManager:
    """负责获取当前语言和全部语言"""

    def __init__(self):
        self._current_language: Optional[str] = None

        # 默认加载中文，从模块文件动态生成
        merged_zh_cn = self._merge_language_files("ZH_CN")
        self._loaded_languages: Dict[str, Dict[str, Any]] = {"ZH_CN": merged_zh_cn}

        # 加载resources/Language文件夹下的所有语言文件
        self._load_all_languages()

    def _merge_language_files(self, language_code: Optional[str]) -> Dict[str, Any]:
        """
        从模块化语言文件中合并生成完整的语言字典

        Args:
            language_code: 语言代码，默认为"ZH_CN"

        Returns:
            合并后的语言字典
        """
        merged = {}
        language_code = "ZH_CN" if not language_code else language_code
        language_dir = get_path(LANGUAGE_MODULE_DIR)

        module_entries: List[tuple[str, Optional[str]]] = []

        if os.path.isdir(language_dir):
            # 开发环境：直接从文件系统查找
            language_module_files = glob.glob(os.path.join(language_dir, "*.py"))
            for file_path in language_module_files:
                if file_path.endswith("__init__.py"):
                    continue
                module_entries.append(
                    (os.path.splitext(os.path.basename(file_path))[0], file_path)
                )
        else:
            # 打包环境：利用包信息进行枚举
            logger.warning(f"语言模块目录不存在: {language_dir}")
            try:
                language_package = importlib.import_module("app.Language.modules")
                discovered = {
                    name.rsplit(".", 1)[-1]
                    for _, name, is_pkg in pkgutil.walk_packages(
                        getattr(language_package, "__path__", []),
                        language_package.__name__ + ".",
                    )
                    if not is_pkg and not name.endswith(".__init__")
                }
                if discovered:
                    module_entries.extend(
                        (module_name, None) for module_name in sorted(discovered)
                    )
                else:
                    logger.warning("未能通过 pkgutil.walk_packages 发现语言模块")
            except Exception as discovery_error:
                logger.error(f"枚举语言模块失败: {discovery_error}")

        if not module_entries:
            logger.warning("未找到任何语言模块，返回空语言数据")
            return merged

        # 遍历所有模块并导入
        for module_name, file_path in module_entries:
            try:
                # 优先使用标准导入（适用于打包环境）
                try:
                    module = __import__(
                        f"app.Language.modules.{module_name}",
                        fromlist=[module_name],
                    )
                except ImportError:
                    if not file_path:
                        raise
                    # 如果直接导入失败且存在文件路径，使用动态加载（开发环境）
                    spec = importlib.util.spec_from_file_location(
                        module_name, file_path
                    )
                    if spec is None:
                        logger.warning(f"无法创建模块规范: {file_path}")
                        continue

                    module = importlib.util.module_from_spec(spec)
                    if spec.loader is None:
                        logger.warning(f"模块加载器为空: {file_path}")
                        continue

                    spec.loader.exec_module(module)

                # 遍历模块中的所有属性
                for attr_name in dir(module):
                    attr_value = getattr(module, attr_name)
                    # 如果属性是字典且包含目标语言代码
                    if isinstance(attr_value, dict) and language_code in attr_value:
                        # 将模块内容合并到结果字典中
                        merged[attr_name] = attr_value[language_code]

            except Exception as e:
                logger.error(f"导入语言模块 {file_path} 时出错: {e}")
                continue

        return merged

    def _load_all_languages(self) -> None:
        """加载resources/Language文件夹下的所有语言文件"""
        try:
            # 获取语言文件夹路径
            language_dir = get_resources_path("Language")

            if not language_dir or not os.path.exists(language_dir):
                return

            # 遍历文件夹中的所有.json文件
            for filename in os.listdir(language_dir):
                if filename.endswith(".json"):
                    language_code = filename[:-5]  # 去掉.json后缀

                    # 跳过已加载的语言
                    if language_code in self._loaded_languages:
                        continue

                    file_path = os.path.join(language_dir, filename)

                    try:
                        # 加载语言文件
                        with open(file_path, "r", encoding="utf-8") as f:
                            language_data = json.load(f)
                            self._loaded_languages[language_code] = language_data
                    except Exception as e:
                        logger.error(f"加载语言文件 {filename} 时出错: {e}")

        except Exception as e:
            logger.error(f"加载语言文件夹时出错: {e}")

    def get_current_language(self) -> str:
        """获取当前语言代码

        Returns:
            当前语言代码
        """
        # 如果当前语言未设置，从设置中获取
        if self._current_language is None:
            self._current_language = readme_settings("basic_settings", "language")
            if self._current_language is None:
                self._current_language = "ZH_CN"

        return self._current_language

    def get_current_language_data(self) -> Dict[str, Any]:
        """获取当前语言数据

        Returns:
            当前语言数据字典
        """
        language_code = self.get_current_language()

        # 如果语言未加载，返回默认中文
        if language_code not in self._loaded_languages:
            return self._loaded_languages["ZH_CN"]

        return self._loaded_languages[language_code]

    def get_all_languages(self) -> Dict[str, Dict[str, Any]]:
        """获取所有已加载的语言数据

        Returns:
            包含所有语言数据的字典，键为语言代码，值为语言数据字典
        """
        return dict(self._loaded_languages)

    def get_language_info(self, language_code: str) -> Optional[Dict[str, Any]]:
        """获取指定语言的信息（translate_JSON_file字段）

        Args:
            language_code: 语言代码

        Returns:
            语言信息字典，如果语言不存在则返回None
        """
        if language_code not in self._loaded_languages:
            return None

        language_data = self._loaded_languages[language_code]

        # 返回translate_JSON_file字段，如果不存在则返回空字典
        return language_data.get("translate_JSON_file", {})


# 创建全局语言管理器实例
_simple_language_manager = None


def get_simple_language_manager() -> SimpleLanguageManager:
    """获取全局简化语言管理器实例"""
    global _simple_language_manager
    if _simple_language_manager is None:
        _simple_language_manager = SimpleLanguageManager()
    return _simple_language_manager


# ==================================================
# 简化的语言管理辅助函数
# ==================================================
def get_current_language() -> str:
    """获取当前语言代码

    Returns:
        当前语言代码
    """
    return get_simple_language_manager().get_current_language()


def get_all_languages() -> Dict[str, Dict[str, Any]]:
    """获取所有已加载的语言数据

    Returns:
        包含所有语言数据的字典，键为语言代码，值为语言数据字典
    """
    return get_simple_language_manager().get_all_languages()


def get_all_languages_name() -> List[str]:
    """获取所有已加载的语言名称

    Returns:
        包含所有语言名称的列表，每个元素为语言名称
    """
    language_names = []
    for code, data in get_all_languages().items():
        language_info = data.get("translate_JSON_file", {})
        name = language_info.get("name", code)
        language_names.append(name)
    return language_names


def get_current_language_data() -> Dict[str, Any]:
    """获取当前语言数据

    Returns:
        当前语言数据字典
    """
    return get_simple_language_manager().get_current_language_data()


def get_language_info(language_code: str) -> Optional[Dict[str, Any]]:
    """获取指定语言的信息（translate_JSON_file字段）

    Args:
        language_code: 语言代码

    Returns:
        语言信息字典，如果语言不存在则返回None
    """
    return get_simple_language_manager().get_language_info(language_code)
