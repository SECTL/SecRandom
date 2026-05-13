from typing import Tuple, Optional
from PySide6.QtCore import QSharedMemory
from PySide6.QtNetwork import QLocalSocket, QLocalServer
from loguru import logger

from app.tools.variable import SHARED_MEMORY_KEY
from app.core.utils import safe_execute


def check_single_instance() -> Tuple[Optional[QSharedMemory], bool]:
    """检查单实例，防止多个程序副本同时运行

    Returns:
        tuple: (QSharedMemory, bool) 共享内存对象和是否为第一个实例
    """
    shared_memory = QSharedMemory(SHARED_MEMORY_KEY)
    if not shared_memory.create(1):
        logger.info("检测到已有 SecRandom 实例正在运行，尝试激活已有实例")
        if shared_memory.attach():
            _activate_existing_instance()
            return shared_memory, False
        else:
            logger.warning("无法附加到共享内存，尝试检测服务器状态")
            if _check_server_alive():
                logger.info("检测到活跃的服务器，已有实例正在运行")
                _activate_existing_instance()
                return shared_memory, False
            else:
                logger.warning("服务器不可连接，清理残留资源后重新启动")
                _cleanup_stale_resources()
                if shared_memory.create(1):
                    logger.info("单实例检查通过（清理后重新创建）")
                    return shared_memory, True
                else:
                    logger.exception("清理后仍无法创建共享内存")
                    return shared_memory, False

    logger.info("单实例检查通过，可以安全启动程序")
    return shared_memory, True


def _check_server_alive() -> bool:
    """检查本地服务器是否在监听（可连接）

    Returns:
        bool: 服务器是否可连接
    """
    local_socket = QLocalSocket()
    local_socket.connectToServer(SHARED_MEMORY_KEY)
    connected = local_socket.waitForConnected(500)
    if connected:
        local_socket.disconnectFromServer()
        return True
    return False


def _cleanup_stale_resources() -> None:
    """清理残留的单实例资源"""
    QLocalServer.removeServer(SHARED_MEMORY_KEY)
    logger.debug("已清理残留的socket资源")

    _cleanup_stale_shared_memory()


def _cleanup_stale_shared_memory() -> None:
    """清理残留的共享内存段（POSIX系统需要）

    在POSIX系统上，QSharedMemory在进程崩溃后不会自动清理。
    需要先attach再detach来触发清理。
    """
    stale_memory = QSharedMemory(SHARED_MEMORY_KEY)
    if stale_memory.attach():
        logger.debug("检测到残留的共享内存段，正在清理")
        stale_memory.detach()
        logger.debug("已清理残留的共享内存段")


def _activate_existing_instance() -> bool:
    """激活已有实例

    Returns:
        bool: 是否激活成功
    """
    local_socket = QLocalSocket()
    return safe_execute(
        _send_activate_signal, local_socket, error_message="激活已有实例失败"
    )


def _send_activate_signal(socket: QLocalSocket) -> bool:
    """发送激活信号

    Args:
        socket: 本地套接字

    Returns:
        bool: 是否发送成功
    """
    socket.connectToServer(SHARED_MEMORY_KEY)
    if socket.waitForConnected(1000):
        socket.write(b"activate")
        socket.flush()
        socket.waitForBytesWritten(1000)
        logger.info("已发送激活信号到已有实例")
        socket.disconnectFromServer()
        return True
    return False


def setup_local_server(
    main_window, float_window, url_handler
) -> Optional[QLocalServer]:
    """设置本地服务器，用于接收激活窗口的信号

    Args:
        main_window: 主窗口实例
        float_window: 浮窗实例
        url_handler: URL处理器实例

    Returns:
        QLocalServer: 本地服务器对象
    """
    server = QLocalServer()
    if not server.listen(SHARED_MEMORY_KEY):
        error_string = server.errorString()
        logger.warning(f"本地服务器启动失败，尝试清理残留socket: {error_string}")

        QLocalServer.removeServer(SHARED_MEMORY_KEY)

        if not server.listen(SHARED_MEMORY_KEY):
            logger.warning(f"无法启动本地服务器: {server.errorString()}")
            return None

    server.newConnection.connect(
        lambda: _handle_new_connection(server, main_window, float_window, url_handler)
    )
    logger.debug("setup_local_server: 本地服务器已启动，等待激活信号")
    return server


def _handle_new_connection(
    server: QLocalServer, main_window, float_window, url_handler
) -> None:
    """处理新的连接请求

    Args:
        server: 本地服务器
        main_window: 主窗口实例
        float_window: 浮窗实例
        url_handler: URL处理器实例
    """
    socket = server.nextPendingConnection()
    if not socket:
        return

    if socket.waitForReadyRead(1000):
        data = socket.readAll()
        data_str = _decode_socket_data(data)
        logger.debug(f"setup_local_server.handle_new_connection: 收到数据: {data_str}")

        _process_socket_data(data_str, main_window, url_handler)

    socket.disconnectFromServer()


def _decode_socket_data(data) -> str:
    """解码套接字数据

    Args:
        data: 套接字数据

    Returns:
        解码后的字符串
    """
    return (
        data.data().decode("utf-8")
        if isinstance(data.data(), bytes)
        else str(data.data())
    )


def _process_socket_data(data_str: str, main_window, url_handler) -> None:
    """处理套接字数据

    Args:
        data_str: 数据字符串
        main_window: 主窗口实例
        url_handler: URL处理器实例
    """
    if data_str == "activate":
        _handle_activate_command(main_window)
    elif data_str.startswith("url:"):
        _handle_url_command(data_str, main_window, url_handler)
    else:
        logger.warning(
            f"setup_local_server.handle_new_connection: 未知的数据类型: {data_str}"
        )


def _handle_activate_command(main_window) -> None:
    """处理激活命令

    Args:
        main_window: 主窗口实例
    """
    if main_window:
        from app.core.utils import activate_window

        activate_window(main_window)
        logger.debug("setup_local_server.handle_new_connection: 已激活主窗口")


def _handle_url_command(data_str: str, main_window, url_handler) -> None:
    """处理URL命令

    Args:
        data_str: 数据字符串
        main_window: 主窗口实例
        url_handler: URL处理器实例
    """
    url = data_str[4:]
    logger.debug(f"setup_local_server.handle_new_connection: 收到URL参数: {url}")

    if url_handler:
        result = url_handler.handle_url(url)
        logger.debug(
            f"setup_local_server.handle_new_connection: url_handler.handle_url(url) 结果: {result}"
        )

        if main_window and "settings" not in url:
            from app.core.utils import activate_window

            activate_window(main_window)
            logger.debug("setup_local_server.handle_new_connection: 已激活主窗口")


def send_url_to_existing_instance(url: str) -> bool:
    """向已运行的实例发送URL参数

    Args:
        url: 要发送的URL

    Returns:
        bool: 是否发送成功
    """
    success, _ = safe_execute(_send_url, url, error_message="发送URL参数到已有实例失败")
    return success


def _send_url(url: str) -> bool:
    """发送URL到已有实例

    Args:
        url: 要发送的URL

    Returns:
        bool: 是否发送成功
    """
    local_socket = QLocalSocket()
    local_socket.connectToServer(SHARED_MEMORY_KEY)
    if local_socket.waitForConnected(1000):
        local_socket.write(f"url:{url}".encode("utf-8"))
        local_socket.flush()
        local_socket.waitForBytesWritten(1000)
        logger.debug(f"已发送URL参数到已有实例: {url}")
        local_socket.disconnectFromServer()
        return True
    return False
