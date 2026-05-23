# 名单管理语言配置
list_management = {
    "ZH_CN": {
        "title": {"name": "名单管理", "description": "管理点名、抽奖的名单"},
        "roll_call_list": {"name": "点名名单", "description": "管理点名用学生名单"},
        "lottery_list": {"name": "抽奖名单", "description": "管理抽奖用奖品名单"},
    },
    "EN_US": {
        "title": {
            "name": "List management",
            "description": "Manage the list of picking and lottery",
        },
        "roll_call_list": {
            "name": "Picking list",
            "description": "Manage list of named students",
        },
        "lottery_list": {
            "name": "Lottery list",
            "description": "Manage the list of prizes",
        },
    },
    "JA_JP": {
        "title": {"name": "リスト管理", "description": "点呼、抽選のリストを管理"},
        "roll_call_list": {
            "name": "点呼リスト",
            "description": "点呼用学生リストを管理",
        },
        "lottery_list": {"name": "抽選リスト", "description": "抽選用賞品リストを管理"},
    },
}

# 点名名单语言配置
roll_call_list = {
    "ZH_CN": {
        "title": {"name": "点名名单", "description": "管理点名用学生名单"},
        "set_class_name": {"name": "设置班级名称", "description": "设置当前班级名称"},
        "select_class_name": {
            "name": "选择班级",
            "description": "从已有班级中选择一个班级",
        },
        "import_student_name": {
            "name": "导入学生名单",
            "description": "从文件导入学生名单",
        },
        "name_setting": {"name": "设置姓名", "description": "设置学生姓名"},
        "gender_setting": {"name": "设置性别", "description": "设置学生性别"},
        "group_setting": {"name": "设置小组", "description": "设置学生所属小组"},
        "tag_setting": {"name": "标签设置", "description": "设置学生标签"},
        "export_student_name": {
            "name": "导出学生名单",
            "description": "将学生名单导出到文件",
        },
    },
    "EN_US": {
        "title": {
            "name": "Picking list",
            "description": "Show and manage student list",
        },
        "set_class_name": {
            "name": "Set class name",
            "description": "Set current class name",
        },
        "select_class_name": {
            "name": "Select class",
            "description": "Select a class in existing ones",
        },
        "import_student_name": {
            "name": "Import student name",
            "description": "Import student list from file",
        },
        "name_setting": {"name": "Name settings", "description": "Set student name"},
        "gender_setting": {
            "name": "Gender settings",
            "description": "Set student gender",
        },
        "group_setting": {
            "name": "Group settings",
            "description": "Set group of students",
        },
        "tag_setting": {"name": "Tag settings", "description": "Set student tags"},
        "export_student_name": {
            "name": "Export student list",
            "description": "Export student list to file",
        },
    },
    "JA_JP": {
        "title": {"name": "点呼リスト", "description": "点呼用学生リストの表示と管理"},
        "set_class_name": {
            "name": "クラス名を設定",
            "description": "現在のクラス名を設定",
        },
        "select_class_name": {
            "name": "クラスを選択",
            "description": "既存のクラスから選択",
        },
        "import_student_name": {
            "name": "学生リストをインポート",
            "description": "ファイルから学生リストをインポート",
        },
        "name_setting": {"name": "氏名設定", "description": "学生の氏名を設定"},
        "gender_setting": {
            "name": "性別設定",
            "description": "学生の性別を設定",
        },
        "group_setting": {
            "name": "グループ設定",
            "description": "学生の所属グループを設定",
        },
        "tag_setting": {"name": "タグ設定", "description": "学生のタグを設定"},
        "export_student_name": {
            "name": "学生リストをエクスポート",
            "description": "学生リストをファイルにエクスポート",
        },
    },
}

