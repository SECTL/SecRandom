namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Application lifecycle notifications exposed to plugins. <see cref="AppStarted"/> fires after the
///     host finished startup (post window setup); <see cref="AppStopping"/> fires before host shutdown.
/// </summary>
public interface IAppLifecycleService
{
    event EventHandler? AppStarted;

    event EventHandler? AppStopping;
}
