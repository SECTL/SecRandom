# ==================================================
# 系统导入
# ==================================================

# 系统模块
import json
import os
import sys
import time
import subprocess
import warnings
import random
from urllib3.exceptions import InsecureRequestWarning
from pathlib import Path

# 第三方库
import loguru
from loguru import logger
from PyQt5.QtWidgets import *
from PyQt5.QtGui import *
from PyQt5.QtCore import *
from PyQt5.QtNetwork import *
from qfluentwidgets import *

# 内部模块
from app.common.config import YEAR, MONTH, AUTHOR, VERSION, APPLY_NAME, GITHUB_WEB, BILIBILI_WEB
from app.common.config import get_theme_icon, load_custom_font, check_for_updates, get_update_channel
from app.common.path_utils import path_manager
from app.common.path_utils import open_file, ensure_dir
from app.view.settings import settings_Window
from app.view.main_page.pumping_people import pumping_people
from app.view.main_page.pumping_reward import pumping_reward
from app.view.main_page.history_handoff_setting import history_handoff_setting
from app.view.main_page.vocabulary_learning import vocabulary_learning
from app.view.levitation import LevitationWindow
from app.view.settings_page.about_setting import about
from app.common.about import ContributorDialog, DonationDialog
from app.common.password_settings import check_and_delete_pending_usb

# ==================================================
# 初始化
# ==================================================

# 忽略不安全请求警告
warnings.filterwarnings('ignore', category=InsecureRequestWarning)

# 设置目录
settings_dir = path_manager.get_settings_path().parent
ensure_dir(settings_dir)

def show_update_notification(latest_version):
    """显示自定义更新通知窗口"""
    try:
        from app.common.update_notification import UpdateNotification
        import sys

        # 确保有应用实例
        if QApplication.instance() is None:
            app = QApplication(sys.argv)
        else:
            app = QApplication.instance()

        # 创建并显示通知窗口
        notification = UpdateNotification(latest_version)
        notification.show()
        # 防止通知窗口关闭时程序退出
        original_quit_setting = app.quitOnLastWindowClosed()
        app.setQuitOnLastWindowClosed(False)
        notification.destroyed.connect(lambda: app.setQuitOnLastWindowClosed(original_quit_setting))
        logger.info(f"自定义更新通知已显示，版本: {latest_version}")

    except ImportError as e:
        logger.error(f"导入自定义通知失败: {str(e)}")
    except Exception as e:
        logger.error(f"显示更新通知失败: {str(e)}", exc_info=True)

# ==================================================
# 配置管理类
# ==================================================
class ConfigurationManager:
    """配置管理类
    负责管理所有设置，自动缓存设置以减少IO操作"""

    def __init__(self):
        """初始化设置路径和默认值，并预加载设置"""
        self.app_dir = path_manager._app_root
        self.settings_path = path_manager.get_settings_path('Settings.json')  # 普通设置文件路径
        self.custom_settings_path = path_manager.get_settings_path('custom_settings.json')  # 自定义设置文件路径
        self.enc_settings_path = path_manager.get_enc_set_path()  # 加密设置文件路径
        self.default_settings = {
            'foundation': {
                'main_window_focus_mode': 0,
                'main_window_focus_time': 0,
                'window_width': 800,
                'window_height': 600,
                'pumping_floating_enabled': True,
                'pumping_floating_side': 0,
                'pumping_reward_side': 0,
                'main_window_mode': 0,
                'check_on_startup': True,
                'topmost_switch': False,
            }
        }  # 默认设置模板
        # 预加载设置缓存，减少启动时IO操作
        self._settings_cache = None
        self._custom_settings_cache = None
        self.load_settings()

    def load_settings(self):
        """读取配置文件
        尝试打开设置文件，如果失败就用默认设置
        使用缓存避免重复IO操作"""
        try:
            ensure_dir(self.settings_path.parent)
            with open_file(self.settings_path, 'r', encoding='utf-8') as f:
                self._settings_cache = json.load(f)
                return self._settings_cache
        except Exception as e:
            logger.error(f"加载设置文件失败: {e}")
            self._settings_cache = self.default_settings
            return self._settings_cache  # 返回默认设置作为后备方案

    def load_custom_settings(self):
        """读取自定义配置文件
        尝试打开自定义设置文件，如果失败就用空字典
        使用缓存避免重复IO操作"""
        try:
            ensure_dir(self.custom_settings_path.parent)
            with open_file(self.custom_settings_path, 'r', encoding='utf-8') as f:
                self._custom_settings_cache = json.load(f)
                return self._custom_settings_cache
        except Exception as e:
            logger.error(f"加载自定义设置文件失败: {e}")
            self._custom_settings_cache = {}
            return self._custom_settings_cache  # 返回空字典作为后备方案

    def get_floating_window_setting(self, key, default_value=None):
        """获取浮窗设置
        从自定义设置中找到对应的key值，如果找不到就用默认值"""
        custom_settings = self.load_custom_settings()
        floating_window_settings = custom_settings.get('floating_window', {})
        
        # 如果没有提供默认值，使用默认设置中的值
        if default_value is None and key in self.default_settings['foundation']:
            default_value = self.default_settings['foundation'][key]
            
        return floating_window_settings.get(key, default_value)

    def get_foundation_setting(self, key):
        """获取基础设置
        从设置中找到对应的key值，如果找不到就用默认值"""
        settings = self.load_settings()
        return settings.get('foundation', {}).get(key, self.default_settings['foundation'][key])

    def save_window_size(self, width, height):
        """保存窗口大小
        确保窗口不会太小（至少600x400），然后把新尺寸记下来"""
        if width < 600 or height < 400:  # 太小的窗口不行
            logger.warning("窗口尺寸太小，不保存")
            return

        try:
            settings = self.load_settings()
            if 'foundation' not in settings:
                settings['foundation'] = {}  # 如果没有基础设置，就创建一个
            settings['foundation']['window_width'] = width
            settings['foundation']['window_height'] = height

            ensure_dir(self.settings_path.parent)
            with open_file(self.settings_path, 'w', encoding='utf-8') as f:
                json.dump(settings, f, ensure_ascii=False, indent=4)
            logger.info(f"窗口大小已保存为 {width}x{height}")
        except Exception as e:
            logger.error(f"保存窗口大小失败: {e}")


# ==================================================
# 更新检查类
# ==================================================
class UpdateChecker(QObject):
    """更新检查类
    负责检查是否有新版本发布，发现新版本时发出信号"""

    update_available = pyqtSignal(str)  # 发现新版本时发射的信号

    def __init__(self, parent=None):
        """初始化更新检查系统"""
        super().__init__(parent)

    def check_for_updates(self):
        """启动更新检查任务
        派出Worker线程在后台执行检查，不会阻塞主线程"""
        self.worker = self.UpdateCheckWorker()
        self.worker.result_ready.connect(self.on_update_result)
        self.worker.start()
        logger.info("更新检查任务已启动")

    class UpdateCheckWorker(QThread):
        """更新检查工作线程
        在后台执行版本检查任务，不会阻塞UI主线程"""
        result_ready = pyqtSignal(bool, str)  # 发送检查结果的信号
        
        def __init__(self):
            super().__init__()
            self._is_running = True
            self._force_stop = False
        
        def stop(self):
            """停止线程执行"""
            self._is_running = False
            self._force_stop = True
        
        def run(self):
            """执行更新检查任务"""
            try:
                # 设置线程为可终止
                self.setTerminationEnabled(True)
                
                channel = get_update_channel()
                if self._is_running and not self._force_stop:
                    update_available, latest_version = check_for_updates(channel)
                    if self._is_running and not self._force_stop:
                        self.result_ready.emit(update_available, latest_version)
            except Exception as e:
                logger.error(f"更新检查过程中出错: {e}")
                if self._is_running and not self._force_stop:
                    self.result_ready.emit(False, "")

    def on_update_result(self, update_available, latest_version):
        """处理更新检查结果
        如果发现新版本，发射信号通知"""
        if update_available and latest_version:
            logger.info(f"发现新版本 {latest_version}！准备通知用户")
            self.update_available.emit(latest_version)  # 发射新版本信号
    
    def stop_checking(self):
        """停止更新检查任务
        确保worker线程安全停止，避免线程销毁错误"""
        try:
            if hasattr(self, 'worker') and self.worker:
                # 立即设置强制停止标志
                self.worker.stop()
                
                # 断开信号连接
                try:
                    self.worker.result_ready.disconnect(self.on_update_result)
                except:
                    pass
                
                # 停止worker线程
                if self.worker.isRunning():
                    # 首先尝试优雅退出
                    self.worker.quit()
                    if not self.worker.wait(2000):  # 等待最多2秒
                        logger.warning("线程优雅退出失败，准备强制终止")
                        # 如果优雅退出失败，强制终止
                        self.worker.terminate()
                        self.worker.wait(1000)  # 再等待1秒
                        
                        # 如果还在运行，记录警告
                        if self.worker.isRunning():
                            logger.error("线程仍然在运行，可能存在资源泄漏")
                
                # 清理引用
                self.worker = None
                logger.info("更新检查任务已安全停止")
        except Exception as e:
            logger.error(f"停止更新检查时出错: {e}")
    
    def __del__(self):
        """析构函数，确保资源正确释放"""
        try:
            self.stop_checking()
        except:
            pass


