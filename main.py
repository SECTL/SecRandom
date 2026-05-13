import os
import sys
import time
import gc
import subprocess
import platform

import sentry_sdk
from sentry_sdk.integrations.loguru import LoguruIntegration, LoggingLevels
from PySide6.QtCore import Qt, QTimer, qInstallMessageHandler
from PySide6.QtWidgets import QApplication
from loguru import logger

from app.tools.path_utils import get_app_root
from app.tools.config import (
    configure_logging,
    create_sentry_before_send_filter,
)
from app.tools.settings_default import manage_settings_file
from app.tools.settings_access import (
    get_bool_setting,
    get_or_create_user_id,
    get_setting,
)
from app.tools.online_status import (
    start_online_status_reporter,
    stop_online_status_reporter,
)
from app.tools.variable import (
    APP_QUIT_ON_LAST_WINDOW_CLOSED,
    VERSION,
    EXIT_CODE_RESTART,
    SENTRY_DSN,
    SENTRY_TRACES_SAMPLE_RATE,
    DEV_VERSION,
    DEV_HINT_DELAY_MS,
    UPDATE_CHECK_THREAD_TIMEOUT_MS,
    PROCESS_EXIT_WAIT_SECONDS,
)
from app.core.single_instance import (
    check_single_instance,
    setup_local_server,
    send_url_to_existing_instance,
)
from app.core.font_manager import (
    configure_dpi_scale,
    ensure_application_font_point_size,
)
from app.core.window_manager import WindowManager
from app.core.url_handler_setup import create_url_handler
from app.core.cs_ipc_handler_setup import create_cs_ipc_handler
from app.core.app_init import AppInitializer
from app.tools.update_utils import update_check_thread
import app.core.window_manager as wm


# ==================================================
# Sentry 相关函数
# ==================================================


def initialize_sentry():
    """初始化 Sentry 错误监控系统"""
    sentry_sdk.init(
        dsn=SENTRY_DSN,
        integrations=[
            LoguruIntegration(
                level=LoggingLevels.INFO.value,
                event_level=LoggingLevels.ERROR.value,
            ),
        ],
        before_send=create_sentry_before_send_filter(),
        release=VERSION,
        send_default_pii=True,
        auto_session_tracking=True,
        enable_logs=True,
        traces_sample_rate=SENTRY_TRACES_SAMPLE_RATE,
    )
    user_id = get_or_create_user_id()
    sentry_sdk.set_user({"id": user_id, "ip_address": "{{auto}}"})


def initialize_online_status():
    """初始化在线状态上报系统（后台线程执行，不阻塞主线程）"""
    start_online_status_reporter()


# ==================================================
# 开发提示相关函数
# ==================================================


def add_dev_hint_to_window(window):
    """为窗口添加开发中提示

    Args:
        window: 要添加提示的窗口对象
    """
    from app.view.components.dev_hint_widget import DevHintWidget

    if not window.isWindow() or hasattr(window, "_dev_hint_added"):
        return

    allowed_window_classes = {
        "GuideWindow",
        "MainWindow",
        "SettingsWindow",
        "SimpleWindowTemplate",
    }
    window_class_name = window.__class__.__name__
    if window_class_name not in allowed_window_classes:
        return

    title_bar = getattr(window, "titleBar", None)
    if not title_bar:
        return

    dev_hint = DevHintWidget(title_bar, position_mode="titlebar_center")
    dev_hint.show()

    window._dev_hint_added = True

    original_resize_event = window.resizeEvent

    def new_resize_event(event, orig_event=original_resize_event, dh=dev_hint):
        if orig_event:
            orig_event(event)
        dh.update_position()

    window.resizeEvent = new_resize_event
    dev_hint.update_position()


def add_dev_hints_to_existing_windows():
    """为所有现有窗口添加开发提示"""
    for widget in QApplication.topLevelWidgets():
        add_dev_hint_to_window(widget)


def setup_dev_hints(app):
    """设置开发提示功能

    Args:
        app: QApplication 实例
    """
    QTimer.singleShot(DEV_HINT_DELAY_MS, add_dev_hints_to_existing_windows)

    original_notify = app.notify

    def new_notify(receiver, event):
        result = original_notify(receiver, event)

        if (
            hasattr(event, "type")
            and event.type() == event.Type.Show
            and hasattr(receiver, "isWindow")
            and receiver.isWindow()
            and not hasattr(receiver, "_dev_hint_added")
        ):
            add_dev_hint_to_window(receiver)

        return result

    app.notify = new_notify


# ==================================================
# 应用程序初始化相关函数
# ==================================================


def initialize_application():
    """初始化应用程序环境

    Returns:
        tuple: (program_dir, shared_memory, is_first_instance)
    """
    program_dir = str(get_app_root())

    if os.getcwd() != program_dir:
        os.chdir(program_dir)
        logger.debug(f"工作目录已设置为: {program_dir}")

    logger.remove()
    configure_logging()

    wm.app_start_time = time.perf_counter()

    shared_memory, is_first_instance = check_single_instance()

    time.sleep(PROCESS_EXIT_WAIT_SECONDS)

    return program_dir, shared_memory, is_first_instance


