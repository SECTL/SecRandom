# 通知设置语言配置
notification_settings = {
    "ZH_CN": {"title": {"name": "通知设置", "description": "通知功能设置"}},
    "EN_US": {
        "title": {
            "name": "Notification settings",
            "description": "Notification settings",
        }
    },
    "JA_JP": {
        "title": {"name": "通知設定", "description": "通知機能設定"},
    },
}

# 通用通知文本
notification_common = {
    "ZH_CN": {
        "notification_result": {
            "name": "通知结果",
            "description": "通用通知结果窗口标题",
        },
        "auto_close_hint": "{0}秒后自动关闭\n连续点击3次关闭窗口",
        "manual_close_hint": "连续点击3次关闭窗口",
    },
    "EN_US": {
        "notification_result": {
            "name": "Notice results",
            "description": "Generic notification result window title",
        },
        "auto_close_hint": "Auto close in {0}s\nClick 3 times to close window",
        "manual_close_hint": "Click 3 times to close window",
    },
    "JA_JP": {
        "notification_result": {
            "name": "通知結果",
            "description": "汎用通知結果ウィンドウタイトル",
        },
        "auto_close_hint": "{0}秒後に自動的に閉じる\n3回連続クリックでウィンドウを閉じる",
        "manual_close_hint": "3回連続クリックでウィンドウを閉じる",
    },
}

