from __future__ import annotations

import math
import shutil
import sys
import threading
from pathlib import Path
from typing import Optional, Tuple

from loguru import logger

from app.tools.path_utils import ensure_dir, get_data_path


Rect = Tuple[int, int, int, int]
_CV2_IMPORT_LOCK = threading.Lock()


def merge_face_rects(frame_size: Tuple[int, int], rects: list[Rect]) -> list[Rect]:
    h = int(frame_size[0]) if frame_size and len(frame_size) > 0 else 0
    w = int(frame_size[1]) if frame_size and len(frame_size) > 1 else 0
    if w <= 0 or h <= 0:
        return rects or []
    if not rects or len(rects) <= 1:
        return rects or []

    def _clamp(r: Rect) -> Rect | None:
        x, y, bw, bh = r
        try:
            x1 = int(x)
            y1 = int(y)
            x2 = int(x + bw)
            y2 = int(y + bh)
        except Exception:
            return None
        if x2 <= x1 or y2 <= y1:
            return None
        x1 = max(0, min(x1, w - 1))
        y1 = max(0, min(y1, h - 1))
        x2 = max(0, min(x2, w))
        y2 = max(0, min(y2, h))
        if x2 <= x1 or y2 <= y1:
            return None
        return (x1, y1, int(x2 - x1), int(y2 - y1))

    cleaned: list[Rect] = []
    for r in rects:
        rr = _clamp(r)
        if rr is not None:
            cleaned.append(rr)
    if len(cleaned) <= 1:
        return cleaned

    frame_area = float(w * h)
    areas = [float(max(1, bw) * max(1, bh)) for _, _, bw, bh in cleaned]
    avg_area = sum(areas) / float(len(areas))
    treat_as_parts = len(cleaned) >= 4 and avg_area < frame_area * 0.01

    def _xyxy(r: Rect) -> tuple[float, float, float, float]:
        x, y, bw, bh = r
        return float(x), float(y), float(x + bw), float(y + bh)

    def _iou(a: Rect, b: Rect) -> float:
        ax1, ay1, ax2, ay2 = _xyxy(a)
        bx1, by1, bx2, by2 = _xyxy(b)
        ix1 = max(ax1, bx1)
        iy1 = max(ay1, by1)
        ix2 = min(ax2, bx2)
        iy2 = min(ay2, by2)
        iw = max(0.0, ix2 - ix1)
        ih = max(0.0, iy2 - iy1)
        inter = iw * ih
        if inter <= 0.0:
            return 0.0
        aa = max(1.0, (ax2 - ax1) * (ay2 - ay1))
        ba = max(1.0, (bx2 - bx1) * (by2 - by1))
        return float(inter / (aa + ba - inter))

    def _expanded_xyxy(r: Rect, margin: float) -> tuple[float, float, float, float]:
        x1, y1, x2, y2 = _xyxy(r)
        m = float(max(0.0, margin))
        return (x1 - m, y1 - m, x2 + m, y2 + m)

    def _intersects(
        a: tuple[float, float, float, float], b: tuple[float, float, float, float]
    ) -> bool:
        ax1, ay1, ax2, ay2 = a
        bx1, by1, bx2, by2 = b
        return (min(ax2, bx2) - max(ax1, bx1)) > 0.0 and (
            min(ay2, by2) - max(ay1, by1)
        ) > 0.0

    parent = list(range(len(cleaned)))

    def _find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def _union(i: int, j: int) -> None:
        ri = _find(i)
        rj = _find(j)
        if ri != rj:
            parent[rj] = ri

    for i in range(len(cleaned)):
        for j in range(i + 1, len(cleaned)):
            if _iou(cleaned[i], cleaned[j]) >= 0.2:
                _union(i, j)
                continue
            if not treat_as_parts:
                continue
            wi = float(max(cleaned[i][2], cleaned[i][3]))
            wj = float(max(cleaned[j][2], cleaned[j][3]))
            margin = 0.6 * float(min(wi, wj))
            if _intersects(
                _expanded_xyxy(cleaned[i], margin), _expanded_xyxy(cleaned[j], margin)
            ):
                _union(i, j)

    clusters: dict[int, list[Rect]] = {}
    for idx, r in enumerate(cleaned):
        root = _find(idx)
        clusters.setdefault(root, []).append(r)

    merged: list[Rect] = []
    for rs in clusters.values():
        if not rs:
            continue
        x1 = min(int(r[0]) for r in rs)
        y1 = min(int(r[1]) for r in rs)
        x2 = max(int(r[0] + r[2]) for r in rs)
        y2 = max(int(r[1] + r[3]) for r in rs)
        rr = _clamp((x1, y1, x2 - x1, y2 - y1))
        if rr is not None:
            merged.append(rr)

    merged.sort(key=lambda r: (r[0], r[1], r[2] * r[3]))
    return merged


