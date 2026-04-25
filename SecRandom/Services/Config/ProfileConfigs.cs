using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.Profile;

namespace SecRandom.Services.Config;

public class StudentListConfig(string name) : ConfigHandlerBase<StudentList>(() => new StudentList(name));
public class StudentHistoryConfig(string name) : ConfigHandlerBase<StudentHistory>(() => new StudentHistory(name));

public class PrizeListConfig(string name) : ConfigHandlerBase<PrizeList>(() => new PrizeList(name));
public class PrizeHistoryConfig(string name) : ConfigHandlerBase<PrizeHistory>(() => new PrizeHistory(name));
