using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Mobile;
using SecRandom.Models;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels.SettingsPages.History;
using SecRandom.Views.Mobile;
using SecRandom.Views.SettingsPages.History;

namespace SecRandom.Mobile.Tests;

public sealed class MobileHistoryPageTests
{
    private static readonly SemaphoreSlim HostGate = new(1, 1);

    [AvaloniaFact]
    public async Task LotteryTabIsHiddenWhenLotteryIsDisabled()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        try
        {
            provider = CreateProvider();
            IAppHost.Host = new TestHost(provider);

            var page = new MobileHistoryPage(new TestCapabilities(lotteryEnabled: false));

            Assert.Equal(0, page.FindControl<TabStrip>("HistoryTabs")!.SelectedIndex);
            Assert.False(page.FindControl<TabStripItem>("LotteryTab")!.IsVisible);
        }
        finally
        {
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    [AvaloniaFact]
    public async Task LotteryTabTracksFeatureAvailabilityAndReturnsToRollCall()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        try
        {
            var featureAvailability = new TestFeatureAvailability(lotteryEnabled: true);
            provider = CreateProvider(featureAvailability);
            IAppHost.Host = new TestHost(provider);
            var page = new MobileHistoryPage(new TestCapabilities(lotteryEnabled: true));
            var tabs = page.FindControl<TabStrip>("HistoryTabs")!;
            tabs.SelectedIndex = 1;

            featureAvailability.SetLotteryEnabled(false);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.False(page.FindControl<TabStripItem>("LotteryTab")!.IsVisible);
            Assert.Equal(0, tabs.SelectedIndex);
        }
        finally
        {
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    [AvaloniaFact]
    public async Task RollCallHistoryRefresh_PreservesSelectionsAndFiltersBySubject()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        try
        {
            const string listName = "二班";
            var student = new Student { RecordId = Guid.NewGuid(), Id = "01", Name = "甲" };
            var history = new StudentHistory(listName);
            history.Students[student.RecordId.ToString("D")] = new History
            {
                TotalCount = 2,
                Histories =
                [
                    new HistoryItem { CourseName = "数学", DrawTime = new DateTime(2026, 8, 1, 8, 0, 0) },
                    new HistoryItem { CourseName = "英语", DrawTime = new DateTime(2026, 8, 2, 8, 0, 0) }
                ]
            };
            var config = new MainConfigModel();
            config.LinkageSettings.DataSource = LinkageDataSource.Cses;
            config.LinkageSettings.SubjectHistoryFilterEnabled = true;
            provider = CreateProvider(
                config: config,
                historyQueryService: new TestHistoryQueryService(history),
                catalogManager: new TestProfileCatalogManager(new StudentList(listName) { Students = [student] }));
            IAppHost.Host = new TestHost(provider);

            var viewModel = provider.GetRequiredService<RollCallHistoryViewModel>();
            viewModel.SelectedSubject = "数学";

            viewModel.SelectedMode = HistoryMode.Overview;
            Assert.False(viewModel.ShouldShowSubjectColumn);
            Assert.Equal(1, Assert.Single(viewModel.Rows).TotalCount);

            viewModel.SelectedMode = HistoryMode.Records;
            Assert.True(viewModel.ShouldShowSubjectColumn);
            Assert.Equal("数学", Assert.Single(viewModel.Rows).Subject);

            viewModel.SelectedMode = student.RecordId.ToString("D");
            viewModel.Refresh();

            Assert.True(viewModel.IsSubjectFilteringAvailable);
            Assert.Equal(listName, viewModel.SelectedClassName);
            Assert.Equal(student.RecordId.ToString("D"), viewModel.SelectedMode);
            Assert.Equal("数学", viewModel.SelectedSubject);
            Assert.True(viewModel.ShouldShowSubjectColumn);
            var row = Assert.Single(viewModel.Rows);
            Assert.Equal("数学", row.Subject);
        }
        finally
        {
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    [AvaloniaFact]
    public async Task LotteryHistoryRefresh_PreservesPoolAndMode()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        try
        {
            const string poolName = "奖池";
            var prize = new Prize { RecordId = Guid.NewGuid(), Id = "01", Name = "一等奖" };
            var history = new PrizeHistory(poolName);
            history.Prizes[prize.RecordId.ToString("D")] = new History
            {
                TotalCount = 1,
                Histories = [new HistoryItem { DrawTime = new DateTime(2026, 8, 1, 8, 0, 0) }]
            };
            provider = CreateProvider(
                historyQueryService: new TestHistoryQueryService(prizeHistory: history),
                catalogManager: new TestProfileCatalogManager(prizeList: new PrizeList(poolName) { Prizes = [prize] }));
            IAppHost.Host = new TestHost(provider);

            var viewModel = provider.GetRequiredService<LotteryHistoryViewModel>();
            viewModel.SelectedMode = prize.RecordId.ToString("D");
            viewModel.Refresh();

            Assert.Equal(poolName, viewModel.SelectedPoolName);
            Assert.Equal(prize.RecordId.ToString("D"), viewModel.SelectedMode);
            Assert.Single(viewModel.Rows);
        }
        finally
        {
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    [AvaloniaFact]
    public async Task RollCallHistorySubjectFilter_UsesTheTimeViewColumns()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        Window? window = null;
        try
        {
            const string listName = "二班";
            var student = new Student { RecordId = Guid.NewGuid(), Id = "01", Name = "甲" };
            var history = new StudentHistory(listName);
            history.Students[student.RecordId.ToString("D")] = new History
            {
                TotalCount = 1,
                Histories = [new HistoryItem { CourseName = "数学", DrawTime = new DateTime(2026, 8, 1, 8, 0, 0) }]
            };
            var config = new MainConfigModel();
            config.LinkageSettings.DataSource = LinkageDataSource.Cses;
            config.LinkageSettings.SubjectHistoryFilterEnabled = true;
            provider = CreateProvider(
                config: config,
                historyQueryService: new TestHistoryQueryService(history),
                catalogManager: new TestProfileCatalogManager(new StudentList(listName) { Students = [student] }));
            IAppHost.Host = new TestHost(provider);

            var page = new RollCallHistorySettingsPage();
            window = new Window { Content = page };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            page.ViewModel.SelectedSubject = "数学";
            page.ViewModel.SelectedMode = HistoryMode.Records;
            Dispatcher.UIThread.RunJobs();

            var columns = page.FindControl<DataGrid>("HistoryGrid")!.Columns;
            Assert.True(columns[0].IsVisible);  // 点名时间
            Assert.True(columns[1].IsVisible);  // 学号
            Assert.True(columns[2].IsVisible);  // 姓名
            Assert.True(columns[3].IsVisible);  // 性别
            Assert.True(columns[4].IsVisible);  // 小组
            Assert.False(columns[5].IsVisible); // 点名次数
            Assert.False(columns[6].IsVisible); // 点名模式
            Assert.False(columns[7].IsVisible); // 点名人数
            Assert.False(columns[8].IsVisible); // 性别限制
            Assert.False(columns[9].IsVisible); // 小组限制
            Assert.True(columns[10].IsVisible); // 科目
            Assert.True(columns[11].IsVisible); // 权重
        }
        finally
        {
            window?.Close();
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    private static ServiceProvider CreateProvider(
        IFeatureAvailabilityService? featureAvailability = null,
        MainConfigModel? config = null,
        IHistoryQueryService? historyQueryService = null,
        IProfileCatalogManager? catalogManager = null)
    {
        var services = new ServiceCollection();
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new InMemoryConfigService(config ?? new MainConfigModel()));
        var profileService = new EmptyProfileService();
        var availability = featureAvailability as TestFeatureAvailability ?? new TestFeatureAvailability(lotteryEnabled: false);

        services.AddSingleton(configHandler);
        services.AddSingleton(availability);
        services.AddSingleton<IFeatureAvailabilityService>(availability);
        services.AddSingleton<IProfileService>(profileService);
        services.AddSingleton(historyQueryService ?? new TestHistoryQueryService());
        services.AddSingleton(catalogManager ?? new TestProfileCatalogManager());
        services.AddSingleton(new DrawEngine(configHandler, profileService, NullLogger<DrawEngine>.Instance));
        services.AddTransient<RollCallHistoryViewModel>();
        services.AddTransient<LotteryHistoryViewModel>();
        return services.BuildServiceProvider();
    }

    private sealed class TestCapabilities(bool lotteryEnabled) : IMobileCapabilities
    {
        public bool IsLotteryEnabled { get; } = lotteryEnabled;
        public bool SupportsInAppUpdate => false;
    }

    private sealed class TestFeatureAvailability(bool lotteryEnabled) : IFeatureAvailabilityService
    {
        public bool IsLotteryEnabled { get; private set; } = lotteryEnabled;
        public event EventHandler? Changed;
        public void Refresh() { }

        public void SetLotteryEnabled(bool enabled)
        {
            IsLotteryEnabled = enabled;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class InMemoryConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T value ? value : fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class TestHistoryQueryService(StudentHistory? studentHistory = null, PrizeHistory? prizeHistory = null)
        : IHistoryQueryService
    {
        public IReadOnlyList<string> GetStudentHistoryNames() => studentHistory is null ? [] : [studentHistory.Name];
        public IReadOnlyList<string> GetPrizeHistoryNames() => prizeHistory is null ? [] : [prizeHistory.Name];
        public IReadOnlyList<HistoryQueryItem> GetRecentItems(int maximumCount) => [];
        public StudentHistory? LoadStudentHistory(string name) =>
            string.Equals(name, studentHistory?.Name, StringComparison.Ordinal) ? studentHistory : null;
        public PrizeHistory? LoadPrizeHistory(string name) =>
            string.Equals(name, prizeHistory?.Name, StringComparison.Ordinal) ? prizeHistory : null;
    }

    private sealed class TestProfileCatalogManager(StudentList? studentList = null, PrizeList? prizeList = null)
        : IProfileCatalogManager
    {
        public IReadOnlyList<string> GetStudentListNames() => studentList is null ? [] : [studentList.Name];
        public IReadOnlyList<string> GetPrizeListNames() => prizeList is null ? [] : [prizeList.Name];
        public bool StudentListExists(string name) => string.Equals(name, studentList?.Name, StringComparison.Ordinal);
        public bool PrizeListExists(string name) => string.Equals(name, prizeList?.Name, StringComparison.Ordinal);
        public bool CreateStudentList(string name) => false;
        public bool CreatePrizeList(string name) => false;
        public bool RenameStudentList(string oldName, string newName) => false;
        public bool RenamePrizeList(string oldName, string newName) => false;
        public bool DeleteStudentList(string name, bool deleteHistory) => false;
        public bool DeletePrizeList(string name, bool deleteHistory) => false;
        public StudentList? LoadStudentList(string name) =>
            string.Equals(name, studentList?.Name, StringComparison.Ordinal) ? studentList : null;
        public PrizeList? LoadPrizeList(string name) =>
            string.Equals(name, prizeList?.Name, StringComparison.Ordinal) ? prizeList : null;
        public bool SaveStudentList(StudentList list) => false;
        public bool SavePrizeList(PrizeList list) => false;
        public bool ReplaceStudents(string name, IReadOnlyList<Student> students) => false;
        public bool ReplacePrizes(string name, IReadOnlyList<Prize> prizes) => false;
        public void SetDefaultStudentList(string name) { }
        public void SetDefaultPrizePool(string name) { }
        public bool ClearStudentHistory(string name) => false;
        public bool ClearPrizeHistory(string name) => false;
    }

    private sealed class EmptyProfileService : IProfileService
    {
        public StudentList? CurrentStudentList => null;
        public StudentHistory? CurrentStudentHistory => null;
        public PrizeList? CurrentPrizeList => null;
        public PrizeHistory? CurrentPrizeHistory => null;
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
        public void LoadStudentProfile(string name, bool saveCurrent = true) { }
        public void LoadPrizeProfile(string name, bool saveCurrent = true) { }

        public void RecordStudentHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount,
            string drawGroup = "", string drawGender = "", int drawMethod = 0,
            IReadOnlyDictionary<Student, double>? weights = null, string courseName = "", string? drawRoundId = null) { }

        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount,
            int drawMethod = 0, string? drawRoundId = null) { }

        public void ClearCurrentStudentHistory() { }
        public void ClearCurrentPrizeHistory() { }
        public void SaveProfile() { }
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
