# ==================================================
# 导入模块
# ==================================================
import json
import copy
import threading
from typing import List, Dict, Any, Tuple
from loguru import logger

from app.tools.path_utils import *


_list_cache_lock = threading.RLock()
_student_list_cache: Dict[Any, Tuple[Any, List[Dict[str, Any]]]] = {}
_pool_list_cache: Dict[Any, Tuple[Any, List[Dict[str, Any]]]] = {}
_class_name_cache: Dict[Any, Tuple[Any, List[str]]] = {}
_pool_name_cache: Dict[Any, Tuple[Any, List[str]]] = {}


def _get_file_signature(path):
    try:
        stat = path.stat()
        return (stat.st_mtime_ns, stat.st_size)
    except Exception:
        return None


def _get_directory_signature(path):
    try:
        stat = path.stat()
        return (stat.st_mtime_ns, stat.st_size)
    except Exception:
        return None


def clear_list_cache(list_type: str | None = None, name: str | None = None):
    """清空名单缓存，供导入、重命名、删除名单后主动调用。"""
    with _list_cache_lock:
        if list_type is None:
            _student_list_cache.clear()
            _pool_list_cache.clear()
            _class_name_cache.clear()
            _pool_name_cache.clear()
            return

        if list_type == "roll_call":
            _class_name_cache.clear()
            if name is None:
                _student_list_cache.clear()
            else:
                _student_list_cache.pop(name, None)
        elif list_type == "lottery":
            _pool_name_cache.clear()
            if name is None:
                _pool_list_cache.clear()
            else:
                _pool_list_cache.pop(name, None)


def _normalize_tags(value) -> List[str]:
    if value is None:
        return []
    if isinstance(value, list):
        tags = []
        for item in value:
            item = str(item).strip()
            if item and item not in tags:
                tags.append(item)
        return tags
    raw = str(value).strip()
    if not raw or raw.lower() == "nan":
        return []
    for sep in ["，", ",", "；", ";", "|", "/", "\\", "\n", "\t"]:
        raw = raw.replace(sep, " ")
    tags = []
    for item in raw.split(" "):
        item = item.strip()
        if item and item not in tags:
            tags.append(item)
    return tags


def _format_tags_for_export(value) -> str:
    if value is None:
        return ""
    if isinstance(value, list):
        return ",".join([str(x).strip() for x in value if str(x).strip()])
    raw = str(value).strip()
    return "" if raw.lower() == "nan" else raw


def get_duplicate_names(names: List[str]) -> List[str]:
    duplicates = []
    seen = set()
    added = set()
    for name in names:
        if name in seen and name not in added:
            duplicates.append(name)
            added.add(name)
        else:
            seen.add(name)
    return duplicates


def make_unique_names(
    names: List[str], sep: str = "_"
) -> Tuple[List[str], Dict[str, List[str]]]:
    used = set()
    counters: Dict[str, int] = {}
    rename_map: Dict[str, List[str]] = {}
    unique_names: List[str] = []

    for raw in names:
        if raw not in used:
            used.add(raw)
            unique_names.append(raw)
            continue

        index = counters.get(raw, 0) + 1
        candidate = f"{raw}{sep}{index}"
        while candidate in used:
            index += 1
            candidate = f"{raw}{sep}{index}"

        counters[raw] = index
        used.add(candidate)
        unique_names.append(candidate)
        rename_map.setdefault(raw, []).append(candidate)

    return unique_names, rename_map


# ==================================================
# 班级列表管理函数
# ==================================================
def get_class_name_list() -> List[str]:
    """获取班级名称列表

    从 data/list/roll_call_list 文件夹中读取所有班级名单文件，
    并返回班级名称列表

    Returns:
        List[str]: 班级名称列表
    """
    try:
        # 获取班级名单文件夹路径
        roll_call_list_dir = get_data_path("list", "roll_call_list")

        # 如果文件夹不存在，创建文件夹
        if not roll_call_list_dir.exists():
            logger.warning(f"班级名单文件夹不存在: {roll_call_list_dir}")
            roll_call_list_dir.mkdir(parents=True, exist_ok=True)
            with _list_cache_lock:
                _class_name_cache.clear()
            return []

        signature = _get_directory_signature(roll_call_list_dir)
        with _list_cache_lock:
            cached = _class_name_cache.get(roll_call_list_dir)
            if cached is not None and cached[0] == signature:
                return list(cached[1])

        # 获取文件夹中的所有文件
        class_files = []
        for file_path in roll_call_list_dir.glob("*.json"):
            # 获取文件名（不带扩展名）作为班级名称
            class_name = file_path.stem
            class_files.append(class_name)

        # 按字母顺序排序
        class_files.sort()

        # logger.debug(f"找到 {len(class_files)} 个班级: {class_files}")
        with _list_cache_lock:
            _class_name_cache[roll_call_list_dir] = (signature, class_files)
        return class_files

    except Exception as e:
        logger.error(f"获取班级列表失败: {e}")
        return []


