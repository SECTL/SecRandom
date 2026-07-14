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
using SecRandom.Services;
using SecRandom.ViewModels.MainPages;
using LR = SecRandom.Langs.Ipc.Resources;

namespace SecRandom.Services.Ipc;

public sealed class ProtocolCommandRouter(
    MainConfigHandler configHandler,
    RollCallPageViewModel rollCall,
    LotteryPageViewModel lottery,
    QuickDrawPageViewModel quickDraw,
    IProfileQueryService profileQuery,
    ISecurityService security,
    FeatureAvailabilityService featureAvailability)
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
            return Failure("url", "protocol_disabled", LR.M_Disabled);
        if (!ProtocolRequestParser.TryParse(value, isUrlActivation, out var request, out var failure))
            return Failure("url", failure!.Code, failure.Message);

        if (request!.Route.StartsWith("data/", StringComparison.Ordinal))
        {
            if (isUrlActivation)
                return Success(LR.M_DataOnlyIpc);
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
            "settings" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "basicSettingsInterface"), cancellationToken),
            "settings/basic" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "basicSettingsInterface"), cancellationToken),
            "settings/list" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "listManagementInterface"), cancellationToken),
            "settings/extraction" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "extractionSettingsInterface"), cancellationToken),
            "settings/floating" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "floatingWindowManagementInterface"), cancellationToken),
            "settings/notification" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "notificationSettingsInterface"), cancellationToken),
            "settings/safety" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "safetySettingsInterface"), cancellationToken),
            "settings/custom" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "customSettingsInterface"), cancellationToken),
            "settings/voice" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "voiceSettingsInterface"), cancellationToken),
            "settings/history" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "historyInterface"), cancellationToken),
            "settings/more" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "moreSettingsInterface"), cancellationToken),
            "settings/update" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "updateInterface"), cancellationToken),
            "settings/about" => await HandleSettingsWindowAsync(WithLegacySettingsPage(request.Query, "aboutInterface"), cancellationToken),
            "window/float" => await HandleFloatingWindowAsync(request.Query, cancellationToken),
            "tray/toggle" => await RunAuthorizedAsync(SecurityOperation.ToggleMainWindow, () => { App.SetMainWindowVisibility("toggle"); return Task.CompletedTask; }, LR.M_MainToggled, cancellationToken),
            "tray/settings" => await HandleSettingsWindowAsync([new ProtocolQueryItem("action", "show")], cancellationToken),
            "tray/float" => await RunAuthorizedAsync(SecurityOperation.ToggleFloatingWindow, () => { App.SetFloatingWindowVisibility("toggle"); return Task.CompletedTask; }, LR.M_FloatingToggled, cancellationToken),
            "tray/restart" => await RunAuthorizedAsync(SecurityOperation.RestartApplication, () => { App.Current.Restart(); return Task.CompletedTask; }, LR.M_Restarting, cancellationToken),
            "tray/exit" => await RunAuthorizedAsync(SecurityOperation.ExitApplication, () => { App.Current.Stop(); return Task.CompletedTask; }, LR.M_Exiting, cancellationToken),
            _ when route.StartsWith("roll_call/", StringComparison.Ordinal) => await HandleRollCallAsync(route, request.Query, cancellationToken),
            _ when route.StartsWith("lottery/", StringComparison.Ordinal) => await HandleLotteryAsync(route, request.Query, cancellationToken),
            _ => Failure("url", "invalid_command", LR.M_UnsupportedCommand)
        };
    }

    private async Task<IpcResponseEnvelope> HandleMainWindowAsync(IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        var page = ProtocolRequestParser.GetLast(query, "page", "page_name", "name", "value");
        if (page is not null && !MainPages.TryGetValue(page, out page))
            return Failure("url", "invalid_parameter", LR.M_InvalidMainPage);
        if (page == "main.lottery" && !featureAvailability.IsLotteryEnabled)
            return Failure("url", "feature_disabled", "抽奖功能已关闭。", true);
        var action = ParseAction(query, page is null ? "toggle" : "show");
        if (action is null) return Failure("url", "invalid_parameter", LR.M_InvalidWindowAction);
        return await RunAuthorizedAsync(SecurityOperation.ToggleMainWindow, () =>
        {
            App.SetMainWindowVisibility(action, page);
            return Task.CompletedTask;
        }, LR.M_MainWindowRequested, token);
    }

    private async Task<IpcResponseEnvelope> HandleSettingsWindowAsync(IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        var page = ProtocolRequestParser.GetLast(query, "page", "page_name", "name", "value");
        if (page is not null && !SettingsPages.TryGetValue(page, out page))
            return Failure("url", "invalid_parameter", LR.M_InvalidSettingsPage);
        var action = ParseAction(query, "toggle");
        if (action is null) return Failure("url", "invalid_parameter", LR.M_InvalidWindowAction);
        if (action == "hide")
        {
            return await RunAuthorizedAsync(SecurityOperation.OpenSettings, () =>
            {
                App.SetSettingsWindowVisibility(action, page ?? "settings.general.basic", false);
                return Task.CompletedTask;
            }, LR.M_SettingsHidden, token);
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
            ? Success(LR.M_SettingsPreviewOpened, new { preview = true })
            : authorization.IsAuthorized
                ? Success(LR.M_SettingsRequested)
                : Failure("url", "authorization_denied", LR.M_AuthorizationDenied, true);
    }

    private async Task<IpcResponseEnvelope> HandleFloatingWindowAsync(IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        if (ProtocolRequestParser.GetLast(query, "page", "page_name", "name", "value") is not null)
            return Failure("url", "invalid_parameter", LR.M_FloatPageUnsupported, true);
        var action = ParseAction(query, "toggle");
        return action is null
            ? Failure("url", "invalid_parameter", LR.M_InvalidWindowAction)
            : await RunAuthorizedAsync(SecurityOperation.ToggleFloatingWindow, () =>
            {
                App.SetFloatingWindowVisibility(action);
                return Task.CompletedTask;
            }, LR.M_FloatingRequested, token);
    }

    private async Task<IpcResponseEnvelope> HandleRollCallAsync(string route, IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        switch (route)
        {
            case "roll_call/start": return await StartLinkageAsync(
                () => !rollCall.IsDrawing && rollCall.CanStartDraw,
                () => rollCall.StartProtocolDrawAsync(protectLinkage: true),
                LR.M_RollCallStarted,
                token);
            case "roll_call/stop": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () =>
            {
                rollCall.StopProtocolDraw();
                return Task.CompletedTask;
            }, LR.M_RollCallStopped, token);
            case "roll_call/reset": return await RunLinkageAsync(() => rollCall.ResetProtocolDrawAsync(protectLinkage: true), LR.M_RollCallReset, token);
            case "roll_call/quick_draw": return await HandleQuickDrawAsync(token);
            case "roll_call/set_count": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetCount(
                query, LR.L_RollCallCount, rollCall.TotalCount, rollCall.RemainingCount, rollCall.MaximumDrawCount, value => rollCall.DrawCount = value), LR.M_RollCallCountSet, token);
            case "roll_call/set_group": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetGroup(
                query, LR.L_RollCallGroup, rollCall.GroupOptions, value => rollCall.SelectedGroup = value), LR.M_RollCallGroupSet, token);
            case "roll_call/set_gender": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetGender(
                query, LR.L_RollCallGender, rollCall.GenderOptions, value => rollCall.SelectedGender = value), LR.M_RollCallGenderSet, token);
            case "roll_call/set_list": return await RunAuthorizedAsync(SecurityOperation.LinkageAction, () => SetStudentList(
                query, rollCall.StudentListNames, value => rollCall.SelectedStudentListName = value), LR.M_StudentListSet, token);
            default: return Failure("url", "invalid_command", LR.M_UnsupportedRollCall, true);
        }
    }

    private async Task<IpcResponseEnvelope> HandleLotteryAsync(string route, IReadOnlyList<ProtocolQueryItem> query, CancellationToken token)
    {
        if (!featureAvailability.IsLotteryEnabled)
            return Failure("url", "feature_disabled", "抽奖功能已关闭。", true);

        switch (route)
        {
            case "lottery/start": return await StartLinkageAsync(
                () => featureAvailability.IsLotteryEnabled && !lottery.IsDrawing && lottery.CanStartDraw,
                () => lottery.StartProtocolDrawAsync(protectLinkage: true),
                LR.M_LotteryStarted,
                token);
            case "lottery/stop": return await RunLotteryAuthorizedAsync(() =>
            {
                lottery.StopProtocolDraw();
                return Task.CompletedTask;
            }, LR.M_LotteryStopped, token);
            case "lottery/reset": return await RunLotteryLinkageAsync(() => lottery.ResetProtocolDrawAsync(protectLinkage: true), LR.M_LotteryReset, token);
            case "lottery/set_count": return await RunLotteryAuthorizedAsync(() => SetCount(
                query, LR.L_LotteryCount, lottery.TotalCount, lottery.RemainingCount, lottery.MaximumDrawCount, value => lottery.DrawCount = value), LR.M_LotteryCountSet, token);
            case "lottery/set_pool": return await RunLotteryAuthorizedAsync(() => SetPrizePool(query), LR.M_PoolSet, token);
            case "lottery/set_list": return await RunLotteryAuthorizedAsync(() => SetStudentList(
                query, lottery.StudentListNames, value => lottery.SelectedStudentListName = value), LR.M_StudentListSet, token);
            case "lottery/set_group": return await RunLotteryAuthorizedAsync(() => SetLotteryGroup(query), LR.M_LotteryGroupSet, token);
            case "lottery/set_gender": return await RunLotteryAuthorizedAsync(() => SetLotteryGender(query), LR.M_LotteryGenderSet, token);
            default: return Failure("url", "invalid_command", LR.M_UnsupportedLottery, true);
        }
    }

    private IpcResponseEnvelope HandleDataQuery(string route, IReadOnlyList<ProtocolQueryItem> query)
    {
        var name = ProtocolRequestParser.GetLast(query, "class_name", "classname", "class", "pool_name", "poolname", "pool", "name", "list_name");
        if (string.IsNullOrWhiteSpace(name)) return Failure("url", "missing_parameter", LR.M_MissingProfileName, true);
        return route switch
        {
            "data/roll_call_list" => LoadStudents(name),
            "data/lottery_list" => LoadPrizes(name),
            "data/roll_call_history" => LoadStudentHistory(name),
            "data/lottery_history" => LoadPrizeHistory(name),
            _ => Failure("url", "invalid_command", LR.M_UnsupportedData, true)
        };
    }

    private IpcResponseEnvelope LoadStudents(string name)
    {
        var list = profileQuery.LoadStudentList(name);
        if (list is null)
            return Failure("url", "not_found", LR.M_RollCallListNotFound, true);
        var data = list.Students.Where(student => student.IsCandidate)
            .Select(student => new IpcRecordDto(student.Id, student.Name, student.Gender)).ToList();
        return Success(LR.M_RollCallListLoaded, data);
    }

    private IpcResponseEnvelope LoadPrizes(string name)
    {
        var list = profileQuery.LoadPrizeList(name);
        if (list is null)
            return Failure("url", "not_found", LR.M_PrizePoolNotFound, true);
        var data = list.Prizes.Where(prize => prize.Exists)
            .Select(prize => new IpcRecordDto(prize.Id, prize.Name, string.Empty)).ToList();
        return Success(LR.M_PrizePoolLoaded, data);
    }

    private IpcResponseEnvelope LoadStudentHistory(string name)
    {
        var history = profileQuery.LoadStudentHistory(name);
        if (history is null)
            return Failure("url", "not_found", LR.M_RollCallHistoryNotFound, true);
        var data = history.Students.Values.SelectMany(item => item.Histories)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DrawRoundId) ? $"legacy:{item.DrawTime:O}:{item.DrawNumbers}:{item.DrawMethod}" : item.DrawRoundId)
            .OrderByDescending(group => group.Max(item => item.DrawTime))
            .ThenByDescending(group => group.Key)
            .Select(group => new IpcHistoryEntryDto(group.Max(item => item.DrawTime).ToString("O"), group.Select(item => new IpcHistoryRecordDto(item.RecordNumber, item.RecordName)).ToList())).ToList() ?? [];
        return Success(LR.M_RollCallHistoryLoaded, data);
    }

    private IpcResponseEnvelope LoadPrizeHistory(string name)
    {
        var history = profileQuery.LoadPrizeHistory(name);
        if (history is null)
            return Failure("url", "not_found", LR.M_PrizeHistoryNotFound, true);
        var data = history.Prizes.Values.SelectMany(item => item.Histories)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DrawRoundId) ? $"legacy:{item.DrawTime:O}:{item.DrawNumbers}" : item.DrawRoundId)
            .OrderByDescending(group => group.Max(item => item.DrawTime))
            .ThenByDescending(group => group.Key)
            .Select(group => new IpcHistoryEntryDto(
                group.Max(item => item.DrawTime).ToString("O"),
                null,
                group.Select(item => new IpcHistoryRecordDto(item.RecordNumber, item.RecordName)).ToList())).ToList() ?? [];
        return Success(LR.M_PrizeHistoryLoaded, data);
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
            throw new ProtocolCommandException("invalid_state", string.Format(LR.M_NoEligibleRecordsFormat, label));
        if (!int.TryParse(ProtocolRequestParser.GetLast(query, "count", "draw_count", "value"), out var count)
            || count < 1 || count > maximumDrawCount)
            throw new ProtocolCommandException("invalid_parameter", string.Format(LR.M_InvalidParameterFormat, label));
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
            throw new ProtocolCommandException("invalid_parameter", string.Format(LR.M_InvalidParameterFormat, label));
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
            throw new ProtocolCommandException("invalid_parameter", string.Format(LR.M_InvalidParameterFormat, label));
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
            throw new ProtocolCommandException("invalid_parameter", LR.M_InvalidStudentList);
        setStudentList(selected);
        return Task.CompletedTask;
    }

    private Task SetPrizePool(IReadOnlyList<ProtocolQueryItem> query)
    {
        var name = ProtocolRequestParser.GetLast(query, "pool_name", "poolname", "pool", "name", "text", "value");
        var selected = ResolveOption(name, ProtocolRequestParser.GetLast(query, "pool_index", "index"), lottery.PrizeListNames.ToArray(), null);
        if (selected is null)
            throw new ProtocolCommandException("invalid_parameter", LR.M_InvalidPool);
        lottery.SelectedPrizeListName = selected;
        return Task.CompletedTask;
    }

    private Task SetLotteryGroup(IReadOnlyList<ProtocolQueryItem> query)
    {
        if (!lottery.IsStudentAssignmentEnabled)
            throw new ProtocolCommandException("invalid_state", LR.M_AssignmentUnavailable);
        return SetGroup(query, LR.L_LotteryGroup, lottery.GroupOptions, value => lottery.SelectedGroup = value);
    }

    private Task SetLotteryGender(IReadOnlyList<ProtocolQueryItem> query)
    {
        if (!lottery.IsStudentAssignmentEnabled)
            throw new ProtocolCommandException("invalid_state", LR.M_AssignmentUnavailable);
        return SetGender(query, LR.L_LotteryGender, lottery.GenderOptions, value => lottery.SelectedGender = value);
    }

    private async Task<IpcResponseEnvelope> RunAuthorizedAsync(SecurityOperation operation, Func<Task> action, string message, CancellationToken token)
    {
        try
        {
            var allowed = await security.AuthorizeAsync(operation, action, token).ConfigureAwait(true);
            return allowed ? Success(message) : Failure("url", "authorization_denied", LR.M_AuthorizationDenied, true);
        }
        catch (ProtocolCommandException exception)
        {
            return Failure("url", exception.Code, exception.Message, true);
        }
    }

    private async Task<IpcResponseEnvelope> StartLinkageAsync(
        Func<bool> canStart,
        Func<Task<bool>> start,
        string message,
        CancellationToken token)
    {
        if (!canStart())
            return Failure("url", "invalid_state", LR.M_InvalidStartState, true);

        var allowed = await start().ConfigureAwait(true);
        if (!allowed)
            return Failure("url", "authorization_denied", LR.M_AuthorizationDenied, true);

        return Success(message, new { state = "running" });
    }

    private Task<IpcResponseEnvelope> RunLotteryAuthorizedAsync(Func<Task> action, string message, CancellationToken token)
    {
        return RunAuthorizedAsync(SecurityOperation.LinkageAction, () =>
        {
            if (!featureAvailability.IsLotteryEnabled)
                throw new ProtocolCommandException("feature_disabled", "抽奖功能已关闭。");
            return action();
        }, message, token);
    }

    private async Task<IpcResponseEnvelope> RunLotteryLinkageAsync(Func<Task<bool>> action, string message, CancellationToken token)
    {
        if (!featureAvailability.IsLotteryEnabled)
            return Failure("url", "feature_disabled", "抽奖功能已关闭。", true);
        return await RunLinkageAsync(async () =>
        {
            if (!featureAvailability.IsLotteryEnabled)
                return false;
            return await action().ConfigureAwait(true);
        }, message, token);
    }

    private async Task<IpcResponseEnvelope> HandleQuickDrawAsync(CancellationToken token)
    {
        var allowed = await quickDraw.StartProtocolDrawAsync(protectLinkage: true).ConfigureAwait(true);
        if (!allowed)
            return Failure("url", "authorization_denied", LR.M_AuthorizationDenied, true);

        var student = quickDraw.LastDrawnStudent;
        return student is null
            ? Failure("url", "invalid_state", LR.M_NoQuickDrawResult, true)
            : Success(LR.M_QuickDrawSucceeded, new IpcRecordDto(student.Id, student.Name, student.Gender));
    }

    private async Task<IpcResponseEnvelope> RunLinkageAsync(
        Func<Task<bool>> action,
        string message,
        CancellationToken token)
    {
        var allowed = await action().ConfigureAwait(true);
        return allowed ? Success(message) : Failure("url", "authorization_denied", LR.M_AuthorizationDenied, true);
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

    private static IReadOnlyList<ProtocolQueryItem> WithLegacySettingsPage(IReadOnlyList<ProtocolQueryItem> query, string page)
    {
        var action = ProtocolRequestParser.GetLast(query, "action", "mode", "op", "do", "visible");
        return action is null
            ? [.. query, new ProtocolQueryItem("action", "show"), new ProtocolQueryItem("page", page)]
            : [.. query, new ProtocolQueryItem("page", page)];
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
