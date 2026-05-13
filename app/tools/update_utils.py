# ==================================================
# 导入模块
# ==================================================
import asyncio
import time
from typing import Any, Tuple, Callable, Optional
import aiohttp
from loguru import logger
import yaml
from PySide6.QtCore import QObject, Signal, QThread
from app.tools.path_utils import *
from app.tools.variable import *
from app.tools.settings_access import *


# ==================================================
# 辅助函数
# ==================================================
def safe_int(s: str) -> int:
    """安全地将字符串转换为整数

    Args:
        s (str): 要转换的字符串

    Returns:
        int: 转换后的整数，如果转换失败则返回 0
    """
    try:
        return int(s) if s else 0
    except ValueError:
        return 0


def _run_async_func(async_func: Any, *args: Any, **kwargs: Any) -> Any:
    """运行异步函数（同步包装器）

    Args:
        async_func: 要运行的异步函数
        *args: 位置参数
        **kwargs: 关键字参数

    Returns:
        Any: 异步函数的返回值
    """
    try:
        return asyncio.run(async_func(*args, **kwargs))
    except Exception as e:
        logger.exception(f"运行异步函数失败: {e}")
        return None


def check_exe_integrity(exe_path: str) -> bool:
    """检查EXE安装程序的完整性

    Args:
        exe_path (str): exe文件路径

    Returns:
        bool: 文件完整返回True，否则返回False
    """
    try:
        logger.debug(f"检查EXE安装程序完整性: {exe_path}")

        # 检查文件是否存在
        exe_file = get_path(exe_path)
        if not exe_file.exists():
            logger.exception(f"EXE文件不存在: {exe_path}")
            return False

        # 检查文件大小
        file_size = exe_file.stat().st_size
        if file_size == 0:
            logger.exception(f"EXE文件大小为0: {exe_path}")
            return False

        # 检查PE文件头（Windows可执行文件）
        with open(exe_path, "rb") as f:
            # 读取前两个字节，检查MZ签名
            magic = f.read(2)
            if magic != b"MZ":
                logger.exception(f"EXE文件格式错误，不是有效的PE文件: {exe_path}")
                return False

        logger.debug(f"EXE安装程序完整性检查通过: {exe_path}")
        return True
    except Exception as e:
        logger.exception(f"检查EXE安装程序完整性失败: {e}")
        return False


def check_update_file_integrity(file_path: str, file_type: str = None) -> bool:
    """检查更新文件的完整性（仅支持 EXE）

    Args:
        file_path (str): 更新文件路径
        file_type (str, optional): 文件类型，如果不指定则自动检测

    Returns:
        bool: 文件完整返回True，否则返回False
    """
    try:
        # 自动检测文件类型
        if file_type is None:
            file_ext = get_path(file_path).suffix.lower()
            if file_ext == ".exe":
                file_type = "exe"
            else:
                logger.exception(
                    f"不支持的更新文件类型: {file_ext}，仅支持 EXE 安装程序"
                )
                return False

        # 根据文件类型调用相应的检查函数
        if file_type == "exe":
            return check_exe_integrity(file_path)
        else:
            logger.exception(f"不支持的文件类型: {file_type}，仅支持 EXE 安装程序")
            return False
    except Exception as e:
        logger.exception(f"检查更新文件完整性失败: {e}")
        return False


async def run_installer_and_exit(exe_path: str) -> bool:
    """
    运行 EXE 安装程序并退出应用程序

    Args:
        exe_path (str): exe 安装程序路径

    Returns:
        bool: 启动成功返回 True，否则返回 False
    """
    try:
        logger.info(f"准备运行安装程序: {exe_path}")

        # 验证安装程序存在
        if not get_path(exe_path).exists():
            logger.exception(f"安装程序不存在: {exe_path}")
            return False

        # 检查安装程序完整性
        if not check_exe_integrity(exe_path):
            logger.exception(f"安装程序不完整或已损坏: {exe_path}")
            return False

        # 使用 subprocess 启动安装程序
        import subprocess

        # Windows 系统
        if os.name == "nt":
            logger.info("Windows 系统，启动安装程序")

            # 使用 start 命令启动安装程序，不等待安装完成
            # 使用 DETACHED_PROCESS 标志创建新进程
            DETACHED_PROCESS = 0x00000008
            subprocess.Popen(
                [exe_path],
                creationflags=DETACHED_PROCESS,
                close_fds=True,
            )
        else:
            logger.warning("非 Windows 系统，不支持运行 EXE 安装程序")
            return False

        logger.info("安装程序已启动，准备退出应用程序")

        # 退出应用程序
        import sys

        sys.exit(0)

        return True
    except Exception as e:
        logger.exception(f"运行安装程序失败: {e}")
        return False


def parse_version(version_str: str) -> Tuple[list[int], list[str]]:
    """解析版本号为数字部分和预发布部分

    Args:
        version_str (str): 版本号字符串，如 "1.2.3" 或 "1.2.3-alpha.1"

    Returns:
        Tuple[list[int], list[str]]: 数字部分和预发布部分的元组
    """
    if "-" in version_str:
        main_version, pre_release = version_str.split("-", 1)
        pre_parts = pre_release.split(".")
    else:
        main_version = version_str
        pre_parts = []

    # 分割主版本号为数字部分
    main_parts = list(map(safe_int, main_version.split(".")))
    return main_parts, pre_parts


# ==================================================
# 更新工具函数
# ==================================================


