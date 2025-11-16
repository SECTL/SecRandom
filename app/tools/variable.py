# 定义非设置项的初始变量

# ==================== 软件基本信息 ====================
APPLY_NAME = "SecRandom"  # 软件名称
VERSION = "v0.0.0.0"  # 软件当前版本
NEXT_VERSION = "v2.0.0.0"  # 软件下一个版本
YEAR = 2025  # 软件发布年份
MONTH = 4  # 软件发布月份
AUTHOR = "lzy98276"  # 软件作者
APP_DESCRIPTION = (
    "一个易用的班级抽号软件，专为教育场景设计，让课堂点名更高效透明"  # 软件描述
)
APP_COPYRIGHT = f"Copyright © {YEAR} {AUTHOR}. All rights reserved."  # 软件版权信息
APP_LICENSE = "GPL-3.0 License"  # 软件许可证
APP_EMAIL = "lzy.12@foxmail.com"  # 软件作者邮箱

# ==================== 联系与链接信息 ====================
GITHUB_WEB = "https://github.com/SECTL/SecRandom"  # 软件GitHub仓库
BILIBILI_WEB = "https://space.bilibili.com/520571577"  # 软件作者Bilibili空间
WEBSITE = "https://secrandom.netlify.app"  # 软件官方网站
DONATION_URL = "https://afdian.com/a/lzy0983"  # 捐赠链接

# ==================== UI相关默认值 ====================
DEFAULT_THEME_COLOR = "#66CCFF"  # 天依蓝 - 默认主题色
FALLBACK_THEME_COLOR = "#3AF2FF"  # 备用主题色
DEFAULT_ICON_CODEPOINT = 62634  # 默认图标码点(info图标)
WIDTH_SPINBOX = 180  # 自旋框宽度
MINIMUM_WINDOW_SIZE = (600, 400)  # 窗口最小尺寸

# ==================== 字体相关配置 ====================
# 第一种字体名称
DEFAULT_FONT_NAME_PRIMARY = "HarmonyOS Sans SC"  # 第一种字体名称
DEFAULT_FONT_FILENAME_PRIMARY = "HarmonyOS_Sans_SC_Bold.ttf"  # 第一种字体文件名

# 第二种字体名称
DEFAULT_FONT_NAME_ALT = "汉仪文黑-85W"  # 第二种字体名称
DEFAULT_FONT_FILENAME_ALT = "汉仪文黑-85W.ttf"  # 第二种字体文件名

# 字体配置映射表 - 提高可维护性
FONT_CONFIG_MAP = {
    DEFAULT_FONT_NAME_ALT: {
        "filename": DEFAULT_FONT_FILENAME_ALT,
        "display_name": DEFAULT_FONT_NAME_ALT,
    },
    DEFAULT_FONT_NAME_PRIMARY: {
        "filename": DEFAULT_FONT_FILENAME_PRIMARY,
        "display_name": DEFAULT_FONT_NAME_PRIMARY,
    },
}

# ==================== 文件系统相关默认值 ====================
DEFAULT_SETTINGS_FILENAME = "settings.json"  # 默认设置文件名
DEFAULT_FILE_ENCODING = "utf-8"  # 默认文件编码

# ==================== 路径相关常量 ====================
# 日志路径
LOG_DIR = "logs"  # 日志目录名
LOG_FILENAME_FORMAT = "SecRandom_{time:YYYY-MM-DD-HH-mm-ss}.log"  # 日志文件名格式

# 资源文件夹路径
STUDENT_IMAGE_FOLDER = "images/student_images"  # 学生图片文件夹名
PRIZE_IMAGE_FOLDER = "images/prize_images"  # 奖品图片文件夹名
ANIMATION_MUSIC_FOLDER = "music/animation_music"  # 动画音乐文件夹名
RESULT_MUSIC_FOLDER = "music/result_music"  # 结果音乐文件夹名

# ==================== 日志相关常量 ====================
LOG_ROTATION_SIZE = "1 MB"  # 日志文件轮转大小
LOG_RETENTION_DAYS = "30 days"  # 日志保留天数
LOG_COMPRESSION = "tar.gz"  # 日志压缩格式

# ==================== 应用程序相关常量 ====================
APP_QUIT_ON_LAST_WINDOW_CLOSED = False  # 最后一个窗口关闭时是否退出应用程序
APP_INIT_DELAY = 100  # 应用程序初始化延迟时间(毫秒)
FONT_APPLY_DELAY = 0  # 字体应用延迟时间(毫秒)
APPLY_DELAY = 0  # 应用延迟时间(毫秒)

