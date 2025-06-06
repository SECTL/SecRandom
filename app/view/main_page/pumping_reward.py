from qfluentwidgets import *
from PyQt5.QtWidgets import *
from PyQt5.QtCore import *
from PyQt5.QtGui import *

import os
import sys
import json
import random
import pyttsx3
import datetime
from loguru import logger
from random import SystemRandom
system_random = SystemRandom()

from app.common.config import load_custom_font

class pumping_reward(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        # 定义变量
        self.is_animating = False
        self.draw_mode = "random"
        self.animation_timer = None
        # 使用全局语音引擎单例
        # 检查系统版本是否为Windows 10及以上
        if sys.platform == 'win32' and sys.getwindowsversion().major >= 10:
            if not hasattr(QApplication.instance(), 'pumping_reward_voice_engine'):
                QApplication.instance().pumping_reward_voice_engine = pyttsx3.init()
                QApplication.instance().pumping_reward_voice_engine.startLoop(False)
            self.voice_engine = QApplication.instance().pumping_reward_voice_engine
        else:
            logger.warning("语音功能仅在Windows 10及以上系统可用")
        self.initUI()
    
    def start_draw(self):
        """开始抽选奖品"""
        # 获取抽选模式和动画模式设置
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                pumping_reward_draw_mode = settings['pumping_reward']['draw_mode']
                pumping_reward_animation_mode = settings['pumping_reward']['animation_mode']
        except Exception as e:
            pumping_reward_draw_mode = 0
            pumping_reward_animation_mode = 0
            logger.error(f"加载设置时出错: {e}, 使用默认设置")

        # 根据抽选模式执行不同逻辑
        # 跟随全局设置
        if pumping_reward_draw_mode == 0:  # 重复随机
            self.draw_mode = "random"
        elif pumping_reward_draw_mode == 1:  # 不重复抽取(直到软件重启)
            self.draw_mode = "until_reboot"
        elif pumping_reward_draw_mode == 2:  # 不重复抽取(直到抽完全部奖)
            self.draw_mode = "until_all"
            
        # 根据动画模式执行不同逻辑
        if pumping_reward_animation_mode == 0:  # 手动停止动画
            self.start_button.setText("停止")
            self.is_animating = True
            self.animation_timer = QTimer()
            self.animation_timer.timeout.connect(self._show_random_reward)
            self.animation_timer.start(100)
            self.start_button.clicked.disconnect()
            self.start_button.clicked.connect(self._stop_animation)
            
        elif pumping_reward_animation_mode == 1:  # 自动播放完整动画
            self._play_full_animation()
            
        elif pumping_reward_animation_mode == 2:  # 直接显示结果
            self._show_result_directly()
        
    def _show_random_reward(self):
        """显示随机奖品（用于动画效果）"""
        reward_name = self.reward_combo.currentText()

        if reward_name and reward_name not in ["你暂未添加奖池", "加载奖池列表失败"]:
            reward_file = f"app/resource/reward/{reward_name}.json"

            if self.draw_mode == "until_reboot":
                draw_record_file = f"app/resource/Temp/until_the_reboot_{reward_name}.json"
            elif self.draw_mode == "until_all":
                draw_record_file = f"app/resource/Temp/until_all_draw_{reward_name}.json"
            
            if self.draw_mode in ["until_reboot", "until_all"]:
                # 创建Temp目录如果不存在
                os.makedirs(os.path.dirname(draw_record_file), exist_ok=True)
                
                # 初始化抽取记录文件
                if not os.path.exists(draw_record_file):
                    with open(draw_record_file, 'w', encoding='utf-8') as f:
                        json.dump([], f, ensure_ascii=False, indent=4)
                
                # 读取已抽取记录
                drawn_rewards = []
                with open(draw_record_file, 'r', encoding='utf-8') as f:
                    try:
                        drawn_rewards = json.load(f)
                    except json.JSONDecodeError:
                        drawn_rewards = []
            else:
                drawn_rewards = []

            if os.path.exists(reward_file):
                with open(reward_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    cleaned_data = []
                    # 获取奖品列表
                    for reward_name, reward_info in data.items():
                        if isinstance(reward_info, dict) and 'id' in reward_info:
                            id = reward_info.get('id', '')
                            name = reward_name
                            probability = reward_info.get('probability', '1')
                            cleaned_data.append((id, name, probability))

                    # 如果所有奖品都已抽取过，则使用全部奖品名单
                    rewards = [s for s in cleaned_data if s[1].replace(' ', '') not in [x.replace(' ', '') for x in drawn_rewards]] or cleaned_data

                    if rewards:
                        # 从self.current_count获取抽取奖数
                        draw_count = self.current_count
                        
                        # 根据设置选项选择随机方法
                        use_system_random = self.get_random_method_setting()
                        
                        if use_system_random == 1:  # 使用SystemRandom的方式-不可预测抽取
                            if len(rewards) <= draw_count:
                                selected_rewards = rewards
                            else:
                                if self.draw_mode == "random":
                                    # 允许重复抽取
                                    weights = [float(reward[2]) for reward in rewards]
                                    selected_rewards = system_random.choices(
                                        rewards,
                                        weights=weights,
                                        k=draw_count
                                    )
                                else:
                                    # 不重复抽取
                                    weights = [float(reward[2]) for reward in rewards]
                                    selected_rewards = []
                                    temp_rewards = rewards.copy()
                                    temp_weights = weights.copy()
                                    for _ in range(draw_count):
                                        if not temp_rewards:
                                            break
                                        chosen = system_random.choices(
                                            temp_rewards,
                                            weights=temp_weights,
                                            k=1
                                        )[0]
                                        selected_rewards.append(chosen)
                                        index = temp_rewards.index(chosen)
                                        temp_rewards.pop(index)
                                        temp_weights.pop(index)

                        else:  # 默认使用random的方式-可预测抽取
                            if len(rewards) <= draw_count:
                                selected_rewards = rewards
                            else:
                                if self.draw_mode == "random":
                                    # 允许重复抽取
                                    weights = [float(reward[2]) for reward in rewards]
                                    selected_rewards = random.choices(
                                        rewards,
                                        weights=weights,
                                        k=draw_count
                                    )
                                else:
                                    # 不重复抽取
                                    weights = [float(reward[2]) for reward in rewards]
                                    selected_rewards = []
                                    temp_rewards = rewards.copy()
                                    temp_weights = weights.copy()
                                    for _ in range(draw_count):
                                        if not temp_rewards:
                                            break
                                        chosen = random.choices(
                                            temp_rewards,
                                            weights=temp_weights,
                                            k=1
                                        )[0]
                                        selected_rewards.append(chosen)
                                        index = temp_rewards.index(chosen)
                                        temp_rewards.pop(index)
                                        temp_weights.pop(index)

                        # 清除旧布局和标签
                        if hasattr(self, 'container') and isinstance(self.container, list):
                            for label in self.container:
                                label.deleteLater()
                            self.container = []
                        elif hasattr(self, 'container') and isinstance(self.container, QWidget):
                            try:
                                if self.container:
                                    self.container.deleteLater()
                            except RuntimeError:
                                pass
                            del self.container

                        if hasattr(self, 'reward_labels'):
                            for label in self.reward_labels:
                                try:
                                    if label:
                                        label.deleteLater()
                                except RuntimeError:
                                    pass
                            self.reward_labels = []

                        # 删除布局中的所有内容
                        while self.result_grid.count(): 
                            item = self.result_grid.takeAt(0)
                            widget = item.widget()
                            if widget:
                                try:
                                    widget.deleteLater()
                                except RuntimeError:
                                    pass

                        # 创建新布局
                        vbox_layout = QGridLayout()
                        # 创建新标签列表
                        self.reward_labels = []
                        for num, name, probability in selected_rewards:
                            reward_id_str = f"{num:02}"
                            label = BodyLabel(f"{reward_id_str} {name}")

                            label.setAlignment(Qt.AlignCenter)
                            # 读取设置中的font_size值
                            try:
                                with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                                    settings = json.load(f)
                                    font_size = settings['pumping_reward']['font_size']
                                    if font_size < 30:
                                        font_size = 85
                            except Exception as e:
                                font_size = 85
                                logger.error(f"加载字体设置时出错: {e}, 使用默认设置")
                            # 根据设置调整字体大小
                            if font_size < 30:
                                label.setFont(QFont(load_custom_font(), 85))
                            else:
                                label.setFont(QFont(load_custom_font(), font_size))
                            self.reward_labels.append(label)
                            vbox_layout.addWidget(label)

                        # 计算所有标签的宽度之和，并考虑间距和边距
                        if self.reward_labels:
                            total_width = sum(label.sizeHint().width() for label in self.reward_labels) + \
                                          len(self.reward_labels) * 180
                            available_width = self.width() - 20
                            
                            # 如果总宽度超过可用宽度，则计算每行最多能放几个标签
                            if total_width > available_width:
                                avg_label_width = total_width / len(self.reward_labels)
                                max_columns = max(1, int(available_width // avg_label_width))
                            else:
                                max_columns = len(self.reward_labels)  # 一行显示所有标签
                        else:
                            max_columns = 1
                        
                        # 复用 container 和 vbox_layout
                        if not hasattr(self, 'container'):
                            self.container = QWidget()
                            self.vbox_layout = QGridLayout()
                            self.container.setLayout(self.vbox_layout)
                        else:
                            # 清空旧标签
                            for i in reversed(range(self.vbox_layout.count())):
                                item = self.vbox_layout.itemAt(i)
                                if item.widget():
                                    item.widget().setParent(None)

                        for i, label in enumerate(self.reward_labels):
                            row = i // max_columns
                            col = i % max_columns
                            self.vbox_layout.addWidget(label, row, col)
                        
                        self.result_grid.addWidget(self.container)
                        
                        return
        
        else:
            self.clear_layout(self.result_grid)
            if hasattr(self, 'container'):
                self.container.deleteLater()
                del self.container
            if hasattr(self, 'reward_labels'):
                for label in self.reward_labels:
                    label.deleteLater()
            
            # 创建错误标签
            error_label = BodyLabel("-- 抽选失败")
            error_label.setAlignment(Qt.AlignCenter)
            
            # 获取字体大小设置
            font_size = 85
            try:
                with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    font_size = max(settings['pumping_reward']['font_size'], 30)
            except Exception as e:
                logger.error(f"加载字体设置时出错: {e}, 使用默认设置")
            
            error_label.setFont(QFont(load_custom_font(), font_size))
            self.result_grid.addWidget(error_label)
    
    # 停止动画并显示最终结果
    def _stop_animation(self):
        """停止动画并显示最终结果"""
        self.animation_timer.stop()
        self.is_animating = False
        self.start_button.setText("开始")
        self.start_button.clicked.disconnect()
        self.start_button.clicked.connect(self.start_draw)
        
        # 显示最终结果
        self.random()
            
        # 动画结束后进行语音播报
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                voice_enabled = settings['pumping_reward']['voice_enabled']
                
                if voice_enabled == True:  # 开启语音
                    if hasattr(self, 'reward_labels'):
                        for label in self.reward_labels:
                            parts = label.text().split()
                            if len(parts) >= 2 and len(parts[-1]) == 1 and len(parts[-2]) == 1:
                                name = parts[-2] + parts[-1]
                            else:
                                name = parts[-1]
                            name = name.replace(' ', '')
                            self.voice_engine.say(f"{name}")
                            self.voice_engine.iterate()
        except Exception as e:
            logger.error(f"语音播报出错: {e}")
    
    # 播放完整动画（快速显示5个随机奖品后显示最终结果）
    def _play_full_animation(self):
        """播放完整动画（快速显示5个随机奖品后显示最终结果）"""
        self.is_animating = True
        self.animation_timer = QTimer()
        self.animation_timer.timeout.connect(self._show_random_reward)
        self.animation_timer.start(100)
        self.start_button.setEnabled(False)  # 禁用按钮
        
        # 5次随机后停止
        QTimer.singleShot(500, lambda: [
            self.animation_timer.stop(),
            self._stop_animation(),
            self.start_button.setEnabled(True)  # 恢复按钮
          ])
    
    # 直接显示结果（无动画效果）
    def _show_result_directly(self):
        """直接显示结果（无动画效果）"""
        self.random()

        # 动画结束后进行语音播报
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                voice_enabled = settings['pumping_reward']['voice_enabled']
                
                if voice_enabled == True:  # 开启语音
                    if hasattr(self, 'reward_labels'):
                        for label in self.reward_labels:
                            parts = label.text().split()
                            if len(parts) >= 2 and len(parts[-1]) == 1 and len(parts[-2]) == 1:
                                name = parts[-2] + parts[-1]
                            else:
                                name = parts[-1]
                            name = name.replace(' ', '')
                            self.voice_engine.say(f"{name}")
                            self.voice_engine.iterate()
        except Exception as e:
            logger.error(f"语音播报出错: {e}")
    
    # 根据抽取模式抽选奖品
    def random(self):
        """根据抽取模式抽选奖品"""
        reward_name = self.reward_combo.currentText()
        
        if reward_name and reward_name not in ["你暂未添加奖池", "加载奖池列表失败"]:
            reward_file = f"app/resource/reward/{reward_name}.json"

            if self.draw_mode == "until_reboot":
                draw_record_file = f"app/resource/Temp/until_the_reboot_{reward_name}.json"
            elif self.draw_mode == "until_all":
                draw_record_file = f"app/resource/Temp/until_all_draw_{reward_name}.json"
            
            if self.draw_mode in ["until_reboot", "until_all"]:
                # 创建Temp目录如果不存在
                os.makedirs(os.path.dirname(draw_record_file), exist_ok=True)
                
                # 初始化抽取记录文件
                if not os.path.exists(draw_record_file):
                    with open(draw_record_file, 'w', encoding='utf-8') as f:
                        json.dump([], f, ensure_ascii=False, indent=4)
                
                # 读取已抽取记录
                drawn_rewards = []
                with open(draw_record_file, 'r', encoding='utf-8') as f:
                    try:
                        drawn_rewards = json.load(f)
                    except json.JSONDecodeError:
                        drawn_rewards = []
            else:
                drawn_rewards = []
            
            if os.path.exists(reward_file):
                with open(reward_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    cleaned_data = []
                    
                    # 获取奖品列表
                    for prize_pools, reward_info in data.items():
                        if isinstance(reward_info, dict) and 'id' in reward_info:
                            id = reward_info.get('id', '')
                            name = prize_pools
                            probability = reward_info.get('probability', '1')
                            cleaned_data.append((id, name, probability))

                    if self.draw_mode == "random":
                        available_rewards = cleaned_data
                    elif self.draw_mode == "until_reboot" or self.draw_mode == "until_all":
                        available_rewards = [s for s in cleaned_data if s[1].replace(' ', '') not in [x.replace(' ', '') for x in drawn_rewards]]

                    if available_rewards:
                        # 从self.current_count获取抽取奖数
                        draw_count = self.current_count
                        
                        # 根据设置选项选择随机方法
                        use_system_random = self.get_random_method_setting()
                        
                        if use_system_random == 1:  # 使用SystemRandom的方式-不可预测抽取
                            if len(available_rewards) <= draw_count:
                                selected_rewards = available_rewards
                            else:
                                if self.draw_mode == "random":
                                    # 允许重复抽取
                                    weights = [float(reward[2]) for reward in available_rewards]
                                    selected_rewards = system_random.choices(
                                        available_rewards,
                                        weights=weights,
                                        k=draw_count
                                    )
                                else:
                                    # 不重复抽取
                                    weights = [float(reward[2]) for reward in available_rewards]
                                    selected_rewards = []
                                    temp_rewards = available_rewards.copy()
                                    temp_weights = weights.copy()
                                    for _ in range(draw_count):
                                        if not temp_rewards:
                                            break
                                        chosen = system_random.choices(
                                            temp_rewards,
                                            weights=temp_weights,
                                            k=1
                                        )[0]
                                        selected_rewards.append(chosen)
                                        index = temp_rewards.index(chosen)
                                        temp_rewards.pop(index)
                                        temp_weights.pop(index)

                        else:  # 默认使用random的方式-可预测抽取
                            if len(available_rewards) <= draw_count:
                                selected_rewards = available_rewards
                            else:
                                if self.draw_mode == "random":
                                    # 允许重复抽取
                                    weights = [float(reward[2]) for reward in available_rewards]
                                    selected_rewards = random.choices(
                                        available_rewards,
                                        weights=weights,
                                        k=draw_count
                                    )
                                else:
                                    # 不重复抽取
                                    weights = [float(reward[2]) for reward in available_rewards]
                                    selected_rewards = []
                                    temp_rewards = available_rewards.copy()
                                    temp_weights = weights.copy()
                                    for _ in range(draw_count):
                                        if not temp_rewards:
                                            break
                                        chosen = random.choices(
                                            temp_rewards,
                                            weights=temp_weights,
                                            k=1
                                        )[0]
                                        selected_rewards.append(chosen)
                                        index = temp_rewards.index(chosen)
                                        temp_rewards.pop(index)
                                        temp_weights.pop(index)

                        # 更新历史记录
                        self._update_history(reward_name, selected_rewards)
                        
                        # 显示结果
                        if hasattr(self, 'container'):
                            self.container.deleteLater()
                            del self.container
                        if hasattr(self, 'reward_labels'):
                            for label in self.reward_labels:
                                label.deleteLater()

                        while self.result_grid.count(): 
                            item = self.result_grid.takeAt(0)
                            widget = item.widget()
                            if widget:
                                widget.deleteLater()

                        self.reward_labels = []
                        for num, selected, probability in selected_rewards:
                            reward_id_str = f"{num:02}"
                            label = BodyLabel(f"{reward_id_str} {selected}")

                            label.setAlignment(Qt.AlignCenter)
                            try:
                                with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                                    settings = json.load(f)
                                    font_size = settings['pumping_reward']['font_size']
                                    if font_size < 30:
                                        font_size = 85
                            except Exception as e:
                                font_size = 85
                                logger.error(f"加载字体设置时出错: {e}, 使用默认设置")
                            
                            if font_size < 30:
                                label.setFont(QFont(load_custom_font(), 85))
                            else:
                                label.setFont(QFont(load_custom_font(), font_size))
                            
                            self.reward_labels.append(label)

                        # 计算所有标签的宽度之和，并考虑间距和边距
                        if self.reward_labels:
                            total_width = sum(label.sizeHint().width() for label in self.reward_labels) + \
                                          len(self.reward_labels) * 180
                            available_width = self.width() - 20
                            
                            # 如果总宽度超过可用宽度，则计算每行最多能放几个标签
                            if total_width > available_width:
                                avg_label_width = total_width / len(self.reward_labels)
                                max_columns = max(1, int(available_width // avg_label_width))
                            else:
                                max_columns = len(self.reward_labels)  # 一行显示所有标签
                        else:
                            max_columns = 1
                        
                        # 复用 container 和 vbox_layout
                        if not hasattr(self, 'container'):
                            self.container = QWidget()
                            self.vbox_layout = QGridLayout()
                            self.container.setLayout(self.vbox_layout)
                        else:
                            # 清空旧标签
                            for i in reversed(range(self.vbox_layout.count())):
                                item = self.vbox_layout.itemAt(i)
                                if item.widget():
                                    item.widget().setParent(None)

                        for i, label in enumerate(self.reward_labels):
                            row = i // max_columns
                            col = i % max_columns
                            self.vbox_layout.addWidget(label, row, col)
                        
                        self.result_grid.addWidget(self.container)
                        
                        if self.draw_mode in ["until_reboot", "until_all"]:
                            # 更新抽取记录
                            drawn_rewards.extend([s[1].replace(' ', '') for s in selected_rewards])
                            with open(draw_record_file, 'w', encoding='utf-8') as f:
                                json.dump(drawn_rewards, f, ensure_ascii=False, indent=4)

                        self.update_total_count()
                        return
                    else:
                        if self.draw_mode in ["until_reboot", "until_all"]:
                            # 删除临时文件
                            if os.path.exists(draw_record_file):
                                os.remove(draw_record_file)

                        self.random()
                        return

        else:
            self.clear_layout(self.result_grid)
            if hasattr(self, 'container'):
                self.container.deleteLater()
                del self.container
            if hasattr(self, 'reward_labels'):
                for label in self.reward_labels:
                    label.deleteLater()
            
            # 创建错误标签
            error_label = BodyLabel("-- 抽选失败")
            error_label.setAlignment(Qt.AlignCenter)
            
            # 获取字体大小设置
            font_size = 85
            try:
                with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                    settings = json.load(f)
                    font_size = max(settings['pumping_reward']['font_size'], 30)
            except Exception as e:
                logger.error(f"加载字体设置时出错: {e}, 使用默认设置")
            
            error_label.setFont(QFont(load_custom_font(), font_size))
            self.result_grid.addWidget(error_label)

    # 清除旧布局和标签
    def clear_layout(self, layout):
        while layout.count():
            item = layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()

    # 获取随机抽取方法的设置
    def get_random_method_setting(self):
        """获取随机抽取方法的设置"""
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                random_method = settings['pumping_reward']['draw_pumping']
                return random_method
        except Exception as e:
            logger.error(f"加载随机抽取方法设置时出错: {e}, 使用默认设置")
            return 0

    # 更新历史记录
    def _update_history(self, reward_name, selected_rewards):
        """更新历史记录"""
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                reward_history_enabled = settings['history']['reward_history_enabled']
        except Exception as e:
            reward_history_enabled = False
            logger.error(f"加载历史记录设置时出错: {e}, 使用默认设置")
        
        if not reward_history_enabled:
            logger.info("历史记录功能已被禁用。")
            return
        
        history_file = f"app/resource/reward/history/{reward_name}.json"
        os.makedirs(os.path.dirname(history_file), exist_ok=True)
        
        history_data = {}
        if os.path.exists(history_file):
            with open(history_file, 'r', encoding='utf-8') as f:
                try:
                    history_data = json.load(f)
                except json.JSONDecodeError:
                    history_data = {}
        
        # 初始化数据结构
        if "pumping_reward" not in history_data:
            history_data["pumping_reward"] = {}
        if "total_rounds" not in history_data:
            history_data["total_rounds"] = 0
        
        # 更新总轮次
        history_data["total_rounds"] += 1

        current_time = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        
        # 更新被选中奖品的记录
        for reward_id, reward_name, probability in selected_rewards:
            # 获取奖品信息
            if reward_name not in history_data["pumping_reward"]:
                history_data["pumping_reward"][reward_name] = {
                    "total_number_of_times": 1,
                    "last_drawn_time": current_time,
                    "rounds_missed": 0,
                    "time": [{
                        "draw_method": self.draw_mode,
                        "draw_time": current_time,
                        "draw_reward_numbers": self.current_count
                    }]
                }
            else:
                history_data["pumping_reward"][reward_name]["total_number_of_times"] += 1
                history_data["pumping_reward"][reward_name]["last_drawn_time"] = current_time
                history_data["pumping_reward"][reward_name]["rounds_missed"] = 0
                history_data["pumping_reward"][reward_name]["time"].append({
                    "draw_method": self.draw_mode,
                    "draw_time": current_time,
                    "draw_reward_numbers": self.current_count
                })
        
        # 更新未被选中奖品的rounds_missed
        all_rewards = set()
        reward_file = f"app/resource/reward/{reward_name}.json"
        if os.path.exists(reward_file):
            with open(reward_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
                for reward_name, reward_info in data.items():
                    if isinstance(reward_info, dict) and 'id' in reward_info:
                        name = reward_name
                        all_rewards.add(name)
        
        selected_names = {s[1] for s in selected_rewards}
        for reward_name in all_rewards:
            if reward_name in history_data["pumping_reward"] and reward_name not in selected_names:
                history_data["pumping_reward"][reward_name]["rounds_missed"] += 1
        
        # 保存历史记录
        with open(history_file, 'w', encoding='utf-8') as f:
            json.dump(history_data, f, ensure_ascii=False, indent=4)

    # 更新总奖数显示   
    def update_total_count(self):
        """根据选择的奖池更新总奖数显示"""
        reward_name = self.reward_combo.currentText()

        self._update_count_display()

        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                pumping_reward_reward_quantity = f"{settings['pumping_reward']['reward_theme']}"
        except Exception:
            pumping_reward_reward_quantity = 0

        if reward_name and reward_name not in ["你暂未添加奖池", "加载奖池列表失败"]:
            reward_file = f"app/resource/reward/{reward_name}.json"
            if os.path.exists(reward_file):
                cleaned_data = self._get_cleaned_data(reward_file)
                drawn_count = self._get_drawn_count(reward_name)
                _count = len(cleaned_data) - drawn_count
                count = len(cleaned_data)
                if _count <= 0:
                    _count = count
                    InfoBar.success(
                        title='抽取完成',
                        content="抽取完了所有奖品了！记录已清除!",
                        orient=Qt.Horizontal,
                        parent=self,
                        isClosable=True,
                        duration=3000,
                        position=InfoBarPosition.TOP
                    )
                if pumping_reward_reward_quantity == 1:
                    self.total_label = BodyLabel('总奖数: 0')
                    self.total_label.setText(f'总奖数: {count}')
                if pumping_reward_reward_quantity == 2:
                    self.total_label = BodyLabel('剩余奖数: 0')
                    self.total_label.setText(f'剩余奖数: {_count}')
                else:
                    self.total_label.setText(f'总奖数: {count} | 剩余奖数: {_count}')
                self.max_count = count
                self._update_count_display()
            else:
                self._set_default_count(pumping_reward_reward_quantity)
        else:
            self._set_default_count(pumping_reward_reward_quantity)

    # 对用户的选择进行返回奖品数量数量
    def _get_cleaned_data(self, reward_file):
        with open(reward_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
            # 初始化不同情况的列表
            cleaned_data = []
            for reward_name, reward_info in data.items():
                if isinstance(reward_info, dict) and 'id' in reward_info:
                    id = reward_info.get('id', '')
                    name = reward_name
                    probability = reward_info.get('probability', '')
                    cleaned_data.append((id, name, probability))

            return cleaned_data

    # 获取已抽取奖数
    def _get_drawn_count(self, reward_name):
        if self.draw_mode in ["until_reboot", "until_all"]:
            if self.draw_mode == "until_reboot":
                draw_record_file = f"app/resource/Temp/until_the_reboot_{reward_name}.json"
            elif self.draw_mode == "until_all":
                draw_record_file = f"app/resource/Temp/until_all_draw_{reward_name}.json"
            if os.path.exists(draw_record_file):
                try:
                    with open(draw_record_file, 'r', encoding='utf-8') as f:
                        return len(json.load(f))
                except Exception as e:
                    # 处理加载文件出错的情况，返回 0
                    logger.error(f"加载抽取记录文件 {draw_record_file} 出错: {e}")
                    return 0
            else:
                return 0
        else:
            return 0

    # 设置默认总奖数显示
    def _set_default_count(self, pumping_reward_reward_quantity):
        if pumping_reward_reward_quantity == 1:
            self.total_label = BodyLabel('总奖数: 0')
        if pumping_reward_reward_quantity == 2:
            self.total_label = BodyLabel('剩余奖数: 0')
        else:
            self.total_label = BodyLabel('总奖数: 0 | 剩余奖数: 0')
        self.max_count = 0
    
    # 增加抽取奖数
    def _increase_count(self):
        """增加抽取奖数"""
        if self.current_count < self.max_count:
            self.current_count += 1
            self._update_count_display()

    # 减少抽取奖数        
    def _decrease_count(self):
        """减少抽取奖数"""
        if self.current_count > 1:
            self.current_count -= 1
            self._update_count_display()

    # 更新奖数显示        
    def _update_count_display(self):
        """更新奖数显示"""
        self.count_label.setText(str(self.current_count))
        
        # 根据当前奖数启用/禁用按钮
        self.plus_button.setEnabled(self.current_count < self.max_count)
        self.minus_button.setEnabled(self.current_count > 1)
        self.start_button.setEnabled(self.current_count <= self.max_count and self.current_count > 0)
    
    # 刷新奖池列表         
    def refresh_reward_list(self):
        """刷新奖池下拉框选项"""
        self.reward_combo.clear()
        try:
            list_folder = "app/resource/reward"
            if os.path.exists(list_folder) and os.path.isdir(list_folder):
                files = os.listdir(list_folder)
                classes = []
                for file in files:
                    if file.endswith('.json'):
                        reward_name = os.path.splitext(file)[0]
                        classes.append(reward_name)
                
                self.reward_combo.clear()
                if classes:
                    self.reward_combo.addItems(classes)
                    logger.info("加载奖池列表成功！")
                else:
                    logger.error("你暂未添加奖池")
                    self.reward_combo.addItem("你暂未添加奖池")
            else:
                logger.error("你暂未添加奖池")
                self.reward_combo.addItem("你暂未添加奖池")
        except Exception as e:
            logger.error(f"加载奖池名称失败: {str(e)}")

        self.update_total_count()

        InfoBar.success(
            title='奖池列表',
            content="奖池列表更新成功！",
            orient=Qt.Horizontal,
            parent=self,
            isClosable=True,
            duration=3000,
            position=InfoBarPosition.TOP
        )
    
    # 恢复初始状态
    def _reset_to_initial_state(self):
        """恢复初始状态"""
        self._clean_temp_files()
        self.current_count = 1
        self.clear_layout(self.result_grid)
        self.update_total_count()

    # 清理临时文件
    def _clean_temp_files(self):
        import glob
        temp_dir = "app/resource/Temp"
        if os.path.exists(temp_dir):
            for file in glob.glob(f"{temp_dir}/until_the_reboot_*.json"):
                try:
                    os.remove(file)
                    logger.info(f"已清理临时抽取记录文件: {file}")
                except Exception as e:
                    logger.error(f"清理临时抽取记录文件失败: {e}")

    # 初始化UI
    def initUI(self):
        # 加载设置
        try:
            with open('app/Settings/Settings.json', 'r', encoding='utf-8') as f:
                settings = json.load(f)
                pumping_reward_reward_quantity = f"{settings['pumping_reward']['reward_theme']}"
                pumping_reward_prize_pools_quantity = f"{settings['pumping_reward']['prize_pools_quantity']}"
                pumping_reward_refresh_button = f"{settings['pumping_reward']['refresh_button']}"
                pumping_reward_list_refresh_button = f"{settings['pumping_reward']['list_refresh_button']}"
        except Exception as e:
            logger.error(f"加载设置时出错: {e}, 使用默认设置")
            pumping_reward_reward_quantity = 0
            pumping_reward_prize_pools_quantity = True
            pumping_reward_refresh_button = True
            pumping_reward_list_refresh_button = True
            
        # 根据设置控制UI元素显示状态
        show_reward_quantity = pumping_reward_reward_quantity
        if show_reward_quantity != 3:
            show_reward_quantity = True
        else:
            show_reward_quantity = False
        
        show_prize_pools_quantity = pumping_reward_prize_pools_quantity
        if show_prize_pools_quantity == 'True':
            show_prize_pools_quantity = True
        else:
            show_prize_pools_quantity = False

        show_list_refresh_button = pumping_reward_list_refresh_button
        if show_list_refresh_button == 'True':
            show_list_refresh_button = True
        else:
            show_list_refresh_button = False

        show_refresh_button = pumping_reward_refresh_button
        if show_refresh_button == 'True':
            show_refresh_button = True
        else:
            show_refresh_button = False
        # 主布局
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        # 设置滚动条样式
        scroll_area.setStyleSheet("""
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
        # 启用触屏滚动
        QScroller.grabGesture(scroll_area.viewport(), QScroller.LeftMouseButtonGesture)
        
        # 创建主容器和布局
        container = QWidget(scroll_area)
        scroll_area_container = QVBoxLayout(container)
        
        # 控制面板
        control_panel = QVBoxLayout()
        control_panel.setContentsMargins(10, 10, 50, 10)

        # 刷新按钮
        self.refresh_button = PushButton('重置记录')
        self.refresh_button.setFixedSize(200, 50)
        self.refresh_button.setFont(QFont(load_custom_font(), 15))
        self.refresh_button.clicked.connect(self._reset_to_initial_state)
        self.refresh_button.setVisible(show_refresh_button)
        control_panel.addWidget(self.refresh_button, 0, Qt.AlignVCenter)

        # 刷新按钮
        self.refresh_button = PushButton('刷新列表')
        self.refresh_button.setFixedSize(200, 50)
        self.refresh_button.setFont(QFont(load_custom_font(), 15))
        self.refresh_button.clicked.connect(self.refresh_reward_list)
        self.refresh_button.setVisible(show_refresh_button)
        control_panel.addWidget(self.refresh_button, 0, Qt.AlignVCenter)

        # 创建一个水平布局
        horizontal_layout = QHBoxLayout()

        # 减号按钮
        self.minus_button = PushButton('-')
        self.minus_button.setFixedSize(60, 50)
        self.minus_button.setFont(QFont(load_custom_font(), 30))
        self.minus_button.clicked.connect(self._decrease_count)
        horizontal_layout.addWidget(self.minus_button, 0, Qt.AlignLeft)

        # 奖数显示
        self.count_label = BodyLabel('1')
        self.count_label.setAlignment(Qt.AlignCenter)
        self.count_label.setFont(QFont(load_custom_font(), 30))
        self.count_label.setFixedWidth(65)
        horizontal_layout.addWidget(self.count_label, 0, Qt.AlignLeft)

        # 加号按钮
        self.plus_button = PushButton('+')
        self.plus_button.setFixedSize(60, 50)
        self.plus_button.setFont(QFont(load_custom_font(), 30))
        self.plus_button.clicked.connect(self._increase_count)
        horizontal_layout.addWidget(self.plus_button, 0, Qt.AlignLeft)

        # 将水平布局添加到控制面板
        control_panel.addLayout(horizontal_layout)

        # 开始按钮
        self.start_button = PrimaryPushButton('开始')
        self.start_button.setFixedSize(200, 50)
        self.start_button.setFont(QFont(load_custom_font(), 20))
        self.start_button.clicked.connect(self.start_draw)
        control_panel.addWidget(self.start_button, 0, Qt.AlignVCenter)
        
        # 奖池下拉框
        self.reward_combo = ComboBox()
        self.reward_combo.setFixedSize(200, 50)
        self.reward_combo.setFont(QFont(load_custom_font(), 15))
        self.reward_combo.setVisible(show_prize_pools_quantity)
        
        # 加载奖池列表
        try:
            list_folder = "app/resource/reward"
            if os.path.exists(list_folder) and os.path.isdir(list_folder):
                files = os.listdir(list_folder)
                classes = []
                for file in files:
                    if file.endswith('.json'):
                        reward_name = os.path.splitext(file)[0]
                        classes.append(reward_name)
                
                self.reward_combo.clear()
                if classes:
                    self.reward_combo.addItems(classes)
                    logger.info("加载奖池列表成功！")
                else:
                    logger.error("你暂未添加奖池")
                    self.reward_combo.addItem("你暂未添加奖池")
            else:
                logger.error("你暂未添加奖池")
                self.reward_combo.addItem("你暂未添加奖池")
        except Exception as e:
            logger.error(f"加载奖池列表失败: {str(e)}")
            self.reward_combo.addItem("加载奖池列表失败")
        
        control_panel.addWidget(self.reward_combo)
        
        # 总奖数和剩余奖数显示
        if pumping_reward_reward_quantity == 1:
            self.total_label = BodyLabel('总奖数: 0')
        if pumping_reward_reward_quantity == 2:
            self.total_label = BodyLabel('剩余奖数: 0')
        else:
            self.total_label = BodyLabel('总奖数: 0 | 剩余奖数: 0')
        self.total_label.setFont(QFont(load_custom_font(), 12))
        self.total_label.setAlignment(Qt.AlignCenter)
        self.total_label.setFixedWidth(200)
        self.total_label.setVisible(show_reward_quantity)
        control_panel.addWidget(self.total_label, 0, Qt.AlignLeft)
        
        control_panel.addStretch(1)
        
        # 结果区域布局
        self.result_grid = QGridLayout()
        self.result_grid.setSpacing(10)
        self.result_grid.setAlignment(Qt.AlignTop)

        scroll_area_container.addLayout(self.result_grid)
        
        # 奖池选择变化时更新总奖数
        self.reward_combo.currentTextChanged.connect(self.update_total_count)
        
        # 初始化抽取奖数
        self.current_count = 1
        self.max_count = 0
        
        # 初始化总奖数显示
        self.update_total_count()
        
        # 设置容器并应用布局
        main_layout = QHBoxLayout(self)

        control_button_layout = QVBoxLayout()

        control_button_layout.addStretch(5)
        
        # 将control_panel布局包裹在QWidget中
        control_panel_widget = QWidget()
        control_panel_widget.setLayout(control_panel)
        control_button_layout.addWidget(control_panel_widget, 0, Qt.AlignBottom)

        # 将scroll_area添加到主布局中
        scroll_area.setWidget(container)
        # 创建一个QWidget来包含control_button_layout
        control_button_widget = QWidget()
        control_button_widget.setLayout(control_button_layout)
        # 将control_button_widget添加到主布局中
        main_layout.addWidget(control_button_widget)
        main_layout.addWidget(scroll_area)

        # 显示主布局
        self.setLayout(main_layout)