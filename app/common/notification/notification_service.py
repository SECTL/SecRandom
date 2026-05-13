from loguru import logger
from random import SystemRandom

from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QApplication
from PySide6.QtCore import Qt, QPoint, QTimer, QPropertyAnimation, QEasingCurve, QRect
from PySide6.QtGui import QMouseEvent
from qfluentwidgets import CardWidget, BodyLabel

from app.page_building.page_template import PageTemplate
from app.tools.variable import WINDOW_BOTTOM_POSITION_FACTOR
from app.Language.obtain_language import get_any_position_value
from app.tools.settings_access import readme_settings_async
from app.common.IPC_URL.url_ipc_handler import URLIPCHandler
from app.common.IPC_URL.csharp_ipc_handler import CSharpIPCHandler

system_random = SystemRandom()


class NotificationContentWidget(QWidget):
    """通知内容控件，用于在浮窗中显示内容"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.layout = QVBoxLayout(self)
        self.layout.setContentsMargins(15, 15, 15, 15)
        self.layout.setSpacing(10)
        self.content_widgets = []
        self.cached_widgets = []

    def update_content(self, widgets):
        """更新内容控件（优化版，复用控件避免闪烁）"""
        if not widgets:
            return

        min_count = min(len(widgets), len(self.cached_widgets))

        for i in range(min_count):
            old_widget = self.cached_widgets[i]
            new_widget = widgets[i]

            if old_widget and new_widget:
                old_layout = old_widget.layout()
                new_layout = new_widget.layout()

                if old_layout and new_layout:
                    for j in range(min(old_layout.count(), new_layout.count())):
                        old_item = old_layout.itemAt(j)
                        new_item = new_layout.itemAt(j)

                        if old_item and new_item:
                            old_label = old_item.widget()
                            new_label = new_item.widget()

                            if old_label and new_label:
                                old_label.setText(new_label.text())
                                old_label.setStyleSheet(new_label.styleSheet())

        for i in range(min_count, len(widgets)):
            self.layout.addWidget(widgets[i])
            self.content_widgets.append(widgets[i])

        for i in range(min_count, len(self.cached_widgets)):
            widget = self.cached_widgets[i]
            self.layout.removeWidget(widget)
            widget.deleteLater()
            if widget in self.content_widgets:
                self.content_widgets.remove(widget)

        self.cached_widgets = widgets

        # 确保新添加的 BodyLabel 可见：根据主题强制设置前景色
        try:
            from app.tools.personalised import is_dark_theme
            from qfluentwidgets import qconfig
            from qfluentwidgets import BodyLabel as QFBodyLabel

            fg = "#ffffff" if is_dark_theme(qconfig) else "#000000"

            def apply_fg_to(w):
                if isinstance(w, QFBodyLabel):
                    existing = w.styleSheet() or ""
                    if "color:" not in existing:
                        w.setStyleSheet(existing + f" color: {fg};")
                else:
                    for child in w.findChildren(QFBodyLabel):
                        existing = child.styleSheet() or ""
                        if "color:" not in existing:
                            child.setStyleSheet(existing + f" color: {fg};")

            for w in self.content_widgets:
                try:
                    apply_fg_to(w)
                except Exception as e:
                    logger.exception("应用前景色到内容控件时出错: {}", e)
        except Exception:
            pass


class NotificationWindowTemplate(PageTemplate):
    """通知窗口页面模板"""

    def __init__(self, parent=None):
        super().__init__(content_widget_class=NotificationContentWidget, parent=parent)


class FloatingNotificationWindow(CardWidget):
    """用于显示抽取结果的浮动通知窗口"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setWindowFlags(
            Qt.FramelessWindowHint
            | Qt.WindowStaysOnTopHint
            | Qt.Tool
            | Qt.WindowDoesNotAcceptFocus
        )

        # 添加拖动支持
        self.drag_position = QPoint()

        # 添加三击关闭功能支持
        self.click_count = 0
        self.click_timer = QTimer()
        self.click_timer.setSingleShot(True)
        self.click_timer.timeout.connect(self.reset_click_count)

        # 设置UI
        self.setup_ui()

        # 订阅主题变化，确保切换主题时更新文字颜色
        try:
            from qfluentwidgets import qconfig

            qconfig.themeChanged.connect(self._on_theme_changed)
        except Exception as e:
            logger.exception("连接 themeChanged 信号时出错（已忽略）: {}", e)

        # 设置窗口圆角
        self.setBorderRadius(15)

        # 自动关闭定时器
        self.auto_close_timer = QTimer()
        self.auto_close_timer.setSingleShot(True)
        self.auto_close_timer.timeout.connect(self.start_hide_animation)

        # 倒计时更新定时器
        self.countdown_timer = QTimer()
        self.countdown_timer.timeout.connect(self.update_countdown_display)

        # 周期性置顶定时器
        self._periodic_topmost_timer = QTimer()
        self._periodic_topmost_timer.timeout.connect(self._periodic_topmost)
        self._periodic_topmost_interval = 100

        # 动画相关
        self.geometry_animation = None
        self.opacity_animation = None
        self.is_animation_enabled = True

        # 关闭动画
        self.hide_animation = None

        # 启动周期性置顶
        self._start_periodic_topmost()

    def mousePressEvent(self, event: QMouseEvent):
        """鼠标按下事件处理，用于窗口拖拽"""
        if event.button() == Qt.LeftButton:
            self.drag_position = (
                event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            )
            event.accept()

    def mouseMoveEvent(self, event: QMouseEvent):
        """鼠标移动事件处理，用于窗口拖拽"""
        if event.buttons() == Qt.LeftButton and not self.drag_position.isNull():
            self.move(event.globalPosition().toPoint() - self.drag_position)
            event.accept()

    def mouseReleaseEvent(self, event: QMouseEvent):
        """鼠标释放事件处理"""
        if event.button() == Qt.LeftButton:
            self.drag_position = QPoint()
            # 处理三击关闭功能
            self.handle_mouse_click()
        event.accept()

    def handle_mouse_click(self):
        """处理鼠标点击，实现三击关闭功能"""
        self.click_count += 1

        if self.click_count == 1:
            # 第一次点击启动计时器
            self.click_timer.start(500)  # 500毫秒内必须完成三次点击
        elif self.click_count == 3:
            # 第三次点击关闭窗口
            self.click_timer.stop()
            self.reset_click_count()
            self.start_hide_animation()  # 使用带动画的关闭方法

    def reset_click_count(self):
        """重置点击计数"""
        self.click_count = 0

    def setup_ui(self):
        """初始化UI组件"""
        # 创建主布局
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # 创建顶部拖动提示线容器（用于添加边距）
        self.drag_line_container = QWidget()
        self.update_drag_line_container_style()
        drag_line_layout = QHBoxLayout(self.drag_line_container)
        drag_line_layout.setContentsMargins(15, 15, 15, 0)

        # 创建顶部拖动提示线
        self.drag_line = QWidget()
        self.drag_line.setFixedHeight(5)
        self.drag_line.setFixedWidth(80)
        self.update_drag_line_style()
        drag_line_layout.addWidget(self.drag_line)

        layout.addWidget(self.drag_line_container)

        # 添加一个小的间隔元素，确保拖动条与主布局紧密连接
        spacer = QWidget()
        spacer.setFixedHeight(0)
        spacer.setStyleSheet("background-color: transparent;")
        layout.addWidget(spacer)

        # 创建背景widget，用于设置透明度
        self.background_widget = QWidget()
        self.update_background_style()
        layout.addWidget(self.background_widget)

        # 创建内容布局
        content_layout = QVBoxLayout(self.background_widget)
        content_layout.setContentsMargins(15, 15, 15, 15)
        content_layout.setSpacing(10)

        self.content_layout = QVBoxLayout()
        self.content_layout.setSpacing(10)
        content_layout.addLayout(self.content_layout)

        # 添加倒计时提示标签
        self.countdown_label = BodyLabel()
        self.countdown_label.setAlignment(Qt.AlignCenter)
        content_layout.addWidget(self.countdown_label)

    def update_background_style(self):
        """根据主题更新背景样式"""
        try:
            # 导入主题判断函数
            from app.tools.personalised import is_dark_theme
            from qfluentwidgets import qconfig

            # 判断是否为深色主题
            if is_dark_theme(qconfig):
                # 深色主题使用深色背景
                background_color = "#202020"
                # 拖动线使用浅色
                self.drag_line_color = "#a0a0a0"
            else:
                # 浅色主题使用浅色背景
                background_color = "#ffffff"
                # 拖动线使用深色
                self.drag_line_color = "#606060"

            self.background_widget.setStyleSheet(
                f"background-color: {background_color}; border-top-left-radius: 0px; border-top-right-radius: 0px; border-bottom-left-radius: 15px; border-bottom-right-radius: 15px;"
            )
            self.update_drag_line_style()
            self.update_drag_line_container_style()
        except Exception as e:
            logger.exception("更新背景样式时出错（使用备用方案）: {}", e)
            # 如果无法获取主题信息，默认使用白色背景和深色拖动线
            background_color = "#ffffff"
            self.background_widget.setStyleSheet(
                "background-color: #ffffff; border-top-left-radius: 0px; border-top-right-radius: 0px; border-bottom-left-radius: 15px; border-bottom-right-radius: 15px;"
            )
            self.drag_line_color = "#606060"
            self.update_drag_line_style()
            self.update_drag_line_container_style()

    def update_drag_line_container_style(self):
        """更新拖动线容器样式"""
        if hasattr(self, "drag_line_container"):
            try:
                # 导入主题判断函数
                from app.tools.personalised import is_dark_theme
                from qfluentwidgets import qconfig

                # 判断是否为深色主题
                if is_dark_theme(qconfig):
                    # 深色主题使用深色背景
                    background_color = "#202020"
                else:
                    # 浅色主题使用浅色背景
                    background_color = "#ffffff"

                self.drag_line_container.setStyleSheet(
                    f"background-color: {background_color}; border-top-left-radius: 15px; border-top-right-radius: 15px; border-bottom-left-radius: 0px; border-bottom-right-radius: 0px;"
                )
            except Exception as e:
                logger.exception("更新拖拽线容器样式时出错（使用备用方案）: {}", e)
                # 如果无法获取主题信息，默认使用白色背景
                self.drag_line_container.setStyleSheet(
                    "background-color: #ffffff; border-top-left-radius: 15px; border-top-right-radius: 15px; border-bottom-left-radius: 0px; border-bottom-right-radius: 0px;"
                )

    def update_drag_line_style(self):
        """更新拖动线样式"""
        if hasattr(self, "drag_line") and hasattr(self, "drag_line_color"):
            self.drag_line.setStyleSheet(f"""
                QWidget {{
                    background-color: {self.drag_line_color};
                    border-radius: 2px;
                }}
            """)

    def apply_settings(self, settings=None, settings_group=None, is_animating=False):
        """应用设置到通知窗口

        Args:
            settings: 设置字典
            settings_group: 设置组名称
            is_animating: 是否在动画过程中，如果是则不启动自动关闭定时器
        """
        if settings:
            # 使用传递的设置
            transparency = settings.get("transparency", 0.6)
            auto_close_time = settings.get("auto_close_time", 5)
            self.is_animation_enabled = settings.get("animation", True)
        else:
            transparency = 0.6
            auto_close_time = 5
            self.is_animation_enabled = True

        # 保存设置组，用于后续判断是否抢占焦点
        self.settings_group = settings_group

        # 设置透明度（背景和字体透明度统一）
        self.setWindowOpacity(transparency)

        # 设置倒计时标签颜色，确保与背景对比
        try:
            from app.tools.personalised import is_dark_theme
            from qfluentwidgets import qconfig

            fg = "#ffffff" if is_dark_theme(qconfig) else "#000000"
            existing = self.countdown_label.styleSheet() or ""
            if "color:" not in existing:
                self.countdown_label.setStyleSheet(existing + f" color: {fg};")
        except Exception as e:
            logger.exception("设置倒计时标签颜色时出错: {}", e)

        # 根据设置定位窗口
        self.position_window(settings)

        # 设置自动关闭时间（仅在非动画状态下启动）
        if auto_close_time > 0 and not is_animating:
            self.auto_close_timer.stop()
            self.auto_close_timer.setInterval(auto_close_time * 1000)  # 转换为毫秒
            # 初始化倒计时并启动更新定时器
            self.countdown_timer.stop()
            self.remaining_time = auto_close_time  # 初始化为设定的自动关闭时间
            # 先显示初始倒计时
            self.countdown_label.setText(
                get_any_position_value("notification_common", "auto_close_hint").format(
                    self.remaining_time  # 显示当前设定的值
                )
            )
            self.countdown_timer.start(1000)  # 每秒更新一次
            self.auto_close_timer.start()  # 启动自动关闭定时器作为备用
        else:
            # 停止倒计时更新定时器并显示手动关闭提示
            self.countdown_timer.stop()
            self.countdown_label.setText(
                get_any_position_value("notification_common", "manual_close_hint")
            )

    def update_countdown_display(self):
        """更新倒计时显示"""
        self.remaining_time -= 1
        # 动画完成时显示倒计时
        if self.remaining_time > 0:
            self.countdown_label.setText(
                get_any_position_value("notification_common", "auto_close_hint").format(
                    self.remaining_time
                )
            )
        else:
            # 当倒计时减到0或以下时立即关闭窗口
            self.countdown_timer.stop()
            self.auto_close_timer.stop()  # 停止自动关闭定时器，防止重复关闭
            self.start_hide_animation()

    def _on_theme_changed(self):
        """主题切换时更新浮窗内文字和背景颜色"""
        try:
            from app.tools.personalised import is_dark_theme
            from qfluentwidgets import qconfig

            fg = "#ffffff" if is_dark_theme(qconfig) else "#000000"

            # 更新倒计时标签颜色
            try:
                existing = self.countdown_label.styleSheet() or ""
                parts = [
                    p.strip()
                    for p in existing.split(";")
                    if p.strip() and not p.strip().startswith("color:")
                ]
                parts.append(f"color: {fg} !important")
                self.countdown_label.setStyleSheet("; ".join(parts) + ";")
            except Exception as e:
                logger.exception("应用倒计时标签颜色时出错（已忽略）：{}", e)
                pass

            # 更新背景与拖动线样式
            try:
                self.update_background_style()
                self.update_drag_line_style()
                self.update_drag_line_container_style()
            except Exception as e:
                logger.exception("更新背景/拖拽线样式时出错（已忽略）: {}", e)
                pass
        except Exception as e:
            logger.exception("处理 _on_theme_changed 时出错（已忽略）: {}", e)
            pass

    def _get_screen_from_settings(self, settings):
        """根据设置获取屏幕"""
        screen = QApplication.primaryScreen()

        if settings:
            enabled_monitor_name = settings.get("enabled_monitor", "OFF")
            # 尝试获取指定的显示器
            if enabled_monitor_name != "OFF":
                for s in QApplication.screens():
                    if s.name() == enabled_monitor_name:
                        screen = s
                        logger.debug("使用显示器：{}", enabled_monitor_name)
                        break

        if screen is None:
            screen = QApplication.primaryScreen()

        return screen

    def _get_position_coordinates(
        self, screen_geometry, window_width, window_height, position_index
    ):
        """获取位置坐标 (x, y)"""
        center_x = screen_geometry.center().x() - window_width // 2
        center_y = screen_geometry.center().y() - window_height // 2
        left_x = screen_geometry.left()
        right_x = screen_geometry.right() - window_width
        top_y = screen_geometry.top()
        bottom_y = (
            screen_geometry.bottom() - window_height * WINDOW_BOTTOM_POSITION_FACTOR
        )

        # 定义位置映射 (x, y)
        # 0: 中心, 1: 顶部, 2: 底部, 3: 左侧, 4: 右侧,
        # 5: 顶部左侧, 6: 顶部右侧, 7: 底部左侧, 8: 底部右侧
        positions = {
            0: (center_x, center_y),
            1: (center_x, top_y),
            2: (center_x, bottom_y),
            3: (left_x, center_y),
            4: (right_x, center_y),
            5: (left_x, top_y),
            6: (right_x, top_y),
            7: (left_x, bottom_y),
            8: (right_x, bottom_y),
        }

        return positions.get(position_index, (center_x, center_y))

    def _calculate_window_position(
        self,
        screen_geometry,
        position_index,
        horizontal_offset,
        vertical_offset,
        window_width=None,
        window_height=None,
    ):
        """计算窗口位置"""
        if window_width is None:
            window_width = self.width() if self.width() > 0 else 300
        if window_height is None:
            window_height = self.height() if self.height() > 0 else 200

        base_x, base_y = self._get_position_coordinates(
            screen_geometry, window_width, window_height, position_index
        )

        return QRect(
            int(base_x + horizontal_offset),
            int(base_y + vertical_offset),
            window_width,
            window_height,
        )

    def position_window(self, settings=None):
        """根据设置定位浮窗"""
        # 获取屏幕
        screen = self._get_screen_from_settings(settings)
        screen_geometry = screen.geometry()

        # 获取设置
        if settings:
            position_index = settings.get("window_position", 0)
            horizontal_offset = settings.get("horizontal_offset", 0)
            vertical_offset = settings.get("vertical_offset", 0)
        else:
            position_index = 0
            horizontal_offset = 0
            vertical_offset = 0

        window_rect = self._calculate_window_position(
            screen_geometry,
            position_index,
            horizontal_offset,
            vertical_offset,
            self.width(),
            self.height(),
        )
        self.move(window_rect.x(), window_rect.y())

    def _calculate_animation_start_geometry(
        self, final_geometry, screen_geometry, position_index
    ):
        """根据位置确定动画起始位置，确保从屏幕外进入"""
        center_x = final_geometry.center().x()
        center_y = final_geometry.center().y()
        w = final_geometry.width()
        h = final_geometry.height()

        # 定义起始位置映射 (x, y, w, h)
        # 0: 中心 (从中心缩放)
        # 1: 顶部 (从屏幕顶部外进入)
        # 2: 底部 (高度从0开始)
        # 3: 左侧 (宽度从0开始)
        # 4: 右侧 (宽度从0开始)
        # 5-8: 角落 (宽高都从0开始)
        start_rects = {
            0: (center_x - w // 2, center_y - h // 2, 0, 0),
            1: (final_geometry.x(), screen_geometry.top() - h, w, h),
            2: (final_geometry.x(), screen_geometry.bottom(), w, 0),
            3: (screen_geometry.left() - w, final_geometry.y(), 0, h),
            4: (screen_geometry.right(), final_geometry.y(), 0, h),
            5: (screen_geometry.left() - w, screen_geometry.top() - h, 0, 0),
            6: (screen_geometry.right(), screen_geometry.top() - h, 0, 0),
            7: (screen_geometry.left() - w, screen_geometry.bottom(), 0, 0),
            8: (screen_geometry.right(), screen_geometry.bottom(), 0, 0),
        }

        # 获取参数，默认为中心缩放
        rect_params = start_rects.get(position_index, start_rects[0])
        return QRect(*rect_params)

    def start_show_animation(self, settings=None):
        """开始显示动画"""
        # 重置动画完成标志
        if not self.is_animation_enabled:
            # 即使没有动画也要确保在正确的屏幕显示
            screen = self._get_screen_from_settings(settings)
            screen_geometry = screen.geometry()

            if settings:
                position_index = settings.get("window_position", 0)
                horizontal_offset = settings.get("horizontal_offset", 0)
                vertical_offset = settings.get("vertical_offset", 0)
            else:
                position_index = 0
                horizontal_offset = 0
                vertical_offset = 0

            # 计算最终位置
            window_rect = self._calculate_window_position(
                screen_geometry,
                position_index,
                horizontal_offset,
                vertical_offset,
                self.width(),
                self.height(),
            )
            self.move(window_rect.x(), window_rect.y())

            self.show()
            self.update_countdown_display()
            return

        # 确保窗口大小已调整
        self.adjustSize()

        # 获取设置以确定动画类型
        position_index = 0
        if settings:
            position_index = settings.get("window_position", 0)

        # 根据设置计算最终位置和屏幕信息，但不移动窗口
        screen = self._get_screen_from_settings(settings)
        screen_geometry = screen.geometry()

        if settings:
            horizontal_offset = settings.get("horizontal_offset", 0)
            vertical_offset = settings.get("vertical_offset", 0)
        else:
            horizontal_offset = 0
            vertical_offset = 0

        # 计算最终位置
        window_rect = self._calculate_window_position(
            screen_geometry,
            position_index,
            horizontal_offset,
            vertical_offset,
            self.width(),
            self.height(),
        )
        # 使用当前窗口的实际尺寸而不是固定尺寸
        final_geometry = QRect(
            window_rect.x(), window_rect.y(), self.width(), self.height()
        )

        # 计算动画起始位置
        start_geometry = self._calculate_animation_start_geometry(
            final_geometry, screen_geometry, position_index
        )

        # 透明度
        transparency = settings.get("transparency", 0.6)

        # 创建几何动画
        self.geometry_animation = QPropertyAnimation(self, b"geometry")
        self.geometry_animation.setDuration(750)  # 增加动画时长以获得更平滑的效果
        self.geometry_animation.setStartValue(start_geometry)
        self.geometry_animation.setEndValue(final_geometry)
        self.geometry_animation.setEasingCurve(
            QEasingCurve.OutCubic
        )  # 使用更自然的缓动曲线

        # 创建透明度动画
        self.opacity_animation = QPropertyAnimation(self, b"windowOpacity")
        self.opacity_animation.setDuration(450)
        self.opacity_animation.setStartValue(0.0)
        self.opacity_animation.setEndValue(transparency)
        self.opacity_animation.setEasingCurve(
            QEasingCurve.OutCubic
        )  # 使用更自然的缓动曲线

        # 连接动画完成信号
        self.geometry_animation.finished.connect(self.on_animation_finished)

        # 设置窗口初始位置并开始动画
        self.setGeometry(start_geometry)
        self.setWindowOpacity(0.0)  # 初始透明度为0
        self.show()

        # 确保窗口管理器处理显示事件
        QApplication.processEvents()

        # 并行启动所有动画
        self.geometry_animation.start()
        self.opacity_animation.start()

        # 立即更新倒计时显示（显示"正在抽取中"）
        self.update_countdown_display()
        # 确保颜色与当前主题同步
        try:
            self._on_theme_changed()
        except Exception as e:
            logger.exception("显示时同步主题时出错（已忽略）: {}", e)

    def on_animation_finished(self):
        """动画完成后的处理"""
        # 清理动画对象
        if hasattr(self, "geometry_animation"):
            self.geometry_animation.deleteLater()
            del self.geometry_animation
        if hasattr(self, "opacity_animation"):
            self.opacity_animation.deleteLater()
            del self.opacity_animation

        # 如果设置了自动关闭时间，则启动定时器
        if hasattr(self, "auto_close_timer") and self.auto_close_timer.interval() > 0:
            self.auto_close_timer.start()

        # 检查是否设置了"无焦点模式"
        do_not_steal_focus = False
        try:
            do_not_steal_focus = readme_settings_async(
                "floating_window_management", "do_not_steal_focus"
            )
        except Exception:
            pass

        # 如果没有设置"无焦点模式"，则确保窗口保持在最前面并激活
        if not do_not_steal_focus:
            self.raise_()
            # 仅在窗口允许接受焦点时调用激活，避免 QWindow::requestActivate 警告
            try:
                if not (self.windowFlags() & Qt.WindowDoesNotAcceptFocus):
                    self.activateWindow()
            except Exception as e:
                logger.exception("激活窗口时出错（已忽略）: {}", e)

        # 更新倒计时显示
        self.update_countdown_display()

    def _start_periodic_topmost(self):
        """启动周期性置顶定时器"""
        self._periodic_topmost_timer.start(self._periodic_topmost_interval)

    def _periodic_topmost(self):
        """周期性将窗口置顶"""
        if self.isVisible():
            self.raise_()

    def start_hide_animation(self):
        """开始隐藏动画"""
        if not self.is_animation_enabled:
            self.hide()
            return

        # 创建透明度动画
        self.hide_animation = QPropertyAnimation(self, b"windowOpacity")
        self.hide_animation.setDuration(300)
        self.hide_animation.setStartValue(self.windowOpacity())
        self.hide_animation.setEndValue(0.0)
        self.hide_animation.setEasingCurve(QEasingCurve.OutCubic)

        # 连接动画完成信号
        self.hide_animation.finished.connect(self.on_hide_animation_finished)

        # 启动动画
        self.hide_animation.start()

    def on_hide_animation_finished(self):
        """隐藏动画完成后的处理"""
        # 清理动画对象
        if self.hide_animation:
            self.hide_animation.deleteLater()
            self.hide_animation = None

        # 隐藏窗口
        self.hide()

    @property
    def current_theme_foreground(self):
        """获取当前主题下的前景色"""
        try:
            from app.tools.personalised import is_dark_theme
            from qfluentwidgets import qconfig

            return "#ffffff" if is_dark_theme(qconfig) else "#000000"
        except Exception:
            return "#000000"

    def _apply_custom_font(self, label, font_settings_group):
        """应用自定义字体"""
        if not font_settings_group:
            return

        use_global_font = readme_settings_async(font_settings_group, "use_global_font")
        if use_global_font == 1:
            custom_font = readme_settings_async(font_settings_group, "custom_font")
            if custom_font and hasattr(label, "setStyleSheet"):
                current_style = label.styleSheet()
                # 检查是否已经包含该字体设置，避免重复添加
                if f"font-family: '{custom_font}'" not in current_style:
                    label.setStyleSheet(
                        f"font-family: '{custom_font}'; {current_style}"
                    )

    def _calculate_target_size(self, student_labels):
        """计算目标窗口大小"""
        total_height = 0
        max_width = 0

        inner_spacing = self.content_layout.spacing()
        inner_margins = self.content_layout.contentsMargins()
        outer_layout = self.background_widget.layout()
        outer_spacing = outer_layout.spacing() if outer_layout is not None else 0
        outer_margins = (
            outer_layout.contentsMargins() if outer_layout is not None else None
        )

        for label in student_labels:
            # 计算标签大小
            label.adjustSize()
            size_hint = label.sizeHint()
            total_height += size_hint.height()
            max_width = max(max_width, size_hint.width())

        # 加上布局间距和边距
        if student_labels:
            total_height += inner_spacing * (len(student_labels) - 1)

        total_height += inner_margins.top() + inner_margins.bottom()
        max_width += inner_margins.left() + inner_margins.right()

        # 加上倒计时标签的高度
        if self.countdown_label:
            countdown_height = self.countdown_label.sizeHint().height()
            total_height += countdown_height + outer_spacing

        if outer_margins is not None:
            total_height += outer_margins.top() + outer_margins.bottom()
            max_width += outer_margins.left() + outer_margins.right()

        if (
            hasattr(self, "drag_line_container")
            and self.drag_line_container is not None
        ):
            total_height += self.drag_line_container.sizeHint().height()

        # 设置窗口大小限制
        window_width = max(300, max_width + 30)
        window_height = min(600, total_height + 30)

        return window_width, window_height

    def _clear_content_layout(self):
        """清除内容布局中的所有控件"""
        while self.content_layout.count():
            item = self.content_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()

    def update_content(
        self,
        student_labels,
        settings=None,
        font_settings_group=None,
        settings_group=None,
        is_animating=False,
    ):
        """更新通知窗口的内容

        Args:
            student_labels: 包含学生信息的BodyLabel控件列表
            settings: 通知设置参数
            font_settings_group: 字体设置组名称
            settings_group: 通知设置组名称
            is_animating: 是否在动画过程中
        """
        # 保存设置组，用于后续判断是否抢占焦点
        if settings_group:
            self.settings_group = settings_group
        elif font_settings_group:
            # 如果没有提供settings_group，尝试从font_settings_group推断
            group_mapping = {
                "roll_call_settings": "roll_call_notification_settings",
                "quick_draw_settings": "quick_draw_notification_settings",
                "lottery_settings": "lottery_notification_settings",
            }
            self.settings_group = group_mapping.get(font_settings_group, None)

        if not student_labels:
            return

        # 应用字体并预计算窗口大小
        for label in student_labels:
            self._apply_custom_font(label, font_settings_group)

        window_width, window_height = self._calculate_target_size(student_labels)

        # 清除所有旧控件
        self._clear_content_layout()

        # 添加新控件
        for label in student_labels:
            self.content_layout.addWidget(label)

        # 设置窗口大小（动画过程中避免频繁调整导致闪烁）
        if not is_animating or not self.isVisible():
            self.setFixedSize(window_width, window_height)

        # 确保颜色与当前主题同步
        try:
            self._on_theme_changed()
        except Exception as e:
            logger.exception("更新内容时同步主题时出错（已忽略）: {}", e)

        # 检查窗口是否已经显示
        if self.isVisible():
            # 如果窗口已经显示，只更新内容和透明度，不重新定位窗口
            if settings:
                transparency = settings.get("transparency", 0.6)
                self.setWindowOpacity(transparency)

                # 重新应用倒计时设置，但不重新定位窗口
                auto_close_time = settings.get("auto_close_time", 5)
                if auto_close_time > 0:
                    self.auto_close_timer.stop()
                    self.auto_close_timer.setInterval(auto_close_time * 1000)
                    # 初始化倒计时并启动更新定时器
                    self.countdown_timer.stop()
                    self.remaining_time = auto_close_time
                    self.update_countdown_display()
                    self.auto_close_timer.start()
                    self.countdown_timer.start(1000)  # 每秒更新一次
                else:
                    # 停止倒计时更新定时器并显示手动关闭提示
                    self.countdown_timer.stop()
                    self.countdown_label.setText("连续点击3次关闭窗口")
        else:
            # 如果窗口未显示，完整初始化窗口
            self.apply_settings(settings)

            # 显示窗口
            self.start_show_animation(settings)


class FloatingNotificationManager:
    """管理浮动通知窗口"""

    _instance = None
    _initialized = False

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(FloatingNotificationManager, cls).__new__(cls)
        return cls._instance

    def __init__(self):
        if not self._initialized:
            self.notification_windows = {}
            # 初始化IPC处理器
            self.ipc_handler = URLIPCHandler("SecRandom", "secrandom")
            self._initialized = True

    def send_to_classisland(
        self,
        class_name,
        selected_students,
        draw_count=1,
        settings=None,
        settings_group=None,
        fallback_on_error=True,
        is_animating=False,
    ):
        """发送通知到ClassIsland

        Args:
            class_name: 班级名称
            selected_students: 选中的学生列表 [(学号, 姓名, 是否存在), ...]
            draw_count: 抽取的学生数量
            settings: 通知设置参数
            settings_group: 设置组名称
            fallback_on_error: 是否在错误时回退到内置通知
            is_animating: 是否在动画过程中，如果是则不启动自动关闭定时器
        """

        try:
            import re

            def _normalize_text(value):
                text = str(value or "")
                text = text.replace("\r\n", "\n").replace("\r", "\n")
                text = text.strip()
                if "\n" in text:
                    text = text.replace("\n", " - ")
                text = re.sub(r"\s*-\s*", " - ", text)
                text = re.sub(r"\s{2,}", " ", text)
                return text.strip()

            def _safe_student_id(value):
                if value is None:
                    return 0
                try:
                    float_val = float(value)
                    if float_val != float_val:
                        return 0
                    return int(float_val)
                except (ValueError, TypeError, OverflowError):
                    return 0

            show_random = 0
            if settings:
                try:
                    show_random = int(settings.get("show_random", 0) or 0)
                except Exception:
                    show_random = 0

            ipc_students = None
            if isinstance(settings, dict):
                ipc_students = settings.get("ipc_selected_students")

            group_mode = False
            for item in selected_students or []:
                if (
                    isinstance(item, (list, tuple))
                    and len(item) >= 2
                    and item[0] is None
                ):
                    group_mode = True
                    break

            if isinstance(ipc_students, list) and ipc_students:
                selected_students_for_ipc = []
                for item in ipc_students:
                    if not isinstance(item, dict):
                        continue
                    student_id = _safe_student_id(item.get("student_id", 0))
                    student_name = _normalize_text(item.get("student_name", ""))
                    display_text = (
                        _normalize_text(item.get("display_text", "")) or student_name
                    )
                    group_name_raw = _normalize_text(item.get("group_name", ""))
                    lottery_name = _normalize_text(item.get("lottery_name", ""))
                    is_lottery = bool(
                        settings_group and "lottery" in str(settings_group)
                    ) or bool(lottery_name)
                    group_name = group_name_raw if (group_mode or is_lottery) else ""
                    if group_mode and (not is_lottery) and show_random == 0:
                        group_name = ""
                    selected_students_for_ipc.append(
                        {
                            "student_id": student_id,
                            "student_name": student_name,
                            "display_text": display_text,
                            "exists": bool(item.get("exists", True)),
                            "group_name": group_name,
                            "lottery_name": lottery_name,
                        }
                    )
            elif group_mode:
                selected_students_for_ipc = []
                for item in selected_students or []:
                    if not isinstance(item, (list, tuple)) or len(item) < 3:
                        continue
                    group_display_text = _normalize_text(item[1])
                    exist = bool(item[2])

                    group_name = ""
                    student_name = group_display_text
                    if show_random > 0 and " - " in group_display_text:
                        parts = group_display_text.split(" - ", 1)
                        if len(parts) == 2 and parts[0].strip() and parts[1].strip():
                            group_name = parts[0].strip()
                            student_name = parts[1].strip()

                    selected_students_for_ipc.append(
                        {
                            "student_id": 0,
                            "student_name": student_name,
                            "display_text": group_display_text,
                            "exists": exist,
                            "group_name": group_name,
                            "lottery_name": "",
                        }
                    )
            else:
                selected_students_for_ipc = []
                for item in selected_students or []:
                    if not isinstance(item, (list, tuple)) or len(item) < 3:
                        continue
                    student_id = item[0]
                    if student_id is None:
                        student_id = 0
                    normalized_name = _normalize_text(item[1])
                    exist = bool(item[2])

                    # 安全转换student_id，处理浮点数字符串如"18.0"和"nan"
                    student_id_int = _safe_student_id(student_id)

                    selected_students_for_ipc.append(
                        {
                            "student_id": student_id_int,
                            "student_name": normalized_name,
                            "display_text": normalized_name,
                            "exists": exist,
                            "group_name": "",
                            "lottery_name": "",
                        }
                    )

            cs_ipc = CSharpIPCHandler.instance()
            status = cs_ipc.send_notification(
                class_name,
                selected_students_for_ipc,
                draw_count,
                settings,
                settings_group,
                is_animating,
            )
            if status:
                logger.info("成功发送通知到ClassIsland，结果未知")
            else:
                if fallback_on_error:
                    logger.info("因错误回退到SecRandom通知服务")
                    self._show_secrandom_notification(
                        class_name,
                        selected_students,
                        draw_count,
                        settings,
                        settings_group,
                        is_animating,
                    )
                else:
                    logger.warning("发送通知到ClassIsland失败")
        except Exception as e:
            logger.exception("发送通知到ClassIsland时出错: {}", e)
            # 如果发生异常，回退到SecRandom通知服务
            if fallback_on_error:
                logger.info("因错误回退到SecRandom通知服务")
                self._show_secrandom_notification(
                    class_name,
                    selected_students,
                    draw_count,
                    settings,
                    settings_group,
                    is_animating,
                )
            else:
                logger.warning("发送通知到ClassIsland时出错，但不回退")

    def _get_display_settings(self, settings):
        """获取显示设置"""
        if settings:
            return {
                "font_size": settings.get("font_size", 50),
                "animation_color": settings.get("animation_color_theme", 0),
                "display_format": settings.get("display_format", 0),
                "display_style": settings.get("display_style", 0),
                "show_student_image": settings.get("student_image", False),
                "image_position": settings.get("image_position"),
                "is_animation_enabled": settings.get("animation", True),
                "show_random": settings.get("show_random", 0),
            }
        return {
            "font_size": 50,
            "animation_color": 0,
            "display_format": 0,
            "display_style": 0,
            "show_student_image": False,
            "image_position": None,
            "is_animation_enabled": True,
            "show_random": 0,
        }

    def _determine_font_settings_group(self, settings_group):
        """确定字体设置组"""
        if settings_group is None:
            return "notification_settings", "notification_settings"

        group_mapping = {
            "roll_call_notification_settings": "roll_call_settings",
            "quick_draw_notification_settings": "quick_draw_settings",
            "lottery_notification_settings": "lottery_settings",
        }
        font_settings_group = group_mapping.get(settings_group, settings_group)
        return settings_group, font_settings_group

    def _show_secrandom_notification(
        self,
        class_name,
        selected_students,
        draw_count=1,
        settings=None,
        settings_group=None,
        is_animating=False,
    ):
        """显示SecRandom内置通知（用于回退）

        Args:
            class_name: 班级名称
            selected_students: 选中的学生列表 [(学号, 姓名, 是否存在), ...]
            draw_count: 抽取的学生数量
            settings: 通知设置参数
            settings_group: 设置组名称
            is_animating: 是否在动画过程中，如果是则不启动自动关闭定时器
        """
        # 重新调用SecRandom通知服务，使用原始的show_roll_call_result逻辑
        display_settings = self._get_display_settings(settings)

        # 使用ResultDisplayUtils创建学生标签（动态导入避免循环依赖）
        from app.common.display.result_display import ResultDisplayUtils

        # 确定使用的设置组
        settings_group, font_settings_group = self._determine_font_settings_group(
            settings_group
        )

        group_index = 0
        for item in selected_students or []:
            if isinstance(item, (list, tuple)) and len(item) >= 2 and item[0] is None:
                group_index = 1
                break

        student_labels = ResultDisplayUtils.create_student_label(
            class_name=class_name,
            selected_students=selected_students,
            draw_count=draw_count,
            font_size=display_settings["font_size"],
            animation_color=display_settings["animation_color"],
            display_format=display_settings["display_format"],
            display_style=0,
            show_student_image=display_settings["show_student_image"],
            image_position=display_settings.get("image_position"),
            group_index=group_index,
            show_random=display_settings.get("show_random", 0),
            settings_group=font_settings_group,
            custom_font_family=font_settings_group,
        )

        # 创建或获取通知窗口
        if "floating" not in self.notification_windows:
            self.notification_windows["floating"] = FloatingNotificationWindow()

        window = self.notification_windows["floating"]
        window.is_animation_enabled = display_settings["is_animation_enabled"]
        # 如果窗口已经存在并且有活动的自动关闭定时器，停止它以防止窗口被隐藏
        if window.auto_close_timer.isActive():
            window.auto_close_timer.stop()

        # 应用显示时长设置到窗口
        if settings:
            display_duration = settings.get("notification_display_duration", 5)
            window.apply_settings(
                {**settings, "auto_close_time": display_duration},
                settings_group,
                is_animating,
            )
        else:
            window.apply_settings(
                {"auto_close_time": 5}, settings_group, is_animating
            )  # 默认5秒

        window.update_content(
            student_labels, settings, font_settings_group, settings_group, is_animating
        )

    def get_notification_title(self):
        """获取通知标题文本（支持多语言）"""
        try:
            from app.Language.obtain_language import get_content_name_async

            return get_content_name_async(
                "notification_settings", "notification_result"
            )
        except Exception as e:
            logger.exception("获取通知标题时出错（使用备用方案）: {}", e)
            # 如果无法获取多语言文本，则使用默认文本
            return "通知结果"

    def show_roll_call_result(
        self,
        class_name,
        selected_students,
        draw_count=1,
        settings=None,
        settings_group=None,
        is_animating=False,
    ):
        """在浮动通知窗口中显示点名结果

        Args:
            class_name: 班级名称
            selected_students: 选中的学生列表 [(学号, 姓名, 是否存在), ...]
            draw_count: 抽取的学生数量
            settings: 通知设置参数
            settings_group: 设置组名称，默认为notification_settings
            is_animating: 是否在动画过程中，如果是则不启动自动关闭定时器
        """
        notification_service_type = 0
        exceed_threshold = False
        if settings_group:
            try:
                notification_service_type = readme_settings_async(
                    settings_group, "notification_service_type"
                )
            except Exception as e:
                logger.warning("获取通知服务类型设置失败，使用默认值: {}", e)
                notification_service_type = 0

            try:
                use_main_window_when_exceed_threshold = readme_settings_async(
                    settings_group, "use_main_window_when_exceed_threshold"
                )
                max_notify_count = readme_settings_async(
                    settings_group, "main_window_display_threshold"
                )
                exceed_threshold = use_main_window_when_exceed_threshold and int(
                    draw_count or 0
                ) > int(max_notify_count or 0)
            except Exception:
                exceed_threshold = False

        if exceed_threshold:
            return

        # 如果选择了ClassIsland通知服务，则发送到ClassIsland
        if notification_service_type == 1:  # 1 表示 ClassIsland 通知服务
            self.send_to_classisland(
                class_name,
                selected_students,
                draw_count,
                settings,
                settings_group,
                is_animating=is_animating,
            )
            return

        # 如果选择了内置+ClassIsland通知服务，则同时发送到两个服务
        if notification_service_type == 2:  # 2 表示 内置+ClassIsland 通知服务
            # 先发送到ClassIsland，不回退（因为会继续显示内置通知）
            self.send_to_classisland(
                class_name,
                selected_students,
                draw_count,
                settings,
                settings_group,
                fallback_on_error=False,
                is_animating=is_animating,
            )

        # 否则使用SecRandom浮窗通知
        self._show_secrandom_notification(
            class_name,
            selected_students,
            draw_count,
            settings,
            settings_group,
            is_animating,
        )

    def close_all_notifications(self):
        """关闭所有浮动通知窗口"""
        # 关闭浮窗
        for window in self.notification_windows.values():
            if window.auto_close_timer.isActive():
                window.auto_close_timer.stop()
            window.close()
        self.notification_windows.clear()


def show_roll_call_notification(
    class_name,
    selected_students,
    draw_count=1,
    settings=None,
    settings_group=None,
    is_animating=False,
):
    """显示点名通知的便捷函数

    Args:
        class_name: 班级名称
        selected_students: 选中的学生列表 [(学号, 姓名, 是否存在), ...]
        draw_count: 抽取的学生数量
        settings: 通知设置参数
        settings_group: 设置组名称
        is_animating: 是否在动画过程中，如果是则不启动自动关闭定时器
    """
    manager = FloatingNotificationManager()
    manager.show_roll_call_result(
        class_name,
        selected_students,
        draw_count,
        settings,
        settings_group,
        is_animating,
    )
