"""
SECTL 在线状态上报模块

本模块实现了向 SECTL API 上报在线状态的功能，用于统计在线人数。
客户端应定期调用此接口（建议每 1-2 分钟）来保持在线状态。
服务端会根据 last_active 时间判断用户是否在线（5分钟内活跃视为在线）。
"""

import re
import uuid
import socket
import threading
from concurrent.futures import ThreadPoolExecutor
from typing import Optional, Dict, Any

import requests
from loguru import logger

from app.tools.settings_access import readme_settings_async
from app.tools.variable import (
    SECTL_API_BASE_URL,
    SECTL_PLATFORM_ID,
    SECTL_ONLINE_REPORT_INTERVAL_MS,
    SECTL_ONLINE_REPORT_TIMEOUT_SECONDS,
    SYSTEM,
)


_online_status_reporter: Optional["OnlineStatusReporter"] = None
_executor = ThreadPoolExecutor(max_workers=2, thread_name_prefix="online_status")
_ip_location_cache: Dict[str, Optional[str] | bool] = {
    "initialized": False,
    "ip_address": None,
    "country": None,
    "province": None,
    "city": None,
    "district": None,
}
_ip_location_cache_lock = threading.Lock()


def _detect_device_type() -> str:
    system = SYSTEM
    if system == "windows":
        return "windows-desktop"
    elif system == "macos":
        return "macos-desktop"
    elif system == "linux":
        return "linux-desktop"
    else:
        return "unknown-desktop"


def _get_local_ip() -> str:
    ip = None
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.settimeout(2)
        try:
            s.connect(("8.8.8.8", 53))
            ip = s.getsockname()[0]
        finally:
            s.close()
    except Exception:
        pass

    if not ip or ip.startswith("127."):
        try:
            hostname = socket.gethostname()
            ip = socket.getaddrinfo(hostname, None, socket.AF_INET)[0][4][0]
        except Exception:
            pass

    return ip if ip else "127.0.0.1"


def _get_public_ip(timeout_seconds: float = 5.0) -> Optional[str]:
    services = [
        "https://321260.xyz/api/ip.php",  # v4出口网络 (优先)
        "https://ddns.oray.com/checkip",
        "http://v4.66666.host:66/ip",
        "https://myip.ipip.net",
    ]

    for service in services:
        try:
            response = requests.get(service, timeout=timeout_seconds)
            if response.status_code == 200:
                text = response.text.strip()
                match = re.search(r"\b(?:\d{1,3}\.){3}\d{1,3}\b", text)
                if match:
                    return match.group(0)
        except Exception:
            continue

    logger.warning("所有公网IP服务均获取失败")
    return None


def _get_ip_location(ip: str, timeout_seconds: float = 10.0) -> Dict[str, Any]:
    if (
        not ip
        or ip == "unknown"
        or ip.startswith("127.")
        or ip == "localhost"
        or ip.startswith("192.168.")
        or ip.startswith("10.")
    ):
        return {
            "country": "本地",
            "province": "本地",
            "city": "本地",
            "district": "本地",
        }

    try:
        response = requests.get(
            f"https://uapis.cn/api/v1/network/ipinfo?ip={ip}&source=commercial",
            timeout=timeout_seconds,
        )

        if not response.ok:
            raise Exception(f"HTTP error! status: {response.status_code}")

        data = response.json()

        if data.get("ip"):
            region_parts = [
                p.strip() for p in (data.get("region") or "").split(" ") if p.strip()
            ]
            return {
                "country": region_parts[0] if len(region_parts) > 0 else "未知",
                "province": region_parts[1] if len(region_parts) > 1 else "未知",
                "city": region_parts[2] if len(region_parts) > 2 else "未知",
                "district": data.get("district")
                or (region_parts[3] if len(region_parts) > 3 else "未知"),
            }
        else:
            raise Exception(f"IP lookup failed: {data.get('msg', 'Unknown error')}")
    except Exception:
        return {
            "country": "未知",
            "province": "未知",
            "city": "未知",
            "district": "未知",
        }


def _build_ip_location_cache() -> Dict[str, str]:
    telemetry_mode = readme_settings_async("basic_settings", "telemetry_mode") or "full"
    if telemetry_mode == "anonymous":
        return {
            "ip_address": "0.0.0.0",
            "country": "未知",
            "province": "未知",
            "city": "未知",
            "district": "未知",
        }

    ip_address = _get_public_ip(SECTL_ONLINE_REPORT_TIMEOUT_SECONDS)
    if not ip_address:
        ip_address = _get_local_ip()

    location = _get_ip_location(ip_address, SECTL_ONLINE_REPORT_TIMEOUT_SECONDS)
    return {
        "ip_address": ip_address,
        "country": location.get("country", "未知"),
        "province": location.get("province", "未知"),
        "city": location.get("city", "未知"),
        "district": location.get("district", "未知"),
    }


