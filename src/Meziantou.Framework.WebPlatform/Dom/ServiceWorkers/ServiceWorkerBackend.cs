namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storage of the service worker registrations (https://w3c.github.io/ServiceWorker/#dfn-scope-to-registration-map)
/// of the browsing contexts, e.g. a SQLite database or <see cref="InMemoryServiceWorkerBackend"/> (see
/// <see cref="BrowsingContextOptions.ServiceWorkers"/>). The backend stores the registrations of each origin, with their
/// installed (waiting) and activated (active) service workers and the scripts of these workers: the library implements
/// the registrations and runs the service workers, and only reads and writes the registrations.
/// </summary>
/// <remarks>
/// <para>
/// The methods are called on the threads of the event loops of the windows and the workers that use the registrations: an
/// implementation must be thread-safe. A registration is saved as a whole (<see cref="SaveRegistration"/>) each time it
/// changes: its waiting or active worker is installed or activated, its last update check time or its navigation preload
/// state changes.
/// </para>
/// <para>
/// The browsing contexts that share a backend share the registrations, but each browsing context group runs its own
/// instances of the service workers (as several browser sessions of the same profile): a registration changed by a group is
/// read by the other groups when they look it up for the first time.
/// </para>
/// </remarks>
public abstract class ServiceWorkerBackend
{
    /// <summary>Returns the registrations of an origin.</summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>).</param>
    public abstract IReadOnlyList<StoredServiceWorkerRegistration> GetRegistrations(string origin);

    /// <summary>Saves a registration: replaces the registration with the same <see cref="StoredServiceWorkerRegistration.ScopeUrl"/>, or adds it.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="registration">The registration.</param>
    public abstract void SaveRegistration(string origin, StoredServiceWorkerRegistration registration);

    /// <summary>Deletes the registration of a scope, and returns true, or returns false when there is no such registration.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="scopeUrl">The serialized scope URL of the registration.</param>
    public abstract bool DeleteRegistration(string origin, string scopeUrl);
}

/// <summary>A service worker registration (https://w3c.github.io/ServiceWorker/#dfn-service-worker-registration) stored by a <see cref="ServiceWorkerBackend"/>.</summary>
public sealed class StoredServiceWorkerRegistration
{
    /// <summary>Gets the serialized scope URL (https://w3c.github.io/ServiceWorker/#dfn-scope-url).</summary>
    public required string ScopeUrl { get; init; }

    /// <summary>Gets the update via cache mode: <c>imports</c>, <c>all</c> or <c>none</c> (https://w3c.github.io/ServiceWorker/#dfn-update-via-cache).</summary>
    public string UpdateViaCache { get; init; } = "imports";

    /// <summary>Gets the time of the last update check (https://w3c.github.io/ServiceWorker/#dfn-last-update-check-time), or null.</summary>
    public DateTimeOffset? LastUpdateCheckTime { get; init; }

    /// <summary>Gets whether the navigation preload is enabled (https://w3c.github.io/ServiceWorker/#service-worker-registration-navigation-preload-enabled-flag).</summary>
    public bool NavigationPreloadEnabled { get; init; }

    /// <summary>Gets the value of the <c>Service-Worker-Navigation-Preload</c> header (https://w3c.github.io/ServiceWorker/#service-worker-registration-navigation-preload-header-value).</summary>
    public string NavigationPreloadHeaderValue { get; init; } = "true";

    /// <summary>Gets the installed service worker that waits to be activated, or null.</summary>
    public StoredServiceWorker? Waiting { get; init; }

    /// <summary>Gets the activated service worker, or null.</summary>
    public StoredServiceWorker? Active { get; init; }
}

/// <summary>An installed or activated service worker (https://w3c.github.io/ServiceWorker/#dfn-service-worker) stored by a <see cref="ServiceWorkerBackend"/>.</summary>
public sealed class StoredServiceWorker
{
    /// <summary>Gets the identifier of the service worker, unique in its registration.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the serialized URL of the script (https://w3c.github.io/ServiceWorker/#dfn-script-url).</summary>
    public required string ScriptUrl { get; init; }

    /// <summary>Gets the type of the script: <c>classic</c> or <c>module</c>.</summary>
    public string Type { get; init; } = "classic";

    /// <summary>Gets whether the service worker was activated (<c>activated</c>) or only installed (<c>installed</c>).</summary>
    public string State { get; init; } = "installed";

    /// <summary>Gets whether the classic scripts imported flag is set (https://w3c.github.io/ServiceWorker/#dfn-classic-scripts-imported-flag).</summary>
    public bool ClassicScriptsImported { get; init; }

    /// <summary>Gets the types of the events the service worker handles (https://w3c.github.io/ServiceWorker/#dfn-set-of-event-types-to-handle), e.g. <c>fetch</c>.</summary>
    public IReadOnlyList<string> EventTypesToHandle { get; init; } = [];

    /// <summary>Gets the responses of the scripts of the service worker (https://w3c.github.io/ServiceWorker/#dfn-script-resource-map): its main script and the scripts it imports.</summary>
    public IReadOnlyList<StoredServiceWorkerScript> Scripts { get; init; } = [];

    /// <summary>Gets the URLs of the scripts imported by the service worker (https://w3c.github.io/ServiceWorker/#dfn-set-of-used-scripts).</summary>
    public IReadOnlyList<string> UsedScriptUrls { get; init; } = [];
}

/// <summary>A response of the script resource map of a <see cref="StoredServiceWorker"/>.</summary>
public sealed class StoredServiceWorkerScript
{
    /// <summary>Gets the serialized URL of the request of the script (the key of the map).</summary>
    public required string Url { get; init; }

    /// <summary>Gets the serialized URL of the response (after the redirects), or null when it is <see cref="Url"/>.</summary>
    public string? ResponseUrl { get; init; }

    /// <summary>Gets the status.</summary>
    public int Status { get; init; } = 200;

    /// <summary>Gets the status message.</summary>
    public string StatusText { get; init; } = "";

    /// <summary>Gets the headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the body.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }
}
