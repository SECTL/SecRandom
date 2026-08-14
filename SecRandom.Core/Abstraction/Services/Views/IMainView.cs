namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>
///     Host main-view capability exposed to plugins: drawer access plus main-page navigation by page id.
///     Page ids are the registered <c>main.xxx</c> ids; navigating to an unavailable or unregistered id is a no-op.
/// </summary>
public interface IMainView : IDrawerView
{
    void NavigateToPage(string id);
}