def get_student_list(class_name: str) -> List[Dict[str, Any]]:
    """获取指定班级的学生列表

    从 data/list/roll_call_list 文件夹中读取指定班级的名单文件，
    并返回学生列表

    Args:
        class_name: 班级名称

    Returns:
        List[Dict[str, Any]]: 学生列表，每个学生是一个字典，包含姓名、ID、性别、小组等信息
    """
    try:
        # 获取班级名单文件路径
        roll_call_list_dir = get_data_path("list", "roll_call_list")
        class_file_path = roll_call_list_dir / f"{class_name}.json"

        # 如果文件不存在，返回空列表
        if not class_file_path.exists():
            logger.warning(f"班级名单文件不存在: {class_file_path}")
            with _list_cache_lock:
                _student_list_cache.pop(class_name, None)
            return []

        signature = _get_file_signature(class_file_path)
        with _list_cache_lock:
            cached = _student_list_cache.get(class_name)
            if cached is not None and cached[0] == signature:
                return copy.deepcopy(cached[1])

        # 读取JSON文件
        with open(class_file_path, "r", encoding="utf-8") as f:
            student_data = json.load(f)

        # 将字典数据转换为列表形式
        student_list = []
        for name, info in student_data.items():
            student = {
                "name": name,
                "id": info.get("id", 0),
                "gender": info.get("gender", "未知"),
                "group": info.get("group", "未分组"),
                "exist": info.get("exist", True),
                "tags": _normalize_tags(info.get("tags", [])),
            }
            student_list.append(student)

        # 按ID排序
        student_list.sort(key=lambda x: x["id"])

        # logger.debug(f"班级 {class_name} 共有 {len(student_list)} 名学生")
        with _list_cache_lock:
            _student_list_cache[class_name] = (signature, student_list)
        return copy.deepcopy(student_list)

    except Exception as e:
        logger.error(f"获取学生列表失败: {e}")
        return []


def get_group_list(class_name: str) -> List[Dict[str, Any]]:
    """获取指定班级的小组列表

    从 data/list/roll_call_list 文件夹中读取指定班级的名单文件，
    并返回小组列表

    Args:
        class_name: 班级名称

    Returns:
        List[Dict[str, Any]]: 小组列表，每个小组是一个字典，包含小组名称、学生列表等信息
    """
    student_list = get_student_list(class_name)
    group_set = set()  # 使用集合确保不重复
    for student in student_list:
        group_name = student["group"]
        group_set.add(group_name)

    # 转换为列表并排序
    group_list = sorted(list(group_set))
    return group_list


def get_gender_list(class_name: str) -> List[str]:
    """获取指定班级的性别列表

    从 data/list/roll_call_list 文件夹中读取指定班级的名单文件，
    并返回性别列表

    Args:
        class_name: 班级名称

    Returns:
        List[str]: 性别列表，包含所有学生的性别
    """
    student_list = get_student_list(class_name)
    gender_set = set()  # 使用集合确保不重复
    for student in student_list:
        gender = student["gender"]
        gender_set.add(gender)

    # 转换为列表并排序
    gender_list = sorted(list(gender_set))
    return gender_list


def get_group_members(class_name: str, group_name: str) -> List[Dict[str, Any]]:
    """获取指定班级中指定小组的成员列表

    从 data/list/roll_call_list 文件夹中读取指定班级的名单文件，
    并返回指定小组的成员列表

    Args:
        class_name: 班级名称
        group_name: 小组名称

    Returns:
        List[Dict[str, Any]]: 小组成员列表，每个成员是一个字典，包含姓名、ID、性别、小组等信息
    """
    student_list = get_student_list(class_name)
    group_members = []

    for student in student_list:
        if student["group"] == group_name:
            group_members.append(student)

    # 按ID排序
    group_members.sort(key=lambda x: x["id"])
    return group_members


