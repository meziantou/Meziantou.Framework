namespace Meziantou.Framework.WebPlatform.Dom;

public abstract partial class GlobalScope
{
    /// <summary>The service worker objects of the global object, created when first used (see <see cref="ServiceWorkerObjects"/>), or null.</summary>
    internal ServiceWorkerObjectMap? ServiceWorkerObjectsOrNull { get; private set; }

    /// <summary>
    /// The service worker object map (https://w3c.github.io/ServiceWorker/#environment-settings-object-service-worker-object-map),
    /// the service worker registration object map and the <see cref="ServiceWorkerContainer"/> of the global object: the
    /// global object is registered in its browsing context group, whose algorithms update its objects.
    /// </summary>
    internal ServiceWorkerObjectMap ServiceWorkerObjects
    {
        get
        {
            if (ServiceWorkerObjectsOrNull is { } objects)
                return objects;

            objects = new ServiceWorkerObjectMap();
            ServiceWorkerObjectsOrNull = objects;
            ServiceWorkerRegistry.Get(EnvironmentDocument)?.AddObjectHolder(this);
            return objects;
        }
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#get-the-service-worker-object: a new object gets the state of the worker, or
    /// <paramref name="state"/> (the state when the task that creates it was queued: the tasks queued afterwards update it).
    /// </summary>
    internal ServiceWorker GetServiceWorkerObject(ServiceWorkerRecord worker, ServiceWorkerState? state = null)
    {
        var objects = ServiceWorkerObjects;
        if (!objects.Workers.TryGetValue(worker, out var workerObject))
        {
            if (state is null)
            {
                lock (worker.Registration.Registry.Lock)
                {
                    state = worker.State;
                }
            }

            workerObject = new ServiceWorker(this, worker, state.Value);
            objects.Workers.Add(worker, workerObject);
        }

        return workerObject;
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#get-the-service-worker-registration-object: a new object gets the workers of the
    /// registration, or the ones of <paramref name="snapshot"/> (the workers when the task that creates it was queued: the
    /// tasks queued afterwards update them).
    /// </summary>
    internal ServiceWorkerRegistration GetServiceWorkerRegistrationObject(ServiceWorkerRegistrationRecord registration, ServiceWorkerRegistrationSnapshot? snapshot = null)
    {
        var objects = ServiceWorkerObjects;
        if (!objects.Registrations.TryGetValue(registration, out var registrationObject))
        {
            var workers = snapshot ?? registration.Registry.Snapshot(registration);
            registrationObject = new ServiceWorkerRegistration(this, registration) { StateVersion = workers.Version };
            objects.Registrations.Add(registration, registrationObject);
            registrationObject.SetWorker(ServiceWorkerRegistrationSlot.Installing, workers.Installing is { } installing ? GetServiceWorkerObject(installing, workers.InstallingState) : null);
            registrationObject.SetWorker(ServiceWorkerRegistrationSlot.Waiting, workers.Waiting is { } waiting ? GetServiceWorkerObject(waiting, workers.WaitingState) : null);
            registrationObject.SetWorker(ServiceWorkerRegistrationSlot.Active, workers.Active is { } active ? GetServiceWorkerObject(active, workers.ActiveState) : null);
        }

        return registrationObject;
    }
}

/// <summary>
/// The workers of a registration and their states at a point in time (read under the lock of the registry).
/// <paramref name="Version"/> is the number of changes of the registrations of the registry at that time: an object created
/// from the snapshot ignores the tasks of the earlier changes (see <see cref="ServiceWorkerRegistration.StateVersion"/>).
/// </summary>
internal readonly record struct ServiceWorkerRegistrationSnapshot(
    ServiceWorkerRecord? Installing, ServiceWorkerState InstallingState,
    ServiceWorkerRecord? Waiting, ServiceWorkerState WaitingState,
    ServiceWorkerRecord? Active, ServiceWorkerState ActiveState,
    long Version);

/// <summary>The service worker objects of a global object (used on its thread only).</summary>
internal sealed class ServiceWorkerObjectMap
{
    public Dictionary<ServiceWorkerRecord, ServiceWorker> Workers { get; } = [];

    public Dictionary<ServiceWorkerRegistrationRecord, ServiceWorkerRegistration> Registrations { get; } = [];

    /// <summary>The <see cref="ServiceWorkerContainer"/> of the navigator of the global object, once created.</summary>
    public ServiceWorkerContainer? Container { get; set; }
}
