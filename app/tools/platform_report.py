"""SECTL 统计增量上报工具。"""

from __future__ import annotations

import threading
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime

import requests
from loguru import logger

from app.tools.settings_access import (
    get_int_setting,
    readme_settings_async,
    update_settings,
)
from app.tools.variable import (
    SECTL_API_BASE_URL,
    SECTL_ONLINE_REPORT_TIMEOUT_SECONDS,
    SECTL_PLATFORM_ID,
)


_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="platform_report")
_delayed_report_timer: threading.Timer | None = None
_delayed_report_lock = threading.Lock()
_pending_increment_lock = threading.Lock()
_pending_increments: dict[tuple[str, str], int] = {}
_REPORT_DELAY_SECONDS = 3


EVENT_METRICS = {
    "roll_call": {
        "daily": ("roll_call_daily_count", "daily_roll_call_count"),
        "weekly": ("roll_call_weekly_count", "weekly_roll_call_count"),
        "monthly": ("roll_call_monthly_count", "monthly_roll_call_count"),
        "total": ("roll_call_total_count", "total_roll_call_count"),
    },
    "lottery": {
        "daily": ("lottery_daily_count", "daily_lottery_count"),
        "weekly": ("lottery_weekly_count", "weekly_lottery_count"),
        "monthly": ("lottery_monthly_count", "monthly_lottery_count"),
        "total": ("lottery_total_count", "total_lottery_count"),
    },
    "app_launch": {
        "daily": ("app_launch_daily_count", "daily_app_launch_count"),
        "weekly": ("app_launch_weekly_count", "weekly_app_launch_count"),
        "monthly": ("app_launch_monthly_count", "monthly_app_launch_count"),
        "total": ("app_launch_total_count", "total_app_launch_count"),
    },
}


def _headers() -> dict[str, str]:
    return {"Content-Type": "application/json"}


def _usage_increment_url() -> str:
    return f"{SECTL_API_BASE_URL.rstrip('/')}/api/stats/usage/increment"


def _period_values(now: datetime) -> dict[str, str]:
    year, week, _ = now.isocalendar()
    return {
        "daily": now.strftime("%Y-%m-%d"),
        "weekly": f"{year}-W{week:02d}",
        "monthly": now.strftime("%Y-%m"),
        "total": "all",
    }


def _reset_period_counts_if_needed(now: datetime):
    periods = _period_values(now)
    stored_day = readme_settings_async("user_info", "custom_report_day") or ""
    stored_week = readme_settings_async("user_info", "custom_report_week") or ""
    stored_month = readme_settings_async("user_info", "custom_report_month") or ""

    if stored_day != periods["daily"]:
        for key in (
            "roll_call_daily_count",
            "lottery_daily_count",
            "app_launch_daily_count",
        ):
            update_settings("user_info", key, 0)
        update_settings("user_info", "custom_report_day", periods["daily"])

    if stored_week != periods["weekly"]:
        for key in (
            "roll_call_weekly_count",
            "lottery_weekly_count",
            "app_launch_weekly_count",
        ):
            update_settings("user_info", key, 0)
        update_settings("user_info", "custom_report_week", periods["weekly"])

    if stored_month != periods["monthly"]:
        for key in (
            "roll_call_monthly_count",
            "lottery_monthly_count",
            "app_launch_monthly_count",
        ):
            update_settings("user_info", key, 0)
        update_settings("user_info", "custom_report_month", periods["monthly"])


def _increment_setting(key: str) -> int:
    value = get_int_setting("user_info", key, 0) + 1
    update_settings("user_info", key, value)
    return value


def _add_pending_increment(field_key: str, period: str, delta: int = 1):
    with _pending_increment_lock:
        key = (field_key, period)
        _pending_increments[key] = _pending_increments.get(key, 0) + delta


def _take_pending_increments() -> dict[tuple[str, str], int]:
    with _pending_increment_lock:
        increments = dict(_pending_increments)
        _pending_increments.clear()
        return increments


def _restore_pending_increments(increments: dict[tuple[str, str], int]):
    with _pending_increment_lock:
        for key, delta in increments.items():
            _pending_increments[key] = _pending_increments.get(key, 0) + delta


def _record_event_counts(event_name: str):
    metrics = EVENT_METRICS.get(event_name)
    if not metrics:
        return

    now = datetime.now()
    periods = _period_values(now)
    _reset_period_counts_if_needed(now)

    for period_kind, (setting_key, field_key) in metrics.items():
        _increment_setting(setting_key)
        _add_pending_increment(field_key, periods[period_kind])

    if event_name in {"roll_call", "lottery"}:
        _increment_setting("total_draw_count")


def _report_increment_batch(increments: dict[tuple[str, str], int]) -> bool:
    for (field_key, period), delta in increments.items():
        payload = {
            "platform_id": SECTL_PLATFORM_ID,
            "field_key": field_key,
            "delta": delta,
            "period": period,
        }
        try:
            response = requests.post(
                _usage_increment_url(),
                json=payload,
                headers=_headers(),
                timeout=SECTL_ONLINE_REPORT_TIMEOUT_SECONDS,
            )
        except requests.exceptions.Timeout:
            logger.warning(f"上报统计增量超时: {field_key}, period={period}")
            return False
        except requests.exceptions.ConnectionError:
            logger.warning(f"上报统计增量连接失败: {field_key}, period={period}")
            return False
        except Exception as e:
            logger.warning(f"上报统计增量失败: {field_key}, period={period}, error={e}")
            return False

        if response.status_code >= 400:
            logger.warning(
                f"上报统计增量失败: {field_key}, period={period}, HTTP {response.status_code}"
            )
            return False
    return True


def report_platform_metrics_async():
    """异步上报当前待发送的统计增量。"""
    if readme_settings_async("basic_settings", "telemetry_mode") == "off":
        return

    increments = _take_pending_increments()
    if not increments:
        return

    def _do_report():
        if not _report_increment_batch(increments):
            _restore_pending_increments(increments)

    _executor.submit(_do_report)


def report_platform_metrics_delayed():
    """延迟上报统计增量，合并短时间内的多次抽取。"""
    if readme_settings_async("basic_settings", "telemetry_mode") == "off":
        return

    global _delayed_report_timer
    with _delayed_report_lock:
        if _delayed_report_timer is not None:
            _delayed_report_timer.cancel()
        _delayed_report_timer = threading.Timer(
            _REPORT_DELAY_SECONDS, report_platform_metrics_async
        )
        _delayed_report_timer.daemon = True
        _delayed_report_timer.start()


def record_roll_call_metric_async():
    """记录一次点名并延迟上报统计增量。"""
    _record_event_counts("roll_call")
    report_platform_metrics_delayed()


def record_lottery_metric_async():
    """记录一次抽奖并延迟上报统计增量。"""
    _record_event_counts("lottery")
    report_platform_metrics_delayed()


def record_app_launch_metric_async():
    """记录一次应用启动并异步上报统计增量。"""
    _record_event_counts("app_launch")
    report_platform_metrics_async()
