from __future__ import annotations

import re
import os
import sys
import threading
import time
from dataclasses import dataclass
from typing import Any, Union

from loguru import logger


CameraSource = Union[int, str]


@dataclass(frozen=True)
class CameraDeviceInfo:
    """从 Qt 枚举并映射到 OpenCV 捕获源的摄像头设备。"""

    name: str
    source: CameraSource
    qt_id: str


_CACHE_LOCK = threading.Lock()
_CACHED_DEVICES: list[CameraDeviceInfo] | None = None
_CACHED_AT: float = 0.0
_OPENCV_WARMUP_STARTED = False
_QT_WARMUP_INFLIGHT = False
_QT_WARMUP_THREAD: object | None = None
_QT_WARMUP_WORKER: object | None = None
_QT_WARMUP_HOOKED = False
_RESOLUTION_CACHE_LOCK = threading.Lock()
_RESOLUTION_CACHE: dict[str, tuple[float, list[tuple[int, int]]]] = {}
_RESOLUTION_CACHE_TTL_S = 300.0
_FALLBACK_RESOLUTIONS: list[tuple[int, int]] = [
    (3840, 2160),
    (2560, 1440),
    (2560, 1080),
    (1920, 1200),
    (1920, 1080),
    (1600, 1200),
    (1600, 900),
    (1366, 768),
    (1280, 1024),
    (1280, 960),
    (1280, 800),
    (1280, 720),
    (1024, 768),
    (800, 600),
    (640, 480),
    (320, 240),
]


def _silence_opencv_logs(cv2: Any) -> None:
    try:
        os.environ.setdefault("OPENCV_LOG_LEVEL", "SILENT")
    except Exception:
        pass

    try:
        utils = getattr(cv2, "utils", None)
        logging_mod = getattr(utils, "logging", None) if utils is not None else None
        setter = getattr(logging_mod, "setLogLevel", None)
        if callable(setter):
            level = getattr(logging_mod, "LOG_LEVEL_SILENT", None)
            if level is None:
                level = getattr(logging_mod, "LOG_LEVEL_ERROR", None)
            if level is not None:
                setter(level)
    except Exception:
        pass

    try:
        setter = getattr(cv2, "setLogLevel", None)
        if callable(setter):
            level = getattr(cv2, "LOG_LEVEL_SILENT", None)
            if level is None:
                level = getattr(cv2, "LOG_LEVEL_ERROR", None)
            setter(0 if level is None else level)
    except Exception:
        pass

    try:
        redirect = getattr(cv2, "redirectError", None)
        if callable(redirect):
            redirect(lambda *args: 0)
    except Exception:
        pass


def _qt_device_id_to_string(device_id: Any) -> str:
    """将 Qt 摄像头 id 对象（通常是 QByteArray）转换为可读字符串。"""
    try:
        raw = bytes(device_id)
    except Exception:
        try:
            raw = str(device_id).encode("utf-8", errors="ignore")
        except Exception:
            return ""

    try:
        return raw.decode("utf-8", errors="ignore")
    except Exception:
        return ""


def _infer_opencv_source(qt_id: str, name: str, fallback_index: int) -> CameraSource:
    """从 Qt 摄像头 id 推断 OpenCV VideoCapture 源。"""
    if qt_id.startswith("/dev/"):
        return qt_id

    match = re.search(r"video(\d+)", qt_id, flags=re.IGNORECASE)
    if match:
        try:
            return int(match.group(1))
        except Exception:
            return fallback_index

    return fallback_index


def _get_cached_devices() -> list[CameraDeviceInfo] | None:
    with _CACHE_LOCK:
        if _CACHED_DEVICES is None:
            return None
        return list(_CACHED_DEVICES)


def _set_cached_devices(devices: list[CameraDeviceInfo]) -> None:
    global _CACHED_DEVICES, _CACHED_AT
    with _CACHE_LOCK:
        _CACHED_DEVICES = list(devices)
        _CACHED_AT = time.monotonic()


def _list_cameras_via_qt() -> list[CameraDeviceInfo]:
    import os

    if sys.platform.startswith("win"):
        os.environ.setdefault("OPENCV_VIDEOIO_PRIORITY_MSMF", "1")

    try:
        from PySide6.QtMultimedia import QMediaDevices  # type: ignore

        qt_devices = list(QMediaDevices.videoInputs())
    except Exception as exc:
        logger.warning("通过 QtMultimedia 枚举摄像头失败: {}", exc)
        qt_devices = []

    results: list[CameraDeviceInfo] = []
    for index, device in enumerate(qt_devices):
        try:
            name = device.description() or f"Camera {index}"
        except Exception:
            name = f"Camera {index}"

        try:
            qt_id = _qt_device_id_to_string(device.id())
        except Exception:
            qt_id = ""

        source = _infer_opencv_source(qt_id, name, fallback_index=index)
        results.append(CameraDeviceInfo(name=name, source=source, qt_id=qt_id))

    logger.info("QtMultimedia 检测到 {} 个摄像头", len(results))
    return results