# ==================================================
# 奖池列表管理函数
# ==================================================
def get_pool_name_list() -> List[str]:
    """获取奖池名称列表

    从 data/list/lottery_list 文件夹中读取所有奖池名单文件，
    并返回奖池名称列表

    Returns:
        List[str]: 奖池名称列表
    """
    try:
        # 获取奖池名单文件夹路径
        lottery_list_dir = get_data_path("list/lottery_list")

        # 如果文件夹不存在，创建文件夹
        if not lottery_list_dir.exists():
            logger.warning(f"奖池名单文件夹不存在: {lottery_list_dir}")
            lottery_list_dir.mkdir(parents=True, exist_ok=True)
            with _list_cache_lock:
                _pool_name_cache.clear()
            return []

        signature = _get_directory_signature(lottery_list_dir)
        with _list_cache_lock:
            cached = _pool_name_cache.get(lottery_list_dir)
            if cached is not None and cached[0] == signature:
                return list(cached[1])

        # 获取文件夹中的所有文件
        pool_files = []
        for file_path in lottery_list_dir.glob("*.json"):
            # 获取文件名（不带扩展名）作为奖池名称
            pool_name = file_path.stem
            pool_files.append(pool_name)

        # 按字母顺序排序
        pool_files.sort()

        # logger.debug(f"找到 {len(pool_files)} 个奖池: {pool_files}")
        with _list_cache_lock:
            _pool_name_cache[lottery_list_dir] = (signature, pool_files)
        return pool_files

    except Exception as e:
        logger.error(f"获取奖池列表失败: {e}")
        return []


def get_pool_list(pool_name: str) -> List[Dict[str, Any]]:
    """获取指定奖池的奖品列表

    从 data/list/lottery_list 文件夹中读取指定奖池的名单文件，
    并返回奖品列表

    Args:
        pool_name: 奖池名称

    Returns:
        List[Dict[str, Any]]: 奖品列表，每个奖品是一个字典，包含名称、ID、权重等信息
    """
    try:
        # 获取奖池名单文件路径
        lottery_list_dir = get_data_path("list/lottery_list")
        pool_file_path = lottery_list_dir / f"{pool_name}.json"

        # 如果文件不存在，返回空列表
        if not pool_file_path.exists():
            logger.warning(f"奖池名单文件不存在: {pool_file_path}")
            with _list_cache_lock:
                _pool_list_cache.pop(pool_name, None)
            return []

        signature = _get_file_signature(pool_file_path)
        with _list_cache_lock:
            cached = _pool_list_cache.get(pool_name)
            if cached is not None and cached[0] == signature:
                return copy.deepcopy(cached[1])

        # 读取JSON文件
        with open(pool_file_path, "r", encoding="utf-8") as f:
            pool_data = json.load(f)

        # 将字典数据转换为列表形式
        pool_list = []
        for name, info in pool_data.items():
            raw_count = info.get("count", 1)
            try:
                count = int(raw_count)
            except Exception:
                count = 1
            if count < 0:
                count = 0
            pool = {
                "name": name,
                "id": info.get("id", 0),
                "weight": info.get("weight", 1),
                "exist": info.get("exist", True),
                "count": count,
                "tags": _normalize_tags(info.get("tags", [])),
            }
            pool_list.append(pool)

        # 按ID排序
        pool_list.sort(key=lambda x: x["id"])

        # logger.debug(f"奖池 {pool_name} 共有 {len(pool_list)} 个奖品")
        with _list_cache_lock:
            _pool_list_cache[pool_name] = (signature, pool_list)
        return copy.deepcopy(pool_list)

    except Exception as e:
        logger.error(f"获取奖池列表失败: {e}")
        return []


