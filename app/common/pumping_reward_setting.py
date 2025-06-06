from qfluentwidgets import *
from qfluentwidgets import FluentIcon as FIF
from PyQt5.QtGui import *
from PyQt5.QtCore import *
from PyQt5.QtWidgets import *

import json
import os
from loguru import logger

from app.common.config import get_theme_icon, load_custom_font

class pumping_reward_SettinsCard(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle("抽奖设置")
        self.setBorderRadius(8)
        self.settings_file = "app/Settings/Settings.json"
        self.default_settings = {
            "extraction_scope": 0,
            "font_size": 50,
            "draw_mode": 0,
            "draw_pumping": 0,
            "animation_mode": 0,
            "voice_enabled": True,
            "reward_theme": 0,
            "list_refresh_button": True,
            "prize_pools_quantity": True,
            "refresh_button": True, 
        }

        self.pumping_reward_Draw_comboBox = ComboBox()
        self.pumping_reward_mode_Draw_comboBox = ComboBox()
        self.pumping_reward_Animation_comboBox = ComboBox()
        self.pumping_reward_theme_comboBox = ComboBox()
        self.pumping_reward_Voice_switch = SwitchButton()
        self.pumping_reward_list_refresh_button_switch = SwitchButton()
        self.pumping_reward_refresh_button_switch = SwitchButton()
        self.pumping_reward_prize_pools_quantity_switch = SwitchButton()
        
        self.pumping_reward_font_size_edit = LineEdit()
        
        # 抽取模式下拉框
        self.pumping_reward_Draw_comboBox.setFixedWidth(250)
        self.pumping_reward_Draw_comboBox.addItems(["重复抽取", "不重复抽取(直到软件重启)", "不重复抽取(直到抽完全部奖项)"])
        self.pumping_reward_Draw_comboBox.currentIndexChanged.connect(self.save_settings)
        self.pumping_reward_Draw_comboBox.setFont(QFont(load_custom_font(), 12))

        # 抽取方式下拉框
        self.pumping_reward_mode_Draw_comboBox.setFixedWidth(250)
        self.pumping_reward_mode_Draw_comboBox.addItems(["可预测抽取", "不可预测抽取"])
        self.pumping_reward_mode_Draw_comboBox.currentIndexChanged.connect(self.save_settings)
        self.pumping_reward_mode_Draw_comboBox.setFont(QFont(load_custom_font(), 12))

        # 字体大小
        self.pumping_reward_font_size_edit.setPlaceholderText("请输入字体大小 (30-200)")
        self.pumping_reward_font_size_edit.setClearButtonEnabled(True)
        # 设置宽度和高度
        self.pumping_reward_font_size_edit.setFixedWidth(150)
        self.pumping_reward_font_size_edit.setFixedHeight(32)
        # 设置字体
        self.pumping_reward_font_size_edit.setFont(QFont(load_custom_font(), 12))

        # 添加重置按钮
        reset_action = QAction(FluentIcon.SYNC.qicon(), "", triggered=self.reset_font_size)
        self.pumping_reward_font_size_edit.addAction(reset_action, QLineEdit.LeadingPosition)

        # 添加应用按钮
        apply_action = QAction(FluentIcon.SAVE.qicon(), "", triggered=self.apply_font_size)
        self.pumping_reward_font_size_edit.addAction(apply_action, QLineEdit.TrailingPosition)

        # 语音播放按钮
        self.pumping_reward_Voice_switch.setOnText("开启")
        self.pumping_reward_Voice_switch.setOffText("关闭")
        self.pumping_reward_Voice_switch.checkedChanged.connect(self.on_pumping_reward_Voice_switch_changed)
        self.pumping_reward_Voice_switch.setFont(QFont(load_custom_font(), 12))

        # 动画模式下拉框
        self.pumping_reward_Animation_comboBox.setFixedWidth(250)
        self.pumping_reward_Animation_comboBox.addItems(["手动停止动画", "自动播放完整动画", "直接显示结果"])
        self.pumping_reward_Animation_comboBox.currentIndexChanged.connect(lambda: self.save_settings())
        self.pumping_reward_Animation_comboBox.setFont(QFont(load_custom_font(), 12))

        # 奖品量样式下拉框
        self.pumping_reward_theme_comboBox.setFixedWidth(150)
        self.pumping_reward_theme_comboBox.addItems(["总数 | 剩余", "总数", "剩余", "不显示"])
        self.pumping_reward_theme_comboBox.currentIndexChanged.connect(self.save_settings)
        self.pumping_reward_theme_comboBox.setFont(QFont(load_custom_font(), 12))

        # 重置记录显隐
        self.pumping_reward_list_refresh_button_switch.setOnText("显示")
        self.pumping_reward_list_refresh_button_switch.setOffText("隐藏")
        self.pumping_reward_list_refresh_button_switch.checkedChanged.connect(self.on_pumping_reward_Voice_switch_changed)
        self.pumping_reward_list_refresh_button_switch.setFont(QFont(load_custom_font(), 12))

        # 刷新列表显隐
        self.pumping_reward_refresh_button_switch.setOnText("显示")
        self.pumping_reward_refresh_button_switch.setOffText("隐藏")
        self.pumping_reward_refresh_button_switch.checkedChanged.connect(self.on_pumping_reward_Voice_switch_changed)
        self.pumping_reward_refresh_button_switch.setFont(QFont(load_custom_font(), 12))

        # 便捷修改奖池功能显示显隐
        self.pumping_reward_prize_pools_quantity_switch.setOnText("显示")
        self.pumping_reward_prize_pools_quantity_switch.setOffText("隐藏")
        self.pumping_reward_prize_pools_quantity_switch.checkedChanged.connect(self.on_pumping_reward_Voice_switch_changed)
        self.pumping_reward_prize_pools_quantity_switch.setFont(QFont(load_custom_font(), 12))

        # 添加组件到分组中
        self.addGroup(get_theme_icon("ic_fluent_arrow_sync_20_filled"), "抽取模式", "设置抽取模式", self.pumping_reward_Draw_comboBox)
        self.addGroup(get_theme_icon("ic_fluent_arrow_sync_20_filled"), "抽取方式", "设置抽取方式", self.pumping_reward_mode_Draw_comboBox)
        self.addGroup(get_theme_icon("ic_fluent_text_font_size_20_filled"), "字体大小", "设置抽取结果的字体大小", self.pumping_reward_font_size_edit)
        self.addGroup(get_theme_icon("ic_fluent_person_feedback_20_filled"), "语音播放", "设置结果公布时是否播放语音", self.pumping_reward_Voice_switch)
        self.addGroup(get_theme_icon("ic_fluent_calendar_video_20_filled"), "动画模式", "设置抽取时的动画播放方式", self.pumping_reward_Animation_comboBox)
        self.addGroup(get_theme_icon("ic_fluent_people_eye_20_filled"), "奖品量", "设置该功能的显示格式", self.pumping_reward_theme_comboBox)
        self.addGroup(get_theme_icon("ic_fluent_people_eye_20_filled"), "重置记录", "设置该功能是否显示(重启生效)", self.pumping_reward_list_refresh_button_switch)
        self.addGroup(get_theme_icon("ic_fluent_apps_list_20_filled"), "刷新列表", "设置该功能是否显示(重启生效)", self.pumping_reward_refresh_button_switch)
        self.addGroup(get_theme_icon("ic_fluent_convert_range_20_filled"), "切换奖池", "设置该功能是否显示(重启生效)", self.pumping_reward_prize_pools_quantity_switch)

        self.load_settings()  # 加载设置
        self.save_settings()  # 保存设置

    def on_pumping_reward_Voice_switch_changed(self, checked):
        self.save_settings()

    def apply_font_size(self):
        try:
            font_size = int(self.pumping_reward_font_size_edit.text())
            if 30 <= font_size <= 200:
                self.pumping_reward_font_size_edit.setText(str(font_size))
                self.save_settings()
                InfoBar.success(
                    title='设置成功',
                    content=f"设置字体大小为: {font_size}",
                    orient=Qt.Horizontal,
                    parent=self,
                    isClosable=True,
                    duration=3000,
                    position=InfoBarPosition.TOP
                )
            else:
                logger.warning(f"字体大小超出范围: {font_size}")
                InfoBar.warning(
                    title='字体大小超出范围',
                    content=f"字体大小超出范围，请输入30-200之间的整数: {font_size}",
                    orient=Qt.Horizontal,
                    parent=self,
                    isClosable=True,
                    duration=3000,
                    position=InfoBarPosition.TOP
                )
        except ValueError:
            logger.warning(f"无效的字体大小输入: {self.pumping_reward_font_size_edit.text()}")
            InfoBar.warning(
                title='无效的字体大小输入',
                content=f"无效的字体大小输入(需要是整数)：{self.pumping_reward_font_size_edit.text()}",
                orient=Qt.Horizontal,
                parent=self,
                isClosable=True,
                duration=3000,
                position=InfoBarPosition.TOP
            )

    def reset_font_size(self):
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                foundation_settings = settings.get('foundation', {})
                main_window_size = foundation_settings.get('main_window_size', 0)
                if main_window_size == 0:
                    self.pumping_reward_font_size_edit.setText(str("50"))
                elif main_window_size == 1:
                    self.pumping_reward_font_size_edit.setText(str("85"))
                else:
                    self.pumping_reward_font_size_edit.setText(str("50"))
        except FileNotFoundError as e:
            logger.error(f"加载设置时出错: {e}, 使用默认大小:50")
            self.pumping_reward_font_size_edit.setText(str("50"))
        except KeyError:
            logger.error(f"设置文件中缺少'foundation'键, 使用默认大小:50")
            self.pumping_reward_font_size_edit.setText(str("50"))
        self.save_settings()
        self.load_settings()
        
    def load_settings(self):
        try:
            if os.path.exists(self.settings_file):
                with open(self.settings_file, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    pumping_reward_settings = settings.get("pumping_reward", {})

                    font_size = pumping_reward_settings.get("font_size", self.default_settings["font_size"])
                    
                    # 直接使用索引值
                    draw_mode = pumping_reward_settings.get("draw_mode", self.default_settings["draw_mode"])
                    if draw_mode < 0 or draw_mode >= self.pumping_reward_Draw_comboBox.count():
                        logger.warning(f"无效的抽取模式索引: {draw_mode}")
                        draw_mode = self.default_settings["draw_mode"]

                    draw_pumping = pumping_reward_settings.get("draw_pumping", self.default_settings["draw_pumping"])
                    if draw_pumping < 0 or draw_pumping >= self.pumping_reward_mode_Draw_comboBox.count():
                        logger.warning(f"无效的抽取方式索引: {draw_pumping}")
                        draw_pumping = self.default_settings["draw_pumping"]
                        
                    animation_mode = pumping_reward_settings.get("animation_mode", self.default_settings["animation_mode"])
                    if animation_mode < 0 or animation_mode >= self.pumping_reward_Animation_comboBox.count():
                        logger.warning(f"无效的动画模式索引: {animation_mode}")
                        animation_mode = self.default_settings["animation_mode"]
                        
                    voice_enabled = pumping_reward_settings.get("voice_enabled", self.default_settings["voice_enabled"])

                    reward_theme = pumping_reward_settings.get("reward_theme", self.default_settings["reward_theme"])
                    if reward_theme < 0 or reward_theme >= self.pumping_reward_theme_comboBox.count():
                        logger.warning(f"无效的人数/组数样式索引: {reward_theme}")
                        reward_theme = self.default_settings["reward_theme"]

                    prize_pools_quantity = pumping_reward_settings.get("prize_pools_quantity", self.default_settings["prize_pools_quantity"])
                    refresh_button = pumping_reward_settings.get("refresh_button", self.default_settings["refresh_button"])
                    list_refresh_button = pumping_reward_settings.get("list_refresh_button", self.default_settings["list_refresh_button"])
                    
                    self.pumping_reward_Draw_comboBox.setCurrentIndex(draw_mode)
                    self.pumping_reward_mode_Draw_comboBox.setCurrentIndex(draw_pumping)
                    self.pumping_reward_font_size_edit.setText(str(font_size))
                    self.pumping_reward_Animation_comboBox.setCurrentIndex(animation_mode)
                    self.pumping_reward_Voice_switch.setChecked(voice_enabled)
                    self.pumping_reward_theme_comboBox.setCurrentIndex(reward_theme)
                    self.pumping_reward_list_refresh_button_switch.setChecked(list_refresh_button)
                    self.pumping_reward_prize_pools_quantity_switch.setChecked(prize_pools_quantity)
                    self.pumping_reward_refresh_button_switch.setChecked(refresh_button)
                    logger.info(f"加载抽奖设置完成")
            else:
                self.pumping_reward_Draw_comboBox.setCurrentIndex(self.default_settings["draw_mode"])
                self.pumping_reward_mode_Draw_comboBox.setCurrentIndex(self.default_settings["draw_pumping"])
                self.pumping_reward_font_size_edit.setText(str(self.default_settings["font_size"]))
                self.pumping_reward_Animation_comboBox.setCurrentIndex(self.default_settings["animation_mode"])
                self.pumping_reward_Voice_switch.setChecked(self.default_settings["voice_enabled"])
                self.pumping_reward_theme_comboBox.setCurrentIndex(self.default_settings["reward_theme"])
                self.pumping_reward_list_refresh_button_switch.setChecked(self.default_settings["list_refresh_button"]) 
                self.pumping_reward_prize_pools_quantity_switch.setChecked(self.default_settings["prize_pools_quantity"])
                self.pumping_reward_refresh_button_switch.setChecked(self.default_settings["refresh_button"])
                self.save_settings()
        except Exception as e:
            logger.error(f"加载设置时出错: {e}")
            self.pumping_reward_Draw_comboBox.setCurrentIndex(self.default_settings["draw_mode"])
            self.pumping_reward_mode_Draw_comboBox.setCurrentIndex(self.default_settings["draw_pumping"])
            self.pumping_reward_font_size_edit.setText(str(self.default_settings["font_size"]))
            self.pumping_reward_Animation_comboBox.setCurrentIndex(self.default_settings["animation_mode"])
            self.pumping_reward_Voice_switch.setChecked(self.default_settings["voice_enabled"])
            self.pumping_reward_theme_comboBox.setCurrentIndex(self.default_settings["reward_theme"])
            self.pumping_reward_list_refresh_button_switch.setChecked(self.default_settings["list_refresh_button"])
            self.pumping_reward_prize_pools_quantity_switch.setChecked(self.default_settings["prize_pools_quantity"])
            self.pumping_reward_refresh_button_switch.setChecked(self.default_settings["refresh_button"])
            self.save_settings()
    
    def save_settings(self):
        # 先读取现有设置
        existing_settings = {}
        if os.path.exists(self.settings_file):
            with open(self.settings_file, 'r', encoding='utf-8') as f:
                try:
                    existing_settings = json.load(f)
                except json.JSONDecodeError:
                    existing_settings = {}
        
        # 更新pumping_reward部分的所有设置
        if "pumping_reward" not in existing_settings:
            existing_settings["pumping_reward"] = {}
            
        pumping_reward_settings = existing_settings["pumping_reward"]
        # 只保存索引值
        pumping_reward_settings["draw_mode"] = self.pumping_reward_Draw_comboBox.currentIndex()
        pumping_reward_settings["draw_pumping"] = self.pumping_reward_mode_Draw_comboBox.currentIndex()
        pumping_reward_settings["animation_mode"] = self.pumping_reward_Animation_comboBox.currentIndex()
        pumping_reward_settings["voice_enabled"] = self.pumping_reward_Voice_switch.isChecked()
        pumping_reward_settings["reward_theme"] = self.pumping_reward_theme_comboBox.currentIndex()
        pumping_reward_settings["list_refresh_button"] = self.pumping_reward_list_refresh_button_switch.isChecked()
        pumping_reward_settings["prize_pools_quantity"] = self.pumping_reward_prize_pools_quantity_switch.isChecked()
        pumping_reward_settings["refresh_button"] = self.pumping_reward_refresh_button_switch.isChecked()

        # 保存字体大小
        try:
            font_size = int(self.pumping_reward_font_size_edit.text())
            if 30 <= font_size <= 200:
                pumping_reward_settings["font_size"] = font_size
            else:
                logger.warning(f"字体大小超出范围: {font_size}")
                InfoBar.warning(
                    title='字体大小超出范围',
                    content=f"字体大小超出范围，请输入30-200之间的整数: {font_size}",
                    orient=Qt.Horizontal,
                    parent=self,
                    isClosable=True,
                    duration=3000,
                    position=InfoBarPosition.TOP
                )
        except ValueError:
            logger.warning(f"无效的字体大小输入: {self.pumping_reward_font_size_edit.text()}")
            InfoBar.warning(
                title='无效的字体大小输入',
                content=f"无效的字体大小输入(需要是整数)：{self.pumping_reward_font_size_edit.text()}",
                orient=Qt.Horizontal,
                parent=self,
                isClosable=True,
                duration=3000,
                position=InfoBarPosition.TOP
            )
        
        os.makedirs(os.path.dirname(self.settings_file), exist_ok=True)
        with open(self.settings_file, 'w', encoding='utf-8') as f:
            json.dump(existing_settings, f, indent=4)
