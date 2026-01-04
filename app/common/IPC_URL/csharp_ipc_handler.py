import sys
import asyncio
import threading
from typing import Optional
from loguru import logger

from app.tools.path_utils import get_data_path

CSHARP_AVAILABLE = False

try:
    # 添加 dlls path
    sys.path.append(str(get_data_path("dlls")))

    # 导入 Python.NET
    from pythonnet import load

    load("coreclr", runtime_config=get_data_path("dlls", "dotnet.runtimeconfig.json"))

    # 加载 .NET CoreCLR 程序集
    import clr

    clr.AddReference("ClassIsland.Shared.IPC")
    clr.AddReference("SecRandom4Ci.Interface")

    # 导入程序集
    from System import Action
    from ClassIsland.Shared.Enums import TimeState
    from ClassIsland.Shared.IPC import IpcClient, IpcRoutedNotifyIds
    from ClassIsland.Shared.IPC.Abstractions.Services import IPublicLessonsService
    from dotnetCampus.Ipc.CompilerServices.GeneratedProxies import GeneratedIpcFactory
    from SecRandom4Ci.Interface.Services import ISecRandomService
    from SecRandom4Ci.Interface.Models import CallResult, Student

    CSHARP_AVAILABLE = True
except Exception as e:
    logger.warning("无法加载 Python.NET，将会回滚！")
    logger.warning(e)


if CSHARP_AVAILABLE:

    class CSharpIPCHandler:
        """C# dotnetCampus.Ipc 处理器，用于连接 ClassIsland 实例"""

        _instance: Optional["CSharpIPCHandler"] = None

        def __new__(cls):
            if cls._instance is None:
                cls._instance = super().__new__(cls)
                cls._instance._initialized = False
            return cls._instance

        @classmethod
        def instance(cls):
            """获取单例实例"""
            if cls._instance is None:
                cls._instance = cls()
            return cls._instance

        def __init__(self):
            """
            初始化 C# IPC 处理器
            """
            self.ipc_client: Optional[IpcClient] = None
            self.client_thread: Optional[threading.Thread] = None
            self.is_running = False
            self.is_connected = False
            self._stop_event = threading.Event()
            self._disconnect_logged = False  # 跟踪是否已记录断连日志
            self._last_on_class_left_log_time = 0  # 上次记录距离上课时间的时间

        def start_ipc_client(self) -> bool:
            """
            启动 C# IPC 客户端

            Returns:
                启动成功返回True，失败返回False
            """
            if self.is_running:
                return True

            try:
                self._stop_event.clear()
                self.client_thread = threading.Thread(
                    target=self._run_client, daemon=False
                )
                self.client_thread.start()
                self.is_running = True
                return True
            except Exception as e:
                logger.error(f"启动 C# IPC 客户端失败: {e}")
                return False

        def stop_ipc_client(self):
            """停止 C# IPC 客户端"""
            if not self.is_running:
                return

            logger.debug("正在停止 C# IPC 客户端...")
            self.is_running = False
            self._stop_event.set()

            if self.client_thread and self.client_thread.is_alive():
                logger.debug("等待 C# IPC 线程结束...")
                # 给予足够的时间正常退出，因为 daemon=False
                self.client_thread.join(timeout=1.0)
                if self.client_thread.is_alive():
                    logger.warning("C# IPC 线程未能在超时时间内完全结束，可能由于 .NET 调用阻塞")
            
            # 线程结束后再清理资源，避免竞态条件
            self.ipc_client = None
            self.is_connected = False
            logger.debug("C# IPC 客户端停止指令已发出")

        def send_notification(
            self,
            class_name,
            selected_students,
            draw_count=1,
            settings=None,
            settings_group=None,
        ) -> bool:
            """发送提醒"""
            if not self.is_running:
                return False

            if not self.is_connected:
                return False

            if settings:
                display_duration = settings.get("notification_display_duration", 5)
            else:
                display_duration = 5

            randomService = GeneratedIpcFactory.CreateIpcProxy[ISecRandomService](
                self.ipc_client.Provider, self.ipc_client.PeerProxy
            )
            result = self.convert_to_call_result(
                class_name, selected_students, draw_count, display_duration
            )
            randomService.NotifyResult(result)

            return True

        def is_breaking(self) -> bool:
            """是否处于下课时间"""
            lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                self.ipc_client.Provider, self.ipc_client.PeerProxy
            )
            state = lessonSc.CurrentState in [
                getattr(TimeState, "None"),
                TimeState.PrepareOnClass,
                TimeState.Breaking,
                TimeState.AfterSchool,
            ]
            logger.debug(
                f"获取到的 ClassIsland 时间状态: {lessonSc.CurrentState} 是否下课: {state}"
            )
            return state

        def get_on_class_left_time(self) -> int:
            """获取距离上课剩余时间（秒）

            Returns:
                int: 距离上课的剩余时间（秒），如果当前正在上课或没有下一节课程则返回0
            """
            try:
                import time

                lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                    self.ipc_client.Provider, self.ipc_client.PeerProxy
                )
                on_class_left_time = lessonSc.OnClassLeftTime
                total_seconds = int(on_class_left_time.TotalSeconds)

                # 根据距离上课的时间调整日志记录频率
                # 距离上课3秒前：每30秒记录一次
                # 距离上课3秒内：每秒记录一次
                current_time = time.time()
                should_log = False

                if total_seconds > 0 and total_seconds <= 3:
                    # 3秒内，每秒记录一次
                    should_log = True
                elif current_time - self._last_on_class_left_log_time >= 30:
                    # 3秒前，每30秒记录一次
                    should_log = True
                    self._last_on_class_left_log_time = current_time

                if should_log:
                    logger.debug(f"获取到的距离上课剩余时间: {total_seconds} 秒")

                return total_seconds
            except Exception as e:
                logger.error(f"获取距离上课时间失败: {e}")
                return 0

        @staticmethod
        def convert_to_call_result(
            class_name: str, selected_students, draw_count: int, display_duration=5.0
        ) -> CallResult:
            result = CallResult()
            result.ClassName = class_name
            result.DrawCount = draw_count
            result.DisplayDuration = display_duration
            for student in selected_students:
                cs_student = Student()
                cs_student.StudentId = student[0]
                cs_student.StudentName = student[1]
                cs_student.Exists = student[2]
                result.SelectedStudents.Add(cs_student)
            return result

        def _on_class_test(self):
            lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                self.ipc_client.Provider, self.ipc_client.PeerProxy
            )
            logger.debug(
                f"上课 {lessonSc.CurrentSubject.Name} 时间: {lessonSc.CurrentTimeLayoutItem}"
            )

        def _run_client(self):
            """运行 C# IPC 客户端"""

            async def client():
                """异步客户端"""
                try:
                    self.ipc_client = IpcClient()
                    self.ipc_client.JsonIpcProvider.AddNotifyHandler(
                        IpcRoutedNotifyIds.OnClassNotifyId,
                        Action(lambda: self._on_class_test()),
                    )

                    task = self.ipc_client.Connect()
                    # 优化：在等待连接时定期检查 is_running 标志，以便快速响应退出请求
                    # 避免在 .NET 内部 Wait() 导致线程无法被 Python 正常终止
                    while self.is_running and not task.IsCompleted:
                        await asyncio.sleep(0.1)
                    
                    if not self.is_running:
                        return
                        
                    if task.IsFaulted:
                        logger.error(f"C# IPC 连接失败: {task.Exception}")
                        self.is_connected = False
                        return
                        
                    self.is_connected = True

                    while self.is_running:
                        # 缩短轮询间隔，提高退出响应速度 (每次睡眠 0.1s，共 0.5s)
                        for _ in range(5):
                            if not self.is_running:
                                break
                            await asyncio.sleep(0.1)

                        if not self.is_running:
                            break

                        if not self._check_alive():
                            if not self._disconnect_logged:
                                logger.debug("C# IPC 断连！重连...")
                                self._disconnect_logged = True
                            self.is_connected = False

                            task = self.ipc_client.Connect()
                            while self.is_running and not task.IsCompleted:
                                await asyncio.sleep(0.1)
                            
                            if not self.is_running:
                                break
                                
                            if task.IsFaulted:
                                continue
                                
                            self.is_connected = True
                            self._disconnect_logged = False
                except Exception as e:
                    if self.is_running:
                        logger.error(f"C# IPC 客户端运行出错: {e}")
                finally:
                    # 在线程内部安全地释放资源
                    try:
                        if self.ipc_client and hasattr(self.ipc_client, "Dispose"):
                            self.ipc_client.Dispose()
                            logger.debug("C# IPC 客户端资源已释放 (Dispose)")
                    except Exception as e:
                        logger.debug(f"释放 C# IPC 客户端资源时出错: {e}")
                    self.is_connected = False

            # 启动新的 asyncio 事件循环
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            try:
                loop.run_until_complete(client())
            finally:
                loop.close()

        def _check_alive(self) -> bool:
            """客户端是否正常连接"""
            try:
                randomService = GeneratedIpcFactory.CreateIpcProxy[ISecRandomService](
                    self.ipc_client.Provider, self.ipc_client.PeerProxy
                )
                return randomService.IsAlive() == "Yes"
            except Exception:
                return False
