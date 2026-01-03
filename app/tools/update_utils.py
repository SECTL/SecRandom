# ==================================================
# 导入模块
# ==================================================
import asyncio
import shutil
import subprocess
import sys
from tempfile import NamedTemporaryFile
import time
from typing import Any, Tuple, Callable, Optional
import zipfile
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
        logger.error(f"运行异步函数失败: {e}")
        return None


def check_zip_integrity(zip_path: str) -> bool:
    """检查ZIP文件的完整性

    Args:
        zip_path (str): zip文件路径

    Returns:
        bool: 文件完整返回True，否则返回False
    """
    try:
        logger.debug(f"检查ZIP文件完整性: {zip_path}")

        # 检查文件是否存在
        if not Path(zip_path).exists():
            logger.error(f"ZIP文件不存在: {zip_path}")
            return False

        # 检查文件大小
        file_size = Path(zip_path).stat().st_size
        if file_size == 0:
            logger.error(f"ZIP文件大小为0: {zip_path}")
            return False

        # 尝试打开并测试ZIP文件
        with zipfile.ZipFile(zip_path, "r") as zip_ref:
            # 测试ZIP文件的完整性
            bad_file = zip_ref.testzip()
            if bad_file is not None:
                logger.error(f"ZIP文件损坏，损坏的文件: {bad_file}")
                return False

            # 检查ZIP文件是否为空
            file_list = zip_ref.namelist()
            if not file_list:
                logger.error(f"ZIP文件为空: {zip_path}")
                return False

        logger.debug(f"ZIP文件完整性检查通过: {zip_path}")
        return True
    except zipfile.BadZipFile as e:
        logger.error(f"ZIP文件格式错误: {e}")
        return False
    except Exception as e:
        logger.error(f"检查ZIP文件完整性失败: {e}")
        return False


def check_deb_integrity(deb_path: str) -> bool:
    """检查DEB包的完整性

    Args:
        deb_path (str): deb文件路径

    Returns:
        bool: 文件完整返回True，否则返回False
    """
    try:
        logger.debug(f"检查DEB包完整性: {deb_path}")

        # 检查文件是否存在
        if not Path(deb_path).exists():
            logger.error(f"DEB文件不存在: {deb_path}")
            return False

        # 检查文件大小
        file_size = Path(deb_path).stat().st_size
        if file_size == 0:
            logger.error(f"DEB文件大小为0: {deb_path}")
            return False

        # DEB包实际上是ar归档格式
        # 检查ar文件头
        with open(deb_path, "rb") as f:
            # 检查ar文件签名
            magic = f.read(8)
            if not magic.startswith(b"!<arch>"):
                logger.error(f"DEB文件格式错误，不是有效的ar归档: {deb_path}")
                return False

            # 读取ar文件内容
            f.seek(0)
            content = f.read()

            # 检查是否包含必要的文件（debian-binary, control.tar.gz, data.tar.gz）
            if b"debian-binary" not in content:
                logger.error(f"DEB文件缺少debian-binary: {deb_path}")
                return False

            if b"control.tar" not in content:
                logger.error(f"DEB文件缺少control.tar: {deb_path}")
                return False

            if b"data.tar" not in content:
                logger.error(f"DEB文件缺少data.tar: {deb_path}")
                return False

        logger.debug(f"DEB包完整性检查通过: {deb_path}")
        return True
    except Exception as e:
        logger.error(f"检查DEB包完整性失败: {e}")
        return False


def check_update_file_integrity(file_path: str, file_type: str = None) -> bool:
    """检查更新文件的完整性（自动检测文件类型）

    Args:
        file_path (str): 更新文件路径
        file_type (str, optional): 文件类型，如果不指定则自动检测

    Returns:
        bool: 文件完整返回True，否则返回False
    """
    try:
        # 自动检测文件类型
        if file_type is None:
            file_ext = Path(file_path).suffix.lower()
            if file_ext == ".zip":
                file_type = "zip"
            elif file_ext == ".deb":
                file_type = "deb"
            else:
                logger.error(f"不支持的更新文件类型: {file_ext}")
                return False

        # 根据文件类型调用相应的检查函数
        if file_type == "zip":
            return check_zip_integrity(file_path)
        elif file_type == "deb":
            return check_deb_integrity(file_path)
        else:
            logger.error(f"不支持的文件类型: {file_type}")
            return False
    except Exception as e:
        logger.error(f"检查更新文件完整性失败: {e}")
        return False


