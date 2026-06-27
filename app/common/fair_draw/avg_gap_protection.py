# ==================================================
# 导入库
# ==================================================

from typing import List, Dict, Any
from loguru import logger
from app.common.history import *
from app.tools.settings_access import get_bool_setting, get_int_setting


# ==================================================
# 辅助函数
# ==================================================


def _get_student_name(student: Dict[str, Any]) -> str:
    """获取学生姓名，优先使用name字段，其次使用id字段"""
    return student.get("name", student.get("id", ""))


def _sort_candidates_by_count(
    candidates: List[Dict[str, Any]], student_counts: Dict[str, int]
) -> List[Dict[str, Any]]:
    """按抽取次数从小到大排序候选人"""
    # 先转换为(次数, 学生)元组列表
    candidates_with_count = []
    for student in candidates:
        student_name = _get_student_name(student)
        count = student_counts.get(student_name, 0)
        candidates_with_count.append((count, student))

    # 按次数从小到大排序
    candidates_with_count.sort(key=lambda x: x[0])

    # 提取排序后的学生列表
    return [student for _, student in candidates_with_count]


def _get_expanded_pool(
    candidates: List[Dict[str, Any]],
    student_counts: Dict[str, int],
    target_count: int,
    initial_threshold: int,
    max_count: int,
) -> List[Dict[str, Any]]:
    """根据阈值扩展候选池，直到达到目标人数或最大阈值"""
    expanded_pool = []
    new_threshold = initial_threshold

    # 第一次尝试扩展
    for student in candidates:
        student_name = _get_student_name(student)
        count = student_counts.get(student_name, 0)
        if count <= new_threshold:
            expanded_pool.append(student)

    logger.debug(f"第一次扩大后候选池人数: {len(expanded_pool)}")

    # 如果仍然不足，继续向上扩大，直到达到最大次数
    while len(expanded_pool) < target_count and new_threshold < max_count:
        new_threshold += 1
        logger.debug(f"扩大后仍不足，继续扩大到阈值: {new_threshold}")
        expanded_pool = []
        for student in candidates:
            student_name = _get_student_name(student)
            count = student_counts.get(student_name, 0)
            if count <= new_threshold:
                expanded_pool.append(student)
        logger.debug(f"再次扩大后候选池人数: {len(expanded_pool)}")

    return expanded_pool


def _filter_candidates_by_max_count(
    candidates: List[Dict[str, Any]],
    student_counts: Dict[str, int],
    max_allowed_count: int,
) -> List[Dict[str, Any]]:
    """按最大允许抽取次数过滤候选人。"""
    return [
        student
        for student in candidates
        if student_counts.get(_get_student_name(student), 0) <= max_allowed_count
    ]


# ==================================================
# 平均值 + 差值保护的公平抽取功能
# ==================================================