def _list_cameras_via_opencv() -> list[CameraDeviceInfo]:
    """使用 OpenCV 直接枚举摄像头设备（作为 QtMultimedia 的备用方案）。

    Returns:
        摄像头设备列表。当没有可用摄像头或 OpenCV 不可用时，此列表可能为空。
    """
    try:
        import cv2  # type: ignore
    except Exception as exc:
        logger.warning("OpenCV 导入失败，无法通过 OpenCV 枚举摄像头: {}", exc)
        return []

    _silence_opencv_logs(cv2)

    results: list[CameraDeviceInfo] = []
    max_index = 30

    backend_candidates: list[int] = []
    if sys.platform.startswith("win"):
        backend_candidates = [
            getattr(cv2, "CAP_DSHOW", cv2.CAP_ANY),
            getattr(cv2, "CAP_MSMF", cv2.CAP_ANY),
            cv2.CAP_ANY,
        ]
    elif sys.platform.startswith("linux"):
        backend_candidates = [
            getattr(cv2, "CAP_V4L2", cv2.CAP_ANY),
            cv2.CAP_ANY,
        ]
    elif sys.platform == "darwin":
        backend_candidates = [
            getattr(cv2, "CAP_AVFOUNDATION", cv2.CAP_ANY),
            cv2.CAP_ANY,
        ]
    else:
        backend_candidates = [cv2.CAP_ANY]

    for index in range(max_index):
        cap = None
        for backend in backend_candidates:
            try:
                if backend == cv2.CAP_ANY:
                    cap = cv2.VideoCapture(index)
                else:
                    cap = cv2.VideoCapture(index, backend)
            except Exception:
                continue

            if cap is not None and cap.isOpened():
                try:
                    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
                    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
                    backend_name = _get_backend_name(backend)
                    name = f"Camera {index} ({backend_name}, {width}x{height})"
                    results.append(
                        CameraDeviceInfo(
                            name=name, source=index, qt_id=f"opencv_{index}"
                        )
                    )
                except Exception:
                    results.append(
                        CameraDeviceInfo(
                            name=f"Camera {index}",
                            source=index,
                            qt_id=f"opencv_{index}",
                        )
                    )
                finally:
                    try:
                        cap.release()
                    except Exception:
                        pass
                break

            if cap is not None:
                try:
                    cap.release()
                except Exception:
                    pass

    return results


def _get_backend_name(backend: int) -> str:
    """获取 OpenCV 后端名称。"""
    try:
        import cv2  # type: ignore

        backend_map = {
            getattr(cv2, "CAP_DSHOW", -1): "DirectShow",
            getattr(cv2, "CAP_MSMF", -1): "MSMF",
            getattr(cv2, "CAP_V4L2", -1): "V4L2",
            getattr(cv2, "CAP_AVFOUNDATION", -1): "AVFoundation",
        }
        return backend_map.get(backend, "Unknown")
    except Exception:
        return "Unknown"


def _start_opencv_warmup() -> None:
    if sys.platform.startswith("win"):
        return

    global _OPENCV_WARMUP_STARTED
    with _CACHE_LOCK:
        if _OPENCV_WARMUP_STARTED:
            return
        _OPENCV_WARMUP_STARTED = True

    def _worker():
        try:
            opencv_devices = _list_cameras_via_opencv()
            if not opencv_devices:
                return
            logger.info("通过 OpenCV 检测到 {} 个摄像头", len(opencv_devices))

            cached = _get_cached_devices() or []
            seen_sources = {d.source for d in cached}
            merged = list(cached)
            for device in opencv_devices:
                if device.source not in seen_sources:
                    merged.append(device)
            _set_cached_devices(merged)
        except Exception:
            return

    threading.Thread(target=_worker, daemon=True).start()


def get_cached_camera_devices() -> list[CameraDeviceInfo]:
    return _get_cached_devices() or []


def warmup_camera_devices(force_refresh: bool = False) -> None:
    try:
        list_camera_devices(force_refresh=force_refresh)
    except Exception:
        pass


