# 浮窗管理语言配置
floating_window_management = {
    "ZH_CN": {
        "title": {"name": "浮窗管理", "description": "配置浮窗相关设置"},
        "basic_settings": {"name": "基本设置", "description": "配置浮窗基本设置"},
        "appearance_settings": {
            "name": "外观设置",
            "description": "配置浮窗外观设置",
        },
        "edge_settings": {"name": "贴边设置", "description": "配置浮窗贴边设置"},
        "foreground_hiding_settings": {
            "name": "前台隐藏设置",
            "description": "配置浮窗在前台应用时的隐藏设置",
        },
        "startup_display_floating_window": {
            "name": "启动时显示浮窗",
            "description": "控制软件启动时是否自动显示浮窗",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_opacity": {
            "name": "浮窗不透明度",
            "description": "调整浮窗不透明度",
        },
        "floating_window_topmost_mode": {
            "name": "置顶模式",
            "description": "选择浮窗置顶方式（UIA置顶需以管理员运行）",
            "combo_items": ["关闭置顶", "置顶", "UIA置顶"],
        },
        "extend_quick_draw_component": {
            "name": "扩展闪抽组件",
            "description": "在浮窗闪抽按钮旁显示更多选择控件",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "uia_topmost_restart_dialog_title": {
            "name": "需要重启",
            "description": "UIA置顶切换后重启提示标题",
        },
        "uia_topmost_restart_dialog_content": {
            "name": "已切换为UIA置顶模式，需要重启生效，是否立即重启？",
            "description": "UIA置顶切换后重启提示内容",
        },
        "uia_topmost_disable_restart_dialog_content": {
            "name": "已关闭UIA置顶模式，需要完全退出软件后重新启动才会生效",
            "description": "关闭UIA置顶后提示内容",
        },
        "uia_topmost_disable_restart_dialog_ok_btn": {
            "name": "知道了",
            "description": "关闭UIA置顶后提示按钮文本",
        },
        "uia_topmost_restart_dialog_restart_btn": {
            "name": "重启",
            "description": "UIA置顶切换后重启按钮文本",
        },
        "uia_topmost_restart_dialog_cancel_btn": {
            "name": "取消",
            "description": "UIA置顶切换后取消按钮文本",
        },
        "reset_floating_window_position_button": {
            "name": "重置浮窗位置",
            "description": "将浮窗位置重置为默认位置",
            "pushbutton_name": "重置位置",
        },
        "floating_window_button_control": {
            "name": "浮窗控件配置",
            "description": "选择在浮窗中显示的功能按钮",
            "combo_items": [
                "点名",
                "闪抽",
                "抽奖",
                "点名+闪抽",
                "点名+抽奖",
                "闪抽+抽奖",
                "点名+闪抽+抽奖",
                "计时器",
                "点名+计时器",
                "闪抽+计时器",
                "抽奖+计时器",
                "点名+闪抽+计时器",
                "点名+抽奖+计时器",
                "闪抽+抽奖+计时器",
                "点名+闪抽+抽奖+计时器",
            ],
        },
        "roll_call_button": {"name": "点名", "description": "浮窗点名按钮文本"},
        "quick_draw_button": {"name": "闪抽", "description": "浮窗闪抽按钮文本"},
        "lottery_button": {"name": "抽奖", "description": "浮窗抽奖按钮文本"},
        "face_draw_button": {"name": "人脸抽", "description": "浮窗人脸抽按钮文本"},
        "timer_button": {"name": "计时器", "description": "浮窗计时器按钮文本"},
        "floating_window_placement": {
            "name": "浮窗排列方式",
            "description": "设置浮窗中控件排列方式",
            "combo_items": ["矩形排列", "竖向排列", "横向排列"],
        },
        "floating_window_display_style": {
            "name": "浮窗显示样式",
            "description": "设置浮窗中控件显示样式",
            "combo_items": ["图标+文字", "图标", "文字"],
        },
        "floating_window_theme": {
            "name": "浮窗主题",
            "description": "设置浮窗主题（可跟随软件全局或单独设置）",
        },
        "floating_window_theme_follow_global": {
            "name": "跟随软件全局",
            "description": "浮窗主题选项：跟随软件全局主题",
        },
        "floating_window_stick_to_edge": {
            "name": "贴边功能",
            "description": "控制浮窗是否自动贴边",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_stick_to_edge_recover_seconds": {
            "name": "贴边收纳时间",
            "description": "设置浮窗贴边后自动收纳时间（秒）",
        },
        "floating_window_draggable": {
            "name": "浮窗可拖动",
            "description": "控制浮窗是否可被拖动",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_long_press_duration": {
            "name": "长按时间",
            "description": "设置浮窗长按时间（毫秒）",
        },
        "floating_window_stick_to_edge_display_style": {
            "name": "贴边显示样式",
            "description": "设置浮窗贴边时显示样式",
            "combo_items": ["图标", "文字", "箭头"],
        },
        "hide_floating_window_on_foreground": {
            "name": "前台窗口隐藏浮窗",
            "description": "当前台窗口标题或进程名匹配时自动隐藏浮窗",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "hide_floating_window_on_foreground_window_titles": {
            "name": "前台窗口标题",
            "description": "用英文 ; 分隔，包含匹配时隐藏浮窗",
        },
        "hide_floating_window_on_foreground_process_names": {
            "name": "前台进程名",
            "description": "用英文 ; 分隔，如 WeChat.exe，匹配时隐藏浮窗",
        },
        "floating_window_stick_to_edge_arrow_text": {
            "name": "抽",
            "description": "设置浮窗贴边时箭头按钮显示的文字",
        },
        "floating_window_size": {
            "name": "浮窗大小",
            "description": "设置浮窗按钮和图标的大小",
            "combo_items": ["超级小", "超小", "小", "中", "大", "超大", "超级大"],
        },
        "do_not_steal_focus": {
            "name": "无焦点模式",
            "description": "通知窗口显示时不抢占焦点，保持原有顶层软件焦点",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
    },
    "EN_US": {
        "title": {
            "name": "Floating window management",
            "description": "Configure floating window related settings",
        },
        "basic_settings": {
            "name": "Basic settings",
            "description": "Configure floating window basic settings",
        },
        "appearance_settings": {
            "name": "Appearance settings",
            "description": "Configure floating window appearance settings",
        },
        "edge_settings": {
            "name": "Edge settings",
            "description": "Configure floating window edge settings",
        },
        "foreground_hiding_settings": {
            "name": "Foreground hiding settings",
            "description": "Configure floating window hiding settings when foreground application is active",
        },
        "startup_display_floating_window": {
            "name": "Show popup on startup",
            "description": "Set whether to show the floating window after boot",
        },
        "floating_window_opacity": {
            "name": "Floating window transparency",
            "description": "Adjust floating window transparency",
        },
        "floating_window_topmost_mode": {
            "name": "Topmost mode",
            "description": "Select floating window topmost mode (UIA requires run as administrator)",
            "combo_items": ["Disable topmost", "Topmost", "UIA topmost"],
        },
        "extend_quick_draw_component": {
            "name": "Extended Quick Pick controls",
            "description": "Show extra selectors next to the floating window Quick Pick button",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "uia_topmost_restart_dialog_title": {
            "name": "Restart Required",
            "description": "Restart dialog title after switching UIA topmost",
        },
        "uia_topmost_restart_dialog_content": {
            "name": "UIA topmost mode has been enabled. Restart now to apply changes?",
            "description": "Restart dialog content after switching UIA topmost",
        },
        "uia_topmost_disable_restart_dialog_content": {
            "name": "UIA topmost mode has been disabled. Fully exit the app and relaunch to apply.",
            "description": "Hint content after disabling UIA topmost",
        },
        "uia_topmost_disable_restart_dialog_ok_btn": {
            "name": "OK",
            "description": "OK button text after disabling UIA topmost",
        },
        "uia_topmost_restart_dialog_restart_btn": {
            "name": "Restart",
            "description": "Restart button text after switching UIA topmost",
        },
        "uia_topmost_restart_dialog_cancel_btn": {
            "name": "Cancel",
            "description": "Cancel button text after switching UIA topmost",
        },
        "reset_floating_window_position_button": {
            "name": "Reset floating window position",
            "description": "Reset floating window to default position",
            "pushbutton_name": "Reset position",
        },
        "floating_window_button_control": {
            "name": "Floating window controls config",
            "description": "Select the button to show in floating window",
            "combo_items": {
                "0": "Pick",
                "1": "Quick Pick",
                "2": "Lottery",
                "3": "Pick + Quick Pick",
                "4": "Pick + Lottery",
                "5": "Quick Pick + Lottery",
                "6": "Pick + Quick Pick + Lottery",
                "7": "Timer",
                "8": "Pick + Timer",
                "9": "Quick Pick + Timer",
                "10": "Lottery + Timer",
                "11": "Pick + Quick Pick + Timer",
                "12": "Pick + Lottery + Timer",
                "13": "Quick Pick + Lottery + Timer",
                "14": "Pick + Quick Pick + Lottery + Timer",
            },
        },
        "roll_call_button": {
            "name": "Pick",
            "description": "Floating window pick button",
        },
        "quick_draw_button": {
            "name": "Quick Pick",
            "description": "Floating window quick pick button",
        },
        "lottery_button": {
            "name": "Lottery",
            "description": "Floating window lottery button",
        },
        "face_draw_button": {
            "name": "Face Pick",
            "description": "Floating window face pick button",
        },
        "timer_button": {
            "name": "Timer",
            "description": "Floating window timer button",
        },
        "floating_window_placement": {
            "name": "Floating window layout",
            "description": "Configure layout of buttons in floating window",
            "combo_items": {"0": "Rectangle", "1": "Portrait", "2": "Landscape"},
        },
        "floating_window_display_style": {
            "name": "Floating window style",
            "description": "Configure style of buttons in floating window",
            "combo_items": {"0": "Icon + Text", "1": "Icon only", "2": "Text only"},
        },
        "floating_window_theme": {
            "name": "Floating window theme",
            "description": "Set floating window theme (follow global or override)",
        },
        "floating_window_theme_follow_global": {
            "name": "Follow global",
            "description": "Floating window theme option: follow app global theme",
        },
        "floating_window_stick_to_edge": {
            "name": "Edge function",
            "description": "Whether to dock floating window automatically",
        },
        "floating_window_stick_to_edge_recover_seconds": {
            "name": "Edge receipt time",
            "description": "Set the automatic reception time after the floating window near side (seconds)",
        },
        "floating_window_stick_to_edge_display_style": {
            "name": "Edge style",
            "description": "Configure docked floating window style",
            "combo_items": {"0": "Icon", "1": "Text", "2": "Arrow"},
        },
        "hide_floating_window_on_foreground": {
            "name": "Hide floating window on foreground",
            "description": "Hide floating window when foreground title or process matches",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "hide_floating_window_on_foreground_window_titles": {
            "name": "Foreground window titles",
            "description": "Use English ';' to separate, hide on substring match",
        },
        "hide_floating_window_on_foreground_process_names": {
            "name": "Foreground process names",
            "description": "Use English ';' to separate, e.g. WeChat.exe",
        },
        "floating_window_long_press_duration": {
            "name": "Long press time",
            "description": "Set floating window long by time (milliseconds)",
        },
        "floating_window_draggable": {
            "name": "Floating window draggable",
            "description": "Set if floating window is draggable",
        },
        "floating_window_stick_to_edge_arrow_text": {
            "name": "Pick",
            "description": "Set the text to show on arrow button when the floating window is docked",
        },
        "floating_window_size": {
            "name": "Floating window size",
            "description": "Set the size of buttons and icons in floating window",
            "combo_items": {
                "0": "Extra Small",
                "1": "Very Small",
                "2": "Small",
                "3": "Medium",
                "4": "Large",
                "5": "Extra Large",
                "6": "Extra Extra Large",
            },
        },
        "do_not_steal_focus": {
            "name": "Focusless mode",
            "description": "Do not steal focus when notification window appears, keep focus on original top-level software",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
    },
    "JA_JP": {
        "title": {
            "name": "フローティングウィンドウ管理",
            "description": "フローティングウィンドウ関連設定を設定",
        },
        "basic_settings": {
            "name": "基本設定",
            "description": "フローティングウィンドウ基本設定を設定",
        },
        "appearance_settings": {
            "name": "外観設定",
            "description": "フローティングウィンドウ外観設定を設定",
        },
        "edge_settings": {
            "name": "エッジ設定",
            "description": "フローティングウィンドウエッジ設定を設定",
        },
        "foreground_hiding_settings": {
            "name": "フォアグラウンド非表示設定",
            "description": "フォアグラウンドアプリアクティブ時のフローティングウィンドウ非表示設定を設定",
        },
        "startup_display_floating_window": {
            "name": "起動時にフローティングウィンドウを表示",
            "description": "アプリ起動時にフローティングウィンドウを自動表示するかどうかを制御",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_opacity": {
            "name": "フローティングウィンドウ不透明度",
            "description": "フローティングウィンドウの不透明度を調整",
        },
        "floating_window_topmost_mode": {
            "name": "最前面モード",
            "description": "フローティングウィンドウの最前面方式を選択（UIA最前面は管理者権限で実行が必要）",
            "combo_items": {"0": "最前面を無効", "1": "最前面", "2": "UIA最前面"},
        },
        "extend_quick_draw_component": {
            "name": "クイック抽選拡張コンポーネント",
            "description": "フローティングウィンドウのクイック抽選ボタン横に追加の選択項目を表示",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "uia_topmost_restart_dialog_title": {
            "name": "再起動が必要です",
            "description": "UIA最前面切替後の再起動プロンプトタイトル",
        },
        "uia_topmost_restart_dialog_content": {
            "name": "UIA最前面モードに切り替えました。有効にするには再起動が必要です。今すぐ再起動しますか？",
            "description": "UIA最前面切替後の再起動プロンプト内容",
        },
        "uia_topmost_disable_restart_dialog_content": {
            "name": "UIA最前面モードを無効にしました。完全にアプリを終了して再起動すると有効になります",
            "description": "UIA最前面無効後のプロンプト内容",
        },
        "uia_topmost_disable_restart_dialog_ok_btn": {
            "name": "了解しました",
            "description": "UIA最前面無効後のプロンプトボタンテキスト",
        },
        "uia_topmost_restart_dialog_restart_btn": {
            "name": "再起動",
            "description": "UIA最前面切替後の再起動ボタンテキスト",
        },
        "uia_topmost_restart_dialog_cancel_btn": {
            "name": "キャンセル",
            "description": "UIA最前面切替後のキャンセルボタンテキスト",
        },
        "reset_floating_window_position_button": {
            "name": "フローティングウィンドウ位置をリセット",
            "description": "フローティングウィンドウ位置をデフォルト位置にリセット",
            "pushbutton_name": "位置をリセット",
        },
        "floating_window_button_control": {
            "name": "フローティングウィンドウコントロール設定",
            "description": "フローティングウィンドウに表示する機能ボタンを選択",
            "combo_items": {
                "0": "点呼",
                "1": "クイック抽選",
                "2": "抽選",
                "3": "点呼+クイック抽選",
                "4": "点呼+抽選",
                "5": "クイック抽選+抽選",
                "6": "点呼+クイック抽選+抽選",
                "7": "タイマー",
                "8": "点呼+タイマー",
                "9": "クイック抽選+タイマー",
                "10": "抽選+タイマー",
                "11": "点呼+クイック抽選+タイマー",
                "12": "点呼+抽選+タイマー",
                "13": "クイック抽選+抽選+タイマー",
                "14": "点呼+クイック抽選+抽選+タイマー",
            },
        },
        "roll_call_button": {
            "name": "点呼",
            "description": "フローティングウィンドウ点呼ボタンテキスト",
        },
        "quick_draw_button": {
            "name": "クイック抽選",
            "description": "フローティングウィンドウクイック抽選ボタンテキスト",
        },
        "lottery_button": {
            "name": "抽選",
            "description": "フローティングウィンドウ抽選ボタンテキスト",
        },
        "face_draw_button": {
            "name": "顔抽選",
            "description": "フローティングウィンドウ顔抽選ボタンテキスト",
        },
        "timer_button": {
            "name": "タイマー",
            "description": "フローティングウィンドウタイマーボタンテキスト",
        },
        "floating_window_placement": {
            "name": "フローティングウィンドウ配置方式",
            "description": "フローティングウィンドウ内コントロールの配置方式を設定",
            "combo_items": {"0": "矩形配置", "1": "縦配置", "2": "横配置"},
        },
        "floating_window_display_style": {
            "name": "フローティングウィンドウ表示スタイル",
            "description": "フローティングウィンドウ内コントロールの表示スタイルを設定",
            "combo_items": {"0": "アイコン+文字", "1": "アイコンのみ", "2": "文字のみ"},
        },
        "floating_window_theme": {
            "name": "フローティングウィンドウテーマ",
            "description": "フローティングウィンドウテーマを設定（アプリグローバルに従うか個別設定）",
        },
        "floating_window_theme_follow_global": {
            "name": "アプリグローバルに従う",
            "description": "フローティングウィンドウテーマオプション：アプリグローバルテーマに従う",
        },
        "floating_window_stick_to_edge": {
            "name": "エッジ貼り付け機能",
            "description": "フローティングウィンドウが自動的にエッジに貼り付くかどうかを制御",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_stick_to_edge_recover_seconds": {
            "name": "エッジ収納時間",
            "description": "フローティングウィンドウエッジ貼り付け後の自動収納時間を設定（秒）",
        },
        "floating_window_draggable": {
            "name": "フローティングウィンドウドラッグ可能",
            "description": "フローティングウィンドウがドラッグ可能かどうかを制御",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "floating_window_long_press_duration": {
            "name": "長押し時間",
            "description": "フローティングウィンドウ長押し時間を設定（ミリ秒）",
        },
        "floating_window_stick_to_edge_display_style": {
            "name": "エッジ貼り付け表示スタイル",
            "description": "フローティングウィンドウエッジ貼り付け時の表示スタイルを設定",
            "combo_items": {"0": "アイコン", "1": "文字", "2": "矢印"},
        },
        "hide_floating_window_on_foreground": {
            "name": "フォアグラウンドウィンドウでフローティングウィンドウを非表示",
            "description": "フォアグラウンドウィンドウタイトルまたはプロセス名が一致するときにフローティングウィンドウを自動非表示",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "hide_floating_window_on_foreground_window_titles": {
            "name": "フォアグラウンドウィンドウタイトル",
            "description": "英語 ; で区切り、一致時にフローティングウィンドウを非表示",
        },
        "hide_floating_window_on_foreground_process_names": {
            "name": "フォアグラウンドプロセス名",
            "description": "英語 ; で区切り、例 WeChat.exe、一致時にフローティングウィンドウを非表示",
        },
        "floating_window_stick_to_edge_arrow_text": {
            "name": "抽",
            "description": "フローティングウィンドウエッジ貼り付け時の矢印ボタンに表示する文字を設定",
        },
        "floating_window_size": {
            "name": "フローティングウィンドウサイズ",
            "description": "フローティングウィンドウボタンとアイコンのサイズを設定",
            "combo_items": {
                "0": "超極小",
                "1": "極小",
                "2": "小",
                "3": "中",
                "4": "大",
                "5": "特大",
                "6": "超特大",
            },
        },
        "do_not_steal_focus": {
            "name": "フォーカスなしモード",
            "description": "通知ウィンドウ表示時にフォーカスを奪わず、元のトップレベルソフトフォーカスを維持",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
    },
}
