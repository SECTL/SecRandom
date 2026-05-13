# 抽取设置语言配置
extraction_settings = {
    "ZH_CN": {"title": {"name": "抽取设置", "description": "抽取功能设置"}},
    "EN_US": {"title": {"name": "Pick settings", "description": "Pick settings"}},
    "JA_JP": {"title": {"name": "抽選設定", "description": "抽選機能設定"}},
}

# 点名设置语言配置
roll_call_settings = {
    "ZH_CN": {
        "title": {"name": "点名设置", "description": "点名功能设置"},
        "list_specific_settings_entry": {
            "name": "独立名单配置",
            "description": "按名单覆盖点名设置（优先于全局设置）",
            "pushbutton_name": "打开",
        },
        "list_specific_settings_window": {
            "name": "独立名单配置",
            "description": "按名单覆盖点名设置",
        },
        "list_specific_settings_select_list": {
            "name": "选择名单",
            "description": "选择要配置的抽取名单",
        },
        "list_specific_settings_sync_button": {
            "name": "同步主设置",
            "description": "清除该名单的独立覆盖设置，恢复跟随全局",
            "pushbutton_name": "同步主设置",
        },
        "extraction_function": {
            "name": "抽取功能",
            "description": "设置点名抽取功能",
        },
        "display_settings": {
            "name": "显示设置",
            "description": "设置点名结果显示方式",
        },
        "basic_animation_settings": {
            "name": "动画设置",
            "description": "设置点名动画效果",
        },
        "color_theme_settings": {
            "name": "颜色主题设置",
            "description": "设置点名结果颜色主题",
        },
        "student_image_settings": {
            "name": "学生头像设置",
            "description": "设置点名结果中学生头像显示",
        },
        "music_settings": {"name": "音乐设置", "description": "设置点名时播放的音乐"},
        "draw_mode": {
            "name": "抽取模式",
            "description": "设置点名抽取模式",
            "combo_items": ["重复抽取", "不重复抽取", "半重复抽取"],
        },
        "clear_record": {
            "name": "清除抽取记录方式",
            "description": "设置清除抽取记录的时机",
            "combo_items": ["重启后清除", "直到全部抽取完"],
            "combo_items_other": ["重启后清除", "直到全部抽取完", "无需清除"],
        },
        "half_repeat": {
            "name": "半重复抽取次数",
            "description": "设置每人被抽中多少次后清除抽取记录",
        },
        "draw_type": {
            "name": "抽取方式",
            "description": "设置点名抽取方式",
            "combo_items": ["随机抽取", "公平抽取"],
        },
        "default_class": {
            "name": "默认抽取名单",
            "description": "设置默认抽取名单",
        },
        "font_size": {"name": "字体大小", "description": "设置点名结果字体大小"},
        "reminder_text": {
            "name": "抽取前/清空后提示语",
            "description": "设置抽取前和清空后显示的提示文字",
        },
        "reminder_font_size": {
            "name": "提示语字体大小",
            "description": "设置提示语文字的字体大小",
        },
        "reminder_text_color": {
            "name": "提示语颜色",
            "description": "设置提示语文字的颜色",
        },
        "reminder_text_opacity": {
            "name": "提示语透明度",
            "description": "设置提示语文字的透明度（0-100）",
        },
        "use_global_font": {
            "name": "使用全局字体",
            "description": "是否使用全局字体设置",
            "combo_items": ["跟随全局字体", "使用自定义字体"],
        },
        "custom_font": {"name": "自定义字体", "description": "选择自定义字体"},
        "display_format": {
            "name": "结果显示格式",
            "description": "设置点名结果显示格式",
            "combo_items": ["学号+姓名", "姓名", "学号"],
        },
        "display_style": {
            "name": "结果显示样式",
            "description": "设置点名结果显示样式",
            "combo_items": ["默认样式", "卡片样式"],
        },
        "show_random": {
            "name": "显示随机组员格式",
            "description": "设置随机组员显示格式",
            "combo_items": ["不显示", "组名[换行]姓名", "组名[短横杠]姓名"],
        },
        "show_tags": {
            "name": "显示标签",
            "description": "设置是否在结果中显示标签",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "动画模式",
            "description": "设置点名抽取动画效果",
            "combo_items": ["手动停止动画", "自动播放动画", "直接显示结果"],
        },
        "animation_interval": {
            "name": "动画间隔",
            "description": "设置点名动画间隔时间（毫秒）",
        },
        "autoplay_count": {
            "name": "自动播放次数",
            "description": "设置点名动画自动播放次数",
        },
        "result_flow_animation_style": {
            "name": "结果布局动画",
            "description": "设置点名结果布局出现动画效果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "result_flow_animation_duration": {
            "name": "结果布局动画时长",
            "description": "设置点名结果布局动画时长（毫秒）",
        },
        "animation_color_theme": {
            "name": "动画/结果颜色主题",
            "description": "设置点名动画/结果颜色主题",
            "combo_items": ["关闭", "随机颜色", "固定颜色"],
        },
        "animation_fixed_color": {
            "name": "动画/结果固定颜色",
            "description": "设置点名动画/结果固定颜色",
        },
        "student_image": {
            "name": "显示学生图片",
            "description": "设置是否显示学生图片",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "student_image_position": {
            "name": "头像位置",
            "description": "设置学生头像在结果中的位置",
            "combo_items": ["左部", "顶部", "右部", "底部"],
        },
        "open_student_image_folder": {
            "name": "学生图片文件夹",
            "description": "管理学生图片文件，图片文件名需与学生姓名一致",
        },
        "show_weight_transparency": {
            "name": "权重透明化",
            "description": "在抽取结果界面展示每位参与者的权重明细",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "wp_title": {"name": "权重明细", "description": ""},
        "wp_collapse": {"name": "折叠", "description": ""},
        "wp_expand": {"name": "展开", "description": ""},
        "wp_help": {"name": "查看权重计算规则", "description": ""},
        "wp_col_student": {"name": "学生", "description": ""},
        "wp_col_base": {"name": "基础权重", "description": ""},
        "wp_col_freq": {"name": "频率因子", "description": ""},
        "wp_col_group": {"name": "小组平衡", "description": ""},
        "wp_col_gender": {"name": "性别平衡", "description": ""},
        "wp_col_time": {"name": "时间因子", "description": ""},
        "wp_col_shield": {"name": "屏蔽", "description": ""},
        "wp_col_total": {"name": "最终权重", "description": ""},
        "wp_shielded": {"name": "已屏蔽", "description": ""},
        "wp_normal": {"name": "正常", "description": ""},
        "wp_formula_title": {"name": "权重计算规则", "description": ""},
        "wp_close": {"name": "关闭", "description": ""},
        "wp_formula_content": {
            "name": "**权重计算规则**\n\n**总权重 = 基础权重 + 频率因子 + 小组平衡 + 性别平衡 + 时间因子**\n\n---\n\n**基础权重**: 每个学生的基础分，可在设置中调整（默认1.0）\n\n**频率因子**: 被抽中次数越少 → 因子越高\n• 支持线性/平方根/指数三种算法（可在设置中选择）\n• 前10轮自动保护新同学（冷启动）\n\n**小组平衡**: 所在小组被抽越少 → 加成越高\n\n**性别平衡**: 该性别被抽越少 → 加成越高\n\n**时间因子**: 距离上次被抽越久 → 权重逐渐恢复\n• 超30天未抽 → 时间因子加满\n\n---\n\n**特殊规则**:\n• 屏蔽保护：刚抽过的人在设定时间内几乎不会被再抽到（权重降至 1/10）\n• 权重范围：最低 0.05，最高 5.0\n\n**平均值差值保护**（独立机制）: 只允许抽取次数≤平均值的成员进入候选池，差距过大时排除极值",
            "description": "",
        },
    },
    "EN_US": {
        "title": {"name": "Picking settings", "description": "Picking settings"},
        "list_specific_settings_entry": {
            "name": "Per-list overrides",
            "description": "Override Roll Call settings per list (higher priority than global)",
            "pushbutton_name": "Open",
        },
        "list_specific_settings_window": {
            "name": "Per-list overrides",
            "description": "Override Roll Call settings per list",
        },
        "list_specific_settings_select_list": {
            "name": "Select list",
            "description": "Select the list to configure",
        },
        "list_specific_settings_sync_button": {
            "name": "Sync with global",
            "description": "Clear per-list overrides and follow global settings",
            "pushbutton_name": "Sync with global",
        },
        "extraction_function": {
            "name": "Picking function",
            "description": "Set picking function",
        },
        "display_settings": {
            "name": "Display settings",
            "description": "Set the method to display the pick results",
        },
        "basic_animation_settings": {
            "name": "Animation settings",
            "description": "Set picking animations",
        },
        "color_theme_settings": {
            "name": "Color theme settings",
            "description": "Set the color theme of pick results",
        },
        "student_image_settings": {
            "name": "Student image settings",
            "description": "Set to show student avatars in the results",
        },
        "music_settings": {
            "name": "Music settings",
            "description": "Set the music to play on picking",
        },
        "draw_mode": {
            "name": "Picking mode",
            "description": "Set picking mode",
            "combo_items": {
                "0": "Pick with repeating",
                "1": "Pick without repeating",
                "2": "Semi-repeatedly pick",
            },
        },
        "clear_record": {
            "name": "Clear history method",
            "description": "Set the time to clean picking history",
            "combo_items": {"0": "Clear on restart", "1": "Until all have been picked"},
            "combo_items_other": {
                "0": "Clear on restart",
                "1": "Until all have been picked",
                "2": "Do not clear",
            },
        },
        "half_repeat": {
            "name": "Semi-repeated pick count",
            "description": "Set the maximum picked times of each person to clean history",
        },
        "draw_type": {
            "name": "Picking method",
            "description": "Set picking method",
            "combo_items": {"0": "Random pick", "1": "Fair pick"},
        },
        "font_size": {
            "name": "Font size",
            "description": "Set picking result font size",
        },
        "reminder_text": {
            "name": "Reminder text",
            "description": "Text displayed before the draw and after resetting",
        },
        "reminder_font_size": {
            "name": "Reminder font size",
            "description": "Font size for the reminder text",
        },
        "reminder_text_color": {
            "name": "Reminder color",
            "description": "Color of the reminder text",
        },
        "reminder_text_opacity": {
            "name": "Reminder opacity",
            "description": "Opacity of the reminder text (0-100)",
        },
        "use_global_font": {
            "name": "Use global font",
            "description": "Whether to use global font settings",
            "combo_items": {"0": "Follow global font", "1": "Use custom font"},
        },
        "custom_font": {
            "name": "Custom font",
            "description": "Select custom font",
        },
        "display_format": {
            "name": "Result display format",
            "description": "Set the results display format",
            "combo_items": {"0": "Student ID + Name", "1": "Name", "2": "Student ID"},
        },
        "display_style": {
            "name": "Result display style",
            "description": "Set the pick result display style",
            "combo_items": {"0": "Default", "1": "Card"},
        },
        "show_random": {
            "name": "Format of showing random group member",
            "description": "Set random group member display format",
            "combo_items": {
                "0": "Hide",
                "1": "Group[New line]Name",
                "2": "Group name[Dash]name",
            },
        },
        "show_tags": {
            "name": "Show tags",
            "description": "Whether to show tags in results",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "Animation mode",
            "description": "Set picking animations",
            "combo_items": {
                "0": "Manually stop animation",
                "1": "Automatically play animation",
                "2": "Directly show result",
            },
        },
        "animation_interval": {
            "name": "Animation interval",
            "description": "Set the interval between picking animations (ms)",
        },
        "autoplay_count": {
            "name": "Autoplay count",
            "description": "Set the number of times to animate",
        },
        "result_flow_animation_style": {
            "name": "Result layout animation",
            "description": "Set result flow layout intro animation",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "result_flow_animation_duration": {
            "name": "Result layout duration",
            "description": "Set result layout animation duration (ms)",
        },
        "animation_color_theme": {
            "name": "Animation/Result Color Theme",
            "description": "Set the animate/result color theme",
            "combo_items": {"0": "Disabled", "1": "Random color", "2": "Fixed color"},
        },
        "animation_fixed_color": {
            "name": "Animation/Result fixed color",
            "description": "Set the animation/result color",
        },
        "student_image": {
            "name": "Show student images",
            "description": "Set whether to show student images",
        },
        "student_image_position": {
            "name": "Image position",
            "description": "Set the position of student images in results",
            "combo_items": {"0": "Left", "1": "Top", "2": "Right", "3": "Bottom"},
        },
        "open_student_image_folder": {
            "name": "Student image folder",
            "description": "Manage student image files. Picture file names must match student name",
        },
        "default_class": {
            "name": "Default class",
            "description": "Set the default class to use for drawing",
        },
        "show_weight_transparency": {
            "name": "Weight Transparency",
            "description": "Show weight details for each participant on the result interface",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "wp_title": {"name": "Weight Details", "description": ""},
        "wp_collapse": {"name": "Collapse", "description": ""},
        "wp_expand": {"name": "Expand", "description": ""},
        "wp_help": {"name": "View Weight Calculation Rules", "description": ""},
        "wp_col_student": {"name": "Student", "description": ""},
        "wp_col_base": {"name": "Base", "description": ""},
        "wp_col_freq": {"name": "Frequency", "description": ""},
        "wp_col_group": {"name": "Group", "description": ""},
        "wp_col_gender": {"name": "Gender", "description": ""},
        "wp_col_time": {"name": "Time", "description": ""},
        "wp_col_shield": {"name": "Shield", "description": ""},
        "wp_col_total": {"name": "Total", "description": ""},
        "wp_shielded": {"name": "Shielded", "description": ""},
        "wp_normal": {"name": "Normal", "description": ""},
        "wp_formula_title": {"name": "Weight Calculation Rules", "description": ""},
        "wp_close": {"name": "Close", "description": ""},
        "wp_formula_content": {
            "name": "**Weight Calculation Rules**\n\n**Total Weight = Base Weight + Frequency Factor + Group Balance + Gender Balance + Time Factor**\n\n---\n\n**Base Weight**: Initial weight for each student, adjustable in settings (default 1.0)\n\n**Frequency Factor**: Fewer draws → higher factor\n• Supports Linear/Square Root/Exponential (configurable)\n• First 10 rounds protect new students (Cold Start)\n\n**Group Balance**: Group drawn less often → higher bonus\n\n**Gender Balance**: Gender drawn less often → higher bonus\n\n**Time Factor**: Longer since last drawn → weight gradually recovers\n• Over 30 days → time factor maxed out\n\n---\n\n**Special Rules**:\n• Shield Protection: Recently drawn students are almost never drawn again (weight reduced to 1/10)\n• Weight Range: Minimum 0.05, Maximum 5.0\n\n**Average Gap Protection** (independent mechanism): Only candidates with draw count ≤ average enter the pool; extreme values are excluded when gap is too large",
            "description": "",
        },
    },
    "JA_JP": {
        "title": {"name": "点呼設定", "description": "点呼機能設定"},
        "list_specific_settings_entry": {
            "name": "リスト別上書き",
            "description": "リストごとに点呼設定を上書き（グローバルより優先）",
            "pushbutton_name": "開く",
        },
        "list_specific_settings_window": {
            "name": "リスト別上書き",
            "description": "リストごとに点呼設定を上書き",
        },
        "list_specific_settings_select_list": {
            "name": "リストを選択",
            "description": "設定する抽選リストを選択",
        },
        "list_specific_settings_sync_button": {
            "name": "全体設定に同期",
            "description": "リスト別上書きをクリアして全体設定に従う",
            "pushbutton_name": "全体設定に同期",
        },
        "extraction_function": {
            "name": "抽選機能",
            "description": "点呼抽選機能を設定",
        },
        "display_settings": {
            "name": "表示設定",
            "description": "点呼結果の表示方法を設定",
        },
        "basic_animation_settings": {
            "name": "アニメーション設定",
            "description": "点呼アニメーション効果を設定",
        },
        "color_theme_settings": {
            "name": "カラーテーマ設定",
            "description": "点呼結果のカラーテーマを設定",
        },
        "student_image_settings": {
            "name": "学生アバター設定",
            "description": "点呼結果の学生アバター表示を設定",
        },
        "music_settings": {
            "name": "音楽設定",
            "description": "点呼時に再生する音楽を設定",
        },
        "draw_mode": {
            "name": "抽選モード",
            "description": "点呼抽選モードを設定",
            "combo_items": {
                "0": "繰り返し抽選",
                "1": "繰り返さない抽選",
                "2": "半繰り返し抽選",
            },
        },
        "clear_record": {
            "name": "履歴消去方法",
            "description": "抽選履歴を消去するタイミングを設定",
            "combo_items": {"0": "再起動時に消去", "1": "全員抽選するまで"},
            "combo_items_other": {
                "0": "再起動時に消去",
                "1": "全員抽選するまで",
                "2": "消去しない",
            },
        },
        "half_repeat": {
            "name": "半繰り返し抽選回数",
            "description": "各人が何回抽選された後に履歴を消去するかを設定",
        },
        "draw_type": {
            "name": "抽選方法",
            "description": "点呼抽選方法を設定",
            "combo_items": {"0": "ランダム抽選", "1": "公平抽選"},
        },
        "font_size": {
            "name": "フォントサイズ",
            "description": "点呼結果のフォントサイズを設定",
        },
        "reminder_text": {
            "name": "リマインダーテキスト",
            "description": "抽選前とリセット後に表示するテキスト",
        },
        "reminder_font_size": {
            "name": "リマインダーフォントサイズ",
            "description": "リマインダーテキストのフォントサイズ",
        },
        "reminder_text_color": {
            "name": "リマインダー色",
            "description": "リマインダーテキストの色",
        },
        "reminder_text_opacity": {
            "name": "リマインダー透明度",
            "description": "リマインダーテキストの透明度（0-100）",
        },
        "use_global_font": {
            "name": "グローバルフォントを使用",
            "description": "グローバルフォント設定を使用するかどうか",
            "combo_items": {
                "0": "グローバルフォントに従う",
                "1": "カスタムフォントを使用",
            },
        },
        "custom_font": {
            "name": "カスタムフォント",
            "description": "カスタムフォントを選択",
        },
        "display_format": {
            "name": "結果表示フォーマット",
            "description": "点呼結果の表示フォーマットを設定",
            "combo_items": {"0": "学籍番号+氏名", "1": "氏名", "2": "学籍番号"},
        },
        "display_style": {
            "name": "結果表示スタイル",
            "description": "点呼結果の表示スタイルを設定",
            "combo_items": {"0": "デフォルト", "1": "カード"},
        },
        "show_random": {
            "name": "ランダムグループメンバー表示フォーマット",
            "description": "ランダムグループメンバーの表示フォーマットを設定",
            "combo_items": {
                "0": "非表示",
                "1": "グループ[改行]氏名",
                "2": "グループ[ダッシュ]氏名",
            },
        },
        "show_tags": {
            "name": "タグ表示",
            "description": "結果にタグを表示するか設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "アニメーションモード",
            "description": "点呼アニメーションを設定",
            "combo_items": {"0": "手動停止", "1": "自動再生", "2": "結果を直接表示"},
        },
        "animation_interval": {
            "name": "アニメーション間隔",
            "description": "点呼アニメーションの間隔を設定（ミリ秒）",
        },
        "autoplay_count": {
            "name": "自動再生回数",
            "description": "点呼アニメーションの自動再生回数を設定",
        },
        "result_flow_animation_style": {
            "name": "結果レイアウトアニメーション",
            "description": "点呼結果レイアウトの導入アニメーションを設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "result_flow_animation_duration": {
            "name": "結果レイアウト時間",
            "description": "点呼結果レイアウトアニメーションの時間を設定（ミリ秒）",
        },
        "animation_color_theme": {
            "name": "アニメーション/結果カラーテーマ",
            "description": "点呼アニメーション/結果のカラーテーマを設定",
            "combo_items": {"0": "無効", "1": "ランダム色", "2": "固定色"},
        },
        "animation_fixed_color": {
            "name": "アニメーション/結果固定色",
            "description": "点呼アニメーション/結果の固定色を設定",
        },
        "student_image": {
            "name": "学生画像を表示",
            "description": "学生画像を表示するかどうかを設定",
        },
        "student_image_position": {
            "name": "画像位置",
            "description": "結果における学生画像の位置を設定",
            "combo_items": {"0": "左", "1": "上", "2": "右", "3": "下"},
        },
        "open_student_image_folder": {
            "name": "学生画像フォルダ",
            "description": "学生画像ファイルを管理。画像ファイル名は学生氏名と一致する必要があります",
        },
        "default_class": {
            "name": "デフォルトクラス",
            "description": "抽選に使用するデフォルトクラスを設定",
        },
    },
}

# 闪抽设置
quick_draw_settings = {
    "ZH_CN": {
        "title": {"name": "闪抽设置", "description": "闪抽功能设置"},
        "list_specific_settings_entry": {
            "name": "独立名单配置",
            "description": "按名单覆盖闪抽设置（优先于全局设置）",
            "pushbutton_name": "打开",
        },
        "list_specific_settings_window": {
            "name": "独立名单配置",
            "description": "按名单覆盖闪抽设置",
        },
        "list_specific_settings_select_list": {
            "name": "选择名单",
            "description": "选择要配置的抽取名单",
        },
        "list_specific_settings_sync_button": {
            "name": "同步主设置",
            "description": "清除该名单的独立覆盖设置，恢复跟随全局",
            "pushbutton_name": "同步主设置",
        },
        "extraction_function": {
            "name": "抽取功能",
            "description": "设置闪抽抽取功能",
        },
        "display_settings": {
            "name": "显示设置",
            "description": "设置闪抽结果显示方式",
        },
        "basic_animation_settings": {
            "name": "动画设置",
            "description": "设置闪抽动画效果",
        },
        "color_theme_settings": {
            "name": "颜色主题设置",
            "description": "设置闪抽结果颜色主题",
        },
        "student_image_settings": {
            "name": "学生头像设置",
            "description": "设置闪抽结果中学生头像显示",
        },
        "music_settings": {"name": "音乐设置", "description": "设置闪抽时播放的音乐"},
        "draw_mode": {
            "name": "抽取模式",
            "description": "设置闪抽抽取模式",
            "combo_items": ["重复抽取", "不重复抽取", "半重复抽取"],
        },
        "half_repeat": {
            "name": "半重复抽取次数",
            "description": "设置每人被抽中多少次后清除抽取记录",
        },
        "draw_type": {
            "name": "抽取方式",
            "description": "设置闪抽抽取方式",
            "combo_items": ["随机抽取", "公平抽取"],
        },
        "default_class": {
            "name": "默认抽取名单",
            "description": "设置默认使用的抽取名单",
        },
        "disable_after_click": {
            "name": "点击后禁用时间",
            "description": "设置点击一次闪抽后禁用闪抽功能的时间（秒）",
        },
        "draw_count": {
            "name": "抽取人数",
            "description": "设置每次闪抽抽取的人数",
        },
        "font_size": {"name": "字体大小", "description": "设置闪抽结果字体大小"},
        "use_global_font": {
            "name": "使用全局字体",
            "description": "是否使用全局字体设置",
            "combo_items": ["跟随全局字体", "使用自定义字体"],
        },
        "custom_font": {"name": "自定义字体", "description": "选择自定义字体"},
        "display_format": {
            "name": "结果显示格式",
            "description": "设置闪抽结果显示格式",
            "combo_items": ["学号+姓名", "姓名", "学号"],
        },
        "show_random": {
            "name": "显示随机组员格式",
            "description": "设置随机组员显示格式",
            "combo_items": ["不显示", "组名[换行]姓名", "组名[短横杠]姓名"],
        },
        "show_tags": {
            "name": "显示标签",
            "description": "设置是否在结果中显示标签",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "动画模式",
            "description": "设置闪抽抽取动画效果",
            "combo_items": ["自动播放动画", "直接显示结果"],
        },
        "animation_interval": {
            "name": "动画间隔",
            "description": "设置闪抽动画间隔时间(毫秒)",
        },
        "autoplay_count": {
            "name": "自动播放次数",
            "description": "设置闪抽动画自动播放次数",
        },
        "animation_color_theme": {
            "name": "动画/结果颜色主题",
            "description": "设置闪抽动画/结果颜色主题",
            "combo_items": ["关闭", "随机颜色", "固定颜色"],
        },
        "animation_fixed_color": {
            "name": "动画/结果固定颜色",
            "description": "设置闪抽动画/结果固定颜色",
        },
        "student_image": {
            "name": "显示学生图片",
            "description": "设置是否显示学生图片",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "student_image_position": {
            "name": "头像位置",
            "description": "设置学生头像在结果中的位置",
            "combo_items": ["左侧", "顶部", "右侧", "底部"],
        },
        "open_student_image_folder": {
            "name": "学生图片文件夹",
            "description": "管理学生图片文件，图片文件名需与学生姓名一致",
        },
    },
    "EN_US": {
        "title": {"name": "Quick Pick settings", "description": "Quick Pick settings"},
        "list_specific_settings_entry": {
            "name": "Per-list overrides",
            "description": "Override Quick Pick settings per list (higher priority than global)",
            "pushbutton_name": "Open",
        },
        "list_specific_settings_window": {
            "name": "Per-list overrides",
            "description": "Override Quick Pick settings per list",
        },
        "list_specific_settings_select_list": {
            "name": "Select list",
            "description": "Select the student list to configure",
        },
        "list_specific_settings_sync_button": {
            "name": "Sync with global",
            "description": "Clear per-list overrides and follow global settings",
            "pushbutton_name": "Sync with global",
        },
        "extraction_function": {
            "name": "Picking function",
            "description": "Set Quick Pick function",
        },
        "display_settings": {
            "name": "Display settings",
            "description": "Set the method to display Quick Pick results",
        },
        "basic_animation_settings": {
            "name": "Animation settings",
            "description": "Set Quick Pick animations",
        },
        "color_theme_settings": {
            "name": "Color theme settings",
            "description": "Set the color theme of Quick Pick results",
        },
        "student_image_settings": {
            "name": "Student image settings",
            "description": "Set to show student avatars in the Quick Pick results",
        },
        "music_settings": {
            "name": "Music settings",
            "description": "Set the music to play when conducting Quick Pick",
        },
        "draw_mode": {
            "name": "Picking mode",
            "description": "Set Quick Pick mode",
            "combo_items": {
                "0": "Pick with repeating",
                "1": "Pick without repeating",
                "2": "Semi-repeatedly pick",
            },
        },
        "half_repeat": {
            "name": "Semi-repeated pick count",
            "description": "Set the maximum picked times of each person to clean history",
        },
        "draw_type": {
            "name": "Picking method",
            "description": "Set Quick Pick method",
            "combo_items": {"0": "Random pick", "1": "Fair pick"},
        },
        "font_size": {
            "name": "Font size",
            "description": "Set Quick Pick result font size",
        },
        "use_global_font": {
            "name": "Use global font",
            "description": "Whether to use global font settings",
            "combo_items": {"0": "Follow global font", "1": "Use custom font"},
        },
        "custom_font": {
            "name": "Custom font",
            "description": "Select custom font",
        },
        "display_format": {
            "name": "Result display format",
            "description": "Set Quick Pick display format",
            "combo_items": {"0": "Student ID + Name", "1": "Name", "2": "Student ID"},
        },
        "show_random": {
            "name": "Format of showing random group member",
            "description": "Set random group member display format",
            "combo_items": {
                "0": "Hide",
                "1": "Group[New line]Name",
                "2": "Group name[Dash]name",
            },
        },
        "show_tags": {
            "name": "Show tags",
            "description": "Whether to show tags in results",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "Animation mode",
            "description": "Set Quick Pick animations",
            "combo_items": {
                "2": "Directly show result",
                "0": "Automatically play animation",
                "1": "Directly show result (no animation)",
            },
        },
        "animation_interval": {
            "name": "Animation interval",
            "description": "Set the interval between Quick Pick (ms)",
        },
        "autoplay_count": {
            "name": "Autoplay count",
            "description": "Configure Quick Pick animation autoplay count",
        },
        "animation_color_theme": {
            "name": "Animation color theme",
            "description": "Set the animate/result color theme of Quick Pick",
            "combo_items": {"0": "Disabled", "1": "Random color", "2": "Fixed color"},
        },
        "result_color_theme": {
            "name": "Results color theme",
            "description": "Set the color theme of Quick Pick results",
            "combo_items": {"0": "Disabled", "1": "Random color", "2": "Fixed color"},
        },
        "animation_fixed_color": {
            "name": "Animation fixed color",
            "description": "Set the animation/result color of Quick Pick",
        },
        "student_image": {
            "name": "Show student images",
            "description": "Set whether to show student images",
        },
        "student_image_position": {
            "name": "Image position",
            "description": "Set the position of student images in results",
            "combo_items": {"0": "Left", "1": "Top", "2": "Right", "3": "Bottom"},
        },
        "open_student_image_folder": {
            "name": "Student image folder",
            "description": "Manage student image files. Picture file names must match student name",
        },
        "default_class": {
            "name": "Default class",
            "description": "Set the default class to use for drawing",
        },
        "disable_after_click": {
            "name": "Disable after click",
            "description": "Set the time to disable Quick Pick after one click (s)",
        },
        "draw_count": {
            "name": "Draw count",
            "description": "Set the number of students to draw in Quick Pick",
        },
    },
    "JA_JP": {
        "title": {"name": "クイック抽選設定", "description": "クイック抽選設定"},
        "list_specific_settings_entry": {
            "name": "リスト別設定",
            "description": "リストごとにクイック抽選設定を上書き（全体設定より優先）",
            "pushbutton_name": "開く",
        },
        "list_specific_settings_window": {
            "name": "リスト別設定",
            "description": "リストごとにクイック抽選設定を上書き",
        },
        "list_specific_settings_select_list": {
            "name": "リスト選択",
            "description": "設定する学生リストを選択",
        },
        "list_specific_settings_sync_button": {
            "name": "全体設定に同期",
            "description": "リスト別上書きをクリアして全体設定に従う",
            "pushbutton_name": "全体設定に同期",
        },
        "extraction_function": {
            "name": "抽選機能",
            "description": "クイック抽選機能を設定",
        },
        "display_settings": {
            "name": "表示設定",
            "description": "クイック抽選結果の表示方法を設定",
        },
        "basic_animation_settings": {
            "name": "アニメーション設定",
            "description": "クイック抽選アニメーションを設定",
        },
        "color_theme_settings": {
            "name": "カラーテーマ設定",
            "description": "クイック抽選結果のカラーテーマを設定",
        },
        "student_image_settings": {
            "name": "学生アバター設定",
            "description": "クイック抽選結果の学生アバター表示を設定",
        },
        "music_settings": {
            "name": "音楽設定",
            "description": "クイック抽選時に再生する音楽を設定",
        },
        "draw_mode": {
            "name": "抽選モード",
            "description": "クイック抽選モードを設定",
            "combo_items": {
                "0": "繰り返し抽選",
                "1": "繰り返さない抽選",
                "2": "半繰り返し抽選",
            },
        },
        "half_repeat": {
            "name": "半繰り返し抽選回数",
            "description": "各人が何回抽選された後に履歴を消去するかを設定",
        },
        "draw_type": {
            "name": "抽選方法",
            "description": "クイック抽選方法を設定",
            "combo_items": {"0": "ランダム抽選", "1": "公平抽選"},
        },
        "font_size": {
            "name": "フォントサイズ",
            "description": "クイック抽選結果のフォントサイズを設定",
        },
        "use_global_font": {
            "name": "グローバルフォントを使用",
            "description": "グローバルフォント設定を使用するかどうか",
            "combo_items": {
                "0": "グローバルフォントに従う",
                "1": "カスタムフォントを使用",
            },
        },
        "custom_font": {
            "name": "カスタムフォント",
            "description": "カスタムフォントを選択",
        },
        "display_format": {
            "name": "結果表示フォーマット",
            "description": "クイック抽選の表示フォーマットを設定",
            "combo_items": {"0": "学籍番号+氏名", "1": "氏名", "2": "学籍番号"},
        },
        "show_random": {
            "name": "ランダムグループメンバー表示フォーマット",
            "description": "ランダムグループメンバーの表示フォーマットを設定",
            "combo_items": {
                "0": "非表示",
                "1": "グループ[改行]氏名",
                "2": "グループ[ダッシュ]氏名",
            },
        },
        "show_tags": {
            "name": "タグ表示",
            "description": "結果にタグを表示するか設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "アニメーションモード",
            "description": "クイック抽選アニメーションを設定",
            "combo_items": {"0": "自動再生", "1": "結果を直接表示"},
        },
        "animation_interval": {
            "name": "アニメーション間隔",
            "description": "クイック抽選の間隔を設定（ミリ秒）",
        },
        "autoplay_count": {
            "name": "自動再生回数",
            "description": "クイック抽選アニメーションの自動再生回数を設定",
        },
        "animation_color_theme": {
            "name": "アニメーションカラーテーマ",
            "description": "クイック抽選のアニメーション/結果カラーテーマを設定",
            "combo_items": {"0": "無効", "1": "ランダム色", "2": "固定色"},
        },
        "result_color_theme": {
            "name": "結果カラーテーマ",
            "description": "クイック抽選結果のカラーテーマを設定",
            "combo_items": {"0": "無効", "1": "ランダム色", "2": "固定色"},
        },
        "animation_fixed_color": {
            "name": "アニメーション固定色",
            "description": "クイック抽選のアニメーション/結果の色を設定",
        },
        "student_image": {
            "name": "学生画像を表示",
            "description": "学生画像を表示するかどうかを設定",
        },
        "student_image_position": {
            "name": "画像位置",
            "description": "結果における学生画像の位置を設定",
            "combo_items": {"0": "左", "1": "上", "2": "右", "3": "下"},
        },
        "open_student_image_folder": {
            "name": "学生画像フォルダ",
            "description": "学生画像ファイルを管理。画像ファイル名は学生氏名と一致する必要があります",
        },
        "default_class": {
            "name": "デフォルトクラス",
            "description": "抽選に使用するデフォルトクラスを設定",
        },
        "disable_after_click": {
            "name": "クリック後無効化時間",
            "description": "クイック抽選を1回クリックした後に無効化する時間を設定（秒）",
        },
        "draw_count": {
            "name": "抽選人数",
            "description": "クイック抽選で抽選する人数を設定",
        },
    },
}

# 抽奖设置
lottery_settings = {
    "ZH_CN": {
        "title": {"name": "抽奖设置", "description": "抽奖功能设置"},
        "list_specific_settings_entry": {
            "name": "独立名单配置",
            "description": "按奖池覆盖抽奖设置（优先于全局设置）",
            "pushbutton_name": "打开",
        },
        "list_specific_settings_window": {
            "name": "独立名单配置",
            "description": "按奖池覆盖抽奖设置",
        },
        "list_specific_settings_select_pool": {
            "name": "选择奖池",
            "description": "选择要配置的奖池名单",
        },
        "list_specific_settings_sync_button": {
            "name": "同步主设置",
            "description": "清除该奖池的独立覆盖设置，恢复跟随全局",
            "pushbutton_name": "同步主设置",
        },
        "extraction_function": {
            "name": "抽取功能",
            "description": "设置抽奖抽取功能",
        },
        "display_settings": {
            "name": "显示设置",
            "description": "设置抽奖结果显示方式",
        },
        "basic_animation_settings": {
            "name": "动画设置",
            "description": "设置抽奖动画效果",
        },
        "color_theme_settings": {
            "name": "颜色主题设置",
            "description": "设置抽奖结果颜色主题",
        },
        "lottery_image_settings": {
            "name": "奖品图片设置",
            "description": "设置抽奖结果中奖品图片显示",
        },
        "music_settings": {"name": "音乐设置", "description": "设置抽奖时播放的音乐"},
        "draw_mode": {
            "name": "抽取模式",
            "description": "设置抽奖抽取模式",
            "combo_items": ["重复抽取", "不重复抽取", "半重复抽取"],
        },
        "clear_record": {
            "name": "清除抽取记录方式",
            "description": "设置清除抽奖抽取记录方式",
            "combo_items": ["重启后清除", "直到全部抽取完"],
            "combo_items_other": ["重启后清除", "直到全部抽取完", "无需清除"],
        },
        "half_repeat": {
            "name": "半重复抽取次数",
            "description": "设置每人被抽中多少次后清除抽取记录",
        },
        "draw_type": {
            "name": "抽取方式",
            "description": "设置抽奖抽取方式",
            "combo_items": ["份数模式", "数量模式"],
        },
        "default_pool": {
            "name": "默认抽取名单",
            "description": "设置默认使用的抽取名单",
        },
        "font_size": {"name": "字体大小", "description": "设置抽奖结果字体大小"},
        "reminder_text": {
            "name": "抽取前/清空后提示语",
            "description": "设置抽取前和清空后显示的提示文字",
        },
        "reminder_font_size": {
            "name": "提示语字体大小",
            "description": "设置提示语文字的字体大小",
        },
        "reminder_text_color": {
            "name": "提示语颜色",
            "description": "设置提示语文字的颜色",
        },
        "reminder_text_opacity": {
            "name": "提示语透明度",
            "description": "设置提示语文字的透明度（0-100）",
        },
        "use_global_font": {
            "name": "使用全局字体",
            "description": "是否使用全局字体设置",
            "combo_items": ["跟随全局字体", "使用自定义字体"],
        },
        "custom_font": {"name": "自定义字体", "description": "选择自定义字体"},
        "display_format": {
            "name": "结果显示格式",
            "description": "设置抽奖结果显示格式",
            "combo_items": ["序号+名称", "名称", "序号"],
        },
        "display_style": {
            "name": "结果显示样式",
            "description": "设置抽奖结果显示样式",
            "combo_items": ["默认样式", "卡片样式"],
        },
        "show_random": {
            "name": "显示随机学生格式",
            "description": "设置显示随机学生抽取结果格式",
            "combo_items": [
                "奖品[换行]小组[短横杠]姓名",
                "奖品[换行]小组[换行]姓名",
                "奖品[短横杠]小组[短横杠]姓名",
                "奖品[换行]姓名",
                "奖品[短横杠]姓名",
                "奖品[换行]小组",
                "奖品[短横杠]小组",
            ],
        },
        "show_tags": {
            "name": "显示标签",
            "description": "设置是否在结果中显示标签",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "动画模式",
            "description": "设置抽奖抽取动画效果",
            "combo_items": ["手动停止动画", "自动播放动画", "直接显示结果"],
        },
        "animation_interval": {
            "name": "动画间隔",
            "description": "设置抽奖动画间隔时间(毫秒)",
        },
        "autoplay_count": {
            "name": "自动播放次数",
            "description": "设置抽奖动画自动播放次数",
        },
        "result_flow_animation_style": {
            "name": "结果布局动画",
            "description": "设置抽奖结果布局出现动画效果",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "result_flow_animation_duration": {
            "name": "结果布局动画时长",
            "description": "设置抽奖结果布局动画时长（毫秒）",
        },
        "animation_color_theme": {
            "name": "动画/结果颜色主题",
            "description": "设置抽奖动画/结果颜色主题",
            "combo_items": ["关闭", "随机颜色", "固定颜色"],
        },
        "animation_fixed_color": {
            "name": "动画/结果固定颜色",
            "description": "设置抽奖动画/结果固定颜色",
        },
        "lottery_image": {
            "name": "显示奖品图片",
            "description": "设置是否显示奖品图片",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "lottery_image_position": {
            "name": "图片位置",
            "description": "设置奖品图片在结果中的位置",
            "combo_items": ["左侧", "顶部", "右侧", "底部"],
        },
        "open_lottery_image_folder": {
            "name": "奖品图片文件夹",
            "description": "管理奖品图片文件，图片文件名需与奖品名称一致",
        },
        "show_weight_transparency": {
            "name": "权重透明化",
            "description": "在抽取结果界面展示每位参与者的权重明细",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "wp_title": {"name": "权重明细", "description": ""},
        "wp_collapse": {"name": "折叠", "description": ""},
        "wp_expand": {"name": "展开", "description": ""},
        "wp_help": {"name": "查看权重计算规则", "description": ""},
        "wp_col_student": {"name": "学生", "description": ""},
        "wp_col_base": {"name": "基础权重", "description": ""},
        "wp_col_freq": {"name": "频率因子", "description": ""},
        "wp_col_group": {"name": "小组平衡", "description": ""},
        "wp_col_gender": {"name": "性别平衡", "description": ""},
        "wp_col_time": {"name": "时间因子", "description": ""},
        "wp_col_shield": {"name": "屏蔽", "description": ""},
        "wp_col_total": {"name": "最终权重", "description": ""},
        "wp_shielded": {"name": "已屏蔽", "description": ""},
        "wp_normal": {"name": "正常", "description": ""},
        "wp_formula_title": {"name": "权重计算规则", "description": ""},
        "wp_close": {"name": "关闭", "description": ""},
        "wp_formula_content": {
            "name": "**权重计算规则**\n\n**总权重 = 基础权重 + 频率因子 + 小组平衡 + 性别平衡 + 时间因子**\n\n---\n\n**基础权重**: 每个学生的基础分，可在设置中调整（默认1.0）\n\n**频率因子**: 被抽中次数越少 → 因子越高\n• 支持线性/平方根/指数三种算法（可在设置中选择）\n• 前10轮自动保护新同学（冷启动）\n\n**小组平衡**: 所在小组被抽越少 → 加成越高\n\n**性别平衡**: 该性别被抽越少 → 加成越高\n\n**时间因子**: 距离上次被抽越久 → 权重逐渐恢复\n• 超30天未抽 → 时间因子加满\n\n---\n\n**特殊规则**:\n• 屏蔽保护：刚抽过的人在设定时间内几乎不会被再抽到（权重降至 1/10）\n• 权重范围：最低 0.05，最高 5.0\n\n**平均值差值保护**（独立机制）: 只允许抽取次数≤平均值的成员进入候选池，差距过大时排除极值",
            "description": "",
        },
    },
    "EN_US": {
        "title": {"name": "Lottery settings", "description": "Lottery settings"},
        "list_specific_settings_entry": {
            "name": "Per-pool overrides",
            "description": "Override Lottery settings per pool (higher priority than global)",
            "pushbutton_name": "Open",
        },
        "list_specific_settings_window": {
            "name": "Per-pool overrides",
            "description": "Override Lottery settings per pool",
        },
        "list_specific_settings_select_pool": {
            "name": "Select pool",
            "description": "Select the prize pool to configure",
        },
        "list_specific_settings_sync_button": {
            "name": "Sync with global",
            "description": "Clear per-pool overrides and follow global settings",
            "pushbutton_name": "Sync with global",
        },
        "extraction_function": {
            "name": "Picking function",
            "description": "Set lottery function",
        },
        "display_settings": {
            "name": "Display settings",
            "description": "Set the method to display the lottery results",
        },
        "basic_animation_settings": {
            "name": "Animation settings",
            "description": "Set lottery animations",
        },
        "color_theme_settings": {
            "name": "Color theme settings",
            "description": "Set the color theme of lottery results",
        },
        "student_image_settings": {
            "name": "Prize image settings",
            "description": "Set the prize image to display in lottery results",
        },
        "music_settings": {
            "name": "Music settings",
            "description": "Set the music to play when lottery",
        },
        "draw_mode": {
            "name": "Picking mode",
            "description": "Set lottery mode",
            "combo_items": {
                "0": "Pick with repeating",
                "1": "Pick without repeating",
                "2": "Semi-repeatedly pick",
            },
        },
        "clear_record": {
            "name": "Clear history method",
            "description": "Sets the method to clear lottery records",
            "combo_items": {"0": "Clear on restart", "1": "Until all have been picked"},
            "combo_items_other": {
                "0": "Clear on restart",
                "1": "Until all have been picked",
                "2": "Do not clear",
            },
        },
        "half_repeat": {
            "name": "Semi-repeated pick count",
            "description": "Set the maximum picked times of each person to clean history",
        },
        "draw_type": {
            "name": "Draw type",
            "description": "Set lottery draw type",
            "combo_items": {"0": "Piece mode", "1": "Count mode"},
        },
        "font_size": {
            "name": "Font size",
            "description": "Set the lottery result font size",
        },
        "reminder_text": {
            "name": "Reminder text",
            "description": "Text displayed before the draw and after resetting",
        },
        "reminder_font_size": {
            "name": "Reminder font size",
            "description": "Font size for the reminder text",
        },
        "reminder_text_color": {
            "name": "Reminder color",
            "description": "Color of the reminder text",
        },
        "reminder_text_opacity": {
            "name": "Reminder opacity",
            "description": "Opacity of the reminder text (0-100)",
        },
        "use_global_font": {
            "name": "Use global font",
            "description": "Whether to use global font settings",
            "combo_items": {"0": "Follow global font", "1": "Use custom font"},
        },
        "custom_font": {
            "name": "Custom font",
            "description": "Select custom font",
        },
        "display_format": {
            "name": "Result display format",
            "description": "Set the lottery results display format",
            "combo_items": {"0": "Serial + Name", "1": "Name", "2": "Serial"},
        },
        "display_style": {
            "name": "Result display style",
            "description": "Set the lottery result display style",
            "combo_items": {"0": "Default", "1": "Card"},
        },
        "show_random": {
            "name": "Random student display format",
            "description": "Set random student display format in lottery results",
            "combo_items": {
                "0": "Prize[New line]Group[Dash]Name",
                "1": "Prize[New line]Group[New line]Name",
                "2": "Prize[Dash]Group[Dash]Name",
                "3": "Prize[Dash]Name",
                "4": "Prize[New line]Name",
                "5": "Prize[New line]Group",
                "6": "Prize[Dash]Group",
                "7": "Group[New line]Prize[New line]Name",
                "8": "Group[Dash]Prize[Dash]Name",
                "9": "Group[New line]Name[New line]Prize",
                "10": "Name[New line]Prize",
                "11": "Name[Dash]Prize",
            },
        },
        "show_tags": {
            "name": "Show tags",
            "description": "Whether to show tags in results",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "Animation mode",
            "description": "Set lottery animations",
            "combo_items": {
                "0": "Manually stop animation",
                "1": "Automatically play animation",
                "2": "Directly show result (no animation)",
            },
        },
        "animation_interval": {
            "name": "Animation interval",
            "description": "Set the interval between lottery animations (ms)",
        },
        "autoplay_count": {
            "name": "Autoplay count",
            "description": "Set the number of times to animate",
        },
        "result_flow_animation_style": {
            "name": "Result layout animation",
            "description": "Set result flow layout intro animation",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "result_flow_animation_duration": {
            "name": "Result layout duration",
            "description": "Set result layout animation duration (ms)",
        },
        "animation_color_theme": {
            "name": "Animation color theme",
            "description": "Set the animate/result color theme",
            "combo_items": {"0": "Disabled", "1": "Theme color", "2": "Fixed color"},
        },
        "result_color_theme": {
            "name": "Results color theme",
            "description": "Set the color theme of lottery results",
            "combo_items": {"0": "Disabled", "1": "Random color", "2": "Fixed color"},
        },
        "animation_fixed_color": {
            "name": "Animation fixed color",
            "description": "Set the animation/result color",
        },
        "lottery_image": {
            "name": "Show prize images",
            "description": "Set whether to show prize images",
        },
        "lottery_image_position": {
            "name": "Image position",
            "description": "Set the position of prize images in results",
            "combo_items": {"0": "Left", "1": "Top", "2": "Right", "3": "Bottom"},
        },
        "open_lottery_image_folder": {
            "name": "Prize image folder",
            "description": "Manage s\nprize image files. Picture file names must match prize names",
        },
        "lottery_image_settings": {
            "name": "Prize image settings",
            "description": "Set the prize image to display in lottery results",
        },
        "default_pool": {
            "name": "Default pool",
            "description": "Set the default pool to use for drawing",
        },
        "show_weight_transparency": {
            "name": "Weight Transparency",
            "description": "Show weight details for each participant on the result interface",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "wp_title": {"name": "Weight Details", "description": ""},
        "wp_collapse": {"name": "Collapse", "description": ""},
        "wp_expand": {"name": "Expand", "description": ""},
        "wp_help": {"name": "View Weight Calculation Rules", "description": ""},
        "wp_col_student": {"name": "Student", "description": ""},
        "wp_col_base": {"name": "Base", "description": ""},
        "wp_col_freq": {"name": "Frequency", "description": ""},
        "wp_col_group": {"name": "Group", "description": ""},
        "wp_col_gender": {"name": "Gender", "description": ""},
        "wp_col_time": {"name": "Time", "description": ""},
        "wp_col_shield": {"name": "Shield", "description": ""},
        "wp_col_total": {"name": "Total", "description": ""},
        "wp_shielded": {"name": "Shielded", "description": ""},
        "wp_normal": {"name": "Normal", "description": ""},
        "wp_formula_title": {"name": "Weight Calculation Rules", "description": ""},
        "wp_close": {"name": "Close", "description": ""},
        "wp_formula_content": {
            "name": "**Weight Calculation Rules**\n\n**Total Weight = Base Weight + Frequency Factor + Group Balance + Gender Balance + Time Factor**\n\n---\n\n**Base Weight**: Initial weight for each student, adjustable in settings (default 1.0)\n\n**Frequency Factor**: Fewer draws → higher factor\n• Supports Linear/Square Root/Exponential (configurable)\n• First 10 rounds protect new students (Cold Start)\n\n**Group Balance**: Group drawn less often → higher bonus\n\n**Gender Balance**: Gender drawn less often → higher bonus\n\n**Time Factor**: Longer since last drawn → weight gradually recovers\n• Over 30 days → time factor maxed out\n\n---\n\n**Special Rules**:\n• Shield Protection: Recently drawn students are almost never drawn again (weight reduced to 1/10)\n• Weight Range: Minimum 0.05, Maximum 5.0\n\n**Average Gap Protection** (independent mechanism): Only candidates with draw count ≤ average enter the pool; extreme values are excluded when gap is too large",
            "description": "",
        },
    },
    "JA_JP": {
        "title": {"name": "抽選設定", "description": "抽選機能設定"},
        "list_specific_settings_entry": {
            "name": "プール別設定",
            "description": "プールごとに抽選設定を上書き（全体設定より優先）",
            "pushbutton_name": "開く",
        },
        "list_specific_settings_window": {
            "name": "プール別設定",
            "description": "プールごとに抽選設定を上書き",
        },
        "list_specific_settings_select_pool": {
            "name": "プール選択",
            "description": "設定する賞品プールを選択",
        },
        "list_specific_settings_sync_button": {
            "name": "全体設定に同期",
            "description": "プール別上書きをクリアして全体設定に従う",
            "pushbutton_name": "全体設定に同期",
        },
        "extraction_function": {
            "name": "抽選機能",
            "description": "抽選抽選機能を設定",
        },
        "display_settings": {
            "name": "表示設定",
            "description": "抽選結果の表示方法を設定",
        },
        "basic_animation_settings": {
            "name": "アニメーション設定",
            "description": "抽選アニメーション効果を設定",
        },
        "color_theme_settings": {
            "name": "カラーテーマ設定",
            "description": "抽選結果のカラーテーマを設定",
        },
        "lottery_image_settings": {
            "name": "賞品画像設定",
            "description": "抽選結果の賞品画像表示を設定",
        },
        "music_settings": {
            "name": "音楽設定",
            "description": "抽選時に再生する音楽を設定",
        },
        "draw_mode": {
            "name": "抽選モード",
            "description": "抽選抽選モードを設定",
            "combo_items": {
                "0": "繰り返し抽選",
                "1": "繰り返さない抽選",
                "2": "半繰り返し抽選",
            },
        },
        "clear_record": {
            "name": "履歴消去方法",
            "description": "抽選抽選履歴の消去方法を設定",
            "combo_items": {"0": "再起動時に消去", "1": "全員抽選するまで"},
            "combo_items_other": {
                "0": "再起動時に消去",
                "1": "全員抽選するまで",
                "2": "消去しない",
            },
        },
        "half_repeat": {
            "name": "半繰り返し抽選回数",
            "description": "各人が何回抽選された後に履歴を消去するかを設定",
        },
        "draw_type": {
            "name": "抽選方式",
            "description": "抽選方式を設定",
            "combo_items": {"0": "份数モード", "1": "数量モード"},
        },
        "font_size": {
            "name": "フォントサイズ",
            "description": "抽選結果のフォントサイズを設定",
        },
        "reminder_text": {
            "name": "リマインダーテキスト",
            "description": "抽選前とリセット後に表示するテキスト",
        },
        "reminder_font_size": {
            "name": "リマインダーフォントサイズ",
            "description": "リマインダーテキストのフォントサイズ",
        },
        "reminder_text_color": {
            "name": "リマインダー色",
            "description": "リマインダーテキストの色",
        },
        "reminder_text_opacity": {
            "name": "リマインダー透明度",
            "description": "リマインダーテキストの透明度（0-100）",
        },
        "use_global_font": {
            "name": "グローバルフォントを使用",
            "description": "グローバルフォント設定を使用するかどうか",
            "combo_items": {
                "0": "グローバルフォントに従う",
                "1": "カスタムフォントを使用",
            },
        },
        "custom_font": {
            "name": "カスタムフォント",
            "description": "カスタムフォントを選択",
        },
        "display_format": {
            "name": "結果表示フォーマット",
            "description": "抽選結果の表示フォーマットを設定",
            "combo_items": {"0": "番号+名称", "1": "名称", "2": "番号"},
        },
        "display_style": {
            "name": "結果表示スタイル",
            "description": "抽選結果の表示スタイルを設定",
            "combo_items": {"0": "デフォルト", "1": "カード"},
        },
        "show_random": {
            "name": "ランダム学生表示フォーマット",
            "description": "抽選結果のランダム学生表示フォーマットを設定",
            "combo_items": {
                "0": "賞品[改行]グループ[ダッシュ]氏名",
                "1": "賞品[改行]グループ[改行]氏名",
                "2": "賞品[ダッシュ]グループ[ダッシュ]氏名",
                "3": "賞品[ダッシュ]氏名",
                "4": "賞品[改行]氏名",
                "5": "賞品[改行]グループ",
                "6": "賞品[ダッシュ]グループ",
                "7": "グループ[改行]賞品[改行]氏名",
                "8": "グループ[ダッシュ]賞品[ダッシュ]氏名",
                "9": "グループ[改行]氏名[改行]賞品",
                "10": "氏名[改行]賞品",
                "11": "氏名[ダッシュ]賞品",
            },
        },
        "show_tags": {
            "name": "タグ表示",
            "description": "結果にタグを表示するか設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "animation": {
            "name": "アニメーションモード",
            "description": "抽選アニメーションを設定",
            "combo_items": {"0": "手動停止", "1": "自動再生", "2": "結果を直接表示"},
        },
        "animation_interval": {
            "name": "アニメーション間隔",
            "description": "抽選アニメーションの間隔を設定（ミリ秒）",
        },
        "autoplay_count": {
            "name": "自動再生回数",
            "description": "アニメーションの回数を設定",
        },
        "result_flow_animation_style": {
            "name": "結果レイアウトアニメーション",
            "description": "抽選結果レイアウトの導入アニメーションを設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "result_flow_animation_duration": {
            "name": "結果レイアウト時間",
            "description": "抽選結果レイアウトアニメーションの時間を設定（ミリ秒）",
        },
        "animation_color_theme": {
            "name": "アニメーションカラーテーマ",
            "description": "アニメーション/結果のカラーテーマを設定",
            "combo_items": {"0": "無効", "1": "テーマ色", "2": "固定色"},
        },
        "result_color_theme": {
            "name": "結果カラーテーマ",
            "description": "抽選結果のカラーテーマを設定",
            "combo_items": {"0": "無効", "1": "ランダム色", "2": "固定色"},
        },
        "animation_fixed_color": {
            "name": "アニメーション固定色",
            "description": "アニメーション/結果の色を設定",
        },
        "lottery_image": {
            "name": "賞品画像を表示",
            "description": "賞品画像を表示するかどうかを設定",
        },
        "lottery_image_position": {
            "name": "画像位置",
            "description": "結果における賞品画像の位置を設定",
            "combo_items": {"0": "左", "1": "上", "2": "右", "3": "下"},
        },
        "open_lottery_image_folder": {
            "name": "賞品画像フォルダ",
            "description": "賞品画像ファイルを管理。画像ファイル名は賞品名と一致する必要があります",
        },
        "default_pool": {
            "name": "デフォルトプール",
            "description": "抽選に使用するデフォルトプールを設定",
        },
    },
}
# 人脸检测设置（人脸抽取）
face_detector_settings = {
    "ZH_CN": {
        "title": {"name": "人脸抽取", "description": "相机预览人脸检测模型选择"},
        "basic_settings": {"name": "基础设置", "description": "人脸检测基础设置"},
        "advanced_settings": {"name": "高级设置", "description": "人脸检测高级设置"},
        "camera_source": {
            "name": "摄像头",
            "description": "选择人脸抽取使用的摄像头设备",
        },
        "camera_display_resolution": {
            "name": "摄像头显示分辨率",
            "description": "选择摄像头预览采集分辨率（默认使用最高）",
            "recommended_suffix": "推荐",
        },
        "detector_type": {
            "name": "检测模型",
            "description": "选择相机预览的人脸检测模型文件（ONNX）",
        },
        "camera_preview_mode": {
            "name": "模式",
            "description": "选择摄像头预览工作模式",
            "combo_items": ["抽取模式", "识别模式"],
        },
        "model_input_size": {
            "name": "模型输入尺寸",
            "description": "设置输入到人脸检测模型的画面宽高（0 表示使用原始帧）",
        },
        "open_model_folder": {
            "name": "模型文件夹",
            "description": "打开 data/cv_models，用于添加 ONNX 模型文件",
            "pushbutton_name": "打开",
        },
        "picker_animation_settings": {
            "name": "抽取动画",
            "description": "开始抽取后的框选动画设置",
        },
        "picker_frame_color": {"name": "框颜色", "description": "抽取动画圆形框颜色"},
        "picking_duration_seconds": {
            "name": "抽取时长",
            "description": "抽取动画持续时间（秒）",
        },
        "play_process_audio": {
            "name": "过程音效",
            "description": "抽取过程中是否播放音效",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "play_result_audio": {
            "name": "结果音效",
            "description": "抽取结果展示时是否播放音效",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
    },
    "EN_US": {
        "title": {
            "name": "Face picking",
            "description": "Face detection model for camera preview",
        },
        "basic_settings": {
            "name": "Basic settings",
            "description": "Face detection basic settings",
        },
        "advanced_settings": {
            "name": "Advanced settings",
            "description": "Advanced face picking settings",
        },
        "camera_source": {
            "name": "Camera",
            "description": "Select the camera device for face picking",
        },
        "camera_display_resolution": {
            "name": "Camera resolution",
            "description": "Select capture resolution for camera preview (default: highest)",
            "recommended_suffix": "Recommended",
        },
        "detector_type": {
            "name": "Detection model",
            "description": "Select ONNX model file for camera preview",
        },
        "camera_preview_mode": {
            "name": "Mode",
            "description": "Select camera preview mode",
            "combo_items": ["Picking mode", "Recognition mode"],
        },
        "model_input_size": {
            "name": "Model input size",
            "description": "Set input frame width/height for the detector (0 = original frame)",
        },
        "open_model_folder": {
            "name": "Model folder",
            "description": "Open data/cv_models to add ONNX model files",
            "pushbutton_name": "Open",
        },
        "picker_animation_settings": {
            "name": "Picking animation",
            "description": "Circle frame animation after tapping Start",
        },
        "picker_frame_color": {
            "name": "Frame color",
            "description": "Color of circle frames during picking",
        },
        "picking_duration_seconds": {
            "name": "Picking duration",
            "description": "Picking animation duration (seconds)",
        },
        "play_process_audio": {
            "name": "Process audio",
            "description": "Play audio during picking",
            "switchbutton_name": {"enable": "On", "disable": "Off"},
        },
        "play_result_audio": {
            "name": "Result audio",
            "description": "Play audio when showing result",
            "switchbutton_name": {"enable": "On", "disable": "Off"},
        },
    },
    "JA_JP": {
        "title": {
            "name": "顔抽選",
            "description": "カメラプレビューの顔検出モデルを選択",
        },
        "basic_settings": {"name": "基本設定", "description": "顔検出の基本設定"},
        "advanced_settings": {"name": "詳細設定", "description": "顔検出の詳細設定"},
        "camera_source": {
            "name": "カメラ",
            "description": "顔抽選に使用するカメラデバイスを選択",
        },
        "camera_display_resolution": {
            "name": "カメラ解像度",
            "description": "カメラプレビューの解像度を選択（デフォルトは最大）",
            "recommended_suffix": "推奨",
        },
        "detector_type": {
            "name": "検出モデル",
            "description": "カメラプレビューで使用する ONNX モデルファイルを選択",
        },
        "camera_preview_mode": {
            "name": "モード",
            "description": "カメラプレビューの動作モードを選択",
            "combo_items": ["抽選モード", "認識モード"],
        },
        "model_input_size": {
            "name": "モデル入力サイズ",
            "description": "顔検出モデルへ入力するフレームサイズ（0 は元のサイズ）",
        },
        "open_model_folder": {
            "name": "モデルフォルダ",
            "description": "data/cv_models を開いて ONNX モデルを追加",
            "pushbutton_name": "開く",
        },
        "picker_animation_settings": {
            "name": "抽選アニメーション",
            "description": "開始後の枠アニメーション設定",
        },
        "picker_frame_color": {
            "name": "枠色",
            "description": "抽選アニメーションの円枠色",
        },
        "picking_duration_seconds": {
            "name": "抽選時間",
            "description": "抽選アニメーションの時間（秒）",
        },
        "play_process_audio": {
            "name": "過程音声",
            "description": "抽選中に音声を再生するか",
            "switchbutton_name": {"enable": "オン", "disable": "オフ"},
        },
        "play_result_audio": {
            "name": "結果音声",
            "description": "結果表示時に音声を再生するか",
            "switchbutton_name": {"enable": "オン", "disable": "オフ"},
        },
    },
}
