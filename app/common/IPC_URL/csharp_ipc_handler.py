import sys
import asyncio
import threading
from typing import Optional, Any, TypedDict
from loguru import logger

from app.tools.path_utils import get_data_path

CSHARP_AVAILABLE = False


class SelectedStudentNotificationInfo(TypedDict):
    student_id: int
    student_name: str
    display_text: str
    exists: bool
    group_name: str
    lottery_name: str


if sys.platform == "win32":
    try:
        sys.path.append(str(get_data_path("dlls")))

        from pythonnet import load

        load(
            "coreclr", runtime_config=get_data_path("dlls", "dotnet.runtimeconfig.json")
        )

        import clr

        clr.AddReference("ClassIsland.Shared.IPC")
        clr.AddReference("SecRandom4Ci.Interface")

        from System import Action, DateTime, Version
        from ClassIsland.Shared.Enums import TimeState
        from ClassIsland.Shared.IPC import IpcClient, IpcRoutedNotifyIds
        from ClassIsland.Shared.IPC.Abstractions.Services import IPublicLessonsService
        from dotnetCampus.Ipc.CompilerServices.GeneratedProxies import (
            GeneratedIpcFactory,
        )
        from SecRandom4Ci.Interface.Enums import ResultType
        from SecRandom4Ci.Interface.Models import (
            CallResult,
            Student,
            NotificationData,
            NotificationItem,
        )
        from SecRandom4Ci.Interface.Services import ISecRandomService

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
            self.loop: Optional[asyncio.AbstractEventLoop] = None
            self.is_running = False
            self.is_connected = False
            self._no_plugin_logged = False
            self._last_on_class_left_log_time = 0  # 上次记录距离上课时间的时间
            self._last_known_subject_name: Optional[str] = None

        def start_ipc_client(self) -> bool:
            """
            启动 C# IPC 客户端

            Returns:
                启动成功返回True，失败返回False
            """
            if self.is_running:
                return True

            try:
                self.is_running = True
                self.client_thread = threading.Thread(
                    target=self._run_client, daemon=False
                )
                self.client_thread.start()
                return True
            except Exception as e:
                self.is_running = False
                logger.exception(f"启动 C# IPC 客户端失败: {e}")
                return False

        def stop_ipc_client(self):
            """停止 C# IPC 客户端"""
            logger.debug("正在停止 C# IPC 客户端...")
            self.is_running = False
            if self.loop and self.loop.is_running():
                # 获取所有正在运行的任务并取消它们
                # 这会使 await asyncio.sleep(1) 等操作抛出 CancelledError
                try:
                    for task in asyncio.all_tasks(self.loop):
                        self.loop.call_soon_threadsafe(task.cancel)
                except Exception as e:
                    logger.warning(f"取消 IPC 客户端任务时出错: {e}")

            if self.client_thread and self.client_thread.is_alive():
                # 给一点时间让线程退出，但不阻塞太久
                # 线程不是 daemon 的，这里只等待短时间以避免长时间阻塞主线程
                self.client_thread.join(timeout=0.5)
            logger.debug("C# IPC 客户端停止请求已发出")

        def send_notification(
            self,
            class_name,
            selected_students: list[SelectedStudentNotificationInfo],
            draw_count=1,
            settings=None,
            settings_group=None,
            is_animating=False,
        ) -> bool:
            """发送提醒"""
            if not self.is_running:
                return False

            if not self.is_connected:
                return False

            coerced_students: list[SelectedStudentNotificationInfo] = []
            for student in selected_students or []:
                item = self._coerce_student(student)
                if item is None:
                    continue
                coerced_students.append(item)

            if settings:
                display_duration = settings.get("notification_display_duration", 5)
            else:
                display_duration = 5

            logger.debug(
                f"发送通知到 ClassIsland: 班级={class_name}, 选中学生={selected_students}, 抽取数量={draw_count}, 显示时长={display_duration}, 设置组={settings_group}, 是否动画={is_animating}"
            )

            randomService = GeneratedIpcFactory.CreateIpcProxy[ISecRandomService](
                self.ipc_client.Provider, self.ipc_client.PeerProxy
            )

            try:
                plugin_version = randomService.GetPluginVersion()
            except Exception as e:
                plugin_version = Version.Parse("0.0.0.1")

            logger.debug(f"插件版本为 {plugin_version}")
            if plugin_version < Version.Parse("1.2.0.0"):
                logger.warning("检测到旧版插件 (小于 1.2.0.0)，请尽快升级！")

                result = CallResult()
                result.ClassName = class_name
                result.DrawCount = draw_count
                result.DisplayDuration = display_duration
                for student in coerced_students:
                    cs_student = Student()
                    cs_student.StudentId = student["student_id"]
                    cs_student.StudentName = student["display_text"]
                    cs_student.Exists = student["exists"]
                    result.SelectedStudents.Add(cs_student)
                randomService.NotifyResult(result)

                return True

            if settings_group is None:
                notification_type = ResultType.Unknown
            elif "roll_call" in settings_group:
                notification_type = [
                    ResultType.PartialRollCall,
                    ResultType.FinishedRollCall,
                ][not is_animating]
            elif "quick_draw" in settings_group:
                notification_type = [
                    ResultType.PartialQuickDraw,
                    ResultType.FinishedQuickDraw,
                ][not is_animating]
            elif "lottery" in settings_group:
                notification_type = [
                    ResultType.PartialLottery,
                    ResultType.FinishedLottery,
                ][not is_animating]
            else:
                notification_type = ResultType.Unknown

            logger.debug(f"通知类型: {notification_type}")

            data = NotificationData()
            data.ResultType = notification_type
            data.ClassName = class_name
            data.DrawCount = draw_count
            data.DisplayDuration = display_duration
            for student in coerced_students:
                item = NotificationItem()
                item.StudentId = student["student_id"]
                item.StudentName = student["student_name"]
                item.Exists = student["exists"]
                item.HasGroup = bool(student["group_name"])
                item.GroupName = student["group_name"]
                item.IsLottery = bool(student["lottery_name"])
                item.LotteryName = student["lottery_name"]
                data.Items.Add(item)

            randomService.ShowNotification(data)
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

                if should_log and total_seconds != 0:
                    logger.debug(f"获取到的距离上课剩余时间: {total_seconds} 秒")

                return total_seconds
            except Exception as e:
                logger.error(f"获取距离上课时间失败: {e}")
                return 0

        def get_current_class_info(self) -> dict:
            """获取当前课程信息

            Returns:
                dict: 课程信息字典，包含 name, start_time, end_time, teacher, location
                      如果当前没有课程或获取失败，返回空字典
            """
            try:
                if not self.is_running or not self.is_connected:
                    return {}

                lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                    self.ipc_client.Provider, self.ipc_client.PeerProxy
                )

                # 检查是否有当前课程
                if not lessonSc.CurrentSubject:
                    logger.debug("ClassIsland 当前没有课程")
                    return {}

                # 获取课程名称
                class_name = (
                    lessonSc.CurrentSubject.Name if lessonSc.CurrentSubject else ""
                )
                # 如果获取到的是 class_name 为空 或者是 "???"，说明当前没有课程
                if not class_name or class_name.strip() == "???":
                    logger.debug("ClassIsland 当前没有课程")
                    return {}
                logger.info(f"从 ClassIsland 获取当前课程: {class_name}")
                self._last_known_subject_name = class_name
                return {"name": class_name}

            except Exception as e:
                logger.error(f"从 ClassIsland 获取课程信息失败: {e}")
                return {}

        def get_next_class_info(self) -> dict:
            """获取下一节课的课程信息

            Returns:
                dict: 课程信息字典，包含 name, start_time, end_time, teacher, location
                      如果没有下一节课或获取失败，返回空字典
            """
            try:
                if not self.is_running or not self.is_connected:
                    return {}

                lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                    self.ipc_client.Provider, self.ipc_client.PeerProxy
                )

                # 检查是否有下一节课
                if not lessonSc.NextClassSubject:
                    logger.debug("ClassIsland 没有下一节课")
                    return {}

                # 获取课程名称
                class_name = (
                    lessonSc.NextClassSubject.Name if lessonSc.NextClassSubject else ""
                )
                if not class_name or class_name.strip() == "???":
                    logger.debug("ClassIsland 下一节课名称无效")
                    return {}

                logger.info(f"从 ClassIsland 获取下一节课: {class_name}")
                return {"name": class_name}

            except Exception as e:
                logger.error(f"从 ClassIsland 获取下一节课信息失败: {e}")
                return {}

        def get_previous_class_info(self) -> dict:
            if not self._last_known_subject_name:
                return {}
            if self._last_known_subject_name.strip() == "???":
                return {}
            return {"name": self._last_known_subject_name}

        def get_elapsed_since_previous_time_point_end_seconds(self) -> int:
            try:
                if not self.is_running or not self.is_connected:
                    return 0

                lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                    self.ipc_client.Provider, self.ipc_client.PeerProxy
                )

                current_index = int(lessonSc.CurrentSelectedIndex)
                if current_index <= 0:
                    return 0

                class_plan = lessonSc.CurrentClassPlan
                if not class_plan:
                    return 0

                valid_items = class_plan.ValidTimeLayoutItems
                if not valid_items:
                    return 0

                previous_item = valid_items[current_index - 1]
                end_time = previous_item.EndTime
                elapsed = DateTime.Now.TimeOfDay - end_time
                total_seconds = int(elapsed.TotalSeconds)
                return max(0, total_seconds)
            except Exception:
                return 0

        def _on_class_test(self):
            lessonSc = GeneratedIpcFactory.CreateIpcProxy[IPublicLessonsService](
                self.ipc_client.Provider, self.ipc_client.PeerProxy
            )
            try:
                if lessonSc.CurrentSubject and lessonSc.CurrentSubject.Name:
                    name = str(lessonSc.CurrentSubject.Name)
                    if name and name.strip() != "???":
                        self._last_known_subject_name = name
            except Exception:
                pass
            logger.debug(
                f"上课 {lessonSc.CurrentSubject.Name} 时间: {lessonSc.CurrentTimeLayoutItem}"
            )

        def _run_client(self):
            """运行 C# IPC 客户端"""

            async def connect_with_retry(retry_delay: float = 5.0) -> bool:
                """尝试连接 ClassIsland IPC 服务端，失败时自动重试，直到连接成功或停止运行。

                Returns:
                    True 表示连接成功，False 表示 is_running 变为 False 而退出。
                """
                while self.is_running:
                    try:
                        task = self.ipc_client.Connect()
                        await self.loop.run_in_executor(
                            None, lambda task=task: task.Wait()
                        )
                        return True
                    except asyncio.CancelledError:
                        raise
                    except Exception as e:
                        logger.warning(
                            f"C# IPC 连接失败（ClassIsland 可能未运行），{retry_delay:.0f}s 后重试: {e}"
                        )
                        try:
                            await asyncio.sleep(retry_delay)
                        except asyncio.CancelledError:
                            raise
                return False

            async def client():
                """异步客户端"""

                self.ipc_client = IpcClient()
                self.ipc_client.JsonIpcProvider.AddNotifyHandler(
                    IpcRoutedNotifyIds.OnClassNotifyId,
                    Action(lambda: self._on_class_test()),
                )

                try:
                    if not await connect_with_retry():
                        return
                    self.is_connected = True
                    logger.debug("C# IPC 连接成功！")

                    while self.is_running:
                        await asyncio.sleep(1)

                        # logger.debug(f"stat: plugin({self._check_plugin_alive()}) ci({self._check_ci_alive()})")
                        if not self.check_plugin_alive():
                            if not self.check_ci_alive():
                                logger.debug("C# IPC 断连！重连...")
                                self.is_connected = False

                                if not await connect_with_retry():
                                    break
                                self.is_connected = True
                                logger.debug("C# IPC 连接成功！")
                            elif not self._no_plugin_logged:
                                logger.debug("未安装 SecRandom-Ci 插件。")
                                self._no_plugin_logged = True
                        else:
                            self._no_plugin_logged = False
                finally:
                    self.ipc_client = None
                    self.is_connected = False

            # 启动新的 asyncio 事件循环
            self.loop = asyncio.new_event_loop()
            asyncio.set_event_loop(self.loop)
            try:
                self.loop.run_until_complete(client())
            except asyncio.CancelledError:
                pass
            except Exception as e:
                logger.exception(f"C# IPC 客户端循环出错: {e}")
            finally:
                self.loop.close()
                self.loop = None

        def check_ci_alive(self) -> bool:
            """ClassIsland 是否正常连接"""
            try:
                lessonsService = GeneratedIpcFactory.CreateIpcProxy[
                    IPublicLessonsService
                ](self.ipc_client.Provider, self.ipc_client.PeerProxy)
                return lessonsService.IsTimerRunning
            except Exception as e:
                logger.debug(e)
                return False

        def check_plugin_alive(self) -> bool:
            """SecRandom-Ci 插件是否正常连接"""
            try:
                randomService = GeneratedIpcFactory.CreateIpcProxy[ISecRandomService](
                    self.ipc_client.Provider, self.ipc_client.PeerProxy
                )
                return randomService.IsAlive() == "Yes"
            except Exception:
                return False

        @staticmethod
        def _safe_int(value: Any) -> int:
            try:
                if value is None:
                    return 0
                return int(value)
            except Exception:
                return 0

        def _coerce_student(self, value: Any) -> SelectedStudentNotificationInfo | None:
            if isinstance(value, dict):
                student_id = self._safe_int(value.get("student_id", value.get("id", 0)))
                student_name = str(
                    value.get("student_name", value.get("name", "")) or ""
                )
                display_text = str(
                    value.get(
                        "display_text", value.get("display", value.get("text", ""))
                    )
                    or student_name
                )
                exists = bool(value.get("exists", value.get("exist", True)))
                group_name = str(value.get("group_name", value.get("group", "")) or "")
                lottery_name = str(
                    value.get(
                        "lottery_name",
                        value.get(
                            "lottery",
                            value.get("prize_name", value.get("prize", "")),
                        ),
                    )
                    or ""
                )
                return {
                    "student_id": student_id,
                    "student_name": student_name,
                    "display_text": display_text,
                    "exists": exists,
                    "group_name": group_name,
                    "lottery_name": lottery_name,
                }

            if isinstance(value, (list, tuple)) and len(value) >= 3:
                student_name = str(value[1] or "")
                return {
                    "student_id": self._safe_int(value[0]),
                    "student_name": student_name,
                    "display_text": student_name,
                    "exists": bool(value[2]),
                    "group_name": "",
                    "lottery_name": "",
                }

            return None
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
            pass

        def send_notification(
            self,
            class_name,
            selected_students,
            draw_count=1,
            settings=None,
            settings_group=None,
            is_animating=False,
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

        def get_current_class_info(self) -> dict:
            """获取当前课程信息

            Returns:
                dict: 课程信息字典，包含 name, start_time, end_time, teacher, location
                      如果当前没有课程或获取失败，返回空字典
            """
            return {}

        def get_next_class_info(self) -> dict:
            """获取下一节课的课程信息

            Returns:
                dict: 课程信息字典，包含 name, start_time, end_time, teacher, location
                      如果没有下一节课或获取失败，返回空字典
            """
            return {}

        def get_previous_class_info(self) -> dict:
            return {}

        def get_elapsed_since_previous_time_point_end_seconds(self) -> int:
            return 0

        def _on_class_test(self):
            pass

        def _run_client(self):
            """运行 C# IPC 客户端"""
            pass

        def check_ci_alive(self) -> bool:
            """ClassIsland 是否正常连接"""
            return False

        def check_plugin_alive(self) -> bool:
            """SecRandom-Ci 插件是否正常连接"""
            return False
