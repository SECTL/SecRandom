using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Shared;

namespace SecRandom.Services.Linkage;

public sealed class CsesScheduleStore(
    CsesScheduleParser parser,
    ILogger<CsesScheduleStore> logger) : ICsesScheduleStore
{
    public string SchedulePath => Utils.GetFilePath("CSES", "cses_schedule.yml");
    public event EventHandler? ScheduleChanged;

    public CsesSchedule? Load()
    {
        try
        {
            return File.Exists(SchedulePath) ? parser.Parse(File.ReadAllText(SchedulePath)) : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取 CSES 课程表失败。Path={Path}", SchedulePath);
            return null;
        }
    }

    public async Task<CsesSchedule> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var schedule = parser.Parse(content);
        var temporaryPath = SchedulePath + ".importing";
        Directory.CreateDirectory(Path.GetDirectoryName(SchedulePath)!);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, SchedulePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        ScheduleChanged?.Invoke(this, EventArgs.Empty);
        logger.LogInformation("已导入 CSES 课程表：课程数={Count}。", schedule.Courses.Count);
        return schedule;
    }

    public void Clear()
    {
        if (!File.Exists(SchedulePath))
            return;
        File.Delete(SchedulePath);
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
        logger.LogInformation("已清除 CSES 课程表。");
    }
}