async def test_source_latency(source_url: str, timeout: int = 5) -> float:
    """
    测试镜像源的延迟

    Args:
        source_url (str): 镜像源 URL
        timeout (int, optional): 超时时间（秒），默认5秒

    Returns:
        float: 延迟时间（毫秒），如果测试失败则返回无穷大
    """
    try:
        start_time = time.time()
        test_url = source_url

        async with aiohttp.ClientSession(
            timeout=aiohttp.ClientTimeout(total=timeout)
        ) as session:
            async with session.get(test_url, allow_redirects=True) as response:
                response.raise_for_status()
                latency = (time.time() - start_time) * 1000  # 转换为毫秒
                logger.debug(f"镜像源 {source_url} 延迟: {latency:.2f}ms")
                return latency
    except Exception as e:
        logger.debug(f"测试镜像源 {source_url} 延迟失败: {e}")
        return float("inf")


async def get_best_source() -> dict:
    """
    获取延迟最低的镜像源

    Returns:
        dict: 延迟最低的镜像源配置
    """
    try:
        if SYSTEM == "macos":
            return UPDATE_SOURCES[0]
        logger.info("开始测试所有镜像源的延迟...")

        # 并发测试所有镜像源
        tasks = []
        for source in UPDATE_SOURCES:
            task = test_source_latency(source["url"])
            tasks.append(task)

        # 等待所有测试完成
        latencies = await asyncio.gather(*tasks, return_exceptions=True)

        # 找到延迟最低的镜像源
        best_source = None
        best_latency = float("inf")

        for i, latency in enumerate(latencies):
            if isinstance(latency, Exception):
                logger.debug(f"镜像源 {UPDATE_SOURCES[i]['name']} 测试失败")
                continue

            if latency < best_latency:
                best_latency = latency
                best_source = UPDATE_SOURCES[i]

        if best_source:
            logger.info(
                f"选择延迟最低的镜像源: {best_source['name']} ({best_source['url']}) - 延迟: {best_latency:.2f}ms"
            )
        else:
            logger.warning("所有镜像源测试失败，使用默认源")
            best_source = UPDATE_SOURCES[0]  # 使用第一个源作为默认

        return best_source
    except Exception as e:
        logger.warning(f"获取最佳镜像源失败: {e}")
        return UPDATE_SOURCES[0]  # 返回默认源


def get_update_source_url() -> str:
    """
    获取更新源 URL（自动选择延迟最低的源）

    Returns:
        str: 更新源 URL，如果获取失败则返回默认值
    """
    try:
        # 异步获取最佳镜像源
        best_source = _run_async_func(get_best_source)
        if best_source:
            source_url = best_source["url"]
            logger.debug(f"获取更新源 URL 成功: {source_url}")
            return source_url
        else:
            return "https://github.com"
    except Exception as e:
        logger.warning(f"获取更新源 URL 失败: {e}")
        return "https://github.com"


async def get_update_source_url_async() -> str:
    """
    获取更新源 URL（自动选择延迟最低的源）- 异步版本

    Returns:
        str: 更新源 URL，如果获取失败则返回默认值
    """
    try:
        best_source = await get_best_source()
        if best_source:
            source_url = best_source["url"]
            logger.debug(f"获取更新源 URL 成功: {source_url}")
            return source_url
        else:
            return "https://github.com"
    except Exception as e:
        logger.warning(f"获取更新源 URL 失败: {e}")
        return "https://github.com"


def get_update_check_url() -> str:
    """
    获取更新检查 URL

    Returns:
        str: 更新检查 URL
    """
    source_url = get_update_source_url()
    repo_url = GITHUB_WEB

    # 构建完整的 GitHub URL
    github_raw_url = f"{repo_url}/raw/master/metadata.yaml"

    # 如果是默认源（GitHub），直接返回
    if source_url == "https://github.com":
        update_check_url = github_raw_url
    else:
        # 其他镜像源，在 GitHub URL 前添加镜像源 URL
        update_check_url = f"{source_url}/{github_raw_url}"

    logger.debug(f"生成更新检查 URL 成功: {update_check_url}")
    return update_check_url


async def get_update_check_url_async() -> str:
    """
    获取更新检查 URL - 异步版本

    Returns:
        str: 更新检查 URL
    """
    source_url = await get_update_source_url_async()
    repo_url = GITHUB_WEB

    # 构建完整的 GitHub URL
    github_raw_url = f"{repo_url}/raw/master/metadata.yaml"

    # 如果是默认源（GitHub），直接返回
    if source_url == "https://github.com":
        update_check_url = github_raw_url
    else:
        # 其他镜像源，在 GitHub URL 前添加镜像源 URL
        update_check_url = f"{source_url}/{github_raw_url}"

    logger.debug(f"生成更新检查 URL 成功: {update_check_url}")
    return update_check_url