def _find_first_existing_model(
    model_dir: Path, candidate_filenames: list[str]
) -> Path | None:
    for name in candidate_filenames:
        p = Path(model_dir) / name
        if p.exists() and p.is_file() and p.stat().st_size > 0:
            return p
    return None


def _find_glob_model(model_dir: Path, pattern: str) -> Path | None:
    try:
        for p in sorted(Path(model_dir).glob(pattern)):
            if p.exists() and p.is_file() and p.stat().st_size > 0:
                return p
    except Exception:
        return None
    return None


def get_cv_models_dir() -> Path:
    return ensure_dir(get_data_path("cv_models"))


def list_onnx_model_filenames(model_dir: Optional[Path] = None) -> list[str]:
    folder = Path(model_dir) if model_dir is not None else get_cv_models_dir()
    try:
        if not folder.exists():
            return []
    except Exception:
        return []
    try:
        items: list[str] = []
        for p in sorted(folder.glob("*.onnx")):
            if p.exists() and p.is_file() and p.stat().st_size > 0:
                items.append(p.name)
        return items
    except Exception:
        return []


def resolve_onnx_model_path(
    model_filename: str, model_dir: Optional[Path] = None
) -> Path:
    text = str(model_filename or "").strip()
    if not text:
        raise FileNotFoundError("model filename is empty")
    folder = Path(model_dir) if model_dir is not None else get_cv_models_dir()
    path = Path(folder) / text
    if path.exists() and path.is_file() and path.stat().st_size > 0:
        return path
    raise FileNotFoundError(str(path))


def get_default_yunet_model_filename() -> str:
    """获取 YuNet 轻量人脸检测模型的默认文件名。"""
    return "face_detection_yunet_2023mar_int8bq.onnx"


def ensure_yunet_model_path(
    *,
    allow_download: bool = False,
    model_url: Optional[str] = None,
    filename: Optional[str] = None,
) -> Path:
    model_dir = ensure_dir(get_data_path("cv_models"))
    if filename:
        target = Path(model_dir) / filename
        if target.exists() and target.is_file() and target.stat().st_size > 0:
            return target
        raise FileNotFoundError(str(target))

    candidates = [
        get_default_yunet_model_filename(),
        "face_detection_yunet_2023mar_int8bq.onnx",
        "face_detection_yunet_2023mar_int8.onnx",
        "face_detection_yunet.onnx",
    ]
    hit = _find_first_existing_model(Path(model_dir), candidates)
    if hit is not None:
        return hit

    glob_hit = _find_glob_model(Path(model_dir), "face_detection_yunet*.onnx")
    if glob_hit is not None:
        return glob_hit

    raise FileNotFoundError(str(Path(model_dir) / get_default_yunet_model_filename()))


