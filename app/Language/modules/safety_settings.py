# 安全设置语言配置
safety_settings = {
    "ZH_CN": {"title": {"name": "安全设置", "description": "配置软件的安全相关设置"}}
}

# 基础安全设置语言配置
basic_safety_settings = {
    "ZH_CN": {
        "title": {"name": "基础安全设置", "description": "配置基础安全验证功能"},
        "verification_method": {
            "name": "验证方式",
            "description": "配置安全功能的验证方式",
        },
        "verification_process": {
            "name": "安全验证步骤",
            "description": "选择安全验证的组合方式",
            "combo_items": [
                "单步验证（任选一种方式）",
                "仅密码",
                "仅TOTP",
                "仅U盘解锁",
                "密码+TOTP",
                "密码+U盘解锁",
                "TOTP+U盘解锁",
                "密码+TOTP+U盘解锁",
            ],
        },
        "security_operations": {
            "name": "安全操作",
            "description": "配置需要安全验证的操作",
        },
        "safety_switch": {
            "name": "安全开关",
            "description": "启用后，所有安全操作都需要验证密码",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "set_password": {
            "name": "设置/修改密码",
            "description": "设置或修改安全验证密码",
        },
        "totp_switch": {
            "name": "TOTP验证",
            "description": "启用后可在安全操作中使用TOTP动态口令",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "set_totp": {"name": "设置TOTP", "description": "配置TOTP动态口令验证"},
        "usb_switch": {
            "name": "U盘验证",
            "description": "启用后可在安全操作中使用U盘验证",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "bind_usb": {"name": "绑定U盘", "description": "绑定用于验证的U盘设备"},
        "unbind_usb": {"name": "解绑U盘", "description": "解除U盘设备的绑定"},
        "show_hide_floating_window_switch": {
            "name": "显示/隐藏浮窗验证",
            "description": "启用后显示或隐藏浮窗时需要安全验证",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "restart_switch": {
            "name": "重启验证",
            "description": "启用后重启软件时需要安全验证",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
        "exit_switch": {
            "name": "退出验证",
            "description": "启用后退出软件时需要安全验证",
            "switchbutton_name": {"enable": "启用", "disable": "禁用"},
        },
    }
}
