using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Shared;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>draw.reset</c>：清的是"本轮临时记录"，不是历史记录。
/// </summary>
/// <remarks>
///     <para>
///         这条能力的名字很容易被理解成"清空记录"，所以最要紧的断言不是"清干净了"，
///         而是**历史文件一个字节都没动**：历史是名单的长期账本，丢了不可再生；
///         临时记录只是"这一轮谁被抽到过"，本来就是要被重置的。
///     </para>
/// </remarks>
public sealed class ControlDrawResetTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "SecRandom", "control-draw-reset-tests", Guid.NewGuid().ToString("N"));

    public ControlDrawResetTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    // ---------------------------------------------------------------- 载荷

    [Fact]
    public void 载荷缺省时清的是点名()
    {
        Assert.True(ControlDrawResetRequest.TryParse(null, out var request, out var reason), reason);

        Assert.Equal(ControlDrawResetRequest.TargetRollCall, request.Target);
        Assert.Null(request.ListName);
        Assert.False(request.ClearsPrizes);
    }

    [Fact]
    public void 载荷读目标与名单名()
    {
        Assert.True(ControlDrawResetRequest.TryParse(
            Parse("""{ "target": "lottery", "list_name": "  元旦抽奖  " }"""), out var request, out var reason), reason);

        Assert.Equal(ControlDrawResetRequest.TargetLottery, request.Target);
        Assert.Equal("元旦抽奖", request.ListName);
        Assert.True(request.ClearsPrizes);

        // quick 与 roll_call 共用学生临时记录。
        Assert.True(ControlDrawResetRequest.TryParse(Parse("""{ "target": "quick" }"""), out var quick, out _));
        Assert.False(quick.ClearsPrizes);
        Assert.Null(quick.ListName);
    }

    [Theory]
    [InlineData("""[]""", "invalid_command")]
    [InlineData("""{ "target": "homework" }""", "invalid_value:target:unsupported")]
    [InlineData("""{ "target": 3 }""", "invalid_value:target:type_mismatch")]
    [InlineData("""{ "list_name": 3 }""", "invalid_value:list_name:type_mismatch")]
    public void 非法载荷被拒绝并说明哪个字段错了(string json, string expectedReason)
    {
        Assert.False(ControlDrawResetRequest.TryParse(Parse(json), out _, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void 空白名单名等于清全部()
    {
        Assert.True(ControlDrawResetRequest.TryParse(Parse("""{ "list_name": "   " }"""), out var request, out _));

        Assert.Null(request.ListName);
    }

    // ---------------------------------------------------------------- 行为

    [Fact]
    public void 重置只清临时记录不动历史()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var handler = CreateHandler(provider);

        // 一份真实名单 + 两个已经抽到过的学生（临时记录）。
        Assert.True(catalog.ReplaceStudents("测试 1", [
            new Student { Id = "12", Name = "学生12" },
            new Student { Id = "13", Name = "学生13" }
        ]));
        temporary.RecordStudents("测试 1", string.Empty, string.Empty, [
            new Student { Id = "12", Name = "学生12" },
            new Student { Id = "13", Name = "学生13" }
        ]);
        Assert.Equal(2, temporary.GetStudentCounts("测试 1", string.Empty, string.Empty).Values.Sum());

        // 历史文件：内容与字节都要在执行前后逐字节相同。
        var historyPath = Utils.GetFilePath("history", "roll_call", "测试 1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
        var historyBytes = "{\"records\":[{\"id\":\"12\",\"name\":\"学生12\"}]}"u8.ToArray();
        File.WriteAllBytes(historyPath, historyBytes);

        var outcome = handler.Apply(new ControlDrawResetRequest(ControlDrawResetRequest.TargetRollCall, "测试 1"));

        Assert.True(outcome.Ok, outcome.Reason);
        Assert.Equal(0, temporary.GetStudentCounts("测试 1", string.Empty, string.Empty).Values.Sum());

        Assert.Equal(historyBytes, File.ReadAllBytes(historyPath));

        // 回执形状：{ target, list_name, cleared }
        var detail = outcome.Detail!.Value;
        Assert.Equal("roll_call", detail.GetProperty("target").GetString());
        Assert.Equal("测试 1", detail.GetProperty("list_name").GetString());
        Assert.Equal(2, detail.GetProperty("cleared").GetInt32());
    }

    [Fact]
    public void 重置奖品目标清的是奖品而不是学生()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var handler = CreateHandler(provider);

        Assert.True(catalog.ReplaceStudents("测试 1", [new Student { Id = "12", Name = "学生12" }]));
        Assert.True(catalog.ReplacePrizes("元旦抽奖", [new Prize { Id = "p1", Name = "一等奖", Count = 1 }]));

        temporary.RecordStudents("测试 1", string.Empty, string.Empty, [new Student { Id = "12", Name = "学生12" }]);
        temporary.RecordPrizes("元旦抽奖", [new Prize { Id = "p1", Name = "一等奖", Count = 1 }]);

        var outcome = handler.Apply(new ControlDrawResetRequest(ControlDrawResetRequest.TargetLottery, null));

        Assert.True(outcome.Ok, outcome.Reason);
        Assert.Equal(0, temporary.GetPrizeCounts("元旦抽奖").Values.Sum());
        // 奖品目标不该顺手把学生的进度也清了。
        Assert.Equal(1, temporary.GetStudentCounts("测试 1", string.Empty, string.Empty).Values.Sum());
    }

    [Fact]
    public void 名单不存在时拒绝并带上字段()
    {
        using var provider = CreateProvider();
        var handler = CreateHandler(provider);

        var outcome = handler.Apply(new ControlDrawResetRequest(ControlDrawResetRequest.TargetRollCall, "不存在的名单"));

        Assert.False(outcome.Ok);
        Assert.Equal("invalid_value:list_name:not_found", outcome.Reason);
        Assert.Equal("list_name", outcome.Detail!.Value.GetProperty("field").GetString());
    }

    [Fact]
    public void 抽取进行中拒绝重置改设置与换名单()
    {
        // 边抽边清会让"抽到了谁"与临时记录对不上；这条策略是协议行为，必须钉住。
        Assert.True(ControlDrawBusyGuard.IsRefusedWhileDrawing(ControlCapabilities.DrawReset));
        Assert.True(ControlDrawBusyGuard.IsRefusedWhileDrawing(ControlCapabilities.SettingsWrite));
        Assert.True(ControlDrawBusyGuard.IsRefusedWhileDrawing(ControlCapabilities.RosterWrite));

        Assert.False(ControlDrawBusyGuard.IsRefusedWhileDrawing(ControlCapabilities.DrawTrigger));
        Assert.False(ControlDrawBusyGuard.IsRefusedWhileDrawing(ControlCapabilities.MediaPlay));
        Assert.False(ControlDrawBusyGuard.IsRefusedWhileDrawing(ControlCapabilities.StatusRead));
    }

    [Fact]
    public void 分发器声明并接受draw_reset能力()
    {
        // 能力清单是节点对服务端的承诺：声明了却不接，服务端会下发注定失败的命令。
        var source = File.ReadAllText(GetRepositoryPath("SecRandom/Services/ControlNode/ControlCommandDispatcher.cs"));

        Assert.Contains("ControlCapabilities.DrawReset,", source, StringComparison.Ordinal);
        Assert.Contains("ControlCapabilities.DrawReset => true", source, StringComparison.Ordinal);
        Assert.Contains("drawReset.ExecuteAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 重置会同时清掉页面的展示态()
    {
        // "数据清了但页面还挂着上一轮抽到的人"= 用户看到的"重置没生效"。
        // 因此除了数据，还必须复用各页面本地点一次重置的那条路径。
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var presenter = new FakeResetPresenter();
        var handler = CreateHandler(provider, presenter);

        Assert.True(catalog.ReplaceStudents("测试 1", [new Student { Id = "12", Name = "学生12" }]));
        temporary.RecordStudents("测试 1", string.Empty, string.Empty, [new Student { Id = "12", Name = "学生12" }]);

        var outcome = handler.Apply(new ControlDrawResetRequest(ControlDrawResetRequest.TargetRollCall, "测试 1"));

        Assert.True(outcome.Ok, outcome.Reason);
        Assert.Equal(1, presenter.GateCalls);
        Assert.Equal(1, presenter.ClearCalls);
        Assert.Equal(0, temporary.GetStudentCounts("测试 1", string.Empty, string.Empty).Values.Sum());
    }

    [Fact]
    public void 需要本机验证时拒绝重置并且什么都不清()
    {
        // 远程命令绝不弹验证框；拒绝时必须"数据与展示态都没动"，否则会出现自相矛盾的中间状态。
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var presenter = new FakeResetPresenter { GateResult = RemoteDrawOutcome.Denied("local_verification_required") };
        var handler = CreateHandler(provider, presenter);

        Assert.True(catalog.ReplaceStudents("测试 1", [new Student { Id = "12", Name = "学生12" }]));
        temporary.RecordStudents("测试 1", string.Empty, string.Empty, [new Student { Id = "12", Name = "学生12" }]);

        var historyPath = Utils.GetFilePath("history", "roll_call", "测试 1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
        var historyBytes = "{\"records\":[]}"u8.ToArray();
        File.WriteAllBytes(historyPath, historyBytes);

        var outcome = handler.Apply(new ControlDrawResetRequest(ControlDrawResetRequest.TargetRollCall, "测试 1"));

        Assert.False(outcome.Ok);
        Assert.Equal("draw_denied", outcome.Reason);
        Assert.Equal(0, presenter.ClearCalls);
        Assert.Equal(1, temporary.GetStudentCounts("测试 1", string.Empty, string.Empty).Values.Sum());
        Assert.Equal(historyBytes, File.ReadAllBytes(historyPath));
    }

    [Fact]
    public void 点名与快抽共用学生记录所以两者展示态一起清()
    {
        // 只清一个页面的话，另一个页面还挂着上一轮抽到的人——数据归零、界面在说谎。
        Assert.Equal(
            [DrawResetSurface.RollCall, DrawResetSurface.Quick],
            ControlDrawResetTargets.SurfacesFor(ControlDrawResetRequest.TargetRollCall));
        Assert.Equal(
            [DrawResetSurface.RollCall, DrawResetSurface.Quick],
            ControlDrawResetTargets.SurfacesFor(ControlDrawResetRequest.TargetQuick));
        Assert.Equal(
            [DrawResetSurface.Lottery],
            ControlDrawResetTargets.SurfacesFor(ControlDrawResetRequest.TargetLottery));
    }

    [Fact]
    public void 三个页面都提供非交互的展示态重置入口()
    {
        // 展示态清理必须复用各页面既有的本地重置路径（而不是另写一套只清数据的逻辑）。
        var rollCall = File.ReadAllText(GetRepositoryPath("SecRandom/ViewModels/MainPages/RollCallPageViewModel.cs"));
        var quick = File.ReadAllText(GetRepositoryPath("SecRandom/ViewModels/MainPages/QuickDrawPageViewModel.cs"));
        var lottery = File.ReadAllText(GetRepositoryPath("SecRandom/ViewModels/MainPages/LotteryPageViewModel.cs"));

        Assert.Contains("public void ResetRemotePresentation() => ResetDrawHistoryCore(showToast: false);", rollCall, StringComparison.Ordinal);
        Assert.Contains("public void ResetRemotePresentation() => ClearHistoryCore();", quick, StringComparison.Ordinal);
        Assert.Contains("public void ResetRemotePresentation() => ResetDisplayCore(showToast: false);", lottery, StringComparison.Ordinal);

        // 闸门判定与清理分开：先判定，数据清完再清展示态。
        foreach (var source in new[] { rollCall, quick, lottery })
            Assert.Contains("public RemoteDrawOutcome? EvaluateRemoteReset()", source, StringComparison.Ordinal);

        var presenter = File.ReadAllText(GetRepositoryPath("SecRandom/Services/ControlNode/ControlPageDrawResetPresenter.cs"));
        Assert.Contains("ControlDrawResetTargets.SurfacesFor", presenter, StringComparison.Ordinal);
        Assert.Contains("ResetRemotePresentation()", presenter, StringComparison.Ordinal);
        Assert.Contains("EvaluateRemoteReset()", presenter, StringComparison.Ordinal);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ControlDrawResetHandler CreateHandler(
        ServiceProvider provider,
        IControlDrawResetPresenter? presenter = null) =>
        new(
            provider.GetRequiredService<IDrawTemporaryRecordService>(),
            provider.GetRequiredService<IProfileCatalogManager>(),
            presenter ?? new FakeResetPresenter(),
            provider.GetRequiredService<ILogger<ControlDrawResetHandler>>());

    /// <summary>假展示层：单测只需要知道"该拒绝时拒绝了、该清时清了"。</summary>
    private sealed class FakeResetPresenter : IControlDrawResetPresenter
    {
        public RemoteDrawOutcome? GateResult { get; set; }

        public int GateCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public RemoteDrawOutcome? EvaluateGate(ControlDrawResetRequest request)
        {
            GateCalls++;
            return GateResult;
        }

        public void ClearPresentation(ControlDrawResetRequest request) => ClearCalls++;
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() =>
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Utils.{name} was not found.");

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
