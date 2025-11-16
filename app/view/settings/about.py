# ==================================================
# 导入库
# ==================================================

from loguru import logger
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *
from qfluentwidgets import FluentIcon as FIF

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *

from app.page_building.another_window import create_contributor_window


# ==================================================
# 关于卡片类
# ==================================================
class about(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("about", "title"))
        self.setBorderRadius(8)

        # 打开GitHub按钮
        self.about_github_Button = HyperlinkButton(
            FIF.GITHUB, GITHUB_WEB, get_content_name_async("about", "github")
        )

        # 打开bilibili按钮
        self.about_bilibili_Button = HyperlinkButton(
            BILIBILI_WEB, get_content_name_async("about", "bilibili")
        )

        # 打开网站按钮
        self.about_website_Button = HyperlinkButton(
            WEBSITE, get_content_name_async("about", "website")
        )

        # 查看当前软件版本号
        version_text = (
            f"Dev Version-{NEXT_VERSION}" if VERSION == "v0.0.0.0" else VERSION
        )
        self.about_version_label = BodyLabel(version_text)

        # 查看当前软件版权所属
        self.about_author_label = BodyLabel(f"Copyright © {YEAR} {APPLY_NAME}")

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

        # 检查更新按钮
        self.check_update_button = PushButton(
            get_content_name_async("about", "check_update")
        )
        self.check_update_button.setIcon(
            get_theme_icon("ic_fluent_arrow_sync_20_filled")
        )
        self.check_update_button.clicked.connect(self.check_updates)

        # 添加更新通道选择
        self.channel_combo = ComboBox()
        self.channel_combo.addItems(get_content_combo_name_async("about", "channel"))
        self.channel_combo.setCurrentIndex(readme_settings_async("about", "channel"))
        self.channel_combo.currentIndexChanged.connect(
            lambda: update_settings(
                "about", "channel", self.channel_combo.currentIndex()
            )
        )

        self.addGroup(
            get_theme_icon("ic_fluent_branch_fork_link_20_filled"),
            get_content_name_async("about", "bilibili"),
            get_content_description_async("about", "bilibili"),
            self.about_bilibili_Button,
        )
        self.addGroup(
            FIF.GITHUB,
            get_content_name_async("about", "github"),
            get_content_description_async("about", "github"),
            self.about_github_Button,
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
            self.about_author_label,
        )
        self.addGroup(
            FIF.GLOBE,
            get_content_name_async("about", "website"),
            get_content_description_async("about", "website"),
            self.about_website_Button,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_info_20_filled"),
            get_content_name_async("about", "version"),
            get_content_description_async("about", "version"),
            self.about_version_label,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_sync_20_filled"),
            get_content_name_async("about", "channel"),
            get_content_description_async("about", "channel"),
            self.channel_combo,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_arrow_sync_20_filled"),
            get_content_name_async("about", "check_update"),
            get_content_description_async("about", "check_update"),
            self.check_update_button,
        )

    def show_contributors(self):
        """显示贡献人员"""
        create_contributor_window()

    def open_donation_url(self):
        """打开捐赠链接"""
        QDesktopServices.openUrl(QUrl(DONATION_URL))

    def check_updates(self):
        """检查更新"""
        logger.debug("检查更新")
