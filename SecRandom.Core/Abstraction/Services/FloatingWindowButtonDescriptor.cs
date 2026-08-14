namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     A runtime-registered floating-window button contributed by a plugin. <see cref="Id"/> must be
///     unique at the application registry level; <see cref="Icon"/> is a Fluent icon name from the
///     Core icon catalog and <see cref="Label"/> is the tooltip/text shown on the button.
/// </summary>
public sealed record FloatingWindowButtonDescriptor(
    string Id,
    string Icon,
    string Label,
    Action Click);