# ==================================================
# 托盘图标管理器类
# ==================================================
class TrayIconManager(QObject):
    """系统托盘图标管理器
    负责管理托盘图标和菜单，提供右键菜单功能"""

    def __init__(self, main_window):
        """初始化系统托盘图标，设置图标和提示文字"""
        super().__init__(main_window)
        self.main_window = main_window
        self.tray_icon = QSystemTrayIcon(main_window)
        self.tray_icon.setIcon(QIcon(str(path_manager.get_resource_path('icon', 'secrandom-icon-v2.png')))) 
        self.tray_icon.setToolTip('SecRandom')  # 鼠标放上去会显示的文字
        self._create_menu()  # 创建菜单
        self.tray_icon.activated.connect(self._on_tray_activated)  # 连接点击事件
        
        # 初始化菜单自动关闭定时器
        self.menu_timer = QTimer(main_window)
        self.menu_timer.setSingleShot(True)
        self.menu_timer.timeout.connect(self._on_menu_timeout)
        
        # 安装事件过滤器来检测点击外部
        self.tray_menu.installEventFilter(self)
        QApplication.instance().installEventFilter(self)
        
        logger.info("托盘图标管理器已初始化")

    def _get_tray_settings(self):
        """获取托盘设置
        从自定义设置中读取托盘菜单项的显示设置"""
        try:
            # 默认设置
            default_settings = {
                "show_main_window": True,
                "show_floating_window": True,
                "restart": True,
                "exit": True,
                "flash": False,
            }
            
            # 从自定义设置中读取托盘设置
            custom_settings = self.main_window.config_manager.load_custom_settings()
            tray_settings = custom_settings.get("tray", {})
            
            # 合并默认设置和用户设置
            for key, default_value in default_settings.items():
                tray_settings[key] = tray_settings.get(key, default_value)
                
            logger.debug(f"托盘设置已加载")
            return tray_settings
            
        except Exception as e:
            logger.error(f"加载托盘设置失败: {e}")
            return default_settings

    def _create_menu(self):
        """创建托盘菜单
        创建右键菜单，包含各种常用功能"""
        self.tray_menu = RoundMenu(parent=self.main_window)
        tray_settings = self._get_tray_settings()
        
        # 关于SecRandom（始终显示）
        self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_info_20_filled"), '关于SecRandom', triggered=self.main_window.show_about_tab))
        self.tray_menu.addSeparator()
        
        # 主界面控制
        if tray_settings.get("show_main_window", True):
            self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_power_20_filled"), '暂时显示/隐藏主界面', triggered=self.main_window.toggle_window))
        
        if tray_settings.get("show_floating_window", True):
            self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_window_ad_20_filled"), '暂时显示/隐藏浮窗', triggered=self.main_window.toggle_levitation_window))
        
        # 闪抽功能
        if tray_settings.get("flash", False):
            self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_lightning_20_filled"), '闪抽', triggered=self.main_window._show_direct_extraction_window_from_shortcut))
        
        # 打开设置界面（始终显示）
        self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_settings_20_filled"), '打开设置界面', triggered=self.main_window.show_setting_interface))
        self.tray_menu.addSeparator()
        
        # 系统操作
        if tray_settings.get("restart", True):
            self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_arrow_sync_20_filled"), '重启', triggered=self.main_window.restart_app))
        
        if tray_settings.get("exit", True):
            self.tray_menu.addAction(Action(get_theme_icon("ic_fluent_arrow_exit_20_filled"), '退出', triggered=self.main_window.close_window_secrandom))
            
        logger.info("托盘菜单已创建")

    def _on_tray_activated(self, reason):
        """处理托盘图标点击事件
        当用户点击托盘图标时，显示菜单"""
        if reason in (QSystemTrayIcon.Trigger, QSystemTrayIcon.Context):
            pos = QCursor.pos()  # 获取鼠标位置
            
            # 确保菜单不会超出屏幕边界
            screen = QApplication.primaryScreen().availableGeometry()
            menu_size = self.tray_menu.sizeHint()
            
            # 计算菜单显示位置，优先在鼠标位置附近显示
            # 如果鼠标位置右侧空间不足，显示在鼠标左侧
            if pos.x() + menu_size.width() > screen.right():
                adjusted_x = pos.x() - menu_size.width()
            else:
                adjusted_x = pos.x()
            
            # 如果鼠标位置下方空间不足，显示在鼠标上方
            if pos.y() + menu_size.height() > screen.bottom():
                adjusted_y = pos.y() - menu_size.height()
            else:
                adjusted_y = pos.y()
            
            # 确保菜单完全在屏幕内
            adjusted_x = max(screen.left(), min(adjusted_x, screen.right() - menu_size.width()))
            adjusted_y = max(screen.top(), min(adjusted_y, screen.bottom() - menu_size.height()))
            
            adjusted_pos = QPoint(adjusted_x, adjusted_y - 35)
            self.tray_menu.popup(adjusted_pos)  # 在调整后的位置显示菜单
            
            # 启动5秒自动关闭定时器
            self.menu_timer.start(5000)  # 5秒后自动关闭
            logger.debug("托盘菜单已显示")
    
    def _on_menu_timeout(self):
        """菜单超时自动关闭
        当用户5秒内没有操作菜单时，自动关闭菜单"""
        if self.tray_menu.isVisible():
            self.tray_menu.close()
            logger.debug("托盘菜单因超时自动关闭")
    
    def eventFilter(self, obj, event):
        """事件过滤器
        监听菜单相关事件，当用户点击菜单外部时自动关闭菜单"""
        if obj == self.tray_menu:
            # 如果是菜单被点击，停止定时器（用户正在操作）
            if event.type() == event.MouseButtonPress:
                self.menu_timer.stop()
            # 如果菜单失去焦点，关闭菜单
            elif event.type() == event.Hide:
                self.menu_timer.stop()
        
        # 检测点击外部区域关闭菜单
        if event.type() == event.MouseButtonPress and self.tray_menu.isVisible():
            # 获取点击位置
            click_pos = event.globalPos()
            menu_rect = self.tray_menu.geometry()
            
            # 如果点击位置不在菜单区域内，关闭菜单
            if not menu_rect.contains(click_pos):
                self.tray_menu.close()
                self.menu_timer.stop()
                logger.debug("托盘菜单因点击外部而关闭")
                return True
        
        return super().eventFilter(obj, event)


