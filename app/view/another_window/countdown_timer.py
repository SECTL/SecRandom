from __future__ import annotations

from dataclasses import dataclass

from loguru import logger
from PySide6.QtCore import (
    QObject,
    QPoint,
    QRect,
    QRectF,
    QSize,
    QEvent,
    QEasingCurve,
    QParallelAnimationGroup,
    QPropertyAnimation,
    QSequentialAnimationGroup,
    Property,
    QTimer,
    Qt,
    Signal,
    QElapsedTimer,
    QDateTime,
)
from PySide6.QtGui import QColor, QFont, QFontMetrics, QPainter
from PySide6.QtWidgets import (
    QApplication,
    QAbstractItemView,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QHeaderView,
    QGraphicsOpacityEffect,
    QLabel,
    QSizePolicy,
    QSpacerItem,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    CaptionLabel,
    PrimaryPushButton,
    PushButton,
    qconfig,
    SegmentedWidget,
    Slider,
    TableWidget,
    ToolButton,
)

from app.Language.obtain_language import (
    get_any_position_value_async,
    get_content_description_async,
    get_content_name_async,
    get_content_pushbutton_name_async,
)
from app.tools.personalised import get_theme_icon, is_dark_theme, load_custom_font


@dataclass(frozen=True)
class _Preset:
    label: str
    seconds: int


