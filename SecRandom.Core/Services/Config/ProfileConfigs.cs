using SecRandom.Core.Abstraction;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Config;

public class ProfileConfigHandlerBase<T> : ConfigHandlerBase<T> where T : ProfileConfigBase
{
    public ProfileConfigHandlerBase(string name)
        : base(() => (T)Activator.CreateInstance(typeof(T), [name])!)
    {
        Name = name;
        Data.Name = name;
    }

    public string Name { get; }

    public override void Reload()
    {
        base.Reload();
        Data.Name = Name;
    }
}

public class StudentListConfig(string name) : ProfileConfigHandlerBase<StudentList>(name);

public class StudentHistoryConfig(string name) : ProfileConfigHandlerBase<StudentHistory>(name);

public class PrizeListConfig(string name) : ProfileConfigHandlerBase<PrizeList>(name);

public class PrizeHistoryConfig(string name) : ProfileConfigHandlerBase<PrizeHistory>(name);