# ==================================================
# 主窗口类
# ==================================================
class Window(MSFluentWindow):
    """主窗口类
    程序的核心控制中心，所有重要操作都从这里发起"""

    # ==============================
    # 常量定义
    # ==============================
    
    # 定义清理信号
    cleanup_signal = pyqtSignal()
    FOCUS_TIMEOUT_MAP = [
        0, 0, 3000, 5000, 10000, 15000, 30000, 60000, 120000, 180000, 300000, 600000, 1800000,
        2700000, 3600000, 7200000, 10800000, 21600000, 43200000
    ]
    """焦点超时时间数组
    存储不同模式下窗口自动隐藏的时间阈值（毫秒）
    0=不隐藏，1=立即隐藏，其他值按索引对应不同时长"""

    FOCUS_TIMEOUT_TIME = [
        0, 1000, 2000, 3000, 5000, 10000, 15000, 30000, 60000, 300000, 600000, 900000, 1800000,
        3600000, 7200000, 10800000, 21600000, 43200000
    ]
    """焦点检查间隔数组
    存储焦点检查的时间间隔（毫秒）
    不同索引对应不同的检查频率，数值越小检查越频繁"""

    MINIMUM_WINDOW_SIZE = (600, 400)
    """窗口最小尺寸
    窗口最小不能小于这个尺寸，否则会影响界面显示"""

    # ==============================
    # 初始化与生命周期方法
    # ==============================
    def __init__(self):
        super().__init__()
        # 设置窗口对象名称，方便其他组件查找
        self.setObjectName("MainWindow")
        
        # 初始化管理器
        self.config_manager = ConfigurationManager()
        self.update_checker = UpdateChecker(self)
        self.update_checker.update_available.connect(show_update_notification)

        # 初始化IPC服务器
        self.server = QLocalServer(self)
        self.server.newConnection.connect(self.handle_new_connection)
        
        # 清理可能存在的旧服务器实例
        QLocalServer.removeServer("SecRandomIPC")
        
        # 尝试监听，如果失败则输出错误日志
        if not self.server.listen("SecRandomIPC"):
            logger.error(f"IPC服务器监听失败: {self.server.errorString()}")
        else:
            logger.info("IPC服务器监听成功: SecRandomIPC")

        # 初始化定时器
        self.focus_timer = QTimer(self)
        self.focus_timer.timeout.connect(self.check_focus_timeout)
        self.last_focus_time = QDateTime.currentDateTime()

        self.resize_timer = QTimer(self)
        self.resize_timer.setSingleShot(True)
        self.resize_timer.timeout.connect(lambda: self.config_manager.save_window_size(self.width(), self.height()))

        # USB检测定时器
        self.usb_detection_timer = QTimer(self)
        self.usb_detection_timer.timeout.connect(check_and_delete_pending_usb)
        self.usb_detection_timer.start(5000)  # 每5秒检查一次


        # 初始化焦点模式设置
        self.focus_mode = self.config_manager.get_foundation_setting('main_window_focus_mode')
        self.focus_time = self.config_manager.get_foundation_setting('main_window_focus_time')

        # 验证焦点时间有效性
        if self.focus_time >= len(self.FOCUS_TIMEOUT_TIME):
            self.focus_time = 1

        # 启动焦点计时器
        # 修复CPU占用过高问题，设置最低计时器间隔为200ms
        if self.focus_time == 0:
            pass
        else:
            # 确保计时器间隔不小于200ms
            interval = max(self.FOCUS_TIMEOUT_TIME[self.focus_time], 200)
            self.focus_timer.start(interval)

        # 设置窗口属性
        window_width = self.config_manager.get_foundation_setting('window_width')
        window_height = self.config_manager.get_foundation_setting('window_height')
        self.resize(window_width, window_height)
        self.setMinimumSize(self.MINIMUM_WINDOW_SIZE[0], self.MINIMUM_WINDOW_SIZE[1])
        self.setWindowTitle('SecRandom')
        self.setWindowIcon(QIcon(str(path_manager.get_resource_path('icon', 'secrandom-icon-v2.png'))))

        # 应用背景图片
        self.apply_background_image()

        # 检查更新
        check_startup = self.config_manager.get_foundation_setting('check_on_startup')
        if check_startup:
            self.check_updates_async()

        self._position_window()
        self.createSubInterface()
        self.tray_manager = TrayIconManager(self)
        self.tray_manager.tray_icon.show()
        self.start_cleanup()
        self.levitation_window = LevitationWindow()
        if self.config_manager.get_floating_window_setting('pumping_floating_enabled'):
            self.levitation_window.show()

        if self.config_manager.get_foundation_setting('topmost_switch'):
            self.setWindowFlags(self.windowFlags() | Qt.WindowStaysOnTopHint) # 置顶
        else:
            self.setWindowFlags(self.windowFlags() & ~Qt.WindowStaysOnTopHint) # 取消置顶

        self._apply_window_visibility_settings()

    def apply_background_image(self):
        """应用背景图片和颜色
        检查设置中的 enable_main_background 和 enable_main_background_color，
        如果开启则应用主界面背景图片或背景颜色"""
        try:
            # 读取自定义设置
            custom_settings_path = path_manager.get_settings_path('custom_settings.json')
            with open_file(custom_settings_path, 'r', encoding='utf-8') as f:
                custom_settings = json.load(f)
                
            # 检查是否启用了主界面背景图标
            personal_settings = custom_settings.get('personal', {})
            enable_main_background = personal_settings.get('enable_main_background', True)
            enable_main_background_color = personal_settings.get('enable_main_background_color', False)
            
            # 优先应用背景颜色（如果启用）
            if enable_main_background_color:
                main_background_color = personal_settings.get('main_background_color', '#FFFFFF')
                
                # 创建背景颜色标签（使用QLabel实现，与背景图片保持一致）
                self.background_label = QLabel(self)
                self.background_label.setGeometry(0, 0, self.width(), self.height())
                self.background_label.setStyleSheet(f"background-color: {main_background_color};")
                self.background_label.lower()  # 将背景标签置于底层
                
                # 确保背景标签随窗口大小变化
                self.background_label.setAttribute(Qt.WA_StyledBackground, True)
                
                # 设置窗口属性，确保背景可见
                self.setAttribute(Qt.WA_TranslucentBackground)
                self.setStyleSheet("background: transparent;")
                
                # 保存原始的resizeEvent方法
                self.original_resizeEvent = self.resizeEvent
                
                # 重写resizeEvent方法，调整背景大小
                self.resizeEvent = self._on_resize_event
                
                logger.info(f"已成功应用主界面背景颜色 {main_background_color}")
                
            # 如果背景颜色未启用，但背景图片启用了，则应用背景图片
            elif enable_main_background:
                # 获取主界面背景图片设置
                main_background_image = personal_settings.get('main_background_image', '')
                
                # 检查是否选择了背景图片
                if main_background_image and main_background_image != "无背景图":
                    # 获取背景图片文件夹路径
                    background_dir = path_manager.get_resource_path('images', 'background')
                    image_path = background_dir / main_background_image
                        
                    # 检查图片文件是否存在
                    if image_path.exists():
                        # 创建背景图片对象
                        background_pixmap = QPixmap(str(image_path))
                        
                        # 如果图片加载成功，应用背景
                        if not background_pixmap.isNull():
                            # 获取模糊度和亮度设置
                            blur_value = personal_settings.get('background_blur', 10)
                            brightness_value = personal_settings.get('background_brightness', 30)
                            
                            # 应用模糊效果
                            if blur_value > 0:
                                # 创建模糊效果
                                blur_effect = QGraphicsBlurEffect()
                                blur_effect.setBlurRadius(blur_value)
                                
                                # 创建临时场景和图形项来应用模糊效果
                                scene = QGraphicsScene()
                                item = QGraphicsPixmapItem(background_pixmap)
                                item.setGraphicsEffect(blur_effect)
                                scene.addItem(item)
                                
                                # 创建渲染图像
                                result_image = QImage(background_pixmap.size(), QImage.Format_ARGB32)
                                result_image.fill(Qt.transparent)
                                painter = QPainter(result_image)
                                scene.render(painter)
                                painter.end()
                                
                                # 更新背景图片
                                background_pixmap = QPixmap.fromImage(result_image)
                            
                            # 应用亮度效果
                            if brightness_value != 100:
                                # 创建图像副本
                                brightness_image = QImage(background_pixmap.size(), QImage.Format_ARGB32)
                                brightness_image.fill(Qt.transparent)
                                painter = QPainter(brightness_image)
                                
                                # 计算亮度调整因子
                                brightness_factor = brightness_value / 100.0
                                
                                # 应用亮度调整
                                painter.setOpacity(brightness_factor)
                                painter.drawPixmap(0, 0, background_pixmap)
                                painter.end()
                                
                                # 更新背景图片
                                background_pixmap = QPixmap.fromImage(brightness_image)
                            
                            # 创建背景标签并设置样式
                            self.background_label = QLabel(self)
                            self.background_label.setGeometry(0, 0, self.width(), self.height())
                            self.background_label.setPixmap(background_pixmap.scaled(
                                self.width(), self.height(), 
                                Qt.IgnoreAspectRatio, 
                                Qt.SmoothTransformation
                            ))
                            self.background_label.lower()  # 将背景标签置于底层
                            
                            # 保存原始图片，用于窗口大小调整时重新缩放
                            self.original_background_pixmap = background_pixmap
                            
                            # 确保背景标签随窗口大小变化
                            self.background_label.setAttribute(Qt.WA_StyledBackground, True)
                            
                            # 设置窗口属性，确保背景可见
                            self.setAttribute(Qt.WA_TranslucentBackground)
                            self.setStyleSheet("background: transparent;")
                            
                            # 保存原始的resizeEvent方法
                            self.original_resizeEvent = self.resizeEvent
                            
                            # 重写resizeEvent方法，调整背景大小
                            self.resizeEvent = self._on_resize_event
                            
                            logger.info(f"已成功应用主界面背景图片 {main_background_image}，模糊度: {blur_value}，亮度: {brightness_value}%")
                        else:
                            logger.error(f"主界面背景图片 {main_background_image} 加载失败")
                    else:
                        logger.warning(f"主界面背景图片 {main_background_image} 不存在")
                else:
                    logger.debug("未选择主界面背景图片")
            else:
                # 如果两者都未启用，则使用默认背景
                self.setStyleSheet("background: transparent;")
                
                # 清除可能存在的背景图片标签
                if hasattr(self, 'background_label') and self.background_label:
                    self.background_label.deleteLater()
                    delattr(self, 'background_label')
                
                # 恢复原始的resizeEvent方法
                if hasattr(self, 'original_resizeEvent'):
                    self.resizeEvent = self.original_resizeEvent
                    delattr(self, 'original_resizeEvent')
                
                logger.debug("主界面背景图片和颜色功能均未启用，使用默认背景")
                
        except FileNotFoundError:
            logger.warning("自定义设置文件不存在，使用默认设置")
        except Exception as e:
            logger.error(f"应用主界面背景图片或颜色时发生异常: {e}")
    
    def _on_resize_event(self, event):
        """窗口大小调整处理
        当窗口大小改变时，自动调整背景图片大小，确保背景始终填满整个窗口"""
        # 调用原始的resizeEvent，确保布局正确更新
        if hasattr(self, 'original_resizeEvent'):
            self.original_resizeEvent(event)
        else:
            super(Window, self).resizeEvent(event)
        
        # 强制更新布局
        self.updateGeometry()
        self.update()
        
        # 如果存在背景标签，调整其大小
        if hasattr(self, 'background_label') and self.background_label:
            self.background_label.setGeometry(0, 0, self.width(), self.height())
            # 使用保存的原始图片进行缩放，避免重复缩放导致的像素化
            if hasattr(self, 'original_background_pixmap') and self.original_background_pixmap:
                self.background_label.setPixmap(self.original_background_pixmap.scaled(
                    self.width(), self.height(), 
                    Qt.IgnoreAspectRatio, 
                    Qt.SmoothTransformation
                ))
        
        # 处理窗口最大化状态
        if self.isMaximized():
            self._handle_maximized_state()
    
    def _handle_maximized_state(self):
        """窗口最大化处理
        当窗口最大化时，确保所有控件正确适应新的窗口大小"""
        # 确保所有子控件适应最大化窗口
        for child in self.findChildren(QWidget):
            child.updateGeometry()
        
        # 强制重新布局
        QApplication.processEvents()
        
        # 延迟再次更新布局，确保所有控件都已适应
        QTimer.singleShot(100, self._delayed_layout_update)
    
    def _delayed_layout_update(self):
        """延迟布局更新
        在窗口最大化后延迟执行布局更新，确保所有控件都已正确适应"""
        # 再次强制更新布局
        self.updateGeometry()
        self.update()
        
        # 确保所有子控件再次更新
        for child in self.findChildren(QWidget):
            child.updateGeometry()
        
        # 最后一次强制重新布局
        QApplication.processEvents()

    def _position_window(self):
        """窗口定位
        根据屏幕尺寸和用户设置自动计算最佳位置"""
        import platform
        
        screen = QApplication.primaryScreen()
        desktop = screen.availableGeometry()
        w, h = desktop.width(), desktop.height()
        main_window_mode = self.config_manager.get_foundation_setting('main_window_mode')
        
        # 计算目标位置
        if main_window_mode == 0:
            # 模式0：屏幕正中央定位
            target_x = w // 2 - self.width() // 2
            target_y = h // 2 - self.height() // 2
        elif main_window_mode == 1:
            # 模式1：屏幕偏下定位（更符合视觉习惯）
            target_x = w // 2 - self.width() // 2
            target_y = h * 3 // 5 - self.height() // 2
        
        # Linux兼容性处理
        if platform.system().lower() == 'linux':
            self._position_window_linux(target_x, target_y)
        else:
            # Windows和其他系统使用标准方法
            self.move(target_x, target_y)
            
        logger.debug(f"窗口已定位到({self.x()}, {self.y()})位置")
    
    def _position_window_linux(self, target_x, target_y):
        """Linux窗口定位
        专门为Linux系统优化的窗口定位方法，处理各种窗口管理器兼容性问题"""
        try:
            # 确保窗口已经显示
            if not self.isVisible():
                self.show()
            
            # 先移动到目标位置
            self.move(target_x, target_y)
            
            # 如果move没有生效，尝试使用setGeometry
            current_x, current_y = self.x(), self.y()
            if abs(current_x - target_x) > 10 or abs(current_y - target_y) > 10:
                # move方法可能没有生效，使用setGeometry
                window_width = self.width()
                window_height = self.height()
                self.setGeometry(target_x, target_y, window_width, window_height)
            
            # 再次检查位置，如果仍然不正确，尝试延迟重定位
            current_x, current_y = self.x(), self.y()
            if abs(current_x - target_x) > 10 or abs(current_y - target_y) > 10:
                # 使用QTimer延迟重定位，给窗口管理器一些时间
                QTimer.singleShot(100, lambda: self._delayed_position_linux(target_x, target_y))
                
        except Exception as e:
            logger.error(f"Linux窗口定位失败: {e}")
            # 最后的备用方案：强制设置几何形状
            try:
                window_width = self.width() if self.width() > 0 else 800
                window_height = self.height() if self.height() > 0 else 600
                self.setGeometry(target_x, target_y, window_width, window_height)
            except Exception as e2:
                logger.error(f"Linux窗口定位备用方案也失败: {e2}")
    
    def _delayed_position_linux(self, target_x, target_y):
        """Linux延迟定位
        给窗口管理器一些时间处理后，再次尝试定位窗口"""
        try:
            # 再次尝试move
            self.move(target_x, target_y)
            
            # 检查是否成功
            current_x, current_y = self.x(), self.y()
            if abs(current_x - target_x) > 10 or abs(current_y - target_y) > 10:
                # 仍然失败，使用setGeometry
                window_width = self.width() if self.width() > 0 else 800
                window_height = self.height() if self.height() > 0 else 600
                self.setGeometry(target_x, target_y, window_width, window_height)
                
            logger.debug(f"Linux延迟定位完成，当前位置({self.x()}, {self.y()})")
            
        except Exception as e:
            logger.error(f"Linux延迟定位失败: {e}")
    
    def _apply_window_visibility_settings(self):
        """应用窗口显示设置
        根据用户保存的设置决定窗口是否自动显示"""
        try:
            settings = self.config_manager.load_settings()
            if settings.get('toggle_window') == 'show':
                self.show()
                logger.info("根据设置自动显示主窗口")
        except Exception as e:
            logger.error(f"加载窗口显示设置失败: {e}")

    def check_updates_async(self):
        """异步检查更新
        异步执行版本检查任务，不会阻塞主线程"""
        self.update_checker.check_for_updates()
        logger.info("更新检查任务已启动")

    def createSubInterface(self):
        """创建子界面
        搭建子界面导航系统"""
        # 创建设置界面
        self.settingInterface = settings_Window(self)
        self.settingInterface.setObjectName("settingInterface")
        logger.debug("设置界面已创建")

        # 创建关于界面
        self.about_settingInterface = about(self)
        self.about_settingInterface.setObjectName("about_settingInterface")
        logger.debug("关于界面已创建")
        
        # 通用界面创建函数
        def _create_interface(attr_name, class_type, sidebar_key, default_value, default_on_error=None, object_name=None, create_on_value_not=2, log_name=None, create_on_error=True):
            settings_path = path_manager.get_settings_path('custom_settings.json')
            with open_file(settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                sidebar_settings = settings.get('sidebar', {})
                value = sidebar_settings.get(sidebar_key, default_value)
                if value != create_on_value_not:
                    setattr(self, attr_name, class_type(self))
                    if object_name:
                        getattr(self, attr_name).setObjectName(object_name)
                    logger.debug(f"{log_name or attr_name}界面已创建")
                else:
                    logger.debug(f"'{log_name or attr_name}'界面已设置为不创建")
                    setattr(self, attr_name, None)

        # 创建各个子界面
        # 抽象成函数，提升可读性喵~
        _create_interface(
            attr_name='history_handoff_settingInterface',
            class_type=history_handoff_setting,
            sidebar_key='main_window_history_switch',
            default_value=1,
            object_name='history_handoff_settingInterface',
            log_name='历史交接设置',
            create_on_error=True
        )
        _create_interface(
            attr_name='vocabulary_learningInterface',
            class_type=vocabulary_learning,
            sidebar_key='main_window_side_switch',
            default_value=2,
            object_name='vocabulary_learningInterface',
            log_name='背单词',
            create_on_error=False,
            default_on_error=None
        )
        _create_interface(
            attr_name='pumping_peopleInterface',
            class_type=pumping_people,
            sidebar_key='pumping_floating_side',
            default_value=0,
            object_name='pumping_peopleInterface',
            log_name='抽人',
            create_on_error=True
        )
        _create_interface(
            attr_name='pumping_rewardInterface',
            class_type=pumping_reward,
            sidebar_key='pumping_reward_side',
            default_value=0,
            object_name='pumping_rewardInterface',
            log_name='抽奖',
            create_on_error=True
        )

        # 初始化导航系统
        self.initNavigation()
        logger.info("所有子界面和导航系统已初始化完成")

    def initNavigation(self):
        """初始化导航系统
        根据用户设置构建个性化菜单导航"""
        try:
            settings_path = path_manager.get_settings_path('custom_settings.json')
            with open_file(settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                sidebar_settings = settings.get('sidebar', {})
                logger.debug("已读取导航配置，准备构建个性化菜单")

                # 根据设置决定"点名"界面位置
                pumping_floating_side = sidebar_settings.get('pumping_floating_side', 0)
                if pumping_floating_side == 1:
                    if self.pumping_peopleInterface is not None:
                        self.addSubInterface(self.pumping_peopleInterface, get_theme_icon("ic_fluent_people_community_20_filled"), '点名', position=NavigationItemPosition.BOTTOM)
                        logger.debug("'点名'界面已放置在底部导航栏")
                    else:
                        logger.debug("'点名'界面未创建，无法添加到导航栏")
                elif pumping_floating_side == 2:
                    logger.debug("'点名'界面已设置为不显示")
                else:
                    if self.pumping_peopleInterface is not None:
                        self.addSubInterface(self.pumping_peopleInterface, get_theme_icon("ic_fluent_people_community_20_filled"), '点名', position=NavigationItemPosition.TOP)
                        logger.debug("'点名'界面已放置在顶部导航栏")
                    else:
                        logger.debug("'点名'界面未创建，无法添加到导航栏")

                # 根据设置决定"抽奖"界面位置
                pumping_reward_side = sidebar_settings.get('pumping_reward_side', 0)
                if pumping_reward_side == 1:
                    if self.pumping_rewardInterface is not None:
                        self.addSubInterface(self.pumping_rewardInterface, get_theme_icon("ic_fluent_reward_20_filled"), '抽奖', position=NavigationItemPosition.BOTTOM)
                        logger.debug("'抽奖'界面已放置在底部导航栏")
                    else:
                        logger.debug("'抽奖'界面未创建，无法添加到导航栏")
                elif pumping_reward_side == 2:
                    logger.debug("'抽奖'界面已设置为不显示")
                else:
                    if self.pumping_rewardInterface is not None:
                        self.addSubInterface(self.pumping_rewardInterface, get_theme_icon("ic_fluent_reward_20_filled"), '抽奖', position=NavigationItemPosition.TOP)
                        logger.debug("'抽奖'界面已放置在顶部导航栏")
                    else:
                        logger.debug("'抽奖'界面未创建，无法添加到导航栏")

        except FileNotFoundError as e:
            logger.error(f"配置文件找不到: {e}, 使用默认顶部导航布局")
            if self.pumping_peopleInterface is not None:
                self.addSubInterface(self.pumping_peopleInterface, get_theme_icon("ic_fluent_people_community_20_filled"), '点名', position=NavigationItemPosition.TOP)
            if self.pumping_rewardInterface is not None:
                self.addSubInterface(self.pumping_rewardInterface, get_theme_icon("ic_fluent_reward_20_filled"), '抽奖', position=NavigationItemPosition.TOP)

        try:
            # 添加单词PK界面导航项
            vocabulary_side = sidebar_settings.get('main_window_side_switch', 2)
            if vocabulary_side == 1:
                if self.vocabulary_learningInterface is not None:
                    self.addSubInterface(self.vocabulary_learningInterface, get_theme_icon("ic_fluent_text_whole_word_20_filled"), '单词PK', position=NavigationItemPosition.BOTTOM)
                    logger.debug("'单词PK'界面已放置在底部导航栏")
                else:
                    logger.debug("'单词PK'界面未创建，无法添加到导航栏")
            elif vocabulary_side == 2:
                logger.debug("'单词PK'界面已设置为不显示")
            else:
                if self.vocabulary_learningInterface is not None:
                    self.addSubInterface(self.vocabulary_learningInterface, get_theme_icon("ic_fluent_text_whole_word_20_filled"), '单词PK', position=NavigationItemPosition.TOP)
                    logger.debug("'单词PK'界面已放置在顶部导航栏")
                else:
                    logger.debug("'单词PK'界面未创建，无法添加到导航栏")
        except Exception as e:
            if self.vocabulary_learningInterface is not None:
                self.addSubInterface(self.vocabulary_learningInterface, get_theme_icon("ic_fluent_text_whole_word_20_filled"), '单词PK', position=NavigationItemPosition.BOTTOM)
            logger.error(f"加载单词PK界面导航项失败: {e}")

        # 添加历史记录导航项
        try:
            history_side = sidebar_settings.get('main_window_history_switch', 1)
            if history_side == 1:
                if self.history_handoff_settingInterface is not None:
                    # 为历史记录导航项添加点击事件处理器
                    history_item = self.addSubInterface(self.history_handoff_settingInterface, get_theme_icon("ic_fluent_chat_history_20_filled"), '历史记录', position=NavigationItemPosition.BOTTOM)
                    # 点击历史记录导航项时切换到历史记录界面
                    history_item.clicked.connect(lambda: self.switchTo(self.history_handoff_settingInterface))
                    logger.debug("'历史记录'导航项已放置在底部导航栏")
                else:
                    logger.debug("'历史记录'界面未创建，无法添加到导航栏")
            elif history_side == 2:
                logger.debug("'历史记录'导航项已设置为不显示")
            else:
                if self.history_handoff_settingInterface is not None:
                    # 为历史记录导航项添加点击事件处理器
                    history_item = self.addSubInterface(self.history_handoff_settingInterface, get_theme_icon("ic_fluent_chat_history_20_filled"), '历史记录', position=NavigationItemPosition.TOP)
                    # 点击历史记录导航项时切换到历史记录界面
                    history_item.clicked.connect(lambda: self.switchTo(self.history_handoff_settingInterface))
                    logger.debug("'历史记录'导航项已放置在顶部导航栏")
                else:
                    logger.debug("'历史记录'界面未创建，无法添加到导航栏")
        except Exception as e:
            logger.error(f"加载历史记录导航项失败: {e}")
            # 默认添加到底部导航栏
            if self.history_handoff_settingInterface is not None:
                history_item = self.addSubInterface(self.history_handoff_settingInterface, get_theme_icon("ic_fluent_chat_history_20_filled"), '历史记录', position=NavigationItemPosition.BOTTOM)
                # 点击历史记录导航项时切换到历史记录界面
                history_item.clicked.connect(lambda: self.switchTo(self.history_handoff_settingInterface))

    # 移除关于按钮，不添加到主界面导航栏

        try:
            settings_side = sidebar_settings.get('show_settings_icon', 1)
            if settings_side == 1:
                # 创建一个空的设置界面占位符，用于导航栏
                self.settings_placeholder = QWidget()
                self.settings_placeholder.setObjectName("settings_placeholder")
                settings_item = self.addSubInterface(self.settings_placeholder, get_theme_icon("ic_fluent_settings_20_filled"), '设置', position=NavigationItemPosition.BOTTOM)
                # 为导航项添加点击事件处理器，调用show_setting_interface方法
                settings_item.clicked.connect(self.show_setting_interface)
                settings_item.clicked.connect(lambda: self.switchTo(self.pumping_peopleInterface))
                logger.debug("'设置'图标已放置在底部导航栏")
            elif settings_side == 2:
                logger.debug("'设置'图标已设置为不显示")
            else:
                # 创建一个空的设置界面占位符，用于导航栏
                self.settings_placeholder = QWidget()
                self.settings_placeholder.setObjectName("settings_placeholder")
                settings_item = self.addSubInterface(self.settings_placeholder, get_theme_icon("ic_fluent_settings_20_filled"), '设置', position=NavigationItemPosition.TOP)
                # 为导航项添加点击事件处理器，调用show_setting_interface方法
                settings_item.clicked.connect(self.show_setting_interface)
                settings_item.clicked.connect(lambda: self.switchTo(self.pumping_peopleInterface))
                logger.debug("'设置'图标已放置在顶部导航栏")
        except Exception as e:
            logger.error(f"加载设置图标失败: {e}")
            if sidebar_settings.get('show_settings_icon', True):
                # 创建一个空的设置界面占位符，用于导航栏
                self.settings_placeholder = QWidget()
                self.settings_placeholder.setObjectName("settings_placeholder")
                settings_item = self.addSubInterface(self.settings_placeholder, get_theme_icon("ic_fluent_settings_20_filled"), '设置', position=NavigationItemPosition.BOTTOM)
                # 为导航项添加点击事件处理器，调用show_setting_interface方法
                settings_item.clicked.connect(self.show_setting_interface)
                settings_item.clicked.connect(lambda: self.switchTo(self.pumping_peopleInterface))
        
        logger.info("所有导航项已布置完成，导航系统可以正常使用")

    def closeEvent(self, event):
        """窗口关闭事件处理
        拦截窗口关闭事件，隐藏窗口并保存窗口大小"""
        self.hide()
        event.ignore()
        self.save_window_size()
        logger.info("窗口关闭事件已拦截，程序已转入后台运行")

    def resizeEvent(self, event):
        """窗口大小变化事件处理
        检测窗口大小变化并启动尺寸记录倒计时"""
        self.resize_timer.start(500)
        super().resizeEvent(event)

    def save_window_size(self):
        """保存窗口大小
        记录当前窗口尺寸，下次启动时自动恢复"""
        if not self.isMaximized():
            self.config_manager.save_window_size(self.width(), self.height())
            logger.info(f"已保存窗口尺寸为{self.width()}x{self.height()}像素")

    def update_focus_mode(self, mode):
        """更新焦点模式
        切换到不同的焦点模式，触发不同的自动隐藏行为"""
        self.focus_mode = mode
        self.last_focus_time = QDateTime.currentDateTime()

        if mode < len(self.FOCUS_TIMEOUT_MAP):
            self.focus_timeout = self.FOCUS_TIMEOUT_MAP[mode]

    def update_focus_time(self, time):
        """更新焦点检查时间间隔
        调整窗口焦点检查的时间间隔"""
        self.focus_time = time
        self.last_focus_time = QDateTime.currentDateTime()

        if time < len(self.FOCUS_TIMEOUT_TIME):
            self.focus_timeout = self.FOCUS_TIMEOUT_TIME[time]
            self.focus_timer.start(self.focus_timeout)
        else:
            self.focus_timer.start(0)

    def check_focus_timeout(self):
        """检查焦点超时
        监控窗口焦点状态，超时无操作则自动隐藏窗口"""
        if self.focus_mode == 0:  # 不关闭模式
            return

        if not self.isActiveWindow() and not self.isMinimized():
            elapsed = self.last_focus_time.msecsTo(QDateTime.currentDateTime())
            timeout = self.FOCUS_TIMEOUT_MAP[self.focus_mode]

            if self.focus_mode == 1:  # 直接关闭模式
                self.hide()
            elif elapsed >= timeout:
                self.hide()
        else:
            self.last_focus_time = QDateTime.currentDateTime()

    def stop_focus_timer(self):
        """停止焦点计时器
        禁用窗口自动隐藏功能"""
        self.focus_timer.stop()

    def showEvent(self, event):
        """窗口显示事件处理
        窗口显示时重置焦点时间，开始监控用户活动"""
        super().showEvent(event)
        self.last_focus_time = QDateTime.currentDateTime()

    def focusInEvent(self, event):
        """窗口获得焦点事件处理
        窗口获得焦点时重置闲置计时器"""
        super().focusInEvent(event)
        self.last_focus_time = QDateTime.currentDateTime()

    def _is_non_class_time(self):
        """检测当前时间是否在非上课时间段
        当'课间禁用'开关启用时，用于判断是否需要安全验证"""
        try:
            # 读取程序功能设置
            settings_path = path_manager.get_settings_path('custom_settings.json')
            if not path_manager.file_exists(settings_path):
                return False
                
            with open_file(settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                
            # 检查课间禁用开关是否启用
            program_functionality = settings.get("program_functionality", {})
            instant_draw_disable = program_functionality.get("instant_draw_disable", False)
            
            if not instant_draw_disable:
                return False
                
            # 读取上课时间段设置
            time_settings_path = path_manager.get_settings_path('time_settings.json')
            if not path_manager.file_exists(time_settings_path):
                return False
                
            with open_file(time_settings_path, 'r', encoding='utf-8') as f:
                time_settings = json.load(f)
                
            # 获取非上课时间段
            non_class_times = time_settings.get('non_class_times', {})
            if not non_class_times:
                return False
                
            # 获取当前时间
            current_time = QDateTime.currentDateTime()
            current_hour = current_time.time().hour()
            current_minute = current_time.time().minute()
            current_second = current_time.time().second()
            
            # 将当前时间转换为总秒数
            current_total_seconds = current_hour * 3600 + current_minute * 60 + current_second
            
            # 检查当前时间是否在任何非上课时间段内
            for time_range in non_class_times.values():
                try:
                    start_end = time_range.split('-')
                    if len(start_end) != 2:
                        continue
                        
                    start_time_str, end_time_str = start_end
                    
                    # 解析开始时间
                    start_parts = list(map(int, start_time_str.split(':')))
                    start_total_seconds = start_parts[0] * 3600 + start_parts[1] * 60 + (start_parts[2] if len(start_parts) > 2 else 0)
                    
                    # 解析结束时间
                    end_parts = list(map(int, end_time_str.split(':')))
                    end_total_seconds = end_parts[0] * 3600 + end_parts[1] * 60 + (end_parts[2] if len(end_parts) > 2 else 0)
                    
                    # 检查当前时间是否在该非上课时间段内
                    if start_total_seconds <= current_total_seconds < end_total_seconds:
                        return True
                        
                except Exception as e:
                    logger.error(f"解析非上课时间段失败: {e}")
                    continue
                    
            return False
            
        except Exception as e:
            logger.error(f"检测非上课时间失败: {e}")
            return False

    def show_about_tab(self):
        """显示关于标签页
        导航到关于页面，可查看软件版本和作者信息"""
        # 检查是否在非上课时间且需要安全验证
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消打开关于界面操作")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
                
        if self.isMinimized():
            self.showNormal()
        else:
            self.show()
            self.activateWindow()
            self.raise_()
        self.switchTo(self.about_settingInterface)

    def start_cleanup(self):
        """启动清理
        软件启动时清理上次遗留的临时抽取记录文件"""
        try:
            settings_path = path_manager.get_settings_path('Settings.json')
            with open_file(settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                clear_mode = settings['pumping_people']['clear_mode']
                instant_clear_mode = settings['instant_draw']['clear_mode']
                instant_clear = settings['instant_draw']['instant_clear']
                logger.debug("准备执行对应清理方案")

        except Exception as e:
            clear_mode = 1
            instant_clear_mode = 1
            instant_clear = False
            logger.error(f"加载抽选模式设置失败: {e}")

        import glob
        temp_dir = path_manager.get_temp_path('')
        ensure_dir(temp_dir)

        if (clear_mode != 1 or instant_clear_mode != 1) and not instant_clear:
            if path_manager.file_exists(temp_dir):
                for file in glob.glob(f"{temp_dir}/*.json"):
                    try:
                        os.remove(file)
                        logger.info(f"已删除临时抽取记录文件: {file}")
                    except Exception as e:
                        logger.error(f"删除临时文件出错: {e}")
        elif (clear_mode != 1 or instant_clear_mode != 1) and instant_clear:
            if path_manager.file_exists(temp_dir):
                for file in glob.glob(f"{temp_dir}/*_instant.json"):
                    try:
                        os.remove(file)
                        logger.info(f"已删除临时抽取记录文件: {file}")
                    except Exception as e:
                        logger.error(f"删除临时文件出错: {e}")

    def toggle_window(self):
        """切换窗口显示状态
        在显示和隐藏状态之间切换窗口，切换时自动激活点名界面"""
        # 检查是否在非上课时间且需要安全验证
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消打开窗口切换操作")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
                
        # 执行窗口切换逻辑
        if self.config_manager.get_foundation_setting('topmost_switch'):
            self.setWindowFlags(self.windowFlags() | Qt.WindowStaysOnTopHint)
        else:
            self.setWindowFlags(self.windowFlags() & ~Qt.WindowStaysOnTopHint)

        if self.isVisible():
            self.hide()
            logger.info("主窗口已隐藏")
            if self.isMinimized():
                self.showNormal()
                self.activateWindow()
                self.raise_()
        else:
            if self.isMinimized():
                self.showNormal()
                self.activateWindow()
                self.raise_()
            else:
                self.show()
                self.activateWindow()
                self.raise_()
            logger.info("主窗口已显示")
        self.switchTo(self.pumping_peopleInterface)

    def calculate_menu_position(self, menu):
        """计算托盘菜单位置
        计算托盘菜单的最佳显示位置，确保菜单不会超出屏幕边界"""
        screen = QApplication.primaryScreen().availableGeometry()
        menu_size = menu.sizeHint()

        cursor_pos = QCursor.pos()

        x = cursor_pos.x() + 20
        y = cursor_pos.y() - menu_size.height()

        if x + menu_size.width() > screen.right():
            x = screen.right() - menu_size.width()
        if y < screen.top():
            y = screen.top()

        return QPoint(x, y)

    def close_window_secrandom(self):
        """关闭窗口
        执行安全验证后关闭程序，释放所有资源"""
        try:
            enc_settings_path = path_manager.get_enc_set_path()
            with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                logger.debug("正在读取安全设置，准备执行退出验证")

                if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                    if settings.get('hashed_set', {}).get('exit_verification_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消退出程序操作")
                            return
        except Exception as e:
            logger.error(f"密码验证系统出错: {e}")
            return

        logger.info("安全验证通过，开始执行完全退出程序流程")
        self.hide()
        if hasattr(self, 'levitation_window'):
            self.levitation_window.hide()
            logger.debug("悬浮窗已隐藏")
            
        if hasattr(self, 'focus_timer'):
            self.stop_focus_timer()
            logger.debug("焦点计时器已停止")

        if hasattr(self, 'usb_detection_timer'):
            self.usb_detection_timer.stop()
            logger.debug("USB绑定已关闭")

        # 停止resize_timer以优化CPU占用
        if hasattr(self, 'resize_timer') and self.resize_timer.isActive():
            self.resize_timer.stop()
            logger.debug("resize_timer已停止")

        # 停止托盘菜单定时器
        if hasattr(self, 'tray_manager') and hasattr(self.tray_manager, 'menu_timer'):
            if self.tray_manager.menu_timer.isActive():
                self.tray_manager.menu_timer.stop()
                logger.debug("托盘菜单定时器已停止")

        # 停止USB监控线程
        if hasattr(self, 'settingInterface') and self.settingInterface:
            if hasattr(self.settingInterface, 'usb_monitor_thread') and self.settingInterface.usb_monitor_thread:
                # 先断开信号连接，避免在线程停止过程中触发信号
                self.settingInterface.usb_monitor_thread.usb_removed.disconnect()
                # 停止线程并等待完全停止
                self.settingInterface.usb_monitor_thread.stop()
                # 等待一小段时间确保线程完全停止
                if self.settingInterface.usb_monitor_thread.isRunning():
                    self.settingInterface.usb_monitor_thread.wait(500)  # 等待最多500ms
                self.settingInterface.usb_monitor_thread = None
                logger.debug("USB监控线程已停止")

        if hasattr(self, 'server'):
            self.server.close()
            logger.debug("IPC服务器已关闭")

        # 停止更新检查
        if hasattr(self, 'update_checker') and self.update_checker:
            self.update_checker.stop_checking()
            logger.debug("更新检查已停止")
            
        # 关闭共享内存
        if hasattr(self, 'shared_memory'):
            try:
                self.shared_memory.detach()
                if self.shared_memory.isAttached():
                    self.shared_memory.detach()
                logger.debug("共享内存已完全释放")
            except Exception as e:
                logger.error(f"共享内存释放出错: {e}")

        logger.debug("所有资源已成功释放，程序即将退出")
        
        # 正确关闭日志系统
        try:
            # 移除所有日志处理器
            loguru.logger.remove()
            logger.info("日志系统已安全关闭")
        except Exception as e:
            logger.error(f"日志系统关闭出错: {e}")

        # 确保完全退出应用程序
        QApplication.quit()
        sys.exit(0)

    def restart_app(self):
        """重启应用程序
        执行安全验证后重启程序，清理所有资源"""
        try:
            enc_settings_path = path_manager.get_enc_set_path()
            with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                    if settings.get('hashed_set', {}).get('restart_verification_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消重启操作")
                            return
        except Exception as e:
            logger.error(f"密码验证过程出错: {e}")
            return

        logger.info("安全验证通过，开始执行完全重启程序流程")
        
        # 隐藏所有窗口
        self.hide()
        if hasattr(self, 'levitation_window'):
            self.levitation_window.hide()
            logger.debug("悬浮窗已隐藏")
        
        # 彻底清理设置界面，防止重启后嵌套问题
        if hasattr(self, 'settingInterface') and self.settingInterface:
            try:
                # 先断开所有信号连接
                if hasattr(self.settingInterface, 'usb_monitor_thread') and self.settingInterface.usb_monitor_thread:
                    self.settingInterface.usb_monitor_thread.usb_removed.disconnect()
                
                # 关闭设置界面
                self.settingInterface.close()
                self.settingInterface = None
                logger.debug("设置界面已完全清理")
            except Exception as e:
                logger.error(f"清理设置界面时出错: {e}")
        
        # 停止所有计时器
        if hasattr(self, 'focus_timer'):
            self.stop_focus_timer()
            logger.debug("焦点计时器已停止")
    
        # 停止USB检测计时器
        if hasattr(self, 'usb_detection_timer'):
            self.usb_detection_timer.stop()
            logger.debug("USB绑定已关闭")

        # 停止resize_timer以优化CPU占用
        if hasattr(self, 'resize_timer') and self.resize_timer.isActive():
            self.resize_timer.stop()
            logger.debug("resize_timer已停止")

        # 停止托盘菜单定时器
        if hasattr(self, 'tray_manager') and hasattr(self.tray_manager, 'menu_timer'):
            if self.tray_manager.menu_timer.isActive():
                self.tray_manager.menu_timer.stop()
                logger.debug("托盘菜单定时器已停止")
                
        # 停止USB监控线程
        if hasattr(self, 'settingInterface') and self.settingInterface:
            if hasattr(self.settingInterface, 'usb_monitor_thread') and self.settingInterface.usb_monitor_thread:
                # 先断开信号连接，避免在线程停止过程中触发信号
                self.settingInterface.usb_monitor_thread.usb_removed.disconnect()
                # 停止线程并等待完全停止
                self.settingInterface.usb_monitor_thread.stop()
                # 等待一小段时间确保线程完全停止
                if self.settingInterface.usb_monitor_thread.isRunning():
                    self.settingInterface.usb_monitor_thread.wait(500)  # 等待最多500ms
                self.settingInterface.usb_monitor_thread = None
                logger.debug("USB监控线程已停止")
        
        # 关闭IPC服务器
        if hasattr(self, 'server'):
            self.server.close()
            logger.debug("IPC服务器已关闭")
        
        # 停止更新检查
        if hasattr(self, 'update_checker') and self.update_checker:
            self.update_checker.stop_checking()
            logger.debug("更新检查已停止")

        # 关闭共享内存
        if hasattr(self, 'shared_memory'):
            try:
                self.shared_memory.detach()
                if self.shared_memory.isAttached():
                    self.shared_memory.detach()
                logger.info("共享内存已完全释放")
            except Exception as e:
                logger.error(f"共享内存释放出错: {e}")
        
        # 重置密码验证状态，防止重启后打不开设置
        try:
            enc_settings_path = path_manager.get_enc_set_path()
            with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
            if 'hashed_set' in settings and 'verification_start' in settings['hashed_set']:
                settings['hashed_set']['verification_start'] = False
                with open_file(enc_settings_path, 'w', encoding='utf-8') as f:
                    json.dump(settings, f, ensure_ascii=False, indent=4)
                logger.debug("密码验证状态已重置")
        except Exception as e:
            logger.error(f"重置密码验证状态时出错: {e}")
        
        # 给系统更多时间清理资源
        time.sleep(1.0)
        
        # 启动新进程，过滤掉可能导致问题的参数
        try:
            # 获取当前工作目录
            working_dir = os.getcwd()
            
            # 过滤命令行参数，移除可能导致问题的参数
            filtered_args = [arg for arg in sys.argv if not arg.startswith('--')]
            
            # 使用更安全的启动方式
            startup_info = subprocess.STARTUPINFO()
            startup_info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            
            # 启动新进程
            subprocess.Popen(
                [sys.executable] + filtered_args,
                cwd=working_dir,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS,
                startupinfo=startup_info
            )
            logger.info("新进程已成功启动")
        except Exception as e:
            logger.error(f"启动新进程失败: {e}")
            return

        # 正确关闭日志系统
        try:
            # 移除所有日志处理器
            loguru.logger.remove()
            logger.info("日志系统已安全关闭")
        except Exception as e:
            logger.error(f"日志系统关闭出错: {e}")
        
        # 完全退出当前应用程序
        QApplication.quit()
        sys.exit(0)

    def show_setting_interface(self):
        """显示设置界面
        打开程序设置界面，需要密码验证"""
        try:
            enc_settings_path = path_manager.get_enc_set_path()
            with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                if settings.get('hashed_set', {}).get('start_password_enabled', False):
                    from app.common.password_dialog import PasswordDialog
                    dialog = PasswordDialog(self)
                    if dialog.exec_() != QDialog.Accepted:
                        logger.warning("用户取消打开设置界面操作")
                        return
        except Exception as e:
            logger.error(f"密码验证失败: {e}")

        try:
            enc_settings_path = path_manager.get_enc_set_path()
            with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
            settings['hashed_set']['verification_start'] = True
            with open_file(enc_settings_path, 'w', encoding='utf-8') as f:
                json.dump(settings, f, ensure_ascii=False, indent=4)
        except Exception as e:
            logger.error(f"写入verification_start失败: {e}")

        if not hasattr(self, 'settingInterface') or not self.settingInterface:
            self.settingInterface = settings_Window(self)

        if self.settingInterface.isVisible() and not self.settingInterface.isMinimized():
            self.settingInterface.showNormal() 
            self.settingInterface.activateWindow()
            self.settingInterface.raise_()
        else:
            if self.settingInterface.isMinimized():
                self.settingInterface.showNormal()
                self.settingInterface.activateWindow()
                self.settingInterface.raise_()
            else:
                self.settingInterface.show()
                self.settingInterface.activateWindow()
                self.settingInterface.raise_()

    def toggle_levitation_window(self):
        """切换悬浮窗显示状态
        在显示和隐藏状态之间切换悬浮窗口"""
        try:
            enc_settings_path = path_manager.get_enc_set_path()
            with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                settings = json.load(f)
                if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                    if settings.get('hashed_set', {}).get('show_hide_verification_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消暂时切换浮窗显示/隐藏状态操作")
                            return
        except Exception as e:
            logger.error(f"密码验证失败: {e}")
            return

        if not hasattr(self, 'levitation_window') or not self.levitation_window:
            self.levitation_window.show()
        elif self.levitation_window.isVisible():
            self.levitation_window.hide()
        else:
            self.levitation_window.show()
            self.levitation_window.activateWindow()
            self.levitation_window.raise_()

    @pyqtSlot()
    def _show_direct_extraction_window_from_shortcut(self):
        """通过快捷键显示闪抽界面
        通过全局快捷键触发闪抽界面，确保在主线程中执行UI操作"""
        try:
            # 检查levitation_window是否存在，不存在则创建
            if not hasattr(self, 'levitation_window') or not self.levitation_window:
                self.levitation_window = LevitationWindow()
            
            # 调用LevitationWindow的_show_direct_extraction_window方法
            self.levitation_window._show_direct_extraction_window()
            logger.info("通过快捷键成功触发闪抽界面")
        except Exception as e:
            logger.error(f"快捷键触发闪抽界面失败: {e}")

    @pyqtSlot()
    def _show_pumping_interface_from_shortcut(self):
        """通过快捷键显示点名界面
        通过全局快捷键打开点名界面，确保在主线程中执行UI操作"""
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到点名界面
            self.switchTo(self.pumping_peopleInterface)
            logger.info("通过快捷键成功打开点名界面")
        except Exception as e:
            logger.error(f"快捷键打开点名界面失败: {e}")

    @pyqtSlot()
    def _show_reward_interface_from_shortcut(self):
        """通过快捷键显示抽奖界面
        通过全局快捷键打开抽奖界面，确保在主线程中执行UI操作"""
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到抽奖界面
            self.switchTo(self.pumping_rewardInterface)
            logger.info("通过快捷键成功打开抽奖界面")
        except Exception as e:
            logger.error(f"快捷键打开抽奖界面失败: {e}")

    @pyqtSlot()
    def _trigger_pumping_from_shortcut(self):
        """通过快捷键触发点名操作
        通过全局快捷键触发点名操作，确保在主线程中执行UI操作"""
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到点名界面
            self.switchTo(self.pumping_peopleInterface)
            
            # 触发点名操作
            self.pumping_peopleInterface.start_draw()
            logger.info("通过快捷键成功触发点名操作")
        except Exception as e:
            logger.error(f"快捷键触发点名操作失败: {e}")

    @pyqtSlot()
    def _trigger_reward_from_shortcut(self):
        """通过快捷键触发抽奖操作
        通过全局快捷键触发抽奖操作，确保在主线程中执行UI操作"""
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到抽奖界面
            self.switchTo(self.pumping_rewardInterface)
            
            # 触发抽奖操作
            self.pumping_rewardInterface.start_draw()
            logger.info("通过快捷键成功触发抽奖操作")
        except Exception as e:
            logger.error(f"快捷键触发抽奖操作失败: {e}")

    def handle_new_connection(self):
        client_connection = self.server.nextPendingConnection()
        if not client_connection:
            return

        client_connection.readyRead.connect(lambda: self.read_client_data(client_connection))
        client_connection.disconnected.connect(lambda: self.cleanup_connection(client_connection))
        
    def cleanup_connection(self, client_connection):
        """清理IPC连接资源"""
        client_connection.disconnectFromServer()
        client_connection.deleteLater()

    def read_client_data(self, client_connection):
        try:
            # 检查连接状态和是否可读
            if not client_connection or not client_connection.isOpen() or not client_connection.isReadable():
                logger.warning("IPC连接未打开或不可读，跳过处理")
                return
                
            data = client_connection.readAll().data().decode().strip()
            if not data:
                logger.warning("接收到空的IPC消息，跳过处理")
                return
                
            logger.info(f"接收到IPC消息: {data}")
            
            if data == 'show':
                self.toggle_window()
            elif data.startswith('url:'):
                # 处理URL命令
                url_command = data[4:].strip()  # 移除'url:'前缀
                logger.info(f"处理URL命令: {url_command}")
                
                # 使用url_handler处理URL命令
                from app.common.url_handler import get_url_handler
                url_handler = get_url_handler()
                # 设置URL命令
                url_handler.url_command = url_command
                # 处理URL命令，这会显示通知窗口（如果设置启用）
                url_handler.process_url_command(self)
            else:
                logger.warning(f"未知的IPC消息: {data}")
        except Exception as e:
            logger.error(f"处理IPC消息时发生错误: {e}")
        finally:
            # 安全地清理连接
            try:
                # 使用更安全的方式检查对象是否有效
                if client_connection and hasattr(client_connection, 'disconnectFromServer'):
                    try:
                        client_connection.disconnectFromServer()
                    except RuntimeError:
                        # 对象已被删除，忽略错误
                        pass
                    try:
                        client_connection.deleteLater()
                    except RuntimeError:
                        # 对象已被删除，忽略错误
                        pass
            except Exception as e:
                logger.error(f"清理IPC连接时发生错误: {e}")

    def show_window_from_ipc(self, socket):
        """从IPC接收显示窗口请求并激活窗口"""
        try:
            # 检查连接状态和是否可读
            if not socket or not socket.isOpen() or not socket.isReadable():
                logger.warning("IPC连接未打开或不可读，跳过处理")
                return
                
            data = socket.readAll().data().decode().strip()
            if not data:
                logger.warning("接收到空的IPC窗口显示请求，跳过处理")
                return
                
            logger.info(f"接收到IPC窗口显示请求: {data}")
            
            # 确保主窗口资源正确加载并显示
            self.show()
            self.activateWindow()
            self.raise_()
            
            # 处理悬浮窗口
            self._handle_levitation_window()
        except Exception as e:
            logger.error(f"处理IPC窗口显示请求时发生错误: {e}")
        finally:
            # 安全地清理连接
            try:
                # 使用更安全的方式检查对象是否有效
                if socket and hasattr(socket, 'disconnectFromServer'):
                    try:
                        socket.disconnectFromServer()
                    except RuntimeError:
                        # 对象已被删除，忽略错误
                        pass
            except Exception as e:
                logger.error(f"清理IPC连接时发生错误: {e}")

    def _handle_levitation_window(self):
        """处理悬浮窗口激活"""
        if hasattr(self, 'levitation_window') and self.levitation_window:
            self.levitation_window.raise_()
            self.levitation_window.activateWindow()
    
    # ==================================================
    # URL协议支持方法
    # ==================================================
    def show_main_window(self):
        """通过URL协议显示主窗口
        通过URL协议打开主界面，自动显示并激活窗口"""
        logger.info("正在打开主界面")
        self.toggle_window()
        logger.info("主界面已成功打开")
    
    def show_settings_window(self):
        """通过URL协议显示设置窗口
        通过URL协议打开设置界面，检查是否跳过安全验证"""
        logger.info("正在打开设置界面")
        
        # 检查是否跳过安全验证
        skip_security = False
        try:
            settings_path = path_manager.get_settings_path('fixed_url_settings.json')
            if path_manager.file_exists(settings_path):
                with open_file(settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    fixed_url_settings = settings.get('fixed_url', {})
                    skip_security = fixed_url_settings.get('settings_url_skip_security', False)
        except Exception as e:
            logger.error(f"读取跳过安全验证设置失败: {e}")
        
        # 如果没有设置跳过安全验证，则进行密码验证
        if not skip_security:
            self.show_setting_interface()
        else:
            # 跳过安全验证，直接创建并显示设置界面
            if not hasattr(self, 'settingInterface') or not self.settingInterface:
                from app.view.settings import settings_Window
                self.settingInterface = settings_Window(self)

            if self.settingInterface.isVisible() and not self.settingInterface.isMinimized():
                self.settingInterface.showNormal() 
                self.settingInterface.activateWindow()
                self.settingInterface.raise_()
            else:
                if self.settingInterface.isMinimized():
                    self.settingInterface.showNormal()
                    self.settingInterface.activateWindow()
                    self.settingInterface.raise_()
                else:
                    self.settingInterface.show()
                    self.settingInterface.activateWindow()
                    self.settingInterface.raise_()
        
        logger.info("设置界面已成功打开")
    
    def show_pumping_window(self):
        """通过URL协议显示点名窗口
        通过URL协议打开点名界面，自动切换到点名界面"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return

        logger.info("正在打开点名界面")
        if not self.isVisible():
            self.show()
            self.activateWindow()
            self.raise_()
        self.switchTo(self.pumping_peopleInterface)
        logger.info("点名界面已成功打开")
    
    def show_reward_window(self):
        """通过URL协议显示抽奖窗口
        通过URL协议打开抽奖界面，自动切换到抽奖界面"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return

        logger.info("正在打开抽奖界面")
        if not self.isVisible():
            self.show()
            self.activateWindow()
            self.raise_()
        self.switchTo(self.pumping_rewardInterface)
        logger.info("抽奖界面已成功打开")
    
    def show_history_window(self):
        """通过URL协议显示历史记录窗口
        通过URL协议打开历史记录界面，自动切换到历史记录界面"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return

        logger.info("正在打开历史记录界面")
        if not self.isVisible():
            self.show()
            self.activateWindow()
            self.raise_()
        self.switchTo(self.history_handoff_settingInterface)
        # 触发历史记录数据加载
        self.history_handoff_settingInterface.pumping_people_card.load_data()
        logger.info("历史记录界面已成功打开")
    
    def show_floating_window(self):
        """通过URL协议显示浮窗
        通过URL协议打开浮窗界面，检查是否跳过安全验证"""
        # 检查是否跳过安全验证
        skip_security = False
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return

        try:
            settings_path = path_manager.get_settings_path('fixed_url_settings.json')
            if path_manager.file_exists(settings_path):
                with open_file(settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    fixed_url_settings = settings.get('fixed_url', {})
                    skip_security = fixed_url_settings.get('floating_url_skip_security', False)
        except Exception as e:
            logger.error(f"读取跳过安全验证设置失败: {e}")
        
        # 如果没有设置跳过安全验证，则进行密码验证
        if not skip_security:
            self.toggle_levitation_window()
        else:
            # 跳过安全验证，直接切换浮窗状态
            if not hasattr(self, 'levitation_window') or not self.levitation_window:
                self.levitation_window.show()
            elif self.levitation_window.isVisible():
                self.levitation_window.hide()
            else:
                self.levitation_window.show()
                self.levitation_window.activateWindow()
                self.levitation_window.raise_()
        
        logger.info("浮窗界面已成功打开")
    
    def show_plugin_settings_window(self):
        """通过URL协议显示插件设置窗口
        通过URL协议打开插件设置界面，检查是否跳过安全验证"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        # 检查是否跳过安全验证
        skip_security = False
        try:
            settings_path = path_manager.get_settings_path('fixed_url_settings.json')
            if path_manager.file_exists(settings_path):
                with open_file(settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    fixed_url_settings = settings.get('fixed_url', {})
                    skip_security = fixed_url_settings.get('plugin_settings_open_url_skip_security', False)
        except Exception as e:
            logger.error(f"读取跳过安全验证设置失败: {e}")
        
        # 如果没有设置跳过安全验证，则进行密码验证
        if not skip_security:
            # 确保设置窗口存在
            if not hasattr(self, 'settingInterface') or not self.settingInterface:
                from app.view.settings import settings_Window
                self.settingInterface = settings_Window(self)
            
            # 调用设置窗口的插件设置界面方法
            self.settingInterface.show_plugin_settings_interface()
        else:
            # 跳过安全验证，直接创建并显示设置窗口，然后切换到插件设置界面
            if not hasattr(self, 'settingInterface') or not self.settingInterface:
                from app.view.settings import settings_Window
                self.settingInterface = settings_Window(self)
            
            # 确保设置窗口可见
            if not self.settingInterface.isVisible():
                self.settingInterface.show()
                self.settingInterface.activateWindow()
                self.settingInterface.raise_()
            
            # 如果窗口最小化，则恢复
            if self.settingInterface.isMinimized():
                self.settingInterface.showNormal()
            
            # 检查插件设置界面是否存在
            if self.settingInterface.plugin_settingsInterface is not None:
                # 切换到插件设置界面
                self.settingInterface.stackedWidget.setCurrentWidget(self.settingInterface.plugin_settingsInterface)
                logger.info("插件设置界面已成功打开")
            else:
                logger.error("插件设置界面不存在，无法打开")
                # 尝试重新创建插件设置界面
                try:
                    from app.view.plugins.plugin_settings import PluginSettingsWindow
                    self.settingInterface.plugin_settingsInterface = PluginSettingsWindow(self.settingInterface)
                    self.settingInterface.plugin_settingsInterface.setObjectName("plugin_settingsInterface")
                    logger.debug("设置界面: 插件设置界面重新创建成功")
                    # 重新初始化导航
                    self.settingInterface.initNavigation()
                    # 切换到插件设置界面
                    self.settingInterface.stackedWidget.setCurrentWidget(self.settingInterface.plugin_settingsInterface)
                    logger.info("插件设置界面重新创建并成功打开")
                except Exception as e:
                    logger.error(f"重新创建插件设置界面失败: {e}")
        
        logger.info("插件设置界面已成功打开")
    
    def start_pumping_selection(self):
        """通过URL参数启动抽选功能
        通过URL参数启动抽选功能，检查当前界面并调用相应的开始方法"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到点名界面
            self.switchTo(self.pumping_peopleInterface)
            
            # 尝试调用点名界面的开始方法
            self.pumping_peopleInterface.start_draw()
            logger.info("抽选功能已成功启动")
        except Exception as e:
            logger.error(f"启动抽选功能失败: {e}")
    
    def stop_pumping_selection(self):
        """通过URL参数停止抽选功能
        通过URL参数停止抽选功能，检查当前界面并调用相应的停止方法"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到点名界面
            self.switchTo(self.pumping_peopleInterface)
            
            # 尝试调用点名界面的停止方法
            self.pumping_peopleInterface._stop_animation()
            logger.info("抽选功能已成功停止")
        except Exception as e:
            logger.error(f"停止抽选功能失败: {e}")
    
    def reset_pumping_selection(self):
        """通过URL参数重置抽选状态
        通过URL参数重置抽选状态，检查当前界面并调用相应的重置方法"""
        logger.info("正在重置抽选状态")
        try:
            # # 确保主窗口可见
            # if not self.isVisible():
            #     self.show()
            #     self.activateWindow()
            #     self.raise_()
            
            # # 切换到点名界面
            # self.switchTo(self.pumping_peopleInterface)
            
            # 尝试调用点名界面的重置方法
            self.pumping_peopleInterface._reset_to_initial_state()
            logger.info("抽选状态已成功重置")
        except Exception as e:
            logger.error(f"重置抽选状态失败: {e}")
    
    def start_reward_selection(self):
        """通过URL参数启动抽奖功能
        通过URL参数启动抽奖功能，检查当前界面并调用相应的开始方法"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到抽奖界面
            self.switchTo(self.pumping_rewardInterface)
            
            # 尝试调用抽奖界面的开始方法
            self.pumping_rewardInterface.start_draw()
            logger.info("抽奖功能已成功启动")
        except Exception as e:
            logger.error(f"启动抽奖功能失败: {e}")
    
    def stop_reward_selection(self):
        """通过URL参数停止抽奖功能
        通过URL参数停止抽奖功能，检查当前界面并调用相应的停止方法"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        try:
            # 确保主窗口可见
            if not self.isVisible():
                self.show()
                self.activateWindow()
                self.raise_()
            
            # 切换到抽奖界面
            self.switchTo(self.pumping_rewardInterface)
            
            # 尝试调用抽奖界面的停止方法
            self.pumping_rewardInterface._stop_animation()
            logger.info("抽奖功能已成功停止")
        except Exception as e:
            logger.error(f"停止抽奖功能失败: {e}")
    
    def reset_reward_selection(self):
        """通过URL参数重置抽奖状态
        通过URL参数重置抽奖状态，检查当前界面并调用相应的重置方法"""
        logger.info("正在重置抽奖状态")
        try:
            # 尝试调用抽奖界面的重置方法
            self.pumping_rewardInterface._reset_to_initial_state()
            logger.info("抽奖状态已成功重置")
        except Exception as e:
            logger.error(f"重置抽奖状态失败: {e}")

    def show_direct_extraction(self):
        """通过URL参数直接打开点名界面
        通过URL参数直接打开点名界面，自动切换到点名界面"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用抽取功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        self.levitation_window._show_direct_extraction_window()
        logger.info("闪抽界面已成功打开")
    
    def show_about_window(self):
        """通过URL协议打开关于界面
        通过URL协议打开关于界面，自动切换到关于界面"""
        if self._is_non_class_time():
            try:
                enc_settings_path = path_manager.get_enc_set_path()
                with open_file(enc_settings_path, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    if settings.get('hashed_set', {}).get('start_password_enabled', False) == True:
                        from app.common.password_dialog import PasswordDialog
                        dialog = PasswordDialog(self)
                        if dialog.exec_() != QDialog.Accepted:
                            logger.warning("用户取消在课间调用URL使用功能")
                            return
            except Exception as e:
                logger.error(f"密码验证失败: {e}")
                return
        
        if not self.isVisible():
            self.show()
            self.activateWindow()
            self.raise_()
        self.switchTo(self.about_settingInterface)
        logger.info("关于界面已成功打开")
    
    def show_donation_dialog(self):
        """通过URL参数打开捐赠支持对话框
        通过URL参数打开捐赠支持对话框，显示捐赠支持对话框"""
        try:
            # 打开捐赠支持对话框
            donation_dialog = DonationDialog(self)
            donation_dialog.exec_()
            logger.info("捐赠支持对话框已成功打开")
        except Exception as e:
            logger.error(f"打开捐赠支持对话框失败: {e}")
    
    def show_contributor_dialog(self):
        """通过URL参数打开贡献者对话框
        通过URL参数打开贡献者对话框，显示贡献者对话框"""
        try:
            # 打开贡献者对话框
            contributor_dialog = ContributorDialog(self)
            contributor_dialog.exec_()
            logger.info("贡献者对话框已成功打开")
        except Exception as e:
            logger.error(f"打开贡献者对话框失败: {e}")
