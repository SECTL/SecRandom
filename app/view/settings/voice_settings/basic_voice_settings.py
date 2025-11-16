# ==================================================
# 导入库
# ==================================================
import asyncio
import edge_tts
import aiohttp

from loguru import logger
from PySide6.QtWidgets import *
from PySide6.QtGui import *
from PySide6.QtCore import *
from PySide6.QtNetwork import *
from qfluentwidgets import *

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.personalised import *
from app.tools.settings_default import *
from app.tools.settings_access import *
from app.Language.obtain_language import *


# ==================================================
# 基本语音设置
# ==================================================
class basic_voice_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加语音引擎设置组件
        self.voice_engine_widget = basic_settings_voice_engine(self)
        self.vBoxLayout.addWidget(self.voice_engine_widget)

        # 添加音量设置组件
        self.volume_widget = basic_settings_volume(self)
        self.vBoxLayout.addWidget(self.volume_widget)

        # 添加系统音量控制组件
        self.system_volume_widget = basic_settings_system_volume(self)
        self.vBoxLayout.addWidget(self.system_volume_widget)


class basic_settings_voice_engine(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("basic_voice_settings", "voice_engine_group")
        )
        self.setBorderRadius(8)

        # 初始化异步更新相关变量
        self._is_updating_voices = False

        # 语音引擎设置
        self.voice_engine = ComboBox()
        self.voice_engine.addItems(
            get_content_combo_name_async("basic_voice_settings", "voice_engine")
        )
        self.voice_engine.setCurrentText(
            readme_settings_async("basic_voice_settings", "voice_engine")
        )
        self.voice_engine.currentTextChanged.connect(self.on_voice_engine_changed)

        # 初始化Edge TTS语音名称设置
        self.edge_tts_voice_name = ComboBox()
        self.edge_tts_voice_name.addItems(
            get_content_combo_name_async("basic_voice_settings", "edge_tts_voice_name")
        )
        self.edge_tts_voice_name.setCurrentText(
            readme_settings_async("basic_voice_settings", "edge_tts_voice_name")
        )
        self.edge_tts_voice_name.currentTextChanged.connect(
            lambda text: update_settings(
                "basic_voice_settings", "edge_tts_voice_name", text
            )
        )

        # 根据当前语音引擎设置Edge TTS语音名称的可用性
        current_engine = self.voice_engine.currentText()
        self.edge_tts_voice_name.setEnabled(current_engine == "Edge TTS")

        # 如果当前是Edge TTS，异步更新语音列表
        if current_engine == "Edge TTS":
            QTimer.singleShot(1000, self._async_update_edge_tts_voices)

        # 语音播放设备设置
        self.voice_playback = ComboBox()
        self.voice_playback.addItems(
            get_content_combo_name_async("basic_voice_settings", "voice_playback")
        )
        self.voice_playback.setCurrentText(
            readme_settings_async("basic_voice_settings", "voice_playback")
        )
        self.voice_playback.currentTextChanged.connect(
            lambda text: update_settings("basic_voice_settings", "voice_playback", text)
        )

        # 语速调节设置
        self.speech_rate = SpinBox()
        self.speech_rate.setFixedWidth(WIDTH_SPINBOX)
        self.speech_rate.setRange(0, 100)
        self.speech_rate.setSuffix("wpm")
        self.speech_rate.setValue(
            int(readme_settings_async("basic_voice_settings", "speech_rate"))
        )
        self.speech_rate.valueChanged.connect(
            lambda value: update_settings("basic_voice_settings", "speech_rate", value)
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_2_20_filled"),
            get_content_name_async("basic_voice_settings", "voice_engine"),
            get_content_description_async("basic_voice_settings", "voice_engine"),
            self.voice_engine,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_mic_record_20_filled"),
            get_content_name_async("basic_voice_settings", "edge_tts_voice_name"),
            get_content_description_async(
                "basic_voice_settings", "edge_tts_voice_name"
            ),
            self.edge_tts_voice_name,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_0_20_filled"),
            get_content_name_async("basic_voice_settings", "voice_playback"),
            get_content_description_async("basic_voice_settings", "voice_playback"),
            self.voice_playback,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_top_speed_20_filled"),
            get_content_name_async("basic_voice_settings", "speech_rate"),
            get_content_description_async("basic_voice_settings", "speech_rate"),
            self.speech_rate,
        )

    def on_voice_engine_changed(self, text):
        """语音引擎改变时的处理函数"""
        # 更新设置
        update_settings("basic_voice_settings", "voice_engine", text)

        # 根据选择的引擎启用/禁用Edge TTS语音名称选择
        is_edge_tts = text == "Edge TTS"
        self.edge_tts_voice_name.setEnabled(is_edge_tts)

        # 当切换到Edge TTS时，延迟异步更新语音列表，避免频繁触发网络请求
        if is_edge_tts:
            # 使用QTimer延迟执行，给UI一些响应时间
            QTimer.singleShot(500, self._async_update_edge_tts_voices)

    def _async_update_edge_tts_voices(self):
        """异步更新Edge TTS语音列表"""
        # 如果正在更新，则不再重复请求
        if self._is_updating_voices:
            return

        self._is_updating_voices = True
        try:
            # 尝试获取当前事件循环
            try:
                loop = asyncio.get_event_loop()
                if loop.is_running():
                    # 如果循环正在运行，使用QTimer.singleShot创建任务
                    QTimer.singleShot(
                        0,
                        lambda: asyncio.create_task(
                            self._update_edge_tts_voices_task()
                        ),
                    )
                else:
                    # 如果循环没有运行，直接创建任务
                    loop.create_task(self._update_edge_tts_voices_task())
            except RuntimeError:
                # 如果没有事件循环，创建一个新的
                loop = asyncio.new_event_loop()
                asyncio.set_event_loop(loop)
                loop.create_task(self._update_edge_tts_voices_task())
        except Exception as e:
            logger.error(f"创建Edge TTS语音更新任务失败: {e}")
            self._is_updating_voices = False

    async def _update_edge_tts_voices_task(self):
        """异步更新Edge TTS语音列表的任务"""
        try:
            # 获取最新的语音列表
            voices = await self._get_edge_tts_voices()
            # 获取当前选中的语音
            current_voice = self.edge_tts_voice_name.currentText()

            # 使用信号槽机制在主线程中更新UI
            self._update_voice_combo_box(voices, current_voice)
        except Exception as e:
            logger.error(f"更新Edge TTS语音列表失败: {e}")
        finally:
            # 无论成功或失败，都要重置更新标志
            self._is_updating_voices = False

    @Slot(list, str)
    def _update_voice_combo_box(self, voices, current_voice):
        """在主线程中更新语音列表下拉框"""
        try:
            # 清空当前列表
            self.edge_tts_voice_name.clear()
            # 添加新的语音列表
            self.edge_tts_voice_name.addItems([v["id"] for v in voices])
            # 尝试恢复之前选中的语音
            index = self.edge_tts_voice_name.findText(current_voice)
            if index >= 0:
                self.edge_tts_voice_name.setCurrentIndex(index)
            else:
                # 如果找不到之前选中的语音，使用默认语音
                self.edge_tts_voice_name.setCurrentText("zh-CN-XiaoxiaoNeural")

            logger.info(f"Edge TTS语音列表已更新，共{len(voices)}个语音")
        except Exception as e:
            logger.error(f"更新语音列表下拉框失败: {e}")

    async def _get_edge_tts_voices(self):
        """获取Edge TTS语音列表"""
        max_retries = 3
        retry_delay = 2  # 秒

        for attempt in range(max_retries):
            try:
                voices = await edge_tts.list_voices()
                filtered_voices = [
                    {
                        "name": v["FriendlyName"],
                        "id": v["ShortName"]
                        if not v["Locale"].startswith("zh-CN")
                        else f"zh-CN-{v['FriendlyName'].split()[1]}Neural",
                        "languages": v["Locale"].replace("_", "-"),
                        "full_info": f"{v['Gender']} | {v['Locale']} | Type: {v.get('VoiceType', 'Unknown')}",
                    }
                    for v in voices
                    if v["Locale"]
                ]
                return filtered_voices
            except (aiohttp.ClientError, aiohttp.ClientResponseError) as e:
                if attempt < max_retries - 1:
                    logger.error(
                        f"Edge TTS服务连接失败，第{attempt + 1}次重试中... 错误: {str(e)!r}"
                    )
                    await asyncio.sleep(retry_delay)
                    continue
                else:
                    logger.error(
                        f"Edge TTS服务连接失败，已重试{max_retries}次: {str(e)!r}"
                    )
            except KeyError as e:
                logger.error(f"Edge TTS语音解析失败: {str(e)!r}")
                break
            except Exception as e:
                if attempt < max_retries - 1:
                    logger.error(
                        f"Edge TTS语音获取失败，第{attempt + 1}次重试中... 错误: {str(e)!r}"
                    )
                    await asyncio.sleep(retry_delay)
                    continue
                else:
                    logger.error(f"Edge TTS语音解析失败: {str(e)!r}")

        # 所有尝试都失败后，返回默认语音列表
        return self._get_default_voices()

    def _get_default_voices(self):
        """获取默认语音列表"""
        default_voices = [
            {
                "name": "Xiaoxiao",
                "id": "zh-CN-XiaoxiaoNeural",
                "languages": "zh-CN",
                "full_info": "Female | zh-CN | Type: Neural",
            },
            {
                "name": "Yunxi",
                "id": "zh-CN-YunxiNeural",
                "languages": "zh-CN",
                "full_info": "Male | zh-CN | Type: Neural",
            },
            {
                "name": "Xiaoyi",
                "id": "zh-CN-XiaoyiNeural",
                "languages": "zh-CN",
                "full_info": "Female | zh-CN | Type: Neural",
            },
            {
                "name": "Jenny",
                "id": "en-US-JennyNeural",
                "languages": "en-US",
                "full_info": "Female | en-US | Type: Neural",
            },
            {
                "name": "Guy",
                "id": "en-US-GuyNeural",
                "languages": "en-US",
                "full_info": "Male | en-US | Type: Neural",
            },
        ]
        logger.info("Edge TTS服务不可用，使用默认语音列表")
        return default_voices