# ==================== 界面交互相关常量 ====================
MENU_AUTO_CLOSE_TIMEOUT = 5000  # 菜单自动关闭时间（毫秒）

# ==================== 语言相关常量 ====================
LANGUAGE_ZH_CN = "ZH_CN"  # 中文
LANGUAGE_EN_US = "EN_US"  # 英文
DEFAULT_LANGUAGE = LANGUAGE_ZH_CN  # 默认语言为中文
LANGUAGE_MODULE_DIR = "app/Language/modules"  # 模块化语言文件路径

# ==================== 共享内存相关常量 ====================
SHARED_MEMORY_KEY = "SecRandomSharedMemory"  # 共享内存键名

# ==================== 结果显示相关常量 ====================
# 布局间距
AVATAR_LABEL_SPACING = 8  # 头像和标签之间的间距
GRID_LAYOUT_SPACING = 20  # 网格布局统一间距
GRID_HORIZONTAL_SPACING = 20  # 网格布局水平间距
GRID_VERTICAL_SPACING = 10  # 网格布局垂直间距
GRID_ITEM_MARGIN = 40  # 网格项目边距
GRID_ITEM_SPACING = 20  # 网格项目间距
DEFAULT_AVAILABLE_WIDTH = 800  # 默认可用宽度

# 颜色生成相关
DEFAULT_MIN_SATURATION = 0.7  # 默认最小饱和度
DEFAULT_MAX_SATURATION = 1.0  # 默认最大饱和度
DEFAULT_MIN_VALUE = 0.7  # 默认最小亮度值
DEFAULT_MAX_VALUE = 1.0  # 默认最大亮度值

# 浅色主题颜色调整
LIGHT_VALUE_MULTIPLIER = 0.7  # 浅色主题亮度乘数
LIGHT_MAX_VALUE_MULTIPLIER = 0.8  # 浅色主题最大亮度乘数
LIGHT_THEME_MAX_VALUE = 0.5  # 浅色主题最大亮度值
LIGHT_THEME_ADJUSTED_MAX_VALUE = 0.7  # 浅色主题调整后的最大亮度值

# 深色主题颜色调整
DARK_VALUE_MULTIPLIER = 1.2  # 深色主题亮度乘数
DARK_MAX_VALUE_MULTIPLIER = 1.0  # 深色主题最大亮度乘数
DARK_THEME_MIN_VALUE = 0.85  # 深色主题最小亮度值
DARK_THEME_MAX_VALUE = 1.0  # 深色主题最大亮度值
LIGHTNESS_THRESHOLD = 127  # 浅色/深色主题亮度阈值
RGB_COLOR_FORMAT = "rgb({r},{g},{b})"  # RGB颜色格式字符串

# 图片相关
SUPPORTED_IMAGE_EXTENSIONS = [".png", ".jpg", ".jpeg", ".svg"]  # 支持的图片扩展名

# 格式化相关
STUDENT_ID_FORMAT = "{num:02}"  # 学号格式化字符串
NAME_SPACING = "    "  # 姓名之间的间距

# ==================== 贡献者页面相关常量 ====================
CONTRIBUTOR_CARD_MIN_WIDTH = 250  # 贡献者卡片最小宽度
CONTRIBUTOR_MAX_COLUMNS = 12  # 贡献者页面最大列数限制
CONTRIBUTOR_CARD_SPACING = 20  # 贡献者卡片间距
CONTRIBUTOR_CARD_MARGIN = 15  # 贡献者卡片内边距
CONTRIBUTOR_AVATAR_RADIUS = 64  # 贡献者头像半径
CONTRIBUTOR_MAX_ROLE_WIDTH = 500  # 贡献者职责文本最大宽度

# ==================== 学生卡片相关常量 ====================
STUDENT_CARD_MIN_WIDTH = 200  # 学生卡片最小宽度
STUDENT_CARD_FIXED_WIDTH = 180  # 学生卡片固定宽度
STUDENT_CARD_FIXED_HEIGHT = 100  # 学生卡片固定高度
STUDENT_MAX_COLUMNS = 8  # 学生页面最大列数限制
STUDENT_CARD_SPACING = 15  # 学生卡片间距
STUDENT_CARD_MARGIN = 12  # 学生卡片内边距
STUDENT_AVATAR_RADIUS = 50  # 学生头像半径

# ==================== 全局变量 ====================
main_window = None  # 全局主窗口引用

# ==================== Settings 背景预热配置 ====================
# 后台预热设置页面的默认时间间隔（毫秒）和默认最大预热页数
SETTINGS_WARMUP_INTERVAL_MS = 800
SETTINGS_WARMUP_MAX_PRELOAD = 1
