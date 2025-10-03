from shlex import join
from qfluentwidgets import *
from qfluentwidgets import FluentIcon as FIF
from PyQt5.QtGui import *
from PyQt5.QtWidgets import *
from PyQt5.QtCore import *

import json
import os
import sys
import platform
from pathlib import Path
from datetime import datetime
from loguru import logger
import winreg
from app.common.config import get_theme_icon, load_custom_font, is_dark_theme, VERSION
from app.common.path_utils import path_manager
from app.common.path_utils import open_file, ensure_dir

is_dark = is_dark_theme(qconfig)

class fixed_url_SettinsCard(GroupHeaderCardWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTitle("固定Url管理")
        self.setBorderRadius(8)
        self.settings_file = path_manager.get_settings_path('custom_settings.json')
        self.default_settings = {
            "enable_main_url": True,
            "enable_settings_url": True,
            "enable_pumping_url": True,
            "enable_reward_url": True,
            "enable_history_url": True,
            "enable_floating_url": True,
            "enable_about_url": True,
            "enable_direct_extraction_url": True,
            "enable_plugin_settings_url": True,
            # Action参数URL设置
            "enable_pumping_action_url": True,
            "enable_reward_action_url": True,
            "enable_about_action_url": True,
            "enable_plugin_settings_action_url": True,
            # 点名Action参数URL独立开关
            "enable_pumping_start_url": True,
            "enable_pumping_stop_url": True,
            "enable_pumping_reset_url": True,
            # 抽奖Action参数URL独立开关
            "enable_reward_start_url": True,
            "enable_reward_stop_url": True,
            "enable_reward_reset_url": True,
            # 关于界面Action参数URL独立开关
            "enable_about_donation_url": True,
            "enable_about_contributor_url": True,
            # 插件设置Action参数URL独立开关
            "enable_plugin_settings_open_url": True,
            # 弹窗提醒设置(0=disabled, 1=notify_only, 2=confirm, 3=confirm_with_security)
            "main_url_notification": 0,
            "settings_url_notification": 0,
            "pumping_url_notification": 0,
            "reward_url_notification": 0,
            "history_url_notification": 0,
            "floating_url_notification": 0,
            "about_url_notification": 0,
            "direct_extraction_url_notification": 0,
            "plugin_settings_url_notification": 0,
            "pumping_start_url_notification": 0,
            "pumping_stop_url_notification": 0,
            "pumping_reset_url_notification": 0,
            "reward_start_url_notification": 0,
            "reward_stop_url_notification": 0,
            "reward_reset_url_notification": 0,
            "about_donation_url_notification": 0,
            "about_contributor_url_notification": 0,
            "plugin_settings_open_url_notification": 0,
            # 跳过安全验证设置
            "settings_url_skip_security": False,
            "floating_url_skip_security": False,
            "plugin_settings_open_url_skip_security": False,
        }
        
        # 创建主界面URL开关
        self.main_url_switch = SwitchButton()
        self.main_url_switch.setOnText("开启")
        self.main_url_switch.setOffText("关闭")
        self.main_url_switch.setFont(QFont(load_custom_font(), 12))
        self.main_url_switch.checkedChanged.connect(self.save_settings)
        
        # 创建设置界面URL开关
        self.settings_url_switch = SwitchButton()
        self.settings_url_switch.setOnText("开启")
        self.settings_url_switch.setOffText("关闭")
        self.settings_url_switch.setFont(QFont(load_custom_font(), 12))
        self.settings_url_switch.checkedChanged.connect(self.save_settings)
        
        # 创建点名界面URL开关
        self.pumping_url_switch = SwitchButton()
        self.pumping_url_switch.setOnText("开启")
        self.pumping_url_switch.setOffText("关闭")
        self.pumping_url_switch.setFont(QFont(load_custom_font(), 12))
        self.pumping_url_switch.checkedChanged.connect(self.on_pumping_url_changed)
        
        # 创建抽奖界面URL开关
        self.reward_url_switch = SwitchButton()
        self.reward_url_switch.setOnText("开启")
        self.reward_url_switch.setOffText("关闭")
        self.reward_url_switch.setFont(QFont(load_custom_font(), 12))
        self.reward_url_switch.checkedChanged.connect(self.on_reward_url_changed)
        
        # 创建历史记录界面URL开关
        self.history_url_switch = SwitchButton()
        self.history_url_switch.setOnText("开启")
        self.history_url_switch.setOffText("关闭")
        self.history_url_switch.setFont(QFont(load_custom_font(), 12))
        self.history_url_switch.checkedChanged.connect(self.save_settings)
        
        # 创建浮窗界面URL开关
        self.floating_url_switch = SwitchButton()
        self.floating_url_switch.setOnText("开启")
        self.floating_url_switch.setOffText("关闭")
        self.floating_url_switch.setFont(QFont(load_custom_font(), 12))
        self.floating_url_switch.checkedChanged.connect(self.save_settings)
        
        # 创建关于界面URL开关
        self.about_url_switch = SwitchButton()
        self.about_url_switch.setOnText("开启")
        self.about_url_switch.setOffText("关闭")
        self.about_url_switch.setFont(QFont(load_custom_font(), 12))
        self.about_url_switch.checkedChanged.connect(self.on_about_url_changed)
        
        # 创建闪抽界面URL开关
        self.direct_extraction_url_switch = SwitchButton()
        self.direct_extraction_url_switch.setOnText("开启")
        self.direct_extraction_url_switch.setOffText("关闭")
        self.direct_extraction_url_switch.setFont(QFont(load_custom_font(), 12))
        self.direct_extraction_url_switch.checkedChanged.connect(self.save_settings)

        # 创建Action参数URL开关
        # 点名Action参数URL开关
        self.pumping_action_url_switch = SwitchButton()
        self.pumping_action_url_switch.setOnText("开启")
        self.pumping_action_url_switch.setOffText("关闭")
        self.pumping_action_url_switch.setFont(QFont(load_custom_font(), 12))
        self.pumping_action_url_switch.checkedChanged.connect(self.save_settings)
        
        # 点名Action参数URL独立开关
        self.pumping_start_url_switch = SwitchButton()
        self.pumping_start_url_switch.setOnText("开启")
        self.pumping_start_url_switch.setOffText("关闭")
        self.pumping_start_url_switch.setFont(QFont(load_custom_font(), 12))
        self.pumping_start_url_switch.checkedChanged.connect(self.save_settings)
        
        self.pumping_stop_url_switch = SwitchButton()
        self.pumping_stop_url_switch.setOnText("开启")
        self.pumping_stop_url_switch.setOffText("关闭")
        self.pumping_stop_url_switch.setFont(QFont(load_custom_font(), 12))
        self.pumping_stop_url_switch.checkedChanged.connect(self.save_settings)
        
        self.pumping_reset_url_switch = SwitchButton()
        self.pumping_reset_url_switch.setOnText("开启")
        self.pumping_reset_url_switch.setOffText("关闭")
        self.pumping_reset_url_switch.setFont(QFont(load_custom_font(), 12))
        self.pumping_reset_url_switch.checkedChanged.connect(self.save_settings)
        
        # 抽奖Action参数URL开关
        self.reward_action_url_switch = SwitchButton()
        self.reward_action_url_switch.setOnText("开启")
        self.reward_action_url_switch.setOffText("关闭")
        self.reward_action_url_switch.setFont(QFont(load_custom_font(), 12))
        self.reward_action_url_switch.checkedChanged.connect(self.save_settings)
        
        # 抽奖Action参数URL独立开关
        self.reward_start_url_switch = SwitchButton()
        self.reward_start_url_switch.setOnText("开启")
        self.reward_start_url_switch.setOffText("关闭")
        self.reward_start_url_switch.setFont(QFont(load_custom_font(), 12))
        self.reward_start_url_switch.checkedChanged.connect(self.save_settings)
        
        self.reward_stop_url_switch = SwitchButton()
        self.reward_stop_url_switch.setOnText("开启")
        self.reward_stop_url_switch.setOffText("关闭")
        self.reward_stop_url_switch.setFont(QFont(load_custom_font(), 12))
        self.reward_stop_url_switch.checkedChanged.connect(self.save_settings)
        
        self.reward_reset_url_switch = SwitchButton()
        self.reward_reset_url_switch.setOnText("开启")
        self.reward_reset_url_switch.setOffText("关闭")
        self.reward_reset_url_switch.setFont(QFont(load_custom_font(), 12))
        self.reward_reset_url_switch.checkedChanged.connect(self.save_settings)
        
        # 关于界面Action参数URL开关
        self.about_action_url_switch = SwitchButton()
        self.about_action_url_switch.setOnText("开启")
        self.about_action_url_switch.setOffText("关闭")
        self.about_action_url_switch.setFont(QFont(load_custom_font(), 12))
        self.about_action_url_switch.checkedChanged.connect(self.save_settings)
        
        # 关于界面Action参数URL独立开关
        self.about_donation_url_switch = SwitchButton()
        self.about_donation_url_switch.setOnText("开启")
        self.about_donation_url_switch.setOffText("关闭")
        self.about_donation_url_switch.setFont(QFont(load_custom_font(), 12))
        self.about_donation_url_switch.checkedChanged.connect(self.save_settings)
        
        self.about_contributor_url_switch = SwitchButton()
        self.about_contributor_url_switch.setOnText("开启")
        self.about_contributor_url_switch.setOffText("关闭")
        self.about_contributor_url_switch.setFont(QFont(load_custom_font(), 12))
        self.about_contributor_url_switch.checkedChanged.connect(self.save_settings)
        
        # 插件设置Action参数URL开关
        self.plugin_settings_action_url_switch = SwitchButton()
        self.plugin_settings_action_url_switch.setOnText("开启")
        self.plugin_settings_action_url_switch.setOffText("关闭")
        self.plugin_settings_action_url_switch.setFont(QFont(load_custom_font(), 12))
        self.plugin_settings_action_url_switch.checkedChanged.connect(self.save_settings)
        
        # 创建插件设置Action参数URL独立开关
        self.plugin_settings_open_url_switch = SwitchButton()
        self.plugin_settings_open_url_switch.setOnText("开启")
        self.plugin_settings_open_url_switch.setOffText("关闭")
        self.plugin_settings_open_url_switch.setFont(QFont(load_custom_font(), 12))
        self.plugin_settings_open_url_switch.checkedChanged.connect(self.save_settings)

        # 创建弹窗提醒下拉框
        self.main_url_notification_combo = ComboBox()
        self.main_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.main_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.main_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("main_url_notification", index)
        )

        self.settings_url_notification_combo = ComboBox()
        self.settings_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.settings_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.settings_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("settings_url_notification", index)
        )

        self.pumping_url_notification_combo = ComboBox()
        self.pumping_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.pumping_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.pumping_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("pumping_url_notification", index)
        )

        self.reward_url_notification_combo = ComboBox()
        self.reward_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.reward_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.reward_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("reward_url_notification", index)
        )

        self.history_url_notification_combo = ComboBox()
        self.history_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.history_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.history_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("history_url_notification", index)
        )

        self.floating_url_notification_combo = ComboBox()
        self.floating_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.floating_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.floating_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("floating_url_notification", index)
        )

        self.about_url_notification_combo = ComboBox()
        self.about_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.about_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.about_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("about_url_notification", index)
        )

        self.direct_extraction_url_notification_combo = ComboBox()
        self.direct_extraction_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.direct_extraction_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.direct_extraction_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("direct_extraction_url_notification", index)
        )

        self.plugin_settings_url_notification_combo = ComboBox()
        self.plugin_settings_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.plugin_settings_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.plugin_settings_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("plugin_settings_url_notification", index)
        )

        self.pumping_start_url_notification_combo = ComboBox()
        self.pumping_start_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.pumping_start_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.pumping_start_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("pumping_start_url_notification", index)
        )

        self.pumping_stop_url_notification_combo = ComboBox()
        self.pumping_stop_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.pumping_stop_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.pumping_stop_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("pumping_stop_url_notification", index)
        )

        self.pumping_reset_url_notification_combo = ComboBox()
        self.pumping_reset_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.pumping_reset_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.pumping_reset_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("pumping_reset_url_notification", index)
        )

        self.reward_start_url_notification_combo = ComboBox()
        self.reward_start_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.reward_start_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.reward_start_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("reward_start_url_notification", index)
        )

        self.reward_stop_url_notification_combo = ComboBox()
        self.reward_stop_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.reward_stop_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.reward_stop_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("reward_stop_url_notification", index)
        )

        self.reward_reset_url_notification_combo = ComboBox()
        self.reward_reset_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.reward_reset_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.reward_reset_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("reward_reset_url_notification", index)
        )

        self.about_donation_url_notification_combo = ComboBox()
        self.about_donation_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.about_donation_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.about_donation_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("about_donation_url_notification", index)
        )

        self.about_contributor_url_notification_combo = ComboBox()
        self.about_contributor_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.about_contributor_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.about_contributor_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("about_contributor_url_notification", index)
        )

        self.plugin_settings_open_url_notification_combo = ComboBox()
        self.plugin_settings_open_url_notification_combo.addItems(["禁用", "仅提醒", "弹窗同意", "安全验证同意"])
        self.plugin_settings_open_url_notification_combo.setFont(QFont(load_custom_font(), 12))
        self.plugin_settings_open_url_notification_combo.currentIndexChanged.connect(
            lambda index: self.on_notification_changed("plugin_settings_open_url_notification", index)
        )

        # 创建跳过安全验证开关
        self.settings_url_skip_security_switch = SwitchButton()
        self.settings_url_skip_security_switch.setOnText("开启")
        self.settings_url_skip_security_switch.setOffText("关闭")
        self.settings_url_skip_security_switch.setFont(QFont(load_custom_font(), 12))
        self.settings_url_skip_security_switch.checkedChanged.connect(self.save_settings)
        
        self.floating_url_skip_security_switch = SwitchButton()
        self.floating_url_skip_security_switch.setOnText("开启")
        self.floating_url_skip_security_switch.setOffText("关闭")
        self.floating_url_skip_security_switch.setFont(QFont(load_custom_font(), 12))
        self.floating_url_skip_security_switch.checkedChanged.connect(self.save_settings)
        
        self.plugin_settings_open_url_skip_security_switch = SwitchButton()
        self.plugin_settings_open_url_skip_security_switch.setOnText("开启")
        self.plugin_settings_open_url_skip_security_switch.setOffText("关闭")
        self.plugin_settings_open_url_skip_security_switch.setFont(QFont(load_custom_font(), 12))
        self.plugin_settings_open_url_skip_security_switch.checkedChanged.connect(self.save_settings)

        # 添加URL设置组
        self.addGroup(get_theme_icon("ic_fluent_home_20_filled"), "主界面URL", "secrandom://main - 打开SecRandom主界面", self.main_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "主界面URL弹窗提醒", "选择通过URL协议打开主界面时的弹窗提醒方式", self.main_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_settings_20_filled"), "设置界面URL", "secrandom://settings - 打开设置界面", self.settings_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "设置界面URL弹窗提醒", "选择通过URL协议打开设置界面时的弹窗提醒方式", self.settings_url_notification_combo)
        self.addGroup(get_theme_icon("ic_fluent_shield_20_filled"), "设置界面跳过安全验证", "通过URL协议打开设置界面时是否跳过安全验证", self.settings_url_skip_security_switch)
        
        self.addGroup(get_theme_icon("ic_fluent_people_community_20_filled"), "点名界面URL", "secrandom://pumping - 打开点名界面", self.pumping_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "点名界面URL弹窗提醒", "选择通过URL协议打开点名界面时的弹窗提醒方式", self.pumping_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_reward_20_filled"), "抽奖界面URL", "secrandom://reward - 打开抽奖界面", self.reward_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "抽奖界面URL弹窗提醒", "选择通过URL协议打开抽奖界面时的弹窗提醒方式", self.reward_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_chat_history_20_filled"), "历史记录界面URL", "secrandom://history - 打开历史记录界面", self.history_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "历史记录界面URL弹窗提醒", "选择通过URL协议打开历史记录界面时的弹窗提醒方式", self.history_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_window_ad_20_filled"), "浮窗界面URL", "secrandom://floating - 打开浮窗界面", self.floating_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "浮窗界面URL弹窗提醒", "选择通过URL协议显示/隐藏浮窗界面时的弹窗提醒方式", self.floating_url_notification_combo)
        self.addGroup(get_theme_icon("ic_fluent_shield_20_filled"), "浮窗界面跳过安全验证", "通过URL协议显示/隐藏浮窗界面时是否跳过安全验证", self.floating_url_skip_security_switch)
        
        self.addGroup(get_theme_icon("ic_fluent_info_20_filled"), "关于界面URL", "secrandom://about - 打开关于界面", self.about_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "关于界面URL弹窗提醒", "选择通过URL协议打开关于界面时的弹窗提醒方式", self.about_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_flash_20_filled"), "闪抽界面URL", "secrandom://direct_extraction - 打开闪抽界面", self.direct_extraction_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "闪抽界面URL弹窗提醒", "选择通过URL协议打开闪抽界面时的弹窗提醒方式", self.direct_extraction_url_notification_combo)
        
        # 添加Action参数URL设置组
        self.addGroup(get_theme_icon("ic_fluent_people_community_20_filled"), "开始点名URL", "secrandom://pumping?action=start - 自动切换到点名界面并开始抽选操作", self.pumping_start_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "开始点名URL弹窗提醒", "选择通过URL协议开始点名操作时的弹窗提醒方式", self.pumping_start_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_people_community_20_filled"), "停止点名URL", "secrandom://pumping?action=stop - 自动切换到点名界面并停止当前的点名操作", self.pumping_stop_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "停止点名URL弹窗提醒", "选择通过URL协议停止点名操作时的弹窗提醒方式", self.pumping_stop_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_people_community_20_filled"), "重置点名URL", "secrandom://pumping?action=reset - 自动切换到点名界面并清空当前的抽选结果", self.pumping_reset_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "重置点名URL弹窗提醒", "选择通过URL协议重置点名操作时的弹窗提醒方式", self.pumping_reset_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_reward_20_filled"), "开始抽奖URL", "secrandom://reward?action=start - 自动切换到抽奖界面并开始抽奖操作", self.reward_start_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "开始抽奖URL弹窗提醒", "选择通过URL协议开始抽奖操作时的弹窗提醒方式", self.reward_start_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_reward_20_filled"), "停止抽奖URL", "secrandom://reward?action=stop - 自动切换到抽奖界面并停止当前的抽奖操作", self.reward_stop_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "停止抽奖URL弹窗提醒", "选择通过URL协议停止抽奖操作时的弹窗提醒方式", self.reward_stop_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_reward_20_filled"), "重置抽奖URL", "secrandom://reward?action=reset - 自动切换到抽奖界面并清空当前的抽奖结果", self.reward_reset_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "重置抽奖URL弹窗提醒", "选择通过URL协议重置抽奖操作时的弹窗提醒方式", self.reward_reset_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_document_person_20_filled"), "打开捐赠支持对话框URL", "secrandom://about?action=donation - 自动切换到关于界面并打开捐赠支持对话框", self.about_donation_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "打开捐赠支持对话框URL弹窗提醒", "选择通过URL协议打开捐赠支持对话框时的弹窗提醒方式", self.about_donation_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_document_person_20_filled"), "打开贡献者对话框URL", "secrandom://about?action=contributor - 自动切换到关于界面并打开贡献者对话框", self.about_contributor_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "打开贡献者对话框URL弹窗提醒", "选择通过URL协议打开贡献者对话框时的弹窗提醒方式", self.about_contributor_url_notification_combo)
        
        self.addGroup(get_theme_icon("ic_fluent_database_plug_connected_20_filled"), "打开插件页面URL", "secrandom://plugin_settings?action=open - 打开插件页面，管理和配置插件", self.plugin_settings_open_url_switch)
        self.addGroup(get_theme_icon("ic_fluent_alert_20_filled"), "打开插件页面URL弹窗提醒", "选择通过URL协议打开插件页面时的弹窗提醒方式", self.plugin_settings_open_url_notification_combo)
        self.addGroup(get_theme_icon("ic_fluent_shield_20_filled"), "打开插件页面跳过安全验证", "通过URL协议打开插件页面时是否跳过安全验证", self.plugin_settings_open_url_skip_security_switch)

        # 加载设置
        self.load_settings()
        self.save_settings()

    # 又臭又长还重复的东西不抽象成函数是抖m吗
    def _fuckingResetter(self):
        self.main_url_switch.setChecked(self.default_settings["enable_main_url"])
        self.settings_url_switch.setChecked(self.default_settings["enable_settings_url"])
        self.pumping_url_switch.setChecked(self.default_settings["enable_pumping_url"])
        self.reward_url_switch.setChecked(self.default_settings["enable_reward_url"])
        self.history_url_switch.setChecked(self.default_settings["enable_history_url"])
        self.floating_url_switch.setChecked(self.default_settings["enable_floating_url"])
        self.about_url_switch.setChecked(self.default_settings["enable_about_url"])
        self.direct_extraction_url_switch.setChecked(self.default_settings["enable_direct_extraction_url"])
        self.pumping_action_url_switch.setChecked(self.default_settings["enable_pumping_action_url"])
        self.reward_action_url_switch.setChecked(self.default_settings["enable_reward_action_url"])
        self.about_action_url_switch.setChecked(self.default_settings["enable_about_action_url"])
        self.plugin_settings_action_url_switch.setChecked(self.default_settings["enable_plugin_settings_action_url"])
        
        # 加载点名Action参数URL独立开关
        self.pumping_start_url_switch.setChecked(self.default_settings["enable_pumping_start_url"])
        self.pumping_stop_url_switch.setChecked(self.default_settings["enable_pumping_stop_url"])
        self.pumping_reset_url_switch.setChecked(self.default_settings["enable_pumping_reset_url"])
        
        # 加载抽奖Action参数URL独立开关
        self.reward_start_url_switch.setChecked(self.default_settings["enable_reward_start_url"])
        self.reward_stop_url_switch.setChecked(self.default_settings["enable_reward_stop_url"])
        self.reward_reset_url_switch.setChecked(self.default_settings["enable_reward_reset_url"])
        
        # 加载关于界面Action参数URL独立开关
        self.about_donation_url_switch.setChecked(self.default_settings["enable_about_donation_url"])
        self.about_contributor_url_switch.setChecked(self.default_settings["enable_about_contributor_url"])
        
        # 加载插件设置Action参数URL独立开关
        self.plugin_settings_open_url_switch.setChecked(self.default_settings["enable_plugin_settings_open_url"])

        # 加载弹窗提醒设置
        self.main_url_notification_combo.setCurrentIndex(self.default_settings["main_url_notification"])
        self.settings_url_notification_combo.setCurrentIndex(self.default_settings["settings_url_notification"])
        self.pumping_url_notification_combo.setCurrentIndex(self.default_settings["pumping_url_notification"])
        self.reward_url_notification_combo.setCurrentIndex(self.default_settings["reward_url_notification"])
        self.history_url_notification_combo.setCurrentIndex(self.default_settings["history_url_notification"])
        self.floating_url_notification_combo.setCurrentIndex(self.default_settings["floating_url_notification"])
        self.about_url_notification_combo.setCurrentIndex(self.default_settings["about_url_notification"])
        self.direct_extraction_url_notification_combo.setCurrentIndex(self.default_settings["direct_extraction_url_notification"])
        self.plugin_settings_url_notification_combo.setCurrentIndex(self.default_settings["plugin_settings_url_notification"])
        self.pumping_start_url_notification_combo.setCurrentIndex(self.default_settings["pumping_start_url_notification"])
        self.pumping_stop_url_notification_combo.setCurrentIndex(self.default_settings["pumping_stop_url_notification"])
        self.pumping_reset_url_notification_combo.setCurrentIndex(self.default_settings["pumping_reset_url_notification"])
        self.reward_start_url_notification_combo.setCurrentIndex(self.default_settings["reward_start_url_notification"])
        self.reward_stop_url_notification_combo.setCurrentIndex(self.default_settings["reward_stop_url_notification"])
        self.reward_reset_url_notification_combo.setCurrentIndex(self.default_settings["reward_reset_url_notification"])
        self.about_donation_url_notification_combo.setCurrentIndex(self.default_settings["about_donation_url_notification"])
        self.about_contributor_url_notification_combo.setCurrentIndex(self.default_settings["about_contributor_url_notification"])
        self.plugin_settings_open_url_notification_combo.setCurrentIndex(self.default_settings["plugin_settings_open_url_notification"])
    
        # 加载跳过安全验证设置
        self.settings_url_skip_security_switch.setChecked(self.default_settings["settings_url_skip_security"])
        self.floating_url_skip_security_switch.setChecked(self.default_settings["floating_url_skip_security"])
        self.plugin_settings_open_url_skip_security_switch.setChecked(self.default_settings["plugin_settings_open_url_skip_security"])

    def load_settings(self):
        try:
            if path_manager.file_exists(self.settings_file):
                with open_file(self.settings_file, 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    fixed_url_settings = settings.get("fixed_url", {})
                    
                    # 加载主界面URL设置
                    self.main_url_switch.setChecked(fixed_url_settings.get("enable_main_url", self.default_settings["enable_main_url"]))
                    
                    # 加载设置界面URL设置
                    self.settings_url_switch.setChecked(fixed_url_settings.get("enable_settings_url", self.default_settings["enable_settings_url"]))
                    
                    # 加载点名界面URL设置
                    self.pumping_url_switch.setChecked(fixed_url_settings.get("enable_pumping_url", self.default_settings["enable_pumping_url"]))
                    
                    # 加载抽奖界面URL设置
                    self.reward_url_switch.setChecked(fixed_url_settings.get("enable_reward_url", self.default_settings["enable_reward_url"]))
                    
                    # 加载历史记录界面URL设置
                    self.history_url_switch.setChecked(fixed_url_settings.get("enable_history_url", self.default_settings["enable_history_url"]))
                    
                    # 加载浮窗界面URL设置
                    self.floating_url_switch.setChecked(fixed_url_settings.get("enable_floating_url", self.default_settings["enable_floating_url"]))
                    
                    # 加载关于界面URL设置
                    self.about_url_switch.setChecked(fixed_url_settings.get("enable_about_url", self.default_settings["enable_about_url"]))
                    
                    # 加载闪抽界面URL设置
                    self.direct_extraction_url_switch.setChecked(fixed_url_settings.get("enable_direct_extraction_url", self.default_settings["enable_direct_extraction_url"]))
                    
                    # 加载Action参数URL设置
                    self.pumping_action_url_switch.setChecked(fixed_url_settings.get("enable_pumping_action_url", self.default_settings["enable_pumping_action_url"]))
                    self.reward_action_url_switch.setChecked(fixed_url_settings.get("enable_reward_action_url", self.default_settings["enable_reward_action_url"]))
                    self.about_action_url_switch.setChecked(fixed_url_settings.get("enable_about_action_url", self.default_settings["enable_about_action_url"]))
                    self.plugin_settings_action_url_switch.setChecked(fixed_url_settings.get("enable_plugin_settings_action_url", self.default_settings["enable_plugin_settings_action_url"]))
                    
                    # 加载点名Action参数URL独立开关
                    self.pumping_start_url_switch.setChecked(fixed_url_settings.get("enable_pumping_start_url", self.default_settings["enable_pumping_start_url"]))
                    self.pumping_stop_url_switch.setChecked(fixed_url_settings.get("enable_pumping_stop_url", self.default_settings["enable_pumping_stop_url"]))
                    self.pumping_reset_url_switch.setChecked(fixed_url_settings.get("enable_pumping_reset_url", self.default_settings["enable_pumping_reset_url"]))
                    
                    # 加载抽奖Action参数URL独立开关
                    self.reward_start_url_switch.setChecked(fixed_url_settings.get("enable_reward_start_url", self.default_settings["enable_reward_start_url"]))
                    self.reward_stop_url_switch.setChecked(fixed_url_settings.get("enable_reward_stop_url", self.default_settings["enable_reward_stop_url"]))
                    self.reward_reset_url_switch.setChecked(fixed_url_settings.get("enable_reward_reset_url", self.default_settings["enable_reward_reset_url"]))
                    
                    # 加载关于界面Action参数URL独立开关
                    self.about_donation_url_switch.setChecked(fixed_url_settings.get("enable_about_donation_url", self.default_settings["enable_about_donation_url"]))
                    self.about_contributor_url_switch.setChecked(fixed_url_settings.get("enable_about_contributor_url", self.default_settings["enable_about_contributor_url"]))
                    
                    # 加载插件设置Action参数URL独立开关
                    self.plugin_settings_open_url_switch.setChecked(fixed_url_settings.get("enable_plugin_settings_open_url", self.default_settings["enable_plugin_settings_open_url"]))
                    
                    # 加载弹窗提醒设置
                    self.main_url_notification_combo.setCurrentIndex(fixed_url_settings.get("main_url_notification", self.default_settings["main_url_notification"]))
                    self.settings_url_notification_combo.setCurrentIndex(fixed_url_settings.get("settings_url_notification", self.default_settings["settings_url_notification"]))
                    self.pumping_url_notification_combo.setCurrentIndex(fixed_url_settings.get("pumping_url_notification", self.default_settings["pumping_url_notification"]))
                    self.reward_url_notification_combo.setCurrentIndex(fixed_url_settings.get("reward_url_notification", self.default_settings["reward_url_notification"]))
                    self.history_url_notification_combo.setCurrentIndex(fixed_url_settings.get("history_url_notification", self.default_settings["history_url_notification"]))
                    self.floating_url_notification_combo.setCurrentIndex(fixed_url_settings.get("floating_url_notification", self.default_settings["floating_url_notification"]))
                    self.about_url_notification_combo.setCurrentIndex(fixed_url_settings.get("about_url_notification", self.default_settings["about_url_notification"]))
                    self.direct_extraction_url_notification_combo.setCurrentIndex(fixed_url_settings.get("direct_extraction_url_notification", self.default_settings["direct_extraction_url_notification"]))
                    self.plugin_settings_url_notification_combo.setCurrentIndex(fixed_url_settings.get("plugin_settings_url_notification", self.default_settings["plugin_settings_url_notification"]))
                    self.pumping_start_url_notification_combo.setCurrentIndex(fixed_url_settings.get("pumping_start_url_notification", self.default_settings["pumping_start_url_notification"]))
                    self.pumping_stop_url_notification_combo.setCurrentIndex(fixed_url_settings.get("pumping_stop_url_notification", self.default_settings["pumping_stop_url_notification"]))
                    self.pumping_reset_url_notification_combo.setCurrentIndex(fixed_url_settings.get("pumping_reset_url_notification", self.default_settings["pumping_reset_url_notification"]))
                    self.reward_start_url_notification_combo.setCurrentIndex(fixed_url_settings.get("reward_start_url_notification", self.default_settings["reward_start_url_notification"]))
                    self.reward_stop_url_notification_combo.setCurrentIndex(fixed_url_settings.get("reward_stop_url_notification", self.default_settings["reward_stop_url_notification"]))
                    self.reward_reset_url_notification_combo.setCurrentIndex(fixed_url_settings.get("reward_reset_url_notification", self.default_settings["reward_reset_url_notification"]))
                    self.about_donation_url_notification_combo.setCurrentIndex(fixed_url_settings.get("about_donation_url_notification", self.default_settings["about_donation_url_notification"]))
                    self.about_contributor_url_notification_combo.setCurrentIndex(fixed_url_settings.get("about_contributor_url_notification", self.default_settings["about_contributor_url_notification"]))
                    self.plugin_settings_open_url_notification_combo.setCurrentIndex(fixed_url_settings.get("plugin_settings_open_url_notification", self.default_settings["plugin_settings_open_url_notification"]))
                    
                    # 加载跳过安全验证设置
                    self.settings_url_skip_security_switch.setChecked(fixed_url_settings.get("settings_url_skip_security", self.default_settings["settings_url_skip_security"]))
                    self.floating_url_skip_security_switch.setChecked(fixed_url_settings.get("floating_url_skip_security", self.default_settings["floating_url_skip_security"]))
                    self.plugin_settings_open_url_skip_security_switch.setChecked(fixed_url_settings.get("plugin_settings_open_url_skip_security", self.default_settings["plugin_settings_open_url_skip_security"]))
                    
                    # 根据基础URL开关状态设置Action参数URL开关的启用/禁用状态
                    if not self.pumping_url_switch.isChecked():
                        self.pumping_action_url_switch.setEnabled(False)
                        self.pumping_start_url_switch.setEnabled(False)
                        self.pumping_stop_url_switch.setEnabled(False)
                        self.pumping_reset_url_switch.setEnabled(False)
                    
                    if not self.reward_url_switch.isChecked():
                        self.reward_action_url_switch.setEnabled(False)
                        self.reward_start_url_switch.setEnabled(False)
                        self.reward_stop_url_switch.setEnabled(False)
                        self.reward_reset_url_switch.setEnabled(False)
                    
                    if not self.about_url_switch.isChecked():
                        self.about_action_url_switch.setEnabled(False)
                        self.about_donation_url_switch.setEnabled(False)
                        self.about_contributor_url_switch.setEnabled(False)

            else:
                logger.warning(f"设置文件不存在: {self.settings_file}")
                self._fuckingResetter()
                self.save_settings()
        except Exception as e:
            logger.error(f"加载设置时出错: {e}")
            self._fuckingResetter()

    def save_settings(self):
        # 先读取现有设置
        existing_settings = {}
        if path_manager.file_exists(self.settings_file):
            with open_file(self.settings_file, 'r', encoding='utf-8') as f:
                try:
                    existing_settings = json.load(f)
                except json.JSONDecodeError:
                    existing_settings = {}
        
        # 更新fixed_url部分的所有设置
        if "fixed_url" not in existing_settings:
            existing_settings["fixed_url"] = {}
            
        fixed_url_settings = existing_settings["fixed_url"]
        
        # 保存主界面URL设置
        fixed_url_settings["enable_main_url"] = self.main_url_switch.isChecked()
        
        # 保存设置界面URL设置
        fixed_url_settings["enable_settings_url"] = self.settings_url_switch.isChecked()
        
        # 保存点名界面URL设置
        fixed_url_settings["enable_pumping_url"] = self.pumping_url_switch.isChecked()
        
        # 保存抽奖界面URL设置
        fixed_url_settings["enable_reward_url"] = self.reward_url_switch.isChecked()
        
        # 保存历史记录界面URL设置
        fixed_url_settings["enable_history_url"] = self.history_url_switch.isChecked()
        
        # 保存浮窗界面URL设置
        fixed_url_settings["enable_floating_url"] = self.floating_url_switch.isChecked()
        
        # 保存关于界面URL设置
        fixed_url_settings["enable_about_url"] = self.about_url_switch.isChecked()
        
        # 保存闪抽界面URL设置
        fixed_url_settings["enable_direct_extraction_url"] = self.direct_extraction_url_switch.isChecked()
        
        # 保存Action参数URL设置
        fixed_url_settings["enable_pumping_action_url"] = self.pumping_action_url_switch.isChecked()
        fixed_url_settings["enable_reward_action_url"] = self.reward_action_url_switch.isChecked()
        fixed_url_settings["enable_about_action_url"] = self.about_action_url_switch.isChecked()
        fixed_url_settings["enable_plugin_settings_action_url"] = self.plugin_settings_action_url_switch.isChecked()
        
        # 保存点名Action参数URL独立开关
        fixed_url_settings["enable_pumping_start_url"] = self.pumping_start_url_switch.isChecked()
        fixed_url_settings["enable_pumping_stop_url"] = self.pumping_stop_url_switch.isChecked()
        fixed_url_settings["enable_pumping_reset_url"] = self.pumping_reset_url_switch.isChecked()
        
        # 保存抽奖Action参数URL独立开关
        fixed_url_settings["enable_reward_start_url"] = self.reward_start_url_switch.isChecked()
        fixed_url_settings["enable_reward_stop_url"] = self.reward_stop_url_switch.isChecked()
        fixed_url_settings["enable_reward_reset_url"] = self.reward_reset_url_switch.isChecked()
        
        # 保存关于界面Action参数URL独立开关
        fixed_url_settings["enable_about_donation_url"] = self.about_donation_url_switch.isChecked()
        fixed_url_settings["enable_about_contributor_url"] = self.about_contributor_url_switch.isChecked()
        
        # 保存插件设置Action参数URL独立开关
        fixed_url_settings["enable_plugin_settings_open_url"] = self.plugin_settings_open_url_switch.isChecked()
        
        # 保存跳过安全验证设置
        fixed_url_settings["settings_url_skip_security"] = self.settings_url_skip_security_switch.isChecked()
        fixed_url_settings["plugin_settings_open_url_skip_security"] = self.plugin_settings_open_url_skip_security_switch.isChecked()
        
        # 保存弹窗提醒设置 (0=disabled, 1=notify_only, 2=confirm, 3=confirm_with_security)
        fixed_url_settings["main_url_notification"] = self.main_url_notification_combo.currentIndex()
        fixed_url_settings["settings_url_notification"] = self.settings_url_notification_combo.currentIndex()
        fixed_url_settings["pumping_url_notification"] = self.pumping_url_notification_combo.currentIndex()
        fixed_url_settings["reward_url_notification"] = self.reward_url_notification_combo.currentIndex()
        fixed_url_settings["history_url_notification"] = self.history_url_notification_combo.currentIndex()
        fixed_url_settings["floating_url_notification"] = self.floating_url_notification_combo.currentIndex()
        fixed_url_settings["about_url_notification"] = self.about_url_notification_combo.currentIndex()
        fixed_url_settings["direct_extraction_url_notification"] = self.direct_extraction_url_notification_combo.currentIndex()
        fixed_url_settings["plugin_settings_url_notification"] = self.plugin_settings_url_notification_combo.currentIndex()
        fixed_url_settings["pumping_start_url_notification"] = self.pumping_start_url_notification_combo.currentIndex()
        fixed_url_settings["pumping_stop_url_notification"] = self.pumping_stop_url_notification_combo.currentIndex()
        fixed_url_settings["pumping_reset_url_notification"] = self.pumping_reset_url_notification_combo.currentIndex()
        fixed_url_settings["reward_start_url_notification"] = self.reward_start_url_notification_combo.currentIndex()
        fixed_url_settings["reward_stop_url_notification"] = self.reward_stop_url_notification_combo.currentIndex()
        fixed_url_settings["reward_reset_url_notification"] = self.reward_reset_url_notification_combo.currentIndex()
        fixed_url_settings["about_donation_url_notification"] = self.about_donation_url_notification_combo.currentIndex()
        fixed_url_settings["about_contributor_url_notification"] = self.about_contributor_url_notification_combo.currentIndex()
        fixed_url_settings["plugin_settings_open_url_notification"] = self.plugin_settings_open_url_notification_combo.currentIndex()
        
        # 保存跳过安全验证设置
        fixed_url_settings["settings_url_skip_security"] = self.settings_url_skip_security_switch.isChecked()
        fixed_url_settings["floating_url_skip_security"] = self.floating_url_skip_security_switch.isChecked()
        fixed_url_settings["plugin_settings_open_url_skip_security"] = self.plugin_settings_open_url_skip_security_switch.isChecked()

        os.makedirs(os.path.dirname(self.settings_file), exist_ok=True)
        with open_file(self.settings_file, 'w', encoding='utf-8') as f:
            json.dump(existing_settings, f, indent=4)
    
    def on_pumping_url_changed(self):
        """点名界面URL开关状态变化处理"""
        # 如果点名界面URL关闭，则关闭并禁用对应的Action参数URL
        if not self.pumping_url_switch.isChecked():
            # 关闭并禁用点名Action参数URL总开关
            self.pumping_action_url_switch.setChecked(False)
            self.pumping_action_url_switch.setEnabled(False)
            # 关闭并禁用所有点名Action参数URL独立开关
            self.pumping_start_url_switch.setChecked(False)
            self.pumping_start_url_switch.setEnabled(False)
            self.pumping_stop_url_switch.setChecked(False)
            self.pumping_stop_url_switch.setEnabled(False)
            self.pumping_reset_url_switch.setChecked(False)
            self.pumping_reset_url_switch.setEnabled(False)
        else:
            # 如果点名界面URL开启，则启用对应的Action参数URL开关
            self.pumping_action_url_switch.setEnabled(True)
            self.pumping_start_url_switch.setEnabled(True)
            self.pumping_stop_url_switch.setEnabled(True)
            self.pumping_reset_url_switch.setEnabled(True)
        self.save_settings()
    
    def on_reward_url_changed(self):
        """抽奖界面URL开关状态变化处理"""
        # 如果抽奖界面URL关闭，则关闭并禁用对应的Action参数URL
        if not self.reward_url_switch.isChecked():
            # 关闭并禁用抽奖Action参数URL总开关
            self.reward_action_url_switch.setChecked(False)
            self.reward_action_url_switch.setEnabled(False)
            # 关闭并禁用所有抽奖Action参数URL独立开关
            self.reward_start_url_switch.setChecked(False)
            self.reward_start_url_switch.setEnabled(False)
            self.reward_stop_url_switch.setChecked(False)
            self.reward_stop_url_switch.setEnabled(False)
            self.reward_reset_url_switch.setChecked(False)
            self.reward_reset_url_switch.setEnabled(False)
        else:
            # 如果抽奖界面URL开启，则启用对应的Action参数URL开关
            self.reward_action_url_switch.setEnabled(True)
            self.reward_start_url_switch.setEnabled(True)
            self.reward_stop_url_switch.setEnabled(True)
            self.reward_reset_url_switch.setEnabled(True)
        self.save_settings()
    
    def on_about_url_changed(self):
        """关于界面URL开关状态变化处理"""
        # 如果关于界面URL关闭，则关闭并禁用对应的Action参数URL
        if not self.about_url_switch.isChecked():
            # 关闭并禁用关于界面Action参数URL总开关
            self.about_action_url_switch.setChecked(False)
            self.about_action_url_switch.setEnabled(False)
            # 关闭并禁用所有关于界面Action参数URL独立开关
            self.about_donation_url_switch.setChecked(False)
            self.about_donation_url_switch.setEnabled(False)
            self.about_contributor_url_switch.setChecked(False)
            self.about_contributor_url_switch.setEnabled(False)
        else:
            # 如果关于界面URL开启，则启用对应的Action参数URL开关
            self.about_action_url_switch.setEnabled(True)
            self.about_donation_url_switch.setEnabled(True)
            self.about_contributor_url_switch.setEnabled(True)
        self.save_settings()
    
    def on_notification_changed(self, setting_key, value):
        """弹窗提醒设置变化处理
        
        Args:
            setting_key: 设置键名
            value: 下拉框索引值 (0=disabled, 1=notify_only, 2=confirm, 3=confirm_with_security)
        """
        try:
            # 保存设置
            existing_settings = {}
            if path_manager.file_exists(self.settings_file):
                with open_file(self.settings_file, 'r', encoding='utf-8') as f:
                    try:
                        existing_settings = json.load(f)
                    except json.JSONDecodeError:
                        existing_settings = {}
            
            # 更新fixed_url部分的所有设置
            if "fixed_url" not in existing_settings:
                existing_settings["fixed_url"] = {}
                
            fixed_url_settings = existing_settings["fixed_url"]
            fixed_url_settings[setting_key] = value
            
            # 保存设置
            os.makedirs(os.path.dirname(self.settings_file), exist_ok=True)
            with open_file(self.settings_file, 'w', encoding='utf-8') as f:
                json.dump(existing_settings, f, indent=4)
                
            logger.info(f"弹窗提醒设置已更新: {setting_key} = {value}")
        except Exception as e:
            logger.error(f"保存弹窗提醒设置时出错: {e}")
    