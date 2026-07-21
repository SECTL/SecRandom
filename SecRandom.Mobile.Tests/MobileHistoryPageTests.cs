using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Models.Profile;
using SecRandom.Views.Mobile;

namespace SecRandom.Mobile.Tests;

public sealed class MobileHistoryPageTests
{
    [AvaloniaFact]
    public void LotteryTabIsHiddenWhenLotteryIsDisabled()
    {
        var profileService = new EmptyProfileService();
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new InMemoryConfigService(new MainConfigModel()));
        var page = new MobileHistoryPage(
            profileService,
            new EmptyHistoryQueryService(),
            new EmptyProfileCatalogManager(),
            configHandler,
            new DrawEngine(configHandler, profileService, NullLogger<DrawEngine>.Instance),
            new TestCapabilities(lotteryEnabled: false));

        Assert.Equal(0, page.FindControl<TabStrip>("HistoryTabs")!.SelectedIndex);
        Assert.False(page.FindControl<TabStripItem>("LotteryTab")!.IsVisible);
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

        public override void SaveConfig<T>(T config)
        {
        }

        public override void DeleteConfig<T>(T config)
        {
        }
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
        public bool DeleteStudentList(string name, bool deleteHistory) => false;
        public bool DeletePrizeList(string name, bool deleteHistory) => false;
        public StudentList? LoadStudentList(string name) => null;
        public PrizeList? LoadPrizeList(string name) => null;
        public bool SaveStudentList(StudentList list) => false;
        public bool SavePrizeList(PrizeList list) => false;
        public bool ReplaceStudents(string name, IReadOnlyList<Student> students) => false;
        public bool ReplacePrizes(string name, IReadOnlyList<Prize> prizes) => false;
        public void SetDefaultStudentList(string name)
        {
        }

        public void SetDefaultPrizePool(string name)
        {
        }

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

        public void LoadStudentProfile(string name, bool saveCurrent = true)
        {
        }

        public void LoadPrizeProfile(string name, bool saveCurrent = true)
        {
        }

        public void RecordStudentHistory(
            IReadOnlyList<Student> students,
            DateTime now,
            int requestedCount,
            string drawGroup = "",
            string drawGender = "",
            int drawMethod = 0,
            IReadOnlyDictionary<Student, double>? weights = null,
            string courseName = "",
            string? drawRoundId = null)
        {
        }

        public void RecordPrizeHistory(
            IReadOnlyList<Prize> prizes,
            DateTime now,
            int requestedCount,
            int drawMethod = 0,
            string? drawRoundId = null)
        {
        }

        public void ClearCurrentStudentHistory()
        {
        }

        public void ClearCurrentPrizeHistory()
        {
        }

        public void SaveProfile()
        {
        }
    }
}
