# ==================================================
# 配置推荐逻辑模块
# ==================================================

from datetime import datetime
from typing import Any

from loguru import logger


def _safe_read(group: str, key: str, default: Any = None) -> Any:
    try:
        from app.tools.settings_access import readme_settings_async

        value = readme_settings_async(group, key)
        return value if value is not None else default
    except Exception:
        logger.debug(f"读取设置失败: {group}.{key}")
        return default


def get_draw_count() -> int:
    try:
        from app.core.app_init import calculate_total_draw_counts

        _, roll_call_total, _ = calculate_total_draw_counts()
        return roll_call_total
    except Exception:
        return 0


BOOLEAN_RULES = [
    ("roll_call_settings", "draw_type", 1, "reason_draw_type_fair"),
    ("lottery_settings", "draw_type", 1, "reason_draw_type_fair"),
    ("quick_draw_settings", "draw_type", 1, "reason_draw_type_fair"),
    ("fair_draw_settings", "enable_avg_gap_protection", True, "reason_enable_avg_gap"),
    ("fair_draw_settings", "fair_draw_time", True, "reason_fair_draw_time"),
    ("fair_draw_settings", "cold_start_enabled", True, "reason_cold_start"),
    ("fair_draw_settings", "shield_enabled", True, "reason_shield"),
]

NUMERIC_KEYS = [
    "gap_threshold",
    "min_pool_size",
    "cold_start_rounds",
    "shield_time",
    "frequency_function",
    "frequency_weight",
    "group_weight",
    "gender_weight",
    "time_weight",
    "base_weight",
    "min_weight",
    "max_weight",
    "shield_time_unit",
]


def load_roll_call_history():
    from app.common.history.file_utils import get_all_history_names, load_history_data

    per_student = {}
    total_groups = {}
    total_genders = {}
    total_draws = 0
    earliest_time = None
    latest_time = None
    student_meta = {}

    class_names = get_all_history_names("roll_call")
    for cn in class_names:
        data = load_history_data("roll_call", cn)
        total_draws += int(data.get("total_rounds", 0) or 0)
        students = data.get("students", {})
        if not isinstance(students, dict):
            continue
        for name, info in students.items():
            if not isinstance(info, dict):
                continue
            sessions = info.get("history", [])
            if not isinstance(sessions, list):
                continue
            count = len(sessions)
            per_student[name] = per_student.get(name, 0) + count

            rounds_missed = info.get("rounds_missed", 0)
            last_drawn = info.get("last_drawn_time", "")
            student_meta[name] = {
                "rounds_missed": max(
                    student_meta.get(name, {}).get("rounds_missed", 0), rounds_missed
                ),
                "last_drawn": last_drawn
                or student_meta.get(name, {}).get("last_drawn", ""),
            }

            if info.get("group"):
                total_groups[info["group"]] = total_groups.get(info["group"], 0) + count
            if info.get("gender"):
                total_genders[info["gender"]] = (
                    total_genders.get(info["gender"], 0) + count
                )

            for s in sessions:
                t = s.get("draw_time", "")
                if t:
                    try:
                        dt = datetime.strptime(t, "%Y-%m-%d %H:%M:%S")
                        if earliest_time is None or dt < earliest_time:
                            earliest_time = dt
                        if latest_time is None or dt > latest_time:
                            latest_time = dt
                    except ValueError:
                        pass

    return {
        "per_student": per_student,
        "total_draws": total_draws,
        "total_students": len(per_student),
        "groups": total_groups,
        "genders": total_genders,
        "meta": student_meta,
        "earliest_time": earliest_time,
        "latest_time": latest_time,
    }


def compute_fairness_metrics(stats):
    per_student = stats["per_student"]
    if not per_student:
        return {}

    counts = list(per_student.values())
    n = len(counts)
    total = sum(counts)
    mean = total / n
    variance = sum((c - mean) ** 2 for c in counts) / n
    std_dev = round(variance**0.5, 2)
    max_val = max(counts)
    min_val = min(counts)
    gap = max_val - min_val
    zeros = sum(1 for c in counts if c == 0)
    max_one_pct = round(max_val / total * 100, 1) if total > 0 else 0

    meta = stats.get("meta", {})
    missed = [m.get("rounds_missed", 0) for m in meta.values()]
    max_missed = max(missed) if missed else 0

    groups = stats.get("groups", {})
    genders = stats.get("genders", {})
    group_vals = list(groups.values())
    gender_vals = list(genders.values())

    if len(group_vals) >= 2:
        avg_g = sum(group_vals) / len(group_vals)
        group_imbalance = round(max(group_vals) / avg_g, 2)
    else:
        group_imbalance = 1.0

    if len(gender_vals) >= 2:
        avg_ge = sum(gender_vals) / len(gender_vals)
        gender_imbalance = round(max(gender_vals) / avg_ge, 2)
    else:
        gender_imbalance = 1.0

    days_span = 1
    if stats.get("earliest_time") and stats.get("latest_time"):
        days_span = (stats["latest_time"] - stats["earliest_time"]).days
        days_span = max(1, days_span)

    return {
        "mean": round(mean, 2),
        "std_dev": std_dev,
        "max": max_val,
        "min": min_val,
        "gap": gap,
        "zeros": zeros,
        "total_students": n,
        "total_draws": total,
        "max_one_pct": max_one_pct,
        "max_missed": max_missed,
        "group_imbalance": group_imbalance,
        "gender_imbalance": gender_imbalance,
        "days_span": days_span,
    }