else:

    class CSharpIPCHandler:
        """C# dotnetCampus.Ipc 处理器，用于连接 ClassIsland 实例"""

        _instance: Optional["CSharpIPCHandler"] = None

        def __new__(cls):
            if cls._instance is None:
                cls._instance = super().__new__(cls)
                cls._instance._initialized = False
            return cls._instance

        @classmethod
        def instance(cls):
            """获取单例实例"""
            if cls._instance is None:
                cls._instance = cls()
            return cls._instance

        def __init__(self):
            """
            初始化 C# IPC 处理器
            """
            self.ipc_client = None
            self.client_thread = None
            self.is_running = False
            self.is_connected = False

        def start_ipc_client(self) -> bool:
            """
            启动 C# IPC 客户端

            Returns:
                启动成功返回True，失败返回False
            """
            return False

        def stop_ipc_client(self):
            """停止 C# IPC 客户端"""
            logger.debug("C# IPC 处理器未启用，无需停止")
            pass

        def send_notification(
            self,
            class_name,
            selected_students,
            draw_count=1,
            settings=None,
            settings_group=None,
        ) -> bool:
            """发送提醒"""
            return False

        def is_breaking(self) -> bool:
            """是否处于下课时间"""
            return False

        def get_on_class_left_time(self) -> int:
            """获取距离上课剩余时间（秒）

            Returns:
                int: 距离上课的剩余时间（秒），如果当前正在上课或没有下一节课程则返回0
            """
            return 0

        @staticmethod
        def convert_to_call_result(
            class_name: str, selected_students, draw_count: int, display_duration=5.0
        ) -> object:
            return object

        def _on_class_test(self):
            pass

        def _run_client(self):
            """运行 C# IPC 客户端"""
            pass
