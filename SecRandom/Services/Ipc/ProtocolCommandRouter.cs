using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Ipc;
using SecRandom.Shared.Models.Ipc;
using SecRandom.Shared.Models.Profile;
using SecRandom.Services.Security;
using SecRandom.Services.Profiles;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Services.Ipc;

public sealed class ProtocolCommandRouter(
    MainConfigHandler configHandler,
    RollCallPageViewModel rollCall,
    LotteryPageViewModel lottery,
    QuickDrawPageViewModel quickDraw,
    IProfileQueryService profileQuery,
    ISecurityService security)
{
    private static readonly Dictionary<string, string> MainPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roll_call_page"] = "main.rollCall", ["roll"] = "main.rollCall",
        ["lottery_page"] = "main.lottery", ["lottery"] = "main.lottery",
        ["history_page"] = "main.history", ["history"] = "main.history"
    };

    private static readonly Dictionary<string, string> SettingsPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["basicsettingsinterface"] = "settings.general.basic",
        ["listmanagementinterface"] = "settings.listManagement.rollCallList",
        ["extractionsettingsinterface"] = "settings.picking.default",
        ["floatingwindowmanagementinterface"] = "settings.personalized.floatingWindow",
        ["notificationsettingsinterface"] = "settings.notification.legacy",
        ["safetysettingsinterface"] = "settings.general.security",
        ["customsettingsinterface"] = "settings.more", ["moresettingsinterface"] = "settings.more",
        ["voicesettingsinterface"] = "settings.notification.voiceMusic",
        ["historyinterface"] = "settings.history.management",
        ["updateinterface"] = "settings.update", ["aboutinterface"] = "settings.about"
    };

    public Task<IpcResponseEnvelope> HandleIpcAsync(IpcRequestEnvelope request, CancellationToken cancellationToken)
    {
        return HandleAsync(request.Payload.Url, false, cancellationToken);
    }

    public async Task HandleUrlAsync(string url)
    {
        await HandleAsync(url, true, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<IpcResponseEnvelope> HandleAsync(string value, bool isUrlActivation, CancellationToken cancellationToken)
    {
        if (isUrlActivation && !configHandler.Data.General.Basic.UrlProtocol)
            return Failure("url", "protocol_disabled", "URL 协议未启用。");
        if (!ProtocolRequestParser.TryParse(value, isUrlActivation, out var request, out var failure))
            return Failure("url", failure!.Code, failure.Message);

        if (request!.Route.StartsWith("data/", StringComparison.Ordinal))
        {
            if (isUrlActivation)
                return Success("数据查询仅支持 IPC。");
            return await Task.Run(() => HandleDataQuery(request.Route, request.Query), cancellationToken).ConfigureAwait(false);
        }

        return await Dispatcher.UIThread.InvokeAsync(
            () => HandleOnUiThreadAsync(request, cancellationToken), DispatcherPriority.Normal).ConfigureAwait(false);
    }

    private async Task<IpcResponseEnvelope> HandleOnUiThreadAsync(ParsedProtocolRequest request, CancellationToken cancellationToken)
    {
        var route = request.Route;
        return route switch
        {
            "window/main" => await HandleMainWindowAsync(request.Query, cancellationToken),
            "window/settings" => await HandleSettingsWindowAsync(request.Query, cancellationToken),
            "settings" => await HandleSettingsWindowAsync(request.Query, cancellationToken),
            "settings/basic" => await HandleSettingsWindowAsync(WithPage(request.Query, "basicSettingsInterface"), cancellationToken),
            "settings/list" => await HandleSettingsWindowAsync(WithPage(request.Query, "listManagementInterface"), cancellationToken),
            "settings/extraction" => await HandleSettingsWindowAsync(WithPage(request.Query, "extractionSettingsInterface"), cancellationToken),
            "settings/floating" => await HandleSettingsWindowAsync(WithPage(request.Query, "floatingWindowManagementInterface"), cancellationToken),
            "settings/notification" => await HandleSettingsWindowAsync(WithPage(request.Query, "notificationSettingsInterface"), cancellationToken),
            "settings/safety" => await HandleSettingsWindowAsync(WithPage(request.Query, "safetySettingsInterface"), cancellationToken),
            "settings/custom" => await HandleSettingsWindowAsync(WithPage(request.Query, "customSettingsInterface"), cancellationToken),
            "settings/voice" => await HandleSettingsWindowAsync(WithPage(request.Query, "voiceSettingsInterface"), cancellationToken),
            "settings/history" => await HandleSettingsWindowAsync(WithPage(request.Query, "historyInterface"), cancellationToken),
            "settings/more" => await HandleSettingsWindowAsync(WithPage(request.Query, "moreSettingsInterface"), cancellationToken),
            "settings/update" => await HandleSettingsWindowAsync(WithPage(request.Query, "updateInterface"), cancellationToken),
            "settings/about" => await HandleSettingsWindowAsync(WithPage(request.Query, "aboutInterface"), cancellationToken),
            "window/float" => await HandleFloatingWindowAsync(request.Query, cancellationToken),
            "tray/toggle" => await RunAuthorizedAsync(SecurityOperation.ToggleMainWindow, () => { App.SetMainWindowVisibility("toggle"); return Task.CompletedTask; }, "主窗口显示状态已切换", cancellationToken),
            "tray/settings" => await HandleSettingsWindowAsync([new ProtocolQueryItem("action", "show")], cancellationToken),
            "tray/float" => await RunAuthorizedAsync(SecurityOperation.ToggleFloatingWindow, () => { App.SetFloatingWindowVisibility("toggle"); return Task.CompletedTask; }, "浮窗显示状态已切换", cancellationToken),
            "tray/restart" => await RunAuthorizedAsync(SecurityOperation.RestartApplication, () => { App.Current.Restart(); return Task.CompletedTask; }, "程序正在重启", cancellationToken),
            "tray/exit" => await RunAuthorizedAsync(SecurityOperation.ExitApplication, () => { App.Current.Stop(); return Task.CompletedTask; }, "程序正在退出", cancellationToken),
            _ when route.StartsWith("roll_call/", StringComparison.Ordinal) => await HandleRollCallAsync(route, request.Query, cancellationToken),
            _ when route.StartsWith("lottery/", StringComparison.Ordinal) => await HandleLotteryAsync(route, request.Query, cancellationToken),
            _ => Failure("url", "invalid_command", "不支持的协议命令。")
        };
    }

    private async Task<IpcResponseEnvelope> HandleMainWindowAsync(IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        var page = ProtocolRequestParser.GetLast(query, "page", "page_name", "name", "value");
        if (page is not null && !MainPages.TryGetValue(page, out page))
            return Failure("url", "invalid_parameter", "主窗口页面参数无效。");
        var action = ParseAction(query, page is null ? "toggle" : "show");
        if (action is null) return Failure("url", "invalid_parameter", "窗口动作参数无效。");
        return await RunAuthorizedAsync(SecurityOperation.ToggleMainWindow, () =>
        {
            App.SetMainWindowVisibility(action, page);
            return Task.CompletedTask;
        }, "已请求主窗口操作", token);
    }

    private async Task<IpcResponseEnvelope> HandleSettingsWindowAsync(IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        var page = ProtocolRequestParser.GetLast(query, "page", "page_name", "name", "value");
        if (page is not null && !SettingsPages.TryGetValue(page, out page))
            return Failure("url", "invalid_parameter", "设置页面参数无效。");
        var action = ParseAction(query, "toggle");
        if (action is null) return Failure("url", "invalid_parameter", "窗口动作参数无效。");
        if (action == "hide")
        {
            return await RunAuthorizedAsync(SecurityOperation.OpenSettings, () =>
            {
                App.SetSettingsWindowVisibility(action, page ?? "settings.general.basic", false);
                return Task.CompletedTask;
            }, "设置窗口已隐藏", token);
        }

        var pageId = page ?? "settings.general.basic";
        var authorization = await security.AuthorizeSettingsAsync(
            () =>
            {
                App.SetSettingsWindowVisibility(action, pageId, false);
                return Task.CompletedTask;
            },
            () =>
            {
                App.SetSettingsWindowVisibility(action, pageId, true);
                return Task.CompletedTask;
            }, token);
        return authorization.PreviewOpened
            ? Success("已打开只读设置预览", new { preview = true })
            : authorization.IsAuthorized
                ? Success("已请求设置窗口操作")
                : Failure("url", "authorization_denied", "操作未获授权。", true);
    }

    private async Task<IpcResponseEnvelope> HandleFloatingWindowAsync(IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        if (ProtocolRequestParser.GetLast(query, "page", "page_name", "name", "value") is not null)
            return Failure("url", "invalid_parameter", "浮窗不支持页面参数。", true);
        var action = ParseAction(query, "toggle");
        return action is null
            ? Failure("url", "invalid_parameter", "窗口动作参数无效。")
            : await RunAuthorizedAsync(SecurityOperation.ToggleFloatingWindow, () =>
            {
                App.SetFloatingWindowVisibility(action);
                return Task.CompletedTask;
            }, "已请求浮窗操作", token);
    }

    private async Task<IpcResponseEnvelope> HandleRollCallAsync(string route, IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        switch (route)
        {
            case "roll_call/start": return await StartAuthorizedAsync(
                SecurityOperation.RollCallStart,
                () => !rollCall.IsDrawing && rollCall.CanStartDraw,
                rollCall.StartProtocolDrawAsync,
                "点名已开始",
                token);
            case "roll_call/stop": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () =>
            {
                rollCall.StopProtocolDraw();
                return Task.CompletedTask;
            }, "点名已停止", token);
            case "roll_call/reset": return await RunAuthorizedAsync(SecurityOperation.RollCallReset, rollCall.ResetProtocolDrawAsync, "点名已重置", token);
            case "roll_call/quick_draw": return await HandleQuickDrawAsync(token);
            case "roll_call/set_count": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetCount(
                query, "点名人数", rollCall.TotalCount, rollCall.RemainingCount, rollCall.MaximumDrawCount, value => rollCall.DrawCount = value), "点名人数已设置", token);
            case "roll_call/set_group": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetGroup(
                query, "点名分组", rollCall.GroupOptions, value => rollCall.SelectedGroup = value), "点名分组已设置", token);
            case "roll_call/set_gender": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetGender(
                query, "点名性别筛选", rollCall.GenderOptions, value => rollCall.SelectedGender = value), "点名性别筛选已设置", token);
            case "roll_call/set_list": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetStudentList(
                query, rollCall.StudentListNames, value => rollCall.SelectedStudentListName = value), "学生名单已设置", token);
            default: return Failure("url", "invalid_command", "不支持的点名命令。", true);
        }
    }

    private async Task<IpcResponseEnvelope> HandleLotteryAsync(string route, IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        switch (route)
        {
            case "lottery/start": return await StartAuthorizedAsync(
                SecurityOperation.LotteryStart,
                () => !lottery.IsDrawing && lottery.CanStartDraw,
                lottery.StartProtocolDrawAsync,
                "抽奖已开始",
                token);
            case "lottery/stop": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () =>
            {
                lottery.StopProtocolDraw();
                return Task.CompletedTask;
            }, "抽奖已停止", token);
            case "lottery/reset": return await RunAuthorizedAsync(SecurityOperation.LotteryReset, lottery.ResetProtocolDrawAsync, "抽奖已重置", token);
            case "lottery/set_count": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetCount(
                query, "抽奖人数", lottery.TotalCount, lottery.RemainingCount, lottery.MaximumDrawCount, value => lottery.DrawCount = value), "抽奖人数已设置", token);
            case "lottery/set_pool": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetPrizePool(query), "奖池已设置", token);
            case "lottery/set_list": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetStudentList(
                query, lottery.StudentListNames, value => lottery.SelectedStudentListName = value), "学生名单已设置", token);
            case "lottery/set_group": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetLotteryGroup(query), "抽奖分组已设置", token);
            case "lottery/set_gender": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetLotteryGender(query), "抽奖性别筛选已设置", token);
            default: return Failure("url", "invalid_command", "不支持的抽奖命令。", true);
        }
    }

    private IpcResponseEnvelope HandleDataQuery(string route, IReadOnlyList<ProtocolQueryItem> query)
    {
        var name = ProtocolRequestParser.GetLast(query, "class_name", "classname", "class", "pool_name", "poolname", "pool", "name", "list_name");
        if (string.IsNullOrWhiteSpace(name)) return Failure("url", "missing_parameter", "缺少名单或奖池名称。", true);
        return route switch
        {
            "data/roll_call_list" => LoadStudents(name),
            "data/lottery_list" => LoadPrizes(name),
            "data/roll_call_history" => LoadStudentHistory(name),
            "data/lottery_history" => LoadPrizeHistory(name),
            _ => Failure("url", "invalid_command", "不支持的数据查询命令。", true)
        };
    }

    private IpcResponseEnvelope LoadStudents(string name)
    {
        var list = profileQuery.LoadStudentList(name);
        if (list is null)
            return Failure("url", "not_found", "未找到点名名单。", true);
        var data = list.Students.Where(student => student.Exists)
            .Select(student => new IpcRecordDto(student.Id, student.Name, student.Gender)).ToList();
        return Success("点名名单获取成功", data);
    }

    private IpcResponseEnvelope LoadPrizes(string name)
    {
        var list = profileQuery.LoadPrizeList(name);
        if (list is null)
            return Failure("url", "not_found", "未找到抽奖奖池。", true);
        var data = list.Prizes.Where(prize => prize.Exists)
            .Select(prize => new IpcRecordDto(prize.Id, prize.Name, string.Empty)).ToList();
        return Success("抽奖名单获取成功", data);
    }

    private IpcResponseEnvelope LoadStudentHistory(string name)
    {
        var history = profileQuery.LoadStudentHistory(name);
        if (history is null)
            return Failure("url", "not_found", "未找到点名历史。", true);
        var data = history.Students.Values.SelectMany(item => item.Histories)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DrawRoundId) ? $"legacy:{item.DrawTime:O}:{item.DrawNumbers}:{item.DrawMethod}" : item.DrawRoundId)
            .OrderByDescending(group => group.Max(item => item.DrawTime))
            .ThenByDescending(group => group.Key)
            .Select(group => new IpcHistoryEntryDto(group.Max(item => item.DrawTime).ToString("O"), group.Select(item => new IpcHistoryRecordDto(item.RecordNumber, item.RecordName)).ToList())).ToList() ?? [];
        return Success("点名历史获取成功", data);
    }

    private IpcResponseEnvelope LoadPrizeHistory(string name)
    {
        var history = profileQuery.LoadPrizeHistory(name);
        if (history is null)
            return Failure("url", "not_found", "未找到抽奖历史。", true);
        var data = history.Prizes.Values.SelectMany(item => item.Histories)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DrawRoundId) ? $"legacy:{item.DrawTime:O}:{item.DrawNumbers}" : item.DrawRoundId)
            .OrderByDescending(group => group.Max(item => item.DrawTime))
            .ThenByDescending(group => group.Key)
            .Select(group => new IpcHistoryEntryDto(
                group.Max(item => item.DrawTime).ToString("O"),
                null,
                group.Select(item => new IpcHistoryRecordDto(item.RecordNumber, item.RecordName)).ToList())).ToList() ?? [];
        return Success("抽奖历史获取成功", data);
    }

    private static Task SetCount(
        IReadOnlyList<ProtocolQueryItem> query,
        string label,
        int totalCount,
        int remainingCount,
        int maximumDrawCount,
        Action<int> setCount)
    {
        if (totalCount < 1 || remainingCount < 1)
            throw new ProtocolCommandException("invalid_state", $"{label}没有可抽取的记录。");
        if (!int.TryParse(ProtocolRequestParser.GetLast(query, "count", "draw_count", "value"), out var count)
            || count < 1 || count > maximumDrawCount)
            throw new ProtocolCommandException("invalid_parameter", $"{label}参数无效。");
        setCount(count);
        return Task.CompletedTask;
    }

    private static Task SetGroup(
        IReadOnlyList<ProtocolQueryItem> query,
        string label,
        IEnumerable<string> options,
        Action<string> setGroup)
    {
        var values = options.ToArray();
        var group = ResolveOption(
            ProtocolRequestParser.GetLast(query, "group", "group_name", "name", "text", "value"),
            ProtocolRequestParser.GetLast(query, "group_index", "index"),
            values,
            "all");
        if (group is null)
            throw new ProtocolCommandException("invalid_parameter", $"{label}参数无效。");
        setGroup(group);
        return Task.CompletedTask;
    }

    private static Task SetGender(
        IReadOnlyList<ProtocolQueryItem> query,
        string label,
        IEnumerable<string> options,
        Action<string> setGender)
    {
        var values = options.ToArray();
        var value = ProtocolRequestParser.GetLast(query, "gender", "name", "text", "value");
        var gender = value?.ToLowerInvariant() switch
        {
            "all" => values.FirstOrDefault(),
            "male" => values.FirstOrDefault(item => string.Equals(item, "男", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item, "male", StringComparison.OrdinalIgnoreCase)),
            "female" => values.FirstOrDefault(item => string.Equals(item, "女", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item, "female", StringComparison.OrdinalIgnoreCase)),
            _ => ResolveOption(value, ProtocolRequestParser.GetLast(query, "gender_index", "index"), values, null)
        };
        if (gender is null)
            throw new ProtocolCommandException("invalid_parameter", $"{label}参数无效。");
        setGender(gender);
        return Task.CompletedTask;
    }

    private static Task SetStudentList(
        IReadOnlyList<ProtocolQueryItem> query,
        IEnumerable<string> options,
        Action<string> setStudentList)
    {
        var name = ProtocolRequestParser.GetLast(query, "class_name", "classname", "class", "list_name", "name", "text", "value");
        var values = options.ToArray();
        var selected = ResolveOption(name, ProtocolRequestParser.GetLast(query, "list_index", "index"), values, null);
        if (selected is null)
            throw new ProtocolCommandException("invalid_parameter", "学生名单参数无效。");
        setStudentList(selected);
        return Task.CompletedTask;
    }

    private Task SetPrizePool(IReadOnlyList<ProtocolQueryItem> query)
    {
        var name = ProtocolRequestParser.GetLast(query, "pool_name", "poolname", "pool", "name", "text", "value");
        var selected = ResolveOption(name, ProtocolRequestParser.GetLast(query, "pool_index", "index"), lottery.PrizeListNames.ToArray(), null);
        if (selected is null)
            throw new ProtocolCommandException("invalid_parameter", "奖池参数无效。");
        lottery.SelectedPrizeListName = selected;
        return Task.CompletedTask;
    }

    private Task SetLotteryGroup(IReadOnlyList<ProtocolQueryItem> query)
    {
        if (!lottery.IsStudentAssignmentEnabled)
            throw new ProtocolCommandException("invalid_state", "抽奖未启用学生分配名单。");
        return SetGroup(query, "抽奖分组", lottery.GroupOptions, value => lottery.SelectedGroup = value);
    }

    private Task SetLotteryGender(IReadOnlyList<ProtocolQueryItem> query)
    {
        if (!lottery.IsStudentAssignmentEnabled)
            throw new ProtocolCommandException("invalid_state", "抽奖未启用学生分配名单。");
        return SetGender(query, "抽奖性别筛选", lottery.GenderOptions, value => lottery.SelectedGender = value);
    }

    private async Task<IpcResponseEnvelope> RunAuthorizedAsync(SecurityOperation operation, Func<Task> action, string message, CancellationToken token)
    {
        try
        {
            var allowed = await security.AuthorizeAsync(operation, action, token).ConfigureAwait(true);
            return allowed ? Success(message) : Failure("url", "authorization_denied", "操作未获授权。", true);
        }
        catch (ProtocolCommandException exception)
        {
            return Failure("url", exception.Code, exception.Message, true);
        }
    }

    private async Task<IpcResponseEnvelope> StartAuthorizedAsync(
        SecurityOperation operation,
        Func<bool> canStart,
        Func<Task> start,
        string message,
        CancellationToken token)
    {
        if (!canStart())
            return Failure("url", "invalid_state", "当前状态无法开始抽取。", true);

        Task? drawTask = null;
        var allowed = await security.AuthorizeAsync(operation, () =>
        {
            drawTask = start();
            return Task.CompletedTask;
        }, token).ConfigureAwait(true);
        if (!allowed)
            return Failure("url", "authorization_denied", "操作未获授权。", true);

        _ = drawTask?.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        return Success(message, new { state = "running" });
    }

    private async Task<IpcResponseEnvelope> HandleQuickDrawAsync(CancellationToken token)
    {
        var allowed = await security.AuthorizeAsync(SecurityOperation.QuickDrawStart, quickDraw.StartProtocolDrawAsync, token).ConfigureAwait(true);
        if (!allowed)
            return Failure("url", "authorization_denied", "操作未获授权。", true);

        var student = quickDraw.LastDrawnStudent;
        return student is null
            ? Failure("url", "invalid_state", "未产生闪抽结果。", true)
            : Success("点名成功", new IpcRecordDto(student.Id, student.Name, student.Gender));
    }

    private static string? ParseAction(IReadOnlyList<ProtocolQueryItem> query, string defaultAction)
    {
        var value = ProtocolRequestParser.GetLast(query, "action", "mode", "op", "do", "visible");
        if (value is null) return defaultAction;
        return value.ToLowerInvariant() switch
        {
            "show" or "open" or "1" or "true" or "yes" or "on" => "show",
            "hide" or "close" or "0" or "false" or "no" or "off" => "hide",
            "toggle" or "switch" => "toggle",
            _ => null
        };
    }

    private static string? ResolveOption(string? value, string? indexValue, IReadOnlyList<string> options, string? allAlias)
    {
        if (string.Equals(value, allAlias, StringComparison.OrdinalIgnoreCase))
            return options.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(value))
            return options.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        return int.TryParse(indexValue, out var index) && index >= 0 && index < options.Count
            ? options[index]
            : null;
    }

    private static IReadOnlyList<ProtocolQueryItem> WithPage(IReadOnlyList<ProtocolQueryItem> query, string page)
    {
        return [.. query, new ProtocolQueryItem("page", page)];
    }

    private static IpcResponseEnvelope Success(string message, object? data = null) => new(true, "url", new IpcBusinessResult("success", message, Data: data));
    private static IpcResponseEnvelope Failure(string type, string code, string message, bool business = false) => business
        ? new(true, type, new IpcBusinessResult("error", message, code))
        : IpcResponseEnvelope.TransportFailure(type, code, message);

    private sealed class ProtocolCommandException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
