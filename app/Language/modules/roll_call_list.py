# 班级名称设置窗口
set_class_name = {
    "ZH_CN": {
        "title": {"name": "班级名称设置", "description": "设置班级名称窗口标题"},
        "description": {
            "name": "在此窗口中，您可以设置班级名称信息\n每行输入将对应一个班级的名称，存储在班级名单文件中\n\n请每行输入一个班级名称，例如：\n高一1班\n高一2班\n高一3班",
            "description": "班级名称设置窗口描述",
        },
        "input_title": {"name": "班级名称列表", "description": "班级名称输入区域标题"},
        "input_placeholder": {
            "name": "请输入班级名称，每行一个班级名称",
            "description": "班级名称输入框占位符",
        },
        "save_button": {"name": "保存", "description": "保存按钮文本"},
        "cancel_button": {"name": "取消", "description": "取消按钮文本"},
        "error_title": {"name": "错误", "description": "错误消息标题"},
        "success_title": {"name": "成功", "description": "成功消息标题"},
        "info_title": {"name": "提示", "description": "信息消息标题"},
        "no_class_names_error": {
            "name": "请至少输入一个班级名称",
            "description": "未输入班级名称时的错误提示",
        },
        "invalid_names_error": {
            "name": "以下班级名称包含非法字符或是保留字: {names}",
            "description": "班级名称验证失败时的错误提示",
        },
        "save_error": {
            "name": "保存班级名称失败",
            "description": "保存班级名称时的错误提示",
        },
        "success_message": {
            "name": "成功创建 {count} 个新班级",
            "description": "成功创建班级时的提示消息",
        },
        "no_new_classes_message": {
            "name": "所有班级名称都已存在，没有创建新班级",
            "description": "没有创建新班级时的提示消息",
        },
        "unsaved_changes_title": {
            "name": "未保存的更改",
            "description": "未保存更改对话框标题",
        },
        "unsaved_changes_message": {
            "name": "您有未保存的更改，确定要关闭窗口吗？",
            "description": "未保存更改对话框内容",
        },
        "discard_button": {"name": "放弃更改", "description": "放弃更改按钮文本"},
        "continue_editing_button": {
            "name": "继续编辑",
            "description": "继续编辑按钮文本",
        },
        "delete_class_title": {"name": "删除班级", "description": "删除班级对话框标题"},
        "delete_class_message": {
            "name": "确定要删除班级 '{class_name}' 吗？此操作将删除该班级的所有学生数据，且无法恢复",
            "description": "删除班级确认对话框内容",
        },
        "delete_class_button": {"name": "删除班级", "description": "删除班级按钮文本"},
        "delete_multiple_classes_title": {
            "name": "删除多个班级",
            "description": "删除多个班级对话框标题",
        },
        "delete_multiple_classes_message": {
            "name": "确定要删除以下 {count} 个班级吗？此操作将删除这些班级的所有学生数据，且无法恢复\n\n{class_names}",
            "description": "删除多个班级确认对话框内容",
        },
        "delete_success_title": {"name": "删除成功", "description": "删除成功通知标题"},
        "delete_success_message": {
            "name": "成功删除 {count} 个班级",
            "description": "删除成功通知内容",
        },
        "delete_cancel_button": {"name": "取消删除", "description": "取消删除按钮文本"},
        "no_deletable_classes": {
            "name": "没有可删除的班级",
            "description": "没有可删除班级时的提示",
        },
        "select_class_to_delete": {
            "name": "请选择要删除的班级",
            "description": "选择删除班级的提示",
        },
        "select_class_dialog_title": {
            "name": "选择要删除的班级",
            "description": "选择删除班级对话框标题",
        },
        "select_class_dialog_message": {
            "name": "请选择要删除的班级：",
            "description": "选择删除班级对话框内容",
        },
        "delete_selected_button": {
            "name": "删除选中",
            "description": "删除选中按钮文本",
        },
        "delete_class_error": {
            "name": "删除班级失败: {error}",
            "description": "删除班级失败错误信息",
        },
        "class_disappeared_title": {
            "name": "班级消失提示",
            "description": "班级消失提示标题",
        },
        "class_disappeared_message": {
            "name": "检测到班级 '{class_name}' 已从输入框中消失，请保存更改以永久删除",
            "description": "单个班级消失提示内容",
        },
        "multiple_classes_disappeared_message": {
            "name": "检测到以下 {count} 个班级已从输入框中消失，请保存更改以永久删除：\n{class_names}",
            "description": "多个班级消失提示内容",
        },
    },
}

