using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Mobile;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels.SettingsPages.History;
using SecRandom.Views.Mobile;

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

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new InMemoryConfigService(new MainConfigModel()));
        var profileService = new EmptyProfileService();

        services.AddSingleton(configHandler);
        services.AddSingleton<IProfileService>(profileService);
        services.AddSingleton<IHistoryQueryService, EmptyHistoryQueryService>();
        services.AddSingleton<IProfileCatalogManager, EmptyProfileCatalogManager>();
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

    private sealed class InMemoryConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T value ? value : fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class EmptyHistoryQueryService : IHistoryQueryService
    {
        public IReadOnlyList<string> GetStudentHistoryNames() => [];
        public IReadOnlyList<string> GetPrizeHistoryNames() => [];
        public IReadOnlyList<HistoryQueryItem> GetRecentItems(int maximumCount) => [];
        public StudentHistory? LoadStudentHistory(string name) => null;
        public PrizeHistory? LoadPrizeHistory(string name) => null;
    }

    private sealed class EmptyProfileCatalogManager : IProfileCatalogManager
    {
        public IReadOnlyList<string> GetStudentListNames() => [];
        public IReadOnlyList<string> GetPrizeListNames() => [];
        public bool StudentListExists(string name) => false;
        public bool PrizeListExists(string name) => false;
        public bool CreateStudentList(string name) => false;
        public bool CreatePrizeList(string name) => false;
        public bool RenameStudentList(string oldName, string newName) => false;
        public bool RenamePrizeList(string oldName, string newName) => false;
        public bool DeleteStudentList(string name, bool deleteHistory) => false;
        public bool DeletePrizeList(string name, bool deleteHistory) => false;
        public StudentList? LoadStudentList(string name) => null;
        public PrizeList? LoadPrizeList(string name) => null;
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