# 点名通知设置语言配置
roll_call_notification_settings = {
    "ZH_CN": {
        "title": {"name": "点名通知设置", "description": "点名通知功能设置"},
        "basic_settings": {"name": "基础设置", "description": "配置通知显示基础参数"},
        "floating_window_mode": {
            "name": "浮窗模式",
            "description": "配置点名通知浮窗行为",
        },
        "classisland_notification_service_settings": {
            "name": "ClassIsland通知服务",
            "description": "配置ClassIsland通知服务参数",
        },
        "call_notification_service": {
            "name": "调用通知服务",
            "description": "启用后调用系统通知服务发送点名结果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "动画效果",
            "description": "控制点名通知窗口动画效果显示",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_enabled_monitor": {
            "name": "显示器选择",
            "description": "选择显示点名通知浮窗的显示器",
        },
        "floating_window_position": {
            "name": "浮窗位置",
            "description": "设置点名通知浮窗屏幕显示位置",
            "combo_items": [
                "中心",
                "顶部",
                "底部",
                "左侧",
                "右侧",
                "顶部左侧",
                "顶部右侧",
                "底部左侧",
                "底部右侧",
            ],
        },
        "floating_window_horizontal_offset": {
            "name": "水平偏移",
            "description": "调整点名通知浮窗相对默认位置水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "调整点名通知浮窗相对默认位置垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "不透明度",
            "description": "调整点名通知浮窗不透明度，数值越小越透明（0-100）",
        },
        "floating_window_auto_close_time": {
            "name": "浮窗自动关闭时间",
            "description": "设置浮窗自动关闭时间（秒），设为0表示不自动关闭",
        },
        "use_main_window_when_exceed_threshold": {
            "name": "超过阈值时不显示浮窗通知",
            "description": "当抽取人数超过设定阈值时，不显示浮窗通知，只在主窗口显示结果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "main_window_display_threshold": {
            "name": "浮窗通知阈值",
            "description": "设置触发浮窗通知的人数阈值，超过此数值则不显示浮窗通知，最小值为1",
        },
        "notification_service_type": {
            "name": "通知服务类型",
            "description": "选择通知服务类型，SecRandom或ClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom+ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "通知显示时长",
            "description": "设置ClassIsland通知显示时长（秒）",
        },
        "do_not_steal_focus": {
            "name": "无焦点模式",
            "description": "点名完成后不抢占焦点，保持原有顶层软件焦点",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "classisland_notification_hint": {
            "title": "ClassIsland通知服务",
            "content": "请确保已安装.NET 8运行时，并在ClassIsland中安装SecRandom-Ci插件",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland通知服务",
            "content": "检测到未安装插件，请在ClassIsland中安装SecRandom-Ci插件",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland通知服务",
            "content": "检测到ClassIsland和SecRandom-Ci插件正常运行，欢迎使用",
        },
    },
    "EN_US": {
        "title": {
            "name": "Picking notification settings",
            "description": "Picking notification settings",
        },
        "basic_settings": {
            "name": "Basic settings",
            "description": "Configure notification display base parameters",
        },
        "window_mode": {
            "name": "Window mode",
            "description": "Configure generic pick notification window display method",
        },
        "floating_window_mode": {
            "name": "Floating window mode",
            "description": "Configure generic pick notification floating window behavior mode",
        },
        "call_notification_service": {
            "name": "Call notification service",
            "description": "Call the system notification service to send the picking result when enabled",
        },
        "animation": {
            "name": "Animation",
            "description": "Configure Pick notification window display animation effect",
        },
        "floating_window_enabled_monitor": {
            "name": "Monitor select",
            "description": "Select the display monitor for picking notification floating windows",
        },
        "floating_window_position": {
            "name": "Floating window position",
            "description": "Configure picking notification floating window on-screen display position",
            "combo_items": {
                "0": "Center",
                "1": "Top",
                "2": "Bottom",
                "3": "Left",
                "4": "Right",
                "5": "Top left",
                "6": "Top right",
                "7": "Bottom left",
                "8": "Bottom right",
            },
        },
        "floating_window_horizontal_offset": {
            "name": "Horizontal offset",
            "description": "Configure the horizontal offset (in pixels) for the pick notification floating window relative to the default position",
        },
        "floating_window_vertical_offset": {
            "name": "Vertical offset",
            "description": "Configure the vertical offset (in pixels) for the pick notification floating window relative to the default position",
        },
        "floating_window_transparency": {
            "name": "Transparency",
            "description": "Configure pick notification floating window transparency, where a smaller value indicates higher transparency (0-100)",
        },
        "floating_window_auto_close_time": {
            "name": "Floating window auto-close time",
            "description": "Set the time to close the floating window automatically (second), set to 0 to not close automatically",
        },
        "use_main_window_when_exceed_threshold": {
            "name": "Use main window when exceeding threshold",
            "description": "When the picked count exceeds the threshold, show results in the main window only (no floating notification)",
        },
        "main_window_display_threshold": {
            "name": "Main window threshold",
            "description": "Set the picked count threshold for showing results in the main window (minimum: 1)",
        },
        "notification_service_type": {
            "name": "Notification service type",
            "description": "Select notification service type, SecRandom or ClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom + ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "Notification display duration",
            "description": "Set notification display duration (seconds)",
        },
        "do_not_steal_focus": {
            "name": "Focusless Mode",
            "description": "Do not steal focus after Quick Pick, keep focus on original top-level software",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland Notification Service",
            "content": "Please ensure .NET 8 runtime is installed and SecRandom-Ci plugin is installed in ClassIsland",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland Notification Service",
            "content": "Plugin not detected, please install SecRandom-Ci plugin in ClassIsland",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland Notification Service",
            "content": "Detected ClassIsland and SecRandom-Ci plugin running properly, welcome to use",
        },
    },
    "JA_JP": {
        "title": {
            "name": "点呼通知設定",
            "description": "点呼通知機能設定",
        },
        "basic_settings": {
            "name": "基本設定",
            "description": "通知表示基本パラメータを設定",
        },
        "window_mode": {
            "name": "ウィンドウモード",
            "description": "汎用点呼通知ウィンドウ表示方法を設定",
        },
        "floating_window_mode": {
            "name": "フローティングウィンドウモード",
            "description": "汎用点呼通知フローティングウィンドウ動作モードを設定",
        },
        "call_notification_service": {
            "name": "通知サービスを呼び出す",
            "description": "有効にするとシステム通知サービスを呼び出して点呼結果を送信",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "アニメーション",
            "description": "点呼通知ウィンドウ表示アニメーション効果を設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_enabled_monitor": {
            "name": "モニター選択",
            "description": "点呼通知フローティングウィンドウを表示するモニターを選択",
        },
        "floating_window_position": {
            "name": "フローティングウィンドウ位置",
            "description": "点呼通知フローティングウィンドウの画面表示位置を設定",
            "combo_items": {
                "0": "中央",
                "1": "上部",
                "2": "下部",
                "3": "左側",
                "4": "右側",
                "5": "左上",
                "6": "右上",
                "7": "左下",
                "8": "右下",
            },
        },
        "floating_window_horizontal_offset": {
            "name": "水平オフセット",
            "description": "点呼通知フローティングウィンドウのデフォルト位置からの水平オフセット（ピクセル）を設定",
        },
        "floating_window_vertical_offset": {
            "name": "垂直オフセット",
            "description": "点呼通知フローティングウィンドウのデフォルト位置からの垂直オフセット（ピクセル）を設定",
        },
        "floating_window_transparency": {
            "name": "不透明度",
            "description": "点呼通知フローティングウィンドウの不透明度を設定、値が小さいほど透明（0-100）",
        },
        "floating_window_auto_close_time": {
            "name": "フローティングウィンドウ自動クローズ時間",
            "description": "フローティングウィンドウの自動クローズ時間を設定（秒）、0に設定すると自動クローズなし",
        },
        "use_main_window_when_exceed_threshold": {
            "name": "閾値超過時にフローティングウィンドウ通知を表示しない",
            "description": "抽出人数が設定閾値を超える場合、フローティングウィンドウ通知を表示せず、メインウィンドウのみに結果を表示",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "main_window_display_threshold": {
            "name": "フローティングウィンドウ通知閾値",
            "description": "フローティングウィンドウ通知をトリガーする人数閾値を設定、この数値を超えるとフローティングウィンドウ通知を表示しない、最小値は1",
        },
        "notification_service_type": {
            "name": "通知サービスタイプ",
            "description": "通知サービスタイプを選択、SecRandomまたはClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom+ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "通知表示時間",
            "description": "ClassIsland通知表示時間を設定（秒）",
        },
        "do_not_steal_focus": {
            "name": "フォーカスなしモード",
            "description": "点呼完了後にフォーカスを奪わず、元のトップレベルソフトフォーカスを維持",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "classisland_notification_hint": {
            "title": "ClassIsland通知サービス",
            "content": ".NET 8ランタイムがインストールされ、ClassIslandにSecRandom-Ciプラグインがインストールされていることを確認してください",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland通知サービス",
            "content": "プラグインがインストールされていないことを検出しました、ClassIslandにSecRandom-Ciプラグインをインストールしてください",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland通知サービス",
            "content": "ClassIslandとSecRandom-Ciプラグインが正常に動作していることを検出しました、ご利用いただきありがとうございます",
        },
    },
}

# 闪抽通知设置
quick_draw_notification_settings = {
    "ZH_CN": {
        "title": {
            "name": "闪抽通知设置",
            "description": "配置闪抽结果通知显示方式和参数",
        },
        "basic_settings": {
            "name": "基础设置",
            "description": "配置闪抽通知基础显示参数",
        },
        "floating_window_mode": {
            "name": "浮窗模式",
            "description": "设置闪抽通知浮窗行为模式",
        },
        "classisland_notification_service_settings": {
            "name": "ClassIsland通知服务",
            "description": "配置ClassIsland通知服务参数",
        },
        "animation": {
            "name": "动画",
            "description": "设置闪抽通知窗口显示动画效果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_enabled_monitor": {
            "name": "选择闪抽通知显示的显示器",
            "description": "选择闪抽通知浮窗显示器",
        },
        "floating_window_position": {
            "name": "浮窗位置",
            "description": "设置闪抽通知浮窗屏幕显示位置",
            "combo_items": [
                "中心",
                "顶部",
                "底部",
                "左侧",
                "右侧",
                "顶部左侧",
                "顶部右侧",
                "底部左侧",
                "底部右侧",
            ],
        },
        "floating_window_horizontal_offset": {
            "name": "水平偏移",
            "description": "设置闪抽通知浮窗相对默认位置水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "设置闪抽通知浮窗相对默认位置垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "浮窗不透明度",
            "description": "设置闪抽通知浮窗不透明度，数值越小越透明（0-100）",
        },
        "floating_window_auto_close_time": {
            "name": "浮窗自动关闭时间",
            "description": "设置浮窗自动关闭时间（秒），设为0表示不自动关闭",
        },
        "notification_service_type": {
            "name": "通知服务类型",
            "description": "选择通知服务类型，SecRandom或ClassIsland",
            "combo_items": ["SecRandom", "ClassIsland", "SecRandom+ClassIsland"],
        },
        "notification_display_duration": {
            "name": "通知显示时长",
            "description": "设置ClassIsland通知显示时长（秒）",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland通知服务",
            "content": "请确保已安装.NET 8运行时，并在ClassIsland中安装SecRandom-Ci插件",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland通知服务",
            "content": "检测到未安装插件，请在ClassIsland中安装SecRandom-Ci插件",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland通知服务",
            "content": "检测到ClassIsland和SecRandom-Ci插件正常运行，欢迎使用",
        },
    },
    "EN_US": {
        "title": {
            "name": "Quick Pick notification settings",
            "description": "Configure Quick Pick result notification display method and parameters",
        },
        "basic_settings": {
            "name": "Basic settings",
            "description": "Configure Quick Pick notification basic display parameters",
        },
        "window_mode": {
            "name": "Window mode",
            "description": "Configure Quick Pick notification window display method",
        },
        "floating_window_mode": {
            "name": "Floating window mode",
            "description": "Configure Quick Pick notification floating window behavior mode",
        },
        "animation": {
            "name": "Animation",
            "description": "Configure Quick Pick notification window display animation effect",
        },
        "floating_window_enabled_monitor": {
            "name": "Select the display monitor for Quick Pick notifications",
            "description": "Select the display monitor for Quick Pick notification floating windows",
        },
        "floating_window_position": {
            "name": "Floating window position",
            "description": "Configure Quick Pick notification floating window on-screen display position",
            "combo_items": {
                "0": "Center",
                "1": "Top",
                "2": "Bottom",
                "3": "Left",
                "4": "Right",
                "5": "Top left",
                "6": "Top right",
                "7": "Bottom left",
                "8": "Bottom right",
            },
        },
        "floating_window_horizontal_offset": {
            "name": "Horizontal offset",
            "description": "Configure the horizontal offset (in pixels) for the Quick Pick notification floating window relative to the default position",
        },
        "floating_window_vertical_offset": {
            "name": "Vertical offset",
            "description": "Configure the vertical offset (in pixels) for the Quick Pick notification floating window relative to the default position",
        },
        "floating_window_transparency": {
            "name": "Floating window transparency",
            "description": "Configure Quick Pick notification floating window transparency, where a smaller value indicates higher transparency (0-100)",
        },
        "floating_window_auto_close_time": {
            "name": "Floating window auto-close time",
            "description": "Set the time to close the floating window automatically (seconds), set to 0 to not close automatically",
        },
        "notification_service_type": {
            "name": "Notification service type",
            "description": "Select notification service type, SecRandom or ClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom + ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "Notification display duration",
            "description": "Set notification display duration (seconds)",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland Notification Service",
            "content": "Please ensure .NET 8 runtime is installed and SecRandom-Ci plugin is installed in ClassIsland",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland Notification Service",
            "content": "Plugin not detected, please install SecRandom-Ci plugin in ClassIsland",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland Notification Service",
            "content": "Detected ClassIsland and SecRandom-Ci plugin running properly, welcome to use",
        },
    },
    "JA_JP": {
        "title": {
            "name": "クイックピック通知設定",
            "description": "クイックピック結果通知表示方法とパラメータを設定",
        },
        "basic_settings": {
            "name": "基本設定",
            "description": "クイックピック通知基本表示パラメータを設定",
        },
        "window_mode": {
            "name": "ウィンドウモード",
            "description": "クイックピック通知ウィンドウ表示方法を設定",
        },
        "floating_window_mode": {
            "name": "フローティングウィンドウモード",
            "description": "クイックピック通知フローティングウィンドウ動作モードを設定",
        },
        "animation": {
            "name": "アニメーション",
            "description": "クイックピック通知ウィンドウ表示アニメーション効果を設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_enabled_monitor": {
            "name": "クイックピック通知表示モニターを選択",
            "description": "クイックピック通知フローティングウィンドウモニターを選択",
        },
        "floating_window_position": {
            "name": "フローティングウィンドウ位置",
            "description": "クイックピック通知フローティングウィンドウ画面表示位置を設定",
            "combo_items": {
                "0": "中央",
                "1": "上部",
                "2": "下部",
                "3": "左側",
                "4": "右側",
                "5": "左上",
                "6": "右上",
                "7": "左下",
                "8": "右下",
            },
        },
        "floating_window_horizontal_offset": {
            "name": "水平オフセット",
            "description": "クイックピック通知フローティングウィンドウのデフォルト位置からの水平オフセット（ピクセル）を設定",
        },
        "floating_window_vertical_offset": {
            "name": "垂直オフセット",
            "description": "クイックピック通知フローティングウィンドウのデフォルト位置からの垂直オフセット（ピクセル）を設定",
        },
        "floating_window_transparency": {
            "name": "フローティングウィンドウ不透明度",
            "description": "クイックピック通知フローティングウィンドウの不透明度を設定、値が小さいほど透明（0-100）",
        },
        "floating_window_auto_close_time": {
            "name": "フローティングウィンドウ自動クローズ時間",
            "description": "フローティングウィンドウの自動クローズ時間を設定（秒）、0に設定すると自動クローズなし",
        },
        "notification_service_type": {
            "name": "通知サービスタイプ",
            "description": "通知サービスタイプを選択、SecRandomまたはClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom+ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "通知表示時間",
            "description": "ClassIsland通知表示時間を設定（秒）",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland通知サービス",
            "content": ".NET 8ランタイムがインストールされ、ClassIslandにSecRandom-Ciプラグインがインストールされていることを確認してください",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland通知サービス",
            "content": "プラグインがインストールされていないことを検出しました、ClassIslandにSecRandom-Ciプラグインをインストールしてください",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland通知サービス",
            "content": "ClassIslandとSecRandom-Ciプラグインが正常に動作していることを検出しました、ご利用いただきありがとうございます",
        },
    },
}

# 抽奖通知设置
lottery_notification_settings = {
    "ZH_CN": {
        "title": {
            "name": "抽奖通知设置",
            "description": "配置抽奖结果通知显示方式和参数",
        },
        "basic_settings": {
            "name": "基础设置",
            "description": "配置抽奖通知基础显示参数",
        },
        "floating_window_mode": {
            "name": "浮窗模式",
            "description": "设置抽奖通知浮窗行为模式",
        },
        "classisland_notification_service_settings": {
            "name": "ClassIsland通知服务",
            "description": "配置ClassIsland通知服务参数",
        },
        "call_notification_service": {
            "name": "调用通知服务",
            "description": "是否调用系统通知服务发送抽奖结果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "动画",
            "description": "设置抽奖通知窗口显示动画效果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_enabled_monitor": {
            "name": "选择抽奖通知显示的显示器",
            "description": "选择抽奖通知浮窗显示器",
        },
        "floating_window_position": {
            "name": "浮窗位置",
            "description": "设置抽奖通知浮窗屏幕显示位置",
            "combo_items": [
                "中心",
                "顶部",
                "底部",
                "左侧",
                "右侧",
                "顶部左侧",
                "顶部右侧",
                "底部左侧",
                "底部右侧",
            ],
        },
        "floating_window_horizontal_offset": {
            "name": "水平偏移",
            "description": "设置抽奖通知浮窗相对默认位置水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "设置抽奖通知浮窗相对默认位置垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "浮窗不透明度",
            "description": "设置抽奖通知浮窗不透明度，数值越小越透明（0-100）",
        },
        "floating_window_auto_close_time": {
            "name": "浮窗自动关闭时间",
            "description": "设置浮窗自动关闭时间（秒），设为0表示不自动关闭",
        },
        "use_main_window_when_exceed_threshold": {
            "name": "超过阈值时不显示浮窗通知",
            "description": "当抽取奖数超过设定阈值时，不显示浮窗通知，只在主窗口显示结果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "main_window_display_threshold": {
            "name": "浮窗通知阈值",
            "description": "设置触发浮窗通知的奖数阈值，超过此数值则不显示浮窗通知，最小值为1",
        },
        "notification_service_type": {
            "name": "通知服务类型",
            "description": "选择通知服务类型，SecRandom或ClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom+ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "通知显示时长",
            "description": "设置ClassIsland通知显示时长（秒）",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland通知服务",
            "content": "请确保已安装.NET 8运行时，并在ClassIsland中安装SecRandom-Ci插件",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland通知服务",
            "content": "检测到未安装插件，请在ClassIsland中安装SecRandom-Ci插件",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland通知服务",
            "content": "检测到ClassIsland和SecRandom-Ci插件正常运行，欢迎使用",
        },
    },
    "EN_US": {
        "title": {
            "name": "Lottery notification settings",
            "description": "Configure lottery result notification display method and parameters",
        },
        "basic_settings": {
            "name": "Basic settings",
            "description": "Configure lottery notification basic display parameters",
        },
        "window_mode": {
            "name": "Window mode",
            "description": "Configure lottery notification window display method",
        },
        "floating_window_mode": {
            "name": "Floating window mode",
            "description": "Configure lottery notification floating window behavior mode",
        },
        "call_notification_service": {
            "name": "Call notification service",
            "description": "Whether to call the system notification service to send lottery results",
        },
        "animation": {
            "name": "Animation",
            "description": "Configure lottery notification window display animation effect",
        },
        "floating_window_enabled_monitor": {
            "name": "Select the display monitor for lottery notifications",
            "description": "Select the display monitor for lottery notification floating windows",
        },
        "floating_window_position": {
            "name": "Floating window position",
            "description": "Configure lottery notification floating window on-screen display position",
            "combo_items": {
                "0": "Center",
                "1": "Top",
                "2": "Bottom",
                "3": "Left",
                "4": "Right",
                "5": "Top left",
                "6": "Top right",
                "7": "Bottom left",
                "8": "Bottom right",
            },
        },
        "floating_window_horizontal_offset": {
            "name": "Horizontal offset",
            "description": "Configure the horizontal offset (in pixels) for the lottery notification floating window relative to the default position",
        },
        "floating_window_vertical_offset": {
            "name": "Vertical offset",
            "description": "Configure the vertical offset (in pixels) for the lottery notification floating window relative to the default position",
        },
        "floating_window_transparency": {
            "name": "Floating window transparency",
            "description": "Configure lottery notification floating window transparency, where a smaller value indicates higher transparency (0-100)",
        },
        "floating_window_auto_close_time": {
            "name": "Floating window auto-close time",
            "description": "Set the time to close the floating window automatically (seconds), set to 0 to not close automatically",
        },
        "use_main_window_when_exceed_threshold": {
            "name": "Use main window when exceeding threshold",
            "description": "When the drawn prize count exceeds the threshold, show results in the main window only (no floating notification)",
        },
        "main_window_display_threshold": {
            "name": "Main window threshold",
            "description": "Set the prize count threshold for showing results in the main window (minimum: 1)",
        },
        "notification_service_type": {
            "name": "Notification service type",
            "description": "Select notification service type, SecRandom or ClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom + ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "Notification display duration",
            "description": "Set notification display duration (seconds)",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland Notification Service",
            "content": "Please ensure .NET 8 runtime is installed and SecRandom-Ci plugin is installed in ClassIsland",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland Notification Service",
            "content": "Plugin not detected, please install SecRandom-Ci plugin in ClassIsland",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland Notification Service",
            "content": "Detected ClassIsland and SecRandom-Ci plugin running properly, welcome to use",
        },
    },
    "JA_JP": {
        "title": {
            "name": "抽選通知設定",
            "description": "抽選結果通知表示方法とパラメータを設定",
        },
        "basic_settings": {
            "name": "基本設定",
            "description": "抽選通知基本表示パラメータを設定",
        },
        "window_mode": {
            "name": "ウィンドウモード",
            "description": "抽選通知ウィンドウ表示方法を設定",
        },
        "floating_window_mode": {
            "name": "フローティングウィンドウモード",
            "description": "抽選通知フローティングウィンドウ動作モードを設定",
        },
        "call_notification_service": {
            "name": "通知サービスを呼び出す",
            "description": "システム通知サービスを呼び出して抽選結果を送信するかどうか",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "アニメーション",
            "description": "抽選通知ウィンドウ表示アニメーション効果を設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_enabled_monitor": {
            "name": "抽選通知表示モニターを選択",
            "description": "抽選通知フローティングウィンドウモニターを選択",
        },
        "floating_window_position": {
            "name": "フローティングウィンドウ位置",
            "description": "抽選通知フローティングウィンドウ画面表示位置を設定",
            "combo_items": {
                "0": "中央",
                "1": "上部",
                "2": "下部",
                "3": "左側",
                "4": "右側",
                "5": "左上",
                "6": "右上",
                "7": "左下",
                "8": "右下",
            },
        },
        "floating_window_horizontal_offset": {
            "name": "水平オフセット",
            "description": "抽選通知フローティングウィンドウのデフォルト位置からの水平オフセット（ピクセル）を設定",
        },
        "floating_window_vertical_offset": {
            "name": "垂直オフセット",
            "description": "抽選通知フローティングウィンドウのデフォルト位置からの垂直オフセット（ピクセル）を設定",
        },
        "floating_window_transparency": {
            "name": "フローティングウィンドウ不透明度",
            "description": "抽選通知フローティングウィンドウの不透明度を設定、値が小さいほど透明（0-100）",
        },
        "floating_window_auto_close_time": {
            "name": "フローティングウィンドウ自動クローズ時間",
            "description": "フローティングウィンドウの自動クローズ時間を設定（秒）、0に設定すると自動クローズなし",
        },
        "use_main_window_when_exceed_threshold": {
            "name": "閾値超過時にフローティングウィンドウ通知を表示しない",
            "description": "抽選賞品数が設定閾値を超える場合、フローティングウィンドウ通知を表示せず、メインウィンドウのみに結果を表示",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "main_window_display_threshold": {
            "name": "フローティングウィンドウ通知閾値",
            "description": "フローティングウィンドウ通知をトリガーする賞品数閾値を設定、この数値を超えるとフローティングウィンドウ通知を表示しない、最小値は1",
        },
        "notification_service_type": {
            "name": "通知サービスタイプ",
            "description": "通知サービスタイプを選択、SecRandomまたはClassIsland",
            "combo_items": [
                "SecRandom",
                "ClassIsland",
                "SecRandom+ClassIsland",
            ],
        },
        "notification_display_duration": {
            "name": "通知表示時間",
            "description": "ClassIsland通知表示時間を設定（秒）",
        },
        "classisland_notification_hint": {
            "title": "ClassIsland通知サービス",
            "content": ".NET 8ランタイムがインストールされ、ClassIslandにSecRandom-Ciプラグインがインストールされていることを確認してください",
        },
        "classisland_plugin_notification_hint": {
            "title": "ClassIsland通知サービス",
            "content": "プラグインがインストールされていないことを検出しました、ClassIslandにSecRandom-Ciプラグインをインストールしてください",
        },
        "classisland_configured_successfully": {
            "title": "ClassIsland通知サービス",
            "content": "ClassIslandとSecRandom-Ciプラグインが正常に動作していることを検出しました、ご利用いただきありがとうございます",
        },
    },
}
