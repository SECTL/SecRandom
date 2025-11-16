# ==================================================
# 导入模块
# ==================================================
import json
from typing import List, Dict, Any, Tuple
from loguru import logger

from app.tools.path_utils import *


# ==================================================
# 班级列表管理函数
# ==================================================
def get_class_name_list() -> List[str]:
    """获取班级名称列表

    从 app/resources/list/roll_call_list 文件夹中读取所有班级名单文件，
    并返回班级名称列表

    Returns:
        List[str]: 班级名称列表
    """
    try:
        # 获取班级名单文件夹路径
        roll_call_list_dir = get_path("app/resources/list/roll_call_list")

        # 如果文件夹不存在，创建文件夹
        if not roll_call_list_dir.exists():
            logger.warning(f"班级名单文件夹不存在: {roll_call_list_dir}")
            roll_call_list_dir.mkdir(parents=True, exist_ok=True)
            return []

        # 获取文件夹中的所有文件
        class_files = []
        for file_path in roll_call_list_dir.glob("*.json"):
            # 获取文件名（不带扩展名）作为班级名称
            class_name = file_path.stem
            class_files.append(class_name)

        # 按字母顺序排序
        class_files.sort()

        # logger.debug(f"找到 {len(class_files)} 个班级: {class_files}")
        return class_files

    except Exception as e:
        logger.error(f"获取班级列表失败: {e}")
        return []


def get_student_list(class_name: str) -> List[Dict[str, Any]]:
    """获取指定班级的学生列表

    从 app/resources/list/roll_call_list 文件夹中读取指定班级的名单文件，
    并返回学生列表

    Args:
        class_name: 班级名称

    Returns:
        List[Dict[str, Any]]: 学生列表，每个学生是一个字典，包含姓名、ID、性别、小组等信息
    """
    try:
        # 获取班级名单文件路径
        roll_call_list_dir = get_path("app/resources/list/roll_call_list")
        class_file_path = roll_call_list_dir / f"{class_name}.json"

        # 如果文件不存在，返回空列表
        if not class_file_path.exists():
            logger.warning(f"班级名单文件不存在: {class_file_path}")
            return []

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
            }
            student_list.append(student)

        # 按ID排序
        student_list.sort(key=lambda x: x["id"])

        # logger.debug(f"班级 {class_name} 共有 {len(student_list)} 名学生")
        return student_list

    except Exception as e:
        logger.error(f"获取学生列表失败: {e}")
        return []


def get_group_list(class_name: str) -> List[Dict[str, Any]]:
    """获取指定班级的小组列表

    从 app/resources/list/roll_call_list 文件夹中读取指定班级的名单文件，
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

    从 app/resources/list/roll_call_list 文件夹中读取指定班级的名单文件，
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

    从 app/resources/list/roll_call_list 文件夹中读取指定班级的名单文件，
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

    从 app/resources/list/lottery_list 文件夹中读取所有奖池名单文件，
    并返回奖池名称列表

    Returns:
        List[str]: 奖池名称列表
    """
    try:
        # 获取奖池名单文件夹路径
        lottery_list_dir = get_path("app/resources/list/lottery_list")

        # 如果文件夹不存在，创建文件夹
        if not lottery_list_dir.exists():
            logger.warning(f"奖池名单文件夹不存在: {lottery_list_dir}")
            lottery_list_dir.mkdir(parents=True, exist_ok=True)
            return []

        # 获取文件夹中的所有文件
        pool_files = []
        for file_path in lottery_list_dir.glob("*.json"):
            # 获取文件名（不带扩展名）作为奖池名称
            pool_name = file_path.stem
            pool_files.append(pool_name)

        # 按字母顺序排序
        pool_files.sort()

        # logger.debug(f"找到 {len(pool_files)} 个奖池: {pool_files}")
        return pool_files

    except Exception as e:
        logger.error(f"获取奖池列表失败: {e}")
        return []