async def get_metadata_info_async() -> dict | None:
    """
    异步获取 metadata.yaml 文件信息

    Returns:
        dict: metadata.yaml 文件的内容，如果读取失败则返回 None
    """
    repo_url = GITHUB_WEB
    github_raw_url = f"{repo_url}/raw/master/metadata.yaml"

    # 读取更新源设置
    update_source = readme_settings("update", "update_source")
    logger.debug(f"更新源设置: {update_source}")

    # 如果选择自动检测延迟（索引0），则测试所有镜像源
    if update_source == 0:
        if SYSTEM == "macos":
            logger.info("macOS 系统跳过镜像源延迟测试")
            sorted_sources = [(0.0, UPDATE_SOURCES[0])]
        else:
            logger.info("开始测试镜像源延迟以获取 metadata.yaml...")
            tasks = []
            for source in UPDATE_SOURCES:
                task = test_source_latency(source["url"])
                tasks.append(task)

            latencies = await asyncio.gather(*tasks, return_exceptions=True)

            sorted_sources = []
            for i, latency in enumerate(latencies):
                if isinstance(latency, Exception):
                    logger.debug(
                        f"镜像源 {UPDATE_SOURCES[i]['name']} 测试失败，延迟设为无穷大"
                    )
                    latency = float("inf")
                sorted_sources.append((latency, UPDATE_SOURCES[i]))

            sorted_sources.sort(key=lambda x: x[0])

        # 按延迟顺序尝试获取 metadata.yaml
        for latency, source in sorted_sources:
            source_url = source["url"]
            logger.debug(
                f"尝试使用镜像源 {source['name']} (延迟: {latency:.2f}ms) 获取 metadata.yaml"
            )

            # 构建更新检查 URL
            if source_url == "https://github.com":
                update_check_url = github_raw_url
            else:
                update_check_url = f"{source_url}/{github_raw_url}"

            logger.debug(f"从网络获取 metadata.yaml: {update_check_url}")

            try:
                # 设置较短的超时时间，避免卡住
                client_timeout = aiohttp.ClientTimeout(
                    total=10,  # 总超时 10 秒
                    connect=5,  # 连接超时 5 秒
                    sock_read=5,  # 读取超时 5 秒
                )
                async with aiohttp.ClientSession(timeout=client_timeout) as session:
                    async with session.get(update_check_url) as response:
                        response.raise_for_status()
                        content = await response.text()
                        metadata = yaml.safe_load(content)
                        logger.info(
                            f"成功使用镜像源 {source['name']} (延迟: {latency:.2f}ms) 读取 metadata.yaml 文件"
                        )
                        return metadata
            except Exception as e:
                logger.warning(
                    f"使用镜像源 {source['name']} 获取 metadata.yaml 失败: {e}"
                )
                continue

        # 所有镜像源都失败了
        logger.warning("所有镜像源都获取 metadata.yaml 文件失败")
        return None
    else:
        # 使用指定的更新源
        source_index = update_source - 1  # 转换为0-based索引
        if 0 <= source_index < len(UPDATE_SOURCES):
            source = UPDATE_SOURCES[source_index]
            source_url = source["url"]
            logger.debug(f"使用指定的更新源 {source['name']} 获取 metadata.yaml")

            # 构建更新检查 URL
            if source_url == "https://github.com":
                update_check_url = github_raw_url
            else:
                update_check_url = f"{source_url}/{github_raw_url}"

            logger.debug(f"从网络获取 metadata.yaml: {update_check_url}")

            try:
                # 设置较短的超时时间，避免卡住
                client_timeout = aiohttp.ClientTimeout(
                    total=10,  # 总超时 10 秒
                    connect=5,  # 连接超时 5 秒
                    sock_read=5,  # 读取超时 5 秒
                )
                async with aiohttp.ClientSession(timeout=client_timeout) as session:
                    async with session.get(update_check_url) as response:
                        response.raise_for_status()
                        content = await response.text()
                        metadata = yaml.safe_load(content)
                        logger.info(
                            f"成功使用指定的更新源 {source['name']} 读取 metadata.yaml 文件"
                        )
                        return metadata
            except Exception as e:
                logger.warning(
                    f"使用指定的更新源 {source['name']} 获取 metadata.yaml 失败: {e}"
                )
                return None
        else:
            logger.exception(f"无效的更新源索引: {source_index}")
            return None


def _render_update_filename(
    name_format: str,
    version: str,
    arch: str,
    system: str = SYSTEM,
    struct: str = STRUCT,
) -> str:
    file_name = name_format.replace("[system]", system)
    file_name = file_name.replace("[version]", version)
    file_name = file_name.replace("[arch]", arch)
    file_name = file_name.replace("[struct]", struct)
    return file_name


async def get_latest_version_async(channel: int | None = None) -> dict | None:
    """
    获取最新版本信息（异步版本）

    Args:
        channel (int, optional): 更新通道，默认为 None，此时会从设置中读取
        0: 稳定通道(release), 1: 测试通道(beta), 2: 发布预览通道

    Returns:
        dict: 包含版本号和版本号数字的字典，格式为 {"version": str, "version_no": int}
    """
    try:
        # 如果没有指定通道，从设置中读取
        if channel is None:
            channel = readme_settings("update", "update_channel")

        # 获取 metadata 信息
        metadata = await get_metadata_info_async()
        if not metadata:
            return None

        channel_name = CHANNEL_MAP.get(channel, "release")
        latest = metadata.get("latest", {})
        latest_no = metadata.get("latest_no", {})

        # 获取版本信息，如果通道不存在则使用稳定通道的版本
        version = latest.get(channel_name, latest.get("release", VERSION))
        version_no = latest_no.get(channel_name, latest_no.get("release", 0))

        # 如果版本号是Disable，返回当前版本，禁止该通道更新
        if version == "Disable":
            logger.debug(f"通道 {channel_name} 已禁用，返回当前版本")
            return {"version": VERSION, "version_no": 0}

        logger.debug(
            f"获取最新版本信息成功: 通道={channel_name}, 版本={version}, 版本号={version_no}"
        )
        return {"version": version, "version_no": version_no}
    except Exception as e:
        logger.warning(f"获取最新版本信息失败: {e}")
        return None


def get_latest_version(channel: int | None = None) -> dict | None:
    """
    获取最新版本信息（同步版本）

    Args:
        channel (int, optional): 更新通道，默认为 None，此时会从设置中读取
        0: 稳定通道(release), 1: 测试通道(beta), 2: 发布预览通道

    Returns:
        dict: 包含版本号和版本号数字的字典，格式为 {"version": str, "version_no": int}
    """
    return _run_async_func(get_latest_version_async, channel)