# 点名表格语言配置
roll_call_table = {
    "ZH_CN": {
        "title": {"name": "点名表格", "description": "展示和管理点名名单"},
        "select_class_name": {
            "name": "选择班级",
            "description": "选择要显示的点名班级",
        },
        "search": {
            "name": "搜索名单",
            "description": "在名单中搜索学生",
        },
        "search_placeholder": {
            "name": "输入学号、姓名、性别、小组或标签进行筛选",
            "description": "搜索框占位符",
        },
        "HeaderLabels": {
            "name": ["存在", "学号", "姓名", "性别", "小组", "标签"],
            "description": "点名表格的列标题",
        },
        "tag_setting_button": {
            "name": "标签设置",
            "description": "标签设置按钮文本",
        },
        "tag_setting_dialog_title": {
            "name": "标签设置",
            "description": "标签设置对话框标题",
        },
        "tag_setting_no_selection": {
            "name": "请先在表格中选择一行",
            "description": "未选择行提示",
        },
        "tag_setting_dialog_placeholder": {
            "name": "用逗号/空格分隔多个标签",
            "description": "标签输入框占位符",
        },
        "tag_setting_dialog_ok": {"name": "确定", "description": "确定按钮文本"},
        "tag_setting_dialog_cancel": {"name": "取消", "description": "取消按钮文本"},
    },
    "EN_US": {
        "title": {"name": "Picking table", "description": "Show and manage name list"},
        "select_class_name": {
            "name": "Select class",
            "description": "Select the class to show",
        },
        "search": {
            "name": "Search list",
            "description": "Search students in the list",
        },
        "search_placeholder": {
            "name": "Filter by ID, name, gender, group or tags",
            "description": "Search box placeholder",
        },
        "HeaderLabels": {
            "name": {
                "0": "Exist",
                "1": "Student ID",
                "2": "Name",
                "3": "Gender",
                "4": "Group",
                "5": "Tags",
            },
            "description": "The column title of the picking table",
        },
        "tag_setting_button": {
            "name": "Tag settings",
            "description": "Tag settings button text",
        },
        "tag_setting_dialog_title": {
            "name": "Tag settings",
            "description": "Tag settings dialog title",
        },
        "tag_setting_no_selection": {
            "name": "Please select a row in the table first",
            "description": "No selection hint",
        },
        "tag_setting_dialog_placeholder": {
            "name": "Separate tags with comma/space",
            "description": "Tag input placeholder",
        },
        "tag_setting_dialog_ok": {"name": "OK", "description": "OK button text"},
        "tag_setting_dialog_cancel": {
            "name": "Cancel",
            "description": "Cancel button text",
        },
    },
    "JA_JP": {
        "title": {"name": "点呼テーブル", "description": "点呼リストの表示と管理"},
        "select_class_name": {
            "name": "クラスを選択",
            "description": "表示する点呼クラスを選択",
        },
        "search": {
            "name": "リスト検索",
            "description": "リスト内の学生を検索",
        },
        "search_placeholder": {
            "name": "学籍番号、氏名、性別、グループ、タグで絞り込み",
            "description": "検索ボックスのプレースホルダー",
        },
        "HeaderLabels": {
            "name": ["存在", "学籍番号", "氏名", "性別", "グループ", "タグ"],
            "description": "点呼テーブルの列タイトル",
        },
        "tag_setting_button": {
            "name": "タグ設定",
            "description": "タグ設定ボタンのテキスト",
        },
        "tag_setting_dialog_title": {
            "name": "タグ設定",
            "description": "タグ設定ダイアログのタイトル",
        },
        "tag_setting_no_selection": {
            "name": "先にテーブルの行を選択してください",
            "description": "未選択ヒント",
        },
        "tag_setting_dialog_placeholder": {
            "name": "カンマ/スペースで区切る",
            "description": "タグ入力プレースホルダー",
        },
        "tag_setting_dialog_ok": {
            "name": "確定",
            "description": "確定ボタンのテキスト",
        },
        "tag_setting_dialog_cancel": {
            "name": "キャンセル",
            "description": "キャンセルボタンのテキスト",
        },
    },
}