def create_yunet_face_detector(
    *,
    model_path: Path,
    input_size: Tuple[int, int] = (320, 320),
    score_threshold: float = 0.85,
    nms_threshold: float = 0.3,
    top_k: int = 5000,
):
    """创建 YuNet 人脸检测器（优先使用 OpenCV FaceDetectorYN）。"""
    import cv2

    if not hasattr(cv2, "FaceDetectorYN"):
        raise RuntimeError("OpenCV FaceDetectorYN is not available in this build")

    def _create(path: Path):
        return cv2.FaceDetectorYN.create(
            str(path),
            "",
            input_size,
            score_threshold,
            nms_threshold,
            top_k,
        )

    try:
        return _create(model_path)
    except Exception:
        import shutil
        import tempfile

        temp_dir = Path(tempfile.gettempdir()) / "SecRandom" / "cv_models"
        try:
            temp_dir.mkdir(parents=True, exist_ok=True)
        except Exception:
            temp_dir = Path(tempfile.gettempdir())
        temp_path = temp_dir / model_path.name
        shutil.copy2(model_path, temp_path)
        return _create(temp_path)


def detect_faces_yunet(
    frame_bgr,
    *,
    detector,
    input_size: Optional[Tuple[int, int]] = None,
    score_threshold: float = 0.85,
) -> list[Rect]:
    """使用 YuNet 检测人脸并返回矩形列表。"""
    import cv2

    h, w = frame_bgr.shape[:2]
    if h <= 0 or w <= 0:
        return []

    in_w = 0
    in_h = 0
    try:
        if input_size is not None:
            in_w = int(input_size[0])
            in_h = int(input_size[1])
    except Exception:
        in_w = 0
        in_h = 0
    if in_w <= 0 or in_h <= 0:
        in_w = int(w)
        in_h = int(h)

    try:
        detector.setInputSize((in_w, in_h))
    except Exception:
        pass

    frame_in = frame_bgr
    if int(in_w) != int(w) or int(in_h) != int(h):
        try:
            frame_in = cv2.resize(
                frame_bgr, (int(in_w), int(in_h)), interpolation=cv2.INTER_LINEAR
            )
        except Exception:
            frame_in = frame_bgr

    try:
        ok, faces = detector.detect(frame_in)
    except Exception as exc:
        raise RuntimeError(f"YuNet detect failed: {exc}") from exc

    if faces is None:
        return []

    scale_x = float(w) / float(in_w) if in_w > 0 else 1.0
    scale_y = float(h) / float(in_h) if in_h > 0 else 1.0

    rects: list[Rect] = []
    try:
        for row in faces:
            x, y, bw, bh, score = row[:5]
            if float(score) < float(score_threshold):
                continue
            rects.append(
                (
                    int(round(float(x) * scale_x)),
                    int(round(float(y) * scale_y)),
                    int(round(float(bw) * scale_x)),
                    int(round(float(bh) * scale_y)),
                )
            )
    except Exception as exc:
        logger.exception("解析 YuNet 检测结果失败: {}", exc)
        return []

    return rects


def _ultralight_input_size_from_name(model_name: str) -> Tuple[int, int]:
    name = str(model_name or "").lower()
    if "640" in name:
        return (640, 480)
    if "320" in name:
        return (320, 240)
    return (320, 240)


def create_ultralight_net(*, model_path: Path):
    with _CV2_IMPORT_LOCK:
        import cv2

    if not hasattr(cv2, "dnn"):
        raise RuntimeError("OpenCV dnn is not available in this build")

    try:
        return cv2.dnn.readNetFromONNX(str(model_path))
    except Exception as exc:
        if not getattr(sys, "frozen", False):
            raise RuntimeError(f"加载 ONNX 模型失败: {model_path}") from exc

        import tempfile

        temp_dir = Path(tempfile.gettempdir()) / "SecRandom" / "cv_models"
        try:
            temp_dir.mkdir(parents=True, exist_ok=True)
        except Exception:
            temp_dir = Path(tempfile.gettempdir())

        temp_path = temp_dir / model_path.name
        try:
            needs_copy = True
            if temp_path.exists():
                try:
                    needs_copy = (
                        temp_path.stat().st_size != model_path.stat().st_size
                        or int(temp_path.stat().st_mtime)
                        != int(model_path.stat().st_mtime)
                    )
                except Exception:
                    needs_copy = True

            if needs_copy:
                shutil.copy2(model_path, temp_path)

            logger.warning(
                f"直接加载 ONNX 模型失败，尝试使用临时副本重新加载: {model_path}"
            )
            return cv2.dnn.readNetFromONNX(str(temp_path))
        except Exception as fallback_exc:
            raise RuntimeError(f"加载 ONNX 模型失败: {model_path}") from fallback_exc


