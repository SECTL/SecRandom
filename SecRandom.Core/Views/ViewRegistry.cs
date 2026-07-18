namespace SecRandom.Core.Views;

public sealed class ViewRegistry : IViewRegistry
{
    private readonly Dictionary<string, ViewRegistration> _registrations = new(StringComparer.Ordinal);

    public void Register(ViewRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();
        if (!_registrations.TryAdd(registration.Id, registration))
            throw new InvalidOperationException($"View '{registration.Id}' is already registered.");
    }

    public bool TryGet(string id, out ViewRegistration? registration) =>
        _registrations.TryGetValue(id, out registration);
}