def handle_existing_instance(shared_memory):
    """处理已存在的应用程序实例

    Args:
        shared_memory: 共享内存对象
    """
    if len(sys.argv) > 1 and any(arg.startswith("secrandom://") for arg in sys.argv):
        for arg in sys.argv[1:]:
            if arg.startswith("secrandom://"):
                send_url_to_existing_instance(arg)
                break

    logger.info("程序将退出，已有实例已激活")
    shared_memory.detach()
    sys.exit(0)


def setup_qt_application():
    """设置 Qt 应用程序

    Returns:
        tuple: (app, window_manager, url_handler, cs_ipc_handler, local_server)
    """
    configure_dpi_scale()

    app = QApplication(sys.argv)
    handler_holder = {"previous_handler": None}

    def qt_message_handler(mode, context, message):
        if str(message).startswith("QFont::setPointSize: Point size <= 0"):
            return
        previous_handler = handler_holder.get("previous_handler")
        if previous_handler is not None:
            previous_handler(mode, context, message)
        else:
            sys.__stderr__.write(f"{message}\n")

    handler_holder["previous_handler"] = qInstallMessageHandler(qt_message_handler)
    ensure_application_font_point_size()

    gc.enable()

    try:
        resident = get_bool_setting("basic_settings", "background_resident", True)
        app.setQuitOnLastWindowClosed(not resident)
    except Exception:
        app.setQuitOnLastWindowClosed(APP_QUIT_ON_LAST_WINDOW_CLOSED)

    app.setAttribute(Qt.ApplicationAttribute.AA_DontCreateNativeWidgetSiblings)

    window_manager = WindowManager()
    url_handler = create_url_handler()
    cs_ipc_handler = create_cs_ipc_handler()
    window_manager.set_url_handler(url_handler)

    local_server = setup_local_server(
        window_manager.get_main_window(), window_manager.get_float_window(), url_handler
    )

    return app, window_manager, url_handler, cs_ipc_handler, local_server


def initialize_app_components(window_manager):
    """初始化应用程序组件

    Args:
        window_manager: 窗口管理器实例
    """
    app_initializer = AppInitializer(window_manager)
    app_initializer.initialize()


# ==================================================
# 应用程序清理相关函数
# ==================================================


def cleanup_resources(
    shared_memory, local_server, url_handler, cs_ipc_handler, update_check_thread
):
    """清理应用程序资源

    Args:
        shared_memory: 共享内存对象
        local_server: 本地服务器对象
        url_handler: URL 处理器对象
        cs_ipc_handler: CS IPC 处理器对象
        update_check_thread: 更新检查线程对象
    """
    if cs_ipc_handler:
        cs_ipc_handler.stop_ipc_client()

    if url_handler and hasattr(url_handler, "url_ipc_handler"):
        url_handler.url_ipc_handler.stop_ipc_server()

    shared_memory.detach()
    logger.debug("共享内存已释放")

    if local_server:
        local_server.close()
        logger.debug("本地服务器已关闭")

    if update_check_thread and update_check_thread.isRunning():
        logger.debug("正在等待更新检查线程完成...")
        update_check_thread.wait(UPDATE_CHECK_THREAD_TIMEOUT_MS)
        if update_check_thread.isRunning():
            logger.warning("更新检查线程超时，强行退出")
        else:
            logger.debug("更新检查线程已安全完成")

    gc.collect()
    logger.debug("垃圾回收已完成")


