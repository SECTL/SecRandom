# ==================================================
# 导入库
# ==================================================
from PySide6.QtWidgets import *
from PySide6.QtCore import *
from PySide6.QtGui import QFont, QIcon
from qfluentwidgets import *
from qframelesswindow import FramelessWindow
from app.view.guide.pages import *
from loguru import logger
from app.Language.obtain_language import get_any_position_value_async
from app.tools.settings_access import update_settings
from app.tools.personalised import load_custom_font
from app.tools.path_utils import get_data_path


# ==================================================
# 引导窗口类
# ==================================================
class GuideWindow(FramelessWindow):
    guideFinished = Signal()
    _DEFAULT_TITLEBAR_HEIGHT = 32
    _TITLEBAR_LEFT_OFFSET_PX = 5

    def __init__(self, width=800, height=600, parent=None):
        super().__init__(parent)
        self.setTitleBar(FluentTitleBar(self))
        try:
            fixed_h = max(
                int(self.titleBar.sizeHint().height()), self._DEFAULT_TITLEBAR_HEIGHT
            )
            self.titleBar.setFixedHeight(fixed_h)
        except Exception:
            self.titleBar.setFixedHeight(self._DEFAULT_TITLEBAR_HEIGHT)
        self.setWindowIcon(
            QIcon(str(get_data_path("assets/icon", "secrandom-icon-paper.png")))
        )
        self.setWindowTitle("SecRandom")
        self.resize(width, height)
        self.setWindowModality(Qt.WindowModality.NonModal)

        # 标题栏
        self.titleBar.setAttribute(Qt.WidgetAttribute.WA_StyledBackground)
        self.titleBar.raise_()
        self._apply_titlebar_font()
        self._rebuild_titlebar_title_and_icon()

        # 主布局
        self.vBoxLayout = QVBoxLayout(self)
        self._sync_layout_margins_with_titlebar()

        # 页面容器
        self.stackedWidget = QStackedWidget(self)
        self.vBoxLayout.addWidget(self.stackedWidget)

        self._page_transition_anim = None
        self._page_transition_target_index = None
        self._initial_show_anim = None
        self._initial_show_animated = False
        self._guide_finished = False
        self._prev_override_index = None

        # 底部导航栏
        self.bottomBar = QWidget(self)
        self.bottomLayout = QHBoxLayout(self.bottomBar)
        self.bottomLayout.setContentsMargins(20, 10, 20, 20)

        self.prevBtn = PushButton(
            get_any_position_value_async("guide", "previous"), self
        )
        self.nextBtn = PrimaryPushButton(
            get_any_position_value_async("guide", "next"), self
        )
        self.bottomLayout.addStretch(1)
        self.bottomLayout.addWidget(self.prevBtn)
        self.bottomLayout.addWidget(self.nextBtn)

        self.vBoxLayout.addWidget(self.bottomBar)

        # 初始化页面
        self.init_pages()

        # 连接信号
        self.prevBtn.clicked.connect(self.prev_page)
        self.nextBtn.clicked.connect(self.next_page)

        self.update_nav_buttons()
        self._on_page_changed(self.current_index)

    def showEvent(self, event):
        super().showEvent(event)
        desktop = QApplication.primaryScreen().availableGeometry()
        self.move(desktop.center() - self.rect().center())
        QTimer.singleShot(0, self._sync_layout_margins_with_titlebar)
        QTimer.singleShot(0, self._rebuild_titlebar_title_and_icon)
        if self._initial_show_animated:
            return
        self._initial_show_animated = True
        QTimer.singleShot(0, self._start_initial_show_animation)

    def _start_initial_show_animation(self):
        if self.current_index != 0:
            return

        if (
            self._page_transition_anim is not None
            and self._page_transition_anim.state() == QAbstractAnimation.State.Running
        ):
            return

        if (
            self._initial_show_anim is not None
            and self._initial_show_anim.state() == QAbstractAnimation.State.Running
        ):
            self._initial_show_anim.stop()
            self._initial_show_anim.deleteLater()
            self._initial_show_anim = None

        effect = QGraphicsOpacityEffect(self.stackedWidget)
        effect.setOpacity(0.0)
        self.stackedWidget.setGraphicsEffect(effect)

        opacity_anim = QPropertyAnimation(effect, b"opacity", self)
        opacity_anim.setDuration(520)
        opacity_anim.setStartValue(0.0)
        opacity_anim.setEndValue(1.0)
        opacity_anim.setEasingCurve(QEasingCurve.Type.OutCubic)

        start_pos = self.stackedWidget.pos()
        pos_anim = QPropertyAnimation(self.stackedWidget, b"pos", self)
        pos_anim.setDuration(520)
        pos_anim.setStartValue(QPoint(start_pos.x(), start_pos.y() + 14))
        pos_anim.setEndValue(start_pos)
        pos_anim.setEasingCurve(QEasingCurve.Type.OutCubic)

        group = QParallelAnimationGroup(self)
        group.addAnimation(opacity_anim)
        group.addAnimation(pos_anim)

        def cleanup():
            self._initial_show_anim = None
            self.stackedWidget.setGraphicsEffect(None)

        group.finished.connect(cleanup)
        self._initial_show_anim = group
        group.start()

    def _apply_titlebar_font(self):
        custom_font = load_custom_font()
        try:
            if getattr(self, "_sr_title_text_label", None) is not None:
                if custom_font:
                    self._sr_title_text_label.setFont(QFont(custom_font, 9))
                else:
                    f = self._sr_title_text_label.font()
                    f.setPointSize(9)
                    self._sr_title_text_label.setFont(f)
        except Exception:
            pass

        for child in self.titleBar.findChildren(QLabel):
            if not isinstance(child, QLabel):
                continue
            label_text = child.text() if hasattr(child, "text") else ""
            if not label_text:
                continue
            if custom_font:
                child.setFont(QFont(custom_font, 9))
            else:
                f = child.font()
                f.setPointSize(9)
                child.setFont(f)
            break

    def _sync_layout_margins_with_titlebar(self) -> None:
        top = 0
        try:
            top = int(self.titleBar.height()) if self.titleBar else 0
        except Exception:
            top = 0

        if top <= 1:
            try:
                top = int(self.titleBar.sizeHint().height()) if self.titleBar else 0
            except Exception:
                top = 0

        if top <= 1:
            top = self._DEFAULT_TITLEBAR_HEIGHT

        self.vBoxLayout.setContentsMargins(0, top, 0, 0)

    def _ensure_sr_title_overlay(self) -> None:
        if getattr(self, "_sr_title_overlay", None) is not None:
            return

        self._sr_title_overlay = QWidget(self.titleBar)
        self._sr_title_overlay.setAttribute(
            Qt.WidgetAttribute.WA_TransparentForMouseEvents
        )
        self._sr_title_overlay.setObjectName("srTitleOverlay")

        layout = QHBoxLayout(self._sr_title_overlay)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(6)

        self._sr_title_icon_label = QLabel(self._sr_title_overlay)
        self._sr_title_icon_label.setAttribute(
            Qt.WidgetAttribute.WA_TransparentForMouseEvents
        )
        self._sr_title_icon_label.setObjectName("srTitleIcon")
        self._sr_title_icon_label.setFixedSize(16, 16)

        self._sr_title_text_label = QLabel(self._sr_title_overlay)
        self._sr_title_text_label.setAttribute(
            Qt.WidgetAttribute.WA_TransparentForMouseEvents
        )
        self._sr_title_text_label.setObjectName("srTitleLabel")
        self._sr_title_text_label.setSizePolicy(
            QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed
        )

        layout.addWidget(self._sr_title_icon_label, 0, Qt.AlignmentFlag.AlignVCenter)
        layout.addWidget(self._sr_title_text_label, 0, Qt.AlignmentFlag.AlignVCenter)

        try:
            self.windowTitleChanged.connect(self._update_sr_title_overlay)
            self.windowIconChanged.connect(self._update_sr_title_overlay)
        except Exception:
            pass

    def _update_sr_title_overlay(self, *args) -> None:
        try:
            if getattr(self, "_sr_title_text_label", None) is None:
                return

            title = self.windowTitle() or ""
            self._sr_title_text_label.setText(title)

            icon = self.windowIcon()
            pixmap = None
            try:
                if icon and not icon.isNull():
                    pixmap = icon.pixmap(16, 16)
            except Exception:
                pixmap = None

            if pixmap is not None and not pixmap.isNull():
                self._sr_title_icon_label.setPixmap(pixmap)
                self._sr_title_icon_label.show()
            else:
                self._sr_title_icon_label.hide()

            self._sr_title_overlay.adjustSize()
        except Exception as e:
            logger.exception(f"更新自定义标题栏失败: {e}")

    def _position_sr_title_overlay(self) -> None:
        try:
            if getattr(self, "_sr_title_overlay", None) is None:
                return
            self._sr_title_overlay.adjustSize()
            x = int(self._TITLEBAR_LEFT_OFFSET_PX)
            y = max(
                0, int((self.titleBar.height() - self._sr_title_overlay.height()) / 2)
            )
            self._sr_title_overlay.move(x, y)
            self._sr_title_overlay.raise_()
        except Exception as e:
            logger.exception(f"定位自定义标题栏失败: {e}")

    def _rebuild_titlebar_title_and_icon(self) -> None:
        try:
            if not self.titleBar:
                return

            current_title = self.windowTitle() or ""
            for child in self.titleBar.findChildren(QLabel):
                if child.objectName() in {"srTitleIcon", "srTitleLabel"}:
                    continue

                try:
                    pixmap = child.pixmap()
                except Exception:
                    pixmap = None

                label_text = child.text() if hasattr(child, "text") else ""
                is_original_icon = pixmap is not None and not pixmap.isNull()
                is_original_title = bool(current_title) and label_text == current_title

                if is_original_icon or is_original_title:
                    child.hide()

            self._ensure_sr_title_overlay()
            self._update_sr_title_overlay()
            self._apply_titlebar_font()
            self._position_sr_title_overlay()
        except Exception as e:
            logger.exception(f"重建标题栏标题失败: {e}")

    def _start_page_transition(self, target_index: int):
        if target_index < 0 or target_index >= len(self.pages):
            return

        if self.stackedWidget.currentIndex() == target_index:
            return

        if (
            self._page_transition_anim is not None
            and self._page_transition_anim.state() == QAbstractAnimation.State.Running
        ):
            self._page_transition_anim.stop()
            self._page_transition_anim.deleteLater()
            self._page_transition_anim = None

        if (
            self._initial_show_anim is not None
            and self._initial_show_anim.state() == QAbstractAnimation.State.Running
        ):
            self._initial_show_anim.stop()
            self._initial_show_anim.deleteLater()
            self._initial_show_anim = None
            self.stackedWidget.setGraphicsEffect(None)

        self._page_transition_target_index = target_index
        self.prevBtn.setEnabled(False)
        self.nextBtn.setEnabled(False)

        effect = self.stackedWidget.graphicsEffect()
        if not isinstance(effect, QGraphicsOpacityEffect):
            effect = QGraphicsOpacityEffect(self.stackedWidget)
            self.stackedWidget.setGraphicsEffect(effect)
        effect.setOpacity(1.0)
        normal_pos = self.stackedWidget.pos()

        fade_out = QPropertyAnimation(effect, b"opacity", self)
        fade_out.setDuration(140)
        fade_out.setStartValue(1.0)
        fade_out.setEndValue(0.0)
        fade_out.setEasingCurve(QEasingCurve.Type.OutCubic)

        fade_in = QPropertyAnimation(effect, b"opacity", self)
        fade_in.setDuration(220)
        fade_in.setStartValue(0.0)
        fade_in.setEndValue(1.0)
        fade_in.setEasingCurve(QEasingCurve.Type.OutCubic)

        pos_anim = QPropertyAnimation(self.stackedWidget, b"pos", self)
        pos_anim.setDuration(220)
        pos_anim.setStartValue(QPoint(normal_pos.x(), normal_pos.y() + 14))
        pos_anim.setEndValue(normal_pos)
        pos_anim.setEasingCurve(QEasingCurve.Type.OutCubic)

        fade_in_group = QParallelAnimationGroup(self)
        fade_in_group.addAnimation(fade_in)
        fade_in_group.addAnimation(pos_anim)

        group = QSequentialAnimationGroup(self)
        group.addAnimation(fade_out)
        group.addAnimation(QPauseAnimation(50, self))
        group.addAnimation(fade_in_group)

        def apply_index():
            target = self._page_transition_target_index
            if target is None:
                return
            self.current_index = target
            self.stackedWidget.setCurrentIndex(target)
            self.stackedWidget.move(normal_pos.x(), normal_pos.y() + 14)
            if self.current_index == len(self.pages) - 1:
                self.nextBtn.setText(get_any_position_value_async("guide", "finish"))
            else:
                self.nextBtn.setText(get_any_position_value_async("guide", "next"))
            self.prevBtn.setEnabled(False)
            self.nextBtn.setEnabled(False)

        def cleanup():
            self._page_transition_target_index = None
            self._page_transition_anim = None
            self.stackedWidget.setGraphicsEffect(None)
            self.stackedWidget.move(normal_pos)
            self._on_page_changed(self.current_index)
            self.update_nav_buttons()

        fade_out.finished.connect(apply_index)
        group.finished.connect(cleanup)

        self._page_transition_anim = group
        group.start()

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        self._sync_layout_margins_with_titlebar()
        self._position_sr_title_overlay()

    def init_pages(self):
        self.welcomePage = WelcomePage(self)
        self.languagePage = LanguagePage(self)
        self.licensePage = LicensePage(self)
        self.migrationPage = MigrationPage(self)
        self.basicSettingsPage = BasicSettingsPage(self)
        self.listPage = ListPage(self)
        self.enhancedPage = EnhancedPage(self)
        self.testPage = TestPage(self)
        self.linksPage = LinksPage(self)

        self.pages = [
            self.welcomePage,
            self.languagePage,
            self.licensePage,
            self.migrationPage,
            self.basicSettingsPage,
            self.listPage,
            self.enhancedPage,
            self.testPage,
            self.linksPage,
        ]

        for page in self.pages:
            self.stackedWidget.addWidget(page)

        self.current_index = 0

        # 许可协议页面逻辑
        self.licensePage.licenseAccepted.connect(self._on_license_accepted)

        # 页面切换监听
        self.stackedWidget.currentChanged.connect(self._on_page_changed)

    def _on_page_changed(self, index):
        self.bottomBar.setVisible(self.pages[index] != self.welcomePage)
        if self.pages[index] == self.licensePage:
            self.nextBtn.setEnabled(self.licensePage.is_accepted())
        else:
            self.nextBtn.setEnabled(True)

    def _on_license_accepted(self, accepted):
        if self.pages[self.current_index] == self.licensePage:
            self.nextBtn.setEnabled(accepted)

    def next_page(self):
        next_idx = self.current_index + 1

        if next_idx < len(self.pages):
            self._start_page_transition(next_idx)
        else:
            update_settings("basic_settings", "guide_completed", True)
            self._guide_finished = True
            self.guideFinished.emit()
            self.close()

    def prev_page(self):
        if self._prev_override_index is not None:
            target = self._prev_override_index
            self._prev_override_index = None
            self._start_page_transition(target)
            return

        prev_idx = self.current_index - 1

        if prev_idx >= 0:
            self._start_page_transition(prev_idx)

    def update_nav_buttons(self):
        self.prevBtn.setEnabled(self.current_index > 0)
        is_last_page = self.current_index == len(self.pages) - 1
        self.nextBtn.setVisible(not is_last_page)

        if is_last_page:
            self.nextBtn.setText(get_any_position_value_async("guide", "finish"))
        else:
            self.nextBtn.setText(get_any_position_value_async("guide", "next"))

    def closeEvent(self, event):
        if self._guide_finished:
            event.accept()
            return super().closeEvent(event)

        dialog = MessageBox(
            get_any_position_value_async("guide", "exit_confirm", "title"),
            get_any_position_value_async("guide", "exit_confirm", "content"),
            self,
        )
        dialog.yesButton.setText(
            get_any_position_value_async("guide", "exit_confirm", "exit_button")
        )
        dialog.cancelButton.setText(
            get_any_position_value_async("guide", "exit_confirm", "cancel_button")
        )

        if dialog.exec():
            event.accept()
            super().closeEvent(event)
            QApplication.quit()
        else:
            event.ignore()