# 导入学生姓名语言配置
import_student_name = {
    "ZH_CN": {
        "title": {
            "name": "导入学生姓名",
            "description": "从Excel或CSV文件导入学生姓名",
        },
        "initial_subtitle": {
            "name": "正在导入到：",
            "description": "正在导入到班级的提示",
        },
        "file_selection_title": {"name": "文件选择", "description": "文件选择区域标题"},
        "no_file_selected": {
            "name": "未选择文件",
            "description": "未选择文件时的提示文本",
        },
        "select_file": {"name": "选择文件", "description": "选择文件按钮文本"},
        "supported_formats": {
            "name": "支持的格式: Excel (.xlsx, .xls) 和 CSV (.csv)",
            "description": "支持的文件格式说明",
        },
        "file_filter": {
            "name": "Excel 文件 (*.xlsx *.xls);;CSV 文件 (*.csv)",
            "description": "文件选择对话框的文件过滤器",
        },
        "dialog_title": {"name": "选择文件", "description": "文件选择对话框标题"},
        "column_mapping_title": {"name": "列映射", "description": "列映射区域标题"},
        "column_mapping_description": {
            "name": "请选择包含学生信息的列",
            "description": "列映射区域说明",
        },
        "column_mapping_id_column": {
            "name": "学号列 (必选):",
            "description": "学号列标签",
        },
        "column_mapping_name_column": {
            "name": "姓名列 (必选):",
            "description": "姓名列标签",
        },
        "column_mapping_gender_column": {
            "name": "性别列 (可选):",
            "description": "性别列标签",
        },
        "column_mapping_group_column": {
            "name": "小组列 (可选):",
            "description": "小组列标签",
        },
        "column_mapping_none": {"name": "无", "description": "无选项文本"},
        "data_preview_title": {"name": "数据预览", "description": "数据预览区域标题"},
        "student_id": {"name": "学号", "description": "学号列标题"},
        "name": {"name": "姓名", "description": "姓名列标题"},
        "gender": {"name": "性别", "description": "性别列标题"},
        "group": {"name": "小组", "description": "小组列标题"},
        "buttons_import": {"name": "导入", "description": "导入按钮文本"},
        "file_loaded_title": {
            "name": "文件已加载",
            "description": "文件加载成功对话框标题",
        },
        "file_loaded_content": {
            "name": "文件加载成功",
            "description": "文件加载成功对话框内容",
        },
        "file_loaded_notification_title": {
            "name": "文件加载成功",
            "description": "文件加载成功通知标题",
        },
        "file_loaded_notification_content": {
            "name": "文件已成功加载，请检查数据预览",
            "description": "文件加载成功通知内容",
        },
        "error_title": {"name": "错误", "description": "错误对话框标题"},
        "load_failed": {"name": "加载文件失败", "description": "加载文件失败错误信息"},
        "load_failed_notification_title": {
            "name": "加载文件失败",
            "description": "加载文件失败通知标题",
        },
        "load_failed_notification_content": {
            "name": "无法加载文件，请检查文件格式和内容",
            "description": "加载文件失败通知内容",
        },
        "import_failed": {
            "name": "导入数据失败",
            "description": "导入数据失败错误信息",
        },
        "import_failed_notification_title": {
            "name": "导入数据失败",
            "description": "导入数据失败通知标题",
        },
        "import_failed_notification_content": {
            "name": "导入数据时发生错误，请检查数据格式和内容",
            "description": "导入数据失败通知内容",
        },
        "unsupported_format": {
            "name": "不支持的文件格式",
            "description": "不支持的文件格式错误信息",
        },
        "no_name_column": {
            "name": "请选择姓名列",
            "description": "未选择姓名列错误信息",
        },
        "no_id_column": {"name": "请选择学号列", "description": "未选择学号列错误信息"},
        "import_success_title": {
            "name": "导入成功",
            "description": "导入成功对话框标题",
        },
        "import_success_content_template": {
            "name": "成功导入 {count} 个学生信息到班级 '{class_name}'",
            "description": "导入成功对话框内容模板",
        },
        "import_success_notification_title": {
            "name": "导入成功",
            "description": "导入成功通知标题",
        },
        "import_success_notification_content_template": {
            "name": "成功导入 {count} 个学生信息到班级 '{class_name}'",
            "description": "导入成功通知内容模板",
        },
        "existing_data_title": {
            "name": "班级已有数据",
            "description": "班级已有数据对话框标题",
        },
        "existing_data_prompt": {
            "name": "班级 '{class_name}' 已包含 {count} 名学生，请选择处理方式:",
            "description": "班级已有数据对话框提示文本",
        },
        "existing_data_option_overwrite": {
            "name": "覆盖现有数据",
            "description": "覆盖现有数据选项",
        },
        "existing_data_option_cancel": {
            "name": "取消导入",
            "description": "取消导入选项",
        },
    }
}

