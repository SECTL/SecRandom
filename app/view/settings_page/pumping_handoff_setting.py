from qfluentwidgets import *
from PyQt5.QtCore import *
from PyQt5.QtWidgets import *

from app.common.config import cfg, AUTHOR, VERSION, YEAR
from app.common.config import load_custom_font

from app.common.pumping_people_setting import pumping_people_SettinsCard
from app.common.list_settings import list_SettinsCard
from app.common.pumping_reward_setting import pumping_reward_SettinsCard
from app.common.reward_settings import reward_SettinsCard  

class pumping_handoff_setting(QFrame):
    def __init__(self, parent: QFrame = None):
        super().__init__(parent=parent)
        
        # 创建Pivot导航栏
        self.pivot = Pivot(self)
        self.stackedWidget = QStackedWidget(self)
        
        # 创建内容页面
        self.pumping_people_page = QWidget()
        self.pumping_people_list_page = QWidget()
        self.pumping_reward_page = QWidget()
        self.pumping_reward_list_page = QWidget()
        
        # 添加子页面
        self.addSubInterface(self.pumping_people_page, 'pumping_People_setting', '抽人设置')
        self.addSubInterface(self.pumping_people_list_page, 'pumping_People_list', '抽人名单')
        self.addSubInterface(self.pumping_reward_page, 'pumping_Reward_setting', '抽奖设置')
        self.addSubInterface(self.pumping_reward_list_page, 'pumping_Reward_list', '抽奖名单')

        # 抽人设置
        # 创建滚动区域
        pumping_people_scroll_area_personal = QScrollArea(self.pumping_people_page)
        pumping_people_scroll_area_personal.setWidgetResizable(True)
        # 设置样式表
        pumping_people_scroll_area_personal.setStyleSheet("""
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
            /* 垂直滚动条整体 */
            QScrollBar:vertical {
                background-color: #E5DDF8;   /* 背景透明 */
                width: 8px;                    /* 宽度 */
                margin: 0px;                   /* 外边距 */
            }
            /* 垂直滚动条的滑块 */
            QScrollBar::handle:vertical {
                background-color: rgba(0, 0, 0, 0.3);    /* 半透明滑块 */
                border-radius: 4px;                      /* 圆角 */
                min-height: 20px;                        /* 最小高度 */
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:vertical:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的上下按钮和顶部、底部区域 */
            QScrollBar::add-line:vertical,
            QScrollBar::sub-line:vertical,
            QScrollBar::up-arrow:vertical,
            QScrollBar::down-arrow:vertical {
                height: 0px;
            }
        
            /* 水平滚动条整体 */
            QScrollBar:horizontal {
                background-color: #E5DDF8;   /* 背景透明 */
                height: 8px;
                margin: 0px;
            }
            /* 水平滚动条的滑块 */
            QScrollBar::handle:horizontal {
                background-color: rgba(0, 0, 0, 0.3);
                border-radius: 4px;
                min-width: 20px;
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:horizontal:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的左右按钮和左侧、右侧区域 */
            QScrollBar::add-line:horizontal,
            QScrollBar::sub-line:horizontal,
            QScrollBar::left-arrow:horizontal,
            QScrollBar::right-arrow:horizontal {
                width: 0px;
            }
        """)
        # 启用鼠标滚轮
        QScroller.grabGesture(pumping_people_scroll_area_personal.viewport(), QScroller.LeftMouseButtonGesture)
        # 创建内部框架
        pumping_people_inner_frame_personal = QWidget(pumping_people_scroll_area_personal)
        pumping_people_inner_layout_personal = QVBoxLayout(pumping_people_inner_frame_personal)
        pumping_people_inner_layout_personal.setAlignment(Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignTop)
        pumping_people_scroll_area_personal.setWidget(pumping_people_inner_frame_personal)
        # 抽人设置卡片组
        pumping_people_card = pumping_people_SettinsCard()
        pumping_people_inner_layout_personal.addWidget(pumping_people_card)
        # 设置抽人设置页面布局
        pumping_people_layout = QVBoxLayout(self.pumping_people_page)
        pumping_people_layout.addWidget(pumping_people_scroll_area_personal)

        # 抽人名单
        # 创建滚动区域
        pumping_people_list_scroll_area_personal = QScrollArea(self.pumping_people_list_page)
        pumping_people_list_scroll_area_personal.setWidgetResizable(True)
        # 设置样式表
        pumping_people_list_scroll_area_personal.setStyleSheet("""
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
            /* 垂直滚动条整体 */
            QScrollBar:vertical {
                background-color: #E5DDF8;   /* 背景透明 */
                width: 8px;                    /* 宽度 */
                margin: 0px;                   /* 外边距 */
            }
            /* 垂直滚动条的滑块 */
            QScrollBar::handle:vertical {
                background-color: rgba(0, 0, 0, 0.3);    /* 半透明滑块 */
                border-radius: 4px;                      /* 圆角 */
                min-height: 20px;                        /* 最小高度 */
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:vertical:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的上下按钮和顶部、底部区域 */
            QScrollBar::add-line:vertical,
            QScrollBar::sub-line:vertical,
            QScrollBar::up-arrow:vertical,
            QScrollBar::down-arrow:vertical {
                height: 0px;
            }
        
            /* 水平滚动条整体 */
            QScrollBar:horizontal {
                background-color: #E5DDF8;   /* 背景透明 */
                height: 8px;
                margin: 0px;
            }
            /* 水平滚动条的滑块 */
            QScrollBar::handle:horizontal {
                background-color: rgba(0, 0, 0, 0.3);
                border-radius: 4px;
                min-width: 20px;
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:horizontal:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的左右按钮和左侧、右侧区域 */
            QScrollBar::add-line:horizontal,
            QScrollBar::sub-line:horizontal,
            QScrollBar::left-arrow:horizontal,
            QScrollBar::right-arrow:horizontal {
                width: 0px;
            }
        """)
        # 启用鼠标滚轮
        QScroller.grabGesture(pumping_people_list_scroll_area_personal.viewport(), QScroller.LeftMouseButtonGesture)
        # 创建内部框架
        pumping_people_list_inner_frame_personal = QWidget(pumping_people_list_scroll_area_personal)
        pumping_people_list_inner_layout_personal = QVBoxLayout(pumping_people_list_inner_frame_personal)
        pumping_people_list_inner_layout_personal.setAlignment(Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignTop)
        pumping_people_list_scroll_area_personal.setWidget(pumping_people_list_inner_frame_personal)
        # 抽人名单设置卡片组
        pumping_people_list_card = list_SettinsCard()
        pumping_people_list_inner_layout_personal.addWidget(pumping_people_list_card)
        # 设置抽人名单设置页面布局
        pumping_people_list_layout = QVBoxLayout(self.pumping_people_list_page)
        pumping_people_list_layout.addWidget(pumping_people_list_scroll_area_personal)

        # 抽奖设置
        # 创建滚动区域
        pumping_reward_scroll_area_personal = QScrollArea(self.pumping_reward_page)
        pumping_reward_scroll_area_personal.setWidgetResizable(True)
        # 设置样式表
        pumping_reward_scroll_area_personal.setStyleSheet("""
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
            /* 垂直滚动条整体 */
            QScrollBar:vertical {
                background-color: #E5DDF8;   /* 背景透明 */
                width: 8px;                    /* 宽度 */
                margin: 0px;                   /* 外边距 */
            }
            /* 垂直滚动条的滑块 */
            QScrollBar::handle:vertical {
                background-color: rgba(0, 0, 0, 0.3);    /* 半透明滑块 */
                border-radius: 4px;                      /* 圆角 */
                min-height: 20px;                        /* 最小高度 */
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:vertical:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的上下按钮和顶部、底部区域 */
            QScrollBar::add-line:vertical,
            QScrollBar::sub-line:vertical,
            QScrollBar::up-arrow:vertical,
            QScrollBar::down-arrow:vertical {
                height: 0px;
            }
        
            /* 水平滚动条整体 */
            QScrollBar:horizontal {
                background-color: #E5DDF8;   /* 背景透明 */
                height: 8px;
                margin: 0px;
            }
            /* 水平滚动条的滑块 */
            QScrollBar::handle:horizontal {
                background-color: rgba(0, 0, 0, 0.3);
                border-radius: 4px;
                min-width: 20px;
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:horizontal:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的左右按钮和左侧、右侧区域 */
            QScrollBar::add-line:horizontal,
            QScrollBar::sub-line:horizontal,
            QScrollBar::left-arrow:horizontal,
            QScrollBar::right-arrow:horizontal {
                width: 0px;
            }
        """)
        # 启用鼠标滚轮
        QScroller.grabGesture(pumping_reward_scroll_area_personal.viewport(), QScroller.LeftMouseButtonGesture)
        # 创建内部框架
        pumping_reward_inner_frame_personal = QWidget(pumping_reward_scroll_area_personal)
        pumping_reward_inner_layout_personal = QVBoxLayout(pumping_reward_inner_frame_personal)
        pumping_reward_inner_layout_personal.setAlignment(Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignTop)
        pumping_reward_scroll_area_personal.setWidget(pumping_reward_inner_frame_personal)
        # 抽奖设置卡片组
        personal_setting_card = pumping_reward_SettinsCard()
        pumping_reward_inner_layout_personal.addWidget(personal_setting_card)
        # 设置抽奖设置页面布局
        pumping_reward_layout = QVBoxLayout(self.pumping_reward_page)
        pumping_reward_layout.addWidget(pumping_reward_scroll_area_personal)

        # 抽奖奖项
        # 创建滚动区域
        pumping_reward_list_scroll_area_personal = QScrollArea(self.pumping_reward_list_page)
        pumping_reward_list_scroll_area_personal.setWidgetResizable(True)
        # 设置样式表
        pumping_reward_list_scroll_area_personal.setStyleSheet("""
            QScrollArea {
                border: none;
                background-color: transparent;
            }
            QScrollArea QWidget {
                border: none;
                background-color: transparent;
            }
            /* 垂直滚动条整体 */
            QScrollBar:vertical {
                background-color: #E5DDF8;   /* 背景透明 */
                width: 8px;                    /* 宽度 */
                margin: 0px;                   /* 外边距 */
            }
            /* 垂直滚动条的滑块 */
            QScrollBar::handle:vertical {
                background-color: rgba(0, 0, 0, 0.3);    /* 半透明滑块 */
                border-radius: 4px;                      /* 圆角 */
                min-height: 20px;                        /* 最小高度 */
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:vertical:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的上下按钮和顶部、底部区域 */
            QScrollBar::add-line:vertical,
            QScrollBar::sub-line:vertical,
            QScrollBar::up-arrow:vertical,
            QScrollBar::down-arrow:vertical {
                height: 0px;
            }
        
            /* 水平滚动条整体 */
            QScrollBar:horizontal {
                background-color: #E5DDF8;   /* 背景透明 */
                height: 8px;
                margin: 0px;
            }
            /* 水平滚动条的滑块 */
            QScrollBar::handle:horizontal {
                background-color: rgba(0, 0, 0, 0.3);
                border-radius: 4px;
                min-width: 20px;
            }
            /* 鼠标悬停在滑块上 */
            QScrollBar::handle:horizontal:hover {
                background-color: rgba(0, 0, 0, 0.5);
            }
            /* 滚动条的左右按钮和左侧、右侧区域 */
            QScrollBar::add-line:horizontal,
            QScrollBar::sub-line:horizontal,
            QScrollBar::left-arrow:horizontal,
            QScrollBar::right-arrow:horizontal {
                width: 0px;
            }
        """)
        # 启用鼠标滚轮
        QScroller.grabGesture(pumping_reward_list_scroll_area_personal.viewport(), QScroller.LeftMouseButtonGesture)
        # 创建内部框架
        pumping_reward_list_inner_frame_personal = QWidget(pumping_reward_list_scroll_area_personal)
        pumping_reward_list_inner_layout_personal = QVBoxLayout(pumping_reward_list_inner_frame_personal)
        pumping_reward_list_inner_layout_personal.setAlignment(Qt.AlignmentFlag.AlignCenter | Qt.AlignmentFlag.AlignTop)
        pumping_reward_list_scroll_area_personal.setWidget(pumping_reward_list_inner_frame_personal)
        # 抽奖设置卡片组
        personal_setting_card = reward_SettinsCard()
        pumping_reward_list_inner_layout_personal.addWidget(personal_setting_card)
        # 设置抽奖设置页面布局
        pumping_reward_list_layout = QVBoxLayout(self.pumping_reward_list_page)
        pumping_reward_list_layout.addWidget(pumping_reward_list_scroll_area_personal)
        
        # 设置主布局
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        main_layout.addWidget(self.pivot, 0, Qt.AlignHCenter)
        main_layout.addWidget(self.stackedWidget)
        
        self.stackedWidget.setCurrentWidget(self.pumping_people_page)
        self.pivot.setCurrentItem('pumping_People_setting')

        self.__connectSignalToSlot()
        
    def addSubInterface(self, widget: QWidget, objectName: str, text: str):
        widget.setObjectName(objectName)
        self.stackedWidget.addWidget(widget)
        
        # 添加导航项
        self.pivot.addItem(
            routeKey=objectName,
            text=text,
            onClick=lambda: self.stackedWidget.setCurrentWidget(widget)
        )

    def __connectSignalToSlot(self):
        cfg.themeChanged.connect(setTheme)
        self.stackedWidget.currentChanged.connect(self.onCurrentIndexChanged)
        
    def onCurrentIndexChanged(self, index):
        widget = self.stackedWidget.widget(index)
        self.pivot.setCurrentItem(widget.objectName())