def extract_zip(zip_path: str, target_dir: str | Path, overwrite: bool = True) -> bool:
    """解压zip文件到指定目录

    Args:
        zip_path (str): zip文件路径
        target_dir (str | Path): 目标目录
        overwrite (bool, optional): 是否覆盖现有文件. Defaults to True.

    Returns:
        bool: 解压成功返回True，否则返回False
    """
    try:
        logger.debug(f"开始解压文件: {zip_path} 到 {target_dir}")

        # 确保目标目录存在
        ensure_dir(target_dir)

        # 打开zip文件
        with zipfile.ZipFile(zip_path, "r") as zip_ref:
            # 获取所有文件列表
            file_list = zip_ref.namelist()
            # 创建已解压文件列表
            extracted_files = set()

            for file in file_list:
                # 构建目标文件路径
                target_file = Path(target_dir) / file

                # 确保父目录存在
                ensure_dir(target_file.parent)

                # 如果文件已存在且不允许覆盖，跳过
                if target_file.exists() and not overwrite:
                    logger.debug(f"文件已存在，跳过: {target_file}")
                    continue

                # 解压文件
                zip_ref.extract(file, target_dir)
                # 添加到已解压文件列表
                extracted_files.add(file)

        logger.debug(f"成功解压以下文件: {extracted_files}")

        logger.debug(f"文件解压完成: {zip_path} 到 {target_dir}")
        return True
    except Exception as e:
        logger.error(f"解压文件失败: {e}")
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
        logger.error(f"获取最佳镜像源失败: {e}")
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
        logger.error(f"获取更新源 URL 失败: {e}")
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
        logger.error(f"获取更新源 URL 失败: {e}")
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
        # 测试所有镜像源的延迟
        logger.info("开始测试镜像源延迟以获取 metadata.yaml...")
        tasks = []
        for source in UPDATE_SOURCES:
            task = test_source_latency(source["url"])
            tasks.append(task)

        # 等待所有测试完成
        latencies = await asyncio.gather(*tasks, return_exceptions=True)

        # 按延迟排序镜像源
        sorted_sources = []
        for i, latency in enumerate(latencies):
            if isinstance(latency, Exception):
                logger.debug(
                    f"镜像源 {UPDATE_SOURCES[i]['name']} 测试失败，延迟设为无穷大"
                )
                latency = float("inf")
            sorted_sources.append((latency, UPDATE_SOURCES[i]))

        # 按延迟升序排序
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
        logger.error("所有镜像源都获取 metadata.yaml 文件失败")
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
                logger.error(
                    f"使用指定的更新源 {source['name']} 获取 metadata.yaml 失败: {e}"
                )
                return None
        else:
            logger.error(f"无效的更新源索引: {source_index}")
            return None