def initialize_ip_location_cache(
    force_refresh: bool = False,
) -> Dict[str, Optional[str] | bool]:
    """初始化在线状态上报使用的 IP 与位置信息缓存。"""
    with _ip_location_cache_lock:
        if _ip_location_cache.get("initialized") and not force_refresh:
            return dict(_ip_location_cache)

    cache_data = _build_ip_location_cache()

    with _ip_location_cache_lock:
        _ip_location_cache.update(cache_data)
        _ip_location_cache["initialized"] = True
        return dict(_ip_location_cache)


def _get_ip_location_cache() -> Dict[str, Optional[str] | bool]:
    with _ip_location_cache_lock:
        return dict(_ip_location_cache)


def _load_or_create_device_uuid() -> str:
    device_uuid = readme_settings_async("basic_settings", "offline_user_id")
    if device_uuid:
        return device_uuid

    device_uuid = str(uuid.uuid4()).lower()
    from app.tools.settings_access import update_settings

    update_settings("basic_settings", "offline_user_id", device_uuid)
    return device_uuid


def _do_report(
    platform_id: str,
    device_uuid: str,
    device_type: str,
    ip_address: Optional[str],
    country: Optional[str],
    province: Optional[str],
    city: Optional[str],
    district: Optional[str],
) -> Dict[str, Any]:
    telemetry_mode = readme_settings_async("basic_settings", "telemetry_mode") or "full"
    report_ip = telemetry_mode != "anonymous"
    logger.debug(f"上报模式: {telemetry_mode}, report_ip={report_ip}")

    if report_ip:
        ip_address = ip_address or "unknown"
        country = country or "未知"
        province = province or "未知"
        city = city or "未知"
        district = district or "未知"
    else:
        ip_address = "0.0.0.0"
        country = "未知"
        province = "未知"
        city = "未知"
        district = "未知"

    payload = {
        "platform_id": platform_id,
        "device_uuid": device_uuid,
        "device_type": device_type,
        "ip_address": ip_address,
        "country": country,
        "province": province,
        "city": city,
        "district": district,
    }

    try:
        response = requests.post(
            f"{SECTL_API_BASE_URL}/api/stats/online",
            json=payload,
            timeout=SECTL_ONLINE_REPORT_TIMEOUT_SECONDS,
        )

        if response.status_code >= 400:
            try:
                error_data = response.json()
                logger.warning(
                    f"上报在线状态失败: {error_data.get('error_description', error_data.get('error', 'Unknown error'))}"
                )
            except Exception:
                logger.warning(f"上报在线状态失败: HTTP {response.status_code}")
            return {"success": False, "error": "request_failed"}

        result = response.json()
        online_count = result.get("online_count", 0)
        update_online_count_cache(online_count)
        if report_ip:
            logger.info(f"上报在线状态成功，当前在线人数: {online_count}")
        else:
            logger.info("上报在线状态成功")
        return result

    except requests.exceptions.Timeout:
        logger.warning("上报在线状态超时")
        return {"success": False, "error": "timeout"}
    except requests.exceptions.ConnectionError:
        logger.warning("上报在线状态连接失败")
        return {"success": False, "error": "connection_error"}
    except Exception as e:
        logger.warning(f"上报在线状态失败: {e}")
        return {"success": False, "error": str(e)}


def report_online_status_async(
    platform_id: Optional[str] = None,
    device_uuid: Optional[str] = None,
    device_type: Optional[str] = None,
    ip_address: Optional[str] = None,
    country: Optional[str] = None,
    province: Optional[str] = None,
    city: Optional[str] = None,
    district: Optional[str] = None,
):
    platform_id = platform_id or SECTL_PLATFORM_ID
    device_uuid = device_uuid or _load_or_create_device_uuid()
    device_type = device_type or _detect_device_type()
    cache = _get_ip_location_cache()
    if cache.get("initialized"):
        ip_address = ip_address or str(cache.get("ip_address") or "unknown")
        country = country or str(cache.get("country") or "未知")
        province = province or str(cache.get("province") or "未知")
        city = city or str(cache.get("city") or "未知")
        district = district or str(cache.get("district") or "未知")

    _executor.submit(
        _do_report,
        platform_id,
        device_uuid,
        device_type,
        ip_address,
        country,
        province,
        city,
        district,
    )