# 抽奖名单语言配置
lottery_list = {
    "ZH_CN": {
        "title": {"name": "抽奖名单", "description": "管理抽奖用奖品名单"},
        "set_pool_name": {"name": "设置奖池名称", "description": "设置当前奖池名称"},
        "select_pool_name": {
            "name": "选择奖池",
            "description": "从已有奖池中选择一个奖池",
        },
        "import_prize_name": {
            "name": "导入奖品名单",
            "description": "从文件导入奖品名单",
        },
        "prize_setting": {"name": "设置奖品", "description": "设置奖品名称"},
        "prize_weight_setting": {"name": "设置权重", "description": "设置奖品中奖权重"},
        "tag_setting": {"name": "标签设置", "description": "设置奖品标签"},
        "prize_count_setting": {"name": "设置数量", "description": "设置奖品中奖数量"},
        "export_prize_name": {
            "name": "导出奖品名单",
            "description": "将奖品名单导出到文件",
        },
    },
    "EN_US": {
        "title": {"name": "Lottery list", "description": "Manage prize list"},
        "set_pool_name": {
            "name": "Set pool name",
            "description": "Set current pool name",
        },
        "select_pool_name": {
            "name": "Select pool",
            "description": "Select a pool in existing ones",
        },
        "import_prize_name": {
            "name": "Import prize list",
            "description": "Import prize list from file",
        },
        "prize_setting": {"name": "Set prize", "description": "Set prize name"},
        "prize_weight_setting": {
            "name": "Weight settings",
            "description": "Set prize weight",
        },
        "tag_setting": {"name": "Tag settings", "description": "Set prize tags"},
        "prize_count_setting": {
            "name": "Count settings",
            "description": "Set prize count",
        },
        "export_prize_name": {
            "name": "Export prize list",
            "description": "Export prize list to file",
        },
    },
    "JA_JP": {
        "title": {"name": "抽選リスト", "description": "抽選用賞品リストを管理"},
        "set_pool_name": {
            "name": "賞プール名を設定",
            "description": "現在の賞プール名を設定",
        },
        "select_pool_name": {
            "name": "賞プールを選択",
            "description": "既存の賞プールから選択",
        },
        "import_prize_name": {
            "name": "賞品リストをインポート",
            "description": "ファイルから賞品リストをインポート",
        },
        "prize_setting": {"name": "賞品設定", "description": "賞品名を設定"},
        "prize_weight_setting": {
            "name": "重み設定",
            "description": "賞品の当選重みを設定",
        },
        "tag_setting": {"name": "タグ設定", "description": "賞品のタグを設定"},
        "prize_count_setting": {
            "name": "数量設定",
            "description": "賞品の当選数量を設定",
        },
        "export_prize_name": {
            "name": "賞品リストをエクスポート",
            "description": "賞品リストをファイルにエクスポート",
        },
    },
}

