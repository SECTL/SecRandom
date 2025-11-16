import os
import re


def get_version_from_env():
    original_version = os.getenv("VERSION", "v0.0.0.0")
    stripped_version = re.sub(r"^v", "", original_version)
    numeric_version = re.search(
        r"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?", stripped_version
    )
    if numeric_version:
        parts = numeric_version.groups(default="0")
        stripped_version = ".".join(parts)
    else:
        stripped_version = "0.0.0.0"
        original_version = "v0.0.0.0"
    return original_version, stripped_version


def update_version_info(version):
    major, minor, patch, build = map(int, version.split("."))
    version_tuple = (major, minor, patch, build)
    version_str = f"{major}.{minor}.{patch}.{build}"
    with open("version_info.txt", "r", encoding="utf-8") as f:
        content = f.read()
    content = re.sub(
        r"filevers=\(\d, \d, \d, \d\)", f"filevers={version_tuple}", content
    )
    content = re.sub(
        r"prodvers=\(\d, \d, \d, \d\)", f"prodvers={version_tuple}", content
    )
    content = re.sub(
        r"StringStruct\(u\'FileVersion\', u\'\d+\.\d+\.\d+\.\d+\'\)",
        f"StringStruct(u'FileVersion', u'{version_str}')",
        content,
    )
    content = re.sub(
        r"StringStruct\(u\'ProductVersion\', u\'\d+\.\d+\.\d+\.\d+\'\)",
        f"StringStruct(u'ProductVersion', u'{version_str}')",
        content,
    )
    with open("version_info.txt", "w", encoding="utf-8") as f:
        f.write(content)


def update_config_py(version):
    with open("app/tools/variable.py", "r", encoding="utf-8") as f:
        content = f.read()
    content = re.sub(
        r'VERSION = "v?\d+\.\d+\.\d+\.\d+"', f'VERSION = "{version}"', content
    )
    with open("app/tools/variable.py", "w", encoding="utf-8") as f:
        f.write(content)


if __name__ == "__main__":
    original_version, stripped_version = get_version_from_env()
    update_version_info(stripped_version)
    update_config_py(original_version)
