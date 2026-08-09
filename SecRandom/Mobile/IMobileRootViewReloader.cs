namespace SecRandom.Mobile;

public interface IMobileRootViewReloader
{
    Task ReloadAsync();
}

internal sealed class MobileRootViewReloader(Func<Task> reloadRoot) : IMobileRootViewReloader
{
    public Task ReloadAsync() => reloadRoot();
}