def compare_versions(current_version: str, latest_version: str) -> int:
    """
    比较版本号，支持语义化版本号格式，包括预发布版本

    Args:
        current_version (str): 当前版本号，格式为 "vX.X.X"、"vX.X.X.X" 或 "vX.X.X-alpha.1" 等
        latest_version (str): 最新版本号，格式为 "vX.X.X"、"vX.X.X.X" 或 "vX.X.X-alpha.1" 等

    Returns:
        int: 1 表示有新版本，0 表示版本相同，-1 表示比较失败
    """
    try:
        # 检查版本号是否为空
        if not current_version or not latest_version:
            logger.exception(
                f"比较版本号失败: 版本号为空，current={current_version}, latest={latest_version}"
            )
            return -1

        # 移除版本号前缀 "v"
        current = current_version.lstrip("v")
        latest = latest_version.lstrip("v")

        # 解析两个版本号
        current_main, current_pre = parse_version(current)
        latest_main, latest_pre = parse_version(latest)

        # 比较主版本号部分
        for i in range(max(len(current_main), len(latest_main))):
            current_part = current_main[i] if i < len(current_main) else 0
            latest_part = latest_main[i] if i < len(latest_main) else 0

            if latest_part > current_part:
                return 1
            elif latest_part < current_part:
                return -1

        # 主版本号相同，比较预发布版本
        # 规则：没有预发布标识符的版本 > 有预发布标识符的版本
        if not current_pre and not latest_pre:
            return 0  # 两个都是正式版本，版本号相同
        elif not current_pre:
            return -1  # 当前是正式版本，最新是预发布版本，当前版本更新
        elif not latest_pre:
            return 1  # 当前是预发布版本，最新是正式版本，有新版本

        # 两个都是预发布版本，比较预发布部分
        for i in range(max(len(current_pre), len(latest_pre))):
            if i >= len(current_pre):
                return 1  # 当前预发布部分更短，最新版本更新
            if i >= len(latest_pre):
                return -1  # 最新预发布部分更短，当前版本更新

            current_pre_part = current_pre[i]
            latest_pre_part = latest_pre[i]

            # 尝试转换为整数比较
            try:
                current_pre_int = int(current_pre_part)
                latest_pre_int = int(latest_pre_part)
                if latest_pre_int > current_pre_int:
                    return 1
                elif latest_pre_int < current_pre_int:
                    return -1
            except ValueError:
                # 不是数字，按字典序比较
                if latest_pre_part > current_pre_part:
                    return 1
                elif latest_pre_part < current_pre_part:
                    return -1

        return 0  # 版本号完全相同
    except Exception as e:
        logger.exception(f"比较版本号失败: {e}")
        return -1


def get_update_download_url(
    version: str, system: str = SYSTEM, arch: str = ARCH, struct: str = STRUCT
) -> str:
    """
    获取更新下载 URL

    Args:
        version (str): 版本号，格式为 "vX.X.X.X"
        system (str, optional): 系统，默认为当前系统
        arch (str, optional): 架构，默认为当前架构
        struct (str, optional): 结构，默认为当前结构

    Returns:
        str: 更新下载 URL
    """
    try:
        # 获取更新源 URL
        source_url = get_update_source_url()

        # 获取 GitHub 仓库 URL
        repo_url = GITHUB_WEB

        # 从 metadata.yaml 获取文件名格式
        name_format = "SecRandom-[system]-[version]-[arch]-[struct].zip"

        # 替换占位符生成实际文件名
        file_name = name_format.replace("[system]", system)
        file_name = file_name.replace("[version]", version)
        file_name = file_name.replace("[arch]", arch)
        file_name = file_name.replace("[struct]", struct)

        # 构建完整的 GitHub 下载 URL
        github_download_url = f"{repo_url}/releases/download/{version}/{file_name}"

        # 如果是默认源（GitHub），直接返回
        if source_url == "https://github.com":
            download_url = github_download_url
        else:
            # 其他镜像源，在 GitHub URL 前添加镜像源 URL
            download_url = f"{source_url}/{github_download_url}"

        logger.debug(f"生成更新下载 URL 成功: {download_url}")
        return download_url
    except Exception as e:
        logger.warning(f"生成更新下载 URL 失败: {e}")
        # 返回默认的 GitHub 下载 URL
        return f"https://github.com/SECTL/SecRandom/releases/download/{version}/SecRandom-{system}-{version}-{arch}-{struct}.zip"


async def get_update_download_url_async(
    version: str, system: str = SYSTEM, arch: str = ARCH, struct: str = STRUCT
) -> str:
    """
    获取更新下载 URL - 异步版本

    Args:
        version (str): 版本号，格式为 "vX.X.X.X"
        system (str, optional): 系统，默认为当前系统
        arch (str, optional): 架构，默认为当前架构
        struct (str, optional): 结构，默认为当前结构

    Returns:
        str: 更新下载 URL
    """
    try:
        # 获取更新源 URL
        source_url = await get_update_source_url_async()

        # 获取 GitHub 仓库 URL
        repo_url = GITHUB_WEB

        # 从 metadata.yaml 获取文件名格式
        name_format = "SecRandom-[system]-[version]-[arch]-[struct].zip"

        # 替换占位符生成实际文件名
        file_name = name_format.replace("[system]", system)
        file_name = file_name.replace("[version]", version)
        file_name = file_name.replace("[arch]", arch)
        file_name = file_name.replace("[struct]", struct)

        # 构建完整的 GitHub 下载 URL
        github_download_url = f"{repo_url}/releases/download/{version}/{file_name}"

        # 如果是默认源（GitHub），直接返回
        if source_url == "https://github.com":
            download_url = github_download_url
        else:
            # 其他镜像源，在 GitHub URL 前添加镜像源 URL
            download_url = f"{source_url}/{github_download_url}"

        logger.debug(f"生成更新下载 URL 成功: {download_url}")
        return download_url
    except Exception as e:
        logger.warning(f"生成更新下载 URL 失败: {e}")
        # 返回默认的 GitHub 下载 URL
        return f"https://github.com/SECTL/SecRandom/releases/download/{version}/SecRandom-{system}-{version}-{arch}-{struct}.zip"