def restart_application(program_dir):
    """重启应用程序

    Args:
        program_dir: 程序目录路径
    """
    logger.info("检测到重启信号，正在重启应用程序...")
    filtered_args = [arg for arg in sys.argv if not arg.startswith("--")]

    executable = sys.executable

    if not os.path.exists(executable):
        logger.critical(f"重启失败：无法找到可执行文件: {executable}")
        os._exit(1)

    try:
        os.chdir(program_dir)

        # Windows 平台使用 subprocess.Popen 启动新进程
        if platform.system() == "Windows":
            try:
                from app.common.windows.uiaccess import (
                    ELEVATE_RESTART_ENV,
                    UIACCESS_RESTART_ENV,
                    UIACCESS_RESTART_ARG,
                    start_elevated_process,
                    start_uiaccess_process,
                )

                need_uiaccess = bool(os.environ.pop(UIACCESS_RESTART_ENV, "") == "1")
                need_elevated = bool(os.environ.pop(ELEVATE_RESTART_ENV, "") == "1")
            except Exception:
                need_uiaccess = False
                need_elevated = False
                start_uiaccess_process = None
                start_elevated_process = None
                UIACCESS_RESTART_ARG = None

            if need_elevated and start_elevated_process is not None:
                cmd = [executable] + filtered_args
                if need_uiaccess and UIACCESS_RESTART_ARG:
                    cmd.append(str(UIACCESS_RESTART_ARG))
                try:
                    time.sleep(max(0.8, float(PROCESS_EXIT_WAIT_SECONDS or 0)))
                except Exception:
                    time.sleep(0.8)
                if bool(start_elevated_process(cmd, cwd=program_dir)):
                    logger.info("Windows 平台：已请求管理员启动新进程")
                    os._exit(0)

            if need_uiaccess and start_uiaccess_process is not None:
                cmd = [executable] + filtered_args
                normalized = []
                for arg in cmd:
                    try:
                        if (
                            isinstance(arg, str)
                            and arg
                            and not os.path.isabs(arg)
                            and not arg.startswith(("-", "/"))
                            and os.path.exists(os.path.join(program_dir, arg))
                        ):
                            normalized.append(os.path.join(program_dir, arg))
                        else:
                            normalized.append(arg)
                    except Exception:
                        normalized.append(arg)

                pid = int(start_uiaccess_process(normalized) or 0)
                if pid > 0:
                    logger.info("Windows 平台：UIAccess 进程已启动")
                    os._exit(0)

            startup_info = subprocess.STARTUPINFO()
            startup_info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            subprocess.Popen(
                [executable] + filtered_args,
                cwd=program_dir,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP
                | subprocess.DETACHED_PROCESS,
                startupinfo=startup_info,
            )
            logger.info("Windows 平台：新进程已启动")
            os._exit(0)
        else:
            # Linux/Unix/macOS 平台使用 os.execl 替换当前进程
            logger.info("Linux/Unix/macOS 平台：使用 execl 重启应用程序")
            os.execl(executable, executable, *filtered_args)
    except Exception as e:
        logger.warning(f"重启应用程序失败: {e}")
        os._exit(1)


def handle_exit(
    exit_code,
    program_dir,
    shared_memory,
    local_server,
    url_handler,
    cs_ipc_handler,
    update_check_thread,
):
    """处理应用程序退出

    Args:
        exit_code: 退出代码
        program_dir: 程序目录路径
        shared_memory: 共享内存对象
        local_server: 本地服务器对象
        url_handler: URL 处理器对象
        cs_ipc_handler: CS IPC 处理器对象
        update_check_thread: 更新检查线程对象
    """
    logger.debug("Qt 事件循环已结束")

    stop_online_status_reporter()

    cleanup_resources(
        shared_memory, local_server, url_handler, cs_ipc_handler, update_check_thread
    )

    logger.info("程序退出流程已完成，正在结束进程")
    if sys.stdout:
        sys.stdout.flush()
    if sys.stderr:
        sys.stderr.flush()

    if exit_code == EXIT_CODE_RESTART:
        restart_application(program_dir)

    os._exit(0)


# ==================================================
# 主程序入口
# ==================================================


def main():
    """主程序入口"""
    try:
        if platform.system() == "Windows":
            from app.common.windows.uiaccess import (
                UIACCESS_RESTART_ARG,
                is_uiaccess_process,
            )

            if UIACCESS_RESTART_ARG in sys.argv:
                try:
                    while UIACCESS_RESTART_ARG in sys.argv:
                        sys.argv.remove(UIACCESS_RESTART_ARG)
                except Exception:
                    pass

                if not bool(is_uiaccess_process()):
                    try:
                        wm.pending_uiaccess_restart_after_show = True
                    except Exception:
                        pass
    except Exception:
        pass

    program_dir, shared_memory, is_first_instance = initialize_application()

    if not is_first_instance:
        handle_existing_instance(shared_memory)

    manage_settings_file()

    if DEV_VERSION not in VERSION:
        if get_bool_setting("basic_settings", "telemetry_enabled", True):
            initialize_sentry()  # 初始化 Sentry 事件上报
    if get_setting("basic_settings", "telemetry_mode") != "off":
        initialize_online_status()  # 初始化在线状态上报

    app, window_manager, url_handler, cs_ipc_handler, local_server = (
        setup_qt_application()
    )

    if not local_server:
        logger.exception("无法启动本地服务器，程序将退出")
        shared_memory.detach()
        sys.exit(1)

    initialize_app_components(window_manager)

    if VERSION == DEV_VERSION:
        setup_dev_hints(app)

    try:
        exit_code = app.exec()
        handle_exit(
            exit_code,
            program_dir,
            shared_memory,
            local_server,
            url_handler,
            cs_ipc_handler,
            update_check_thread,
        )
    except Exception as e:
        logger.exception(f"程序退出过程中发生异常: {e}")
        if shared_memory:
            shared_memory.detach()
        if local_server:
            local_server.close()
        if sys.stdout:
            sys.stdout.flush()
        if sys.stderr:
            sys.stderr.flush()
        os._exit(1)


if __name__ == "__main__":
    main()
