# ==================================================
# 导入库
# ==================================================

import getpass
import time
from datetime import datetime

from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *
from qfluentwidgets import FluentIcon as FIF
from loguru import logger

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *
from app.common.safety.secure_store import (
    read_behind_scenes_settings,
    write_behind_scenes_settings,
)
from app.core.app_init import calculate_total_draw_counts
from app.view.components.center_flow_layout import CenterFlowLayout
from app.tools.online_status import get_cached_online_count, get_online_stats_async
import app.core.window_manager as wm

from app.page_building.another_window import create_contributor_window


# ==================================================
# 关于主容器类
# ==================================================
class about(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加横幅组件
        self.banner_widget = about_banner(self)
        self.vBoxLayout.addWidget(self.banner_widget)

        # 添加关于信息组件
        self.about_info_widget = about_info(self)
        self.vBoxLayout.addWidget(self.about_info_widget)

        self.user_info_card_widget = user_info_card(self)
        self.vBoxLayout.addWidget(self.user_info_card_widget)


# ==================================================
# 横幅组件类
# ==================================================
class about_banner(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        banner_path = get_data_path("assets/icon", "secrandom-banner.png")
        self.banner_image = ImageLabel(f"{banner_path}")
        self.banner_image.scaledToHeight(300)
        self.banner_image.setBorderRadius(12, 12, 12, 12)
        self.banner_image.setScaledContents(True)

        # 加载点击次数
        self.click_count = self._load_click_count()

        # 添加横幅图片到布局
        self.vBoxLayout = QHBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(0)

        # 使图片居中
        self.vBoxLayout.addWidget(self.banner_image, 0, Qt.AlignmentFlag.AlignCenter)

        # 连接点击事件
        self.banner_image.mousePressEvent = self._on_banner_clicked

    def _load_click_count(self):
        """加载横幅点击次数"""
        try:
            data = read_behind_scenes_settings()
            return data.get("banner_click_count", 0)
        except Exception as e:
            logger.exception(f"加载横幅点击次数失败: {e}")
            return 0

    def _save_click_count(self, count):
        """保存横幅点击次数"""
        try:
            data = read_behind_scenes_settings()
            data["banner_click_count"] = count
            write_behind_scenes_settings(data)
        except Exception as e:
            logger.exception(f"保存横幅点击次数失败: {e}")

    def _on_banner_clicked(self, event):
        """横幅点击事件"""
        self.click_count += 1
        self._save_click_count(self.click_count)


# ==================================================
# 关于信息组件类
# ==================================================
class about_info(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("about", "title"))
        self.setBorderRadius(8)

        # 打开GitHub按钮
        self.about_github_Button = HyperlinkButton(
            FIF.GITHUB, GITHUB_WEB, get_content_name_async("about", "github")
        )
        github_widget = self._create_button_with_icon(
            self.about_github_Button, "assets/icon", "sectl-icon.png"
        )

        # 打开bilibili按钮
        self.about_bilibili_Button = HyperlinkButton(
            get_theme_icon("ic_fluent_globe_arrow_forward_20_filled"),
            BILIBILI_WEB,
            get_content_name_async("about", "bilibili"),
        )
        bilibili_widget = self._create_button_with_icon(
            self.about_bilibili_Button, "assets/contribution", "contributor1.png"
        )

        # 打开网站按钮
        self.about_website_Button = HyperlinkButton(
            get_theme_icon("ic_fluent_globe_arrow_forward_20_filled"),
            WEBSITE,
            get_content_name_async("about", "website"),
        )
        website_widget = self._create_button_with_icon(
            self.about_website_Button, "assets/icon", "secrandom-icon-paper.png"
        )

        self.about_organization_Button = HyperlinkButton(
            get_theme_icon("ic_fluent_globe_arrow_forward_20_filled"),
            SECTL_WEBDITE,
            get_content_name_async("about", "organization_website"),
        )
        organization_widget = self._create_button_with_icon(
            self.about_organization_Button, "assets/icon", "sectl-icon.png"
        )

        # 查看当前软件版本号
        version_text = f"{SPECIAL_VERSION} | {CODENAME} ({SYSTEM}-{ARCH})"
        self.about_version_label = BodyLabel(version_text)

        # 查看当前软件版权所属
        # 根据发布年份和当前年份是否相同，决定显示格式
        if INITIAL_AUTHORING_YEAR == CURRENT_YEAR:
            copyright_text = f"Copyright © {INITIAL_AUTHORING_YEAR} {COPYRIGHT_HOLDER}"
        else:
            copyright_text = f"Copyright © {INITIAL_AUTHORING_YEAR}-{CURRENT_YEAR} {COPYRIGHT_HOLDER}"

        self.about_author_label = BodyLabel(copyright_text)
        copyright_widget = self._create_label_with_icon(
            self.about_author_label, "assets/icon", "sectl-icon.png"
        )

        # 创建贡献人员按钮
        self.contributor_button = PushButton(
            get_content_name_async("about", "contributor")
        )
        self.contributor_button.setIcon(
            get_theme_icon("ic_fluent_document_person_20_filled")
        )
        self.contributor_button.clicked.connect(self.show_contributors)

        # 创建捐赠支持按钮
        self.donation_button = PushButton(get_content_name_async("about", "donation"))
        self.donation_button.setIcon(
            get_theme_icon("ic_fluent_document_person_20_filled")
        )
        self.donation_button.clicked.connect(self.open_donation_url)

        self.addGroup(
            get_theme_icon("ic_fluent_branch_fork_link_20_filled"),
            get_content_name_async("about", "bilibili"),
            get_content_description_async("about", "bilibili"),
            bilibili_widget,
        )
        self.addGroup(
            FIF.GITHUB,
            get_content_name_async("about", "github"),
            get_content_description_async("about", "github"),
            github_widget,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_person_20_filled"),
            get_content_name_async("about", "contributor"),
            get_content_description_async("about", "contributor"),
            self.contributor_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_document_person_20_filled"),
            get_content_name_async("about", "donation"),
            get_content_description_async("about", "donation"),
            self.donation_button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_class_20_filled"),
            get_content_name_async("about", "copyright"),
            get_content_description_async("about", "copyright"),
            copyright_widget,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_globe_arrow_forward_20_filled"),
            get_content_name_async("about", "organization_website"),
            get_content_description_async("about", "organization_website"),
            organization_widget,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_globe_arrow_forward_20_filled"),
            get_content_name_async("about", "website"),
            get_content_description_async("about", "website"),
            website_widget,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_flag_pride_20_filled"),
            get_content_name_async("about", "version"),
            get_content_description_async("about", "version"),
            self.about_version_label,
        )

        self.online_count_label = BodyLabel(self._get_online_count_text())
        self.addGroup(
            get_theme_icon("ic_fluent_people_20_filled"),
            get_content_name_async("about", "online_count"),
            get_content_description_async("about", "online_count"),
            self.online_count_label,
        )

        self._online_count_timer = QTimer(self)
        self._online_count_timer.setInterval(60000)
        self._online_count_timer.timeout.connect(self._update_online_count)
        self._online_count_timer.start()
        self._fetch_online_count()

    def _get_online_count_text(self) -> str:
        count = get_cached_online_count()
        if count > 0:
            return str(count)
        return "-"

    def _fetch_online_count(self):
        count = get_cached_online_count()
        self.online_count_label.setText(str(count) if count > 0 else "-")

    def _update_online_count(self):
        self._fetch_online_count()

    def show_contributors(self):
        """显示贡献人员"""
        create_contributor_window()

    def open_donation_url(self):
        """打开捐赠链接"""
        QDesktopServices.openUrl(QUrl(DONATION_URL))

    def _create_button_with_icon(self, button, icon_dir, icon_name):
        """创建带图标的按钮容器"""
        widget = QWidget()
        layout = QHBoxLayout(widget)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(8)

        layout.addWidget(button)

        icon_path = get_data_path(icon_dir, icon_name)
        icon_label = ImageLabel(f"{icon_path}")
        icon_label.scaledToHeight(30)
        icon_label.setBorderRadius(8, 8, 8, 8)
        layout.addWidget(icon_label)

        layout.addStretch()
        return widget

    def _create_label_with_icon(self, label, icon_dir, icon_name):
        """创建带图标的标签容器"""
        widget = QWidget()
        layout = QHBoxLayout(widget)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(8)

        layout.addWidget(label)

        icon_path = get_data_path(icon_dir, icon_name)
        icon_label = ImageLabel(f"{icon_path}")
        icon_label.scaledToHeight(30)
        icon_label.setBorderRadius(8, 8, 8, 8)
        layout.addWidget(icon_label)

        layout.addStretch()
        return widget


class user_info_card(HeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("about", "user_info"))

        user_id = get_or_create_user_id()
        user_name = self._get_user_name()
        first_use_time = self._get_or_create_first_use_time()
        total_draw_count, roll_call_total_count, lottery_total_count = (
            self._ensure_usage_stats()
        )

        self.user_name_label = BodyLabel(
            self._format_label_text(
                get_content_name_async("about", "user_name"), user_name
            )
        )
        self.user_id_label = BodyLabel(
            self._format_label_text(get_content_name_async("about", "user_id"), user_id)
        )
        self.first_use_time_label = BodyLabel(
            self._format_label_text(
                get_content_name_async("about", "first_use_time"), first_use_time
            )
        )
        self._runtime_base_seconds = self._normalize_count(
            readme_settings_async("user_info", "total_runtime_seconds")
        )
        self._runtime_last_persist_minute = int(self._runtime_base_seconds // 60)

        self.runtime_label = BodyLabel(
            self._format_label_text(
                get_content_name_async("about", "runtime"),
                self._format_duration(self._get_total_runtime_seconds()),
            )
        )

        self.total_draw_label = BodyLabel(
            self._format_label_text(
                get_content_name_async("about", "total_draw_count"), total_draw_count
            )
        )
        self.roll_call_total_label = BodyLabel(
            self._format_label_text(
                get_content_name_async("about", "roll_call_total_count"),
                roll_call_total_count,
            )
        )
        self.lottery_total_label = BodyLabel(
            self._format_label_text(
                get_content_name_async("about", "lottery_total_count"),
                lottery_total_count,
            )
        )

        self.info_layout = QVBoxLayout()
        self.info_layout.setContentsMargins(0, 0, 0, 0)
        self.info_layout.setSpacing(6)
        self.info_layout.addWidget(self.user_name_label)
        self.info_layout.addWidget(self.user_id_label)
        self.info_layout.addWidget(self.first_use_time_label)
        self.info_layout.addWidget(self.runtime_label)

        self.stats_widget = QWidget()
        self.stats_layout = CenterFlowLayout(self.stats_widget)
        self.stats_layout.setContentsMargins(0, 0, 0, 0)
        self.stats_layout.setSpacing(8)
        self.stats_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.stats_layout.addWidget(self.total_draw_label)
        self.stats_layout.addWidget(self.roll_call_total_label)
        self.stats_layout.addWidget(self.lottery_total_label)
        self.info_layout.addWidget(self.stats_widget)

        self.copy_button = PushButton(get_content_name_async("about", "copy_user_info"))
        self.copy_button.clicked.connect(self._copy_user_info)
        self.copy_layout = QHBoxLayout()
        self.copy_layout.setContentsMargins(0, 0, 0, 0)
        self.copy_layout.addWidget(self.copy_button)
        self.copy_layout.addStretch()
        self.info_layout.addLayout(self.copy_layout)

        self.viewLayout.addLayout(self.info_layout)

        self.runtime_timer = QTimer(self)
        self.runtime_timer.setInterval(1000)
        self.runtime_timer.timeout.connect(self._update_runtime_label)
        self.runtime_timer.start()

    def _get_user_name(self):
        try:
            user_name = getpass.getuser()
            return user_name if user_name else "Unknown"
        except Exception:
            return "Unknown"

    def _get_or_create_first_use_time(self):
        first_use_time = readme_settings_async("user_info", "first_use_time")
        if isinstance(first_use_time, str) and first_use_time.strip():
            return first_use_time
        current_time = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        update_settings("user_info", "first_use_time", current_time)
        return current_time

    def _get_runtime_seconds(self):
        start_time = wm.app_start_time if getattr(wm, "app_start_time", 0) else 0
        if start_time <= 0:
            return 0
        return max(0, time.perf_counter() - start_time)

    def _format_duration(self, seconds):
        total_seconds = max(0, int(seconds))
        years = total_seconds // (365 * 24 * 3600)
        remaining = total_seconds % (365 * 24 * 3600)
        days = remaining // (24 * 3600)
        remaining = remaining % (24 * 3600)
        hours = remaining // 3600
        minutes = (remaining % 3600) // 60
        secs = remaining % 60
        parts = []
        if years:
            parts.append(
                get_content_name_async("about", "runtime_years").format(value=years)
            )
        if days:
            parts.append(
                get_content_name_async("about", "runtime_days").format(value=days)
            )
        parts.append(
            get_content_name_async("about", "runtime_hms").format(
                hours=hours, minutes=minutes, seconds=secs
            )
        )
        return " ".join(parts)

    def _format_label_text(self, title, value):
        return f"{title}: {value}"

    def _update_runtime_label(self):
        total_seconds = self._get_total_runtime_seconds()
        runtime_text = self._format_duration(total_seconds)
        self.runtime_label.setText(
            self._format_label_text(
                get_content_name_async("about", "runtime"), runtime_text
            )
        )
        # 每秒持久化一次运行时长
        update_settings("user_info", "total_runtime_seconds", int(total_seconds))

    def _get_total_runtime_seconds(self):
        session_seconds = self._get_runtime_seconds()
        return self._runtime_base_seconds + session_seconds

    def _copy_user_info(self):
        text = "\n".join(
            [
                self.user_name_label.text(),
                self.user_id_label.text(),
                self.first_use_time_label.text(),
                self.runtime_label.text(),
                self.total_draw_label.text(),
                self.roll_call_total_label.text(),
                self.lottery_total_label.text(),
            ]
        )
        QApplication.clipboard().setText(text)

    def _ensure_usage_stats(self):
        total_draw_count, roll_call_total_count, lottery_total_count = (
            self._calculate_usage_stats()
        )
        stored_total = self._normalize_count(
            readme_settings_async("user_info", "total_draw_count")
        )
        stored_roll_call = self._normalize_count(
            readme_settings_async("user_info", "roll_call_total_count")
        )
        stored_lottery = self._normalize_count(
            readme_settings_async("user_info", "lottery_total_count")
        )
        if (
            stored_total != total_draw_count
            or stored_roll_call != roll_call_total_count
            or stored_lottery != lottery_total_count
        ):
            update_settings("user_info", "total_draw_count", total_draw_count)
            update_settings("user_info", "roll_call_total_count", roll_call_total_count)
            update_settings("user_info", "lottery_total_count", lottery_total_count)
        return total_draw_count, roll_call_total_count, lottery_total_count

    def _calculate_usage_stats(self):
        return calculate_total_draw_counts()

    def _normalize_count(self, value):
        if value is None:
            return 0
        try:
            return int(value)
        except Exception:
            return 0