async def download_update_async(
    version: str,
    progress_callback: Optional[Callable] = None,
    timeout: int = 300,
    cancel_check: Optional[Callable[[], bool]] = None,
) -> Optional[str]:
    """
    异步下载更新文件

    Args:
        version (str): 版本号，格式为 "vX.X.X"
        progress_callback (Optional[Callable]): 进度回调函数，接收已下载字节数和总字节数
        timeout (int, optional): 下载超时时间（秒），默认300秒
        cancel_check (Optional[Callable[[], bool]]): 取消检查函数，返回True表示取消下载

    Returns:
        Optional[str]: 下载完成的文件路径，如果下载失败则返回 None
    """
    metadata = await get_metadata_info_async()

    name_format = None
    if isinstance(metadata, dict):
        name_format = metadata.get("name_format")
    if not isinstance(name_format, str) or not name_format.strip():
        name_format = "SecRandom-setup-[version]-[arch].exe"

    # 替换占位符生成实际文件名
    file_name = _render_update_filename(
        name_format,
        version=version,
        arch=metadata.get("arch", ARCH) if isinstance(metadata, dict) else ARCH,
        system=metadata.get("system", SYSTEM) if isinstance(metadata, dict) else SYSTEM,
        struct=metadata.get("struct", STRUCT) if isinstance(metadata, dict) else STRUCT,
    )

    # 确定下载保存路径
    download_dir = get_data_path("downloads")
    ensure_dir(download_dir)
    file_path = download_dir / file_name

    # 按优先级排序的镜像源列表
    sources = sorted(UPDATE_SOURCES, key=lambda x: x["priority"])
    repo_url = GITHUB_WEB
    github_download_url = f"{repo_url}/releases/download/{version}/{file_name}"

    # 依次尝试每个镜像源
    for source in sources:
        try:
            source_url = source["url"]
            logger.debug(f"尝试使用镜像源 {source['name']} 下载更新文件")

            # 构建下载 URL
            if source_url == "https://github.com":
                download_url = github_download_url
            else:
                download_url = f"{source_url}/{github_download_url}"

            logger.debug(f"开始下载更新文件: {download_url}")

            # 配置客户端超时设置
            client_timeout = aiohttp.ClientTimeout(
                total=timeout,
                connect=30,
                sock_read=60,
                sock_connect=30,
            )

            # 发送异步请求
            async with aiohttp.ClientSession(timeout=client_timeout) as session:
                async with session.get(
                    download_url,
                    allow_redirects=True,
                    headers={"User-Agent": "SecRandom Update Client"},
                ) as response:
                    response.raise_for_status()

                    # 获取文件总大小
                    total_size = int(response.headers.get("Content-Length", 0))
                    downloaded_size = 0
                    last_progress_time = time.time()

                    # 开始下载文件
                    with open(file_path, "wb") as f:
                        # 使用更大的块大小提高下载速度
                        async for chunk in response.content.iter_chunked(32768):
                            # 检查是否取消下载
                            if cancel_check and cancel_check():
                                logger.info("下载已被用户取消")
                                # 删除已下载的部分文件
                                if file_path.exists():
                                    file_path.unlink()
                                return None

                            if not chunk:
                                break

                            # 写入文件
                            f.write(chunk)
                            downloaded_size += len(chunk)
                            last_progress_time = time.time()

                            # 调用进度回调函数
                            if progress_callback:
                                progress_callback(downloaded_size, total_size)

            # 验证下载的文件完整性
            if not check_update_file_integrity(str(file_path)):
                logger.warning(f"下载的文件不完整或已损坏: {file_path}")
                # 删除损坏的文件
                if file_path.exists():
                    try:
                        file_path.unlink()
                        logger.info(f"已删除损坏的文件: {file_path}")
                    except Exception as unlink_error:
                        logger.warning(f"删除损坏文件失败: {unlink_error}")
                # 继续尝试下一个镜像源
                continue

            logger.debug(f"更新文件下载成功: {file_path}")
            return str(file_path)
        except Exception as e:
            logger.warning(f"使用镜像源 {source['name']} 下载更新文件失败: {e}")
            # 删除部分下载的文件
            if file_path.exists():
                try:
                    file_path.unlink()
                    logger.info(f"已删除部分下载文件: {file_path}")
                except Exception as unlink_error:
                    logger.warning(f"删除部分下载文件失败: {unlink_error}")
            continue

    # 所有镜像源都失败了
    logger.warning("所有镜像源都下载更新文件失败")
    return None


def download_update(
    version: str,
    arch: str = ARCH,
    progress_callback: Optional[Callable] = None,
    timeout: int = 300,
    cancel_check: Optional[Callable[[], bool]] = None,
) -> Optional[str]:
    """
    下载更新文件（同步版本）

    Args:
        version (str): 版本号，格式为 "vX.X.X.X"
        system (str, optional): 系统，默认为当前系统
        arch (str, optional): 架构，默认为当前架构
        struct (str, optional): 结构，默认为当前结构
        progress_callback (Optional[Callable]): 进度回调函数，接收已下载字节数和总字节数
        timeout (int, optional): 下载超时时间（秒），默认300秒
        cancel_check (Optional[Callable[[], bool]]): 取消检查函数，返回True表示取消下载

    Returns:
        Optional[str]: 下载完成的文件路径，如果下载失败则返回 None
    """
    return _run_async_func(
        download_update_async,
        version,
        progress_callback,
        timeout,
        cancel_check,
    )


