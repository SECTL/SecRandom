# 基础设置语言配置
basic_settings = {
    "ZH_CN": {
        "title": {"name": "基础设置", "description": "配置软件的基本功能和外观"},
        "basic_function": {"name": "基础功能", "description": "配置软件的核心功能选项"},
        "data_management": {
            "name": "数据管理",
            "description": "管理软件的数据导入和导出",
        },
        "personalised": {"name": "个性化", "description": "自定义软件外观和用户体验"},
        "autostart": {
            "name": "开机自启",
            "description": "设置软件是否随系统启动自动运行",
            "switchbutton_name": {"enable": "开启", "disable": "关闭"},
        },
        "check_update": {
            "name": "启动时检查更新",
            "description": "设置软件启动时是否自动检查新版本",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "show_startup_window": {
            "name": "显示启动画面",
            "description": "设置软件启动时是否显示欢迎画面",
            "switchbutton_name": {"enable": "显示", "disable": "不显示"},
        },
        "export_diagnostic_data": {
            "name": "导出诊断数据",
            "description": "退出软件时导出诊断信息，用于问题排查",
            "pushbutton_name": "导出诊断数据",
        },
        "export_settings": {
            "name": "导出设置",
            "description": "将当前设置导出为配置文件，用于备份或迁移",
            "pushbutton_name": "导出设置",
        },
        "import_settings": {
            "name": "导入设置",
            "description": "从配置文件导入设置，覆盖当前配置",
            "pushbutton_name": "导入设置",
        },
        "export_all_data": {
            "name": "导出所有数据",
            "description": "退出软件时导出所有数据和设置",
            "pushbutton_name": "导出所有数据",
        },
        "import_all_data": {
            "name": "导入所有数据",
            "description": "启动软件时从备份文件恢复所有数据",
            "pushbutton_name": "导入所有数据",
        },
        "dpiScale": {
            "name": "DPI缩放",
            "description": "调整软件界面缩放比例（需要重启软件才能生效）",
            "combo_items": ["100%", "125%", "150%", "175%", "200%", "自动"],
        },
        "font": {
            "name": "字体",
            "description": "设置软件界面显示字体（需要重启软件才能生效）",
        },
        "theme": {
            "name": "主题模式",
            "description": "选择软件界面的主题样式",
            "combo_items": ["浅色", "深色", "跟随系统"],
        },
        "theme_color": {"name": "主题颜色", "description": "设置软件界面的主题色彩"},
        "language": {
            "name": "显示语言",
            "description": "切换软件界面语言（需要重启软件才能生效）",
        },
    }
}
