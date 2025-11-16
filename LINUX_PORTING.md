# SecRandom Linux 移植说明

## 概述

本文档描述了将 SecRandom 项目移植到 Linux 平台所做的更改。

## 已完成的移植工作

### 1. 创建 linux-port 分支
- 创建了专门的 `linux-port` 分支用于 Linux 平台移植工作

### 2. 依赖管理

#### requirements-linux.txt
创建了 Linux 特定的依赖文件，主要变更：
- 移除了 Windows 特定依赖（pywin32, winshell, comtypes, wmi, pycaw）
- 添加了 Linux 音频控制库 `pulsectl==24.8.0`
- 保留了所有跨平台依赖

#### pyproject.toml
更新项目配置文件：
- 将 Windows 特定依赖添加平台限定符 `platform_system == 'Windows'`
- 添加 Linux 特定依赖 `pulsectl==24.8.0; platform_system == 'Linux'`
- 确保跨平台兼容性

### 3. 代码修改

#### app/common/config.py
实现跨平台音频控制：
- 添加平台检测和条件导入
  - Windows: 使用 pycaw（COM 接口控制）
  - Linux: 使用 pulsectl（PulseAudio 接口）
- 重构 `restore_volume()` 函数支持多平台：
  - Windows: 通过 COM 接口控制音量
  - Linux: 通过 PulseAudio API 控制音量
  - 其他平台: 显示警告信息

#### app/common/another_window.py
- 验证了 Windows 特定的标题栏颜色设置代码已经使用 `os.name == 'nt'` 条件判断
- 无需修改，代码已经是跨平台兼容的

### 4. 构建和 CI/CD

#### .github/workflows/build-linux.yml
创建 Linux 构建工作流：
- 使用 Ubuntu 22.04 作为构建环境
- 安装必要的系统依赖（PulseAudio、Qt 相关库等）
- 使用 PyInstaller 打包应用
- 生成 tar.gz 压缩包（Linux 标准格式）
- 支持手动触发和分支推送触发

### 5. 文档更新

#### README.md
添加 Linux 平台支持说明：
- 更新系统兼容性说明
- 添加 Linux 系统要求
- 提供详细的 Linux 安装步骤
- 说明 Linux 平台已知限制

## 平台差异处理

### 音频控制
- **Windows**: 使用 pycaw 库通过 COM 接口控制系统音量
- **Linux**: 使用 pulsectl 库通过 PulseAudio API 控制系统音量

### 窗口管理
- Windows 特定的窗口标题栏颜色设置仅在 Windows 平台执行
- Linux 使用默认的窗口管理器主题
- **Wayland 支持**: 针对 Wayland 显示服务器优化了系统托盘菜单行为
  - 托盘菜单在 Wayland 下使用独立窗口模式
  - 自动检测 Wayland 环境并应用相应的窗口标志
  - 修复了 Wayland 下托盘菜单定位和点击响应问题

### 系统集成
- 开机自启功能在 Windows 上自动支持
- Linux 需要手动配置（systemd 或桌面环境的自启动设置）

## 技术栈

### 跨平台组件
- PyQt6: GUI 框架（完全跨平台）
- Python 3.8.10: 编程语言（跨平台）
- 大部分依赖库都是跨平台的

### 平台特定组件
- **Windows**:
  - pycaw: 音频控制
  - pywin32: Windows API 访问
  - comtypes: COM 接口

- **Linux**:
  - pulsectl: PulseAudio 音频控制
  - 标准 Linux 系统库

## 安装要求

### Linux 系统依赖
```bash
libpulse-dev        # PulseAudio 开发库
pulseaudio          # PulseAudio 音频系统
libportaudio2       # 音频 I/O 库
libsndfile1         # 音频文件库
libasound2-dev      # ALSA 开发库
portaudio19-dev     # PortAudio 开发库
libxcb-*            # X11 相关库
libgl1-mesa-glx     # OpenGL 库
libegl1             # EGL 库
libdbus-1-3         # D-Bus 库
```

## 已知限制

1. **Linux 平台**:
   - 开机自启需要手动配置
   - 依赖 PulseAudio 音频系统
   - 某些 Windows 特定功能不可用

2. **测试状态**:
   - 代码已经修改为跨平台兼容
   - 需要在实际 Linux 环境中进行测试

## 下一步工作

1. **测试**: 在实际 Linux 环境中测试应用程序
2. **优化**: 根据测试结果优化 Linux 特定代码
3. **文档**: 完善 Linux 平台使用文档
4. **CI/CD**: 验证 Linux 构建工作流
5. **发布**: 创建 Linux 平台的正式发布版本

## 兼容性矩阵

| 功能 | Windows | Linux | 说明 |
|------|---------|-------|------|
| 基础 GUI | ✅ | ✅ | PyQt6 完全跨平台 |
| 音频控制 | ✅ | ✅ | 不同实现但功能相同 |
| 开机自启 | ✅ | ⚠️ | Linux 需手动配置 |
| 窗口主题 | ✅ | ✅ | 部分特性限 Windows |
| 文件操作 | ✅ | ✅ | 完全跨平台 |
| 网络功能 | ✅ | ✅ | 完全跨平台 |

✅ = 完全支持 | ⚠️ = 部分支持/需额外配置 | ❌ = 不支持

## 贡献

如果您在 Linux 平台上测试或使用 SecRandom，欢迎反馈问题和建议！

---

**移植日期**: 2025-11-01
**移植版本**: 基于 v1.1.0
