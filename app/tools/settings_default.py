# ==================================================
# 默认设置配置
# ==================================================
"""
该模块包含所有应用程序设置的默认值配置
使用层级结构组织设置项，第一层为分类，第二层为具体设置项
"""

import json
import platform
import ctypes
import uuid
import zipfile
from loguru import logger

from app.tools.variable import *
from app.tools.path_utils import *
from app.tools.settings_default_storage import *
from app.tools.path_utils import atomic_write_json

Language = DEFAULT_LANGUAGE


# ==================================================
# 便捷函数
# ==================================================
def get_default_settings():
    """获取所有默认设置

    Returns:
        dict: 包含所有默认设置的层级字典
    """
    return DEFAULT_SETTINGS


def get_default_setting(first_level_key: str, second_level_key: str):
    """根据键获取指定的默认设置值

    Args:
        first_level_key: 第一层的键
        second_level_key: 第二层的键

    Returns:
        默认设置值，如果不存在则返回None
    """
    if first_level_key in DEFAULT_SETTINGS:
        if second_level_key in DEFAULT_SETTINGS[first_level_key]:
            return DEFAULT_SETTINGS[first_level_key][second_level_key]["default_value"]
    return None


# ==================================================
# 设置文件管理相关函数
# ==================================================
_DEVICE_UUID_FILE = "device_uuid.json"


