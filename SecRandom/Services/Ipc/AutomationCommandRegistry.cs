using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Core.Services.Ipc;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Services.ControlNode;
using SecRandom.Services.Profiles;
using SecRandom.Services.Security;
using SecRandom.Shared.Models.Ipc;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Services.Ipc;

/// <summary>
///     v2 automation command dispatcher: one catalog entry per method, one implementation per
///     group. It is deliberately the only bridge between the v2 pipe and the app's existing
///     authorization/window/draw services.
/// </summary>
public sealed class AutomationCommandRegistry(
    MainConfigHandler config,
    IFeatureAvailabilityService features,
    ISecurityService security,
    IProfileQueryService profiles,
    IProfileCatalogManager catalog,
    PageRouteResolver pages,
    ControlSettingsPatchHandler settingsPatch,
    ControlRosterReadHandler rosterRead,
    ControlRosterPushHandler rosterPush,
    IControlSettingsReportSource settingsReport,
    AutomationEventBus events,
    IpcAuditLogger audit,
    ILogger<AutomationCommandRegistry> logger)
{
    private static readonly TimeSpan DefaultAwaitTimeout = TimeSpan.FromSeconds(30);

    public async Task<object?> InvokeAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        var descriptor = AutomationCommandCatalog.Find(method);
        if (descriptor is null)
        {
            throw new AutomationRpcException(
                AutomationErrorCodes.UnsupportedMethod,
                $"未知方法：{method}。");
        }

        var stopwatch = Stopwatch.StartNew();
        var scope = descriptor.Scopes.Count > 0 ? descriptor.Scopes[0] : "read.state";

        try
        {
            var result = await ExecuteAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            audit.Write(method, scope, true, stopwatch.ElapsedMilliseconds, null);
            return result;
        }
        catch (AutomationRpcException exception)
        {
            audit.Write(method, scope, false, stopwatch.ElapsedMilliseconds, exception.Code);
            throw;
        }
        catch (OperationCanceledException)
        {
            audit.Write(method, scope, false, stopwatch.ElapsedMilliseconds, AutomationErrorCodes.Timeout);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "IPC v2 方法 {Method} 执行失败。", method);
            audit.Write(method, scope, false, stopwatch.ElapsedMilliseconds, AutomationErrorCodes.InternalError);
            throw new AutomationRpcException(
                AutomationErrorCodes.InternalError,
                "命令执行失败，详情见应用日志。");
        }
    }

    private Task<object?> ExecuteAsync(string method, JsonElement? parameters, CancellationToken cancellationToken) =>
        method switch
        {
            "system.ping" => Task.FromResult<object?>(new
            {
                pong = true,
                serverTime = DateTimeOffset.Now
            }),

            "system.info" => Task.FromResult<object?>(new
            {
                version = GlobalConstants.Version,
                protocol = AutomationCommandCatalog.ProtocolVersion,
                platform = OperatingSystem.IsWindows() ? "windows"
                    : OperatingSystem.IsMacOS() ? "macos" : "linux",
                product = "SecRandom"
            }),

            "system.capabilities" => Task.FromResult<object?>(new
            {
                protocol = AutomationCommandCatalog.ProtocolVersion,
                maxFrameBytes = SingleInstanceService.MaxAutomationFrameLength,
                eventPipe = SingleInstanceService.EventPipeName,
                methods = AutomationCommandCatalog.Methods.Select(entry => new
                {
                    method = entry.Name,
                    since = entry.Since,
                    scopes = entry.Scopes,
                    parameters = entry.Params,
                    result = entry.Result
                })
            }),

            "system.integration.status" => Task.FromResult<object?>(new
            {
                urlProtocolEnabled = config.Data.General.Basic.UrlProtocol,
                automationIpcEnabled = config.Data.General.Basic.AutomationIpcEnabled,
                singleInstancePipe = SingleInstanceService.IpcPipeName,
                eventPipe = SingleInstanceService.EventPipeName
            }),

            "state.windows" => Task.FromResult<object?>(new
            {
                main = App.IsMainWindowVisible,
                settings = App.IsSettingsWindowVisible,
                floating = App.IsFloatingWindowVisible,
                simpleMode = features.IsSimpleModeEnabled
            }),

            "state.draw" => OnUiAsync(() => Task.FromResult<object?>(new
            {
                rollCall = new
                {
                    drawing = RollCall.IsDrawing,
                    list = RollCall.SelectedStudentListName,
                    count = RollCall.DrawCount,
                    remaining = RollCall.RemainingCount,
                    maximum = RollCall.MaximumDrawCount
                },
                lottery = new
                {
                    drawing = features.IsLotteryEnabled && Lottery.IsDrawing,
                    pool = features.IsLotteryEnabled ? Lottery.SelectedPrizeListName : null,
                    count = features.IsLotteryEnabled ? Lottery.DrawCount : 0,
                    remaining = features.IsLotteryEnabled ? Lottery.RemainingCount : 0,
                    maximum = features.IsLotteryEnabled ? Lottery.MaximumDrawCount : 0,
                    enabled = features.IsLotteryEnabled
                },
                quickDraw = new
                {
                    drawing = IsQuickDrawEnabled && QuickDraw.IsDrawing,
                    last = IsQuickDrawEnabled ? ProjectRecord(QuickDraw.LastDrawnStudent?.Id, QuickDraw.LastDrawnStudent?.Name) : null
                }
            })),

            "state.runtime" => Task.FromResult<object?>(new
            {
                uptimeSeconds = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds,
                simpleMode = features.IsSimpleModeEnabled,
                lotteryEnabled = features.IsLotteryEnabled,
                drawing = RollCall.IsDrawing || (features.IsLotteryEnabled && Lottery.IsDrawing)
                          || (IsQuickDrawEnabled && QuickDraw.IsDrawing)
            }),

            "window.show" => WindowAsync(parameters, "show", cancellationToken),
            "window.hide" => WindowAsync(parameters, "hide", cancellationToken),
            "window.toggle" => WindowAsync(parameters, null, cancellationToken),
            "window.navigate" => NavigateAsync(parameters, cancellationToken),

            "settings.schema" => Task.FromResult<object?>(ReadSchema()),
            "settings.get" => Task.FromResult<object?>(new
            {
                values = ControlSettingsSchemaBuilder.ReadValues(config.Data)
            }),
            "settings.set" => SettingsSetAsync(parameters, cancellationToken),
            "settings.page.open" => SettingsPageAsync(parameters, cancellationToken),

            "roster.lists" => Task.FromResult<object?>(new
            {
                students = catalog.GetStudentListNames(),
                prizes = catalog.GetPrizeListNames()
            }),
            "roster.read" => RosterReadAsync(parameters, cancellationToken),
            "roster.write" => RosterWriteAsync(parameters, cancellationToken),
            "roster.activate" => RosterActivateAsync(parameters, cancellationToken),

            "draw.rollCall.trigger" => DrawTriggerAsync("roll_call", parameters, cancellationToken),
            "draw.rollCall.configure" => ConfigureAsync("roll_call", parameters, cancellationToken),
            "draw.rollCall.start" => DrawStartAsync("roll_call", parameters, cancellationToken),
            "draw.rollCall.stop" => DrawStopAsync("roll_call", cancellationToken),
            "draw.rollCall.reset" => DrawResetAsync("roll_call", cancellationToken),
            "draw.quick.trigger" => DrawTriggerAsync("quick", parameters, cancellationToken),
            "draw.lottery.trigger" => DrawTriggerAsync("lottery", parameters, cancellationToken),
            "draw.lottery.configure" => ConfigureAsync("lottery", parameters, cancellationToken),
            "draw.lottery.start" => DrawStartAsync("lottery", parameters, cancellationToken),
            "draw.lottery.stop" => DrawStopAsync("lottery", cancellationToken),
            "draw.lottery.reset" => DrawResetAsync("lottery", cancellationToken),
            "draw.result" => DrawResultAsync(parameters),
            "draw.await" => DrawAwaitAsync(parameters, cancellationToken),

            "history.query" => HistoryQueryAsync(parameters),
            "history.summary" => HistorySummaryAsync(parameters),

            "events.subscribe" => Task.FromResult<object?>(new
            {
                pipe = SingleInstanceService.EventPipeName,
                protocol = AutomationCommandCatalog.ProtocolVersion,
                maxSubscribers = AutomationEventServer.MaxSubscribers
            }),
            "events.unsubscribe" => Task.FromResult<object?>(new { unsubscribed = true }),
            "events.status" => Task.FromResult<object?>(new
            {
                sequence = events.CurrentSequence,
                pipe = SingleInstanceService.EventPipeName,
                maxSubscribers = AutomationEventServer.MaxSubscribers
            }),

            _ => throw new AutomationRpcException(
                AutomationErrorCodes.UnsupportedMethod,
                $"方法 {method} 尚未实现。")
        };

    private Task<object?> WindowAsync(JsonElement? parameters, string? action, CancellationToken cancellationToken)
    {
        var target = ReadString(parameters, "target") ?? "main";
        var page = ReadString(parameters, "page");
        var preview = ReadBool(parameters, "preview") ?? false;
        action ??= "toggle";

        return target switch
        {
            "main" => MainWindowAsync(action, page, cancellationToken),
            "settings" => SettingsWindowAsync(action, page, preview, cancellationToken),
            "float" or "floating" => FloatingWindowAsync(action, cancellationToken),
            _ => throw new AutomationRpcException(
                AutomationErrorCodes.InvalidParameter,
                "target 必须是 main、settings 或 float。",
                details: new { field = "target", allowed = new[] { "main", "settings", "float" } })
        };
    }

    private async Task<object?> MainWindowAsync(string action, string? page, CancellationToken cancellationToken)
    {
        var route = pages.Resolve(PageRouteKind.Main, page);
        if (!route.Success)
            throw RouteError(route);

        var authorized = await security.AuthorizeAsync(
            SecurityOperation.ToggleMainWindow,
            () =>
            {
                App.SetMainWindowVisibility(action, route.PageId);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        if (!authorized)
            throw Denied();

        return new { target = "main", action, page = route.PageId };
    }

    private async Task<object?> SettingsWindowAsync(
        string action,
        string? page,
        bool preview,
        CancellationToken cancellationToken)
    {
        var route = pages.Resolve(PageRouteKind.Settings, page);
        if (!route.Success)
            throw RouteError(route);

        var result = await security.AuthorizeSettingsAsync(
            () =>
            {
                App.SetSettingsWindowVisibility(action, route.PageId!, preview: false);
                return Task.CompletedTask;
            },
            () =>
            {
                App.SetSettingsWindowVisibility(action, route.PageId!, preview: true);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsAuthorized)
            return new { target = "settings", action, page = route.PageId, preview = false };

        if (result.PreviewOpened)
            return new { target = "settings", action, page = route.PageId, preview = true };

        throw Denied();
    }

    private async Task<object?> FloatingWindowAsync(string action, CancellationToken cancellationToken)
    {
        var authorized = await security.AuthorizeAsync(
            SecurityOperation.ToggleFloatingWindow,
            () =>
            {
                App.SetFloatingWindowVisibility(action);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        if (!authorized)
            throw Denied();

        return new { target = "float", action };
    }

    private async Task<object?> NavigateAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var target = ReadString(parameters, "target") ?? "main";
        var page = ReadString(parameters, "page");
        if (string.IsNullOrWhiteSpace(page))
            throw Missing("page");

        return target switch
        {
            "main" => await MainWindowAsync("show", page, cancellationToken).ConfigureAwait(false),
            "settings" => await SettingsWindowAsync("show", page, false, cancellationToken).ConfigureAwait(false),
            _ => throw new AutomationRpcException(
                AutomationErrorCodes.InvalidParameter,
                "window.navigate 的 target 只能是 main 或 settings。",
                details: new { field = "target" })
        };
    }

    private Task<object?> SettingsSetAsync(JsonElement? parameters, CancellationToken cancellationToken) =>
        AuthorizedSettingsWriteAsync(async () =>
        {
            var outcome = await settingsPatch.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                throw new AutomationRpcException(
                    outcome.Reason is "unknown_setting" or "not_writable"
                        ? AutomationErrorCodes.InvalidParameter
                        : AutomationErrorCodes.InvalidParameter,
                    "设置写入被拒绝。",
                    details: outcome.Detail);
            }

            events.Publish("config.changed", new { method = "settings.set" });
            return outcome.Payload;
        }, cancellationToken);

    private Task<object?> SettingsPageAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var page = ReadString(parameters, "page");
        if (string.IsNullOrWhiteSpace(page))
            throw Missing("page");

        return SettingsWindowAsync("show", page, false, cancellationToken);
    }

    private async Task<object?> RosterReadAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var outcome = await rosterRead.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (!outcome.Ok)
        {
            throw new AutomationRpcException(
                outcome.Reason is "list_not_found" or "not_found"
                    ? AutomationErrorCodes.NotFound
                    : AutomationErrorCodes.InvalidParameter,
                "名单读取失败。",
                details: outcome.Detail);
        }

        return outcome.Payload;
    }

    private Task<object?> RosterWriteAsync(JsonElement? parameters, CancellationToken cancellationToken) =>
        AuthorizedSettingsWriteAsync(async () =>
        {
            var outcome = await rosterPush.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                throw new AutomationRpcException(
                    outcome.Reason is "list_not_found" or "not_found"
                        ? AutomationErrorCodes.NotFound
                        : AutomationErrorCodes.InvalidParameter,
                    "名单写入被拒绝。",
                    details: outcome.Detail);
            }

            events.Publish("roster.changed", new { method = "roster.write" });
            return outcome.Payload;
        }, cancellationToken);

    private Task<object?> RosterActivateAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var kind = ReadString(parameters, "kind") ?? "students";
        var name = ReadString(parameters, "list") ?? ReadString(parameters, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw Missing("list");

        return AuthorizedSettingsWriteAsync(async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.Equals(kind, "prizes", StringComparison.OrdinalIgnoreCase))
                {
                    if (!catalog.PrizeListExists(name))
                        throw new AutomationRpcException(AutomationErrorCodes.NotFound, "奖池不存在。");
                    catalog.SetDefaultPrizePool(name);
                }
                else
                {
                    if (!catalog.StudentListExists(name))
                        throw new AutomationRpcException(AutomationErrorCodes.NotFound, "名单不存在。");
                    catalog.SetDefaultStudentList(name);
                }
            });

            events.Publish("roster.changed", new { method = "roster.activate", kind, name });
            return (object?)new { activated = true, kind, list = name };
        }, cancellationToken);
    }

    private async Task<object?> DrawTriggerAsync(
        string target,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        EnsureDrawFeatureEnabled(target);
        var request = ParseDrawRequest(target, parameters);
        var drawTask = target switch
        {
            "roll_call" => OnUiAsync(() => RollCall.StartRemoteDrawAsync(
                request, cancellationToken, allowInteractiveVerification: true)),
            "lottery" => OnUiAsync(() => Lottery.StartRemoteDrawAsync(
                request, cancellationToken, allowInteractiveVerification: true)),
            _ => OnUiAsync(() => QuickDraw.StartRemoteDrawAsync(
                cancellationToken, allowInteractiveVerification: true))
        };
        var outcome = await drawTask.ConfigureAwait(false);

        if (outcome.Status == RemoteDrawStatus.Drawn)
        {
            events.Publish("draw.completed", new { target, count = outcome.Drawn?.Count ?? 0 });
            return new
            {
                target,
                count = outcome.Drawn?.Count ?? 0,
                list = outcome.ListName,
                drawn = outcome.Drawn ?? []
            };
        }

        throw OutcomeError(outcome);
    }

    private Task<object?> ConfigureAsync(
        string target,
        JsonElement? parameters,
        CancellationToken cancellationToken) =>
        OnUiAsync(() =>
        {
            var listName = ReadString(parameters, "list") ?? ReadString(parameters, "list_name");
            var count = ReadInt(parameters, "count");
            var gender = ReadString(parameters, "gender");
            var group = ReadString(parameters, "group");

            if (target == "lottery")
            {
                if (!features.IsLotteryEnabled)
                    throw new AutomationRpcException(AutomationErrorCodes.FeatureDisabled, "抽奖功能已关闭。");

                if (!string.IsNullOrWhiteSpace(listName))
                {
                    if (!catalog.PrizeListExists(listName))
                        throw new AutomationRpcException(AutomationErrorCodes.NotFound, "奖池不存在。");
                    Lottery.SelectedPrizeListName = listName;
                }

                if (count is { } lotteryCount)
                    Lottery.DrawCount = ClampCount(lotteryCount, Lottery.MaximumDrawCount);
                if (gender is not null)
                    Lottery.SelectedGender = gender;
                if (group is not null)
                    Lottery.SelectedGroup = group;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(listName))
                {
                    if (!catalog.StudentListExists(listName))
                        throw new AutomationRpcException(AutomationErrorCodes.NotFound, "名单不存在。");
                    RollCall.SelectedStudentListName = listName;
                }

                if (count is { } rollCallCount)
                    RollCall.DrawCount = ClampCount(rollCallCount, RollCall.MaximumDrawCount);
                if (gender is not null)
                    RollCall.SelectedGender = gender;
                if (group is not null)
                    RollCall.SelectedGroup = group;
            }

            return Task.FromResult<object?>(new
            {
                target,
                list = target == "lottery" ? Lottery.SelectedPrizeListName : RollCall.SelectedStudentListName,
                count = target == "lottery" ? Lottery.DrawCount : RollCall.DrawCount,
                gender = target == "lottery" ? Lottery.SelectedGender : RollCall.SelectedGender,
                group = target == "lottery" ? Lottery.SelectedGroup : RollCall.SelectedGroup
            });
        }, cancellationToken);

    private Task<object?> DrawStartAsync(
        string target,
        JsonElement? parameters,
        CancellationToken cancellationToken) =>
        OnUiAsync(async () =>
        {
            EnsureDrawFeatureEnabled(target);
            if (target == "lottery")
            {
                var started = await Lottery.StartProtocolDrawAsync(protectLinkage: true).ConfigureAwait(false);
                if (!started)
                    throw new AutomationRpcException(AutomationErrorCodes.InvalidState, "抽奖未能开始。");
            }
            else
            {
                var started = await RollCall.StartProtocolDrawAsync(protectLinkage: true).ConfigureAwait(false);
                if (!started)
                    throw new AutomationRpcException(AutomationErrorCodes.InvalidState, "点名未能开始。");
            }

            events.Publish("draw.started", new { target });
            return (object?)new { target, state = "running" };
        }, cancellationToken);

    private Task<object?> DrawStopAsync(string target, CancellationToken cancellationToken) =>
        OnUiAsync(() =>
        {
            EnsureDrawFeatureEnabled(target);
            if (target == "lottery")
                Lottery.StopProtocolDraw();
            else
                RollCall.StopProtocolDraw();

            events.Publish("draw.state.changed", new { target, state = "stopped" });
            return Task.FromResult<object?>(new { target, state = "stopped" });
        }, cancellationToken);

    private Task<object?> DrawResetAsync(string target, CancellationToken cancellationToken) =>
        OnUiAsync(async () =>
        {
            EnsureDrawFeatureEnabled(target);
            if (target == "lottery")
                await Lottery.ResetProtocolDrawAsync().ConfigureAwait(false);
            else
                await RollCall.ResetProtocolDrawAsync().ConfigureAwait(false);

            events.Publish("draw.state.changed", new { target, state = "reset" });
            return (object?)new { target, state = "reset" };
        }, cancellationToken);

    private Task<object?> DrawResultAsync(JsonElement? parameters) =>
        OnUiAsync(() => Task.FromResult<object?>(BuildDrawResult(ReadString(parameters, "target") ?? "roll_call")));

    private object BuildDrawResult(string target)
    {
        EnsureDrawFeatureEnabled(target);
        return target switch
        {
            "quick" => new
            {
                target,
                student = ProjectRecord(QuickDraw.LastDrawnStudent?.Id, QuickDraw.LastDrawnStudent?.Name)
            },
            "lottery" => new { target, prizes = Lottery.LastResultPrizes.Select(ProjectPrize).ToArray() },
            _ => new { target, students = RollCall.LastResultStudents.Select(ProjectStudent).ToArray() }
        };
    }

    private Task<object?> DrawAwaitAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var target = ReadString(parameters, "target") ?? "roll_call";
        EnsureDrawFeatureEnabled(target);
        var timeoutMs = ReadInt(parameters, "timeout_ms");
        var timeout = timeoutMs is { } value and > 0
            ? TimeSpan.FromMilliseconds(Math.Min(value, 120_000))
            : DefaultAwaitTimeout;

        return AwaitDrawAsync(target, timeout, cancellationToken);
    }

    private async Task<object?> AwaitDrawAsync(string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var drawing = await OnUiAsync(() => Task.FromResult(target switch
            {
                "lottery" => Lottery.IsDrawing,
                "quick" => QuickDraw.IsDrawing,
                _ => RollCall.IsDrawing
            })).ConfigureAwait(false);

            if (!drawing)
                return await OnUiAsync(() => Task.FromResult(BuildDrawResult(target))).ConfigureAwait(false);

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        throw new AutomationRpcException(AutomationErrorCodes.Timeout, "等待抽取结果超时。", retryable: true);
    }

    private async Task<object?> HistoryQueryAsync(JsonElement? parameters)
    {
        var kind = ReadString(parameters, "kind") ?? "roll_call";
        var name = ReadString(parameters, "name") ?? ReadString(parameters, "list");
        if (string.IsNullOrWhiteSpace(name))
            throw Missing("name");

        return await OnUiAsync(() => Task.FromResult<object?>(BuildHistory(kind, name, summary: false)));
    }

    private async Task<object?> HistorySummaryAsync(JsonElement? parameters)
    {
        var kind = ReadString(parameters, "kind") ?? "roll_call";
        var name = ReadString(parameters, "name") ?? ReadString(parameters, "list");
        if (string.IsNullOrWhiteSpace(name))
            throw Missing("name");

        return await OnUiAsync(() => Task.FromResult<object?>(BuildHistory(kind, name, summary: true)));
    }

    private object BuildHistory(string kind, string name, bool summary)
    {
        if (string.Equals(kind, "prizes", StringComparison.OrdinalIgnoreCase))
        {
            var history = profiles.LoadPrizeHistory(name)
                          ?? throw new AutomationRpcException(AutomationErrorCodes.NotFound, "奖品历史不存在。");
            var records = history.Prizes.Values.SelectMany(entry => entry.Histories).ToList();
            return summary
                ? new { kind, name, rounds = records.Select(r => r.DrawRoundId).Distinct().Count(), count = records.Count }
                : ProjectHistory(records);
        }

        var studentHistory = profiles.LoadStudentHistory(name)
                             ?? throw new AutomationRpcException(AutomationErrorCodes.NotFound, "点名历史不存在。");
        var items = studentHistory.Students.Values.SelectMany(entry => entry.Histories).ToList();
        return summary
            ? new { kind, name, rounds = items.Select(r => r.DrawRoundId).Distinct().Count(), count = items.Count }
            : ProjectHistory(items);
    }

    private static object ProjectHistory(IReadOnlyCollection<HistoryItem> items) => new
    {
        entries = items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DrawRoundId)
                ? $"legacy:{item.DrawTime:O}:{item.DrawNumbers}:{item.DrawMethod}"
                : item.DrawRoundId, StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(item => item.DrawTime))
            .Select(group => new
            {
                time = group.Max(item => item.DrawTime),
                students = group.Select(item => ProjectRecord(item.RecordNumber, item.RecordName)).ToArray()
            })
            .ToArray()
    };

    private static object? ProjectRecord(string? id, string? name) => new { id, name };

    private static object ProjectStudent(Student student) => new
    {
        id = student.Id,
        name = student.Name,
        gender = student.Gender
    };

    private static object ProjectPrize(Prize prize) => new
    {
        id = prize.Id,
        name = prize.Name
    };

    private object ReadSchema()
    {
        var report = settingsReport.Build();
        if (report is not null)
            return new { revision = report.Revision, schema = report.Schema };

        var built = ControlSettingsSchemaBuilder.Build(config.Data);
        return new { revision = 0, schema = built.Schema };
    }

    private async Task<object?> AuthorizedSettingsWriteAsync(
        Func<Task<object?>> action,
        CancellationToken cancellationToken)
    {
        object? result = null;
        var authorized = await security.AuthorizeAsync(
            SecurityOperation.OpenSettings,
            async () => result = await action().ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return authorized ? result : throw Denied();
    }

    internal static ControlDrawTriggerRequest ParseDrawRequest(string target, JsonElement? parameters)
    {
        if (parameters is null || parameters.Value.ValueKind == JsonValueKind.Null)
            return target == "quick"
                ? ControlDrawTriggerRequest.QuickTarget
                : new ControlDrawTriggerRequest(target, null, null, null, null);

        var element = parameters.Value;
        if (element.ValueKind != JsonValueKind.Object)
            throw new AutomationRpcException(AutomationErrorCodes.InvalidParameter, "抽取参数必须是对象。");

        var fields = new Dictionary<string, JsonElement> { ["target"] = JsonSerializer.SerializeToElement(target) };
        if (element.TryGetProperty("list", out var list) || element.TryGetProperty("list_name", out list))
            fields["list_name"] = list;
        foreach (var name in new[] { "count", "gender", "group", "conditions" })
        {
            if (element.TryGetProperty(name, out var value))
                fields[name] = value;
        }
        var normalized = JsonSerializer.SerializeToElement(fields);

        if (!ControlDrawTriggerRequest.TryParse(normalized, out var request, out var reason))
        {
            throw new AutomationRpcException(
                AutomationErrorCodes.InvalidParameter,
                reason,
                details: new { field = "draw" });
        }

        return request;
    }

    private static AutomationRpcException OutcomeError(RemoteDrawOutcome outcome) =>
        outcome.Reason switch
        {
            "busy" => new AutomationRpcException(
                AutomationErrorCodes.Busy,
                "正在抽取中。",
                retryable: true,
                details: new { drawing = true }),
            "draw_denied" => new AutomationRpcException(
                outcome.Cause is "blocked_by_class_time" or "draw_locked"
                    ? AutomationErrorCodes.FeatureDisabled
                    : AutomationErrorCodes.LocalVerificationRequired,
                "本次抽取被本机拒绝。",
                retryable: true,
                details: new { cause = outcome.Cause }),
            "draw_locked" => new AutomationRpcException(
                AutomationErrorCodes.FeatureDisabled,
                "设备处于集控锁定状态。",
                retryable: true,
                details: new { draw_locked = true }),
            _ => new AutomationRpcException(
                AutomationErrorCodes.InvalidState,
                outcome.Reason ?? "抽取未完成。",
                details: new { reason = outcome.Reason })
        };

    private static AutomationRpcException RouteError(PageRouteResult route) => new(
        route.ErrorCode ?? AutomationErrorCodes.PageNotFound,
        route.Message ?? "页面不可用。",
        details: new { page = route.PageId });

    private static AutomationRpcException Denied() => new(
        AutomationErrorCodes.AuthorizationDenied,
        "本机安全验证未通过。",
        retryable: true);

    private static AutomationRpcException Missing(string field) => new(
        AutomationErrorCodes.MissingParameter,
        $"缺少参数 {field}。",
        details: new { field });

    private static int ClampCount(int value, int maximum) =>
        value < 1 ? 1 : value > maximum ? maximum : value;

    private static Task<T> OnUiAsync<T>(Func<Task<T>> action) =>
        Dispatcher.UIThread.InvokeAsync(action);

    private static Task<T> OnUiAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).GetTask().Unwrap();

    private static string? ReadString(JsonElement? parameters, string name)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String)
            return null;

        var value = (element.GetString() ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private static int? ReadInt(JsonElement? parameters, string name)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value))
            return null;

        return value;
    }

    private static bool? ReadBool(JsonElement? parameters, string name)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty(name, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private RollCallPageViewModel RollCall => IAppHost.GetService<RollCallPageViewModel>();

    private static bool IsQuickDrawEnabled => IAppHost.TryGetService<SecRandom.Core.Services.FeatureConfiguration.FeatureRuntimeSnapshot>()
        ?.IsEnabled(SecRandom.Core.Services.FeatureConfiguration.StartupFeatureCatalog.QuickDraw) != false;

    private void EnsureDrawFeatureEnabled(string target)
    {
        if ((target == "lottery" && !features.IsLotteryEnabled) || (target == "quick" && !IsQuickDrawEnabled))
            throw new AutomationRpcException(AutomationErrorCodes.FeatureDisabled, "抽取功能已关闭。");
    }

    private LotteryPageViewModel Lottery => IAppHost.GetService<LotteryPageViewModel>();

    private QuickDrawPageViewModel QuickDraw => IAppHost.GetService<QuickDrawPageViewModel>();
}
