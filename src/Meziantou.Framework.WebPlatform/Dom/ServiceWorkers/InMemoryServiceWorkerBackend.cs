namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A <see cref="ServiceWorkerBackend"/> that keeps the registrations in memory: the default backend of the browsing contexts
/// (see <see cref="BrowsingContextOptions.ServiceWorkers"/>). It is thread-safe, and can be shared by several browsing contexts.
/// </summary>
public sealed class InMemoryServiceWorkerBackend : ServiceWorkerBackend
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, OrderedDictionary<string, StoredServiceWorkerRegistration>> _origins = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override IReadOnlyList<StoredServiceWorkerRegistration> GetRegistrations(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _origins.TryGetValue(origin, out var registrations) ? [.. registrations.Values] : [];
        }
    }

    /// <inheritdoc/>
    public override void SaveRegistration(string origin, StoredServiceWorkerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(registration);
        lock (_lock)
        {
            if (!_origins.TryGetValue(origin, out var registrations))
            {
                registrations = new OrderedDictionary<string, StoredServiceWorkerRegistration>(StringComparer.Ordinal);
                _origins.Add(origin, registrations);
            }

            registrations[registration.ScopeUrl] = registration;
        }
    }

    /// <inheritdoc/>
    public override bool DeleteRegistration(string origin, string scopeUrl)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(scopeUrl);
        lock (_lock)
        {
            return _origins.TryGetValue(origin, out var registrations) && registrations.Remove(scopeUrl);
        }
    }
}
