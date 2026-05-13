import sys
from PySide6.QtCore import QObject, Signal

from loguru import logger

from app.tools.settings_access import readme_settings_async

try:
    import keyboard
except Exception as e:
    keyboard = None
    logger.warning(f"keyboard 库不可用: {e}")


class ShortcutManager(QObject):
    """快捷键管理器

    负责注册、监听和管理全局快捷键
    使用 keyboard 库实现系统级全局快捷键
    """

    openRollCallPageRequested = Signal()
    useQuickDrawRequested = Signal()
    openLotteryPageRequested = Signal()
    increaseRollCallCountRequested = Signal()
    decreaseRollCallCountRequested = Signal()
    increaseLotteryCountRequested = Signal()
    decreaseLotteryCountRequested = Signal()
    startRollCallRequested = Signal()
    startLotteryRequested = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.shortcuts = {}
        self._enabled = False
        self._available = keyboard is not None and sys.platform != "darwin"
        logger.debug("快捷键管理器初始化中...")
        logger.info("快捷键管理器初始化完成，等待回调函数设置...")

    def _init_shortcuts(self):
        """初始化快捷键"""
        enabled = readme_settings_async("shortcut_settings", "enable_shortcut")
        self._enabled = enabled

        logger.debug(f"快捷键功能状态: {'启用' if enabled else '禁用'}")

        if not self._available:
            logger.warning("快捷键功能不可用")
            return

        if not enabled:
            logger.debug("快捷键功能未启用")
            return

        logger.debug(f"keyboard 库是否可用: {keyboard.is_modifier('ctrl')}")

        self._register_shortcut("open_roll_call_page", self.openRollCallPageRequested)
        self._register_shortcut("use_quick_draw", self.useQuickDrawRequested)
        self._register_shortcut("open_lottery_page", self.openLotteryPageRequested)
        self._register_shortcut(
            "increase_roll_call_count", self.increaseRollCallCountRequested
        )
        self._register_shortcut(
            "decrease_roll_call_count", self.decreaseRollCallCountRequested
        )
        self._register_shortcut(
            "increase_lottery_count", self.increaseLotteryCountRequested
        )
        self._register_shortcut(
            "decrease_lottery_count", self.decreaseLotteryCountRequested
        )
        self._register_shortcut("start_roll_call", self.startRollCallRequested)
        self._register_shortcut("start_lottery", self.startLotteryRequested)

        logger.info(f"快捷键初始化完成，共注册 {len(self.shortcuts)} 个快捷键")

    def _register_shortcut(self, config_key: str, signal: Signal):
        """注册快捷键

        Args:
            config_key: 设置中的配置键
            signal: 要触发的信号
        """
        shortcut_str = readme_settings_async("shortcut_settings", config_key)

        logger.debug(f"尝试注册快捷键: {config_key}, 值: {shortcut_str}")

        if shortcut_str:
            try:
                hotkey = self._convert_to_hotkey(shortcut_str)
                logger.debug(f"快捷键热键: {hotkey}")

                if hotkey:

                    def on_pressed():
                        logger.info(f"快捷键被触发: {config_key}")
                        signal.emit()

                    keyboard.add_hotkey(hotkey, on_pressed)
                    self.shortcuts[config_key] = hotkey
                    logger.info(
                        f"快捷键已注册: {config_key} = {shortcut_str}, 热键: {hotkey}"
                    )
                else:
                    logger.debug(f"快捷键热键为空: {config_key} = {shortcut_str}")
            except Exception as e:
                logger.exception(f"注册快捷键失败 {config_key}: {e}")
                import traceback

                logger.exception(traceback.format_exc())
        else:
            logger.debug(f"快捷键未设置: {config_key}")

    def _convert_to_hotkey(self, shortcut_str: str) -> str:
        """将快捷键字符串转换为 keyboard 库的热键格式

        Args:
            shortcut_str: 快捷键字符串（如 "Ctrl+q"）

        Returns:
            keyboard 库的热键格式（如 "ctrl+q"）
        """
        if not shortcut_str:
            return ""

        hotkey = shortcut_str.lower()

        hotkey = hotkey.replace("ctrl", "ctrl")
        hotkey = hotkey.replace("alt", "alt")
        hotkey = hotkey.replace("shift", "shift")
        hotkey = hotkey.replace("win", "win")

        return hotkey

    def reload_shortcuts(self):
        """重新加载所有快捷键"""
        logger.info("重新加载快捷键")

        if not self._available:
            return

        for config_key, hotkey in self.shortcuts.items():
            try:
                keyboard.remove_hotkey(hotkey)
            except KeyError as e:
                logger.warning(f"注销快捷键失败 {config_key}: {e}")
            except Exception as e:
                logger.warning(f"注销快捷键失败 {config_key}: {e}")

        self.shortcuts.clear()

        if self._enabled:
            self._init_shortcuts()

    def update_shortcut(self, config_key: str, shortcut_str: str):
        """更新单个快捷键

        Args:
            config_key: 设置中的配置键
            shortcut_str: 快捷键字符串
        """
        if config_key in self.shortcuts:
            try:
                old_hotkey = self.shortcuts[config_key]
                keyboard.remove_hotkey(old_hotkey)
            except KeyError as e:
                logger.warning(f"注销快捷键失败 {config_key}: {e}")
            except Exception as e:
                logger.warning(f"注销快捷键失败 {config_key}: {e}")
            finally:
                self.shortcuts.pop(config_key, None)

        if shortcut_str and self._enabled and self._available:
            try:
                hotkey = self._convert_to_hotkey(shortcut_str)
                if hotkey:
                    signal = self._get_signal_for_key(config_key)
                    if signal:

                        def on_pressed():
                            logger.info(f"快捷键被触发: {config_key}")
                            signal.emit()

                        keyboard.add_hotkey(hotkey, on_pressed)
                        self.shortcuts[config_key] = hotkey
                        logger.info(
                            f"快捷键已更新: {config_key} = {shortcut_str}, 热键: {hotkey}"
                        )
            except Exception as e:
                logger.exception(f"更新快捷键失败 {config_key}: {e}")

    def _get_signal_for_key(self, config_key: str) -> Signal:
        """根据配置键获取对应的信号

        Args:
            config_key: 设置中的配置键

        Returns:
            对应的信号对象
        """
        signal_map = {
            "open_roll_call_page": self.openRollCallPageRequested,
            "use_quick_draw": self.useQuickDrawRequested,
            "open_lottery_page": self.openLotteryPageRequested,
            "increase_roll_call_count": self.increaseRollCallCountRequested,
            "decrease_roll_call_count": self.decreaseRollCallCountRequested,
            "increase_lottery_count": self.increaseLotteryCountRequested,
            "decrease_lottery_count": self.decreaseLotteryCountRequested,
            "start_roll_call": self.startRollCallRequested,
            "start_lottery": self.startLotteryRequested,
        }
        return signal_map.get(config_key)

    def set_enabled(self, enabled: bool):
        """启用或禁用所有快捷键

        Args:
            enabled: 是否启用
        """
        self._enabled = enabled

        if not self._available:
            self.shortcuts.clear()
            return

        if enabled:
            self.reload_shortcuts()
        else:
            for config_key, hotkey in self.shortcuts.items():
                try:
                    keyboard.remove_hotkey(hotkey)
                except KeyError as e:
                    logger.warning(f"注销快捷键失败 {config_key}: {e}")
                except Exception as e:
                    logger.warning(f"注销快捷键失败 {config_key}: {e}")
            self.shortcuts.clear()
            logger.info("快捷键已禁用")

    def is_enabled(self) -> bool:
        """检查快捷键是否启用

        Returns:
            是否启用
        """
        return self._enabled

    def cleanup(self):
        """清理所有快捷键（快速清理）"""
        logger.info("清理所有快捷键")
        if not self._available:
            self.shortcuts.clear()
            return
        try:
            # 使用 keyboard.unhook_all() 一次性清理所有钩子，比逐个 remove_hotkey 快得多
            keyboard.unhook_all()
            logger.debug("已清理所有 keyboard 钩子")
        except Exception as e:
            logger.warning(f"清理 keyboard 钩子失败: {e}")
        self.shortcuts.clear()