# 抽奖表格语言配置
lottery_table = {
    "ZH_CN": {
        "title": {"name": "抽奖表格", "description": "展示和管理抽奖名单"},
        "select_pool_name": {
            "name": "选择奖池",
            "description": "选择要显示的抽奖奖池",
        },
        "search": {
            "name": "搜索名单",
            "description": "在名单中搜索奖品",
        },
        "search_placeholder": {
            "name": "输入序号、奖品、权重、标签或数量进行筛选",
            "description": "搜索框占位符",
        },
        "HeaderLabels": {
            "name": ["存在", "序号", "奖品", "权重", "标签", "数量"],
            "description": "抽奖表格的列标题",
        },
        "tag_setting_button": {
            "name": "标签设置",
            "description": "标签设置按钮文本",
        },
        "tag_setting_dialog_title": {
            "name": "标签设置",
            "description": "标签设置对话框标题",
        },
        "tag_setting_no_selection": {
            "name": "请先在表格中选择一行",
            "description": "未选择行提示",
        },
        "tag_setting_dialog_placeholder": {
            "name": "用逗号/空格分隔多个标签",
            "description": "标签输入框占位符",
        },
        "tag_setting_dialog_ok": {"name": "确定", "description": "确定按钮文本"},
        "tag_setting_dialog_cancel": {"name": "取消", "description": "取消按钮文本"},
    },
    "EN_US": {
        "title": {"name": "Lottery table", "description": "Show and manage prize list"},
        "select_pool_name": {
            "name": "Select pool",
            "description": "Select the pool to show",
        },
        "search": {
            "name": "Search list",
            "description": "Search prizes in the list",
        },
        "search_placeholder": {
            "name": "Filter by serial, prize, weight, tags or count",
            "description": "Search box placeholder",
        },
        "HeaderLabels": {
            "name": {
                "0": "Exist",
                "1": "Serial",
                "2": "Prize",
                "3": "Weight",
                "4": "Tags",
                "5": "Count",
            },
            "description": "The column title of the lottery table",
        },
        "tag_setting_button": {
            "name": "Tag settings",
            "description": "Tag settings button text",
        },
        "tag_setting_dialog_title": {
            "name": "Tag settings",
            "description": "Tag settings dialog title",
        },
        "tag_setting_no_selection": {
            "name": "Please select a row in the table first",
            "description": "No selection hint",
        },
        "tag_setting_dialog_placeholder": {
            "name": "Separate tags with comma/space",
            "description": "Tag input placeholder",
        },
        "tag_setting_dialog_ok": {"name": "OK", "description": "OK button text"},
        "tag_setting_dialog_cancel": {
            "name": "Cancel",
            "description": "Cancel button text",
        },
    },
    "JA_JP": {
        "title": {"name": "抽選テーブル", "description": "抽選リストの表示と管理"},
        "select_pool_name": {
            "name": "賞プールを選択",
            "description": "表示する抽選賞プールを選択",
        },
        "search": {
            "name": "リスト検索",
            "description": "リスト内の賞品を検索",
        },
        "search_placeholder": {
            "name": "番号、賞品、重み、タグ、数量で絞り込み",
            "description": "検索ボックスのプレースホルダー",
        },
        "HeaderLabels": {
            "name": ["存在", "番号", "賞品", "重み", "タグ", "数量"],
            "description": "抽選テーブルの列タイトル",
        },
        "tag_setting_button": {
            "name": "タグ設定",
            "description": "タグ設定ボタンのテキスト",
        },
        "tag_setting_dialog_title": {
            "name": "タグ設定",
            "description": "タグ設定ダイアログのタイトル",
        },
        "tag_setting_no_selection": {
            "name": "先にテーブルの行を選択してください",
            "description": "未選択ヒント",
        },
        "tag_setting_dialog_placeholder": {
            "name": "カンマ/スペースで区切る",
            "description": "タグ入力プレースホルダー",
        },
        "tag_setting_dialog_ok": {
            "name": "確定",
            "description": "確定ボタンのテキスト",
        },
        "tag_setting_dialog_cancel": {
            "name": "キャンセル",
            "description": "キャンセルボタンのテキスト",
        },
    },
}

