namespace SecRandom.Core.Views;

public interface IViewRegistry
{
    void Register(ViewRegistration registration);
    bool TryGet(string id, out ViewRegistration? registration);
}