def get_pool_data(pool_name: str) -> Dict[str, Any]:
    """获取指定奖池的数据

    从 app/resources/list/lottery_list 文件夹中读取指定奖池的名单文件，
    并返回奖池数据

    Args:
        pool_name: 奖池名称

    Returns:
        Dict[str, Any]: 奖池数据，包含奖品名称、权重、是否存在等信息
    """
    try:
        # 获取奖池名单文件路径
        lottery_list_dir = get_path("app/resources/list/lottery_list")
        pool_file_path = lottery_list_dir / f"{pool_name}.json"

        # 如果文件不存在，返回空字典
        if not pool_file_path.exists():
            logger.warning(f"奖池名单文件不存在: {pool_file_path}")
            return {}

        # 读取JSON文件
        with open(pool_file_path, "r", encoding="utf-8") as f:
            pool_data = json.load(f)

        # 将字典数据转换为列表形式
        pool_list = []
        for name, info in pool_data.items():
            pool = {
                "name": name,
                "id": info.get("id", 0),
                "weight": info.get("weight", 1),
                "exist": info.get("exist", True),
            }
            pool_list.append(pool)

        # 按ID排序
        pool_list.sort(key=lambda x: x["id"])

        # logger.debug(f"奖池 {pool_name} 共有 {len(pool_list)} 个奖品")
        return pool_list

    except Exception as e:
        logger.error(f"获取奖池数据失败: {e}")
        return []


def get_pool_list(pool_name: str) -> List[Dict[str, Any]]:
    """获取指定奖池的奖品列表

    从 app/resources/list/lottery_list 文件夹中读取指定奖池的名单文件，
    并返回奖品列表

    Args:
        pool_name: 奖池名称

    Returns:
        List[Dict[str, Any]]: 奖品列表，每个奖品是一个字典，包含名称、ID、权重等信息
    """
    try:
        # 获取奖池名单文件路径
        lottery_list_dir = get_path("app/resources/list/lottery_list")
        pool_file_path = lottery_list_dir / f"{pool_name}.json"

        # 如果文件不存在，返回空列表
        if not pool_file_path.exists():
            logger.warning(f"奖池名单文件不存在: {pool_file_path}")
            return []

        # 读取JSON文件
        with open(pool_file_path, "r", encoding="utf-8") as f:
            pool_data = json.load(f)

        # 将字典数据转换为列表形式
        pool_list = []
        for name, info in pool_data.items():
            pool = {
                "name": name,
                "id": info.get("id", 0),
                "weight": info.get("weight", 1),
                "exist": info.get("exist", True),
            }
            pool_list.append(pool)

        # 按ID排序
        pool_list.sort(key=lambda x: x["id"])

        # logger.debug(f"奖池 {pool_name} 共有 {len(pool_list)} 个奖品")
        return pool_list

    except Exception as e:
        logger.error(f"获取奖池列表失败: {e}")
        return []


