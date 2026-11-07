using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.HistoryQuery;

internal sealed class HistoryQueryService(ConfigServiceBase configService, ILogger<HistoryQueryService> logger)
    : IHistoryQueryService
{
    public IReadOnlyList<string> GetStudentHistoryNames() =>
        ProfileHistoryPaths.EnumerateProfileNames(HistoryGroupKind.RollCall);

    public IReadOnlyList<string> GetPrizeHistoryNames() =>
        ProfileHistoryPaths.EnumerateProfileNames(HistoryGroupKind.Lottery);

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
        // 缺失时先按文件存在性拦截，避免配置处理器为不存在的档案落盘空档案。
        if (string.IsNullOrWhiteSpace(name) || !ProfileHistoryPaths.Exists(name, HistoryGroupKind.RollCall))
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
        if (string.IsNullOrWhiteSpace(name) || !ProfileHistoryPaths.Exists(name, HistoryGroupKind.Lottery))
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