def _generate_ultralight_priors(input_size: Tuple[int, int]):
    import numpy as np

    in_w, in_h = int(input_size[0]), int(input_size[1])
    min_boxes = [[10.0, 16.0, 24.0], [32.0, 48.0], [64.0, 96.0], [128.0, 192.0, 256.0]]
    strides = [8, 16, 32, 64]

    priors: list[list[float]] = []
    for boxes, stride in zip(min_boxes, strides, strict=True):
        fm_w = int(math.ceil(in_w / float(stride)))
        fm_h = int(math.ceil(in_h / float(stride)))
        for j in range(fm_h):
            for i in range(fm_w):
                cx = (i + 0.5) * float(stride) / float(in_w)
                cy = (j + 0.5) * float(stride) / float(in_h)
                for b in boxes:
                    w = float(b) / float(in_w)
                    h = float(b) / float(in_h)
                    priors.append([cx, cy, w, h])

    arr = np.asarray(priors, dtype=np.float32)
    arr = np.clip(arr, 0.0, 1.0)
    return arr


def detect_faces_ultralight(
    frame_bgr,
    *,
    net,
    input_size: Tuple[int, int],
    priors=None,
    score_threshold: float = 0.7,
    nms_threshold: float = 0.4,
) -> list[Rect]:
    import cv2
    import numpy as np

    h0, w0 = frame_bgr.shape[:2]
    if h0 <= 0 or w0 <= 0:
        return []

    in_w, in_h = int(input_size[0]), int(input_size[1])
    if in_w <= 0 or in_h <= 0:
        return []

    resized = cv2.resize(frame_bgr, (in_w, in_h), interpolation=cv2.INTER_LINEAR)
    blob = cv2.dnn.blobFromImage(
        resized,
        scalefactor=1.0 / 128.0,
        size=(in_w, in_h),
        mean=(127.0, 127.0, 127.0),
        swapRB=True,
        crop=False,
    )

    net.setInput(blob)
    out_names = []
    try:
        out_names = net.getUnconnectedOutLayersNames()
    except Exception:
        out_names = []
    outs = net.forward(out_names) if out_names else net.forward()
    if outs is None:
        raise RuntimeError("ONNX forward returned None")

    outputs = outs if isinstance(outs, (list, tuple)) else [outs]
    scores = None
    boxes = None
    for out in outputs:
        arr = np.asarray(out)
        if arr.ndim >= 2 and int(arr.shape[-1]) == 2:
            scores = arr
        elif arr.ndim >= 2 and int(arr.shape[-1]) == 4:
            boxes = arr

    if scores is None or boxes is None:
        raise RuntimeError(
            "Unsupported ONNX model outputs (need Nx2 scores and Nx4 boxes)"
        )

    s = np.asarray(scores).reshape(-1, 2).astype(np.float32)
    b = np.asarray(boxes).reshape(-1, 4).astype(np.float32)

    if priors is None:
        priors = _generate_ultralight_priors((in_w, in_h))
    p = np.asarray(priors, dtype=np.float32).reshape(-1, 4)

    n = min(int(b.shape[0]), int(p.shape[0]), int(s.shape[0]))
    if n <= 0:
        return []
    b = b[:n]
    p = p[:n]
    s = s[:n]

    prob = s[:, 1]
    keep = prob > float(score_threshold)
    if not np.any(keep):
        return []

    variances = (0.1, 0.2)
    cx = p[:, 0] + b[:, 0] * variances[0] * p[:, 2]
    cy = p[:, 1] + b[:, 1] * variances[0] * p[:, 3]
    ww = p[:, 2] * np.exp(b[:, 2] * variances[1])
    hh = p[:, 3] * np.exp(b[:, 3] * variances[1])

    x1 = (cx - ww / 2.0) * float(in_w)
    y1 = (cy - hh / 2.0) * float(in_h)
    x2 = (cx + ww / 2.0) * float(in_w)
    y2 = (cy + hh / 2.0) * float(in_h)

    x1 = x1[keep]
    y1 = y1[keep]
    x2 = x2[keep]
    y2 = y2[keep]
    prob = prob[keep]

    scale_x = float(w0) / float(in_w)
    scale_y = float(h0) / float(in_h)

    all_boxes: list[list[float]] = []
    all_scores: list[float] = []
    for xi1, yi1, xi2, yi2, si in zip(x1, y1, x2, y2, prob, strict=False):
        bx = float(xi1) * scale_x
        by = float(yi1) * scale_y
        bw = float(max(0.0, xi2 - xi1)) * scale_x
        bh = float(max(0.0, yi2 - yi1)) * scale_y
        if bw <= 0.0 or bh <= 0.0:
            continue
        all_boxes.append([bx, by, bw, bh])
        all_scores.append(float(si))

    if not all_boxes:
        return []

    idxs = cv2.dnn.NMSBoxes(
        bboxes=all_boxes,
        scores=all_scores,
        score_threshold=float(score_threshold),
        nms_threshold=float(nms_threshold),
    )
    if idxs is None or len(idxs) == 0:
        return []

    picked: list[int] = []
    try:
        for i in idxs:
            picked.append(int(i))
    except Exception:
        try:
            picked = [int(i[0]) for i in idxs]
        except Exception:
            picked = []

    rects: list[Rect] = []
    for i in picked:
        x, y, bw, bh = all_boxes[i]
        xi = max(0, min(int(round(x)), w0 - 1))
        yi = max(0, min(int(round(y)), h0 - 1))
        wi = max(0, min(int(round(bw)), w0 - xi))
        hi = max(0, min(int(round(bh)), h0 - yi))
        if wi > 0 and hi > 0:
            rects.append((xi, yi, wi, hi))
    return rects