def warmup_camera_devices_async(force_refresh: bool = False) -> None:
    global _QT_WARMUP_INFLIGHT, _QT_WARMUP_THREAD, _QT_WARMUP_WORKER, _QT_WARMUP_HOOKED

    if not force_refresh:
        cached = _get_cached_devices()
        if cached is not None:
            return

    with _CACHE_LOCK:
        if _QT_WARMUP_INFLIGHT:
            return
        _QT_WARMUP_INFLIGHT = True

    try:
        from PySide6.QtCore import (  # type: ignore
            QObject,
            QThread,
            Signal,
            Slot,
            QCoreApplication,
        )
    except Exception:
        with _CACHE_LOCK:
            _QT_WARMUP_INFLIGHT = False
        return

    class _Worker(QObject):
        finished = Signal(object)

        @Slot()
        def run(self) -> None:
            try:
                devices = _list_cameras_via_qt()
            except Exception:
                devices = []
            self.finished.emit(devices)

    app = None
    try:
        app = QCoreApplication.instance()
    except Exception:
        app = None

    thread = QThread(app)
    worker = _Worker()
    worker.moveToThread(thread)

    def _on_finished(devices_obj: object) -> None:
        global _QT_WARMUP_INFLIGHT

        try:
            devices = list(devices_obj or [])
        except Exception:
            devices = []

        try:
            _set_cached_devices(devices)
            if devices:
                _start_opencv_warmup()
        finally:
            with _CACHE_LOCK:
                _QT_WARMUP_INFLIGHT = False
            try:
                thread.quit()
            except Exception:
                pass
            try:
                from PySide6.QtCore import QThread  # type: ignore

                if QThread.currentThread() is not thread:
                    thread.wait(1200)
            except Exception:
                pass

    def _on_thread_finished() -> None:
        global _QT_WARMUP_THREAD, _QT_WARMUP_WORKER
        obj_worker = _QT_WARMUP_WORKER
        obj_thread = _QT_WARMUP_THREAD
        _QT_WARMUP_WORKER = None
        _QT_WARMUP_THREAD = None

        try:
            if obj_worker is not None:
                obj_worker.deleteLater()
        except Exception:
            pass
        try:
            if obj_thread is not None:
                obj_thread.deleteLater()
        except Exception:
            pass

    def _on_app_about_to_quit() -> None:
        t = _QT_WARMUP_THREAD
        if t is None:
            return
        try:
            t.quit()
        except Exception:
            pass
        try:
            t.wait(800)
        except Exception:
            pass

    worker.finished.connect(_on_finished)
    thread.started.connect(worker.run)
    thread.finished.connect(_on_thread_finished)

    _QT_WARMUP_THREAD = thread
    _QT_WARMUP_WORKER = worker
    if app is not None and not _QT_WARMUP_HOOKED:
        _QT_WARMUP_HOOKED = True
        try:
            app.aboutToQuit.connect(_on_app_about_to_quit)
        except Exception:
            pass
    thread.start()


def list_camera_devices(force_refresh: bool = False) -> list[CameraDeviceInfo]:
    """通过 PySide6 QtMultimedia 枚举本地摄像头设备。

    Returns:
        摄像头设备列表。当没有可用摄像头或 QtMultimedia 在当前环境中
        不可用时，此列表可能为空。
    """
    if not force_refresh:
        cached = _get_cached_devices()
        if cached is not None:
            return cached

    qt_results = _list_cameras_via_qt()
    if qt_results:
        _set_cached_devices(qt_results)
        return list(qt_results)

    opencv_devices = _list_cameras_via_opencv()
    if opencv_devices:
        logger.info("通过 OpenCV 检测到 {} 个摄像头", len(opencv_devices))
        _set_cached_devices(opencv_devices)
        return list(opencv_devices)

    _set_cached_devices([])
    return []


def _normalize_camera_key(camera_id: object) -> str:
    if camera_id is None:
        return ""
    if isinstance(camera_id, bytes):
        try:
            return camera_id.decode("utf-8", errors="ignore")
        except Exception:
            return ""
    try:
        return str(camera_id)
    except Exception:
        return ""


def _list_resolutions_via_qt(qt_id: str) -> list[tuple[int, int]]:
    try:
        from PySide6.QtMultimedia import QMediaDevices  # type: ignore
    except Exception:
        return []

    try:
        qt_devices = list(QMediaDevices.videoInputs())
    except Exception:
        qt_devices = []

    target = ""
    try:
        target = str(qt_id)
    except Exception:
        target = ""
    if not target:
        return []

    results: set[tuple[int, int]] = set()
    for device in qt_devices:
        try:
            device_id = _qt_device_id_to_string(device.id())
        except Exception:
            device_id = ""
        if not device_id or device_id != target:
            continue
        try:
            formats = list(device.videoFormats())
        except Exception:
            formats = []
        for fmt in formats:
            try:
                size = fmt.resolution()
                w = int(size.width())
                h = int(size.height())
                if w > 0 and h > 0:
                    results.add((w, h))
            except Exception:
                continue
        break

    return sorted(results, key=lambda x: (x[0] * x[1], x[0], x[1]), reverse=True)


