# 历史记录语言配置
history = {
    "ZH_CN": {
        "title": {"name": "历史记录", "description": "查看和管理点名及抽奖的历史记录"}
    }
}

# 历史记录管理语言配置
history_management = {
    "ZH_CN": {
        "title": {"name": "历史记录管理", "description": "管理点名和抽奖的历史记录"},
        "roll_call": {
            "name": "点名历史记录",
            "description": "查看和管理点名历史记录",
        },
        "lottery_history": {
            "name": "抽奖历史记录",
            "description": "查看和管理抽奖历史记录",
        },
        "show_roll_call_history": {
            "name": "启用点名历史记录",
            "description": "控制是否显示点名历史记录功能",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "select_class_name": {
            "name": "选择班级",
            "description": "选择要查看历史记录的班级",
        },
        "clear_roll_call_history": {
            "name": "清除点名历史记录",
            "description": "清除所选班级的点名历史记录",
            "pushbutton_name": "清除",
        },
        "show_lottery_history": {
            "name": "启用抽奖历史记录",
            "description": "控制是否显示抽奖历史记录功能",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "select_pool_name": {
            "name": "选择奖池",
            "description": "选择要查看历史记录的奖池",
        },
        "clear_lottery_history": {
            "name": "清除抽奖历史记录",
            "description": "清除所选奖池的抽奖历史记录",
            "pushbutton_name": "清除",
        },
    }
}

# 点名历史记录表格语言配置
roll_call_history_table = {
    "ZH_CN": {
        "title": {
            "name": "点名历史记录表格",
            "description": "以表格形式展示点名历史记录",
        },
        "select_class_name": {
            "name": "选择班级",
            "description": "选择要查看历史记录的班级",
        },
        "select_mode": {
            "name": "查看模式",
            "description": "选择历史记录的查看方式",
            "combo_items": ["全部记录", "按时间查看"],
        },
        "HeaderLabels_all_not_weight": {
            "name": ["学号", "姓名", "性别", "小组", "点名次数"],
            "description": "点名历史记录表格的列标题（不包含权重）",
        },
        "HeaderLabels_all_weight": {
            "name": ["学号", "姓名", "性别", "小组", "点名次数", "权重"],
            "description": "点名历史记录表格的列标题（包含权重）",
        },
        "HeaderLabels_time_not_weight": {
            "name": ["点名时间", "学号", "姓名", "性别", "小组"],
            "description": "点名历史记录表格的列标题（按时间查看，不包含权重）",
        },
        "HeaderLabels_time_weight": {
            "name": ["点名时间", "学号", "姓名", "性别", "小组", "权重"],
            "description": "点名历史记录表格的列标题（按时间查看，包含权重）",
        },
        "HeaderLabels_Individual_not_weight": {
            "name": ["点名时间", "点名模式", "点名人数", "性别限制", "小组限制"],
            "description": "点名历史记录表格的列标题（个人记录，不包含权重）",
        },
        "HeaderLabels_Individual_weight": {
            "name": [
                "点名时间",
                "点名模式",
                "点名人数",
                "性别限制",
                "小组限制",
                "权重",
            ],
            "description": "点名历史记录表格的列标题（个人记录，包含权重）",
        },
        "select_weight": {
            "name": "显示权重",
            "description": "是否在表格中显示权重信息",
            "switchbutton_name": {"enable": "显示", "disable": "隐藏"},
        },
    }
}

# 抽奖历史记录表格语言配置
lottery_history_table = {
    "ZH_CN": {
        "title": {
            "name": "抽奖历史记录表格",
            "description": "以表格形式展示抽奖历史记录",
        },
        "select_pool_name": {
            "name": "选择奖池",
            "description": "选择要查看历史记录的奖池",
        },
        "select_mode": {
            "name": "查看模式",
            "description": "选择历史记录的查看方式",
            "combo_items": ["全部记录", "按时间查看"],
        },
        "HeaderLabels_all_weight": {
            "name": ["序号", "名称", "中奖次数", "权重"],
            "description": "抽奖历史记录表格的列标题（全部记录）",
        },
        "HeaderLabels_time_weight": {
            "name": ["抽奖时间", "序号", "名称", "权重"],
            "description": "抽奖历史记录表格的列标题（按时间查看）",
        },
        "HeaderLabels_Individual_weight": {
            "name": ["抽奖时间", "抽奖模式", "抽取数量", "权重设置"],
            "description": "抽奖历史记录表格的列标题（单次记录）",
        },
    }
}
