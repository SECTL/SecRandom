using System;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Linkage;

public interface ICsesScheduleStore
{
    string SchedulePath { get; }
    CsesSchedule? Load();
    Task<CsesSchedule> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
    void Clear();
    event EventHandler? ScheduleChanged;
}