def _probe_resolutions_via_opencv(source: CameraSource) -> list[tuple[int, int]]:
    try:
        import cv2  # type: ignore
    except Exception:
        return []

    _silence_opencv_logs(cv2)

    backend_candidates: list[int] = []
    if sys.platform.startswith("win"):
        backend_candidates = [
            getattr(cv2, "CAP_DSHOW", cv2.CAP_ANY),
            getattr(cv2, "CAP_MSMF", cv2.CAP_ANY),
            cv2.CAP_ANY,
        ]
    elif sys.platform.startswith("linux"):
        backend_candidates = [
            getattr(cv2, "CAP_V4L2", cv2.CAP_ANY),
            cv2.CAP_ANY,
        ]
    elif sys.platform == "darwin":
        backend_candidates = [
            getattr(cv2, "CAP_AVFOUNDATION", cv2.CAP_ANY),
            cv2.CAP_ANY,
        ]
    else:
        backend_candidates = [cv2.CAP_ANY]

    candidates: list[tuple[int, int]] = [
        (7680, 4320),
        (5120, 2880),
        (3840, 2160),
        (2560, 1440),
        (2560, 1080),
        (1920, 1200),
        (1920, 1080),
        (1680, 1050),
        (1600, 1200),
        (1600, 900),
        (1440, 900),
        (1366, 768),
        (1280, 1024),
        (1280, 960),
        (1280, 800),
        (1280, 720),
        (1024, 768),
        (800, 600),
        (640, 480),
        (320, 240),
    ]

    results: set[tuple[int, int]] = set()

    for backend in backend_candidates:
        cap = None
        try:
            if backend == cv2.CAP_ANY:
                cap = cv2.VideoCapture(source)
            else:
                cap = cv2.VideoCapture(source, backend)
        except Exception:
            cap = None

        if cap is None or not cap.isOpened():
            try:
                if cap is not None:
                    cap.release()
            except Exception:
                pass
            continue

        try:
            w0 = int(round(float(cap.get(getattr(cv2, "CAP_PROP_FRAME_WIDTH", 3)))))
            h0 = int(round(float(cap.get(getattr(cv2, "CAP_PROP_FRAME_HEIGHT", 4)))))
            if w0 > 0 and h0 > 0:
                results.add((w0, h0))
        except Exception:
            pass

        for w, h in candidates:
            try:
                cap.set(getattr(cv2, "CAP_PROP_FRAME_WIDTH", 3), int(w))
                cap.set(getattr(cv2, "CAP_PROP_FRAME_HEIGHT", 4), int(h))
            except Exception:
                continue

            try:
                w1 = int(round(float(cap.get(getattr(cv2, "CAP_PROP_FRAME_WIDTH", 3)))))
                h1 = int(
                    round(float(cap.get(getattr(cv2, "CAP_PROP_FRAME_HEIGHT", 4))))
                )
            except Exception:
                continue

            if w1 != int(w) or h1 != int(h):
                continue

            ok = False
            frame = None
            try:
                ok, frame = cap.read()
            except Exception:
                ok = False
            if not ok or frame is None:
                continue
            try:
                if int(frame.shape[1]) == int(w) and int(frame.shape[0]) == int(h):
                    results.add((int(w), int(h)))
            except Exception:
                results.add((int(w), int(h)))

        try:
            cap.release()
        except Exception:
            pass

        if results:
            break

    return sorted(results, key=lambda x: (x[0] * x[1], x[0], x[1]), reverse=True)


def list_camera_resolutions(camera_id: object) -> list[tuple[int, int]]:
    key = _normalize_camera_key(camera_id)
    if not key:
        return []

    now = time.monotonic()
    with _RESOLUTION_CACHE_LOCK:
        cached = _RESOLUTION_CACHE.get(key)
        if cached is not None:
            cached_at, cached_values = cached
            if now - cached_at <= _RESOLUTION_CACHE_TTL_S:
                return list(cached_values)

    resolutions: list[tuple[int, int]] = []
    if isinstance(camera_id, str):
        resolutions = _list_resolutions_via_qt(camera_id)

    if not resolutions:
        resolutions = list(_FALLBACK_RESOLUTIONS)

    with _RESOLUTION_CACHE_LOCK:
        _RESOLUTION_CACHE[key] = (now, list(resolutions))

    return list(resolutions)


def get_recommended_camera_resolution(camera_id: object) -> tuple[int, int] | None:
    values = list_camera_resolutions(camera_id)
    return values[0] if values else None
