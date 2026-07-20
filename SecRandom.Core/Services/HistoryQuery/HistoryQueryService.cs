using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.HistoryQuery;

internal sealed class HistoryQueryService(ConfigServiceBase configService, ILogger<HistoryQueryService> logger)
    : IHistoryQueryService
{
    public IReadOnlyList<string> GetStudentHistoryNames() => GetProfileNames("roll_call_history");

    public IReadOnlyList<string> GetPrizeHistoryNames() => GetProfileNames("lottery_history");

    public IReadOnlyList<HistoryQueryItem> GetRecentItems(int maximumCount)
    {
        if (maximumCount <= 0)
            return [];

        var items = new List<HistoryQueryItem>();
        AppendStudentItems(items);
        AppendPrizeItems(items);
        return items.OrderByDescending(item => item.DrawTime).Take(maximumCount).ToArray();
    }

    public StudentHistory? LoadStudentHistory(string name)
    {
        // 缺失时先按文件存在性拦截，避免配置处理器为不存在的档案落盘空文件。
        if (string.IsNullOrWhiteSpace(name) ||
            !File.Exists(Utils.GetFilePath("history", "roll_call_history", $"{name}.json")))
            return null;

        try
        {
            return new StudentHistoryConfig(name, logger, configService).Data;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取点名历史失败：班级={ClassName}。", name);
            return null;
        }
    }

    public PrizeHistory? LoadPrizeHistory(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !File.Exists(Utils.GetFilePath("history", "lottery_history", $"{name}.json")))
            return null;

        try
        {
            return new PrizeHistoryConfig(name, logger, configService).Data;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取抽奖历史失败：奖池={PoolName}。", name);
            return null;
        }
    }

    private IReadOnlyList<string> GetProfileNames(string directory)
    {
        var path = Utils.GetDirectoryPath("history", directory);
        return Directory.Exists(path)
            ? Directory.GetFiles(path, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name)
                .ToArray()
            : [];
    }

    private void AppendStudentItems(List<HistoryQueryItem> target)
    {
        foreach (var profileName in GetStudentHistoryNames())
        {
            try
            {
                var history = new StudentHistoryConfig(profileName, logger, configService).Data;
                foreach (var (key, record) in history.Students)
                {
                    foreach (var item in record.Histories)
                        target.Add(new HistoryQueryItem(profileName, key, DisplayName(item.RecordName, item.RecordNumber, key), item.DrawTime, item.DrawRoundId, false));
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "读取点名历史失败：班级={ClassName}。", profileName);
            }
        }
    }

    private void AppendPrizeItems(List<HistoryQueryItem> target)
    {
        foreach (var profileName in GetPrizeHistoryNames())
        {
            try
            {
                var history = new PrizeHistoryConfig(profileName, logger, configService).Data;
                foreach (var (key, record) in history.Prizes)
                {
                    foreach (var item in record.Histories)
                        target.Add(new HistoryQueryItem(profileName, key, DisplayName(item.RecordName, item.RecordNumber, key), item.DrawTime, item.DrawRoundId, true));
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "读取抽奖历史失败：奖池={PoolName}。", profileName);
            }
        }
    }

    private static string DisplayName(string name, string number, string fallback) =>
        !string.IsNullOrWhiteSpace(name) ? name : !string.IsNullOrWhiteSpace(number) ? number : fallback;
}