def create_onnx_face_detector(
    *, model_path: Path, input_size: Optional[Tuple[int, int]] = None
):
    name = model_path.name.lower()

    override_w = 0
    override_h = 0
    try:
        if input_size is not None:
            override_w = int(input_size[0])
            override_h = int(input_size[1])
    except Exception:
        override_w = 0
        override_h = 0
    override_size = (
        (override_w, override_h) if override_w > 0 and override_h > 0 else None
    )

    if "yunet" in name:
        detector = create_yunet_face_detector(
            model_path=model_path,
            input_size=override_size if override_size is not None else (320, 320),
        )
        return {
            "kind": "yunet",
            "model_path": Path(model_path),
            "detector": detector,
            "input_size": override_size,
        }

    default_input_size = _ultralight_input_size_from_name(model_path.name)
    resolved_input_size = (
        override_size if override_size is not None else default_input_size
    )
    net = create_ultralight_net(model_path=model_path)
    priors = _generate_ultralight_priors(resolved_input_size)
    return {
        "kind": "ultralight",
        "model_path": Path(model_path),
        "net": net,
        "input_size": resolved_input_size,
        "priors": priors,
    }


def detect_faces_onnx(frame_bgr, *, detector_state) -> list[Rect]:
    kind = ""
    try:
        kind = str(detector_state.get("kind", "")).lower()
    except Exception:
        kind = ""

    frame_size = frame_bgr.shape[:2]
    if kind == "yunet":
        rects = detect_faces_yunet(
            frame_bgr,
            detector=detector_state["detector"],
            input_size=detector_state.get("input_size"),
        )
        return merge_face_rects(frame_size, rects)
    if kind == "ultralight":
        rects = detect_faces_ultralight(
            frame_bgr,
            net=detector_state["net"],
            input_size=detector_state["input_size"],
            priors=detector_state.get("priors"),
        )
        return merge_face_rects(frame_size, rects)
    raise RuntimeError("Invalid detector state")