def compute_recommended_values(metrics):
    v = {}
    r = {}

    std_dev = metrics.get("std_dev", 0)
    n = metrics.get("total_students", 1)
    max_missed = metrics.get("max_missed", 0)
    total_draws = metrics.get("total_draws", 1)
    group_imb = metrics.get("group_imbalance", 1.0)
    gender_imb = metrics.get("gender_imbalance", 1.0)
    days_span = metrics.get("days_span", 1)
    zeros = metrics.get("zeros", 0)
    gap = metrics.get("gap", 0)

    v["gap_threshold"] = max(1, min(10, int(std_dev) + 1))
    r["gap_threshold"] = (
        f"标准差为 {std_dev}，最大-最小差距为 {gap}，建议差值阈值设为 {v['gap_threshold']}"
    )

    v["min_pool_size"] = max(3, min(n, int(n * 0.25)))
    r["min_pool_size"] = (
        f"共 {n} 人参与，建议候选池最少保留 {v['min_pool_size']} 人以保证多样性"
    )

    v["cold_start_rounds"] = max(10, max_missed + 5)
    r["cold_start_rounds"] = (
        f"最大遗漏 {max_missed} 轮，{zeros} 人从未被抽中，建议冷启动保护 {v['cold_start_rounds']} 轮"
    )

    daily_draws = total_draws / days_span
    interval_sec = 86400 / max(daily_draws, 0.01)
    v["shield_time"] = round(max(3.0, min(60.0, interval_sec * 0.3)), 1)
    r["shield_time"] = (
        f"日均抽取 {daily_draws:.1f} 次，建议屏蔽时间 {v['shield_time']} 秒避免重复"
    )

    if std_dev <= 1:
        v["frequency_function"] = 0
        fn_name = "线性"
    elif std_dev <= 3:
        v["frequency_function"] = 1
        fn_name = "二次"
    else:
        v["frequency_function"] = 2
        fn_name = "指数"
    r["frequency_function"] = (
        f"标准差为 {std_dev}，分布{'均匀' if std_dev <= 1 else '适中' if std_dev <= 3 else '不均'}，建议使用{fn_name}惩罚函数"
    )

    v["frequency_weight"] = round(max(0.5, min(10.0, std_dev * 0.8)), 2)
    r["frequency_weight"] = (
        f"基于标准差 {std_dev}，建议频率惩罚权重设为 {v['frequency_weight']}"
    )

    v["group_weight"] = round(max(0.3, min(3.0, 0.8 * group_imb)), 2)
    r["group_weight"] = (
        f"小组不均衡指数 {group_imb}，建议小组权重调整为 {v['group_weight']}"
    )

    v["gender_weight"] = round(max(0.3, min(3.0, 0.8 * gender_imb)), 2)
    r["gender_weight"] = (
        f"性别不均衡指数 {gender_imb}，建议性别权重调整为 {v['gender_weight']}"
    )

    v["time_weight"] = round(max(0.2, min(3.0, 0.3 + max_missed * 0.05)), 2)
    r["time_weight"] = (
        f"最大遗漏 {max_missed} 轮，建议时间权重因子设为 {v['time_weight']}"
    )

    v["base_weight"] = 1.0
    r["base_weight"] = "基础权重保持默认值 1.0"

    v["min_weight"] = round(max(0.1, min(1.0, 0.5 - std_dev * 0.05)), 2)
    r["min_weight"] = (
        f"标准差 {std_dev}，为避免权重过低，建议最小值设为 {v['min_weight']}"
    )

    v["max_weight"] = round(max(2.0, min(20.0, 3.0 + std_dev * 1.5)), 2)
    r["max_weight"] = (
        f"标准差 {std_dev}，为控制权重上限，建议最大值设为 {v['max_weight']}"
    )

    v["shield_time_unit"] = 0
    r["shield_time_unit"] = "屏蔽时间单位保持默认值"

    v["_reasons"] = r
    return v


def get_simple_recommendations() -> list:
    recs = []
    for group, key, recommended, reason_key in BOOLEAN_RULES:
        recs.append(
            {
                "group": group,
                "key": key,
                "current": False if isinstance(recommended, bool) else 0,
                "recommended": recommended,
                "reason_key": reason_key,
            }
        )

    stats = load_roll_call_history()
    metrics = compute_fairness_metrics(stats)
    computed = compute_recommended_values(metrics) if metrics else {}
    reasons = computed.get("_reasons", {})

    for key in NUMERIC_KEYS:
        recs.append(
            {
                "group": "fair_draw_settings",
                "key": key,
                "current": 0,
                "recommended": computed.get(key, 0),
                "reason_text": reasons.get(key, ""),
            }
        )
    return recs


def get_recommendations() -> list:
    recs = []
    for group, key, recommended, reason_key in BOOLEAN_RULES:
        current = _safe_read(group, key)
        if current is None:
            continue
        if current != recommended:
            recs.append(
                {
                    "group": group,
                    "key": key,
                    "current": current,
                    "recommended": recommended,
                    "reason_key": reason_key,
                }
            )

    stats = load_roll_call_history()
    metrics = compute_fairness_metrics(stats)
    computed = compute_recommended_values(metrics) if metrics else {}
    reasons = computed.get("_reasons", {})

    for key in NUMERIC_KEYS:
        current = _safe_read("fair_draw_settings", key)
        if current is None:
            continue
        rec_val = computed.get(key)
        if rec_val is not None and current != rec_val:
            recs.append(
                {
                    "group": "fair_draw_settings",
                    "key": key,
                    "current": current,
                    "recommended": rec_val,
                    "reason_text": reasons.get(key, ""),
                }
            )
    return recs
