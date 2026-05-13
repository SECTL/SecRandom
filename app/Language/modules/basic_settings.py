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
        "telemetry_category": {
            "name": "遥测与隐私",
            "description": "管理在线状态上报和隐私相关设置",
        },
        "simplified_mode": {
            "name": "精简设置模式",
            "description": "隐藏高级设置项，仅显示推荐设置",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "simplified_mode_notification": {
            "enable": "已开启精简设置模式",
            "disable": "已关闭精简设置模式",
        },
        "autostart": {
            "name": "开机自启",
            "description": "设置软件是否随系统启动自动运行",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "show_startup_window": {
            "name": "启动显示主窗口",
            "description": "设置软件启动时是否自动显示主窗口",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "main_window_topmost_mode": {
            "name": "主窗口置顶",
            "description": "选择主窗口置顶方式（UIA置顶需以管理员运行）",
            "combo_items": ["关闭置顶", "置顶", "UIA置顶"],
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
        "background_resident": {
            "name": "后台驻留",
            "description": "关闭所有窗口后是否仍在后台常驻",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "auto_save_window_size": {
            "name": "自动保存窗口大小",
            "description": "是否自动保存窗口大小",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "url_protocol": {
            "name": "URL协议注册&IPC服务",
            "description": "注册自定义URL协议(secrandom://)，并启用IPC通信，支持链接启动与外部联动",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "autostart_notification": {
            "enable": "已开启开机自启",
            "disable": "已关闭开机自启",
            "failure": "设置开机自启失败",
        },
        "background_resident_notification": {
            "enable": "已开启后台驻留",
            "disable": "已关闭后台驻留",
        },
        "auto_save_window_size_notification": {
            "enable": "已开启自动保存窗口大小",
            "disable": "已关闭自动保存窗口大小",
        },
        "url_protocol_notification": {
            "enable": "已开启URL协议注册与IPC服务",
            "disable": "已关闭URL协议注册与IPC服务",
            "register_failure": "URL协议&IPC服务注册失败",
            "unregister_failure": "URL协议&IPC服务注销失败",
            "error": "URL协议&IPC服务设置错误: {error}",
        },
        "export_diagnostic_data": {
            "name": "导出诊断数据",
            "description": "退出软件时导出诊断信息，用于排查问题",
            "pushbutton_name": "导出诊断数据",
        },
        "export_settings": {
            "name": "导出设置",
            "description": "将当前设置导出为配置文件，用于备份和迁移",
            "pushbutton_name": "导出设置",
        },
        "import_settings": {
            "name": "导入设置",
            "description": "从配置文件导入设置，覆盖当前配置信息",
            "pushbutton_name": "导入设置",
        },
        "export_all_data": {
            "name": "导出所有数据",
            "description": "退出软件时导出全部数据和设置",
            "pushbutton_name": "导出所有数据",
        },
        "import_all_data": {
            "name": "导入所有数据",
            "description": "启动软件时从备份文件恢复全部数据",
            "pushbutton_name": "导入所有数据",
        },
        "log_viewer": {
            "name": "查看日志",
            "description": "查看和管理程序日志文件",
            "pushbutton_name": "查看日志",
        },
        "backup_manager": {
            "name": "备份管理",
            "description": "管理自动备份与手动备份",
            "pushbutton_name": "备份管理",
        },
        "backup_auto_settings": {"name": "自动备份", "description": "配置自动备份策略"},
        "backup_manual_settings": {
            "name": "手动备份",
            "description": "立即备份并管理备份文件",
        },
        "backup_auto_enabled": {
            "name": "是否启用",
            "description": "是否启用自动备份",
            "switchbutton_name": {"enable": "开启", "disable": "关闭"},
        },
        "backup_auto_interval_days": {
            "name": "自动备份间隔",
            "description": "设置自动备份间隔（天）",
        },
        "backup_auto_max_count": {
            "name": "自动备份上限",
            "description": "设置自动备份最多保留数量（个），0为不限制",
        },
        "backup_last_success": {
            "name": "上次成功备份",
            "description": "显示上次成功备份时间",
        },
        "backup_now": {
            "name": "立即备份",
            "description": "立即创建一份全量备份",
            "pushbutton_name": "立即备份",
        },
        "backup_open_folder": {
            "name": "查看备份文件",
            "description": "打开备份文件夹",
            "pushbutton_name": "查看备份文件",
        },
        "backup_folder_size": {"name": "占用大小", "description": "备份文件夹占用大小"},
        "backup_content_settings": {
            "name": "备份内容",
            "description": "选择需要包含在备份文件中的数据",
        },
        "backup_content_tip": {
            "text": "关闭某一项后，该文件夹不会被打包进备份文件。",
        },
        "backup_restore_settings": {
            "name": "还原备份",
            "description": "选择一个备份文件并还原（重启后生效）",
        },
        "backup_restore_tip": {
            "text": "还原会覆盖当前数据。还原完成后请重启应用程序以使更改生效。",
        },
        "backup_restore_file_list": {
            "name": "备份文件列表",
            "description": "选择要还原的备份文件",
        },
        "backup_restore_refresh": {
            "name": "刷新列表",
            "description": "刷新备份文件列表",
            "pushbutton_name": "刷新",
        },
        "backup_restore_start": {
            "name": "开始还原",
            "description": "使用选中的备份文件还原数据",
            "pushbutton_name": "还原",
        },
        "backup_restore_delete": {
            "name": "删除备份",
            "description": "删除选中的备份文件",
            "pushbutton_name": "删除",
        },
        "backup_restore_no_selection": {
            "text": "请先选择要还原的备份文件",
        },
        "backup_restore_confirm": {
            "title": "恢复备份",
            "content": "确定要从备份文件「{file}」恢复数据吗？\n\n此操作将覆盖当前所有设置和数据，且无法撤销。",
            "confirm_button": "恢复",
            "cancel_button": "取消",
        },
        "backup_restore_delete_confirm": {
            "title": "确认删除",
            "content": "确定要删除该备份文件吗？\n\n{file}\n\n此操作不可恢复。",
            "pushbutton_name": "删除",
        },
        "backup_restore_delete_cancel": {
            "name": "取消",
            "description": "取消删除操作",
            "pushbutton_name": "取消",
        },
        "backup_restore_refresh_result": {
            "success": "已刷新，共 {count} 个备份文件",
            "empty": "已刷新，未发现备份文件",
            "failure": "刷新失败：{error}",
        },
        "backup_restore_delete_result": {
            "success": "已删除：{file}",
            "failure": "删除失败：{error}",
        },
        "backup_restore_table_headers": ["备份文件", "创建时间", "大小", "操作"],
        "include_config": {
            "name": "配置文件",
            "description": "软件设置与配置文件（config）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_list": {
            "name": "名单数据",
            "description": "点名/抽奖名单等数据（list）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_language": {
            "name": "语言文件",
            "description": "多语言文本配置（Language）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_history": {
            "name": "历史记录",
            "description": "抽取历史记录数据（history）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_audio": {
            "name": "音频资源",
            "description": "音频资源文件（audio）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_cses": {
            "name": "CSES 联动",
            "description": "CSES 相关联动数据（CSES）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_images": {
            "name": "图片资源",
            "description": "学生/奖品图片资源（images）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_themes": {
            "name": "主题资源",
            "description": "已安装的主题资源（theme）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_logs": {
            "name": "运行日志",
            "description": "程序运行日志，可能包含敏感信息（logs）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "backup_tabs": {
            "auto": "自动备份",
            "manual": "手动备份",
            "restore": "还原备份",
            "content": "备份内容",
        },
        "backup_last_success_text": {"none": "没有上次备份记录"},
        "backup_now_result": {
            "success": "备份成功:\n{path}",
            "failure": "备份失败:\n{error}",
        },
        "dpiScale": {
            "name": "DPI缩放",
            "description": "调整软件界面缩放比例（重启软件后生效）",
            "combo_items": ["100%", "125%", "150%", "175%", "200%", "自动"],
        },
        "font": {
            "name": "字体",
            "description": "设置软件界面显示字体（重启软件后生效）",
        },
        "font_weight": {
            "name": "字体粗细",
            "description": "设置软件界面字体粗细（重启软件后生效）",
            "combo_items": [
                "极细",
                "特细",
                "细体",
                "常规",
                "中等",
                "半粗",
                "粗体",
                "特粗",
                "极粗",
            ],
        },
        "theme": {
            "name": "主题模式",
            "description": "选择软件界面主题样式",
            "combo_items": ["浅色", "深色", "跟随系统"],
        },
        "theme_color": {"name": "主题颜色", "description": "设置软件界面主题色彩"},
        "open_theme_management": {
            "name": "主题管理",
            "description": "打开主题管理与背景设置",
            "pushbutton_name": "打开",
        },
        "language": {
            "name": "显示语言",
            "description": "切换软件界面语言（重启软件后生效）",
        },
        "settings_import_export": {
            "export_success_title": {"name": "导出设置"},
            "export_success_content": {"name": "设置已成功导出到:\n{path}"},
            "export_failure_title": {"name": "导出设置"},
            "export_failure_content": {"name": "导出设置失败:\n{error}"},
            "import_confirm_title": {"name": "导入设置"},
            "import_confirm_content": {
                "name": "确定要导入这些设置吗？这将覆盖当前设置"
            },
            "import_confirm_button": {"name": "确认导入"},
            "import_cancel_button": {"name": "取消导入"},
            "import_success_title": {"name": "导入设置"},
            "import_success_content": {
                "name": "设置已成功导入\n重启应用程序以使更改生效"
            },
            "import_success_button": {"name": "我知道了"},
            "export_success_button": {"name": "我知道了"},
            "import_failure_title": {"name": "导入设置"},
            "import_failure_content": {"name": "导入设置失败:\n{error}"},
        },
        "telemetry": {
            "name": "遥测",
            "description": "开启后上报在线状态数据以帮助改进软件",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "telemetry_notification": {
            "enable": "已开启遥测",
            "disable": "已关闭遥测，重启后生效",
        },
        "telemetry_mode": {
            "name": "在线状态",
            "description": "完整上报含位置信息，匿名上报仅计入在线人数不报位置，停止则不上报",
            "combo_items": ["完整上报", "匿名上报", "停止上报"],
        },
        "success_enable_content": {"name": "已开启启动显示主窗口"},
        "info_disable_content": {"name": "已关闭启动显示主窗口"},
        "data_import_export": {
            "export_success_title": {"name": "导出所有数据"},
            "export_success_content": {"name": "所有数据已成功导出到:\n{path}"},
            "export_failure_title": {"name": "导出所有数据"},
            "export_failure_content": {"name": "导出所有数据失败:\n{error}"},
            "import_confirm_title": {"name": "导入所有数据"},
            "import_confirm_content": {
                "name": "确定要导入这些数据吗？这将覆盖当前数据"
            },
            "import_confirm_button": {"name": "确认导入"},
            "import_cancel_button": {"name": "取消导入"},
            "import_success_title": {"name": "导入所有数据"},
            "import_success_content": {
                "name": "数据已成功导入\n重启应用程序以使更改生效"
            },
            "import_success_content_skipped": {
                "name": "数据已成功导入（跳过 {count} 个占用文件）\n重启应用程序以使更改生效"
            },
            "import_success_button": {"name": "我知道了"},
            "import_failure_title": {"name": "导入所有数据"},
            "import_failure_content": {"name": "导入所有数据失败:\n{error}"},
            "existing_files_count": {"name": "\n... 还有 {len} 个文件"},
            "existing_files_title": {"name": "文件已存在"},
            "existing_files_content": {
                "name": "以下文件已存在:\n{files}\n\n是否覆盖这些文件？"
            },
            "version_mismatch_title": {"name": "版本不匹配"},
            "version_mismatch_content": {
                "name": "导出数据的软件版本与当前版本不一致:\n\n导出数据的软件: {software_name} {version}\n当前软件: SecRandom {current_version}\n\n是否继续导入？"
            },
            "export_warning_title": {"name": "导出所有数据"},
            "export_warning_content": {
                "name": "即将导出所有数据，包括:\n\n软件版本、设置配置\n点名名单、抽奖名单\n历史记录、日志文件\n\n注意: 导出的数据可能包含敏感信息，请妥善保管。\n\n是否继续导出?"
            },
            "export_confirm_button": {"name": "确认导出"},
            "export_cancel_button": {"name": "取消导出"},
        },
        "diagnostic_data_export": {
            "export_confirm_button": {"name": "确认导出"},
            "export_cancel_button": {"name": "取消导出"},
            "export_success_title": {"name": "导出诊断数据"},
            "export_success_content": {"name": "诊断数据已成功导出到:\n{path}"},
            "export_failure_title": {"name": "导出诊断数据"},
            "export_failure_content": {"name": "导出诊断数据失败:\n{error}"},
            "export_warning_title": {"name": "导出诊断数据"},
            "export_warning_content": {
                "name": "即将导出诊断数据，包括:\n\n软件信息、设置配置\n点名名单、抽奖名单\n历史记录、日志文件\n\n注意: 导出的数据可能包含敏感信息，请妥善保管。\n\n是否继续导出?"
            },
        },
    },
    "EN_US": {
        "title": {
            "name": "Basic settings",
            "description": "Configure basic features and appearance of software",
        },
        "basic_function": {
            "name": "Basic functions",
            "description": "Configure software's core functions",
        },
        "data_management": {
            "name": "Data management",
            "description": "Import or export the software's data",
        },
        "personalised": {
            "name": "Customization",
            "description": "Customize the look and experience of software",
        },
        "telemetry_category": {
            "name": "Telemetry & Privacy",
            "description": "Manage online status reporting and privacy settings",
        },
        "simplified_mode": {
            "name": "Simplified mode",
            "description": "Hide advanced settings, show only recommended settings",
        },
        "simplified_mode_notification": {
            "enable": "Simplified mode enabled",
            "disable": "Simplified mode disabled",
        },
        "autostart": {
            "name": "Start on boot",
            "description": "Set whether the software is running automatically with the system",
        },
        "check_update": {
            "name": "Check for updates on startup",
            "description": "Set whether to automatically check for new versions when software starts",
        },
        "show_startup_window": {
            "name": "Show splash screen",
            "description": "Set whether to show the splash screen on boot",
        },
        "main_window_topmost_mode": {
            "name": "Main window topmost",
            "description": "Select main window topmost mode (UIA requires run as administrator)",
            "combo_items": ["Disable topmost", "Topmost", "UIA topmost"],
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
        "export_diagnostic_data": {
            "name": "Export diagnostic data",
            "description": "Export Diagnostic Information on Exit",
            "pushbutton_name": "Export diagnostic data",
        },
        "export_settings": {
            "name": "Export settings",
            "description": "Export current settings to profile",
            "pushbutton_name": "Export settings",
        },
        "import_settings": {
            "name": "Import settings",
            "description": "Import settings from profile to overwrite the current profile",
            "pushbutton_name": "Import settings",
        },
        "export_all_data": {
            "name": "Export all data",
            "description": "Export all data and settings when exit",
            "pushbutton_name": "Export all data",
        },
        "import_all_data": {
            "name": "Import all data",
            "description": "Restore all data from backup file when software starts",
            "pushbutton_name": "Import all data",
        },
        "log_viewer": {
            "name": "Viewer Log",
            "description": "View and manage program log files",
            "pushbutton_name": "Viewer Log",
        },
        "backup_manager": {
            "name": "Backup manager",
            "description": "Manage automatic and manual backups",
            "pushbutton_name": "Backup manager",
        },
        "backup_auto_settings": {
            "name": "Automatic backup",
            "description": "Configure automatic backup policy",
        },
        "backup_manual_settings": {
            "name": "Manual backup",
            "description": "Create backups and manage backup files",
        },
        "backup_auto_enabled": {
            "name": "Enabled",
            "description": "Enable automatic backups",
            "switchbutton_name": {"enable": "On", "disable": "Off"},
        },
        "backup_auto_interval_days": {
            "name": "Backup interval",
            "description": "Set automatic backup interval (days)",
        },
        "backup_auto_max_count": {
            "name": "Backup limit",
            "description": "Maximum number of backups to keep (0 = unlimited)",
        },
        "backup_last_success": {
            "name": "Last successful backup",
            "description": "Show last successful backup time",
        },
        "backup_now": {
            "name": "Backup now",
            "description": "Create a full backup now",
            "pushbutton_name": "Backup now",
        },
        "backup_open_folder": {
            "name": "View backups",
            "description": "Open backup folder",
            "pushbutton_name": "View backups",
        },
        "backup_folder_size": {
            "name": "Storage usage",
            "description": "Backup folder size",
        },
        "backup_content_settings": {
            "name": "Backup content",
            "description": "Choose what to include in the backup file",
        },
        "backup_content_tip": {
            "text": "When an item is off, its folder will not be included in the backup file.",
        },
        "backup_restore_settings": {
            "name": "Restore backup",
            "description": "Select a backup file to restore (restart required)",
        },
        "backup_restore_tip": {
            "text": "Restoring will overwrite current data. Restart the app for changes to take effect.",
        },
        "backup_restore_file_list": {
            "name": "Backup files",
            "description": "Select a backup file to restore",
        },
        "backup_restore_refresh": {
            "name": "Refresh list",
            "description": "Refresh backup file list",
            "pushbutton_name": "Refresh",
        },
        "backup_restore_start": {
            "name": "Restore",
            "description": "Restore data from the selected backup file",
            "pushbutton_name": "Restore",
        },
        "backup_restore_delete": {
            "name": "Delete backup",
            "description": "Delete the selected backup file",
            "pushbutton_name": "Delete",
        },
        "backup_restore_no_selection": {
            "text": "Please select a backup file to restore first",
        },
        "backup_restore_confirm": {
            "title": "Restore backup",
            "content": "Are you sure you want to restore data from backup file '{file}'?\n\nThis will overwrite all current settings and data, and cannot be undone.",
            "confirm_button": "Restore",
            "cancel_button": "Cancel",
        },
        "backup_restore_delete_confirm": {
            "title": "Confirm delete",
            "content": "Are you sure you want to delete this backup file?\n\n{file}\n\nThis action cannot be undone.",
        },
        "backup_restore_refresh_result": {
            "success": "Refreshed. {count} backup file(s) found",
            "empty": "Refreshed. No backup files found",
            "failure": "Refresh failed: {error}",
        },
        "backup_restore_delete_result": {
            "success": "Deleted: {file}",
            "failure": "Delete failed: {error}",
        },
        "backup_restore_table_headers": ["Backup file", "Created at", "Size", "Action"],
        "include_config": {
            "name": "Config",
            "description": "App settings and configuration files (config)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_list": {
            "name": "Lists",
            "description": "Roll-call/prize lists and related data (list)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_language": {
            "name": "Language",
            "description": "Localization and language resources (Language)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_history": {
            "name": "History",
            "description": "Extraction history records (history)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_audio": {
            "name": "Audio",
            "description": "Audio resources (audio)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_cses": {
            "name": "CSES linkage",
            "description": "CSES integration data (CSES)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_images": {
            "name": "Images",
            "description": "Student/prize image resources (images)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_themes": {
            "name": "Themes",
            "description": "Installed theme resources (theme)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "include_logs": {
            "name": "Logs",
            "description": "Runtime logs, may contain sensitive info (logs)",
            "switchbutton_name": {"enable": "Include", "disable": "Exclude"},
        },
        "backup_tabs": {
            "auto": "Automatic backup",
            "manual": "Manual backup",
            "restore": "Restore backup",
            "content": "Backup content",
        },
        "backup_last_success_text": {"none": "No backup record"},
        "backup_now_result": {
            "success": "Backup succeeded:\n{path}",
            "failure": "Backup failed:\n{error}",
        },
        "dpiScale": {
            "name": "DPI scale settings",
            "description": "Resize the app interface (restart required)",
            "combo_items": {
                "0": "100%",
                "1": "125%",
                "2": "150%",
                "3": "175%",
                "4": "200%",
                "5": "Auto",
            },
        },
        "font": {
            "name": "Font",
            "description": "Set font to display (restart required)",
        },
        "font_weight": {
            "name": "Font weight",
            "description": "Set font weight to display (restart required)",
            "combo_items": [
                "Thin",
                "ExtraLight",
                "Light",
                "Normal",
                "Medium",
                "DemiBold",
                "Bold",
                "ExtraBold",
                "Black",
            ],
        },
        "theme": {
            "name": "Theme mode",
            "description": "Select the software interface theme style",
            "combo_items": {"0": "Light", "1": "Dark", "2": "Follow system"},
        },
        "theme_color": {
            "name": "Theme color",
            "description": "Set the software interface theme color",
        },
        "open_theme_management": {
            "name": "Theme management",
            "description": "Open theme management and background settings",
            "pushbutton_name": "Open",
        },
        "language": {
            "name": "Display language",
            "description": "Switch display language (restart required)",
        },
        "background_resident": {
            "name": "Run in background",
            "description": "Whether to remain in the back office after closing all windows",
        },
        "auto_save_window_size": {
            "name": "Auto save window size",
            "description": "Whether to automatically save window size",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "url_protocol": {
            "name": "URL protocol & IPC service",
            "description": "Register custom URL protocol (secrandom://) and enable IPC, supporting launching via link and external integrations",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "url_protocol_notification": {
            "enable": "URL protocol & IPC service enabled",
            "disable": "URL protocol & IPC service disabled",
            "register_failure": "Failed to register URL protocol & IPC service",
            "unregister_failure": "Failed to unregister URL protocol & IPC service",
            "error": "URL protocol & IPC service setting error: {error}",
        },
        "settings_import_export": {
            "export_success_title": {"name": "Export settings"},
            "export_success_content": {
                "name": "Settings have been exported to:\n{path}"
            },
            "export_failure_title": {"name": "Export settings"},
            "export_failure_content": {"name": "Failed to export settings: \n{error}"},
            "import_confirm_title": {"name": "Import settings"},
            "import_confirm_content": {
                "name": "Are you sure you want to import these settings? This will overwrite the current settings"
            },
            "import_confirm_button": {"name": "Confirm import"},
            "import_cancel_button": {"name": "Cancel import"},
            "import_success_title": {"name": "Import settings"},
            "import_success_content": {
                "name": "Settings successfully imported\nRestart to take effect"
            },
            "import_success_button": {"name": "Got it"},
            "export_success_button": {"name": "Got it"},
            "import_failure_title": {"name": "Import settings"},
            "import_failure_content": {"name": "Failed to import settings: \n{error}"},
        },
        "data_import_export": {
            "export_success_title": {"name": "Export all data"},
            "export_success_content": {
                "name": "All data has been exported to:\n{path}"
            },
            "export_failure_title": {"name": "Export all data"},
            "export_failure_content": {"name": "Failed to export all data: \n{error}"},
            "import_confirm_title": {"name": "Import all data"},
            "import_confirm_content": {
                "name": "Are you sure you want to import these data? This will overwrite the current data"
            },
            "import_confirm_button": {"name": "Confirm import"},
            "import_cancel_button": {"name": "Cancel import"},
            "import_success_title": {"name": "Import all data"},
            "import_success_content": {
                "name": "Data imported successfully into\nRestart APP to take effect"
            },
            "import_success_content_skipped": {
                "name": "Data imported successfully (skipped {count} locked files)\nRestart APP to take effect"
            },
            "import_success_button": {"name": "Got it"},
            "import_failure_title": {"name": "Import all data"},
            "import_failure_content": {"name": "Failed to import all data: \n{error}"},
            "existing_files_count": {"name": "\n... still have {len} files"},
            "existing_files_title": {"name": "File already exists"},
            "existing_files_content": {
                "name": "The following files already exist:\n{files}\n\nOverwrite these files?"
            },
            "version_mismatch_title": {"name": "Version mismatch"},
            "version_mismatch_content": {
                "name": "The version from imported data mismatches with current version:\n\nImported data is from: {software_name} {version}\nCurrent software is: SecRandom {current_version}\n\nContinue importing?"
            },
            "export_warning_title": {"name": "Export all data"},
            "export_warning_content": {
                "name": "All data will soon be exported, including:\n\nsoftware version, settings,\nname list, prize list,\nhistory, log file\n\nNOTE: exported data may contain sensitive information, please keep it safe.\n\nContinue exporting?"
            },
            "export_confirm_button": {"name": "Confirm export"},
            "export_cancel_button": {"name": "Cancel export"},
        },
        "diagnostic_data_export": {
            "export_confirm_button": {"name": "Confirm Export"},
            "export_cancel_button": {"name": "Cancel export"},
            "export_success_title": {"name": "Export diagnostic data"},
            "export_success_content": {
                "name": "Diagnostic data has been exported to:\n{path}"
            },
            "export_failure_title": {"name": "Export diagnostic data"},
            "export_failure_content": {
                "name": "Failed to export diagnostic data: \n{error}"
            },
            "export_warning_title": {"name": "Export diagnostic data"},
            "export_warning_content": {
                "name": "Diagnostic data will soon be exported, including:\n\nsoftware information, settings,\nname list, prize list,\nhistory, log file\n\nNOTE: exported data may contain sensitive information, please keep it safe.\n\nContinue exporting?"
            },
        },
        "telemetry": {
            "name": "Telemetry",
            "description": "Report online status data to help improve the software",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "telemetry_notification": {
            "enable": "Telemetry enabled",
            "disable": "Telemetry disabled, restart required",
        },
        "telemetry_mode": {
            "name": "Online Status",
            "description": "Full includes location, Anonymous counts presence only, Stopped disables reporting",
            "combo_items": ["Full", "Anonymous", "Stopped"],
        },
        "success_enable_content": {
            "name": "Enabled displaying main window when launching"
        },
        "info_disable_content": {
            "name": "Disabled displaying main window when launching"
        },
    },
    "JA_JP": {
        "title": {
            "name": "基本設定",
            "description": "ソフトウェアの基本機能と外観を設定",
        },
        "basic_function": {
            "name": "基本機能",
            "description": "ソフトウェアのコア機能オプションを設定",
        },
        "data_management": {
            "name": "データ管理",
            "description": "ソフトウェアのデータのインポートとエクスポートを管理",
        },
        "personalised": {
            "name": "パーソナライズ",
            "description": "ソフトウェアの外観とユーザー体験をカスタマイズ",
        },
        "telemetry_category": {
            "name": "テレメトリとプライバシー",
            "description": "オンライン状態の報告とプライバシー設定を管理",
        },
        "simplified_mode": {
            "name": "簡易設定モード",
            "description": "高度な設定項目を非表示にし、推奨設定のみを表示",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "simplified_mode_notification": {
            "enable": "簡易設定モードを有効にしました",
            "disable": "簡易設定モードを無効にしました",
        },
        "autostart": {
            "name": "起動時に自動実行",
            "description": "システム起動時にソフトウェアを自動実行するかどうかを設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "show_startup_window": {
            "name": "起動時にメインウィンドウを表示",
            "description": "ソフトウェア起動時にメインウィンドウを自動表示するかどうかを設定",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "main_window_topmost_mode": {
            "name": "メインウィンドウ最前面",
            "description": "メインウィンドウの最前面方式を選択（UIA最前面は管理者権限で実行が必要）",
            "combo_items": ["最前面を無効", "最前面", "UIA最前面"],
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
            "name": "UIA最前面モードを無効にしました。完全にソフトウェアを終了して再起動すると有効になります",
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
        "background_resident": {
            "name": "バックグラウンド常駐",
            "description": "すべてのウィンドウを閉じた後もバックグラウンドで常駐するかどうか",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "auto_save_window_size": {
            "name": "ウィンドウサイズを自動保存",
            "description": "ウィンドウサイズを自動保存するかどうか",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "url_protocol": {
            "name": "URLプロトコル登録&IPCサービス",
            "description": "カスタムURLプロトコル(secrandom://)を登録し、IPC通信を有効にして、リンク起動と外部連携をサポート",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "autostart_notification": {
            "enable": "起動時に自動実行を有効にしました",
            "disable": "起動時に自動実行を無効にしました",
            "failure": "起動時に自動実行の設定に失敗しました",
        },
        "background_resident_notification": {
            "enable": "バックグラウンド常駐を有効にしました",
            "disable": "バックグラウンド常駐を無効にしました",
        },
        "auto_save_window_size_notification": {
            "enable": "ウィンドウサイズの自動保存を有効にしました",
            "disable": "ウィンドウサイズの自動保存を無効にしました",
        },
        "url_protocol_notification": {
            "enable": "URLプロトコル登録とIPCサービスを有効にしました",
            "disable": "URLプロトコル登録とIPCサービスを無効にしました",
            "register_failure": "URLプロトコル&IPCサービスの登録に失敗しました",
            "unregister_failure": "URLプロトコル&IPCサービスの登録解除に失敗しました",
            "error": "URLプロトコル&IPCサービス設定エラー: {error}",
        },
        "export_diagnostic_data": {
            "name": "診断データをエクスポート",
            "description": "ソフトウェア終了時に診断情報をエクスポートし、問題のトラブルシューティングに使用",
            "pushbutton_name": "診断データをエクスポート",
        },
        "export_settings": {
            "name": "設定をエクスポート",
            "description": "現在の設定を設定ファイルとしてエクスポートし、バックアップと移行に使用",
            "pushbutton_name": "設定をエクスポート",
        },
        "import_settings": {
            "name": "設定をインポート",
            "description": "設定ファイルから設定をインポートし、現在の設定情報を上書き",
            "pushbutton_name": "設定をインポート",
        },
        "export_all_data": {
            "name": "すべてのデータをエクスポート",
            "description": "ソフトウェア終了時にすべてのデータと設定をエクスポート",
            "pushbutton_name": "すべてのデータをエクスポート",
        },
        "import_all_data": {
            "name": "すべてのデータをインポート",
            "description": "ソフトウェア起動時にバックアップファイルからすべてのデータを復元",
            "pushbutton_name": "すべてのデータをインポート",
        },
        "log_viewer": {
            "name": "ログを表示",
            "description": "プログラムログファイルを表示・管理",
            "pushbutton_name": "ログを表示",
        },
        "backup_manager": {
            "name": "バックアップ管理",
            "description": "自動バックアップと手動バックアップを管理",
            "pushbutton_name": "バックアップ管理",
        },
        "backup_auto_settings": {
            "name": "自動バックアップ",
            "description": "自動バックアップ戦略を設定",
        },
        "backup_manual_settings": {
            "name": "手動バックアップ",
            "description": "すぐにバックアップを作成し、バックアップファイルを管理",
        },
        "backup_auto_enabled": {
            "name": "有効にするかどうか",
            "description": "自動バックアップを有効にするかどうか",
            "switchbutton_name": {"enable": "有効", "disable": "無効"},
        },
        "backup_auto_interval_days": {
            "name": "自動バックアップ間隔",
            "description": "自動バックアップ間隔を設定（日）",
        },
        "backup_auto_max_count": {
            "name": "自動バックアップ上限",
            "description": "自動バックアップの最大保持数を設定（個）、0は制限なし",
        },
        "backup_last_success": {
            "name": "前回の成功したバックアップ",
            "description": "前回の成功したバックアップ時刻を表示",
        },
        "backup_now": {
            "name": "すぐにバックアップ",
            "description": "すぐにフルバックアップを作成",
            "pushbutton_name": "すぐにバックアップ",
        },
        "backup_open_folder": {
            "name": "バックアップファイルを表示",
            "description": "バックアップフォルダを開く",
            "pushbutton_name": "バックアップファイルを表示",
        },
        "backup_folder_size": {
            "name": "占有サイズ",
            "description": "バックアップフォルダの占有サイズ",
        },
        "backup_content_settings": {
            "name": "バックアップ内容",
            "description": "バックアップファイルに含めるデータを選択",
        },
        "backup_content_tip": {
            "text": "項目をオフにすると、そのフォルダはバックアップファイルにパッケージされません。",
        },
        "backup_restore_settings": {
            "name": "バックアップを復元",
            "description": "バックアップファイルを選択して復元（再起動後に有効）",
        },
        "backup_restore_tip": {
            "text": "復元は現在のデータを上書きします。復元完了後、変更を有効にするためにアプリケーションを再起動してください。",
        },
        "backup_restore_file_list": {
            "name": "バックアップファイルリスト",
            "description": "復元するバックアップファイルを選択",
        },
        "backup_restore_refresh": {
            "name": "リストを更新",
            "description": "バックアップファイルリストを更新",
            "pushbutton_name": "更新",
        },
        "backup_restore_start": {
            "name": "復元を開始",
            "description": "選択したバックアップファイルを使用してデータを復元",
            "pushbutton_name": "復元",
        },
        "backup_restore_delete": {
            "name": "バックアップを削除",
            "description": "選択したバックアップファイルを削除",
            "pushbutton_name": "削除",
        },
        "backup_restore_no_selection": {
            "text": "復元するバックアップファイルを先に選択してください",
        },
        "backup_restore_confirm": {
            "title": "バックアップを復元",
            "content": "バックアップファイル「{file}」からデータを復元しますか？\n\nこの操作は現在のすべての設定とデータを上書きし、元に戻すことはできません。",
            "confirm_button": "復元",
            "cancel_button": "キャンセル",
        },
        "backup_restore_delete_confirm": {
            "title": "削除の確認",
            "content": "このバックアップファイルを削除しますか？\n\n{file}\n\nこの操作は取り消せません。",
            "pushbutton_name": "削除",
        },
        "backup_restore_delete_cancel": {
            "name": "キャンセル",
            "description": "削除操作をキャンセル",
            "pushbutton_name": "キャンセル",
        },
        "backup_restore_refresh_result": {
            "success": "更新しました、合計 {count} 個のバックアップファイル",
            "empty": "更新しました、バックアップファイルが見つかりませんでした",
            "failure": "更新に失敗しました：{error}",
        },
        "backup_restore_delete_result": {
            "success": "削除しました：{file}",
            "failure": "削除に失敗しました：{error}",
        },
        "backup_restore_table_headers": [
            "バックアップファイル",
            "作成時刻",
            "サイズ",
            "操作",
        ],
        "include_config": {
            "name": "設定ファイル",
            "description": "ソフトウェア設定と設定ファイル（config）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_list": {
            "name": "リストデータ",
            "description": "点呼/抽選リストなどのデータ（list）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_language": {
            "name": "言語ファイル",
            "description": "多言語テキスト設定（Language）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_history": {
            "name": "履歴記録",
            "description": "抽選履歴データ（history）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_audio": {
            "name": "オーディオリソース",
            "description": "オーディオリソースファイル（audio）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_cses": {
            "name": "CSES連携",
            "description": "CSES関連連携データ（CSES）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_images": {
            "name": "画像リソース",
            "description": "学生/賞品画像リソース（images）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_themes": {
            "name": "テーマリソース",
            "description": "インストール済みのテーマリソース（theme）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "include_logs": {
            "name": "実行ログ",
            "description": "プログラム実行ログ、機密情報が含まれる可能性があります（logs）",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "backup_tabs": {
            "auto": "自動バックアップ",
            "manual": "手動バックアップ",
            "restore": "バックアップを復元",
            "content": "バックアップ内容",
        },
        "backup_last_success_text": {"none": "前回のバックアップ記録がありません"},
        "backup_now_result": {
            "success": "バックアップ成功:\n{path}",
            "failure": "バックアップ失敗:\n{error}",
        },
        "dpiScale": {
            "name": "DPIスケーリング",
            "description": "ソフトウェアインターフェースのスケーリング比率を調整（ソフトウェア再起動後に有効）",
            "combo_items": ["100%", "125%", "150%", "175%", "200%", "自動"],
        },
        "font": {
            "name": "フォント",
            "description": "ソフトウェアインターフェース表示フォントを設定（ソフトウェア再起動後に有効）",
        },
        "font_weight": {
            "name": "フォント太さ",
            "description": "ソフトウェアインターフェースフォント太さを設定（ソフトウェア再起動後に有効）",
            "combo_items": [
                "極細",
                "特細",
                "細体",
                "標準",
                "中太",
                "半太",
                "太字",
                "特太",
                "極太",
            ],
        },
        "theme": {
            "name": "テーマモード",
            "description": "ソフトウェアインターフェーステーマスタイルを選択",
            "combo_items": ["ライト", "ダーク", "システムに従う"],
        },
        "theme_color": {
            "name": "テーマカラー",
            "description": "ソフトウェアインターフェーステーマカラーを設定",
        },
        "open_theme_management": {
            "name": "テーマ管理",
            "description": "テーマ管理と背景設定を開く",
            "pushbutton_name": "開く",
        },
        "language": {
            "name": "表示言語",
            "description": "ソフトウェアインターフェース言語を切り替え（ソフトウェア再起動後に有効）",
        },
        "settings_import_export": {
            "export_success_title": {"name": "設定をエクスポート"},
            "export_success_content": {
                "name": "設定を正常にエクスポートしました:\n{path}"
            },
            "export_failure_title": {"name": "設定をエクスポート"},
            "export_failure_content": {
                "name": "設定のエクスポートに失敗しました:\n{error}"
            },
            "import_confirm_title": {"name": "設定をインポート"},
            "import_confirm_content": {
                "name": "これらの設定をインポートしますか？これにより現在の設定が上書きされます"
            },
            "import_confirm_button": {"name": "インポートを確認"},
            "import_cancel_button": {"name": "インポートをキャンセル"},
            "import_success_title": {"name": "設定をインポート"},
            "import_success_content": {
                "name": "設定を正常にインポートしました\n変更を有効にするにはアプリケーションを再起動してください"
            },
            "import_success_button": {"name": "了解しました"},
            "export_success_button": {"name": "了解しました"},
            "import_failure_title": {"name": "設定をインポート"},
            "import_failure_content": {
                "name": "設定のインポートに失敗しました:\n{error}"
            },
        },
        "telemetry": {
            "name": "テレメトリ",
            "description": "ソフトウェア改善のため、オンライン状態データを報告",
            "switchbutton_name": {"enable": "", "disable": ""},
        },
        "telemetry_notification": {
            "enable": "テレメトリを有効にしました",
            "disable": "テレメトリを無効にしました、再起動が必要です",
        },
        "telemetry_mode": {
            "name": "オンライン状態",
            "description": "完全は位置情報を含む、匿名は人数のみカウント、停止は報告しない",
            "combo_items": ["完全", "匿名", "停止"],
        },
        "success_enable_content": {
            "name": "起動時にメインウィンドウを表示を有効にしました"
        },
        "info_disable_content": {
            "name": "起動時にメインウィンドウを表示を無効にしました"
        },
        "data_import_export": {
            "export_success_title": {"name": "すべてのデータをエクスポート"},
            "export_success_content": {
                "name": "すべてのデータを正常にエクスポートしました:\n{path}"
            },
            "export_failure_title": {"name": "すべてのデータをエクスポート"},
            "export_failure_content": {
                "name": "すべてのデータのエクスポートに失敗しました:\n{error}"
            },
            "import_confirm_title": {"name": "すべてのデータをインポート"},
            "import_confirm_content": {
                "name": "これらのデータをインポートしますか？これにより現在のデータが上書きされます"
            },
            "import_confirm_button": {"name": "インポートを確認"},
            "import_cancel_button": {"name": "インポートをキャンセル"},
            "import_success_title": {"name": "すべてのデータをインポート"},
            "import_success_content": {
                "name": "データを正常にインポートしました\n変更を有効にするにはアプリケーションを再起動してください"
            },
            "import_success_content_skipped": {
                "name": "データを正常にインポートしました（{count} 個の使用中ファイルをスキップ）\n変更を有効にするにはアプリケーションを再起動してください"
            },
            "import_success_button": {"name": "了解しました"},
            "import_failure_title": {"name": "すべてのデータをインポート"},
            "import_failure_content": {
                "name": "すべてのデータのインポートに失敗しました:\n{error}"
            },
            "existing_files_count": {"name": "\n... まだ {len} 個のファイルがあります"},
            "existing_files_title": {"name": "ファイルが既に存在します"},
            "existing_files_content": {
                "name": "以下のファイルが既に存在します:\n{files}\n\nこれらのファイルを上書きしますか？"
            },
            "version_mismatch_title": {"name": "バージョン不一致"},
            "version_mismatch_content": {
                "name": "インポートデータのソフトウェアバージョンと現在のバージョンが一致しません:\n\nインポートデータのソフトウェア: {software_name} {version}\n現在のソフトウェア: SecRandom {current_version}\n\nインポートを続行しますか？"
            },
            "export_warning_title": {"name": "すべてのデータをエクスポート"},
            "export_warning_content": {
                "name": "まもなくすべてのデータをエクスポートします、以下を含みます:\n\nソフトウェアバージョン、設定\n点呼リスト、抽選リスト\n履歴記録、ログファイル\n\n注意: エクスポートされたデータは機密情報を含む可能性があるため、適切に保管してください。\n\nエクスポートを続行しますか?"
            },
            "export_confirm_button": {"name": "エクスポートを確認"},
            "export_cancel_button": {"name": "エクスポートをキャンセル"},
        },
        "diagnostic_data_export": {
            "export_confirm_button": {"name": "エクスポートを確認"},
            "export_cancel_button": {"name": "エクスポートをキャンセル"},
            "export_success_title": {"name": "診断データをエクスポート"},
            "export_success_content": {
                "name": "診断データを正常にエクスポートしました:\n{path}"
            },
            "export_failure_title": {"name": "診断データをエクスポート"},
            "export_failure_content": {
                "name": "診断データのエクスポートに失敗しました:\n{error}"
            },
            "export_warning_title": {"name": "診断データをエクスポート"},
            "export_warning_content": {
                "name": "まもなく診断データをエクスポートします、以下を含みます:\n\nソフトウェア情報、設定\n点呼リスト、抽選リスト\n履歴記録、ログファイル\n\n注意: エクスポートされたデータは機密情報を含む可能性があるため、適切に保管してください。\n\nエクスポートを続行しますか?"
            },
        },
    },
}
