# ==================================================
# 导入库
# ==================================================
import json
import copy
import threading
from typing import Dict, List, Any
from pathlib import Path

from loguru import logger

from app.tools.path_utils import get_path


_history_cache_lock = threading.RLock()
_history_data_cache: Dict[Path, tuple[tuple[int, int] | None, Dict[str, Any]]] = {}
_history_names_cache: Dict[Path, tuple[tuple[int, int] | None, List[str]]] = {}


def _get_file_signature(path: Path) -> tuple[int, int] | None:
    try:
        stat = path.stat()
        return (stat.st_mtime_ns, stat.st_size)
    except Exception:
        return None


def _get_directory_signature(path: Path) -> tuple[int, int] | None:
    try:
        stat = path.stat()
        return (stat.st_mtime_ns, stat.st_size)
    except Exception:
        return None


def clear_history_cache(history_type: str | None = None, file_name: str | None = None):
    """清空历史缓存，供外部批量导入/删除历史文件后调用。"""
    with _history_cache_lock:
        if history_type is None:
            _history_data_cache.clear()
            _history_names_cache.clear()
            return

        history_dir = get_path(f"data/history/{history_type}_history")
        _history_names_cache.pop(history_dir, None)
        if file_name is None:
            for path in list(_history_data_cache.keys()):
                try:
                    if path.parent == history_dir:
                        _history_data_cache.pop(path, None)
                except Exception:
                    pass
            return

        _history_data_cache.pop(history_dir / f"{file_name}.json", None)


# ==================================================
# 历史记录文件路径处理函数
# ==================================================
def get_history_file_path(history_type: str, file_name: str) -> Path:
    """获取历史记录文件路径

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        file_name: 文件名（不含扩展名）

    Returns:
        Path: 历史记录文件路径
    """
    history_dir = get_path(f"data/history/{history_type}_history")
    history_dir.mkdir(parents=True, exist_ok=True)
    return history_dir / f"{file_name}.json"


# ==================================================
# 历史记录数据读写函数
# ==================================================


def load_history_data(history_type: str, file_name: str) -> Dict[str, Any]:
    """加载历史记录数据

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        file_name: 文件名（不含扩展名）

    Returns:
        Dict[str, Any]: 历史记录数据
    """
    file_path = get_history_file_path(history_type, file_name)

    if not file_path.exists():
        with _history_cache_lock:
            _history_data_cache.pop(file_path, None)
        return {}

    try:
        signature = _get_file_signature(file_path)
        with _history_cache_lock:
            cached = _history_data_cache.get(file_path)
            if cached is not None and cached[0] == signature:
                return copy.deepcopy(cached[1])

        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            data = {}
        with _history_cache_lock:
            _history_data_cache[file_path] = (signature, data)
        return copy.deepcopy(data)
    except Exception as e:
        logger.error(f"加载历史记录数据失败: {e}")
        return {}


def save_history_data(history_type: str, file_name: str, data: Dict[str, Any]) -> bool:
    """保存历史记录数据

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)
        file_name: 文件名（不含扩展名）
        data: 要保存的数据

    Returns:
        bool: 保存是否成功
    """
    file_path = get_history_file_path(history_type, file_name)
    try:
        with open(file_path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=4)
        with _history_cache_lock:
            _history_data_cache[file_path] = (
                _get_file_signature(file_path),
                copy.deepcopy(data),
            )
            _history_names_cache.pop(file_path.parent, None)
        return True
    except Exception as e:
        logger.error(f"保存历史记录数据失败: {e}")
    return False


def get_all_history_names(history_type: str) -> List[str]:
    """获取所有历史记录名称列表

    Args:
        history_type: 历史记录类型 (roll_call, lottery 等)

    Returns:
        List[str]: 历史记录名称列表
    """
    try:
        history_dir = get_path(f"data/history/{history_type}_history")
        if not history_dir.exists():
            with _history_cache_lock:
                _history_names_cache.pop(history_dir, None)
            return []
        signature = _get_directory_signature(history_dir)
        with _history_cache_lock:
            cached = _history_names_cache.get(history_dir)
            if cached is not None and cached[0] == signature:
                return list(cached[1])
        history_files = list(history_dir.glob("*.json"))
        names = [file.stem for file in history_files]
        names.sort()
        with _history_cache_lock:
            _history_names_cache[history_dir] = (signature, names)
        return names
    except Exception as e:
        logger.error(f"获取历史记录名称列表失败: {e}")
        return []
