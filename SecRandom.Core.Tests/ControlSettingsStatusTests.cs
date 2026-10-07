using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Services.Desktop;
using SecRandom.ViewModels.SettingsPages;
using LR = SecRandom.Langs.SettingsPages.General.Control.Resources;

namespace SecRandom.Core.Tests;

/// <summary>
///     集控设置页的连接状态文案：<c>Blocked</c> 必须分"已接入但令牌被拒"与"其它阻断"两种说法。
/// </summary>
/// <remarks>
///     <para>
///         令牌被服务端撤销后，用户唯一的出路是**重新接入**；这时只说"连接被阻断"会让人以为集控坏了，
///         或者被 <c>StatusDetail</c> 指向"去登录 SECTL"——对自建集控那条路是死的。
///         所以这里钉住的是"看到的那句话指向哪个动作"。
///     </para>
///     <para>
///         状态文案在构造期就会被写入（构造函数直接读 <see cref="ControlNodeClient.LinkState" />，
///         不经过 UI 线程投递），因此单测进程里没有消息循环也能断言。
///     </para>
/// </remarks>
public sealed class ControlSettingsStatusTests
{
    [Fact]
    public async Task 令牌被拒且已接入时状态文案指向重新接入()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, source, runTask) = CreateRejectedClient();
        try
        {
            await WaitForBlockedAsync(client, cancellationToken);

            using var viewModel = CreateViewModel(client, enrolled: true);

            Assert.Equal(LR.M_Status_ReenrollRequired, viewModel.StatusText);
            // 详情也要指对路：给的是"重新接入"而不是"去登录 SECTL"。
            Assert.Equal(LR.M_Detail_EnrollmentRejected, viewModel.StatusDetail);
        }
        finally
        {
            await source.CancelAsync();
            await runTask;
        }
    }

    [Fact]
    public async Task 未接入时被阻断仍然只说连接被阻断()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, source, runTask) = CreateRejectedClient();
        try
        {
            await WaitForBlockedAsync(client, cancellationToken);

            // 没接入就没有"重新接入"这回事，别把用户指到一个他根本没填过的地方。
            using var viewModel = CreateViewModel(client, enrolled: false);

            Assert.Equal(LR.M_Status_Blocked, viewModel.StatusText);
        }
        finally
        {
            await source.CancelAsync();
            await runTask;
        }
    }

    [Fact]
    public void 需要重新接入的文案三语齐平且Designer同步()
    {
        var keys = new[] { "Resources.resx", "Resources.en-US.resx", "Resources.ja-JP.resx" }
            .Select(file => File.ReadAllText(GetRepositoryPath(
                Path.Combine("SecRandom", "Langs", "SettingsPages", "General", "Control", file))))
            .ToArray();

        foreach (var resx in keys)
        {
            Assert.Contains("M_Status_ReenrollRequired", resx, StringComparison.Ordinal);
        }

        // 三语文案各不相同（漏翻译会退化成同一句），且都不带句尾句号。
        var values = keys.Select(resx =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                resx, "<data name=\"M_Status_ReenrollRequired\"[^>]*>\\s*<value>(?<text>.*?)</value>");
            Assert.True(match.Success, "三语 resx 里都要有 M_Status_ReenrollRequired 的值。");
            return match.Groups["text"].Value;
        }).ToArray();

        Assert.Equal(3, values.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(values, value => value.EndsWith('。') || value.EndsWith('.'));

        var designer = File.ReadAllText(GetRepositoryPath(
            Path.Combine("SecRandom", "Langs", "SettingsPages", "General", "Control", "Resources.Designer.cs")));
        Assert.Contains("public static string M_Status_ReenrollRequired", designer, StringComparison.Ordinal);
    }

    private static (ControlNodeClient Client, CancellationTokenSource Source, Task RunTask) CreateRejectedClient()
    {
        var client = new ControlNodeClient(
            new RejectingTransportFactory(),
            new StaticCredentialProvider("srn_test_token"),
            new InMemoryStateStore(State()),
            new NoopDispatcher(),
            new ControlNodeClientOptions
            {
                InitialBackoff = TimeSpan.FromMilliseconds(50),
                MaxBackoff = TimeSpan.FromMilliseconds(200),
                MaxConsecutiveUnauthorized = 1
            },
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance);

        var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return (client, source, client.RunAsync(source.Token));
    }

    /// <summary>已配置且已开启远程控制的本机节点状态——否则循环根本不会去连（Disabled/not_configured）。</summary>
    private static ControlNodeState State() => new()
    {
        NodeId = "node-1",
        GroupId = "grp_1",
        ServerUrl = "ws://127.0.0.1:9/v1/node/connect",
        RemoteControlEnabled = true
    };

    private static ControlSettingsPageViewModel CreateViewModel(ControlNodeClient client, bool enrolled)
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));

        return new ControlSettingsPageViewModel(
            configHandler,
            new InMemoryStateStore(State()),
            client,
            new ControlNodeClientOptions(),
            new FakeEndpointStore(),
            new NoopLauncher(),
            NullLogger<ControlSettingsPageViewModel>.Instance,
            new FakeEnrollmentStore(enrolled));
    }

    private static async Task WaitForBlockedAsync(ControlNodeClient client, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (client.LinkState.Status == ControlNodeLinkStatus.Blocked)
                return;

            await Task.Delay(10, cancellationToken);
        }

        throw new TimeoutException($"等待 Blocked 超时（当前 {client.LinkState.Status}/{client.LinkState.Detail}）。");
    }

    private static string GetRepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecRandom.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relativePath);
    }

    /// <summary>连接一建立就被服务端拒绝：真实路径里是 WebSocket 升级响应的 401。</summary>
    private sealed class RejectingTransportFactory : IControlNodeTransportFactory
    {
        public Task<IControlNodeTransport> ConnectAsync(
            ControlNodeConnectRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<IControlNodeTransport>(new ControlNodeConnectRejectedException(HttpStatusCode.Unauthorized));
    }

    private sealed class StaticCredentialProvider(string token) : IControlNodeCredentialProvider
    {
        public Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(token);
    }

    private sealed class NoopDispatcher : IControlCommandDispatcher
    {
        public IReadOnlyList<string> DeclaredCapabilities => [];

        public bool CanExecute(string capability) => false;

        public Task<ControlCommandOutcome> ExecuteAsync(
            ControlCommandInvocation invocation,
            CancellationToken cancellationToken) =>
            Task.FromResult(ControlCommandOutcome.Failure("capability_unsupported"));
    }

    private sealed class InMemoryStateStore(ControlNodeState initial) : IControlNodeStateStore
    {
        private ControlNodeState _state = initial;

        public ControlNodeState Current => _state;

        public event EventHandler<ControlNodeState>? Changed;

        public void Update(Func<ControlNodeState, ControlNodeState> mutate)
        {
            _state = mutate(_state);
            Changed?.Invoke(this, _state);
        }
    }

    private sealed class FakeEndpointStore : IControlPlaneEndpointStore
    {
        private string _current = ControlPlaneClient.DefaultBaseUrl;

        public string Current => _current;

        public bool IsCustom { get; private set; }

        public event EventHandler<string>? Changed;

        public bool TryUpdate(string? endpoint, out string? error)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                error = "empty_endpoint";
                return false;
            }

            _current = endpoint.Trim();
            IsCustom = true;
            error = null;
            Changed?.Invoke(this, _current);
            return true;
        }

        public void ResetToDefault()
        {
            _current = ControlPlaneClient.DefaultBaseUrl;
            IsCustom = false;
            Changed?.Invoke(this, _current);
        }
    }

    private sealed class FakeEnrollmentStore : INodeEnrollmentStore
    {
        private readonly bool _enrolled;
        private int _generation;

        public FakeEnrollmentStore(bool enrolled)
        {
            _enrolled = enrolled;
            Status = enrolled
                ? new NodeEnrollmentStatus
                {
                    HasToken = true,
                    NodeId = "node-1",
                    GroupId = "grp_1",
                    GroupName = "高一（3）班",
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
                }
                : NodeEnrollmentStatus.NotEnrolled;
        }

        public NodeEnrollmentStatus Status { get; }

        public int Generation => _generation;

        public event EventHandler<NodeEnrollmentStatus>? Changed;

        public bool TryGetAccessToken(out string? accessToken)
        {
            accessToken = _enrolled ? "srn_test_token" : null;
            return _enrolled;
        }

        public bool Save(NodeEnrollmentRecord record)
        {
            _generation++;
            Changed?.Invoke(this, Status);
            return true;
        }

        public bool Clear()
        {
            _generation++;
            Changed?.Invoke(this, NodeEnrollmentStatus.NotEnrolled);
            return true;
        }
    }

    private sealed class NoopLauncher : IExternalLauncher
    {
        public bool TryOpenPath(string path) => false;

        public bool TryOpenUri(string uri) => false;
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) => config as T ?? fallback;

        public override void SaveConfig<T>(T config)
        {
        }

        public override void DeleteConfig<T>(T config)
        {
        }
    }
}