# ==================================================
# 学生数据处理函数
# ==================================================
def filter_students_data(
    data: Dict[str, Any],
    group_index: int,
    group_filter: str,
    gender_index: int,
    gender_filter: str,
) -> List[Dict[str, Any]]:
    """根据小组和性别条件过滤学生数据

    根据指定的小组和性别索引条件过滤学生数据，返回包含完整学生信息的列表

    Args:
        data: 学生数据字典，键为学生姓名，值为包含学生信息的字典
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
                for student_name, student_info in data.items():
                    if isinstance(student_info, dict) and "id" in student_info:
                        id = student_info.get("id", "")
                        name = student_name
                        gender = student_info.get("gender", "")
                        group = student_info.get("group", "")
                        exist = student_info.get("exist", True)
                        students_data.append((id, name, gender, group, exist))
            else:  # 抽取特定性别
                for student_name, student_info in data.items():
                    if isinstance(student_info, dict) and "id" in student_info:
                        id = student_info.get("id", "")
                        name = student_name
                        gender = student_info.get("gender", "")
                        group = student_info.get("group", "")
                        exist = student_info.get("exist", True)
                        if gender == gender_filter:
                            students_data.append((id, name, gender, group, exist))

        # 处理小组组号抽取 (group_index = 1)
        elif group_index == 1:
            groups_set = set()
            for student_name, student_info in data.items():
                if isinstance(student_info, dict) and "id" in student_info:
                    group = student_info.get("group", "")
                    gender = student_info.get("gender", "")
                    exist = student_info.get("exist", True)
                    if group:  # 只添加非空小组
                        if (
                            gender_index == 0 or gender == gender_filter
                        ):  # 根据性别条件过滤
                            groups_set.add(group)
            
            # 对小组进行排序，按小组名称排序
            # 返回格式为 (id, name, gender, group, exist)，但小组模式下只需要小组名称
            students_data = []
            for group_name in sorted(groups_set):
                # 在小组模式下，我们只需要小组名称，其他字段可以留空或使用默认值
                students_data.append((None, group_name, None, group_name, True))

        # 处理指定小组抽取 (group_index >= 2)
        elif group_index >= 2:
            for student_name, student_info in data.items():
                if isinstance(student_info, dict) and "id" in student_info:
                    id = student_info.get("id", "")
                    name = student_name
                    gender = student_info.get("gender", "")
                    group = student_info.get("group", "")
                    exist = student_info.get("exist", True)
                    if group == group_filter:  # 匹配指定小组
                        if (
                            gender_index == 0 or gender == gender_filter
                        ):  # 根据性别条件过滤
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

    从 app/resources/list/roll_call_list 文件夹中读取指定班级的名单文件，
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
        roll_call_list_dir = get_path("app/resources/list/roll_call_list")
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
        logger.error(error_msg)
        return False, error_msg


def _export_to_excel(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出学生数据为Excel文件

    Args:
        data: 学生数据字典
        file_path: 导出文件路径

    Returns:
        Tuple[bool, str]: (是否成功, 成功/错误消息)
    """
    try:
        # 转换为DataFrame
        export_data = []
        for name, info in data.items():
            export_data.append(
                {
                    "学号": info["id"],
                    "姓名": name,
                    "性别": info["gender"],
                    "所处小组": info["group"],
                }
            )

        # 延迟导入 pandas，避免程序启动时加载大型 C 扩展
        try:
            import pandas as pd
        except Exception as e:
            logger.error(f"导出Excel需要 pandas 库，但导入失败: {e}")
            return False, "导出失败: pandas 未安装或导入错误"

        df = pd.DataFrame(export_data)

        # 确保文件扩展名正确
        if not file_path.endswith(".xlsx"):
            file_path += ".xlsx"

        # 保存为xlsx文件
        df.to_excel(file_path, index=False, engine="openpyxl")

        success_msg = f"学生名单已导出到: {file_path}"
        logger.info(success_msg)
        return True, success_msg

    except Exception as e:
        error_msg = f"导出Excel文件时出错: {str(e)}"
        logger.error(error_msg)
        return False, error_msg


def _export_to_csv(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出学生数据为CSV文件

    Args:
        data: 学生数据字典
        file_path: 导出文件路径

    Returns:
        Tuple[bool, str]: (是否成功, 成功/错误消息)
    """
    try:
        # 转换为DataFrame
        export_data = []
        for name, info in data.items():
            export_data.append(
                {
                    "学号": info["id"],
                    "姓名": name,
                    "性别": info["gender"],
                    "所处小组": info["group"],
                }
            )

        # 延迟导入 pandas，避免程序启动时加载大型 C 扩展
        try:
            import pandas as pd
        except Exception as e:
            logger.error(f"导出CSV需要 pandas 库，但导入失败: {e}")
            return False, "导出失败: pandas 未安装或导入错误"

        df = pd.DataFrame(export_data)

        # 确保文件扩展名正确
        if not file_path.endswith(".csv"):
            file_path += ".csv"

        # 保存为CSV文件
        df.to_csv(file_path, index=False, encoding="utf-8-sig")

        success_msg = f"学生名单已导出到: {file_path}"
        logger.info(success_msg)
        return True, success_msg

    except Exception as e:
        error_msg = f"导出CSV文件时出错: {str(e)}"
        logger.error(error_msg)
        return False, error_msg


def _export_to_txt(data: Dict[str, Any], file_path: str) -> Tuple[bool, str]:
    """导出学生数据为TXT文件（仅姓名）

    Args:
        data: 学生数据字典
        file_path: 导出文件路径

    Returns:
        Tuple[bool, str]: (是否成功, 成功/错误消息)
    """
    try:
        # 确保文件扩展名正确
        if not file_path.endswith(".txt"):
            file_path += ".txt"

        # 提取姓名并保存为TXT文件，每行一个姓名
        with open(file_path, "w", encoding="utf-8") as f:
            for name in data.keys():
                f.write(f"{name}\n")

        success_msg = f"学生名单已导出到: {file_path}"
        logger.info(success_msg)
        return True, success_msg

    except Exception as e:
        error_msg = f"导出TXT文件时出错: {str(e)}"
        logger.error(error_msg)
        return False, error_msg