# 通知文本配置
notification = {
    "ZH_CN": {
        # 点名名单通知
        "roll_call": {
            "class_name_setting": {
                "title": {
                    "name": "班级名称设置",
                    "description": "班级名称设置通知标题",
                },
                "content": {
                    "name": "已打开班级名称设置窗口",
                    "description": "班级名称设置通知内容",
                },
            },
            "import_student_name": {
                "title": {
                    "name": "学生名单导入",
                    "description": "学生名单导入通知标题",
                },
                "content": {
                    "name": "已打开学生名单导入窗口",
                    "description": "学生名单导入通知内容",
                },
            },
            "name_setting": {
                "title": {"name": "姓名设置", "description": "姓名设置通知标题"},
                "content": {
                    "name": "已打开姓名设置窗口",
                    "description": "姓名设置通知内容",
                },
            },
            "gender_setting": {
                "title": {"name": "性别设置", "description": "性别设置通知标题"},
                "content": {
                    "name": "已打开性别设置窗口",
                    "description": "性别设置通知内容",
                },
            },
            "group_setting": {
                "title": {"name": "小组设置", "description": "小组设置通知标题"},
                "content": {
                    "name": "已打开小组设置窗口",
                    "description": "小组设置通知内容",
                },
            },
            "tag_setting": {
                "title": {"name": "标签设置", "description": "标签设置通知标题"},
                "content": {
                    "name": "已打开标签设置窗口",
                    "description": "标签设置通知内容",
                },
            },
            "export": {
                "title": {
                    "success": {"name": "导出成功", "description": "导出成功通知标题"},
                    "failure": {"name": "导出失败", "description": "导出失败通知标题"},
                },
                "content": {
                    "success": {
                        "name": "学生名单已导出到: {path}",
                        "description": "导出成功通知内容",
                    },
                    "failure": {
                        "name": "请先选择要导出的班级",
                        "description": "导出失败通知内容（未选择班级）",
                    },
                    "error": {"name": "{message}", "description": "导出错误通知内容"},
                },
            },
        },
        # 抽奖名单通知
        "lottery": {
            "pool_name_setting": {
                "title": {
                    "name": "奖池名称设置",
                    "description": "奖池名称设置通知标题",
                },
                "content": {
                    "name": "已打开奖池名称设置窗口",
                    "description": "奖池名称设置通知内容",
                },
            },
            "import_prize_name": {
                "title": {
                    "name": "奖品名单导入",
                    "description": "奖品名单导入通知标题",
                },
                "content": {
                    "name": "已打开奖品名单导入窗口",
                    "description": "奖品名单导入通知内容",
                },
            },
            "prize_setting": {
                "title": {"name": "奖品设置", "description": "奖品设置通知标题"},
                "content": {
                    "name": "已打开奖品设置窗口",
                    "description": "奖品设置通知内容",
                },
            },
            "prize_weight_setting": {
                "title": {
                    "name": "奖品权重设置",
                    "description": "奖品权重设置通知标题",
                },
                "content": {
                    "name": "已打开奖品权重设置窗口",
                    "description": "奖品权重设置通知内容",
                },
            },
            "tag_setting": {
                "title": {"name": "标签设置", "description": "标签设置通知标题"},
                "content": {
                    "name": "已打开标签设置窗口",
                    "description": "标签设置通知内容",
                },
            },
            "prize_count_setting": {
                "title": {
                    "name": "奖品数量设置",
                    "description": "奖品数量设置通知标题",
                },
                "content": {
                    "name": "已打开奖品数量设置窗口",
                    "description": "奖品数量设置通知内容",
                },
            },
            "export": {
                "title": {
                    "success": {"name": "导出成功", "description": "导出成功通知标题"},
                    "failure": {"name": "导出失败", "description": "导出失败通知标题"},
                },
                "content": {
                    "success": {
                        "name": "奖品名单已导出到: {path}",
                        "description": "导出成功通知内容",
                    },
                    "failure": {
                        "name": "请先选择要导出的奖池",
                        "description": "导出失败通知内容（未选择奖池）",
                    },
                    "error": {"name": "{message}", "description": "导出错误通知内容"},
                },
            },
        },
    },
    "EN_US": {
        "roll_call": {
            "class_name_setting": {
                "title": {
                    "name": "Class name settings",
                    "description": "Class name settings notification title",
                },
                "content": {
                    "name": "Class Name Settings window opened",
                    "description": "Class name setting notification content",
                },
            },
            "import_student_name": {
                "title": {
                    "name": "Student list import",
                    "description": "Student list import notification title",
                },
                "content": {
                    "name": "Open student list import window",
                    "description": "Student list import notification content",
                },
            },
            "name_setting": {
                "title": {
                    "name": "Name settings",
                    "description": "Name set notification title",
                },
                "content": {
                    "name": "Name settings window opened",
                    "description": "Name settings notification content",
                },
            },
            "gender_setting": {
                "title": {
                    "name": "Gender settings",
                    "description": "Gender settings notification title",
                },
                "content": {
                    "name": "Gender settings window opened",
                    "description": "Gender settings notification content",
                },
            },
            "group_setting": {
                "title": {
                    "name": "Group Settings",
                    "description": "Group Notification Title",
                },
                "content": {
                    "name": "Group settings window opened",
                    "description": "Group settings notification content",
                },
            },
            "tag_setting": {
                "title": {
                    "name": "Tag settings",
                    "description": "Tag settings notification title",
                },
                "content": {
                    "name": "Tag settings window opened",
                    "description": "Tag settings notification content",
                },
            },
            "export": {
                "title": {
                    "success": {
                        "name": "Export success",
                        "description": "Export successful notification title",
                    },
                    "failure": {
                        "name": "Export failed",
                        "description": "Export Failed Notification Title",
                    },
                },
                "content": {
                    "success": {
                        "name": "Student list has been exported to: {path}",
                        "description": "Export successful notification content",
                    },
                    "failure": {
                        "name": "Please select a class to export first",
                        "description": "Export failed notification content (no class selected)",
                    },
                    "error": {
                        "name": "{message}",
                        "description": "Export error notification content",
                    },
                },
            },
        },
        "lottery": {
            "pool_name_setting": {
                "title": {
                    "name": "Pool name settings",
                    "description": "Pool name settings notification title",
                },
                "content": {
                    "name": "Pool name settings window opened",
                    "description": "Pool name settings notification",
                },
            },
            "import_prize_name": {
                "title": {
                    "name": "Prizes list import",
                    "description": "Prize list import notification title",
                },
                "content": {
                    "name": "Open prize list import window",
                    "description": "Prize list import notification content",
                },
            },
            "prize_setting": {
                "title": {
                    "name": "Prizes settings",
                    "description": "Prize settings notification title",
                },
                "content": {
                    "name": "Pool settings window opened",
                    "description": "Prize settings notification content",
                },
            },
            "prize_weight_setting": {
                "title": {
                    "name": "Prize weight settings",
                    "description": "Prizes reset notification title",
                },
                "content": {
                    "name": "The prize weight setting window has been opened",
                    "description": "Prizes reset notification content",
                },
            },
            "tag_setting": {
                "title": {
                    "name": "Tag settings",
                    "description": "Tag settings notification title",
                },
                "content": {
                    "name": "Tag settings window opened",
                    "description": "Tag settings notification content",
                },
            },
            "prize_count_setting": {
                "title": {
                    "name": "Prize count settings",
                    "description": "Prize count settings notification title",
                },
                "content": {
                    "name": "The prize count setting window has been opened",
                    "description": "Prize count settings notification content",
                },
            },
            "export": {
                "title": {
                    "success": {
                        "name": "Export success",
                        "description": "Export successful notification title",
                    },
                    "failure": {
                        "name": "Export failed",
                        "description": "Export Failed Notification Title",
                    },
                },
                "content": {
                    "success": {
                        "name": "Prize list has been exported to: {path}",
                        "description": "Export successful notification content",
                    },
                    "failure": {
                        "name": "Please select prize pool to export first",
                        "description": "Export failed notification content (no pool selected)",
                    },
                    "error": {
                        "name": "{message}",
                        "description": "Export error notification content",
                    },
                },
            },
        },
    },
    "JA_JP": {
        "roll_call": {
            "class_name_setting": {
                "title": {
                    "name": "クラス名設定",
                    "description": "クラス名設定通知タイトル",
                },
                "content": {
                    "name": "クラス名設定ウィンドウを開きました",
                    "description": "クラス名設定通知内容",
                },
            },
            "import_student_name": {
                "title": {
                    "name": "学生リストインポート",
                    "description": "学生リストインポート通知タイトル",
                },
                "content": {
                    "name": "学生リストインポートウィンドウを開きました",
                    "description": "学生リストインポート通知内容",
                },
            },
            "name_setting": {
                "title": {
                    "name": "氏名設定",
                    "description": "氏名設定通知タイトル",
                },
                "content": {
                    "name": "氏名設定ウィンドウを開きました",
                    "description": "氏名設定通知内容",
                },
            },
            "gender_setting": {
                "title": {
                    "name": "性別設定",
                    "description": "性別設定通知タイトル",
                },
                "content": {
                    "name": "性別設定ウィンドウを開きました",
                    "description": "性別設定通知内容",
                },
            },
            "group_setting": {
                "title": {
                    "name": "グループ設定",
                    "description": "グループ設定通知タイトル",
                },
                "content": {
                    "name": "グループ設定ウィンドウを開きました",
                    "description": "グループ設定通知内容",
                },
            },
            "tag_setting": {
                "title": {
                    "name": "タグ設定",
                    "description": "タグ設定通知タイトル",
                },
                "content": {
                    "name": "タグ設定ウィンドウを開きました",
                    "description": "タグ設定通知内容",
                },
            },
            "export": {
                "title": {
                    "success": {
                        "name": "エクスポート成功",
                        "description": "エクスポート成功通知タイトル",
                    },
                    "failure": {
                        "name": "エクスポート失敗",
                        "description": "エクスポート失敗通知タイトル",
                    },
                },
                "content": {
                    "success": {
                        "name": "学生リストをエクスポートしました: {path}",
                        "description": "エクスポート成功通知内容",
                    },
                    "failure": {
                        "name": "エクスポートするクラスを先に選択してください",
                        "description": "エクスポート失敗通知内容（クラス未選択）",
                    },
                    "error": {
                        "name": "{message}",
                        "description": "エクスポートエラー通知内容",
                    },
                },
            },
        },
        "lottery": {
            "pool_name_setting": {
                "title": {
                    "name": "賞プール名設定",
                    "description": "賞プール名設定通知タイトル",
                },
                "content": {
                    "name": "賞プール名設定ウィンドウを開きました",
                    "description": "賞プール名設定通知内容",
                },
            },
            "import_prize_name": {
                "title": {
                    "name": "賞品リストインポート",
                    "description": "賞品リストインポート通知タイトル",
                },
                "content": {
                    "name": "賞品リストインポートウィンドウを開きました",
                    "description": "賞品リストインポート通知内容",
                },
            },
            "prize_setting": {
                "title": {
                    "name": "賞品設定",
                    "description": "賞品設定通知タイトル",
                },
                "content": {
                    "name": "賞品設定ウィンドウを開きました",
                    "description": "賞品設定通知内容",
                },
            },
            "prize_weight_setting": {
                "title": {
                    "name": "賞品重み設定",
                    "description": "賞品重み設定通知タイトル",
                },
                "content": {
                    "name": "賞品重み設定ウィンドウを開きました",
                    "description": "賞品重み設定通知内容",
                },
            },
            "tag_setting": {
                "title": {
                    "name": "タグ設定",
                    "description": "タグ設定通知タイトル",
                },
                "content": {
                    "name": "タグ設定ウィンドウを開きました",
                    "description": "タグ設定通知内容",
                },
            },
            "prize_count_setting": {
                "title": {
                    "name": "賞品数量設定",
                    "description": "賞品数量設定通知タイトル",
                },
                "content": {
                    "name": "賞品数量設定ウィンドウを開きました",
                    "description": "賞品数量設定通知内容",
                },
            },
            "export": {
                "title": {
                    "success": {
                        "name": "エクスポート成功",
                        "description": "エクスポート成功通知タイトル",
                    },
                    "failure": {
                        "name": "エクスポート失敗",
                        "description": "エクスポート失敗通知タイトル",
                    },
                },
                "content": {
                    "success": {
                        "name": "賞品リストをエクスポートしました: {path}",
                        "description": "エクスポート成功通知内容",
                    },
                    "failure": {
                        "name": "エクスポートする賞プールを先に選択してください",
                        "description": "エクスポート失敗通知内容（賞プール未選択）",
                    },
                    "error": {
                        "name": "{message}",
                        "description": "エクスポートエラー通知内容",
                    },
                },
            },
        },
    },
}

