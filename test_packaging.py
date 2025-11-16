"""
打包验证测试脚本
用于验证修复后的打包是否正常工作
"""

import sys


def test_imports():
    """测试关键模块导入"""
    print("=" * 60)
    print("测试 1: 模块导入")
    print("=" * 60)

    try:
        from app.tools.path_utils import get_path, get_app_root  # noqa: F401

        print("✓ path_utils 导入成功")

        from app.tools.language_manager import get_current_language_data  # noqa: F401

        print("✓ language_manager 导入成功")

        from app.Language.obtain_language import Language  # noqa: F401

        print("✓ obtain_language 导入成功")

        return True
    except Exception as e:
        print(f"✗ 导入失败: {e}")
        return False


def test_paths():
    """测试路径获取"""
    print("\n" + "=" * 60)
    print("测试 2: 路径获取")
    print("=" * 60)

    try:
        from app.tools.path_utils import get_app_root, get_path

        app_root = get_app_root()
        print(f"应用根目录: {app_root}")
        print(f"是否为打包环境: {getattr(sys, 'frozen', False)}")

        if hasattr(sys, "_MEIPASS"):
            print(f"PyInstaller 临时目录: {sys._MEIPASS}")

        # 测试资源路径
        resources_path = get_path("app/resources")
        print(f"资源目录: {resources_path}")
        print(f"资源目录存在: {resources_path.exists()}")

        # 测试语言模块路径
        lang_modules_path = get_path("app/Language/modules")
        print(f"语言模块目录: {lang_modules_path}")
        print(f"语言模块目录存在: {lang_modules_path.exists()}")

        return True
    except Exception as e:
        print(f"✗ 路径测试失败: {e}")
        import traceback

        traceback.print_exc()
        return False


def test_language_loading():
    """测试语言加载"""
    print("\n" + "=" * 60)
    print("测试 3: 语言数据加载")
    print("=" * 60)

    try:
        from app.tools.language_manager import get_current_language_data

        lang_data = get_current_language_data()
        print(f"语言数据类型: {type(lang_data)}")
        print(
            f"语言数据键数量: {len(lang_data.keys()) if isinstance(lang_data, dict) else 'N/A'}"
        )

        if isinstance(lang_data, dict) and len(lang_data) > 0:
            # 显示前几个键
            keys = list(lang_data.keys())[:5]
            print(f"语言数据示例键: {keys}")
            print("✓ 语言数据加载成功")
            return True
        else:
            print("✗ 语言数据为空")
            return False

    except Exception as e:
        print(f"✗ 语言加载失败: {e}")
        import traceback

        traceback.print_exc()
        return False


def test_resource_files():
    """测试资源文件存在性"""
    print("\n" + "=" * 60)
    print("测试 4: 资源文件检查")
    print("=" * 60)

    try:
        from app.tools.path_utils import get_path

        # 检查关键资源目录
        resource_dirs = [
            "app/resources/assets",
            "app/resources/font",
            "app/resources/Language",
            "app/Language/modules",
        ]

        all_exist = True
        for dir_path in resource_dirs:
            path = get_path(dir_path)
            exists = path.exists()
            status = "✓" if exists else "✗"
            print(f"{status} {dir_path}: {exists}")
            if not exists:
                all_exist = False

        return all_exist
    except Exception as e:
        print(f"✗ 资源文件检查失败: {e}")
        import traceback

        traceback.print_exc()
        return False


def main():
    """主测试函数"""
    print("\n" + "=" * 60)
    print("SecRandom 打包验证测试")
    print("=" * 60 + "\n")

    results = []

    # 运行测试
    results.append(("模块导入", test_imports()))
    results.append(("路径获取", test_paths()))
    results.append(("语言加载", test_language_loading()))
    results.append(("资源文件", test_resource_files()))

    # 汇总结果
    print("\n" + "=" * 60)
    print("测试结果汇总")
    print("=" * 60)

    passed = 0
    failed = 0

    for name, result in results:
        status = "通过" if result else "失败"
        symbol = "✓" if result else "✗"
        print(f"{symbol} {name}: {status}")

        if result:
            passed += 1
        else:
            failed += 1

    print("\n" + "=" * 60)
    print(f"总计: {passed} 通过, {failed} 失败")
    print("=" * 60 + "\n")

    if failed == 0:
        print("🎉 所有测试通过！打包修复成功！")
        return 0
    else:
        print("⚠️  部分测试失败，请检查错误信息")
        return 1


if __name__ == "__main__":
    sys.exit(main())
