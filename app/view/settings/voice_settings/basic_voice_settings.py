# ==================================================
# 导入库
# ==================================================
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

# 导入Edge TTS Worker
from app.common.voice.edge_tts_worker import EdgeTTSWorker


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

        # 添加播报完成等待设置组件
        self.voice_wait_widget = basic_settings_voice_wait(self)
        self.vBoxLayout.addWidget(self.voice_wait_widget)


class basic_settings_voice_engine(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("basic_voice_settings", "voice_engine_group")
        )
        self.setBorderRadius(8)

        # 初始化Edge TTS Worker
        self.edge_tts_worker = None

        # 语音功能开关
        self.voice_enable_switch = SwitchButton()
        self.voice_enable_switch.setOffText(
            get_content_switchbutton_name_async(
                "basic_voice_settings", "voice_enable", "disable"
            )
        )
        self.voice_enable_switch.setOnText(
            get_content_switchbutton_name_async(
                "basic_voice_settings", "voice_enable", "enable"
            )
        )
        self.voice_enable_switch.setChecked(
            readme_settings_async("basic_voice_settings", "voice_enable")
        )
        self.voice_enable_switch.checkedChanged.connect(
            lambda state: update_settings("basic_voice_settings", "voice_enable", state)
        )

        # 语音引擎设置
        self.voice_engine = ComboBox()
        self.voice_engine.addItems(
            get_content_combo_name_async("basic_voice_settings", "voice_engine")
        )
        self.voice_engine.setCurrentText(
            readme_settings_async("basic_voice_settings", "voice_engine")
        )
        self.voice_engine.currentIndexChanged.connect(self.on_voice_engine_changed)

        # 初始化Edge TTS语音名称设置
        self.edge_tts_voiceComboBox = ComboBox()
        self.edge_tts_voiceComboBox.addItems(
            get_content_combo_name_async("basic_voice_settings", "edge_tts_voice_name")
        )
        self.edge_tts_voiceComboBox.setCurrentText(
            readme_settings_async("basic_voice_settings", "edge_tts_voice_name")
        )
        self.edge_tts_voiceComboBox.currentTextChanged.connect(
            lambda text: update_settings(
                "basic_voice_settings", "edge_tts_voice_name", text
            )
        )

        # 根据当前语音引擎设置Edge TTS语音名称的可用性
        current_index = self.voice_engine.currentIndex()
        self.edge_tts_voiceComboBox.setEnabled(current_index == 1)

        # 如果当前是Edge TTS，更新语音列表
        if current_index == 1:
            self.update_edge_tts_voices()

        # 添加设置项到分组
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_off_20_filled"),
            get_content_name_async("basic_voice_settings", "voice_enable"),
            get_content_description_async("basic_voice_settings", "voice_enable"),
            self.voice_enable_switch,
        )
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
            self.edge_tts_voiceComboBox,
        )

    def on_voice_engine_changed(self, index):
        """语音引擎改变时的处理函数"""
        # 根据索引获取当前引擎文本
        current_engine = self.voice_engine.itemText(index)
        # 更新设置
        update_settings("basic_voice_settings", "voice_engine", current_engine)

        # 根据选择的引擎启用/禁用Edge TTS语音名称选择
        self.edge_tts_voiceComboBox.setEnabled(index == 1)

        # 如果切换到Edge TTS，更新语音列表
        if index == 1:
            self.update_edge_tts_voices()

    def update_edge_tts_voices(self):
        """异步更新Edge TTS语音列表"""
        # 如果worker正在运行，先停止
        if self.edge_tts_worker and self.edge_tts_worker.isRunning():
            self.edge_tts_worker.terminate()
            self.edge_tts_worker.wait()

        # 创建新的worker实例
        self.edge_tts_worker = EdgeTTSWorker()
        # 连接信号槽
        self.edge_tts_worker.voices_fetched.connect(self.on_voices_fetched)
        self.edge_tts_worker.error_occurred.connect(self.on_voices_fetch_error)
        # 启动worker
        self.edge_tts_worker.start()

    def on_voices_fetched(self, voices):
        """语音列表获取成功后的处理"""
        try:
            # 保存当前选中的语音
            current_voice = self.edge_tts_voiceComboBox.currentText()
            # 清空当前列表
            self.edge_tts_voiceComboBox.clear()
            # 添加新的语音列表
            voice_ids = [v["id"] for v in voices]
            self.edge_tts_voiceComboBox.addItems(voice_ids)

            # 尝试恢复之前选中的语音
            if current_voice in voice_ids:
                self.edge_tts_voiceComboBox.setCurrentText(current_voice)
            elif voice_ids:
                # 如果之前的语音不存在，选择第一个
                self.edge_tts_voiceComboBox.setCurrentIndex(0)

            logger.debug(f"Edge TTS语音列表已更新，共{len(voices)}个语音")
        except Exception as e:
            logger.exception(f"处理Edge TTS语音列表失败: {e}")

    def on_voices_fetch_error(self, error):
        """语音列表获取失败后的处理"""
        logger.exception(f"获取Edge TTS语音列表失败: {error}")

    def __del__(self):
        """析构函数，确保worker正确终止"""
        if self.edge_tts_worker:
            self.edge_tts_worker.terminate()
            self.edge_tts_worker.wait()
            del self.edge_tts_worker


