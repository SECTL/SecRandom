import asyncio
import edge_tts
from loguru import logger
from PySide6.QtCore import QThread, Signal


class EdgeTTSWorker(QThread):
    """Edge TTS语音列表获取线程"""

    voices_fetched = Signal(list)
    error_occurred = Signal(str)

    def run(self):
        """运行线程，获取Edge TTS语音列表"""
        try:
            voices = self.get_voices()
            self.voices_fetched.emit(voices)
        except Exception as e:
            logger.warning(f"获取Edge TTS语音列表失败: {e}")
            self.error_occurred.emit(str(e))

    def get_voices(self):
        """获取Edge TTS语音列表"""
        try:
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            voices = loop.run_until_complete(edge_tts.list_voices())
            loop.close()

            logger.debug(f"Edge TTS API返回原始语音数量: {len(voices)}")

            if voices:
                logger.debug(f"第一个语音的结构: {voices[0]}")
                logger.debug(f"第一个语音的键: {list(voices[0].keys())}")

            filtered_voices = []
            total_processed = 0
            passed_filter = 0

            for v in voices:
                try:
                    total_processed += 1
                    if "Name" in v and "ShortName" in v and "Locale" in v:
                        passed_filter += 1
                        name = v["Name"]
                        display_name = v.get("DisplayName", name)
                        short_name = v["ShortName"]
                        locale = v["Locale"]
                        gender = v.get("Gender", "Unknown")
                        voice_type = v.get("VoiceType", "Unknown")

                        voice_id = short_name

                        filtered_voices.append(
                            {
                                "name": display_name,
                                "id": voice_id,
                                "languages": locale.replace("_", "-"),
                                "full_info": f"{gender} | {locale} | Type: {voice_type}",
                            }
                        )
                except Exception as e:
                    logger.warning(f"处理语音 {v} 时出错: {e}")
                    if total_processed <= 5:
                        logger.debug(f"出错语音结构: {v}")
                    continue

            logger.debug(f"成功获取Edge TTS语音列表，共{len(filtered_voices)}个语音")
            logger.debug(
                f"处理统计: 总共{total_processed}个, 通过过滤{passed_filter}个, 最终{len(filtered_voices)}个"
            )

            if not filtered_voices:
                logger.warning("过滤后语音列表为空，返回默认语音")
                return self.get_default_voices()

            filtered_voices.sort(
                key=lambda x: ("zh-CN" not in x["languages"], x["name"])
            )
            logger.debug(
                f"语音列表已排序，中文语音优先显示，共{len(filtered_voices)}个语音"
            )

            return filtered_voices
        except Exception as e:
            logger.warning(f"获取Edge TTS语音列表失败: {e}")
            return self.get_default_voices()

    def get_default_voices(self):
        """获取默认语音列表"""
        default_voices = [
            {
                "name": "Xiaoxiao (Chinese Female)",
                "id": "zh-CN-XiaoxiaoNeural",
                "languages": "zh-CN",
                "full_info": "Female | zh-CN | Type: Neural",
            },
            {
                "name": "Yunxi (Chinese Male)",
                "id": "zh-CN-YunxiNeural",
                "languages": "zh-CN",
                "full_info": "Male | zh-CN | Type: Neural",
            },
            {
                "name": "Xiaoyi (Chinese Female)",
                "id": "zh-CN-XiaoyiNeural",
                "languages": "zh-CN",
                "full_info": "Female | zh-CN | Type: Neural",
            },
            {
                "name": "Jenny (English Female)",
                "id": "en-US-JennyNeural",
                "languages": "en-US",
                "full_info": "Female | en-US | Type: Neural",
            },
            {
                "name": "Guy (English Male)",
                "id": "en-US-GuyNeural",
                "languages": "en-US",
                "full_info": "Male | en-US | Type: Neural",
            },
        ]
        logger.info("使用默认语音列表")
        return default_voices