# 姓名设置窗口
name_setting = {
    "ZH_CN": {
        "title": {"name": "姓名设置", "description": "设置姓名窗口标题"},
        "description": {
            "name": "在此窗口中，您可以设置姓名信息\n每行输入将对应一个学生的姓名，存储在班级名单文件中\n\n请每行输入一个姓名，例如：\n张三\n李四\n王五",
            "description": "姓名设置窗口描述",
        },
        "input_title": {"name": "姓名列表", "description": "姓名输入区域标题"},
        "input_placeholder": {
            "name": "请输入姓名，每行一个姓名",
            "description": "姓名输入框占位符",
        },
        "save_button": {"name": "保存", "description": "保存按钮文本"},
        "cancel_button": {"name": "取消", "description": "取消按钮文本"},
        "error_title": {"name": "错误", "description": "错误消息标题"},
        "success_title": {"name": "成功", "description": "成功消息标题"},
        "info_title": {"name": "提示", "description": "信息消息标题"},
        "no_names_error": {
            "name": "请至少输入一个姓名",
            "description": "未输入姓名时的错误提示",
        },
        "invalid_names_error": {
            "name": "以下姓名包含非法字符或是保留字: {names}",
            "description": "姓名验证失败时的错误提示",
        },
        "save_error": {"name": "保存姓名失败", "description": "保存姓名时的错误提示"},
        "success_message": {
            "name": "成功创建 {count} 个新姓名",
            "description": "成功创建姓名时的提示消息",
        },
        "no_new_names_message": {
            "name": "所有姓名都已存在，没有创建新姓名",
            "description": "没有创建新姓名时的提示消息",
        },
        "unsaved_changes_title": {
            "name": "未保存的更改",
            "description": "未保存更改对话框标题",
        },
        "unsaved_changes_message": {
            "name": "您有未保存的更改，确定要关闭窗口吗？",
            "description": "未保存更改对话框内容",
        },
        "discard_button": {"name": "放弃更改", "description": "放弃更改按钮文本"},
        "continue_editing_button": {
            "name": "继续编辑",
            "description": "继续编辑按钮文本",
        },
        "delete_button": {"name": "删除", "description": "删除按钮文本"},
        "delete_name_title": {"name": "删除姓名", "description": "删除姓名对话框标题"},
        "delete_name_message": {
            "name": "确定要删除姓名 '{name}' 吗？此操作将删除该姓名的所有信息，且无法恢复",
            "description": "删除姓名确认对话框内容",
        },
        "delete_multiple_names_title": {
            "name": "删除多个姓名",
            "description": "删除多个姓名对话框标题",
        },
        "delete_multiple_names_message": {
            "name": "确定要删除以下 {count} 个姓名吗？此操作将删除这些姓名的所有信息，且无法恢复\n\n{names}",
            "description": "删除多个姓名确认对话框内容",
        },
        "delete_name_success_title": {
            "name": "删除成功",
            "description": "删除姓名成功通知标题",
        },
        "delete_name_success_message": {
            "name": "成功删除 {count} 个姓名",
            "description": "删除姓名成功通知内容",
        },
        "delete_name_cancel_button": {
            "name": "取消删除",
            "description": "取消删除姓名按钮文本",
        },
        "no_deletable_names": {
            "name": "没有可删除的姓名",
            "description": "没有可删除姓名时的提示",
        },
        "select_name_to_delete": {
            "name": "请选择要删除的姓名",
            "description": "选择删除姓名的提示",
        },
        "select_name_dialog_title": {
            "name": "选择要删除的姓名",
            "description": "选择删除姓名对话框标题",
        },
        "select_name_dialog_message": {
            "name": "请选择要删除的姓名：",
            "description": "选择删除姓名对话框内容",
        },
        "delete_selected_names_button": {
            "name": "删除选中",
            "description": "删除选中姓名按钮文本",
        },
        "delete_name_error": {
            "name": "删除姓名失败: {error}",
            "description": "删除姓名失败错误信息",
        },
        "name_deleted_title": {"name": "姓名已删除", "description": "删除姓名提示标题"},
        "name_deleted_message": {
            "name": "姓名 '{name}' 已从输入框中删除，请保存更改以永久删除",
            "description": "删除姓名提示内容",
        },
    },
}