# ==================================================
# 学生数据处理函数
# ==================================================
def filter_students_data(
    data: List[Dict[str, Any]],
    group_index: int,
    group_filter: str,
    gender_index: int,
    gender_filter: str,
) -> List[Tuple]:
    """根据小组和性别条件过滤学生数据

    根据指定的小组和性别索引条件过滤学生数据，返回包含完整学生信息的列表

    Args:
        data: 学生数据列表
        group_index: 小组筛选索引，0表示抽取全班学生，1表示抽取小组组号，大于等于2表示具体的小组索引
        group_filter: 小组筛选条件，当group_index>=2时使用
        gender_index: 性别筛选索引，0表示抽取所有性别，1表示男性，2表示女性
        gender_filter: 性别筛选条件，"男"或"女"

    Returns:
        List[Tuple]: 包含(id, name, gender, group, exist)的元组列表
    """
    students_data = []

    try:
        # 处理全班学生抽取 (group_index = 0)
        if group_index == 0:
            if gender_index == 0:  # 抽取所有性别
                for student_info in data:
                    id = student_info.get("id", "")
                    name = student_info.get("name", "")
                    gender = student_info.get("gender", "")
                    group = student_info.get("group", "")
                    exist = student_info.get("exist", True)
                    students_data.append((id, name, gender, group, exist))
            else:  # 抽取特定性别
                for student_info in data:
                    id = student_info.get("id", "")
                    name = student_info.get("name", "")
                    gender = student_info.get("gender", "")
                    group = student_info.get("group", "")
                    exist = student_info.get("exist", True)
                    if gender == gender_filter:
                        students_data.append((id, name, gender, group, exist))

        # 处理小组组号抽取 (group_index = 1)
        elif group_index == 1:
            groups_set = set()
            for student_info in data:
                group = student_info.get("group", "")
                gender = student_info.get("gender", "")
                if group:  # 只添加非空小组
                    if gender_index == 0 or gender == gender_filter:  # 根据性别条件过滤
                        groups_set.add(group)

            # 对小组进行排序，按小组名称排序
            # 返回格式为 (id, name, gender, group, exist)，但小组模式下只需要小组名称
            students_data = []
            for group_name in sorted(groups_set):
                # 在小组模式下，我们只需要小组名称，其他字段可以留空或使用默认值
                students_data.append((None, group_name, None, group_name, True))

        # 处理指定小组抽取 (group_index >= 2)
        elif group_index >= 2:
            for student_info in data:
                id = student_info.get("id", "")
                name = student_info.get("name", "")
                gender = student_info.get("gender", "")
                group = student_info.get("group", "")
                exist = student_info.get("exist", True)
                if group == group_filter:  # 匹配指定小组
                    if gender_index == 0 or gender == gender_filter:  # 根据性别条件过滤
                        students_data.append((id, name, gender, group, exist))

        # 过滤学生信息的exist为False的学生
        students_data = list(filter(lambda x: x[4], students_data))

        return students_data

    except Exception as e:
        logger.error(f"过滤学生数据失败: {e}")
        return []


