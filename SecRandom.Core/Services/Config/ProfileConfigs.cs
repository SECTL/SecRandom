using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Config;

public class ProfileConfigHandlerBase<T> : ConfigHandlerBase<T> where T : ProfileConfigBase
{
    public ProfileConfigHandlerBase(string name)
        : base(() => (T)Activator.CreateInstance(typeof(T), name)!)
    {
        Initialize(name);
    }

    public ProfileConfigHandlerBase(string name, ILogger logger, ConfigServiceBase configService)
        : base(logger, configService, () => (T)Activator.CreateInstance(typeof(T), name)!)
    {
        Initialize(name);
    }

    private void Initialize(string name)
    {
        Name = name;
        Data.Name = name;
        SaveIfProfileDataNormalized();
    }

    public string Name { get; private set; } = string.Empty;

    public override void Reload()
    {
        base.Reload();
        Data.Name = Name;
        SaveIfProfileDataNormalized();
    }

    private void SaveIfProfileDataNormalized()
    {
        var changed = Data switch
        {
            StudentList studentList => ProfileRecordIdentity.Normalize(studentList),
            PrizeList prizeList => ProfileRecordIdentity.Normalize(prizeList),
            _ => false
        };

        if (changed)
            Save();
    }
}

public class StudentListConfig : ProfileConfigHandlerBase<StudentList>
{
    public StudentListConfig(string name) : base(name)
    {
    }

    public StudentListConfig(string name, ILogger logger, ConfigServiceBase configService) : base(name, logger, configService)
    {
    }
}

/// <summary>
///     点名历史的处理器。历史记录不再走 JSON 文件，而是走 <see cref="ProfileHistoryDatabase" /> 的小数据库；
///     第一次读取某个档案时它会把旧的 JSON 无感迁移过去，因此这里的三个存储接缝之外没有任何调用方需要知道格式变了。
/// </summary>
public class StudentHistoryConfig : ProfileConfigHandlerBase<StudentHistory>
{
    public StudentHistoryConfig(string name) : base(name)
    {
    }

    public StudentHistoryConfig(string name, ILogger logger, ConfigServiceBase configService) : base(name, logger, configService)
    {
    }

    protected override StudentHistory LoadData() =>
        ProfileHistoryDatabase.LoadStudentHistory(FallbackFactory().Name, Logger);

    protected override void SaveData() => ProfileHistoryDatabase.SaveStudentHistory(Data, Logger);

    protected override void DeleteData() => ProfileHistoryDatabase.Delete(Data.Name, HistoryGroupKind.RollCall, Logger);
}

public class PrizeListConfig : ProfileConfigHandlerBase<PrizeList>
{
    public PrizeListConfig(string name) : base(name)
    {
    }

    public PrizeListConfig(string name, ILogger logger, ConfigServiceBase configService) : base(name, logger, configService)
    {
    }
}

/// <summary>抽奖历史的处理器；存储方式与 <see cref="StudentHistoryConfig" /> 相同。</summary>
public class PrizeHistoryConfig : ProfileConfigHandlerBase<PrizeHistory>
{
    public PrizeHistoryConfig(string name) : base(name)
    {
    }

    public PrizeHistoryConfig(string name, ILogger logger, ConfigServiceBase configService) : base(name, logger, configService)
    {
    }

    protected override PrizeHistory LoadData() =>
        ProfileHistoryDatabase.LoadPrizeHistory(FallbackFactory().Name, Logger);

    protected override void SaveData() => ProfileHistoryDatabase.SavePrizeHistory(Data, Logger);

    protected override void DeleteData() => ProfileHistoryDatabase.Delete(Data.Name, HistoryGroupKind.Lottery, Logger);
}