def get_metadata_info() -> dict | None:
    """
    获取 metadata.yaml 文件信息（同步版本）

    Returns:
        dict: metadata.yaml 文件的内容，如果读取失败则返回 None
    """
    return _run_async_func(get_metadata_info_async)


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
        version = latest.get(channel_name, latest.get("release", SPECIAL_VERSION))
        version_no = latest_no.get(channel_name, latest_no.get("release", 0))

        # 如果版本号是Disable，返回当前版本，禁止该通道更新
        if version == "Disable":
            logger.debug(f"通道 {channel_name} 已禁用，返回当前版本")
            return {"version": SPECIAL_VERSION, "version_no": 0}

        logger.debug(
            f"获取最新版本信息成功: 通道={channel_name}, 版本={version}, 版本号={version_no}"
        )
        return {"version": version, "version_no": version_no}
    except Exception as e:
        logger.error(f"获取最新版本信息失败: {e}")
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
        int: 1 表示有新版本，0 表示版本相同，-1 表示当前版本更新，-2 表示比较失败
    """
    try:
        # 检查版本号是否为空
        if not current_version or not latest_version:
            logger.error(
                f"比较版本号失败: 版本号为空，current={current_version}, latest={latest_version}"
            )
            return -2

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
        logger.error(f"比较版本号失败: {e}")
        return -2


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
        logger.error(f"生成更新下载 URL 失败: {e}")
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
        logger.error(f"生成更新下载 URL 失败: {e}")
        # 返回默认的 GitHub 下载 URL
        return f"https://github.com/SECTL/SecRandom/releases/download/{version}/SecRandom-{system}-{version}-{arch}-{struct}.zip"


async def download_update_async(
    version: str,
    system: str = SYSTEM,
    arch: str = ARCH,
    struct: str = STRUCT,
    progress_callback: Optional[Callable] = None,
    timeout: int = 300,
    cancel_check: Optional[Callable[[], bool]] = None,
) -> Optional[str]:
    """
    异步下载更新文件

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
    # 从 metadata.yaml 获取文件名格式
    name_format = "SecRandom-[system]-[version]-[arch]-[struct].zip"

    # 替换占位符生成实际文件名
    file_name = name_format.replace("[system]", system)
    file_name = file_name.replace("[version]", version)
    file_name = file_name.replace("[arch]", arch)
    file_name = file_name.replace("[struct]", struct)

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
            if not check_update_file_integrity(file_path, "zip"):
                logger.warning(f"下载的文件不完整或已损坏: {file_path}")
                # 删除损坏的文件
                if file_path.exists():
                    try:
                        file_path.unlink()
                        logger.info(f"已删除损坏的文件: {file_path}")
                    except Exception as unlink_error:
                        logger.error(f"删除损坏文件失败: {unlink_error}")
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
                    logger.error(f"删除部分下载文件失败: {unlink_error}")
            continue

    # 所有镜像源都失败了
    logger.error("所有镜像源都下载更新文件失败")
    return None


