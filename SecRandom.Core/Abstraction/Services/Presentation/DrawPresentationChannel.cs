namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     Which draw channel a presentation request came from. Every channel that commits a draw round through
///     the host (desktop pages, quick draw, remote/console draws, mobile sessions) reports the same request
///     shape, so a plugin can present results uniformly.
/// </summary>
public enum DrawPresentationChannel
{
    /// <summary>Desktop roll call page (点名).</summary>
    RollCall = 0,

    /// <summary>Desktop lottery page (抽奖).</summary>
    Lottery = 1,

    /// <summary>Quick draw window (快捷抽签).</summary>
    QuickDraw = 2,

    /// <summary>A draw triggered remotely (console / mobile control).</summary>
    RemoteDraw = 3,

    /// <summary>Mobile draw page.</summary>
    MobileDraw = 4
}