class OnlineStatusReporter:
    """在线状态上报器，负责定期上报在线状态（后台线程执行）"""

    def __init__(
        self,
        platform_id: Optional[str] = None,
        report_interval_ms: Optional[int] = None,
    ):
        self.platform_id = platform_id or SECTL_PLATFORM_ID
        self.report_interval_ms = report_interval_ms or SECTL_ONLINE_REPORT_INTERVAL_MS
        self.device_uuid = _load_or_create_device_uuid()
        self.device_type = _detect_device_type()
        self._ip_address: Optional[str] = None
        self._country: Optional[str] = None
        self._province: Optional[str] = None
        self._city: Optional[str] = None
        self._district: Optional[str] = None
        self._timer: Optional[threading.Timer] = None
        self._is_running = False
        self._initialized = False
        self._lock = threading.Lock()

    def _init_ip_and_location_async(self):
        def _do_init():
            cache = initialize_ip_location_cache()
            self._ip_address = str(cache.get("ip_address") or "unknown")
            self._country = str(cache.get("country") or "未知")
            self._province = str(cache.get("province") or "未知")
            self._city = str(cache.get("city") or "未知")
            self._district = str(cache.get("district") or "未知")
            with self._lock:
                self._initialized = True
            logger.debug("在线状态上报器初始化完成")
            self._report_async()

        _executor.submit(_do_init)

    def _schedule_next_report(self):
        """调度下一次上报"""
        with self._lock:
            if not self._is_running:
                return
            self._timer = threading.Timer(
                self.report_interval_ms / 1000.0, self._on_timer_tick
            )
            self._timer.daemon = True
            self._timer.start()

    def _on_timer_tick(self):
        """定时器触发时的回调"""
        self._report_async()
        self._schedule_next_report()

    def start(self):
        with self._lock:
            if self._is_running:
                return

            self._is_running = True
            logger.debug("在线状态上报器正在启动...")

        self._init_ip_and_location_async()
        self._schedule_next_report()
        logger.debug(f"在线状态上报器已启动，上报间隔: {self.report_interval_ms}ms")

    def stop(self):
        with self._lock:
            if not self._is_running:
                return

            self._is_running = False
            if self._timer:
                self._timer.cancel()
                self._timer = None
        logger.debug("在线状态上报器已停止")

    def _report_async(self):
        if not self._is_running:
            return

        if not self._initialized:
            logger.debug("在线状态上报器尚未完成 IP 与位置缓存初始化，跳过本次上报")
            return

        logger.debug("触发在线状态上报")

        _executor.submit(
            _do_report,
            self.platform_id,
            self.device_uuid,
            self.device_type,
            self._ip_address,
            self._country,
            self._province,
            self._city,
            self._district,
        )

    def report_now(self):
        self._report_async()


def get_online_status_reporter() -> Optional[OnlineStatusReporter]:
    return _online_status_reporter


def initialize_online_status_reporter(
    platform_id: Optional[str] = None,
    report_interval_ms: Optional[int] = None,
) -> OnlineStatusReporter:
    global _online_status_reporter

    if _online_status_reporter is not None:
        return _online_status_reporter

    _online_status_reporter = OnlineStatusReporter(
        platform_id=platform_id,
        report_interval_ms=report_interval_ms,
    )
    return _online_status_reporter


def start_online_status_reporter():
    global _online_status_reporter

    if _online_status_reporter is None:
        _online_status_reporter = initialize_online_status_reporter()

    _online_status_reporter.start()
    try:
        from app.tools.platform_report import record_app_launch_metric_async

        record_app_launch_metric_async()
    except Exception as e:
        logger.warning(f"记录应用启动自定义报告失败: {e}")


def stop_online_status_reporter():
    global _online_status_reporter

    if _online_status_reporter is not None:
        _online_status_reporter.stop()


def _do_get_online_stats(platform_id: str) -> Dict[str, Any]:
    try:
        response = requests.get(
            f"{SECTL_API_BASE_URL}/api/stats/platform/{platform_id}",
            timeout=SECTL_ONLINE_REPORT_TIMEOUT_SECONDS,
        )

        if response.status_code >= 400:
            return {"success": False, "error": "request_failed"}

        result = response.json()
        return result

    except Exception:
        return {"success": False, "error": "request_failed"}


def get_online_stats_async(callback, platform_id: Optional[str] = None):
    def _do():
        result = _do_get_online_stats(platform_id or SECTL_PLATFORM_ID)
        if callback:
            callback(result)

    _executor.submit(_do)


_online_count_cache = {"count": 0, "updated_at": 0}


def get_cached_online_count() -> int:
    return _online_count_cache.get("count", 0)


def update_online_count_cache(count: int):
    import time

    _online_count_cache["count"] = count
    _online_count_cache["updated_at"] = time.time()