async def install_update_async(file_path: str) -> bool:
    """
    异步安装更新文件

    Args:
        file_path (str): 更新文件的路径

    Returns:
        bool: 安装成功返回 True，否则返回 False
    """
    try:
        logger.debug(f"开始安装更新文件: {file_path}")

        # 验证更新文件存在
        if not get_path(file_path).exists():
            logger.exception(f"更新文件不存在: {file_path}")
            return False

        # 检查文件类型
        file_ext = get_path(file_path).suffix.lower()

        # 只支持 exe 安装程序
        if file_ext != ".exe":
            logger.exception(f"不支持的更新文件类型: {file_ext}，仅支持 EXE 安装程序")
            return False

        # 检查安装程序完整性
        if not check_exe_integrity(file_path):
            logger.exception(f"安装程序不完整或已损坏: {file_path}")
            # 删除损坏的文件
            try:
                get_path(file_path).unlink(missing_ok=True)
                logger.info(f"已删除损坏的安装程序: {file_path}")
            except Exception as e:
                logger.exception(f"删除损坏的安装程序失败: {e}")
            return False

        # 运行安装程序并退出应用程序
        logger.info("准备运行 EXE 安装程序")
        return await run_installer_and_exit(file_path)
    except Exception as e:
        logger.exception(f"安装更新文件失败: {e}")
        return False


def install_update(file_path: str) -> bool:
    """
    安装更新文件（同步版本）

    Args:
        file_path (str): 更新文件的路径

    Returns:
        bool: 安装成功返回 True，否则返回 False
    """
    return _run_async_func(install_update_async, file_path)


# ==================================================
# 全局更新状态管理器
# ==================================================
class UpdateStatusManager(QObject):
    """全局更新状态管理器，用于在更新页面创建前后同步状态"""

    status_changed = Signal(str)  # 状态变化信号
    download_progress_updated = Signal(int, str)  # 下载进度更新信号
    ui_state_changed = Signal(dict)  # UI状态变化信号

    def __init__(self):
        super().__init__()
        self.status = (
            "idle"  # idle, checking, new_version, downloading, completed, failed
        )
        self.latest_version = None
        self.download_progress = 0
        self.download_speed = ""
        self.download_total = ""
        self.download_file_path = None
        self.error_message = None
        self.download_cancelled = False  # 下载取消标志位

        # UI状态
        self.download_install_button_visible = False
        self.download_install_button_enabled = True
        self.check_update_button_enabled = True
        self.cancel_update_button_visible = False
        self.cancel_update_button_enabled = True
        self.download_progress_visible = False
        self.download_info_label_visible = False
        self.download_info_label_text = ""
        self.indeterminate_ring_visible = False
        self.status_label_text = ""

    def cancel_download(self):
        """取消下载"""
        self.download_cancelled = True

    def reset_cancel_flag(self):
        """重置取消标志位"""
        self.download_cancelled = False

    def set_checking(self):
        """设置正在检查更新的状态"""
        self.status = "checking"
        self.latest_version = None
        self.download_progress = 0
        self.download_speed = ""
        self.download_total = ""
        self.download_file_path = None
        self.error_message = None

        # 更新UI状态
        self.indeterminate_ring_visible = True
        self.check_update_button_enabled = False
        self.download_install_button_visible = False
        self.status_label_text = ""

        self.status_changed.emit("checking")
        self._emit_ui_state()

    def set_new_version(self, version):
        """设置发现新版本的状态"""
        self.status = "new_version"
        self.latest_version = version

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_install_button_visible = True
        self.download_install_button_enabled = True
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("new_version")
        self._emit_ui_state()

    def set_downloading(self):
        """设置正在下载的状态"""
        self.status = "downloading"
        self.download_cancelled = False  # 重置取消标志位

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_progress_visible = True
        self.download_info_label_visible = True
        self.cancel_update_button_visible = True
        self.cancel_update_button_enabled = True
        self.download_install_button_enabled = False
        self.check_update_button_enabled = False
        self.status_label_text = ""

        self.status_changed.emit("downloading")
        self._emit_ui_state()

    def update_download_progress(self, progress, speed):
        """更新下载进度"""
        self.download_progress = progress
        self.download_speed = speed
        self.download_info_label_text = f"{speed}"
        self.download_progress_updated.emit(progress, speed)
        self._emit_ui_state()

    def set_download_complete(self, file_path):
        """设置下载完成的状态"""
        self.status = "completed"
        self.download_file_path = file_path

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_progress_visible = False
        self.download_info_label_visible = True
        self.cancel_update_button_visible = False
        self.download_install_button_visible = True
        self.download_install_button_enabled = True
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("completed")
        self._emit_ui_state()

    def set_download_complete_with_size(self, file_path, file_size):
        """设置下载完成的状态（包含文件大小）"""
        self.status = "completed"
        self.download_file_path = file_path
        self.download_info_label_text = file_size

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_progress_visible = False
        self.download_info_label_visible = True
        self.cancel_update_button_visible = False
        self.download_install_button_visible = True
        self.download_install_button_enabled = True
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("completed")
        self._emit_ui_state()

    def set_download_failed(self):
        """设置下载失败的状态"""
        self.status = "failed"

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_progress_visible = False
        self.download_info_label_visible = False
        self.cancel_update_button_visible = False
        self.download_install_button_enabled = True
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("failed")
        self._emit_ui_state()

    def set_check_failed(self):
        """设置检查失败的状态"""
        self.status = "failed"

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_install_button_visible = False
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("failed")
        self._emit_ui_state()

    def set_latest_version(self):
        """设置已是最新版本的状态"""
        self.status = "idle"

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_install_button_visible = False
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("idle")
        self._emit_ui_state()

    def set_download_cancelled(self):
        """设置下载被取消的状态"""
        self.status = "new_version"  # 恢复到新版本状态，而不是idle
        self.download_cancelled = False  # 重置取消标志位

        # 更新UI状态
        self.indeterminate_ring_visible = False
        self.download_progress_visible = False
        self.download_info_label_visible = False
        self.cancel_update_button_visible = False
        self.download_install_button_visible = True  # 显示下载按钮
        self.download_install_button_enabled = True
        self.check_update_button_enabled = True
        self.status_label_text = ""

        self.status_changed.emit("new_version")
        self._emit_ui_state()

    def _emit_ui_state(self):
        """发送UI状态信号"""
        ui_state = {
            "download_install_button_visible": self.download_install_button_visible,
            "download_install_button_enabled": self.download_install_button_enabled,
            "check_update_button_enabled": self.check_update_button_enabled,
            "cancel_update_button_visible": self.cancel_update_button_visible,
            "cancel_update_button_enabled": self.cancel_update_button_enabled,
            "download_progress_visible": self.download_progress_visible,
            "download_info_label_visible": self.download_info_label_visible,
            "download_info_label_text": self.download_info_label_text,
            "indeterminate_ring_visible": self.indeterminate_ring_visible,
            "status_label_text": self.status_label_text,
        }
        self.ui_state_changed.emit(ui_state)


