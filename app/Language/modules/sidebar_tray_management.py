# 浮窗管理语言配置
floating_window_management = {
    "ZH_CN": {
        "title": {"name": "浮窗管理", "description": "配置浮动窗口相关设置"},
        "basic_settings": {"name": "基本设置", "description": "配置浮动窗口的基本设置"},
        "appearance_settings": {
            "name": "外观设置",
            "description": "配置浮动窗口的外观设置",
        },
        "edge_settings": {"name": "贴边设置", "description": "配置浮动窗口的贴边设置"},
        "startup_display_floating_window": {
            "name": "软件启动时显示浮窗",
            "description": "控制软件启动时是否自动显示浮动窗口",
            "switchbutton_name": {"enable": "显示", "disable": "隐藏"},
        },
        "floating_window_opacity": {
            "name": "浮窗透明度",
            "description": "调整浮动窗口的透明度",
        },
        "reset_floating_window_position_button": {
            "name": "重置浮窗位置按钮",
            "description": "将浮动窗口位置重置为默认位置",
            "pushbutton_name": "重置位置",
        },
        "floating_window_button_control": {
            "name": "浮窗控件配置",
            "description": "选择在浮动窗口中显示的功能按钮",
            "combo_items": [
                "点名",
                "闪抽",
                "即抽",
                "自定义抽",
                "抽奖",
                "点名+闪抽",
                "点名+自定义抽",
                "点名+抽奖",
                "闪抽+自定义抽",
                "闪抽+抽奖",
                "自定义抽+抽奖",
                "点名+闪抽+自定义抽",
                "点名+闪抽+抽奖",
                "点名+自定义抽+抽奖",
                "闪抽+自定义抽+抽奖",
                "点名+闪抽+自定义抽+抽奖",
            ],
        },
        "floating_window_placement": {
            "name": "浮窗排列",
            "description": "设置浮动窗口中控件的排列方式",
            "combo_items": ["矩形排列", "竖向排列", "横向排列"],
        },
        "floating_window_display_style": {
            "name": "浮窗显示样式",
            "description": "设置浮动窗口中控件的显示样式",
            "combo_items": ["图标+文字", "图标", "文字"],
        },
        "floating_window_stick_to_edge": {
            "name": "贴边设置",
            "description": "控制浮动窗口是否自动贴边",
            "switchbutton_name": {"enable": "贴边", "disable": "不贴边"},
        },
        "floating_window_stick_to_edge_recover_seconds": {
            "name": "贴边收纳时间",
            "description": "设置浮动窗口贴边后自动收纳的时间（秒）",
        },
        "floating_window_stick_to_edge_display_style": {
            "name": "贴边显示样式",
            "description": "设置浮动窗口贴边时的显示样式",
            "combo_items": ["图标", "文字", "箭头"],
        },
    }
}

# 侧边栏/托盘管理语言配置
sidebar_tray_management = {
    "ZH_CN": {
        "title": {
            "name": "侧边栏/托盘管理",
            "description": "配置侧边栏/托盘管理相关设置",
        }
    }
}

# 主界面侧边栏语言配置
sidebar_management_window = {
    "ZH_CN": {
        "title": {
            "name": "主界面侧边栏",
            "description": "配置主界面侧边栏管理相关设置",
        },
        "roll_call_sidebar_position": {
            "name": "点名侧边栏位置",
            "description": "配置点名侧边栏位置",
            "combo_items": ["顶部", "底部", "不显示"],
        },
        "custom_roll_call_sidebar_position": {
            "name": "自定义抽侧边栏位置",
            "description": "配置自定义抽侧边栏位置",
            "combo_items": ["顶部", "底部", "不显示"],
        },
        "lottery_sidebar_position": {
            "name": "抽奖侧边栏位置",
            "description": "配置抽奖侧边栏位置",
            "combo_items": ["顶部", "底部", "不显示"],
        },
        "main_window_history": {
            "name": "主窗口历史记录位置",
            "description": "配置主窗口历史记录位置",
            "combo_items": ["顶部", "底部", "不显示"],
        },
        "settings_icon": {
            "name": "设置图标位置",
            "description": "配置侧边栏管理设置图标位置",
            "combo_items": ["顶部", "底部", "不显示"],
        },
    }
}
