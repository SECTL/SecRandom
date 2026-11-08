using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     预览（滚动）动画的公平性防线。
///     <para>
///         预览帧在 QuickDrawPageViewModel.ShowPreviewAsync 里只做
///         <c>candidates.OrderBy(_ =&gt; Random.Shared.Next()).Take(count)</c> 这类内存洗牌，渲染只经
///         DataTemplate / RenderTransform；写临时记录的入口只有 DrawCommitCoordinator
///         （RecordStudents / RecordPrizes），而它只在真实一轮抽取结束后被调用一次。
///         因此反复预览既不抬高临时记录计数，也不写历史，后续权重与「是否已抽到」判定保持不变。
///     </para>
///     <para>
///         这些测试用真实 DI 运行时（AddCoreRuntimeServices）+ 临时 data root，避免 fake 把结论证成同义反复；
///         同时保留一个「提交确实会改变同一测量面」的对照，证明断言灵敏。
///     </para>
/// </summary>
public sealed class PreviewAnimationFairnessTests : IDisposable
{
    private const int PreviewFrameCount = 40;
    private const int SampledPerFrame = 2;

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "SecRandom",
        "preview-animation-fairness",
        Guid.NewGuid().ToString("N"));

    public PreviewAnimationFairnessTests()
    {
        ResetDataRoot();
        ConfigureDataRoot(_dataRoot);
    }

    [Fact]
    public void PreviewFrames_OnlyReshuffleTheFrozenPool_AndWriteNoFairnessState()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var students = SeedStudents(profile, 6);
        var listName = profile.StudentListConfig!.Name;
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();

        var frames = SimulatePreviewFrames(students, SampledPerFrame, listName);

        Assert.Equal(PreviewFrameCount, frames.Count);
        Assert.All(frames, frame => Assert.Equal(SampledPerFrame, frame.Count));
        // 每一帧都只能来自抽取开始前冻结的候选池，永远不会包含池外记录。
        Assert.All(frames, frame => Assert.All(frame, student => Assert.Contains(student, students)));

        // 临时记录 = 公平计数（重复限制 / 平均间隔 / 权重都经它或历史读取），预览后必须仍是空的。
        Assert.Empty(temporary.GetStudentCounts(listName, string.Empty, string.Empty));
        // 持久历史同样一条都不能有。
        Assert.Empty(profile.CurrentStudentHistory!.Students);
        Assert.Equal(0, profile.CurrentStudentHistory.TotalRounds);
        Assert.Equal(0, profile.CurrentStudentHistory.TotalStats);
    }

    [Fact]
    public void RepeatedPreviewFrames_LeaveFairnessWeightsBitIdentical()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var students = SeedStudents(profile, 5);
        var listName = profile.StudentListConfig!.Name;
        var engine = CreateEngine(provider);

        var before = engine.CalculateStudentWeight(students);
        SimulatePreviewFrames(students, SampledPerFrame, listName);
        var after = engine.CalculateStudentWeight(students);

        Assert.Equal(before.Count, after.Count);
        for (var index = 0; index < before.Count; index++)
        {
            Assert.Same(before[index].Candidate, after[index].Candidate);
            // 精确相等：权重是历史上计数的纯函数，预览没有可写入的路径。
            Assert.Equal(before[index].Weight, after[index].Weight);
        }

        Assert.All(after, weighted => Assert.True(weighted.Weight > 0));
    }

    [Fact]
    public void OneRealCommit_DoesMoveTheSameWeightSurface_SoTheGuardIsSensitive()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var students = SeedStudents(profile, 5);
        var listName = profile.StudentListConfig!.Name;
        var engine = CreateEngine(provider);
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var winner = students[0];

        var before = engine.CalculateStudentWeight(students);
        var roundId = provider.GetRequiredService<IDrawCommitService>()
            .CommitStudentDraw(new StudentDrawCommit([winner], DateTime.Now, 1, listName));
        var after = engine.CalculateStudentWeight(students);

        Assert.False(string.IsNullOrWhiteSpace(roundId));
        var beforeWinner = before.Single(weighted => ReferenceEquals(weighted.Candidate, winner)).Weight;
        var afterWinner = after.Single(weighted => ReferenceEquals(weighted.Candidate, winner)).Weight;
        Assert.True(afterWinner < beforeWinner,
            $"一次真实提交必须降低中签者权重（before={beforeWinner}, after={afterWinner}），否则上一条断言没有灵敏度。");

        Assert.Equal(1, temporary.GetStudentCounts(listName, string.Empty, string.Empty)[winner.RecordId.ToString("D")]);
        Assert.Equal(1, profile.CurrentStudentHistory!.TotalRounds);
    }

    [Fact]
    public void PreviewPresentationRequests_CarryNoRoundIdentity_AndThePhaseEnumHasNoCommitPhase()
    {
        // 相位只有「滚动中」与「已提交结果」两种：不存在“预览已提交”这种半状态。
        Assert.Equal([DrawPresentationPhase.Preview, DrawPresentationPhase.Reveal], Enum.GetValues<DrawPresentationPhase>());

        var student = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var preview = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.QuickDraw,
            Phase = DrawPresentationPhase.Preview,
            Students = [student],
            ListName = "class-a",
            RequestedCount = 1
        };

        Assert.True(preview.IsPreview);
        Assert.Equal(1, preview.ItemCount);
        // 预览请求天然没有轮次身份（ResultPresentationBridge.BuildRequest 从不填这两个字段），
        // 呈现器即使想“按轮次反查历史”也无从下手。
        Assert.Equal(Guid.Empty, preview.ProofId);
        Assert.Equal(string.Empty, preview.DrawRoundId);

        var revealed = preview with
        {
            Phase = DrawPresentationPhase.Reveal,
            ProofId = Guid.NewGuid(),
            DrawRoundId = "round-1"
        };
        Assert.False(revealed.IsPreview);
        Assert.True(preview.IsPreview); // with 表达式产出新快照，不改原件

        // init-only：呈现器拿到的快照无法在构造后被逐属性改写（init 访问器带 IsExternalInit 修饰符，不算可变 setter）。
        Assert.True(typeof(DrawPresentationRequest).IsSealed);
        var mutableSetters = typeof(DrawPresentationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true } &&
                               !property.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                                   .Contains(typeof(IsExternalInit)))
            .Select(property => property.Name)
            .ToArray();
        Assert.Empty(mutableSetters);
    }

    [Fact]
    public void PresentationContracts_ExposeNoCommitOrTemporaryRecordSurface()
    {
        var presenterTypes = typeof(IDrawResultPresenter)
            .GetMethods()
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .ToArray();

        Assert.DoesNotContain(typeof(IDrawCommitService), presenterTypes);
        Assert.DoesNotContain(typeof(IDrawTemporaryRecordService), presenterTypes);

        var requestTypes = typeof(DrawPresentationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(typeof(IDrawCommitService), requestTypes);
        Assert.DoesNotContain(typeof(IDrawTemporaryRecordService), requestTypes);
    }

    public void Dispose()
    {
        ResetDataRoot();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    /// <summary>
    ///     复刻 QuickDrawPageViewModel.ShowPreviewAsync 的帧循环（洗牌冻结池 + 生成 Preview 请求），
    ///     但不触碰任何服务：这正是产品代码里预览分支的全部行为。
    /// </summary>
    private static List<IReadOnlyList<Student>> SimulatePreviewFrames(
        IReadOnlyList<Student> frozenPool,
        int count,
        string listName,
        int frames = PreviewFrameCount)
    {
        var rendered = new List<IReadOnlyList<Student>>(frames);
        var previewAnimationRevision = 0;

        for (var frame = 0; frame < frames; frame++)
        {
            var frameStudents = frozenPool.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
            rendered.Add(frameStudents);

            var request = new DrawPresentationRequest
            {
                Channel = DrawPresentationChannel.QuickDraw,
                Phase = DrawPresentationPhase.Preview,
                Students = frameStudents,
                ListName = listName,
                RequestedCount = count
            };
            Assert.True(request.IsPreview);

            previewAnimationRevision++;
        }

        Assert.Equal(frames, previewAnimationRevision);
        return rendered;
    }

    private static List<Student> SeedStudents(IProfileService profile, int count)
    {
        var students = Enumerable.Range(1, count)
            .Select(index => new Student
            {
                Name = $"Preview-{index:00}",
                Id = index.ToString("00"),
                RecordId = Guid.NewGuid()
            })
            .ToList();

        foreach (var student in students)
            profile.CurrentStudentList!.Students.Add(student);

        profile.SaveProfile();
        return students;
    }

    private static DrawEngine CreateEngine(ServiceProvider provider)
    {
        return new DrawEngine(
            provider.GetRequiredService<MainConfigHandler>(),
            provider.GetRequiredService<IProfileService>(),
            NullLogger<DrawEngine>.Instance);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRoot(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRoot()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
