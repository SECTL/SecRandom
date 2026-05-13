# 语音设置语言配置
voice_settings = {
    "ZH_CN": {"title": {"name": "语音设置", "description": "配置语音播报相关功能"}},
    "EN_US": {
        "title": {
            "name": "Voice settings",
            "description": "Configure voice playback related functions",
        }
    },
    "JA_JP": {
        "title": {"name": "音声設定", "description": "音声再生関連機能を設定"},
    },
}

# 基础语音设置语言配置
basic_voice_settings = {
    "ZH_CN": {
        "title": {"name": "基础语音设置", "description": "配置基础语音播报功能"},
        "voice_engine_group": {
            "name": "语音引擎",
            "description": "选择语音合成引擎类型",
        },
        "volume_group": {"name": "语音设置", "description": "调整语音播报相关设置"},
        "voice_wait_group": {
            "name": "防止连续点击",
            "description": "防止在语音播报完成前连续抽取",
        },
        "voice_enable": {
            "name": "语音功能开关",
            "description": "开启或关闭语音播报功能",
            "switchbutton_name": {"enable": "开启", "disable": "关闭"},
        },
        "voice_wait_complete": {
            "name": "播报完成等待",
            "description": "开启后，等待当前语音播报完成才允许下一次抽取",
        },
        "voice_engine": {
            "name": "语音引擎",
            "description": "选择语音合成引擎类型",
            "combo_items": ["系统TTS", "Edge TTS"],
        },
        "edge_tts_voice_name": {
            "name": "Edge TTS 语音角色",
            "description": "选择Edge TTS的语音播报角色",
            "combo_items": [
                "zh-CN-XiaoxiaoNeural",
                "zh-CN-YunxiNeural",
                "zh-CN-XiaoyiNeural",
                "en-US-JennyNeural",
                "en-US-GuyNeural",
            ],
        },
        "volume_size": {"name": "播报音量", "description": "调整语音播报的音量大小"},
        "speech_rate": {"name": "语速调节", "description": "调整语音播报的语速"},
        "system_volume_control": {
            "name": "系统音量控制",
            "description": "是否开启系统音量自动控制",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "system_volume_size": {
            "name": "系统音量大小",
            "description": "设置系统音量的大小",
        },
    },
    "EN_US": {
        "title": {
            "name": "Basic voice settings",
            "description": "Configure basic voice setting",
        },
        "voice_engine_group": {
            "name": "Voice engine",
            "description": "Choose TTS engine type",
        },
        "volume_group": {
            "name": "Volume settings",
            "description": "Adjust speech volume",
        },
        "voice_wait_group": {
            "name": "Prevent rapid clicking",
            "description": "Prevent consecutive draws before voice announcement completes",
        },
        "system_volume_group": {
            "name": "System volume control",
            "description": "Select volume type to control",
        },
        "voice_engine": {
            "name": "Voice engine",
            "description": "Choose TTS engine",
            "combo_items": {"0": "System TTS", "1": "Edge TTS"},
        },
        "edge_tts_voice_name": {
            "name": "Voice name of Edge TTS",
            "description": "Select the voice character of Edge TTS",
            "combo_items": {
                "0": "zh-CN-XiaoxiaoNeural",
                "1": "zh-CN-YunxiNeural",
                "2": "zh-CN-XiaoyiNeural",
                "3": "en-US-JennyNeural",
                "4": "en-US-GuyNeural",
            },
        },
        "voice_playback": {
            "name": "Voice playback device",
            "description": "Choose voice playback device",
            "combo_items": {
                "0": "System default",
                "1": "Speakers",
                "2": "Headphones",
                "3": "Bluetooth devices",
            },
        },
        "volume_size": {"name": "Speech volume", "description": "Adjust speech volume"},
        "speech_rate": {"name": "Speech rate", "description": "Adjust speech rate"},
        "system_volume_control": {
            "name": "System volume control",
            "description": "Select volume type to control",
            "combo_items": {
                "0": "Main volume",
                "1": "App volume",
                "2": "System sound",
                "3": "Microphone volume",
            },
        },
        "system_volume_size": {
            "name": "System volume",
            "description": "Adjust system volume",
        },
        "voice_enable": {
            "name": "Voice toggle",
            "description": "Enable or disable voice announcements",
            "switchbutton_name": {"enable": "Enabled", "disable": "Disabled"},
        },
        "voice_wait_complete": {
            "name": "Wait for announcement",
            "description": "When enabled, wait for the current announcement to complete before allowing the next draw",
        },
    },
    "JA_JP": {
        "title": {
            "name": "基本音声設定",
            "description": "基本音声再生機能を設定",
        },
        "voice_engine_group": {
            "name": "音声エンジン",
            "description": "音声合成エンジンタイプを選択",
        },
        "volume_group": {
            "name": "音声設定",
            "description": "音声再生関連設定を調整",
        },
        "voice_wait_group": {
            "name": "連続クリック防止",
            "description": "音声再生が完了する前に連続抽選が行われないようにする",
        },
        "system_volume_group": {
            "name": "システム音量制御",
            "description": "制御する音量タイプを選択",
        },
        "voice_engine": {
            "name": "音声エンジン",
            "description": "音声合成エンジンタイプを選択",
            "combo_items": {"0": "システムTTS", "1": "Edge TTS"},
        },
        "edge_tts_voice_name": {
            "name": "Edge TTS音声キャラクター",
            "description": "Edge TTSの音声再生キャラクターを選択",
            "combo_items": {
                "0": "zh-CN-XiaoxiaoNeural",
                "1": "zh-CN-YunxiNeural",
                "2": "zh-CN-XiaoyiNeural",
                "3": "en-US-JennyNeural",
                "4": "en-US-GuyNeural",
            },
        },
        "voice_playback": {
            "name": "音声再生デバイス",
            "description": "音声再生デバイスを選択",
            "combo_items": {
                "0": "システムデフォルト",
                "1": "スピーカー",
                "2": "ヘッドフォン",
                "3": "Bluetoothデバイス",
            },
        },
        "volume_size": {"name": "再生音量", "description": "音声再生の音量を調整"},
        "speech_rate": {"name": "再生速度", "description": "音声再生の速度を調整"},
        "system_volume_control": {
            "name": "システム音量制御",
            "description": "システム音量自動制御を有効にするかどうか",
            "combo_items": {
                "0": "メイン音量",
                "1": "アプリ音量",
                "2": "システムサウンド",
                "3": "マイク音量",
            },
        },
        "system_volume_size": {
            "name": "システム音量",
            "description": "システム音量を調整",
        },
        "voice_enable": {
            "name": "音声スイッチ",
            "description": "音声再生を有効または無効にする",
            "switchbutton_name": {"enable": "有効", "disable": "無効"},
        },
        "voice_wait_complete": {
            "name": "再生完了待機",
            "description": "有効にすると、現在の音声再生が完了するまで次の抽選を許可しません",
        },
    },
}

# 特定播报设置语言配置
specific_announcements = {
    "ZH_CN": {
        "title": {"name": "特定播报设置", "description": "配置特定结果的语音播报"},
        "enabled": {
            "name": "特定播报开关",
            "description": "开启或关闭特定结果语音播报的总开关",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "header": {"name": "开关"},
        "mode": {
            "name": "播报模式",
            "description": "选择语音播报模式",
            "combo_items": ["点名", "抽奖"],
        },
        "roll_call_title": {
            "name": "点名模式配置",
            "description": "配置点名模式下的语音播报",
        },
        "select_class_name": {
            "name": "选择班级/奖池",
            "description": "选择要管理TTS的班级或奖池",
        },
        "id_field": {"name": "学号"},
        "name_field": {"name": "姓名"},
        "prefix_field": {
            "name": "播报前缀",
            "description": "在播报内容前添加自定义文本",
        },
        "suffix_field": {
            "name": "播报后缀",
            "description": "在播报内容后添加自定义文本",
        },
        "lottery_title": {
            "name": "抽奖模式配置",
            "description": "配置抽奖模式下的语音播报",
        },
        "lottery_id_field": {"name": "序号"},
        "lottery_name_field": {"name": "名称"},
        "lottery_prefix_field": {
            "name": "抽奖前缀",
            "description": "在抽奖播报内容前添加自定义文本",
        },
        "lottery_suffix_field": {
            "name": "抽奖后缀",
            "description": "在抽奖播报内容后添加自定义文本",
        },
        "tts_alias": {
            "name": "替换名称",
            "description": "用于TTS发音的替换名称，留空则使用默认发音",
        },
    },
    "EN_US": {
        "title": {
            "name": "Specific speech setting",
            "description": "Speech for specific results",
        },
        "enabled": {
            "name": "Enable specific speech",
            "description": "Totally enable speech for specific results",
        },
        "header": {"name": "Enabled"},
        "mode": {
            "name": "Announcements mode",
            "description": "Choose a speech mode",
            "combo_items": {"0": "Picking", "1": "Lottery"},
        },
        "roll_call_title": {
            "name": "Picking mode configuration",
            "description": "Configure speech on drawing mode",
        },
        "select_class_name": {
            "name": "Select a class/pool",
            "description": "Choose a class or pool to manage TTS",
        },
        "id_field": {"name": "Student ID"},
        "name_field": {"name": "Name"},
        "prefix_field": {
            "name": "Speech prefix",
            "description": "Add text before speech content",
        },
        "suffix_field": {
            "name": "Speech suffix",
            "description": "Add text after speech content",
        },
        "lottery_title": {
            "name": "Lottery mode configuration",
            "description": "Configure speech on lottery mode",
        },
        "lottery_id_field": {"name": "Serial"},
        "lottery_name_field": {"name": "Name"},
        "lottery_prefix_field": {
            "name": "Lottery prefix",
            "description": "Add text before speech content",
        },
        "lottery_suffix_field": {
            "name": "Lottery suffix",
            "description": "Add text after speech content",
        },
        "tts_alias": {
            "name": "Replacement",
            "description": "Replacement names for TTS pronunciation. Leave a blank to use default pronunciation",
        },
    },
    "JA_JP": {
        "title": {
            "name": "特定音声設定",
            "description": "特定結果の音声再生を設定",
        },
        "enabled": {
            "name": "特定音声を有効化",
            "description": "特定結果の音声再生を完全に有効化",
        },
        "header": {"name": "有効"},
        "mode": {
            "name": "アナウンスモード",
            "description": "音声モードを選択",
            "combo_items": {"0": "点呼", "1": "抽選"},
        },
        "roll_call_title": {
            "name": "点呼モード設定",
            "description": "点呼モードでの音声再生を設定",
        },
        "select_class_name": {
            "name": "クラス/賞プールを選択",
            "description": "TTSを管理するクラスまたは賞プールを選択",
        },
        "id_field": {"name": "学籍番号"},
        "name_field": {"name": "氏名"},
        "prefix_field": {
            "name": "音声接頭辞",
            "description": "音声コンテンツの前にテキストを追加",
        },
        "suffix_field": {
            "name": "音声接尾辞",
            "description": "音声コンテンツの後にテキストを追加",
        },
        "lottery_title": {
            "name": "抽選モード設定",
            "description": "抽選モードでの音声再生を設定",
        },
        "lottery_id_field": {"name": "番号"},
        "lottery_name_field": {"name": "名称"},
        "lottery_prefix_field": {
            "name": "抽選接頭辞",
            "description": "抽選音声コンテンツの前にテキストを追加",
        },
        "lottery_suffix_field": {
            "name": "抽選接尾辞",
            "description": "抽選音声コンテンツの後にテキストを追加",
        },
        "tts_alias": {
            "name": "置換",
            "description": "TTS発音用の置換名。空白の場合はデフォルト発音を使用",
        },
    },
}