# ==================================================
# 学生数据导出函数
# ==================================================
def export_student_data(
    class_name: str, file_path: str, export_format: str
) -> Tuple[bool, str]:
    """导出学生数据到指定文件

    从 data/list/roll_call_list 文件夹中读取指定班级的名单文件，
    并根据指定格式导出到文件

    Args:
        class_name: 班级名称
        file_path: 导出文件路径
        export_format: 导出格式 ('excel', 'csv', 'txt')

    Returns:
        Tuple[bool, str]: (是否成功, 成功/错误消息)
    """
    try:
        # 获取班级名单文件路径
        roll_call_list_dir = get_data_path("list", "roll_call_list")
        class_file_path = roll_call_list_dir / f"{class_name}.json"

        # 如果文件不存在，返回错误
        if not class_file_path.exists():
            error_msg = f"班级文件 '{class_name}.json' 不存在"
            logger.error(error_msg)
            return False, error_msg

        # 读取JSON文件
        with open(class_file_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        if not data:
            error_msg = "当前班级没有学生数据"
            logger.warning(error_msg)
            return False, error_msg

        # 根据导出格式处理数据
        if export_format.lower() == "excel":
            return _export_to_excel(data, file_path)
        elif export_format.lower() == "csv":
            return _export_to_csv(data, file_path)
        elif export_format.lower() == "txt":
            return _export_to_txt(data, file_path)
        else:
            error_msg = f"不支持的导出格式: {export_format}"
            logger.error(error_msg)
            return False, error_msg

    except FileNotFoundError:
        error_msg = f"班级文件 '{class_name}.json' 不存在"
        logger.error(error_msg)
        return False, error_msg
    except json.JSONDecodeError:
        error_msg = f"班级文件 '{class_name}.json' 格式错误"
        logger.error(error_msg)
        return False, error_msg
    except Exception as e:
        error_msg = f"导出学生名单时出错: {str(e)}"
        logger.exception(error_msg)
        return False, error_msg


def _generic_export_excel(export_data: List[Dict], file_path: str) -> Tuple[bool, str]:
    """通用导出Excel函数"""
    try:
        import pandas as pd

        df = pd.DataFrame(export_data)

        # 确保文件扩展名正确
        if not file_path.endswith(".xlsx"):
            file_path += ".xlsx"

        target_path = get_path(file_path)
        target_path.parent.mkdir(parents=True, exist_ok=True)

        # 保存为xlsx文件
        df.to_excel(target_path, index=False, engine="openpyxl")

        success_msg = f"已导出到: {target_path}"
        logger.info(success_msg)
        return True, success_msg

    except Exception as e:
        error_msg = f"导出Excel文件时出错: {str(e)}"
        logger.exception(error_msg)
        return False, error_msg


def _generic_export_csv(export_data: List[Dict], file_path: str) -> Tuple[bool, str]:
    """通用导出CSV函数"""
    try:
        try:
            import pandas as pd
        except Exception as e:
            logger.exception(f"导出CSV需要 pandas 库，但导入失败: {e}")
            return False, "导出失败: pandas 未安装或导入错误"

        df = pd.DataFrame(export_data)

        # 确保文件扩展名正确
        if not file_path.endswith(".csv"):
            file_path += ".csv"

        target_path = get_path(file_path)
        target_path.parent.mkdir(parents=True, exist_ok=True)

        # 保存为CSV文件
        df.to_csv(target_path, index=False, encoding="utf-8-sig")

        success_msg = f"已导出到: {target_path}"
        logger.info(success_msg)
        return True, success_msg

    except Exception as e:
        error_msg = f"导出CSV文件时出错: {str(e)}"
        logger.exception(error_msg)
        return False, error_msg


def _generic_export_txt(names: List[str], file_path: str) -> Tuple[bool, str]:
    """通用导出TXT函数"""
    try:
        # 确保文件扩展名正确
        if not file_path.endswith(".txt"):
            file_path += ".txt"

        target_path = get_path(file_path)
        target_path.parent.mkdir(parents=True, exist_ok=True)

        # 提取姓名并保存为TXT文件，每行一个姓名
        with open(target_path, "w", encoding="utf-8") as f:
            for name in names:
                f.write(f"{name}\n")

        success_msg = f"已导出到: {target_path}"
        logger.info(success_msg)
        return True, success_msg

    except Exception as e:
        error_msg = f"导出TXT文件时出错: {str(e)}"
        logger.exception(error_msg)
        return False, error_msg


def _prepare_student_export_data(data: Dict[str, Any]) -> List[Dict]:
    """准备学生导出数据"""
    export_data = []

    for name, info in data.items():
        export_data.append(
            {
                "学号": info.get("id", ""),
                "姓名": name,
                "性别": info.get("gender", ""),
                "所处小组": info.get("group", ""),
                "标签": _format_tags_for_export(info.get("tags", "")),
            }
        )
    return export_data


def _prepare_prize_export_data(data: Dict[str, Any]) -> List[Dict]:
    """准备奖品导出数据"""
    export_data = []

    for prize_name, prize_info in data.items():
        export_data.append(
            {
                "ID": prize_info.get("id", ""),
                "奖品名称": prize_name,
                "权重": prize_info.get("weight", 0),
                "标签": _format_tags_for_export(prize_info.get("tags", "")),
                "数量": prize_info.get("count", 1),
            }
        )
    return export_data


def export_prize_data(
    pool_name: str, file_path: str, export_format: str
) -> Tuple[bool, str]:
    try:
        lottery_list_dir = get_data_path("list/lottery_list")
        pool_file_path = lottery_list_dir / f"{pool_name}.json"

        if not pool_file_path.exists():
            error_msg = f"奖池文件 '{pool_name}.json' 不存在"
            logger.error(error_msg)
            return False, error_msg

        with open(pool_file_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        if not data:
            error_msg = "当前奖池没有奖品数据"
            logger.warning(error_msg)
            return False, error_msg

        if export_format.lower() == "excel":
            return _export_prize_to_excel(data, file_path)
        if export_format.lower() == "csv":
            return _export_prize_to_csv(data, file_path)
        if export_format.lower() == "txt":
            return _export_prize_to_txt(data, file_path)

        error_msg = f"不支持的导出格式: {export_format}"
        logger.error(error_msg)
        return False, error_msg

    except FileNotFoundError:
        error_msg = f"奖池文件 '{pool_name}.json' 不存在"
        logger.error(error_msg)
        return False, error_msg
    except json.JSONDecodeError:
        error_msg = f"奖池文件 '{pool_name}.json' 格式错误"
        logger.error(error_msg)
        return False, error_msg
    except Exception as e:
        error_msg = f"导出奖品名单时出错: {str(e)}"
        logger.exception(error_msg)
        return False, error_msg


def _export_to_excel(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出学生数据为Excel文件"""
    return _generic_export_excel(_prepare_student_export_data(data), file_path)


def _export_to_csv(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出学生数据为CSV文件"""
    return _generic_export_csv(_prepare_student_export_data(data), file_path)


def _export_to_txt(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出学生数据为TXT文件（仅姓名）"""
    return _generic_export_txt(list(data.keys()), file_path)


def _export_prize_to_excel(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出奖品数据为Excel文件"""
    return _generic_export_excel(_prepare_prize_export_data(data), file_path)


def _export_prize_to_csv(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出奖品数据为CSV文件"""
    return _generic_export_csv(_prepare_prize_export_data(data), file_path)


def _export_prize_to_txt(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出奖品数据为TXT文件"""
    return _generic_export_txt(list(data.keys()), file_path)
