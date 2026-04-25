using System;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.Profile;

namespace SecRandom.Services.Config;

public class ProfileConfigHandlerBase<T> : ConfigHandlerBase<T> where T : ProfileConfigBase
{
    private readonly string _name;
    
    public ProfileConfigHandlerBase(string name)
        : base(() => (T)Activator.CreateInstance(typeof(T), args: [name])!)
    {
        _name = name;
        Data.Name = name;
    }

    public override void Reload()
    {
        base.Reload();
        Data.Name = _name;
    }
}

public class StudentListConfig(string name) : ProfileConfigHandlerBase<StudentList>(name);
public class StudentHistoryConfig(string name) : ProfileConfigHandlerBase<StudentHistory>(name);

public class PrizeListConfig(string name) : ProfileConfigHandlerBase<PrizeList>(name);
public class PrizeHistoryConfig(string name) : ProfileConfigHandlerBase<PrizeHistory>(name);