class basic_settings_volume(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("basic_voice_settings", "volume_group"))
        self.setBorderRadius(8)

        # 语速调节设置
        self.speech_rate = SpinBox()
        self.speech_rate.setFixedWidth(WIDTH_SPINBOX)
        self.speech_rate.setRange(1, 500)
        self.speech_rate.setSuffix("wpm")
        self.speech_rate.setValue(
            int(readme_settings_async("basic_voice_settings", "speech_rate"))
        )
        self.speech_rate.valueChanged.connect(
            lambda value: update_settings("basic_voice_settings", "speech_rate", value)
        )

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

        # 是否开启系统音量控制
        self.system_volume_control = SwitchButton()
        self.system_volume_control.setOffText(
            get_content_switchbutton_name_async(
                "basic_voice_settings", "system_volume_control", "disable"
            )
        )
        self.system_volume_control.setOnText(
            get_content_switchbutton_name_async(
                "basic_voice_settings", "system_volume_control", "enable"
            )
        )
        self.system_volume_control.setChecked(
            readme_settings_async("basic_voice_settings", "system_volume_control")
        )
        self.system_volume_control.checkedChanged.connect(
            lambda state: update_settings(
                "basic_voice_settings", "system_volume_control", state
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
            get_theme_icon("ic_fluent_top_speed_20_filled"),
            get_content_name_async("basic_voice_settings", "speech_rate"),
            get_content_description_async("basic_voice_settings", "speech_rate"),
            self.speech_rate,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_speaker_edit_20_filled"),
            get_content_name_async("basic_voice_settings", "volume_size"),
            get_content_description_async("basic_voice_settings", "volume_size"),
            self.volume_size,
        )
        self.addGroup(
            get_theme_icon("ic_fluent_voicemail_20_filled"),
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


class basic_settings_voice_wait(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(
            get_content_name_async("basic_voice_settings", "voice_wait_group")
        )
        self.setBorderRadius(8)

        self.voice_wait_complete_switch = SwitchButton()
        self.voice_wait_complete_switch.setChecked(
            readme_settings_async("basic_voice_settings", "voice_wait_complete")
        )
        self.voice_wait_complete_switch.checkedChanged.connect(
            lambda state: update_settings(
                "basic_voice_settings", "voice_wait_complete", state
            )
        )

        self.addGroup(
            get_theme_icon("ic_fluent_timer_20_filled"),
            get_content_name_async("basic_voice_settings", "voice_wait_complete"),
            get_content_description_async(
                "basic_voice_settings", "voice_wait_complete"
            ),
            self.voice_wait_complete_switch,
        )