def _try_recover_settings_from_backup() -> dict | None:
    """尝试从最近的备份中恢复 settings.json 内容。

    遍历备份目录中的 zip 文件（按修改时间倒序），查找包含
    config/settings.json 的备份，返回其中可解析的设置字典。
    如果所有备份都无法恢复则返回 None。
    """
    try:
        backup_dir = get_data_path("backup")
        if not backup_dir.exists():
            return None

        zip_files = sorted(
            backup_dir.glob("*.zip"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )

        for zip_path in zip_files:
            try:
                with zipfile.ZipFile(str(zip_path), "r") as zf:
                    for candidate in (
                        "config/settings.json",
                        "settings.json",
                    ):
                        if candidate in zf.namelist():
                            with zf.open(candidate) as member:
                                content = member.read().decode("utf-8")
                            if content and content.strip():
                                recovered = json.loads(content)
                                if isinstance(recovered, dict):
                                    logger.info(f"从备份 {zip_path.name} 恢复设置成功")
                                    return recovered
            except Exception:
                continue
    except Exception as e:
        logger.warning(f"扫描备份恢复设置失败: {e}")

    return None


def ensure_device_uuid():
    """确保 offline_user_id 为标准的 36 位 UUID 格式

    如果设置中 offline_user_id 不存在或格式无效，自动生成标准 UUID 写入。
    此函数应在 manage_settings_file() 已确保 settings.json 存在后调用。
    """
    settings_file = get_settings_path()
    if not file_exists(settings_file):
        return

    try:
        with open_file(settings_file, "r", encoding="utf-8") as f:
            settings = json.load(f)
    except Exception:
        return

    uuid_str = settings.get("basic_settings", {}).get("offline_user_id", "")
    needs_regenerate = False

    if uuid_str:
        try:
            parsed = uuid.UUID(uuid_str)
            if str(parsed) != uuid_str.lower():
                logger.warning(f"offline_user_id 格式不标准，将重新生成: {uuid_str}")
                needs_regenerate = True
        except (ValueError, AttributeError):
            logger.warning(f"offline_user_id 无效，将重新生成: {uuid_str}")
            needs_regenerate = True
    else:
        logger.info("offline_user_id 为空，将生成新的 UUID")
        needs_regenerate = True

    if needs_regenerate:
        new_uuid = str(uuid.uuid4()).lower()
        settings.setdefault("basic_settings", {})["offline_user_id"] = new_uuid
        try:
            atomic_write_json(settings_file, settings)
            logger.info(f"已生成新的 offline_user_id: {new_uuid}")
        except Exception as e:
            logger.error(f"写入 offline_user_id 失败: {e}")


def manage_settings_file():
    """管理设置文件，确保其存在且完整

    该函数会执行以下操作：
    1. 如果设置文件不存在，创建带有默认值的设置文件
    2. 如果设置文件存在但缺少某些设置项，补全这些设置项
    3. 如果设置文件中有多余的设置项，移除这些设置项
    4. 确保 device_uuid 存在且格式正确
    """
    ensure_device_uuid()
    try:
        settings_file = get_settings_path()
        ensure_dir(settings_file.parent)
        try:
            if platform.system() == "Windows":
                FILE_ATTRIBUTE_HIDDEN = 0x2
                FILE_ATTRIBUTE_SYSTEM = 0x4
                ctypes.windll.kernel32.SetFileAttributesW(
                    str(settings_file.parent),
                    FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM,
                )
        except Exception:
            pass

        default_settings = get_default_settings()

        if not file_exists(settings_file):
            logger.info(f"设置文件不存在，创建默认设置文件: {settings_file}")
            flat_settings = {}
            for first_level_key, first_level_value in default_settings.items():
                flat_settings[first_level_key] = {}
                for second_level_key, second_level_value in first_level_value.items():
                    # 如果默认值为 None，则不写入设置文件
                    if second_level_value["default_value"] is not None:
                        flat_settings[first_level_key][second_level_key] = (
                            second_level_value["default_value"]
                        )

            atomic_write_json(settings_file, flat_settings)
            return

        try:
            with open_file(settings_file, "r", encoding="utf-8") as f:
                current_settings = json.load(f)
        except Exception as e:
            logger.warning(f"读取设置文件失败: {e}，尝试从备份恢复")
            recovered = _try_recover_settings_from_backup()
            if recovered is not None:
                logger.info("从备份恢复设置成功，将使用恢复的设置继续合并")
                current_settings = recovered
            else:
                logger.warning("无可用备份，将创建默认设置文件")
                flat_settings = {}
                for first_level_key, first_level_value in default_settings.items():
                    flat_settings[first_level_key] = {}
                    for (
                        second_level_key,
                        second_level_value,
                    ) in first_level_value.items():
                        if second_level_value["default_value"] is not None:
                            flat_settings[first_level_key][second_level_key] = (
                                second_level_value["default_value"]
                            )

                atomic_write_json(settings_file, flat_settings)
                return

        # 检查并更新设置文件
        settings_updated = False
        updated_settings = {}

        # 处理现有设置项
        for first_level_key, first_level_value in current_settings.items():
            if first_level_key in default_settings:
                updated_settings[first_level_key] = {}
                for second_level_key, second_level_value in first_level_value.items():
                    if second_level_key in default_settings[first_level_key]:
                        # 检查现有值是否已经是简单的值（非字典）
                        if not isinstance(second_level_value, dict):
                            # 如果值为 null，则不写入更新后的设置
                            if second_level_value is not None:
                                # 如果已经是简单值，直接使用
                                updated_settings[first_level_key][second_level_key] = (
                                    second_level_value
                                )
                        else:
                            # 如果是字典结构，提取 default_value
                            if "default_value" in second_level_value:
                                # 如果 default_value 为 null，则不写入更新后的设置
                                if second_level_value["default_value"] is not None:
                                    updated_settings[first_level_key][
                                        second_level_key
                                    ] = second_level_value["default_value"]
                            else:
                                # 如果没有 default_value，使用整个值（但检查是否为 null）
                                if second_level_value is not None:
                                    updated_settings[first_level_key][
                                        second_level_key
                                    ] = second_level_value

        # 添加缺失的设置项
        for first_level_key, first_level_value in default_settings.items():
            if first_level_key not in updated_settings:
                updated_settings[first_level_key] = {}

            for second_level_key, second_level_value in first_level_value.items():
                if second_level_key not in updated_settings[first_level_key]:
                    # 处理两种格式：{"default_value": xxx} 或直接值
                    if (
                        isinstance(second_level_value, dict)
                        and "default_value" in second_level_value
                    ):
                        default_val = second_level_value["default_value"]
                    else:
                        default_val = second_level_value

                    if default_val is not None:
                        updated_settings[first_level_key][second_level_key] = (
                            default_val
                        )
                        settings_updated = True

        # 移除多余的设置项
        for first_level_key in list(current_settings.keys()):
            if first_level_key not in default_settings:
                # logger.debug(f"移除多余的设置分类: {first_level_key}")
                settings_updated = True
                if first_level_key in updated_settings:
                    del updated_settings[first_level_key]
                continue

            for second_level_key in list(current_settings[first_level_key].keys()):
                if second_level_key not in default_settings[first_level_key]:
                    # logger.debug(f"移除多余的设置项: {first_level_key}.{second_level_key}")
                    settings_updated = True
                    if (
                        first_level_key in updated_settings
                        and second_level_key in updated_settings[first_level_key]
                    ):
                        del updated_settings[first_level_key][second_level_key]

        if settings_updated:
            atomic_write_json(settings_file, updated_settings)
        else:
            # logger.debug("设置文件已是最新，无需更新")
            pass

    except Exception as e:
        logger.exception(f"管理设置文件时发生错误: {e}")
