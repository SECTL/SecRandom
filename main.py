import os
import sys
import time
import gc

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QApplication
from loguru import logger

from app.tools.path_utils import get_app_root
from app.tools.config import configure_logging
from app.tools.settings_access import readme_settings_async
from app.tools.variable import APP_QUIT_ON_LAST_WINDOW_CLOSED
from app.core.single_instance import (
    check_single_instance,
    setup_local_server,
    send_url_to_existing_instance,
)
from app.core.font_manager import configure_dpi_scale
from app.core.window_manager import WindowManager
from app.core.url_handler_setup import create_url_handler
from app.core.cs_ipc_handler_setup import create_cs_ipc_handler
from app.core.app_init import AppInitializer
from app.tools.update_utils import update_check_thread
import app.core.window_manager as wm


def main():
    """主程序入口"""
    program_dir = str(get_app_root())

    if os.getcwd() != program_dir:
        os.chdir(program_dir)
        logger.debug(f"工作目录已设置为: {program_dir}")

    logger.remove()
    configure_logging()

    wm.app_start_time = time.perf_counter()

    shared_memory, is_first_instance = check_single_instance()

    if not is_first_instance:
        if len(sys.argv) > 1 and any(
            arg.startswith("secrandom://") for arg in sys.argv
        ):
            for arg in sys.argv[1:]:
                if arg.startswith("secrandom://"):
                    send_url_to_existing_instance(arg)
                    break

        logger.info("程序将退出，已有实例已激活")
        sys.exit(0)

    configure_dpi_scale()

    app = QApplication(sys.argv)

    gc.enable()

    try:
        resident = readme_settings_async("basic_settings", "background_resident")
        resident = True if resident is None else resident
        app.setQuitOnLastWindowClosed(not resident)
    except Exception:
        app.setQuitOnLastWindowClosed(APP_QUIT_ON_LAST_WINDOW_CLOSED)

    app.setAttribute(Qt.ApplicationAttribute.AA_DontCreateNativeWidgetSiblings)

    window_manager = WindowManager()
    window_manager.set_shared_memory(shared_memory)
    url_handler = create_url_handler()
    cs_ipc_handler = create_cs_ipc_handler()
    window_manager.set_url_handler(url_handler)

    local_server = setup_local_server(
        window_manager.get_main_window(), window_manager.get_float_window(), url_handler
    )

    if not local_server:
        logger.error("无法启动本地服务器，程序将退出")
        shared_memory.detach()
        sys.exit(1)

    app_initializer = AppInitializer(window_manager)
    app_initializer.initialize()

    try:
        app.exec()

        shared_memory.detach()

        if local_server:
            local_server.close()

        if update_check_thread and update_check_thread.isRunning():
            logger.debug("等待更新检查线程完成...")
            update_check_thread.wait(5000)
            if update_check_thread.isRunning():
                logger.warning("更新检查线程未在超时时间内完成，强制终止")

        gc.collect()

        sys.exit()
    except Exception as e:
        logger.error(f"应用程序启动失败: {e}")
        shared_memory.detach()

        if local_server:
            local_server.close()

        try:
            if update_check_thread and update_check_thread.isRunning():
                logger.debug("等待更新检查线程完成...")
                update_check_thread.wait(5000)
        except Exception as thread_e:
            logger.exception("处理更新检查线程时发生错误: {}", thread_e)

        sys.exit(1)


if __name__ == "__main__":
    main()