# 性别设置窗口
gender_setting = {
    "ZH_CN": {
        "title": {"name": "性别设置", "description": "设置性别窗口标题"},
        "description": {
            "name": "在此窗口中，您可以设置学生性别信息\n每行输入将对应一个学生的性别，存储在班级名单文件中\n\n请每行输入一个性别，例如：\n男\n女\n其他",
            "description": "性别设置窗口描述",
        },
        "input_title": {"name": "性别列表", "description": "性别输入区域标题"},
        "input_placeholder": {
            "name": "请输入性别，每行一个性别",
            "description": "性别输入框占位符",
        },
        "save_button": {"name": "保存", "description": "保存按钮文本"},
        "cancel_button": {"name": "取消", "description": "取消按钮文本"},
        "error_title": {"name": "错误", "description": "错误消息标题"},
        "success_title": {"name": "成功", "description": "成功消息标题"},
        "info_title": {"name": "提示", "description": "信息消息标题"},
        "no_genders_error": {
            "name": "请至少输入一个性别",
            "description": "未输入性别时的错误提示",
        },
        "invalid_genders_error": {
            "name": "以下性别包含非法字符或是保留字: {genders}",
            "description": "性别验证失败时的错误提示",
        },
        "save_error": {
            "name": "保存性别选项失败",
            "description": "保存性别选项时的错误提示",
        },
        "success_message": {
            "name": "成功创建 {count} 个新性别选项",
            "description": "成功创建性别选项时的提示消息",
        },
        "no_new_genders_message": {
            "name": "所有性别选项都已存在，没有创建新性别选项",
            "description": "没有创建新性别选项时的提示消息",
        },
        "unsaved_changes_title": {
            "name": "未保存的更改",
            "description": "未保存更改对话框标题",
        },
        "unsaved_changes_message": {
            "name": "您有未保存的更改，确定要关闭窗口吗？",
            "description": "未保存更改对话框内容",
        },
        "discard_button": {"name": "放弃更改", "description": "放弃更改按钮文本"},
        "continue_editing_button": {
            "name": "继续编辑",
            "description": "继续编辑按钮文本",
        },
        "delete_button": {"name": "删除", "description": "删除按钮文本"},
        "delete_gender_title": {
            "name": "删除性别选项",
            "description": "删除性别选项对话框标题",
        },
        "delete_gender_message": {
            "name": "确定要删除性别选项 '{gender}' 吗？此操作将删除该性别选项的所有信息，且无法恢复",
            "description": "删除性别选项确认对话框内容",
        },
        "delete_multiple_genders_title": {
            "name": "删除多个性别选项",
            "description": "删除多个性别选项对话框标题",
        },
        "delete_multiple_genders_message": {
            "name": "确定要删除以下 {count} 个性别选项吗？此操作将删除这些性别选项的所有信息，且无法恢复\n\n{genders}",
            "description": "删除多个性别选项确认对话框内容",
        },
        "delete_gender_success_title": {
            "name": "删除成功",
            "description": "删除性别选项成功通知标题",
        },
        "delete_gender_success_message": {
            "name": "成功删除 {count} 个性别选项",
            "description": "删除性别选项成功通知内容",
        },
        "delete_gender_cancel_button": {
            "name": "取消删除",
            "description": "取消删除性别选项按钮文本",
        },
        "no_deletable_genders": {
            "name": "没有可删除的性别选项",
            "description": "没有可删除性别选项时的提示",
        },
        "select_gender_to_delete": {
            "name": "请选择要删除的性别选项",
            "description": "选择删除性别选项的提示",
        },
        "select_gender_dialog_title": {
            "name": "选择要删除的性别选项",
            "description": "选择删除性别选项对话框标题",
        },
        "select_gender_dialog_message": {
            "name": "请选择要删除的性别选项：",
            "description": "选择删除性别选项对话框内容",
        },
        "delete_selected_genders_button": {
            "name": "删除选中",
            "description": "删除选中性别选项按钮文本",
        },
        "delete_gender_error": {
            "name": "删除性别选项失败: {error}",
            "description": "删除性别选项失败错误信息",
        },
        "gender_deleted_title": {
            "name": "性别选项已删除",
            "description": "删除性别选项提示标题",
        },
        "gender_deleted_message": {
            "name": "性别选项 '{gender}' 已从输入框中删除，请保存更改以永久删除",
            "description": "删除性别选项提示内容",
        },
    },
}