def download_update(
    version: str,
    system: str = SYSTEM,
    arch: str = ARCH,
    struct: str = STRUCT,
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
        system,
        arch,
        struct,
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
    temp_script_path = (
        get_path("TEMP") / "installer_temp_script.py"
    )  # 初始化为临时脚本路径，便于后续清理
    try:
        logger.debug(f"开始安装更新文件: {file_path}")

        # 验证更新文件存在
        if not Path(file_path).exists():
            logger.error(f"更新文件不存在: {file_path}")
            return False

        # 检查更新文件完整性
        if not check_update_file_integrity(file_path):
            logger.error(f"更新文件不完整或已损坏: {file_path}")
            # 删除损坏的文件
            try:
                Path(file_path).unlink()
                logger.info(f"已删除损坏的更新文件: {file_path}")
            except Exception as e:
                logger.error(f"删除损坏的更新文件失败: {e}")
            return False

        # 判断是否是开发环境
        is_dev_env = False
        try:
            # 检查是否存在 .git 目录
            git_dir = get_path(".git")
            is_dev_env = git_dir.exists()
        except Exception as e:
            logger.debug(f"检查开发环境失败: {e}")

        if is_dev_env:
            # 开发环境：安装到 TEMP 文件夹
            logger.info("开发环境，安装到 TEMP 文件夹")
            temp_dir = get_path("TEMP")
            ensure_dir(temp_dir)

            # 解压更新文件到 TEMP 目录
            success = extract_zip(file_path, temp_dir, overwrite=True)
            if success:
                # 开发环境：删除旧版本文件
                old_version_dir = get_path("SecRandom")
                if old_version_dir.exists():
                    shutil.rmtree(old_version_dir)
                    logger.info(f"删除旧版本目录: {old_version_dir}")

                # 删除下载的更新文件
                logger.info(f"准备删除更新文件: {file_path}")
                try:
                    Path(file_path).unlink()
                    logger.info(f"更新文件已删除: {file_path}")
                except Exception as e:
                    logger.error(f"删除更新文件失败: {e}")

                logger.info(f"开发环境更新文件安装成功: {file_path}")
                return True
            else:
                logger.error(f"开发环境更新文件安装失败: {file_path}")
                return False
        else:
            # 生产环境：新开进程安装，主进程关闭
            logger.info("生产环境，准备启动独立更新进程")

            # 获取根目录
            root_dir = get_app_root()
            if not Path(root_dir).exists():
                logger.error(f"应用根目录不存在: {root_dir}")
                return False

            # 创建临时安装脚本
            installer_script = """
import zipfile
import os
import sys
import shutil
import time
from pathlib import Path

# 配置日志
from loguru import logger

# 确保日志目录存在
log_dir = Path('logs')
log_dir.mkdir(exist_ok=True)

# 配置日志格式 - 文件输出
logger.add(
    log_dir / 'update_install_{time:YYYY-MM-DD}.log',
    rotation='1 MB',
    retention='30 days',
    compression='tar.gz',
    backtrace=True,
    diagnose=True,
    level='INFO',
    format='<green>{time:YYYY-MM-DD HH:mm:ss}</green> | <level>{level: <8}</level> | <cyan>{name}</cyan>:<cyan>{function}</cyan>:<cyan>{line}</cyan> - <level>{message}</level>'
)

# 配置日志格式 - 终端输出
if sys.stdout is not None:
    logger.add(
        sys.stdout,
        format='<green>{time:YYYY-MM-DD HH:mm:ss}</green> | <level>{level: <8}</level> | <cyan>{name}</cyan>:<cyan>{function}</cyan>:<cyan>{line}</cyan> - <level>{message}</level>',
        level='INFO',
        colorize=True
    )


def ensure_dir(path):
    # 确保目录存在
    Path(path).mkdir(parents=True, exist_ok=True)


def extract_zip(zip_path, target_dir, overwrite=True):
    # 解压zip文件
    try:
        logger.info(f"开始解压文件: {zip_path} 到 {target_dir}")
        ensure_dir(target_dir)

        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            for file in zip_ref.namelist():
                target_file = Path(target_dir) / file
                ensure_dir(target_file.parent)

                if target_file.exists() and not overwrite:
                    logger.info(f"文件已存在，跳过: {target_file}")
                    continue

                # 解压文件
                try:
                    zip_ref.extract(file, target_dir)
                    logger.info(f"解压文件成功: {target_file}")
                except PermissionError:
                    # Windows 上可能需要删除旧文件
                    if target_file.exists():
                        try:
                            target_file.unlink()
                            zip_ref.extract(file, target_dir)
                            logger.info(f"重新解压文件成功: {target_file}")
                        except Exception as e:
                            logger.warning(f"覆盖文件失败，跳过: {target_file}, 错误: {e}")
                            continue
                    else:
                        raise

                # Linux系统下设置可执行权限
                if os.name != 'nt' and file.endswith(('.py', '.sh')):
                    try:
                        # 获取文件的当前权限
                        current_mode = os.stat(target_file).st_mode
                        # 添加执行权限
                        os.chmod(target_file, current_mode | 0o111)
                        logger.info(f"已设置文件执行权限: {target_file}")
                    except Exception as e:
                        logger.warning(f"设置文件执行权限失败: {e}")

        logger.info(f"文件解压完成: {zip_path} 到 {target_dir}")
        return True
    except Exception as e:
        logger.error(f"解压文件失败: {e}")
        return False


def restart_application(root_dir):
    try:
        logger.info("准备重启应用程序")

        # 确定主程序文件
        main_program = None
        possible_main_files = ['main.py', 'SecRandom', 'SecRandom.exe']

        for main_file in possible_main_files:
            main_path = Path(root_dir) / main_file
            if main_path.exists():
                main_program = main_path
                break

        if not main_program:
            logger.error("未找到主程序文件")
            return False

        logger.info(f"找到主程序文件: {main_program}")

        # 根据系统类型选择重启方式
        if os.name == 'nt':
            # Windows系统 - 使用引号保护路径中的空格
            logger.info("Windows系统，使用start命令重启")
            import subprocess
            subprocess.Popen(
                ['start', '""', f'"{main_program}"'],
                shell=True,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL
            )
        else:
            # Linux系统
            logger.info("Linux系统，使用nohup命令后台重启")
            import subprocess
            subprocess.Popen(
                [sys.executable, str(main_program)],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True
            )

        logger.info("应用程序重启成功")
        return True
    except Exception as e:
        logger.error(f"重启应用程序失败: {e}")
        return False


if __name__ == '__main__':
    try:
        # 获取参数
        update_file = sys.argv[1]
        root_dir = sys.argv[2]

        logger.info(f"更新安装脚本启动")
        logger.info(f"更新文件: {update_file}")
        logger.info(f"根目录: {root_dir}")

        # 验证参数
        if not Path(update_file).exists():
            logger.error(f"更新文件不存在: {update_file}")
            sys.exit(1)

        if not Path(root_dir).exists():
            logger.error(f"根目录不存在: {root_dir}")
            sys.exit(1)

        # 等待一段时间，确保主进程已关闭（可配置）
        wait_time = 2
        logger.info(f"等待主进程关闭... ({wait_time}秒)")
        time.sleep(wait_time)

        # 解压更新文件到根目录
        success = extract_zip(update_file, root_dir, overwrite=True)
        if success:
            logger.info("更新安装成功")

            # 重启应用程序
            restart_application(root_dir)

            # 删除下载的更新文件
            logger.info(f"准备删除更新文件: {update_file}")
            try:
                time.sleep(1)  # 给文件系统一点时间
                Path(update_file).unlink()
                logger.info(f"更新文件已删除: {update_file}")
            except Exception as e:
                logger.error(f"删除更新文件失败: {e}")
        else:
            logger.error("更新安装失败")
            sys.exit(1)

    except Exception as e:
        logger.error(f"更新安装脚本执行失败: {e}")
        sys.exit(1)
"""

            # 写入临时脚本文件
            try:
                with NamedTemporaryFile(
                    mode="w", encoding="utf-8", delete=False, suffix=".py"
                ) as temp_script:
                    temp_script.write(installer_script)
                    temp_script_path = temp_script.name
                    logger.debug(f"临时脚本已创建: {temp_script_path}")
            except Exception as e:
                logger.error(f"创建临时脚本失败: {e}")
                return False

            try:
                # 启动独立更新进程
                logger.info("启动独立更新进程")
                subprocess.Popen(
                    [sys.executable, temp_script_path, file_path, str(root_dir)],
                    close_fds=True,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    creationflags=subprocess.CREATE_NO_WINDOW
                    if sys.platform == "win32"
                    else 0,
                )

                # 给子进程足够的时间启动
                time.sleep(1)

                # 关闭主进程
                logger.info("生产环境更新进程已启动，主进程将关闭")
                sys.exit(0)
            except Exception as e:
                logger.error(f"启动更新进程失败: {e}")
                return False

            return True
    except Exception as e:
        logger.error(f"安装更新文件失败: {e}")
        return False
    finally:
        # 说明：
        # - 生产环境中，临时安装脚本会在子进程内部自删除；
        # - 开发环境中，不会创建临时安装脚本（temp_script_path 为空）。
        # 因此，此处不再尝试在主进程中清理临时脚本文件，以避免无效的清理逻辑。
        pass


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
            compare_result = compare_versions(SPECIAL_VERSION, latest_version)

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
                        logger.error("自动安装更新失败")
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
                    title, content, url="https://secrandom.netlify.app/download"
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
                            # 更新全局状态
                            update_status_manager.set_download_complete_with_size(
                                str(expected_file_path), file_size_str
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
                        from pathlib import Path

                        file_size = Path(file_path).stat().st_size

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
                        logger.error("自动下载更新失败")
                        # 通知更新页面下载失败
                        safe_call_update_interface("set_download_failed")
                        # 更新全局状态
                        update_status_manager.set_download_failed()
            elif compare_result == 0 or compare_result == -1:
                # 当前是最新版本或开发版本
                if compare_result == 0:
                    logger.debug("当前已是最新版本")
                else:
                    logger.debug(f"当前版本 ({SPECIAL_VERSION}) 比远程版本 ({latest_version}) 更新，视为最新版本")

                # 通知更新页面已是最新版本
                safe_call_update_interface("set_latest_version")
                # 更新全局状态
                update_status_manager.set_latest_version()
            else:
                # 版本比较失败 (compare_result == -2)
                logger.debug("版本比较失败")
                # 通知更新页面检查失败
                safe_call_update_interface("set_check_failed")
                # 更新全局状态
                update_status_manager.set_check_failed()

            # 更新上次检查时间
            safe_call_update_interface("update_last_check_time")
        except Exception as e:
            logger.error(f"启动时检查更新失败: {e}")
            # 通知更新页面检查失败
            safe_call_update_interface("set_check_failed")
            # 更新全局状态
            update_status_manager.set_check_failed()
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
