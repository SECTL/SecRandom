using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
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

public class StudentHistoryConfig : ProfileConfigHandlerBase<StudentHistory>
{
    public StudentHistoryConfig(string name) : base(name)
    {
    }

    public StudentHistoryConfig(string name, ILogger logger, ConfigServiceBase configService) : base(name, logger, configService)
    {
    }
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

public class PrizeHistoryConfig : ProfileConfigHandlerBase<PrizeHistory>
{
    public PrizeHistoryConfig(string name) : base(name)
    {
    }

    public PrizeHistoryConfig(string name, ILogger logger, ConfigServiceBase configService) : base(name, logger, configService)
    {
    }
}