# QFileDialog 文本配置
qfiledialog = {
    "ZH_CN": {
        "roll_call": {
            "export_student_list": {
                "caption": {
                    "name": "保存学生名单",
                    "description": "保存学生名单对话框标题",
                },
                "filter": {
                    "name": "Excel 文件 (*.xlsx);;CSV 文件 (*.csv);;TXT 文件（仅姓名） (*.txt)",
                    "description": "保存学生名单对话框过滤器",
                },
            },
            "export_history": {
                "caption": {
                    "name": "导出点名记录",
                    "description": "导出点名记录对话框标题",
                },
                "filter": {
                    "name": "Excel 文件 (*.xlsx);;CSV 文件 (*.csv);;TXT 文件 (*.txt)",
                    "description": "导出点名记录对话框过滤器",
                },
            },
        },
        "lottery": {
            "export_prize_name": {
                "caption": {
                    "name": "保存奖品名单",
                    "description": "保存奖品名单对话框标题",
                },
                "filter": {
                    "name": "Excel 文件 (*.xlsx);;CSV 文件 (*.csv);;TXT 文件（仅奖品名） (*.txt)",
                    "description": "保存奖品名单对话框过滤器",
                },
            },
            "export_history": {
                "caption": {
                    "name": "导出抽奖记录",
                    "description": "导出抽奖记录对话框标题",
                },
                "filter": {
                    "name": "Excel 文件 (*.xlsx);;CSV 文件 (*.csv);;TXT 文件 (*.txt)",
                    "description": "导出抽奖记录对话框过滤器",
                },
            },
        },
    },
    "EN_US": {
        "roll_call": {
            "export_student_list": {
                "caption": {
                    "name": "Save student list",
                    "description": "Save student list dialog title",
                },
                "filter": {
                    "name": "Excel files (*.xlsx);;CSV files (*.csv);;TXT files (name only) (*.txt)",
                    "description": "Save student list dialog filter",
                },
            },
            "export_history": {
                "caption": {
                    "name": "Export picking records",
                    "description": "Export picking records dialog title",
                },
                "filter": {
                    "name": "Excel files (*.xlsx);;CSV files (*.csv);;TXT files (*.txt)",
                    "description": "Export picking records dialog filter",
                },
            },
        },
        "lottery": {
            "export_prize_name": {
                "caption": {
                    "name": "Save prize list",
                    "description": "Save prize list dialog title",
                },
                "filter": {
                    "name": "Excel files (*.xlsx);;CSV files (*.csv);;TXT files (only prizes) (*.txt)",
                    "description": "Save prize list dialog filter",
                },
            },
            "export_history": {
                "caption": {
                    "name": "Export lottery records",
                    "description": "Export lottery records dialog title",
                },
                "filter": {
                    "name": "Excel files (*.xlsx);;CSV files (*.csv);;TXT files (*.txt)",
                    "description": "Export lottery records dialog filter",
                },
            },
        },
    },
    "JA_JP": {
        "roll_call": {
            "export_student_list": {
                "caption": {
                    "name": "学生リストを保存",
                    "description": "学生リスト保存ダイアログタイトル",
                },
                "filter": {
                    "name": "Excelファイル (*.xlsx);;CSVファイル (*.csv);;TXTファイル（氏名のみ） (*.txt)",
                    "description": "学生リスト保存ダイアログフィルター",
                },
            },
            "export_history": {
                "caption": {
                    "name": "点呼記録をエクスポート",
                    "description": "点呼記録エクスポートダイアログタイトル",
                },
                "filter": {
                    "name": "Excelファイル (*.xlsx);;CSVファイル (*.csv);;TXTファイル (*.txt)",
                    "description": "点呼記録エクスポートダイアログフィルター",
                },
            },
        },
        "lottery": {
            "export_prize_name": {
                "caption": {
                    "name": "賞品リストを保存",
                    "description": "賞品リスト保存ダイアログタイトル",
                },
                "filter": {
                    "name": "Excelファイル (*.xlsx);;CSVファイル (*.csv);;TXTファイル（賞品名のみ） (*.txt)",
                    "description": "賞品リスト保存ダイアログフィルター",
                },
            },
            "export_history": {
                "caption": {
                    "name": "抽選記録をエクスポート",
                    "description": "抽選記録エクスポートダイアログタイトル",
                },
                "filter": {
                    "name": "Excelファイル (*.xlsx);;CSVファイル (*.csv);;TXTファイル (*.txt)",
                    "description": "抽選記録エクスポートダイアログフィルター",
                },
            },
        },
    },
}