class basic_settings_volume(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("basic_voice_settings", "volume_group"))
        self.setBorderRadius(8)

        # 音量大小设置
        self.volume_size = SpinBox()
        self.volume_size.setFixedWidth(WIDTH_SPINBOX)
        self.volume_size.setRange(0, 100)
        self.volume_size.setSuffix("%")
        self.volume_size.setValue(
            int(readme_settings_async("basic_voice_settings", "volume_size"))
        )
        self.volume_size.valueChanged.connect(
            lambda value: update_settings("basic_voice_settings", "volume_size", value)
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_edit_20_filled"),
            get_content_name_async("basic_voice_settings", "volume_size"),
            get_content_description_async("basic_voice_settings", "volume_size"),
            self.volume_size,
        )


class basic_settings_system_volume(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("basic_voice_settings", "system_volume_group")
        )
        self.setBorderRadius(8)

        # 系统音量控制类型设置
        self.system_volume_control = ComboBox()
        self.system_volume_control.addItems(
            get_content_combo_name_async(
                "basic_voice_settings", "system_volume_control"
            )
        )
        self.system_volume_control.setCurrentText(
            readme_settings_async("basic_voice_settings", "system_volume_control")
        )
        self.system_volume_control.currentTextChanged.connect(
            lambda text: update_settings(
                "basic_voice_settings", "system_volume_control", text
            )
        )

        # 系统音量大小设置
        self.system_volume_size = SpinBox()
        self.system_volume_size.setFixedWidth(WIDTH_SPINBOX)
        self.system_volume_size.setRange(0, 100)
        self.system_volume_size.setSuffix("%")
        self.system_volume_size.setValue(
            int(readme_settings_async("basic_voice_settings", "system_volume_size"))
        )
        self.system_volume_size.valueChanged.connect(
            lambda value: update_settings(
                "basic_voice_settings", "system_volume_size", value
            )
        )

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_settings_20_filled"),
            get_content_name_async("basic_voice_settings", "system_volume_control"),
            get_content_description_async(
                "basic_voice_settings", "system_volume_control"
            ),
            self.system_volume_control,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_edit_20_filled"),
            get_content_name_async("basic_voice_settings", "system_volume_size"),
            get_content_description_async("basic_voice_settings", "system_volume_size"),
            self.system_volume_size,
        )