# 全局更新状态管理器实例
update_status_manager = UpdateStatusManager()

# 全局更新检查线程实例
update_check_thread = None


# ==================================================
# 启动时自动更新检查
# ==================================================
class UpdateCheckThread(QThread):
    """更新检查线程，用于在后台执行更新检查"""

    def __init__(self, settings_window=None):
        super().__init__()
        self.settings_window = settings_window

    def run(self):
        """执行更新检查"""
        loop = None
        try:
            from app.tools.config import send_system_notification
            from app.Language.obtain_language import get_content_name_async
            from PySide6.QtCore import QDateTime

            # 创建新的事件循环
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)

            # 辅助函数：安全地调用更新页面的方法
            def safe_call_update_interface(method_name, *args):
                """安全地调用更新页面的方法"""
                if self.settings_window and hasattr(
                    self.settings_window, "updateInterface"
                ):
                    update_iface = self.settings_window.updateInterface
                    if hasattr(update_iface, method_name):
                        method = getattr(update_iface, method_name)
                        # 直接调用方法，方法内部已经使用 QMetaObject.invokeMethod 确保在主线程执行
                        method(*args)

            # 读取自动更新模式设置
            auto_update_mode = readme_settings_async("update", "auto_update_mode")
            logger.debug(f"自动更新模式: {auto_update_mode}")

            # 如果是模式0（不自动检查更新），直接返回
            if auto_update_mode == 0:
                logger.debug("自动更新模式为0，不执行更新检查")
                return

            # 通知更新页面开始检查
            safe_call_update_interface("set_checking_status")
            # 更新全局状态
            update_status_manager.set_checking()

            # 获取最新版本信息（使用异步方式）
            logger.debug("开始检查更新")
            latest_version_info = loop.run_until_complete(get_latest_version_async())

            if not latest_version_info:
                logger.debug("获取最新版本信息失败")
                # 通知更新页面检查失败
                safe_call_update_interface("set_check_failed")
                # 更新全局状态
                update_status_manager.set_check_failed()
                return

            latest_version = latest_version_info["version"]
            latest_version_no = latest_version_info["version_no"]

            # 比较版本号
            compare_result = compare_versions(VERSION, latest_version)

            # 获取下载文件夹路径
            download_dir = get_data_path("downloads")
            ensure_dir(download_dir)

            # 构建预期的文件名
            expected_filename = DEFAULT_NAME_FORMAT
            expected_filename = expected_filename.replace("[version]", latest_version)
            expected_filename = expected_filename.replace("[system]", SYSTEM)
            expected_filename = expected_filename.replace("[arch]", ARCH)
            expected_filename = expected_filename.replace("[struct]", STRUCT)
            expected_file_path = download_dir / expected_filename

            # 检查是否有已下载的更新文件（模式3：自动安装）
            if (
                expected_file_path.exists()
                and compare_result == 1
                and auto_update_mode == 3
            ):
                logger.debug(
                    f"发现已下载的更新文件，开始验证完整性: {expected_file_path}"
                )
                # 验证文件完整性
                file_integrity_ok = check_update_file_integrity(str(expected_file_path))

                if file_integrity_ok:
                    # 文件完整，可以直接安装
                    logger.debug(
                        f"文件完整性验证通过，开始自动安装: {expected_file_path}"
                    )
                    # 自动安装更新
                    success = install_update(str(expected_file_path))
                    if success:
                        logger.debug("自动安装更新成功")
                    else:
                        logger.exception("自动安装更新失败")
                    return
                else:
                    # 文件损坏，需要重新下载
                    logger.warning(
                        f"已下载的文件损坏，将重新下载: {expected_file_path}"
                    )
                    # 删除损坏的文件
                    try:
                        expected_file_path.unlink()
                        logger.debug(f"已删除损坏的文件: {expected_file_path}")
                    except Exception as e:
                        logger.exception(f"删除损坏文件失败: {e}")
                    # 继续执行下载流程

            if compare_result == 1:
                # 有新版本
                logger.debug(f"发现新版本: {latest_version}")

                # 通知更新页面发现新版本
                safe_call_update_interface("set_new_version_available", latest_version)
                # 更新全局状态
                update_status_manager.set_new_version(latest_version)

                # 发送系统通知
                title = get_content_name_async("update", "update_notification_title")
                content = get_content_name_async(
                    "update", "update_notification_content"
                ).format(version=latest_version)
                send_system_notification(
                    title, content, url="https://secrandom.sectl.top/download.html"
                )

                # 如果是模式2或3，自动下载更新
                if auto_update_mode in [2, 3]:
                    logger.debug(f"自动更新模式为{auto_update_mode}，开始自动下载更新")

                    # 检查文件是否已存在
                    if expected_file_path.exists():
                        logger.debug(f"检测到已下载的更新文件: {expected_file_path}")
                        # 验证文件完整性
                        file_integrity_ok = check_update_file_integrity(
                            str(expected_file_path)
                        )

                        if file_integrity_ok:
                            # 文件完整，可以直接使用
                            logger.debug(f"文件完整性验证通过: {expected_file_path}")

                            # 获取文件大小
                            file_size = expected_file_path.stat().st_size

                            def format_size(size_bytes):
                                """格式化文件大小"""
                                if size_bytes < 1024:
                                    return f"{size_bytes} B"
                                elif size_bytes < 1024 * 1024:
                                    return f"{size_bytes / 1024:.1f} KB"
                                else:
                                    return f"{size_bytes / (1024 * 1024):.1f} MB"

                            file_size_str = format_size(file_size)

                            # 通知更新页面下载完成，并传递文件大小
                            safe_call_update_interface(
                                "set_download_complete_with_size",
                                str(expected_file_path),
                                file_size_str,
                            )
                            return
                        else:
                            # 文件损坏，需要重新下载
                            logger.warning(
                                f"已下载的文件损坏，将重新下载: {expected_file_path}"
                            )
                            # 删除损坏的文件
                            try:
                                expected_file_path.unlink()
                                logger.debug(f"已删除损坏的文件: {expected_file_path}")
                            except Exception as e:
                                logger.error(f"删除损坏文件失败: {e}")
                            # 继续执行下载流程

                    # 通知更新页面开始下载
                    safe_call_update_interface("set_downloading_status")
                    # 更新全局状态
                    update_status_manager.set_downloading()

                    # 定义进度回调函数
                    def progress_callback(downloaded: int, total: int):
                        if total > 0:
                            progress = int((downloaded / total) * 100)
                            # 计算下载速度
                            current_time = (
                                QDateTime.currentDateTime().toMSecsSinceEpoch()
                            )
                            elapsed = (current_time - start_time) / 1000  # 秒
                            speed = downloaded / elapsed if elapsed > 0 else 0
                            speed_str = f"{speed / 1024 / 1024:.2f} MB/s"
                            total_str = f"{total / 1024 / 1024:.2f} MB"
                            downloaded_str = f"{downloaded / 1024 / 1024:.2f} MB"
                            # 通知更新页面更新进度，包含已下载大小和进度百分比
                            safe_call_update_interface(
                                "update_download_progress",
                                progress,
                                f"{speed_str}/s | {downloaded_str} / {total_str} ({progress}%)",
                            )
                            # 更新全局状态
                            update_status_manager.update_download_progress(
                                progress,
                                f"{speed_str}/s | {downloaded_str} / {total_str} ({progress}%)",
                            )

                    start_time = QDateTime.currentDateTime().toMSecsSinceEpoch()

                    # 自动下载更新（使用异步方式）
                    file_path = loop.run_until_complete(
                        download_update_async(
                            latest_version,
                            progress_callback=progress_callback,
                            cancel_check=lambda: update_status_manager.download_cancelled,
                        )
                    )
                    if file_path:
                        logger.debug(f"自动下载更新成功: {file_path}")

                        # 获取文件大小
                        file_size = get_path(file_path).stat().st_size

                        def format_size(size_bytes):
                            """格式化文件大小"""
                            if size_bytes < 1024:
                                return f"{size_bytes} B"
                            elif size_bytes < 1024 * 1024:
                                return f"{size_bytes / 1024:.1f} KB"
                            else:
                                return f"{size_bytes / (1024 * 1024):.1f} MB"

                        file_size_str = format_size(file_size)

                        # 通知更新页面下载完成，并传递文件大小
                        safe_call_update_interface(
                            "set_download_complete_with_size", file_path, file_size_str
                        )
                        # 更新全局状态
                        update_status_manager.set_download_complete_with_size(
                            file_path, file_size_str
                        )
                    elif update_status_manager.download_cancelled:
                        # 下载被取消
                        logger.info("自动下载更新已被用户取消")
                        # 通知更新页面下载被取消
                        safe_call_update_interface("set_download_cancelled")
                        # 更新全局状态
                        update_status_manager.set_download_cancelled()
                    else:
                        logger.warning("自动下载更新失败")
                        # 通知更新页面下载失败
                        safe_call_update_interface("set_download_failed")
                        # 更新全局状态
                        update_status_manager.set_download_failed()
            elif compare_result == 0:
                # 当前是最新版本
                logger.debug("当前已是最新版本")
                # 通知更新页面已是最新版本
                safe_call_update_interface("set_latest_version")
            else:
                # 版本比较失败
                logger.debug("版本比较失败")
                # 通知更新页面检查失败
                safe_call_update_interface("set_check_failed")

            # 更新上次检查时间
            safe_call_update_interface("update_last_check_time")
        except Exception as e:
            logger.warning(f"启动时检查更新失败: {e}")
            # 通知更新页面检查失败
            safe_call_update_interface("set_check_failed")
        finally:
            # 关闭事件循环
            if loop and not loop.is_closed():
                loop.close()


def check_for_updates_on_startup(settings_window=None):
    """
    应用启动时检查更新
    根据自动更新模式设置执行相应的更新操作
    异步执行，避免阻塞应用启动进程

    Args:
        settings_window: 设置窗口实例，用于通知更新页面状态变化
    """
    global update_check_thread
    # 创建并启动更新检查线程
    update_check_thread = UpdateCheckThread(settings_window)
    update_check_thread.start()
    return update_check_thread