class _ProgressRing(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self._progress = 0.0
        self._warning_level = 0.0
        self._opacity = 1.0
        self._ring_width = 10
        self._bg_color = QColor(230, 230, 230, 220)
        self._fg_color = QColor(0, 170, 255, 255)
        self._warn_color = QColor(255, 90, 90, 255)
        self._warning = False
        self.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        self.setFixedSize(400, 400)
        self._progress_anim = QPropertyAnimation(self, b"progress", self)
        self._progress_anim.setDuration(110)
        self._progress_anim.setEasingCurve(QEasingCurve.OutCubic)
        self._warning_anim = QPropertyAnimation(self, b"warningLevel", self)
        self._warning_anim.setDuration(220)
        self._warning_anim.setEasingCurve(QEasingCurve.OutCubic)

    def _get_progress(self) -> float:
        return float(self._progress)

    def _set_progress(self, value: float):
        value = 0.0 if value is None else float(value)
        value = max(0.0, min(1.0, value))
        if abs(self._progress - value) < 1e-6:
            return
        self._progress = value
        self.update()

    progress = Property(float, _get_progress, _set_progress)

    def _get_warning_level(self) -> float:
        return float(self._warning_level)

    def _set_warning_level(self, value: float):
        value = 0.0 if value is None else float(value)
        value = max(0.0, min(1.0, value))
        if abs(self._warning_level - value) < 1e-6:
            return
        self._warning_level = value
        self.update()

    warningLevel = Property(float, _get_warning_level, _set_warning_level)

    def _get_opacity(self) -> float:
        return float(self._opacity)

    def _set_opacity(self, value: float):
        value = 0.0 if value is None else float(value)
        value = max(0.0, min(1.0, value))
        if abs(self._opacity - value) < 1e-6:
            return
        self._opacity = value
        self.update()

    opacity = Property(float, _get_opacity, _set_opacity)

    def set_progress(self, value: float):
        value = 0.0 if value is None else float(value)
        value = max(0.0, min(1.0, value))
        current = float(self._progress)
        if abs(current - value) < 1e-6:
            return

        if value < current and (current - value) > 0.65:
            try:
                self._progress_anim.stop()
            except Exception:
                pass
            self.progress = value
            return

        try:
            self._progress_anim.stop()
        except Exception:
            pass
        self._progress_anim.setStartValue(current)
        self._progress_anim.setEndValue(value)
        self._progress_anim.start()

    def set_warning(self, warning: bool):
        warning = bool(warning)
        self._warning = warning
        try:
            self._warning_anim.stop()
        except Exception:
            pass
        self._warning_anim.setStartValue(float(self._warning_level))
        self._warning_anim.setEndValue(1.0 if warning else 0.0)
        self._warning_anim.start()

    @staticmethod
    def _lerp_color(a: QColor, b: QColor, t: float) -> QColor:
        t = max(0.0, min(1.0, float(t)))
        return QColor(
            int(a.red() + (b.red() - a.red()) * t),
            int(a.green() + (b.green() - a.green()) * t),
            int(a.blue() + (b.blue() - a.blue()) * t),
            int(a.alpha() + (b.alpha() - a.alpha()) * t),
        )

    def paintEvent(self, event):
        painter = QPainter(self)
        if not painter.isActive():
            return
        painter.setRenderHint(QPainter.Antialiasing, True)

        rect = QRectF(
            self._ring_width / 2,
            self._ring_width / 2,
            self.width() - self._ring_width,
            self.height() - self._ring_width,
        )

        opacity = max(0.0, min(1.0, float(self._opacity)))
        if opacity <= 0.001:
            return

        bg = QColor(self._bg_color)
        bg.setAlpha(int(bg.alpha() * opacity))
        fg = self._lerp_color(
            self._fg_color, self._warn_color, float(self._warning_level)
        )
        fg.setAlpha(int(fg.alpha() * opacity))

        pen_bg = painter.pen()
        pen_bg.setWidth(self._ring_width)
        pen_bg.setColor(bg)
        painter.setPen(pen_bg)
        painter.drawArc(rect, 0, 360 * 16)

        pen_fg = painter.pen()
        pen_fg.setWidth(self._ring_width)
        pen_fg.setCapStyle(Qt.RoundCap)
        pen_fg.setColor(fg)
        painter.setPen(pen_fg)

        start_angle = 90 * 16
        span_angle = -int(360 * 16 * self._progress)
        painter.drawArc(rect, start_angle, span_angle)


class _TimerEngine(QObject):
    updated = Signal()
    finished = Signal()
    stateChanged = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._clock = QElapsedTimer()
        self._clock.start()

        self._tick = QTimer(self)
        self._tick.timeout.connect(self._on_tick)
        self._tick.setTimerType(Qt.PreciseTimer)
        self._tick_interval_ms = 33

        self._mode = "countdown"
        self._running = False
        self._paused = False

        self._total_ms = 0
        self._remaining_ms = 0
        self._deadline_ms = 0

        self._elapsed_acc_ms = 0
        self._elapsed_base_ms = 0

        self._warning = False

    def set_mode(self, mode: str):
        mode = str(mode)
        if mode not in {"countdown", "stopwatch", "clock"}:
            return
        if self._mode == mode:
            return
        self.stop()
        self._mode = mode
        self.updated.emit()
        self.stateChanged.emit()

    def mode(self) -> str:
        return self._mode

    def is_running(self) -> bool:
        return self._running

    def is_paused(self) -> bool:
        return self._paused

    def total_ms(self) -> int:
        return int(self._total_ms)

    def current_ms(self) -> int:
        if self._mode == "countdown":
            return int(self._remaining_ms)
        if self._mode == "clock":
            return 0
        return int(self._elapsed_acc_ms)

    def warning(self) -> bool:
        return bool(self._warning)

    def set_total_seconds(self, seconds: int):
        seconds = int(seconds)
        seconds = max(0, seconds)
        self._total_ms = seconds * 1000
        if self._mode == "countdown":
            self._remaining_ms = self._total_ms
        self.updated.emit()

    def set_current_seconds(self, seconds: int):
        seconds = int(seconds)
        seconds = max(0, seconds)
        if self._mode == "countdown":
            self._remaining_ms = seconds * 1000
            self._total_ms = max(self._total_ms, self._remaining_ms)
        else:
            self._elapsed_acc_ms = seconds * 1000
        self.updated.emit()

    def adjust_seconds(self, delta_seconds: int):
        delta_seconds = int(delta_seconds)
        if delta_seconds == 0:
            return
        if self._mode == "countdown":
            new_ms = max(0, self._remaining_ms + delta_seconds * 1000)
            self._remaining_ms = new_ms
            self._total_ms = max(self._total_ms, self._remaining_ms)
            if self._running and not self._paused:
                self._deadline_ms = self._clock.elapsed() + self._remaining_ms
        else:
            self._elapsed_acc_ms = max(0, self._elapsed_acc_ms + delta_seconds * 1000)
            if self._running and not self._paused:
                self._elapsed_base_ms = self._clock.elapsed()
        self.updated.emit()

    def start(self):
        if self._running and not self._paused:
            return
        if self._mode == "clock":
            return
        if self._mode == "countdown":
            if self._remaining_ms <= 0:
                return
            self._deadline_ms = self._clock.elapsed() + self._remaining_ms
            self._warning = False
        else:
            self._elapsed_base_ms = self._clock.elapsed()
            self._warning = False

        self._running = True
        self._paused = False
        self._tick.start(self._tick_interval_ms)
        self.updated.emit()
        self.stateChanged.emit()

    def pause(self):
        if not self._running or self._paused:
            return
        if self._mode == "clock":
            return
        if self._mode == "countdown":
            self._remaining_ms = max(0, self._deadline_ms - self._clock.elapsed())
        else:
            self._elapsed_acc_ms = self._elapsed_acc_ms + max(
                0, self._clock.elapsed() - self._elapsed_base_ms
            )
        self._paused = True
        self._tick.stop()
        self.updated.emit()
        self.stateChanged.emit()

    def resume(self):
        if not self._running or not self._paused:
            return
        if self._mode == "clock":
            return
        if self._mode == "countdown":
            if self._remaining_ms <= 0:
                return
            self._deadline_ms = self._clock.elapsed() + self._remaining_ms
        else:
            self._elapsed_base_ms = self._clock.elapsed()
        self._paused = False
        self._tick.start(self._tick_interval_ms)
        self.updated.emit()
        self.stateChanged.emit()

    def stop(self):
        if not self._running and not self._paused:
            return
        self._tick.stop()
        self._running = False
        self._paused = False
        self._warning = False
        self.updated.emit()
        self.stateChanged.emit()

    def reset(self):
        self._tick.stop()
        self._running = False
        self._paused = False
        self._warning = False
        if self._mode == "countdown":
            self._remaining_ms = int(self._total_ms)
        else:
            self._elapsed_acc_ms = 0
            self._elapsed_base_ms = 0
        self.updated.emit()
        self.stateChanged.emit()

    def _on_tick(self):
        if not self._running or self._paused:
            return
        if self._mode == "countdown":
            self._remaining_ms = max(0, self._deadline_ms - self._clock.elapsed())
            self._warning = self._remaining_ms <= 10_000 and self._total_ms > 0
            self.updated.emit()
            if self._remaining_ms <= 0:
                self._tick.stop()
                self._running = False
                self._paused = False
                self._warning = False
                self.updated.emit()
                self.stateChanged.emit()
                self.finished.emit()
        else:
            self._elapsed_acc_ms = self._elapsed_acc_ms + max(
                0, self._clock.elapsed() - self._elapsed_base_ms
            )
            self._elapsed_base_ms = self._clock.elapsed()
            self.updated.emit()


class CountdownTimerPage(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)

        self._font_family = load_custom_font() or QFont().family()
        self._recent_seconds: list[int] = []
        self._preset_category = "common"
        self._compact_view = False
        self._controls_expanded_width = 600
        self._mode_segment_expanded_width = 0
        self._layout_anim: QParallelAnimationGroup | None = None
        self._ring_opacity_anim: QPropertyAnimation | None = None
        self._mode_switch_anim: QSequentialAnimationGroup | None = None
        self._center_left_spacer: QSpacerItem | None = None
        self._center_between_spacer: QSpacerItem | None = None
        self._center_right_spacer: QSpacerItem | None = None
        self._action_vis_anims: dict[int, QParallelAnimationGroup] = {}
        self._action_max_width: dict[int, int] = {}
        self._stopwatch_laps: list[tuple[int, int]] = []
        self._stopwatch_last_lap_total_ms = 0
        self._mini_floating = False
        self._mini_restore_geometry: bytes | None = None
        self._mini_restore_rect: QRect | None = None
        self._mini_geom_anim: QPropertyAnimation | None = None
        self._idle_timer = QTimer(self)
        self._idle_timer.setSingleShot(True)
        self._idle_timer.setInterval(5000)
        self._idle_timer.timeout.connect(self._on_idle_timeout)
        self._idle_filter_installed = False

        self._engine = _TimerEngine(self)
        self._engine.updated.connect(self._sync_ui_from_engine)
        self._engine.stateChanged.connect(self._sync_controls_state)
        self._engine.finished.connect(self._on_finished)

        self._clock_timer = QTimer(self)
        self._clock_timer.setTimerType(Qt.PreciseTimer)
        self._clock_timer.setInterval(33)
        self._clock_timer.timeout.connect(self._on_clock_tick)

        self._flash_timer = QTimer(self)
        self._flash_timer.timeout.connect(self._on_flash_tick)
        self._flash_steps = 0

        self._build_ui()
        self._sync_ui_from_engine()
        QTimer.singleShot(0, self._install_idle_filter_and_restart)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._sync_controls_state()

        lay = self.layout()
        if lay:
            lay.activate()

    def _build_ui(self):
        if hasattr(self, "_ui_built") and self._ui_built:
            return

        self.setUpdatesEnabled(False)

        try:
            if self.layout():
                old_lay = self.layout()
                dummy = QWidget()
                dummy.setLayout(old_lay)
                dummy.deleteLater()

            for child in self.findChildren(QWidget, options=Qt.FindDirectChildrenOnly):
                child.setParent(None)
                child.deleteLater()

            self._ui_built = True

            self.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)

            root = QVBoxLayout(self)
            root.setContentsMargins(16, 16, 16, 16)
            root.setSpacing(12)
            self._root_layout = root

            self._build_top_bar(root)

            center = QHBoxLayout()
            center.setSpacing(10)

            self._build_left_panel()
            self._build_right_panel()
            self._build_controls_panel()
            self._assemble_center_layout(center)

            root.addLayout(center, 1)

            self._build_bottom_actions(root)

            self._mode_segment_expanded_width = max(
                1, int(self.mode_segment_container.sizeHint().width())
            )
            self.mode_segment_container.setMaximumWidth(
                self._mode_segment_expanded_width
            )
            self._install_shortcuts()
            self._apply_time_text_style()
        finally:
            self.setUpdatesEnabled(True)

    def changeEvent(self, event):
        super().changeEvent(event)
        if event.type() in (
            QEvent.Type.PaletteChange,
            QEvent.Type.ApplicationPaletteChange,
            QEvent.Type.StyleChange,
        ):
            self._apply_time_text_style()

    def _build_top_bar(self, root: QVBoxLayout):
        self.top_bar_container = QWidget(self)
        top_bar = QHBoxLayout(self.top_bar_container)
        top_bar.setContentsMargins(0, 0, 0, 0)
        top_bar.setSpacing(8)

        self.mode_segment_container = QWidget(self.top_bar_container)
        mode_segment_layout = QHBoxLayout(self.mode_segment_container)
        mode_segment_layout.setContentsMargins(0, 0, 0, 0)
        mode_segment_layout.setSpacing(0)

        self.mode_segment = SegmentedWidget(self.mode_segment_container)
        self.mode_segment.setFixedHeight(30)
        self.mode_segment.addItem(
            routeKey="countdown",
            text=get_content_name_async("countdown_timer", "tab_countdown"),
            onClick=lambda: self._switch_mode("countdown"),
        )
        self.mode_segment.addItem(
            routeKey="stopwatch",
            text=get_content_name_async("countdown_timer", "tab_stopwatch"),
            onClick=lambda: self._switch_mode("stopwatch"),
        )
        self.mode_segment.addItem(
            routeKey="clock",
            text=get_content_name_async("countdown_timer", "tab_clock"),
            onClick=lambda: self._switch_mode("clock"),
        )
        self.mode_segment.setCurrentItem("countdown")
        mode_segment_layout.addWidget(self.mode_segment)

        self.persistent_controls = QWidget(self.top_bar_container)
        persistent_layout = QHBoxLayout(self.persistent_controls)
        persistent_layout.setContentsMargins(0, 0, 0, 0)
        persistent_layout.setSpacing(8)

        self.topmost_btn = ToolButton(
            get_theme_icon("ic_fluent_pin_off_20_filled"), self.persistent_controls
        )
        self.topmost_btn.setToolTip(
            get_content_description_async("countdown_timer", "always_on_top")
        )
        self.topmost_btn.setCheckable(True)
        self.topmost_btn.toggled.connect(self._toggle_topmost)

        self.opacity_slider = Slider(Qt.Horizontal, self.persistent_controls)
        self.opacity_slider.setFixedWidth(130)
        self.opacity_slider.setRange(30, 100)
        self.opacity_slider.setValue(100)
        self.opacity_slider.valueChanged.connect(self._apply_opacity)
        self.opacity_label = CaptionLabel(
            get_content_name_async("countdown_timer", "opacity"),
            self.persistent_controls,
        )

        persistent_layout.addWidget(self.opacity_label)
        persistent_layout.addWidget(self.opacity_slider)
        persistent_layout.addWidget(self.topmost_btn)

        top_bar.addWidget(self.mode_segment_container, 0, Qt.AlignLeft)
        top_bar.addStretch(1)
        top_bar.addWidget(self.persistent_controls, 0, Qt.AlignRight)
        root.addWidget(self.top_bar_container)

    def _build_left_panel(self):
        self.left_container = QWidget(self)
        self.left_container.setSizePolicy(QSizePolicy.Preferred, QSizePolicy.Expanding)

        outer_layout = QVBoxLayout(self.left_container)
        outer_layout.setContentsMargins(0, 0, 0, 0)
        outer_layout.setSpacing(0)
        self._left_outer_layout = outer_layout

        center_widget = QWidget(self.left_container)
        center_widget.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        self._left_center_widget = center_widget
        ring_layer = QGridLayout(center_widget)
        ring_layer.setContentsMargins(0, 0, 0, 0)
        ring_layer.setSpacing(0)

        self.ring = _ProgressRing(center_widget)
        ring_layer.addWidget(self.ring, 0, 0, 1, 1)

        self._build_digit_panel(center_widget)
        ring_layer.addWidget(self.digit_panel, 0, 0, 1, 1)

        outer_layout.addStretch(1)
        outer_layout.addWidget(center_widget, 0)
        outer_layout.setAlignment(center_widget, Qt.AlignHCenter | Qt.AlignVCenter)
        outer_layout.addStretch(1)

    def _build_digit_panel(self, parent_widget=None):
        if parent_widget is None:
            parent_widget = self.left_container
        self.digit_panel = QWidget(parent_widget)

        self.state_hint = CaptionLabel("", self.digit_panel)
        self.state_hint.setAlignment(Qt.AlignCenter)
        self._digit_v = QVBoxLayout(self.digit_panel)
        digit_v = self._digit_v
        digit_v.setContentsMargins(0, 0, 0, 0)
        digit_v.setSpacing(10)
        self._digit_top_spacer = QSpacerItem(
            0, 0, QSizePolicy.Minimum, QSizePolicy.Fixed
        )
        self._digit_bottom_spacer = QSpacerItem(
            0, 0, QSizePolicy.Minimum, QSizePolicy.Fixed
        )
        digit_v.addItem(self._digit_top_spacer)

        self._digit_plus_btns = []
        self._digit_minus_btns = []
        self._digit_labels = []
        self._separator_labels = []
        self._ms_label = None
        self._ms_plus_placeholder = None
        self._ms_minus_placeholder = None
        self.stopwatch_panel = None
        self.lap_btn = None
        self.clear_laps_btn = None
        self.lap_table = None
        self.clock_date_label = None
        self._right_v = None
        self._right_top_spacer = None
        self._right_bottom_spacer = None

        plus_row = QHBoxLayout()
        plus_row.setContentsMargins(0, 0, 0, 0)
        plus_row.setSpacing(6)

        digits_row = QHBoxLayout()
        digits_row.setContentsMargins(0, 0, 0, 0)
        digits_row.setSpacing(0)
        digits_row.setAlignment(Qt.AlignCenter)

        minus_row = QHBoxLayout()
        minus_row.setContentsMargins(0, 0, 0, 0)
        minus_row.setSpacing(6)

        for i in range(6):
            if i in (2, 4):
                self._add_digit_colon(digits_row, plus_row, minus_row)

            plus_btn = self._create_digit_button(
                get_theme_icon("ic_fluent_add_20_filled"),
                lambda _=False, idx=i: self._adjust_digit(idx, +1),
            )
            minus_btn = self._create_digit_button(
                get_theme_icon("ic_fluent_subtract_20_filled"),
                lambda _=False, idx=i: self._adjust_digit(idx, -1),
            )
            self._digit_plus_btns.append(plus_btn)
            self._digit_minus_btns.append(minus_btn)
            self._digit_labels.append(self._create_digit_label())

            plus_row.addWidget(plus_btn)
            digits_row.addWidget(self._digit_labels[-1], 0, Qt.AlignVCenter)
            minus_row.addWidget(minus_btn)

        self._ms_label = QLabel(".000", self.digit_panel)
        f = QFont(self._font_family, 34)
        f.setBold(True)
        self._ms_label.setFont(f)
        self._ms_label.setAlignment(Qt.AlignCenter)
        self._ms_label.setFixedWidth(84)
        self._ms_label.setVisible(False)
        digits_row.addWidget(self._ms_label, 0, Qt.AlignVCenter)

        self._ms_plus_placeholder = QWidget(self.digit_panel)
        self._ms_plus_placeholder.setFixedWidth(self._ms_label.width())
        self._ms_plus_placeholder.setVisible(False)
        plus_row.addWidget(self._ms_plus_placeholder)
        self._ms_minus_placeholder = QWidget(self.digit_panel)
        self._ms_minus_placeholder.setFixedWidth(self._ms_label.width())
        self._ms_minus_placeholder.setVisible(False)
        minus_row.addWidget(self._ms_minus_placeholder)

        for row in (plus_row, digits_row, minus_row):
            row.insertStretch(0, 1)
            row.addStretch(1)
        self._digit_plus_row_layout = plus_row
        self._digit_digits_row_layout = digits_row
        self._digit_minus_row_layout = minus_row

        digit_v.addLayout(plus_row)
        digit_v.addLayout(digits_row)
        digit_v.addLayout(minus_row)
        digit_v.addWidget(self.state_hint, 0, Qt.AlignCenter)
        self.clock_date_label = QLabel("", self.digit_panel)
        f = QFont(self._font_family, 18)
        self.clock_date_label.setFont(f)
        self.clock_date_label.setAlignment(Qt.AlignCenter)
        self.clock_date_label.setStyleSheet("color: rgba(120, 120, 120, 180);")
        self.clock_date_label.setVisible(False)
        digit_v.addWidget(self.clock_date_label, 0, Qt.AlignCenter)
        digit_v.addItem(self._digit_bottom_spacer)

    def _add_digit_colon(
        self, digits_row: QHBoxLayout, plus_row: QHBoxLayout, minus_row: QHBoxLayout
    ):
        w = QLabel(":", self.digit_panel)
        f = QFont(self._font_family, 44)
        f.setBold(True)
        w.setFont(f)
        w.setAlignment(Qt.AlignCenter)
        w.setFixedWidth(14)
        self._separator_labels.append(w)
        digits_row.addWidget(w, 0, Qt.AlignVCenter)

        p = QWidget(self.digit_panel)
        p.setFixedWidth(14)
        plus_row.addWidget(p)
        m = QWidget(self.digit_panel)
        m.setFixedWidth(14)
        minus_row.addWidget(m)

    def _create_digit_button(self, icon, callback):
        b = ToolButton(icon, self.digit_panel)
        b.setFixedSize(42, 42)
        b.setIconSize(QSize(18, 18))
        b.clicked.connect(callback)
        return b

    def _create_digit_label(self) -> QLabel:
        lab = QLabel("0", self.digit_panel)
        f = QFont(self._font_family, 52)
        f.setBold(True)
        lab.setFont(f)
        lab.setAlignment(Qt.AlignCenter)
        lab.setFixedWidth(42)
        return lab

    def _build_controls_panel(self):
        self.controls_panel = QFrame(self.right_container)
        self.controls_panel.setFrameShape(QFrame.NoFrame)
        self.controls_panel.setMaximumWidth(self._controls_expanded_width)
        self.controls_panel.setSizePolicy(QSizePolicy.Minimum, QSizePolicy.Expanding)

        self._right_v.insertWidget(1, self.controls_panel, 0, Qt.AlignLeft)

        controls_layout = QVBoxLayout(self.controls_panel)
        controls_layout.setContentsMargins(0, 0, 0, 0)
        controls_layout.setSpacing(10)

        self.preset_panel = QFrame(self.controls_panel)
        preset_panel_layout = QVBoxLayout(self.preset_panel)
        preset_panel_layout.setContentsMargins(0, 0, 0, 0)
        preset_panel_layout.setSpacing(6)

        self.preset_segment = SegmentedWidget(self.preset_panel)
        self.preset_segment.setFixedHeight(34)
        self.preset_segment.addItem(
            routeKey="common",
            text=get_content_name_async("countdown_timer", "preset_common"),
            onClick=lambda: self._set_preset_category("common"),
        )
        self.preset_segment.addItem(
            routeKey="recent",
            text=get_content_name_async("countdown_timer", "preset_recent"),
            onClick=lambda: self._set_preset_category("recent"),
        )
        self.preset_segment.setCurrentItem("common")
        preset_panel_layout.addWidget(self.preset_segment, 0, Qt.AlignLeft)

        self._common_presets = [
            _Preset("05:00", 5 * 60),
            _Preset("10:00", 10 * 60),
            _Preset("15:00", 15 * 60),
            _Preset("30:00", 30 * 60),
            _Preset("45:00", 45 * 60),
            _Preset("60:00", 60 * 60),
        ]

        self.preset_grid = QGridLayout()
        self.preset_grid.setContentsMargins(0, 0, 0, 0)
        self.preset_grid.setHorizontalSpacing(12)
        self.preset_grid.setVerticalSpacing(12)

        self._preset_buttons = []
        for i in range(6):
            btn = PushButton("", self.preset_panel)
            btn.setIcon(get_theme_icon("ic_fluent_timer_20_filled"))
            btn.setFixedSize(148, 48)
            btn.clicked.connect(lambda _=False, idx=i: self._handle_preset_button(idx))
            self._preset_buttons.append(btn)
            self.preset_grid.addWidget(btn, i // 3, i % 3)

        preset_panel_layout.addLayout(self.preset_grid)
        self._sync_preset_buttons()

        controls_layout.addWidget(self.preset_panel, 0, Qt.AlignTop | Qt.AlignLeft)
        self._build_stopwatch_panel(controls_layout)
        controls_layout.addStretch(1)

    def _build_stopwatch_panel(self, controls_layout: QVBoxLayout):
        self.stopwatch_panel = QFrame(self.controls_panel)
        stopwatch_layout = QVBoxLayout(self.stopwatch_panel)
        stopwatch_layout.setContentsMargins(0, 0, 0, 0)
        stopwatch_layout.setSpacing(8)

        self.lap_table = TableWidget(self.stopwatch_panel)
        self.lap_table.setRowCount(0)
        self.lap_table.setColumnCount(3)
        self.lap_table.setMinimumWidth(300)
        self.lap_table.setBorderVisible(True)
        self.lap_table.setBorderRadius(8)
        self.lap_table.setWordWrap(False)
        self.lap_table.setHorizontalHeaderLabels(
            [
                get_content_name_async("countdown_timer", "lap_index"),
                get_content_name_async("countdown_timer", "lap_delta"),
                get_content_name_async("countdown_timer", "lap_time"),
            ]
        )
        self.lap_table.verticalHeader().setVisible(False)
        self.lap_table.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.lap_table.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.lap_table.setSelectionMode(QAbstractItemView.SingleSelection)
        self.lap_table.setShowGrid(False)
        self.lap_table.setAlternatingRowColors(True)
        self.lap_table.setFocusPolicy(Qt.NoFocus)
        self.lap_table.horizontalHeader().setStretchLastSection(False)
        self.lap_table.horizontalHeader().setSectionResizeMode(
            0, QHeaderView.ResizeToContents
        )
        self.lap_table.horizontalHeader().setSectionResizeMode(
            1, QHeaderView.ResizeToContents
        )
        self.lap_table.horizontalHeader().setSectionResizeMode(
            2, QHeaderView.ResizeToContents
        )
        self.lap_table.setVisible(False)

        stopwatch_center_group = QWidget(self.stopwatch_panel)
        stopwatch_center_group_layout = QVBoxLayout(stopwatch_center_group)
        stopwatch_center_group_layout.setContentsMargins(0, 0, 0, 0)
        stopwatch_center_group_layout.setSpacing(8)
        stopwatch_center_group_layout.addWidget(self.lap_table, 0, Qt.AlignHCenter)

        stopwatch_layout.addStretch(1)
        stopwatch_layout.addWidget(stopwatch_center_group, 0, Qt.AlignHCenter)
        stopwatch_layout.addStretch(1)
        self.stopwatch_panel.setMinimumHeight(420)

        self.stopwatch_panel.setVisible(False)
        controls_layout.addWidget(self.stopwatch_panel, 1, Qt.AlignTop)

    def _build_right_panel(self):
        self.right_container = QWidget(self)
        self.right_container.setSizePolicy(QSizePolicy.Preferred, QSizePolicy.Preferred)
        self._right_v = QVBoxLayout(self.right_container)
        self._right_v.setContentsMargins(0, 0, 0, 0)
        self._right_v.setSpacing(0)
        self._right_top_spacer = QSpacerItem(
            0, 0, QSizePolicy.Minimum, QSizePolicy.Fixed
        )
        self._right_bottom_spacer = QSpacerItem(
            0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
        )
        self._right_v.addItem(self._right_top_spacer)
        self._right_v.addItem(self._right_bottom_spacer)

    def _assemble_center_layout(self, center: QHBoxLayout):
        self._center_left_spacer = QSpacerItem(
            0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum
        )
        self._center_between_spacer = QSpacerItem(
            10, 0, QSizePolicy.Fixed, QSizePolicy.Minimum
        )
        self._center_right_spacer = QSpacerItem(
            0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum
        )

        center.addItem(self._center_left_spacer)
        center.addWidget(self.left_container, 0)
        center.addItem(self._center_between_spacer)
        center.addWidget(self.right_container, 0)
        center.addItem(self._center_right_spacer)

    def _build_bottom_actions(self, root: QVBoxLayout):
        self.bottom_actions_container = QWidget(self)

        self.reset_btn = PushButton(
            get_content_pushbutton_name_async("countdown_timer", "reset"),
            self.bottom_actions_container,
        )
        self.reset_btn.setIcon(get_theme_icon("ic_fluent_arrow_sync_20_filled"))
        self.reset_btn.clicked.connect(self._handle_reset)

        self.start_btn = PrimaryPushButton(
            get_content_pushbutton_name_async("countdown_timer", "start"),
            self.bottom_actions_container,
        )
        self.start_btn.setIcon(get_theme_icon("ic_fluent_play_circle_20_filled"))
        self.start_btn.clicked.connect(self._toggle_start_pause)

        self.fullscreen_btn = PushButton(
            get_content_pushbutton_name_async("countdown_timer", "enter_fullscreen"),
            self.bottom_actions_container,
        )
        self.fullscreen_btn.setIcon(
            get_theme_icon("ic_fluent_full_screen_maximize_20_filled")
        )
        self.fullscreen_btn.setToolTip(
            get_content_description_async("countdown_timer", "toggle_fullscreen")
        )
        self.fullscreen_btn.clicked.connect(self._toggle_fullscreen)

        self.lap_btn = PushButton(
            get_content_pushbutton_name_async("countdown_timer", "lap"),
            self.bottom_actions_container,
        )
        self.lap_btn.clicked.connect(self._add_stopwatch_lap)

        self.clear_laps_btn = PushButton(
            get_content_pushbutton_name_async("countdown_timer", "clear_laps"),
            self.bottom_actions_container,
        )
        self.clear_laps_btn.clicked.connect(self._clear_stopwatch_laps)
        self.clear_laps_btn.setEnabled(False)
        self.lap_btn.setVisible(False)
        self.clear_laps_btn.setVisible(False)

        bottom_actions = QHBoxLayout(self.bottom_actions_container)
        bottom_actions.setContentsMargins(0, 0, 0, 0)
        bottom_actions.setSpacing(14)
        bottom_actions.addStretch(1)
        bottom_actions.addWidget(self.reset_btn)
        bottom_actions.addWidget(self.start_btn)
        bottom_actions.addWidget(self.fullscreen_btn)
        bottom_actions.addWidget(self.lap_btn)
        bottom_actions.addWidget(self.clear_laps_btn)
        bottom_actions.addStretch(1)
        root.addWidget(self.bottom_actions_container)

    def _install_shortcuts(self):
        from PySide6.QtGui import QShortcut, QKeySequence

        self._shortcuts = []

        def add_shortcut(seq: str, handler):
            sc = QShortcut(QKeySequence(seq), self, activated=handler)
            sc.setContext(Qt.ApplicationShortcut)
            self._shortcuts.append(sc)

        add_shortcut("Space", self._toggle_start_pause)
        add_shortcut("R", self._engine.reset)
        add_shortcut("F11", self._toggle_fullscreen)
        add_shortcut("Esc", self._exit_fullscreen_if_needed)
        add_shortcut("Up", lambda: self._adjust_seconds_shortcut(10))
        add_shortcut("Down", lambda: self._adjust_seconds_shortcut(-10))

    def _switch_mode(self, mode: str):
        old_mode = self._engine.mode()
        if str(mode) == str(old_mode):
            return
        if self._mini_floating:
            self._exit_mini_floating()
        if old_mode == "clock":
            self._clock_timer.stop()
        self.mode_segment.setCurrentItem(mode)
        self._animate_mode_switch(mode)
        self._restart_idle_timer()

    def _toggle_topmost(self, enabled: bool):
        w = self.window()
        try:
            flags = w.windowFlags()
            if enabled:
                flags |= Qt.WindowStaysOnTopHint
            else:
                flags &= ~Qt.WindowStaysOnTopHint
            w.setWindowFlags(flags)
            w.show()
            self.topmost_btn.setIcon(
                get_theme_icon(
                    "ic_fluent_pin_20_filled"
                    if enabled
                    else "ic_fluent_pin_off_20_filled"
                )
            )
        except Exception:
            logger.exception("toggle topmost failed")

    def _toggle_fullscreen(self):
        w = self.window()
        if self._mini_floating:
            self._exit_mini_floating()
        if w.isFullScreen():
            w.showNormal()
            self.fullscreen_btn.setIcon(
                get_theme_icon("ic_fluent_full_screen_maximize_20_filled")
            )
            self.fullscreen_btn.setText(
                get_content_pushbutton_name_async("countdown_timer", "enter_fullscreen")
            )
            QTimer.singleShot(0, self._apply_fullscreen_layout)
            return
        w.showFullScreen()
        self.fullscreen_btn.setIcon(
            get_theme_icon("ic_fluent_full_screen_minimize_20_filled")
        )
        self.fullscreen_btn.setText(
            get_content_pushbutton_name_async("countdown_timer", "exit_fullscreen")
        )
        QTimer.singleShot(0, self._apply_fullscreen_layout)

    def _exit_fullscreen_if_needed(self):
        w = self.window()
        if w.isFullScreen():
            w.showNormal()
            self.fullscreen_btn.setIcon(
                get_theme_icon("ic_fluent_full_screen_maximize_20_filled")
            )
            self.fullscreen_btn.setText(
                get_content_pushbutton_name_async("countdown_timer", "enter_fullscreen")
            )
            QTimer.singleShot(0, self._apply_fullscreen_layout)

    def _apply_fullscreen_layout(self):
        self.setUpdatesEnabled(False)
        try:
            if (
                self._center_left_spacer is not None
                and self._center_between_spacer is not None
                and self._center_right_spacer is not None
            ):
                self._center_left_spacer.changeSize(
                    0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum
                )
                self._center_right_spacer.changeSize(
                    0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum
                )
            self._sync_controls_state()

            lay = self.layout()
            if lay is not None:
                try:
                    lay.invalidate()
                    lay.activate()
                except Exception:
                    pass
        finally:
            self.setUpdatesEnabled(True)
            self.update()

    def _set_action_visible_animated(self, w: QWidget, visible: bool):
        visible = bool(visible)
        wid = int(w.winId())
        old = self._action_vis_anims.pop(wid, None)
        if old is not None:
            try:
                old.stop()
            except Exception:
                pass
        if visible:
            w.setVisible(True)
            if wid not in self._action_max_width:
                self._action_max_width[wid] = max(1, int(w.sizeHint().width()))
            max_w = int(
                self._action_max_width.get(wid, max(1, int(w.sizeHint().width())))
            )
            w.setMaximumWidth(max_w)
            effect = w.graphicsEffect()
            if isinstance(effect, QGraphicsOpacityEffect):
                effect.setOpacity(1.0)
            return
        else:
            self._action_max_width[wid] = max(
                1, int(w.width() if w.width() > 0 else w.sizeHint().width())
            )
        max_w = int(self._action_max_width.get(wid, max(1, int(w.sizeHint().width()))))
        w.setMaximumWidth(max_w)
        w.setVisible(False)
        effect = w.graphicsEffect()
        if isinstance(effect, QGraphicsOpacityEffect):
            effect.setOpacity(1.0)

    def _adjust_seconds_shortcut(self, delta_seconds: int) -> bool:
        if self._engine.mode() != "countdown":
            return False
        if self._engine.is_running() and not self._engine.is_paused():
            return False
        self._engine.adjust_seconds(int(delta_seconds))
        return True

    def _apply_opacity(self, value: int):
        try:
            self.window().setWindowOpacity(max(0.3, min(1.0, float(value) / 100.0)))
        except Exception:
            logger.exception("apply opacity failed")

    def _set_seconds(self, seconds: int):
        if self._engine.is_running() and not self._engine.is_paused():
            self._engine.pause()
        self._engine.set_mode("countdown")
        self.mode_segment.setCurrentItem("countdown")
        self._engine.set_total_seconds(int(seconds))
        self._engine.reset()

    def _set_preset_category(self, category: str):
        self._preset_category = category
        try:
            self.preset_segment.setCurrentItem(category)
        except Exception:
            pass
        self._sync_preset_buttons()

    def _sync_preset_buttons(self):
        if self._preset_category == "recent":
            presets = [
                _Preset(self._format_duration_label(s), s)
                for s in self._recent_seconds[:6]
            ]
        else:
            presets = self._common_presets

        for i, btn in enumerate(self._preset_buttons):
            if i < len(presets):
                btn.setEnabled(True)
                btn.setText(self._format_duration_label(int(presets[i].seconds)))
                btn.setProperty("_seconds", presets[i].seconds)
            else:
                btn.setEnabled(False)
                btn.setText("--:--")
                btn.setProperty("_seconds", None)

    @staticmethod
    def _format_duration_label(seconds: int) -> str:
        seconds = max(0, int(seconds))
        if seconds >= 3600:
            h = seconds // 3600
            m = (seconds % 3600) // 60
            s = seconds % 60
            return f"{h:02d}:{m:02d}:{s:02d}"
        m = seconds // 60
        s = seconds % 60
        return f"{m:02d}:{s:02d}"

    def _handle_preset_button(self, idx: int):
        if idx < 0 or idx >= len(self._preset_buttons):
            return
        seconds = self._preset_buttons[idx].property("_seconds")
        if seconds is None:
            return
        try:
            seconds = int(seconds)
        except Exception:
            return
        self._set_seconds(seconds)

    def _record_recent(self, seconds: int):
        seconds = int(seconds)
        self._recent_seconds = [s for s in self._recent_seconds if s != seconds]
        self._recent_seconds.insert(0, seconds)
        self._recent_seconds = self._recent_seconds[:12]
        if self._preset_category == "recent":
            self._sync_preset_buttons()

    def _adjust_digit(self, idx: int, delta: int):
        if self._engine.mode() != "countdown":
            self._engine.set_mode("countdown")
            self.mode_segment.setCurrentItem("countdown")
        if self._engine.is_running() and not self._engine.is_paused():
            return

        total_seconds = int(round(self._engine.current_ms() / 1000))
        h, m, s = self._split_hms(total_seconds)
        digits = [h // 10, h % 10, m // 10, m % 10, s // 10, s % 10]
        max_by_idx = [9, 9, 5, 9, 5, 9]
        d = digits[idx]
        d = (d + int(delta)) % (max_by_idx[idx] + 1)
        digits[idx] = d
        h2 = digits[0] * 10 + digits[1]
        m2 = digits[2] * 10 + digits[3]
        s2 = digits[4] * 10 + digits[5]
        seconds = h2 * 3600 + m2 * 60 + s2
        self._engine.set_total_seconds(seconds)
        self._engine.reset()

    def _toggle_start_pause(self):
        if self._engine.mode() == "clock":
            return
        if self._engine.is_running() and not self._engine.is_paused():
            self._engine.pause()
            return
        if self._engine.is_running() and self._engine.is_paused():
            self._engine.resume()
            return
        if self._engine.mode() == "countdown":
            seconds = int(round(self._engine.current_ms() / 1000))
            if seconds > 0:
                self._record_recent(seconds)
        self._engine.start()

    def _handle_reset(self):
        self._engine.reset()
        if self._engine.mode() == "stopwatch":
            self._clear_stopwatch_laps()

    def _apply_time_font(self, mode: str):
        mode = str(mode)
        if mode in {"stopwatch", "clock"}:
            digit_size = 60
            colon_size = 50
            ms_size = 36
            sample_digit_font = QFont(self._font_family, int(digit_size))
            sample_digit_font.setBold(True)
            digit_metrics = QFontMetrics(sample_digit_font)
            digit_width = (
                max(
                    int(digit_metrics.horizontalAdvance("0")),
                    int(digit_metrics.horizontalAdvance("8")),
                )
                + 4
            )

            sample_colon_font = QFont(self._font_family, int(colon_size))
            sample_colon_font.setBold(True)
            colon_metrics = QFontMetrics(sample_colon_font)
            colon_width = int(colon_metrics.horizontalAdvance(":")) + 2

            sample_ms_font = QFont(self._font_family, int(ms_size))
            sample_ms_font.setBold(True)
            ms_metrics = QFontMetrics(sample_ms_font)
            ms_width = int(ms_metrics.horizontalAdvance(".000")) + 2
        else:
            digit_size = 50
            colon_size = 42
            ms_size = 32
            sample_digit_font = QFont(self._font_family, int(digit_size))
            sample_digit_font.setBold(True)
            digit_metrics = QFontMetrics(sample_digit_font)
            digit_width = (
                max(
                    int(digit_metrics.horizontalAdvance("0")),
                    int(digit_metrics.horizontalAdvance("8")),
                )
                + 4
            )

            sample_colon_font = QFont(self._font_family, int(colon_size))
            sample_colon_font.setBold(True)
            colon_metrics = QFontMetrics(sample_colon_font)
            colon_width = int(colon_metrics.horizontalAdvance(":")) + 2

            sample_ms_font = QFont(self._font_family, int(ms_size))
            sample_ms_font.setBold(True)
            ms_metrics = QFontMetrics(sample_ms_font)
            ms_width = int(ms_metrics.horizontalAdvance(".000")) + 2
        date_size = 18 if mode == "clock" else 16

        for lab in self._digit_labels:
            f = lab.font()
            if int(f.pointSize()) != int(digit_size):
                f.setPointSize(int(digit_size))
                lab.setFont(f)
            if int(lab.width()) != int(digit_width):
                lab.setFixedWidth(int(digit_width))

        for lab in self._separator_labels:
            f = lab.font()
            if int(f.pointSize()) != int(colon_size):
                f.setPointSize(int(colon_size))
                lab.setFont(f)
            if int(lab.width()) != int(colon_width):
                lab.setFixedWidth(int(colon_width))

        if self._ms_label is not None:
            f = self._ms_label.font()
            if int(f.pointSize()) != int(ms_size):
                f.setPointSize(int(ms_size))
                self._ms_label.setFont(f)
            if int(self._ms_label.width()) != int(ms_width):
                self._ms_label.setFixedWidth(int(ms_width))
        if self._ms_plus_placeholder is not None:
            self._ms_plus_placeholder.setFixedWidth(int(ms_width))
        if self._ms_minus_placeholder is not None:
            self._ms_minus_placeholder.setFixedWidth(int(ms_width))

        if self.clock_date_label is not None:
            f = self.clock_date_label.font()
            if int(f.pointSize()) != int(date_size):
                f.setPointSize(int(date_size))
                self.clock_date_label.setFont(f)

        self._apply_time_text_style()

    def _time_text_color(self) -> str:
        if is_dark_theme(qconfig):
            return "rgba(255, 255, 255, 230)"
        return "rgba(0, 0, 0, 220)"

    def _apply_time_text_style(self):
        color = self._time_text_color()
        style = f"color: {color}; background: transparent;"
        for lab in [
            *getattr(self, "_digit_labels", []),
            *getattr(self, "_separator_labels", []),
            getattr(self, "_ms_label", None),
        ]:
            if lab is not None:
                lab.setStyleSheet(style)

    @staticmethod
    def _format_stopwatch_ms(total_ms: int) -> str:
        total_ms = max(0, int(total_ms))
        total_s = total_ms // 1000
        ms = total_ms % 1000
        h = total_s // 3600
        m = (total_s % 3600) // 60
        s = total_s % 60
        return f"{h:02d}:{m:02d}:{s:02d}.{ms:03d}"

    def _clear_stopwatch_laps(self):
        self._stopwatch_laps = []
        self._stopwatch_last_lap_total_ms = 0
        if self.lap_table is not None:
            self.lap_table.setRowCount(0)
            self.lap_table.setVisible(False)
        if self.clear_laps_btn is not None:
            self.clear_laps_btn.setEnabled(False)
            self.clear_laps_btn.setVisible(False)
        self._sync_controls_state()

    def _add_stopwatch_lap(self):
        if self._engine.mode() != "stopwatch":
            return
        if not (self._engine.is_running() or self._engine.is_paused()):
            return
        total_ms = int(self._engine.current_ms())
        delta_ms = max(0, total_ms - int(self._stopwatch_last_lap_total_ms))
        self._stopwatch_last_lap_total_ms = total_ms
        self._stopwatch_laps.append((total_ms, delta_ms))
        if self.lap_table is None:
            return

        row = self.lap_table.rowCount()
        self.lap_table.insertRow(row)

        idx_item = QTableWidgetItem(str(row + 1))
        delta_item = QTableWidgetItem(self._format_stopwatch_ms(delta_ms))
        time_item = QTableWidgetItem(self._format_stopwatch_ms(total_ms))
        idx_item.setTextAlignment(Qt.AlignCenter)
        delta_item.setTextAlignment(Qt.AlignCenter)
        time_item.setTextAlignment(Qt.AlignCenter)

        self.lap_table.setItem(row, 0, idx_item)
        self.lap_table.setItem(row, 1, delta_item)
        self.lap_table.setItem(row, 2, time_item)
        self.lap_table.scrollToBottom()
        self.lap_table.setVisible(True)
        if self.clear_laps_btn is not None:
            self.clear_laps_btn.setEnabled(True)
            self.clear_laps_btn.setVisible(True)
        self._sync_controls_state()

    def _on_finished(self):
        self._set_compact_view(False)
        self._set_ring_visible_animated(False)
        if self._mini_floating:
            self._exit_mini_floating()
        self.state_hint.setText(get_content_name_async("countdown_timer", "time_up"))
        self._flash_steps = 10
        self._flash_timer.start(120)
        for _ in range(3):
            QTimer.singleShot(_ * 180, QApplication.beep)

    def _on_flash_tick(self):
        if self._flash_steps <= 0:
            self._flash_timer.stop()
            self._apply_time_text_style()
            return
        self._flash_steps -= 1
        if self._flash_steps % 2 == 0:
            for lab in self._digit_labels:
                lab.setStyleSheet("color: rgb(255, 90, 90);")
        else:
            self._apply_time_text_style()

    def _apply_start_enabled(self):
        mode = self._engine.mode()
        running = self._engine.is_running()
        paused = self._engine.is_paused()
        ms = int(self._engine.current_ms())
        if mode == "clock":
            self.start_btn.setEnabled(False)
            self.reset_btn.setEnabled(False)
            return
        self.reset_btn.setEnabled(True)
        if mode == "countdown" and (not running or paused) and ms <= 0:
            self.start_btn.setEnabled(False)
        else:
            self.start_btn.setEnabled(True)

    def _sync_controls_state(self):
        if not hasattr(self, "_ui_built") or not self._ui_built:
            return

        running = self._engine.is_running()
        paused = self._engine.is_paused()
        mode = self._engine.mode()
        has_laps = len(self._stopwatch_laps) > 0

        w = self.window()
        if self._mini_floating and (
            mode != "countdown"
            or (w is not None and (w.isFullScreen() or w.isMaximized()))
        ):
            self._exit_mini_floating()
        is_mini = bool(self._mini_floating)
        window_width = self.width()
        force_compact_by_width = window_width < 820 and not is_mini
        desired_compact = (
            ((mode == "countdown") and running and not paused)
            or is_mini
            or force_compact_by_width
        )
        self._set_compact_view(desired_compact)

        if not is_mini:
            self.left_container.setSizePolicy(
                QSizePolicy.Minimum, QSizePolicy.Expanding
            )
            self.right_container.setSizePolicy(
                QSizePolicy.Minimum, QSizePolicy.Preferred
            )
        else:
            self.left_container.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Expanding)
            self.right_container.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Preferred)

        if mode == "clock":
            self._set_action_visible_animated(self.start_btn, False)
            self._set_action_visible_animated(self.reset_btn, False)
        else:
            self._set_action_visible_animated(self.start_btn, True)
            self._set_action_visible_animated(self.reset_btn, True)

        editing_enabled = (
            (mode == "countdown") and (not running or paused) and not is_mini
        )
        ring_visible = (
            (mode == "countdown") and running and (not paused) and not is_mini
        )
        self._set_ring_visible_animated(ring_visible)

        for b in self._digit_plus_btns:
            b.setVisible(editing_enabled)
            b.setEnabled(editing_enabled)
        for b in self._digit_minus_btns:
            b.setVisible(editing_enabled)
            b.setEnabled(editing_enabled)

        if self.top_bar_container is not None:
            self.top_bar_container.setVisible(not is_mini)
        if getattr(self, "bottom_actions_container", None) is not None:
            self.bottom_actions_container.setVisible(not is_mini)

        if getattr(self, "right_container", None) is not None:
            right_visible = (mode != "clock") and (not is_mini)
            if mode == "stopwatch":
                right_visible = right_visible and has_laps
            self.right_container.setVisible(right_visible)

        self.state_hint.setVisible(not is_mini)

        self.preset_panel.setVisible(
            editing_enabled and mode == "countdown" and not is_mini
        )
        controls_visible = (mode != "clock") and not is_mini
        if mode == "stopwatch":
            controls_visible = controls_visible and has_laps
        self.controls_panel.setVisible(controls_visible)
        if self.controls_panel is not None:
            if mode == "countdown" and editing_enabled and not desired_compact:
                self.controls_panel.setSizePolicy(
                    QSizePolicy.Minimum, QSizePolicy.Preferred
                )
                try:
                    self.controls_panel.setMaximumHeight(
                        max(1, int(self.controls_panel.sizeHint().height()))
                    )
                except Exception:
                    pass
            else:
                self.controls_panel.setSizePolicy(
                    QSizePolicy.Minimum, QSizePolicy.Expanding
                )
                self.controls_panel.setMaximumHeight(16777215)
        if self._right_top_spacer is not None and self._right_bottom_spacer is not None:
            if mode == "countdown" and editing_enabled and not desired_compact:
                self._right_top_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
                self._right_bottom_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
            elif mode == "stopwatch":
                self._right_top_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
                self._right_bottom_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
            else:
                self._right_top_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Fixed
                )
                self._right_bottom_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
            if self._right_v is not None:
                try:
                    self._right_v.invalidate()
                except Exception:
                    pass
        if (
            self._center_left_spacer is not None
            and self._center_between_spacer is not None
            and self._center_right_spacer is not None
        ):
            digit_rows = [
                getattr(self, "_digit_plus_row_layout", None),
                getattr(self, "_digit_digits_row_layout", None),
                getattr(self, "_digit_minus_row_layout", None),
            ]
            for row in digit_rows:
                if row is not None and row.count() >= 2:
                    row.setStretch(0, 1)
                    row.setStretch(row.count() - 1, 1)

            base_margin = 20 if w and (w.isFullScreen() or w.isMaximized()) else 0
            between_w = 0
            if not is_mini and not desired_compact:
                if mode == "clock":
                    between_w = 0
                elif window_width > 550:
                    between_w = 20
                else:
                    between_w = 0

            extra_left = 0
            if not is_mini:
                if (
                    mode == "countdown"
                    and getattr(self, "preset_panel", None) is not None
                    and self.preset_panel.isVisible()
                    and getattr(self, "right_container", None) is not None
                    and self.right_container.isVisible()
                ):
                    extra_left = between_w + max(
                        0, int(self.right_container.sizeHint().width())
                    )
                elif (
                    mode == "stopwatch"
                    and getattr(self, "right_container", None) is not None
                    and self.right_container.isVisible()
                ):
                    extra_left = between_w + max(
                        0, int(self.right_container.sizeHint().width())
                    )

            try:
                cap = max(
                    0,
                    int(window_width)
                    - max(0, int(self.left_container.sizeHint().width()))
                    - 10,
                )
                extra_left = min(int(extra_left), int(cap))
            except Exception:
                extra_left = int(extra_left)

            self._center_left_spacer.changeSize(
                int(base_margin) + int(extra_left),
                0,
                QSizePolicy.Expanding,
                QSizePolicy.Minimum,
            )
            self._center_right_spacer.changeSize(
                int(base_margin), 0, QSizePolicy.Expanding, QSizePolicy.Minimum
            )

            if not is_mini and not desired_compact:
                # For clock mode the right panel is hidden; keep the center
                # spacer fixed so the left clock container stays centered.
                if mode == "clock":
                    self._center_between_spacer.changeSize(
                        0, 0, QSizePolicy.Fixed, QSizePolicy.Minimum
                    )
                elif window_width > 550:
                    self._center_between_spacer.changeSize(
                        20, 0, QSizePolicy.Fixed, QSizePolicy.Minimum
                    )
                else:
                    self._center_between_spacer.changeSize(
                        0, 0, QSizePolicy.Fixed, QSizePolicy.Minimum
                    )
            else:
                self._center_between_spacer.changeSize(
                    0, 0, QSizePolicy.Fixed, QSizePolicy.Minimum
                )

            lay = self.layout()
            if lay is not None:
                try:
                    lay.invalidate()
                    lay.activate()
                except Exception:
                    pass
        if self.stopwatch_panel is not None:
            self.stopwatch_panel.setVisible(
                mode == "stopwatch" and has_laps and not is_mini
            )
        if self.lap_btn is not None:
            self.lap_btn.setVisible(mode == "stopwatch" and not is_mini)
            self.lap_btn.setEnabled(mode == "stopwatch" and (running or paused))
        if self.clear_laps_btn is not None:
            self.clear_laps_btn.setVisible(
                mode == "stopwatch" and has_laps and not is_mini
            )
            self.clear_laps_btn.setEnabled(mode == "stopwatch" and has_laps)

        if running and not paused:
            self.start_btn.setText(
                get_content_pushbutton_name_async("countdown_timer", "pause")
            )
            self.start_btn.setIcon(get_theme_icon("ic_fluent_pause_circle_20_filled"))
            self.state_hint.setText(
                get_content_name_async("countdown_timer", "running")
            )
        elif running and paused:
            self.start_btn.setText(
                get_content_pushbutton_name_async("countdown_timer", "resume")
            )
            self.start_btn.setIcon(get_theme_icon("ic_fluent_play_circle_20_filled"))
            self.state_hint.setText(get_content_name_async("countdown_timer", "paused"))
        else:
            self.start_btn.setText(
                get_content_pushbutton_name_async("countdown_timer", "start")
            )
            self.start_btn.setIcon(get_theme_icon("ic_fluent_play_circle_20_filled"))
            self.state_hint.setText("")
        self._apply_start_enabled()
        self._apply_time_font(mode)
        if (
            getattr(self, "_digit_top_spacer", None) is not None
            and getattr(self, "_digit_bottom_spacer", None) is not None
        ):
            v_center = (
                (mode in {"clock", "stopwatch"})
                or is_mini
                or (mode == "countdown" and running and not paused)
            )
            if v_center:
                self._digit_top_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
                self._digit_bottom_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Expanding
                )
            else:
                self._digit_top_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Fixed
                )
                self._digit_bottom_spacer.changeSize(
                    0, 0, QSizePolicy.Minimum, QSizePolicy.Fixed
                )
            if getattr(self, "_digit_v", None) is not None:
                try:
                    self._digit_v.invalidate()
                except Exception:
                    pass
        self._restart_idle_timer()

    def _set_compact_view(self, compact: bool):
        compact = bool(compact)
        if self._compact_view == compact:
            if hasattr(self, "controls_panel") and self.controls_panel:
                if compact:
                    self.controls_panel.setVisible(False)
                    self.mode_segment_container.setVisible(False)
                else:
                    self.controls_panel.setVisible(True)
                    self.mode_segment_container.setVisible(True)
            return
        self._compact_view = compact

        if self._layout_anim is not None:
            try:
                self._layout_anim.stop()
            except Exception:
                pass
            self._layout_anim = None

        mode_max = self._mode_segment_expanded_width or int(
            self.mode_segment_container.sizeHint().width()
        )
        controls_effect = self.controls_panel.graphicsEffect()
        if isinstance(controls_effect, QGraphicsOpacityEffect):
            controls_effect.setOpacity(1.0)
        mode_effect = self.mode_segment_container.graphicsEffect()
        if isinstance(mode_effect, QGraphicsOpacityEffect):
            mode_effect.setOpacity(1.0)

        if self._compact_view:
            self.controls_panel.setVisible(False)
            self.mode_segment_container.setVisible(False)
            self.controls_panel.setMaximumWidth(0)
            self.mode_segment_container.setMaximumWidth(0)
        else:
            self.controls_panel.setVisible(True)
            self.mode_segment_container.setVisible(True)
            self.controls_panel.setMaximumWidth(self._controls_expanded_width)
            self.mode_segment_container.setMaximumWidth(mode_max)

    def _set_ring_visible_animated(self, visible: bool):
        visible = bool(visible)
        if self._ring_opacity_anim is not None:
            try:
                self._ring_opacity_anim.stop()
            except Exception:
                pass
            self._ring_opacity_anim = None

        if visible:
            self.ring.setVisible(True)
            self.ring.opacity = 1.0
            return

        if not self.ring.isVisible():
            self.ring.opacity = 0.0
            return
        self.ring.opacity = 0.0
        self.ring.setVisible(False)

    def _animate_mode_switch(self, target_mode: str):
        if self._mode_switch_anim is not None:
            try:
                self._mode_switch_anim.stop()
            except Exception:
                pass
            self._mode_switch_anim = None

        widgets = [self.left_container]
        if getattr(self, "right_container", None) is not None:
            widgets.append(self.right_container)

        effects: list[QGraphicsOpacityEffect] = []
        for w in widgets:
            effect = w.graphicsEffect()
            if not isinstance(effect, QGraphicsOpacityEffect):
                effect = QGraphicsOpacityEffect(w)
                w.setGraphicsEffect(effect)
            effect.setOpacity(1.0)
            effects.append(effect)

        fade_out = QParallelAnimationGroup(self)
        for effect in effects:
            anim = QPropertyAnimation(effect, b"opacity", self)
            anim.setDuration(140)
            anim.setStartValue(effect.opacity())
            anim.setEndValue(0.0)
            anim.setEasingCurve(QEasingCurve.InOutQuad)
            fade_out.addAnimation(anim)

        fade_in = QParallelAnimationGroup(self)
        for effect in effects:
            anim = QPropertyAnimation(effect, b"opacity", self)
            anim.setDuration(180)
            anim.setStartValue(0.0)
            anim.setEndValue(1.0)
            anim.setEasingCurve(QEasingCurve.InOutQuad)
            fade_in.addAnimation(anim)

        def apply_mode():
            if self._engine.mode() == "stopwatch" and target_mode != "stopwatch":
                self._clear_stopwatch_laps()
            self._engine.set_mode(target_mode)
            if target_mode == "clock":
                self._sync_clock_ui()
                self._clock_timer.start()
            else:
                self._clock_timer.stop()

        fade_out.finished.connect(apply_mode)
        seq = QSequentialAnimationGroup(self)
        seq.addAnimation(fade_out)
        seq.addAnimation(fade_in)
        self._mode_switch_anim = seq
        seq.start()

    def _set_ms_visible(self, visible: bool):
        if self._ms_label is not None:
            self._ms_label.setVisible(visible)
        if self._ms_plus_placeholder is not None:
            self._ms_plus_placeholder.setVisible(visible)
        if self._ms_minus_placeholder is not None:
            self._ms_minus_placeholder.setVisible(visible)

    def _set_clock_date_visible(self, visible: bool):
        if self.clock_date_label is not None:
            self.clock_date_label.setVisible(visible)

    def _sync_ui_from_engine(self):
        mode = self._engine.mode()
        if mode == "clock":
            self._set_ms_visible(False)
            self._set_clock_date_visible(True)
            self._sync_clock_ui()
            self._apply_start_enabled()
            return

        ms = int(self._engine.current_ms())
        total_ms = int(self._engine.total_ms())

        if mode == "countdown":
            self._set_ms_visible(False)
            self._set_clock_date_visible(False)
            seconds = max(0, int((ms + 999) // 1000))
            prog = float(ms) / float(total_ms) if total_ms > 0 else 0.0
            self.ring.set_progress(prog)
            self.ring.set_warning(self._engine.warning())
            self._sync_digits_from_seconds(seconds)
        elif mode == "stopwatch":
            self._set_ms_visible(True)
            ms_part = max(0, int(ms % 1000))
            v = f".{ms_part:03d}"
            if self._ms_label is not None and self._ms_label.text() != v:
                self._ms_label.setText(v)
            self._set_clock_date_visible(False)
            seconds = max(0, int(ms // 1000))
            within_min = (ms % 60_000) / 60_000.0
            self.ring.set_progress(within_min)
            self.ring.set_warning(False)
            self._sync_digits_from_seconds(seconds)
        else:
            self._set_ms_visible(False)
            self._set_clock_date_visible(False)
            seconds = max(0, int(ms // 1000))
            within_min = (ms % 60_000) / 60_000.0
            self.ring.set_progress(within_min)
            self.ring.set_warning(False)
            self._sync_digits_from_seconds(seconds)
        self._apply_start_enabled()

    def _on_clock_tick(self):
        if self._engine.mode() != "clock":
            return
        self._sync_clock_ui()

    def _sync_clock_ui(self):
        dt = QDateTime.currentDateTime()
        now = dt.time()
        h = int(now.hour())
        m = int(now.minute())
        s = int(now.second())
        if self._ms_label is not None:
            self._ms_label.setVisible(False)
        if self.clock_date_label is not None:
            d = dt.date()
            wd = int(d.dayOfWeek())
            week_items = get_any_position_value_async(
                "countdown_timer", "clock_weekdays", "items"
            )
            if not isinstance(week_items, (list, tuple)):
                week_items = []
            week = ""
            if 1 <= wd <= len(week_items):
                week = str(week_items[wd - 1])
            date_format = get_any_position_value_async(
                "countdown_timer", "clock_date_format", "format"
            )
            if not isinstance(date_format, str) or not date_format:
                date_format = "yyyy-MM-dd"
            v = dt.toString(date_format)
            if week:
                v = f"{v} {week}"
            if self.clock_date_label.text() != v:
                self.clock_date_label.setText(v)

        digits = [h // 10, h % 10, m // 10, m % 10, s // 10, s % 10]
        for i, lab in enumerate(self._digit_labels):
            v = str(int(digits[i]))
            if lab.text() != v:
                lab.setText(v)
        self.state_hint.setText("")

    def _sync_digits_from_seconds(self, total_seconds: int):
        h, m, s = self._split_hms(int(total_seconds))
        digits = [h // 10, h % 10, m // 10, m % 10, s // 10, s % 10]
        for i, lab in enumerate(self._digit_labels):
            try:
                v = str(int(digits[i]))
                if lab.text() != v:
                    lab.setText(v)
            except Exception:
                if lab.text() != "0":
                    lab.setText("0")

    @staticmethod
    def _split_hms(total_seconds: int):
        total_seconds = max(0, int(total_seconds))
        h = total_seconds // 3600
        m = (total_seconds % 3600) // 60
        s = total_seconds % 60
        return h, m, s

    @staticmethod
    def _format_hms(total_seconds: int) -> str:
        h, m, s = CountdownTimerPage._split_hms(total_seconds)
        return f"{h:02d}:{m:02d}:{s:02d}"

    def _install_idle_filter_and_restart(self):
        if self._idle_filter_installed:
            self._restart_idle_timer()
            return
        self._idle_filter_installed = True

        try:
            self.installEventFilter(self)
        except Exception:
            pass

        try:
            w = self.window()
            if w is not None and w is not self:
                w.installEventFilter(self)
        except Exception:
            pass

        try:
            for child in self.findChildren(QWidget):
                try:
                    child.installEventFilter(self)
                except Exception:
                    pass
        except Exception:
            pass

        self._restart_idle_timer()

    def _restart_idle_timer(self):
        if self._mini_floating:
            try:
                self._idle_timer.stop()
            except Exception:
                pass
            return

        w = self.window()
        if self._engine.mode() != "countdown":
            try:
                self._idle_timer.stop()
            except Exception:
                pass
            return
        if not self._engine.is_running():
            try:
                self._idle_timer.stop()
            except Exception:
                pass
            return
        if self._engine.is_paused():
            try:
                self._idle_timer.stop()
            except Exception:
                pass
            return
        if w is not None and (w.isFullScreen() or w.isMaximized()):
            try:
                self._idle_timer.stop()
            except Exception:
                pass
            return

        try:
            self._idle_timer.start()
        except Exception:
            pass

    def _on_idle_timeout(self):
        w = self.window()
        if self._engine.mode() != "countdown":
            return
        if not self._engine.is_running():
            return
        if self._engine.is_paused():
            return
        if w is not None and (w.isFullScreen() or w.isMaximized()):
            return
        self._enter_mini_floating()

    def _animate_window_geometry(
        self, w: QWidget, end_rect: QRect, duration: int, on_finished=None
    ):
        if self._mini_geom_anim is not None:
            try:
                self._mini_geom_anim.stop()
            except Exception:
                pass
            self._mini_geom_anim = None

        anim = QPropertyAnimation(w, b"geometry", self)
        anim.setDuration(int(duration))
        anim.setStartValue(QRect(w.geometry()))
        anim.setEndValue(QRect(end_rect))
        anim.setEasingCurve(QEasingCurve.OutCubic)

        def _on_finished():
            if self._mini_geom_anim is anim:
                self._mini_geom_anim = None
            if on_finished is not None:
                try:
                    on_finished()
                except Exception:
                    pass

        anim.finished.connect(_on_finished)
        self._mini_geom_anim = anim
        anim.start()

    @staticmethod
    def _clamp_rect_to_available_geometry(rect: QRect, available: QRect) -> QRect:
        r = QRect(rect)
        if r.width() > available.width():
            r.setWidth(available.width())
        if r.height() > available.height():
            r.setHeight(available.height())

        if r.left() < available.left():
            r.moveLeft(available.left())
        if r.top() < available.top():
            r.moveTop(available.top())
        if r.right() > available.right():
            r.moveRight(available.right())
        if r.bottom() > available.bottom():
            r.moveBottom(available.bottom())
        return r

    def _enter_mini_floating(self):
        if self._mini_floating:
            return
        if self._engine.mode() != "countdown":
            return
        if self._engine.is_paused():
            return
        w = self.window()
        if w is not None and (w.isFullScreen() or w.isMaximized()):
            return

        try:
            self._mini_restore_geometry = w.saveGeometry() if w is not None else None
        except Exception:
            self._mini_restore_geometry = None
        try:
            self._mini_restore_rect = QRect(w.geometry()) if w is not None else None
        except Exception:
            self._mini_restore_rect = None

        self._mini_floating = True

        try:
            self._idle_timer.stop()
        except Exception:
            pass

        try:
            self._set_ring_visible_animated(False)
        except Exception:
            pass

        try:
            if getattr(self, "right_container", None) is not None:
                self.right_container.setVisible(False)
        except Exception:
            pass

        try:
            if getattr(self, "top_bar_container", None) is not None:
                self.top_bar_container.setVisible(False)
            if getattr(self, "bottom_actions_container", None) is not None:
                self.bottom_actions_container.setVisible(False)
        except Exception:
            pass

        try:
            self.state_hint.setVisible(False)
        except Exception:
            pass

        try:
            if getattr(self, "_root_layout", None) is not None:
                self._root_layout.setContentsMargins(12, 12, 12, 12)
        except Exception:
            pass

        try:
            if w is not None:
                w.setMinimumSize(0, 0)
                w.setMaximumSize(16777215, 16777215)
        except Exception:
            pass

        try:
            QTimer.singleShot(0, self._shrink_window_for_mini)
        except Exception:
            pass

        self._sync_controls_state()

    def _shrink_window_for_mini(self):
        if not self._mini_floating:
            return
        w = self.window()
        if w is None:
            return
        start_rect = QRect(w.geometry())
        target_w, target_h = 400, 220
        end_rect = QRect(0, 0, int(target_w), int(target_h))
        end_rect.moveCenter(QPoint(start_rect.center()))
        screen = w.screen()
        if screen is not None:
            end_rect = self._clamp_rect_to_available_geometry(
                end_rect, QRect(screen.availableGeometry())
            )

        def finish():
            try:
                w.setFixedSize(int(target_w), int(target_h))
            except Exception:
                pass

        self._animate_window_geometry(w, end_rect, 260, finish)

    def _exit_mini_floating(self):
        if not self._mini_floating:
            return
        w = self.window()
        if w is None:
            self._mini_floating = False
            self._mini_restore_geometry = None
            self._mini_restore_rect = None
            self._sync_controls_state()
            self._restart_idle_timer()
            return

        try:
            w.setMinimumSize(0, 0)
            w.setMaximumSize(16777215, 16777215)
        except Exception:
            pass

        start_rect = QRect(w.geometry())
        restore_rect = self._mini_restore_rect
        if restore_rect is None:
            end_rect = QRect(start_rect)
        else:
            end_rect = QRect(
                0, 0, int(restore_rect.width()), int(restore_rect.height())
            )
            end_rect.moveCenter(QPoint(start_rect.center()))
            screen = w.screen()
            if screen is not None:
                end_rect = self._clamp_rect_to_available_geometry(
                    end_rect, QRect(screen.availableGeometry())
                )

        def finish():
            self._mini_floating = False
            self._mini_restore_geometry = None
            self._mini_restore_rect = None

            try:
                if getattr(self, "_root_layout", None) is not None:
                    self._root_layout.setContentsMargins(16, 16, 16, 16)
            except Exception:
                pass

            try:
                if getattr(self, "top_bar_container", None) is not None:
                    self.top_bar_container.setVisible(True)
                if getattr(self, "bottom_actions_container", None) is not None:
                    self.bottom_actions_container.setVisible(True)
            except Exception:
                pass

            self._sync_controls_state()
            self._restart_idle_timer()

        self._animate_window_geometry(w, end_rect, 280, finish)

    def showEvent(self, event):
        super().showEvent(event)
        QTimer.singleShot(0, self._delayed_layout_sync)

    def _delayed_layout_sync(self):
        """延迟布局同步，确保 widget 的 geometry 已经正确"""
        w = self.window()
        if w:
            size = w.size()
            w.resize(size.width() + 1, size.height())
            w.resize(size)

        self._sync_ui_from_engine()
        self._sync_controls_state()

        lay = self.layout()
        if lay:
            lay.invalidate()
            lay.activate()
        self.updateGeometry()
        self.update()

    def keyPressEvent(self, event):
        try:
            key = event.key()
            modifiers = event.modifiers()
        except Exception:
            return super().keyPressEvent(event)
        if modifiers == Qt.NoModifier and key in {Qt.Key_Up, Qt.Key_Down}:
            handled = self._adjust_seconds_shortcut(10 if key == Qt.Key_Up else -10)
            if handled:
                event.accept()
                return
        return super().keyPressEvent(event)

    def eventFilter(self, watched: QObject, event: QEvent) -> bool:
        try:
            t = event.type()
        except Exception:
            t = None

        if t in {
            QEvent.Type.MouseButtonPress,
            QEvent.Type.MouseButtonDblClick,
            QEvent.Type.MouseMove,
            QEvent.Type.Wheel,
            QEvent.Type.KeyPress,
            QEvent.Type.KeyRelease,
            QEvent.Type.TouchBegin,
            QEvent.Type.TouchUpdate,
            QEvent.Type.Gesture,
        }:
            if self._mini_floating and t in {
                QEvent.Type.MouseButtonPress,
                QEvent.Type.MouseButtonDblClick,
                QEvent.Type.KeyPress,
            }:
                self._exit_mini_floating()
                return True
            self._restart_idle_timer()

        if t == QEvent.Type.WindowStateChange:
            if self._mini_floating:
                w = self.window()
                if w is not None and (w.isFullScreen() or w.isMaximized()):
                    self._exit_mini_floating()
            self._restart_idle_timer()

        return super().eventFilter(watched, event)