# 小组设置窗口
group_setting = {
    "ZH_CN": {
        "title": {"name": "小组设置", "description": "设置小组窗口标题"},
        "description": {
            "name": "在此窗口中，您可以设置学生小组信息\n每行输入将对应一个学生的小组，存储在班级名单文件中\n\n请每行输入一个小组，例如：\nA组\nB组\nC组",
            "description": "小组设置窗口描述",
        },
        "input_title": {"name": "小组列表", "description": "小组输入区域标题"},
        "input_placeholder": {
            "name": "请输入小组，每行一个小组",
            "description": "小组输入框占位符",
        },
        "save_button": {"name": "保存", "description": "保存按钮文本"},
        "cancel_button": {"name": "取消", "description": "取消按钮文本"},
        "error_title": {"name": "错误", "description": "错误消息标题"},
        "success_title": {"name": "成功", "description": "成功消息标题"},
        "info_title": {"name": "提示", "description": "信息消息标题"},
        "no_groups_error": {
            "name": "请至少输入一个小组",
            "description": "未输入小组时的错误提示",
        },
        "invalid_groups_error": {
            "name": "以下小组包含非法字符或是保留字: {groups}",
            "description": "小组验证失败时的错误提示",
        },
        "save_error": {
            "name": "保存小组选项失败",
            "description": "保存小组选项时的错误提示",
        },
        "success_message": {
            "name": "成功创建 {count} 个新小组选项",
            "description": "成功创建小组选项时的提示消息",
        },
        "no_new_groups_message": {
            "name": "所有小组选项都已存在，没有创建新小组选项",
            "description": "没有创建新小组选项时的提示消息",
        },
        "unsaved_changes_title": {
            "name": "未保存的更改",
            "description": "未保存更改对话框标题",
        },
        "unsaved_changes_message": {
            "name": "您有未保存的更改，确定要关闭窗口吗？",
            "description": "未保存更改对话框内容",
        },
        "discard_button": {"name": "放弃更改", "description": "放弃更改按钮文本"},
        "continue_editing_button": {
            "name": "继续编辑",
            "description": "继续编辑按钮文本",
        },
        "delete_button": {"name": "删除", "description": "删除按钮文本"},
        "delete_group_title": {
            "name": "删除小组选项",
            "description": "删除小组选项对话框标题",
        },
        "delete_group_message": {
            "name": "确定要删除小组选项 '{group}' 吗？此操作将删除该小组选项的所有信息，且无法恢复",
            "description": "删除小组选项确认对话框内容",
        },
        "delete_multiple_groups_title": {
            "name": "删除多个小组选项",
            "description": "删除多个小组选项对话框标题",
        },
        "delete_multiple_groups_message": {
            "name": "确定要删除以下 {count} 个小组选项吗？此操作将删除这些小组选项的所有信息，且无法恢复\n\n{groups}",
            "description": "删除多个小组选项确认对话框内容",
        },
        "delete_group_success_title": {
            "name": "删除成功",
            "description": "删除小组选项成功通知标题",
        },
        "delete_group_success_message": {
            "name": "成功删除 {count} 个小组选项",
            "description": "删除小组选项成功通知内容",
        },
        "delete_group_cancel_button": {
            "name": "取消删除",
            "description": "取消删除小组选项按钮文本",
        },
        "no_deletable_groups": {
            "name": "没有可删除的小组选项",
            "description": "没有可删除小组选项时的提示",
        },
        "select_group_to_delete": {
            "name": "请选择要删除的小组选项",
            "description": "选择删除小组选项的提示",
        },
        "select_group_dialog_title": {
            "name": "选择要删除的小组选项",
            "description": "选择删除小组选项对话框标题",
        },
        "select_group_dialog_message": {
            "name": "请选择要删除的小组选项：",
            "description": "选择删除小组选项对话框内容",
        },
        "delete_selected_groups_button": {
            "name": "删除选中",
            "description": "删除选中小组选项按钮文本",
        },
        "delete_group_error": {
            "name": "删除小组选项失败: {error}",
            "description": "删除小组选项失败错误信息",
        },
        "group_deleted_title": {
            "name": "小组选项已删除",
            "description": "删除小组选项提示标题",
        },
        "group_deleted_message": {
            "name": "小组选项 '{group}' 已从输入框中删除，请保存更改以永久删除",
            "description": "删除小组选项提示内容",
        },
    },
}
