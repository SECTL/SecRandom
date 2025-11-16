# 通知设置语言配置
notification_settings = {
    "ZH_CN": {"title": {"name": "通知设置", "description": "通知设置"}}
}

# 点名通知设置语言配置
roll_call_notification_settings = {
    "ZH_CN": {
        "title": {"name": "点名通知设置", "description": "点名通知设置"},
        "basic_settings": {"name": "基础设置", "description": "配置通知显示的基础参数"},
        "window_mode": {
            "name": "窗口模式",
            "description": "配置点名通知窗口的显示方式",
        },
        "floating_window_mode": {
            "name": "浮动窗口模式",
            "description": "配置点名通知浮动窗口的行为",
        },
        "call_notification_service": {  # TODO: 待完善
            "name": "调用通知服务",
            "description": "启用后将调用系统通知服务发送点名结果",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "notification_mode": {
            "name": "通知模式",
            "description": "设置通知显示模式，可选择普通窗口或浮动窗口",
            "combo_items": ["窗口", "浮动窗口"],
        },
        "animation": {
            "name": "动画效果",
            "description": "控制点名通知窗口是否显示动画效果",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "enabled_monitor": {
            "name": "显示器选择",
            "description": "选择用于显示点名通知的显示器",
        },
        "window_position": {
            "name": "窗口位置",
            "description": "设置点名通知窗口在屏幕上的显示位置",
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
        "horizontal_offset": {
            "name": "水平偏移",
            "description": "调整点名通知窗口相对默认位置的水平偏移量（像素）",
        },
        "vertical_offset": {
            "name": "垂直偏移",
            "description": "调整点名通知窗口相对默认位置的垂直偏移量（像素）",
        },
        "transparency": {
            "name": "透明度",
            "description": "调整点名通知窗口的透明度，数值越小越透明（0-100）",
        },
        "floating_window_enabled_monitor": {
            "name": "显示器选择",
            "description": "选择用于显示点名通知浮动窗口的显示器",
        },
        "floating_window_position": {
            "name": "浮动窗口位置",
            "description": "设置点名通知浮动窗口在屏幕上的显示位置",
            "combo_items": [
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
            "description": "调整点名通知浮动窗口相对默认位置的水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "调整点名通知浮动窗口相对默认位置的垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "透明度",
            "description": "调整点名通知浮动窗口的透明度，数值越小越透明（0-100）",
        },
    }
}

# 闪抽通知设置
quick_draw_notification_settings = {
    "ZH_CN": {
        "title": {
            "name": "闪抽通知设置",
            "description": "配置闪抽结果通知的显示方式和参数",
        },
        "basic_settings": {
            "name": "基础设置",
            "description": "配置闪抽通知的基础显示参数",
        },
        "window_mode": {
            "name": "窗口模式",
            "description": "设置闪抽通知窗口的显示方式",
        },
        "floating_window_mode": {
            "name": "浮动窗口模式",
            "description": "设置闪抽通知浮动窗口的行为模式",
        },
        "notification_mode": {
            "name": "通知模式",
            "description": "设置闪抽通知的显示模式，可选择普通窗口或浮动窗口",
            "combo_items": ["窗口", "浮动窗口"],
        },
        "animation": {
            "name": "动画",
            "description": "设置闪抽通知窗口的显示动画效果",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "enabled_monitor": {
            "name": "选择闪抽通知显示的显示器",
            "description": "选择闪抽通知窗口显示的显示器",
        },
        "window_position": {
            "name": "窗口位置",
            "description": "设置闪抽通知窗口在屏幕上的显示位置",
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
        "horizontal_offset": {
            "name": "水平偏移",
            "description": "设置闪抽通知窗口相对默认位置的水平偏移量（像素）",
        },
        "vertical_offset": {
            "name": "垂直偏移",
            "description": "设置闪抽通知窗口相对默认位置的垂直偏移量（像素）",
        },
        "transparency": {
            "name": "透明度",
            "description": "设置闪抽通知窗口的透明度，数值越小越透明（0-100）",
        },
        "floating_window_enabled_monitor": {
            "name": "选择闪抽通知显示的显示器",
            "description": "选择闪抽通知浮动窗口显示的显示器",
        },
        "floating_window_position": {
            "name": "浮动窗口位置",
            "description": "设置闪抽通知浮动窗口在屏幕上的显示位置",
            "combo_items": [
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
            "description": "设置闪抽通知浮动窗口相对默认位置的水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "设置闪抽通知浮动窗口相对默认位置的垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "浮动窗口透明度",
            "description": "设置闪抽通知浮动窗口的透明度，数值越小越透明（0-100）",
        },
    }
}

# 即抽通知设置
instant_draw_notification_settings = {
    "ZH_CN": {
        "title": {
            "name": "即抽通知设置",
            "description": "配置即抽结果通知的显示方式和参数",
        },
        "basic_settings": {
            "name": "基础设置",
            "description": "配置即抽通知的基础显示参数",
        },
        "window_mode": {
            "name": "窗口模式",
            "description": "设置即抽通知窗口的显示方式",
        },
        "floating_window_mode": {
            "name": "浮动窗口模式",
            "description": "设置即抽通知浮动窗口的行为模式",
        },
        "notification_mode": {
            "name": "通知模式",
            "description": "设置通知显示模式，可选择普通窗口或浮动窗口",
            "combo_items": ["窗口", "浮动窗口"],
        },
        "animation": {
            "name": "动画",
            "description": "设置即抽通知窗口的显示动画效果",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "enabled_monitor": {
            "name": "选择即抽通知显示的显示器",
            "description": "选择即抽通知窗口显示的显示器",
        },
        "window_position": {
            "name": "窗口位置",
            "description": "设置即抽通知窗口在屏幕上的显示位置",
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
        "horizontal_offset": {
            "name": "水平偏移",
            "description": "设置即抽通知窗口相对默认位置的水平偏移量（像素）",
        },
        "vertical_offset": {
            "name": "垂直偏移",
            "description": "设置即抽通知窗口相对默认位置的垂直偏移量（像素）",
        },
        "transparency": {
            "name": "透明度",
            "description": "设置即抽通知窗口的透明度，数值越小越透明（0-100）",
        },
        "floating_window_enabled_monitor": {
            "name": "选择即抽通知显示的显示器",
            "description": "选择即抽通知浮动窗口显示的显示器",
        },
        "floating_window_position": {
            "name": "浮动窗口位置",
            "description": "设置即抽通知浮动窗口在屏幕上的显示位置",
            "combo_items": [
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
            "description": "设置即抽通知浮动窗口相对默认位置的水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "设置即抽通知浮动窗口相对默认位置的垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "浮动窗口透明度",
            "description": "设置即抽通知浮动窗口的透明度，数值越小越透明（0-100）",
        },
    }
}

# 自定义抽通知设置
custom_draw_notification_settings = {
    "ZH_CN": {
        "title": {"name": "自定义抽通知设置", "description": "自定义抽通知设置"},
        "basic_settings": {"name": "基础设置", "description": "基础设置"},
        "window_mode": {
            "name": "窗口模式",
            "description": "设置自定义抽取通知窗口的显示方式",
        },
        "floating_window_mode": {
            "name": "浮动窗口模式",
            "description": "设置自定义抽取通知浮动窗口的行为模式",
        },
        "call_notification_service": {  # TODO: 待完善
            "name": "调用通知服务",
            "description": "是否调用系统通知服务发送自定义抽取结果通知",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "notification_mode": {
            "name": "通知模式",
            "description": "设置通知显示模式，可选择普通窗口或浮动窗口",
            "combo_items": ["窗口", "浮动窗口"],
        },
        "animation": {
            "name": "动画",
            "description": "设置自定义抽取通知窗口的显示动画效果",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "enabled_monitor": {
            "name": "选择自定义抽取通知显示的显示器",
            "description": "选择自定义抽取通知窗口显示的显示器",
        },
        "window_position": {
            "name": "窗口位置",
            "description": "设置自定义抽取通知窗口在屏幕上的显示位置",
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
        "horizontal_offset": {
            "name": "水平偏移",
            "description": "设置自定义抽取通知窗口相对默认位置的水平偏移量（像素）",
        },
        "vertical_offset": {
            "name": "垂直偏移",
            "description": "设置自定义抽取通知窗口相对默认位置的垂直偏移量（像素）",
        },
        "transparency": {
            "name": "透明度",
            "description": "设置自定义抽取通知窗口的透明度，数值越小越透明（0-100）",
        },
        "floating_window_enabled_monitor": {
            "name": "选择自定义抽取通知显示的显示器",
            "description": "选择自定义抽取通知浮动窗口显示的显示器",
        },
        "floating_window_position": {
            "name": "浮动窗口位置",
            "description": "设置自定义抽取通知浮动窗口在屏幕上的显示位置",
            "combo_items": [
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
            "description": "设置自定义抽取通知浮动窗口相对默认位置的水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "设置自定义抽取通知浮动窗口相对默认位置的垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "浮动窗口透明度",
            "description": "设置自定义抽取通知浮动窗口的透明度，数值越小越透明（0-100）",
        },
    }
}

# 抽奖通知设置
lottery_notification_settings = {
    "ZH_CN": {
        "title": {
            "name": "抽奖通知设置",
            "description": "配置抽奖结果通知的显示方式和参数",
        },
        "basic_settings": {
            "name": "基础设置",
            "description": "配置抽奖通知的基础显示参数",
        },
        "window_mode": {
            "name": "窗口模式",
            "description": "设置抽奖通知窗口的显示方式",
        },
        "floating_window_mode": {
            "name": "浮动窗口模式",
            "description": "设置抽奖通知浮动窗口的行为模式",
        },
        "call_notification_service": {  # TODO: 对应功能还未实现，暂时不做优化
            "name": "调用通知服务",
            "description": "是否调用系统通知服务发送抽奖结果通知",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "notification_mode": {
            "name": "通知模式",
            "description": "设置通知显示模式，可选择普通窗口或浮动窗口",
            "combo_items": ["窗口", "浮动窗口"],
        },
        "animation": {
            "name": "动画",
            "description": "设置抽奖通知窗口的显示动画效果",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "enabled_monitor": {
            "name": "选择抽奖通知显示的显示器",
            "description": "选择抽奖通知窗口显示的显示器",
        },
        "window_position": {
            "name": "窗口位置",
            "description": "设置抽奖通知窗口在屏幕上的显示位置",
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
        "horizontal_offset": {
            "name": "水平偏移",
            "description": "设置抽奖通知窗口相对默认位置的水平偏移量（像素）",
        },
        "vertical_offset": {
            "name": "垂直偏移",
            "description": "设置抽奖通知窗口相对默认位置的垂直偏移量（像素）",
        },
        "transparency": {
            "name": "透明度",
            "description": "设置抽奖通知窗口的透明度，数值越小越透明（0-100）",
        },
        "floating_window_enabled_monitor": {
            "name": "选择抽奖通知显示的显示器",
            "description": "选择抽奖通知浮动窗口显示的显示器",
        },
        "floating_window_position": {
            "name": "浮动窗口位置",
            "description": "设置抽奖通知浮动窗口在屏幕上的显示位置",
            "combo_items": [
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
            "description": "设置抽奖通知浮动窗口相对默认位置的水平偏移量（像素）",
        },
        "floating_window_vertical_offset": {
            "name": "垂直偏移",
            "description": "设置抽奖通知浮动窗口相对默认位置的垂直偏移量（像素）",
        },
        "floating_window_transparency": {
            "name": "浮动窗口透明度",
            "description": "设置抽奖通知浮动窗口的透明度，数值越小越透明（0-100）",
        },
    }
}