def get_default_scrfd_model_filename() -> str:
    return "det_500m.onnx"


def ensure_scrfd_model_path(
    *,
    allow_download: bool = False,
    model_url: Optional[str] = None,
    filename: Optional[str] = None,
) -> Path:
    model_dir = ensure_dir(get_data_path("cv_models"))
    if filename:
        target = Path(model_dir) / filename
        if target.exists() and target.is_file() and target.stat().st_size > 0:
            return target
        raise FileNotFoundError(str(target))

    candidates = [
        get_default_scrfd_model_filename(),
        "det_2.5g.onnx",
        "det_10g.onnx",
    ]
    hit = _find_first_existing_model(Path(model_dir), candidates)
    if hit is not None:
        return hit

    glob_hit = _find_glob_model(Path(model_dir), "det_*.onnx")
    if glob_hit is not None:
        return glob_hit

    raise FileNotFoundError(str(Path(model_dir) / get_default_scrfd_model_filename()))


def create_scrfd_net(*, model_path: Path):
    with _CV2_IMPORT_LOCK:
        import cv2

    if not hasattr(cv2, "dnn"):
        raise RuntimeError("OpenCV dnn is not available in this build")
    return cv2.dnn.readNetFromONNX(str(model_path))


def detect_faces_scrfd(
    frame_bgr,
    *,
    net,
    input_size: Tuple[int, int] = (640, 640),
    score_threshold: float = 0.5,
    nms_threshold: float = 0.4,
) -> list[Rect]:
    import cv2
    import numpy as np

    h0, w0 = frame_bgr.shape[:2]
    if h0 <= 0 or w0 <= 0:
        return []

    in_w, in_h = int(input_size[0]), int(input_size[1])
    if in_w <= 0 or in_h <= 0:
        return []

    r = min(in_w / float(w0), in_h / float(h0))
    new_w = int(round(w0 * r))
    new_h = int(round(h0 * r))
    resized = cv2.resize(frame_bgr, (new_w, new_h), interpolation=cv2.INTER_LINEAR)
    pad_w = in_w - new_w
    pad_h = in_h - new_h
    left = int(pad_w // 2)
    top = int(pad_h // 2)
    right = int(pad_w - left)
    bottom = int(pad_h - top)
    if left or top or right or bottom:
        resized = cv2.copyMakeBorder(
            resized,
            top,
            bottom,
            left,
            right,
            borderType=cv2.BORDER_CONSTANT,
            value=(0, 0, 0),
        )

    blob = cv2.dnn.blobFromImage(
        resized,
        scalefactor=1.0 / 128.0,
        size=(in_w, in_h),
        mean=(127.5, 127.5, 127.5),
        swapRB=True,
        crop=False,
    )
    net.setInput(blob)

    out_names = []
    try:
        out_names = net.getUnconnectedOutLayersNames()
    except Exception:
        out_names = []
    outs = net.forward(out_names) if out_names else net.forward()
    if outs is None:
        return []

    def _to_2d(arr: np.ndarray) -> np.ndarray:
        x = np.asarray(arr)
        if x.ndim == 4:
            n, c, oh, ow = x.shape
            if n != 1:
                x = x.reshape(-1, c, oh, ow)
                x = x[0]
                c, oh, ow = x.shape
            x = x.transpose(1, 2, 0).reshape(-1, c)
            return x
        if x.ndim == 3:
            return x.reshape(-1, x.shape[-1])
        if x.ndim == 2:
            return x
        if x.ndim == 1:
            return x.reshape(-1, 1)
        return x.reshape(-1, 1)

    scores_list: list[np.ndarray] = []
    bboxes_list: list[np.ndarray] = []
    for out in outs:
        arr = _to_2d(out)
        if arr.size == 0:
            continue
        cols = int(arr.shape[1]) if arr.ndim == 2 else 1
        if cols == 4:
            bboxes_list.append(arr.astype(np.float32))
        elif cols in (1, 2):
            scores_list.append(arr.astype(np.float32))

    pairs: list[tuple[np.ndarray, np.ndarray]] = []
    for s in scores_list:
        for b in bboxes_list:
            if s.shape[0] == b.shape[0]:
                pairs.append((s, b))

    if not pairs:
        return []

    def _infer_stride(n: int) -> tuple[int, int] | None:
        for stride in (8, 16, 32, 64):
            gh = in_h // stride
            gw = in_w // stride
            if gh <= 0 or gw <= 0:
                continue
            for na in (1, 2):
                if n == gh * gw * na:
                    return stride, na
        return None

    all_boxes: list[list[float]] = []
    all_scores: list[float] = []

    for scores, bboxes in pairs:
        n = int(bboxes.shape[0])
        info = _infer_stride(n)
        if info is None:
            continue
        stride, na = info
        gh = in_h // stride
        gw = in_w // stride

        centers_x = (np.arange(gw, dtype=np.float32) + 0.5) * float(stride)
        centers_y = (np.arange(gh, dtype=np.float32) + 0.5) * float(stride)
        grid_x, grid_y = np.meshgrid(centers_x, centers_y)
        centers = np.stack([grid_x, grid_y], axis=-1).reshape(-1, 2)
        if na == 2:
            centers = np.repeat(centers, 2, axis=0)

        s = scores[:, 0] if scores.shape[1] == 1 else scores[:, 1]
        keep = s > float(score_threshold)
        if not np.any(keep):
            continue

        b = bboxes * float(stride)
        c = centers
        x1 = c[:, 0] - b[:, 0]
        y1 = c[:, 1] - b[:, 1]
        x2 = c[:, 0] + b[:, 2]
        y2 = c[:, 1] + b[:, 3]

        x1 = x1[keep]
        y1 = y1[keep]
        x2 = x2[keep]
        y2 = y2[keep]
        s = s[keep]

        for xi1, yi1, xi2, yi2, si in zip(x1, y1, x2, y2, s, strict=False):
            bw = float(max(0.0, xi2 - xi1))
            bh = float(max(0.0, yi2 - yi1))
            if bw <= 0.0 or bh <= 0.0:
                continue
            all_boxes.append([float(xi1), float(yi1), bw, bh])
            all_scores.append(float(si))

    if not all_boxes:
        return []

    idxs = cv2.dnn.NMSBoxes(
        bboxes=all_boxes,
        scores=all_scores,
        score_threshold=float(score_threshold),
        nms_threshold=float(nms_threshold),
    )
    if idxs is None or len(idxs) == 0:
        return []

    picked: list[int] = []
    try:
        for i in idxs:
            picked.append(int(i))
    except Exception:
        try:
            picked = [int(i[0]) for i in idxs]
        except Exception:
            picked = []

    rects: list[Rect] = []
    for i in picked:
        x, y, bw, bh = all_boxes[i]
        x = (x - float(left)) / float(r)
        y = (y - float(top)) / float(r)
        bw = bw / float(r)
        bh = bh / float(r)
        xi = max(0, min(int(round(x)), w0 - 1))
        yi = max(0, min(int(round(y)), h0 - 1))
        wi = max(0, min(int(round(bw)), w0 - xi))
        hi = max(0, min(int(round(bh)), h0 - yi))
        if wi > 0 and hi > 0:
            rects.append((xi, yi, wi, hi))

    return rects
