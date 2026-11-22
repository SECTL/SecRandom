using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Linkage;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Linkage;

namespace SecRandom.Services.Linkage;

/// <summary>Resolves the roster configured for the course currently in progress.</summary>
public sealed class CourseRosterMappingService
{
    private readonly MainConfigHandler _configHandler;
    private readonly CourseLinkageService _linkageService;

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<CourseRosterBinding> Bindings => _configHandler.Data.LinkageSettings.CourseRosterBindings;

    public string? ResolveStudentList(CourseDrawChannel channel)
    {
        var settings = _configHandler.Data.EffectiveLinkageSettings;
        return CourseRosterBindingResolver.Resolve(
            settings.CourseRosterBindings,
            settings.DataSource,
            _linkageService.GetCurrentCourseName(),
            channel);
    }

    public string? ResolvePrizeList() => ResolveStudentList(CourseDrawChannel.Lottery);

    public void NotifyBindingsChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    public CourseRosterMappingService(MainConfigHandler configHandler, CourseLinkageService linkageService)
    {
        _configHandler = configHandler;
        _linkageService = linkageService;
        linkageService.StateChanged += LinkageServiceOnStateChanged;
    }

    private void LinkageServiceOnStateChanged(object? sender, EventArgs e) =>
        SelectionChanged?.Invoke(this, EventArgs.Empty);

    public static void Normalize(CourseRosterBinding binding, LinkageDataSource source)
    {
        binding.DataSource = source;
        binding.CourseName = binding.CourseName?.Trim() ?? string.Empty;
        binding.RollCallStudentListName = binding.RollCallStudentListName?.Trim() ?? string.Empty;
        binding.QuickDrawStudentListName = binding.QuickDrawStudentListName?.Trim() ?? string.Empty;
        binding.LotteryPrizeListName = binding.LotteryPrizeListName?.Trim() ?? string.Empty;
    }
}
