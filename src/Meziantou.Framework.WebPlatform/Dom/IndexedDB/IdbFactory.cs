namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The entry point of IndexedDB of a window or a worker (https://w3c.github.io/IndexedDB/#idbfactory): opens and deletes
/// the databases of its origin, stored by <see cref="BrowsingContextOptions.IndexedDB"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/IndexedDB/#idbfactory"/>
[IdlInterface("IDBFactory", "https://w3c.github.io/IndexedDB/#idbfactory")]
public sealed class IdbFactory
{
    internal IdbFactory(GlobalScope global)
    {
        Global = global;
    }

    internal GlobalScope Global { get; }

    // The connections and the open requests of the global object, closed when it is destroyed
    private readonly List<IdbDatabase> _connections = [];
    private readonly List<IndexedDBConnectionRequest> _requests = [];
    private readonly Lock _lock = new();

    /// <summary>Opens a connection to a database, creating or upgrading it (https://w3c.github.io/IndexedDB/#dom-idbfactory-open).</summary>
    /// <param name="name">The name of the database.</param>
    /// <param name="version">The version of the database, or null for the current version (1 for a new database).</param>
    /// <exception cref="TypeErrorException">The version is 0.</exception>
    /// <exception cref="DomException">A <c>SecurityError</c> when the origin is opaque.</exception>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbfactory-open"/>
    [IdlMember("open", "https://w3c.github.io/IndexedDB/#dom-idbfactory-open")]
    public IdbOpenDBRequest Open(string name, ulong? version = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (version == 0)
            throw new TypeErrorException("The version must be greater than 0");

        var (backend, origin) = ObtainStorage();
        var request = new IdbOpenDBRequest(Global, IndexedDBStorage.GetOperations(Global));
        var state = backend.Coordinator.GetDatabase(origin, name);
        var connectionRequest = new IndexedDBOpenRequest(state, request, version);
        Track(connectionRequest);
        state.Enqueue(connectionRequest);
        return request;
    }

    /// <summary>Deletes a database (https://w3c.github.io/IndexedDB/#dom-idbfactory-deletedatabase).</summary>
    /// <param name="name">The name of the database.</param>
    /// <exception cref="DomException">A <c>SecurityError</c> when the origin is opaque.</exception>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbfactory-deletedatabase"/>
    [IdlMember("deleteDatabase", "https://w3c.github.io/IndexedDB/#dom-idbfactory-deletedatabase")]
    public IdbOpenDBRequest DeleteDatabase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var (backend, origin) = ObtainStorage();
        var request = new IdbOpenDBRequest(Global, IndexedDBStorage.GetOperations(Global));
        var state = backend.Coordinator.GetDatabase(origin, name);
        var connectionRequest = new IndexedDBDeleteRequest(state, request);
        Track(connectionRequest);
        state.Enqueue(connectionRequest);
        return request;
    }

    /// <summary>Returns the names and the versions of the databases of the origin (https://w3c.github.io/IndexedDB/#dom-idbfactory-databases).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbfactory-databases"/>
    [IdlMember("databases", "https://w3c.github.io/IndexedDB/#dom-idbfactory-databases")]
    public Task<IReadOnlyList<IdbDatabaseInfo>> Databases()
    {
        if (IndexedDBStorage.Resolve(Global) is not { } storage)
            return Task.FromException<IReadOnlyList<IdbDatabaseInfo>>(new DomException("The origin of the document is opaque", "SecurityError"));

        var (backend, origin) = storage;

        var completion = new TaskCompletionSource<IReadOnlyList<IdbDatabaseInfo>>();
        Global.QueueGlobalTask(() =>
        {
            try
            {
                // The version of a database that is being upgraded is its committed version
                var result = new List<IdbDatabaseInfo>();
                foreach (var database in backend.GetDatabases(origin))
                {
                    var version = backend.Coordinator.GetCommittedVersion(origin, database.Name, (ulong)database.Version);
                    if (version != 0)
                    {
                        result.Add(new IdbDatabaseInfo { Name = database.Name, Version = version });
                    }
                }

                completion.TrySetResult(result);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                completion.TrySetException(ex as DomException ?? new DomException("The databases could not be listed: " + ex.Message, "UnknownError"));
            }
        });
        return completion.Task;
    }

    /// <summary>Compares two keys (https://w3c.github.io/IndexedDB/#dom-idbfactory-cmp): -1, 0 or 1.</summary>
    /// <exception cref="DomException">A <c>DataError</c> when a value is not a valid key.</exception>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbfactory-cmp"/>
    [IdlMember("cmp", "https://w3c.github.io/IndexedDB/#dom-idbfactory-cmp")]
    public short Cmp(object? first, object? second)
    {
        var operations = IndexedDBStorage.GetOperations(GlobalScope.Current ?? Global);
        var a = IdbOperations.ConvertToKey(operations, first);
        var b = IdbOperations.ConvertToKey(operations, second);
        return (short)a.CompareTo(b);
    }

    /// <summary>
    /// Closes the connections of a global object that is destroyed (a terminated worker, an unloaded document): their
    /// transactions are aborted, and their pending open and delete requests are dropped, so that the other connections
    /// to the databases are not blocked.
    /// </summary>
    internal static void CloseConnections(GlobalScope global)
    {
        if (global.IndexedDBFactory is not { } factory)
            return;

        IndexedDBConnectionRequest[] requests;
        IdbDatabase[] connections;
        lock (factory._lock)
        {
            requests = [.. factory._requests];
            connections = [.. factory._connections];
            factory._requests.Clear();
            factory._connections.Clear();
        }

        foreach (var request in requests)
        {
            request.Cancel();
        }

        foreach (var connection in connections)
        {
            connection.CloseConnection(forced: true, fireCloseEvent: false);
        }
    }

    internal void AddConnection(IdbDatabase connection)
    {
        lock (_lock)
        {
            _connections.Add(connection);
        }
    }

    internal void RemoveConnection(IdbDatabase connection)
    {
        lock (_lock)
        {
            _connections.Remove(connection);
        }
    }

    internal void RemoveRequest(IndexedDBConnectionRequest request)
    {
        lock (_lock)
        {
            _requests.Remove(request);
        }
    }

    private void Track(IndexedDBConnectionRequest request)
    {
        lock (_lock)
        {
            _requests.Add(request);
        }
    }

    private (IndexedDBBackend Backend, string Origin) ObtainStorage()
        => IndexedDBStorage.Resolve(Global) ?? throw new DomException("The origin of the document is opaque", "SecurityError");
}