def apply_avg_gap_protection(
    candidates: List[Dict[str, Any]],
    draw_count: int,
    class_name: str,
    history_type: str = "roll_call",
    subject_filter: str = "",
) -> List[Dict[str, Any]]:
    """
    应用平均值过滤 + 最大差距保护的公平抽取逻辑
    Args:
        candidates: 候选列表，每个元素包含学生信息
        draw_count: 本次要抽取的人数
        class_name: 班级名称
        history_type: 历史记录类型，默认为"roll_call"
        subject_filter: 科目过滤，如果指定则只计算该科目的历史记录
    Returns:
        处理后的候选池
    """
    # 检查功能是否启用
    if not get_bool_setting("fair_draw_settings", "enable_avg_gap_protection"):
        return candidates

    # 集中获取所有配置
    gap_threshold = get_int_setting("fair_draw_settings", "gap_threshold", 3)
    min_pool_size = get_int_setting("fair_draw_settings", "min_pool_size", 1)

    logger.debug(
        f"应用平均值差值保护，抽取人数: {draw_count}, 差距阈值: {gap_threshold}, 最小池大小: {min_pool_size}"
    )

    # 检查候选列表是否为空
    if not candidates:
        logger.debug("候选列表为空，直接返回")
        return candidates

    try:
        # Step 1: 获取当前抽取单位的次数
        # 加载历史记录
        history_data = load_history_data(history_type, class_name)
        # 初始化学生抽取次数字典
        student_counts = {}
        for student in candidates:
            student_name = _get_student_name(student)
            if student_name:
                # 从历史记录中获取该学生的抽取次数
                student_history = history_data.get("students", {}).get(student_name, {})

                # 如果有科目过滤，使用科目统计
                if subject_filter and history_type == "roll_call":
                    subject_stats = student_history.get("subject_stats", {})
                    if subject_filter in subject_stats:
                        student_counts[student_name] = subject_stats[
                            subject_filter
                        ].get("total_count", 0)
                    else:
                        # 如果科目统计中没有该科目，从历史记录中计算
                        history = student_history.get("history", [])
                        filtered_count = 0
                        for record in history:
                            if record.get("class_name", "") == subject_filter:
                                filtered_count += 1
                        student_counts[student_name] = filtered_count
                else:
                    # 没有科目过滤，使用总次数
                    student_counts[student_name] = student_history.get("total_count", 0)

        # 获取所有学生的抽取次数列表
        counts = list(student_counts.values())
        if not counts:
            logger.debug("没有获取到抽取次数，直接返回候选列表")
            return candidates

        # Step 2: 计算平均值和统计信息
        avg = sum(counts) / len(counts)
        min_count = min(counts)
        max_count = max(counts)

        logger.debug(
            f"当前平均值: {avg:.2f}, 最小次数: {min_count}, 最大次数: {max_count}"
        )

        # Step 3: 初始候选池从最低次数开始，避免保护高于平均值的人
        pool_initial = _filter_candidates_by_max_count(
            candidates, student_counts, min_count
        )

        # Step 4: 最大差距保护检查
        max_allowed_count = max_count
        if max_count - min_count > gap_threshold:
            logger.debug("检测到差距超过阈值，执行差距保护")
            max_allowed_count = min(min_count + gap_threshold, max_count)
            pool_initial = _filter_candidates_by_max_count(
                candidates, student_counts, min_count
            )
            logger.debug(
                f"差距保护上限: {max_allowed_count}, 最低次数候选人数: {len(pool_initial)}"
            )

        logger.debug(f"初始候选池人数: {len(pool_initial)}")

        # Step 5: 综合处理 - 人数不足时向上补齐 + 候选池最小人数保障
        # 计算需要满足的最小池大小（取draw_count和min_pool_size中的较大值）
        required_size = max(draw_count, min_pool_size)

        if len(pool_initial) < required_size:
            logger.debug(
                f"候选池人数({len(pool_initial)})低于所需大小({required_size})，执行扩展"
            )

            new_threshold = min_count

            logger.debug(f"当前平均次数: {avg:.2f}, 初始扩展阈值: {new_threshold}")

            # 扩展候选池
            expanded_pool = _get_expanded_pool(
                candidates,
                student_counts,
                required_size,
                new_threshold,
                max_allowed_count,
            )

            # 如果在保护上限内仍不足，才降级使用所有候选学生
            if len(expanded_pool) < required_size:
                logger.debug(
                    f"扩大到保护上限({max_allowed_count})后仍不足，使用所有候选学生"
                )
                expanded_pool = candidates.copy()

            # 按次数从小到大排序
            pool_initial = _sort_candidates_by_count(expanded_pool, student_counts)

            # 如果人数仍然超过需求，只保留前required_size个
            if len(pool_initial) > required_size:
                pool_initial = pool_initial[:required_size]

        logger.debug(f"扩展后候选池人数: {len(pool_initial)}")

        # Step 6: 最终检查 - 确保候选池不为空
        if not pool_initial:
            logger.debug("最终候选池为空，使用所有候选人")
            pool_initial = candidates.copy()
            pool_initial = _sort_candidates_by_count(pool_initial, student_counts)

    except Exception as e:
        logger.exception(f"应用平均值差值保护时发生错误: {e}", exc_info=True)
        # 发生错误时，返回原始候选列表，确保系统可用性
        return candidates

    logger.debug(f"最终候选池人数: {len(pool_initial)}")

    return pool_initial
