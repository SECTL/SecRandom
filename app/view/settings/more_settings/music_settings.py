# ==================================================
# 导入库
# ==================================================

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
# 音乐设置
# ==================================================
class music_settings(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 创建垂直布局
        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.vBoxLayout.setSpacing(10)

        # 添加音乐管理组件
        self.music_management_widget = music_management(self)
        self.vBoxLayout.addWidget(self.music_management_widget)

        # 添加循环播放背景音乐设置
        self.music_loop_setting_widget = music_loop_setting(self)
        self.vBoxLayout.addWidget(self.music_loop_setting_widget)

        # 添加音乐设置表格组件
        self.music_settings_table_widget = music_settings_table(self)
        self.vBoxLayout.addWidget(self.music_settings_table_widget)


class music_management(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("music_settings", "title"))
        self.setBorderRadius(8)

        # 创建水平布局
        self.hBoxLayout = QHBoxLayout()
        self.hBoxLayout.setContentsMargins(0, 0, 0, 0)
        self.hBoxLayout.setSpacing(10)

        # 创建导入音乐按钮
        self.import_music_button = PushButton(
            get_content_name_async("music_settings", "import_music")
        )
        self.import_music_button.clicked.connect(self.import_music)
        self.hBoxLayout.addWidget(self.import_music_button)

        # 创建水平分割线
        self.hBoxLayout.addWidget(HorizontalSeparator())

        # 创建音乐文件选择区域
        self.music_file_layout = QHBoxLayout()
        self.music_file_layout.setSpacing(10)

        # 创建音乐文件下拉框
        self.music_file_combo = ComboBox()
        self.music_file_combo.setFixedWidth(200)
        self.music_file_combo.setPlaceholderText(
            get_content_name_async("music_settings", "select_music_file")
        )
        self.music_file_layout.addWidget(self.music_file_combo)

        # 创建删除按钮
        self.delete_music_button = PushButton(
            get_content_name_async("music_settings", "delete_music")
        )
        self.delete_music_button.setFixedWidth(100)
        self.delete_music_button.setEnabled(False)
        self.delete_music_button.clicked.connect(self.delete_music)
        self.music_file_layout.addWidget(self.delete_music_button)

        # 将音乐文件选择区域添加到主布局
        self.hBoxLayout.addLayout(self.music_file_layout)

        # 添加伸缩空间
        self.hBoxLayout.addStretch(1)

        # 将布局添加到卡片
        self.layout().addLayout(self.hBoxLayout)

        # 初始化音乐文件列表
        self.refresh_music_files()

        # 连接信号
        self.music_file_combo.currentIndexChanged.connect(self.on_music_file_changed)

        # 设置文件系统监视器，监听音乐文件夹变化
        self.setup_file_watcher()

    def import_music(self):
        """导入音乐文件"""
        # 打开文件选择对话框
        file_dialog = QFileDialog()
        file_dialog.setFileMode(QFileDialog.FileMode.ExistingFiles)
        file_dialog.setNameFilter("Audio Files (*.mp3 *.flac *.wav *.ogg)")
        file_dialog.setWindowTitle(
            get_content_name_async("music_settings", "import_music")
        )

        if file_dialog.exec():
            selected_files = file_dialog.selectedFiles()
            # 获取音频文件目录
            audio_dir = get_audio_path("music")
            # 确保目录存在
            ensure_dir(audio_dir)

            # 复制选中的文件到音频目录
            for file_path in selected_files:
                src_file = get_path(file_path)
                dst_file = audio_dir / src_file.name

                # 如果文件已存在，跳过或覆盖
                if not dst_file.exists():
                    try:
                        import shutil

                        shutil.copy2(src_file, dst_file)
                    except Exception as e:
                        logger.exception(
                            f"导入音乐文件失败: {src_file.name}, 错误: {e}"
                        )

            # 刷新音乐文件列表
            self.refresh_music_files()
            # 更新表格中的音乐文件下拉框
            if hasattr(self.parent(), "music_settings_table_widget"):
                self.parent().music_settings_table_widget.refresh_music_files()

    def delete_music(self):
        """删除选中的音乐文件"""
        current_text = self.music_file_combo.currentText()
        if current_text:
            # 获取音频文件路径
            audio_file_path = get_audio_path(f"music/{current_text}")

            # 删除文件
            if audio_file_path.exists():
                try:
                    remove_file(audio_file_path)
                except Exception as e:
                    logger.error(f"删除音乐文件失败: {current_text}, 错误: {e}")

            # 刷新音乐文件列表
            self.refresh_music_files()
            # 更新表格中的音乐文件下拉框
            if hasattr(self.parent(), "music_settings_table_widget"):
                self.parent().music_settings_table_widget.refresh_music_files()

    def refresh_music_files(self):
        """刷新音乐文件列表"""
        # 清空下拉框
        self.music_file_combo.clear()

        # 获取音频文件目录
        audio_dir = get_audio_path("music")
        # 确保目录存在
        ensure_dir(audio_dir)

        # 获取音频文件列表
        music_files = []
        if audio_dir.exists():
            # 支持的音频格式
            supported_formats = [".mp3", ".flac", ".wav", ".ogg"]
            # 遍历目录获取所有支持的音频文件
            for file in audio_dir.iterdir():
                if file.is_file() and file.suffix.lower() in supported_formats:
                    music_files.append(file.name)

        # 添加到下拉框
        self.music_file_combo.addItems(music_files)

        # 更新删除按钮状态
        self.delete_music_button.setEnabled(len(music_files) > 0)

    def on_music_file_changed(self, index):
        """当音乐文件选择变化时"""
        self.delete_music_button.setEnabled(index >= 0)

    def setup_file_watcher(self):
        """设置文件系统监视器，监听音乐文件夹变化"""
        self.file_watcher = QFileSystemWatcher()
        # 获取音频文件目录
        audio_dir = get_audio_path("music")
        # 确保目录存在
        ensure_dir(audio_dir)
        # 添加目录到监视器
        self.file_watcher.addPath(str(audio_dir))
        # 连接目录变化信号
        self.file_watcher.directoryChanged.connect(self.on_directory_changed)

    def on_directory_changed(self, path):
        """当音乐文件夹内容变化时的处理"""
        # 延迟刷新，避免文件操作未完成时就刷新
        QTimer.singleShot(500, self.refresh_music_files)
        # 更新表格中的音乐文件下拉框
        if hasattr(self.parent(), "music_settings_table_widget"):
            QTimer.singleShot(
                500,
                lambda: self.parent().music_settings_table_widget.refresh_music_files(),
            )


class music_loop_setting(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("music_settings", "background_music_loop"))
        self.setBorderRadius(8)

        self.background_music_loop_switch = SwitchButton()
        self.background_music_loop_switch.setOffText(
            get_content_switchbutton_name_async(
                "music_settings", "background_music_loop", "disable"
            )
        )
        self.background_music_loop_switch.setOnText(
            get_content_switchbutton_name_async(
                "music_settings", "background_music_loop", "enable"
            )
        )
        _loop = readme_settings_async("music_settings", "background_music_loop")
        self.background_music_loop_switch.setChecked(True if _loop is None else _loop)
        self.background_music_loop_switch.checkedChanged.connect(
            self.__on_background_music_loop_changed
        )

        self.addGroup(
            get_theme_icon("ic_fluent_arrow_repeat_all_20_filled"),
            get_content_name_async("music_settings", "background_music_loop"),
            get_content_description_async("music_settings", "background_music_loop"),
            self.background_music_loop_switch,
        )

    def __on_background_music_loop_changed(self, checked):
        update_settings("music_settings", "background_music_loop", checked)


class music_settings_table(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle(get_content_name_async("music_settings", "table_title"))
        self.setBorderRadius(8)

        # 创建表格
        self.create_table()

        # 初始化表格数据
        self.init_table_data()

    def create_table(self):
        """创建表格"""
        # 创建表格
        self.table = TableWidget()
        self.table.setBorderVisible(True)
        self.table.setBorderRadius(8)
        self.table.setWordWrap(False)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.verticalHeader().hide()

        # 设置表格列
        headers = [
            get_content_name_async("music_settings", "application_position"),
            get_content_name_async("music_settings", "process_music"),
            get_content_name_async("music_settings", "fade_in_duration"),
            get_content_name_async("music_settings", "fade_out_duration"),
            get_content_name_async("music_settings", "result_music"),
            get_content_name_async("music_settings", "result_fade_in_duration"),
            get_content_name_async("music_settings", "result_fade_out_duration"),
            get_content_name_async("music_settings", "volume"),
        ]
        self.table.setColumnCount(len(headers))
        self.table.setHorizontalHeaderLabels(headers)

        # 设置表格属性
        for i in range(self.table.columnCount()):
            self.table.horizontalHeader().setSectionResizeMode(
                i, QHeaderView.ResizeMode.Stretch
            )
            self.table.horizontalHeader().setDefaultAlignment(
                Qt.AlignmentFlag.AlignCenter
            )

        # 将表格添加到布局
        self.layout().addWidget(self.table)

    def get_music_files(self):
        """获取音乐文件列表"""
        from app.common.music.music_player import get_music_files

        return get_music_files()

    def init_table_data(self):
        """初始化表格数据"""
        # 应用位置配置映射
        self.application_configs = [
            {
                "name": get_content_name_async("music_settings", "roll_call"),
                "config_key": "roll_call_settings",
            },
            {
                "name": get_content_name_async("music_settings", "quick_draw"),
                "config_key": "quick_draw_settings",
            },
            {
                "name": get_content_name_async("music_settings", "lottery"),
                "config_key": "lottery_settings",
            },
        ]

        # 设置表格行数
        self.table.setRowCount(len(self.application_configs))

        # 获取音乐文件列表
        music_files = self.get_music_files()

        # 填充表格数据
        for row, config in enumerate(self.application_configs):
            # 应用位置
            position_item = QTableWidgetItem(config["name"])
            position_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.table.setItem(row, 0, position_item)

            # 过程音乐 - 下拉框
            process_music_combo = ComboBox()
            process_music_combo.addItems(music_files)
            # 从设置中加载数据
            process_music = readme_settings_async(
                config["config_key"], "animation_music"
            )
            if process_music:
                # 如果设置了音乐文件，则设置为当前选择
                if process_music in music_files:
                    process_music_combo.setCurrentText(process_music)
                else:
                    # 如果音乐文件不存在，则选择"无音乐"
                    process_music_combo.setCurrentText(
                        get_content_name_async("music_settings", "no_music")
                    )
            else:
                # 如果没有设置音乐，则选择"无音乐"
                process_music_combo.setCurrentText(
                    get_content_name_async("music_settings", "no_music")
                )
            # 连接信号
            process_music_combo.currentIndexChanged.connect(
                lambda index, key=config["config_key"]: self.update_process_music(
                    key, index
                )
            )
            self.table.setCellWidget(row, 1, process_music_combo)

            # 渐入时长 - CompactSpinBox
            fade_in_spin = CompactSpinBox()
            fade_in_spin.setRange(0, 5000)
            # 从设置中加载数据
            fade_in_duration = readme_settings_async(
                config["config_key"], "animation_music_fade_in"
            )
            fade_in_spin.setValue(fade_in_duration)
            fade_in_spin.setAlignment(Qt.AlignmentFlag.AlignCenter)
            # 连接信号
            fade_in_spin.valueChanged.connect(
                lambda value, key=config["config_key"]: self.update_fade_in_duration(
                    key, value
                )
            )
            self.table.setCellWidget(row, 2, fade_in_spin)

            # 渐出时长 - CompactSpinBox
            fade_out_spin = CompactSpinBox()
            fade_out_spin.setRange(0, 5000)
            # 从设置中加载数据
            fade_out_duration = readme_settings_async(
                config["config_key"], "animation_music_fade_out"
            )
            fade_out_spin.setValue(fade_out_duration)
            fade_out_spin.setAlignment(Qt.AlignmentFlag.AlignCenter)
            # 连接信号
            fade_out_spin.valueChanged.connect(
                lambda value, key=config["config_key"]: self.update_fade_out_duration(
                    key, value
                )
            )
            self.table.setCellWidget(row, 3, fade_out_spin)

            # 结果音乐 - 下拉框
            result_music_combo = ComboBox()
            result_music_combo.addItems(music_files)
            # 从设置中加载数据
            result_music = readme_settings_async(config["config_key"], "result_music")
            if result_music:
                # 如果设置了音乐文件，则设置为当前选择
                if result_music in music_files:
                    result_music_combo.setCurrentText(result_music)
                else:
                    # 如果音乐文件不存在，则选择"无音乐"
                    result_music_combo.setCurrentText(
                        get_content_name_async("music_settings", "no_music")
                    )
            else:
                # 如果没有设置音乐，则选择"无音乐"
                result_music_combo.setCurrentText(
                    get_content_name_async("music_settings", "no_music")
                )
            # 连接信号
            result_music_combo.currentIndexChanged.connect(
                lambda index, key=config["config_key"]: self.update_result_music(
                    key, index
                )
            )
            self.table.setCellWidget(row, 4, result_music_combo)

            # 结果渐入时长 - CompactSpinBox
            result_fade_in_spin = CompactSpinBox()
            result_fade_in_spin.setRange(0, 5000)
            # 从设置中加载数据
            result_fade_in_duration = readme_settings_async(
                config["config_key"], "result_music_fade_in"
            )
            result_fade_in_spin.setValue(result_fade_in_duration)
            result_fade_in_spin.setAlignment(Qt.AlignmentFlag.AlignCenter)
            # 连接信号
            result_fade_in_spin.valueChanged.connect(
                lambda value,
                key=config["config_key"]: self.update_result_fade_in_duration(
                    key, value
                )
            )
            self.table.setCellWidget(row, 5, result_fade_in_spin)

            # 结果渐出时长 - CompactSpinBox
            result_fade_out_spin = CompactSpinBox()
            result_fade_out_spin.setRange(0, 5000)
            # 从设置中加载数据
            result_fade_out_duration = readme_settings_async(
                config["config_key"], "result_music_fade_out"
            )
            result_fade_out_spin.setValue(result_fade_out_duration)
            result_fade_out_spin.setAlignment(Qt.AlignmentFlag.AlignCenter)
            # 连接信号
            result_fade_out_spin.valueChanged.connect(
                lambda value,
                key=config["config_key"]: self.update_result_fade_out_duration(
                    key, value
                )
            )
            self.table.setCellWidget(row, 6, result_fade_out_spin)

            # 音量 - CompactSpinBox
            volume_spin = CompactSpinBox()
            volume_spin.setRange(0, 100)
            # 从设置中加载数据
            volume = readme_settings_async(
                config["config_key"], "animation_music_volume", 100
            )
            volume_spin.setValue(volume)
            volume_spin.setAlignment(Qt.AlignmentFlag.AlignCenter)
            # 连接信号
            volume_spin.valueChanged.connect(
                lambda value, key=config["config_key"]: self.update_volume(key, value)
            )
            self.table.setCellWidget(row, 7, volume_spin)

    def refresh_music_files(self):
        """刷新表格中的音乐文件列表"""
        # 获取最新的音乐文件列表
        music_files = self.get_music_files()

        # 更新表格中所有的音乐下拉框
        for row in range(self.table.rowCount()):
            # 更新过程音乐下拉框
            process_music_combo = self.table.cellWidget(row, 1)
            if isinstance(process_music_combo, ComboBox):
                # 保存当前选择的音乐文件
                current_music = process_music_combo.currentText()
                # 清空并重新添加音乐文件列表
                process_music_combo.clear()
                process_music_combo.addItems(music_files)
                # 恢复之前的选择
                if current_music in music_files:
                    process_music_combo.setCurrentText(current_music)
                else:
                    process_music_combo.setCurrentIndex(0)

            # 更新结果音乐下拉框
            result_music_combo = self.table.cellWidget(row, 4)
            if isinstance(result_music_combo, ComboBox):
                # 保存当前选择的音乐文件
                current_music = result_music_combo.currentText()
                # 清空并重新添加音乐文件列表
                result_music_combo.clear()
                result_music_combo.addItems(music_files)
                # 恢复之前的选择
                if current_music in music_files:
                    result_music_combo.setCurrentText(current_music)
                else:
                    result_music_combo.setCurrentIndex(0)

    def update_process_music(self, config_key, index):
        """更新过程音乐设置"""
        # 获取当前选择的音乐文件名
        process_music_combo = None
        for row in range(self.table.rowCount()):
            # 找到对应的配置行
            row_config_key = self.application_configs[row]["config_key"]
            if row_config_key == config_key:
                process_music_combo = self.table.cellWidget(row, 1)
                break

        if process_music_combo and isinstance(process_music_combo, ComboBox):
            music_file = process_music_combo.currentText()
            # 如果选择了"无音乐"，则保存空字符串
            if music_file == get_content_name_async("music_settings", "no_music"):
                music_file = ""
            update_settings(config_key, "animation_music", music_file)

    def update_fade_in_duration(self, config_key, value):
        """更新渐入时长设置"""
        update_settings(config_key, "animation_music_fade_in", value)

    def update_fade_out_duration(self, config_key, value):
        """更新渐出时长设置"""
        update_settings(config_key, "animation_music_fade_out", value)

    def update_volume(self, config_key, value):
        """更新音量设置"""
        update_settings(config_key, "animation_music_volume", value)

    def update_result_music(self, config_key, index):
        """更新结果音乐设置"""
        # 获取当前选择的音乐文件名
        result_music_combo = None
        for row in range(self.table.rowCount()):
            # 找到对应的配置行
            row_config_key = self.application_configs[row]["config_key"]
            if row_config_key == config_key:
                result_music_combo = self.table.cellWidget(row, 4)
                break

        if result_music_combo and isinstance(result_music_combo, ComboBox):
            music_file = result_music_combo.currentText()
            # 如果选择了"无音乐"，则保存空字符串
            if music_file == get_content_name_async("music_settings", "no_music"):
                music_file = ""
            update_settings(config_key, "result_music", music_file)

    def update_result_fade_in_duration(self, config_key, value):
        """更新结果渐入时长设置"""
        update_settings(config_key, "result_music_fade_in", value)

    def update_result_fade_out_duration(self, config_key, value):
        """更新结果渐出时长设置"""
        update_settings(config_key, "result_music_fade_out", value)
