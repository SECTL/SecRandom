namespace SecRandom.Views;

/// <summary>
/// Releases work owned by drawer content after any drawer-close path.
/// </summary>
internal interface IDrawerCloseAware
{
    Task OnDrawerClosedAsync();
}
