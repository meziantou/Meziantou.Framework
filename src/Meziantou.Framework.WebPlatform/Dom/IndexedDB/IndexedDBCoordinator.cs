namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The state of the databases of an <see cref="IndexedDBBackend"/> shared by the windows and the workers that use it (on
/// several threads): the connections, the connection queues (https://w3c.github.io/IndexedDB/#connection-queue) and the
/// transaction scheduling (https://w3c.github.io/IndexedDB/#transaction-scheduling).
/// </summary>
internal sealed class IndexedDBCoordinator(IndexedDBBackend backend)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Origin, string Name), IndexedDBDatabaseState> _databases = [];

    public IndexedDBBackend Backend { get; } = backend;

    /// <summary>The version of a database, or the version before its live upgrade transaction (the uncommitted version is not visible).</summary>
    public ulong GetCommittedVersion(string origin, string name, ulong version)
    {
        IndexedDBDatabaseState? state;
        lock (_lock)
        {
            _databases.TryGetValue((origin, name), out state);
        }

        return state?.GetUpgradeOldVersion() ?? version;
    }

    public IndexedDBDatabaseState GetDatabase(string origin, string name)
    {
        lock (_lock)
        {
            if (!_databases.TryGetValue((origin, name), out var state))
            {
                state = new IndexedDBDatabaseState(Backend, origin, name);
                _databases.Add((origin, name), state);
            }

            return state;
        }
    }
}

/// <summary>The shared state of a database: its connections, its connection queue and its live transactions.</summary>
internal sealed class IndexedDBDatabaseState(IndexedDBBackend backend, string origin, string name)
{
    private readonly LinkedList<IndexedDBConnectionRequest> _queue = [];
    private readonly List<IdbDatabase> _connections = [];
    private readonly List<IdbTransaction> _transactions = [];

    /// <summary>Guards the connections, the queue and the transactions.</summary>
    public Lock Lock { get; } = new();

    /// <summary>Serializes the calls to the backend for the database.</summary>
    public Lock BackendLock { get; } = new();

    public IndexedDBBackend Backend { get; } = backend;

    public string Origin { get; } = origin;

    public string Name { get; } = name;

    /// <summary>Adds an open or delete request to the connection queue: it is processed after the previous requests.</summary>
    public void Enqueue(IndexedDBConnectionRequest request)
    {
        bool start;
        lock (Lock)
        {
            _queue.AddLast(request);
            start = _queue.Count == 1;
        }

        if (start)
        {
            request.Start();
        }
    }

    /// <summary>Called when a request of the connection queue is processed: the next request starts.</summary>
    public void OnRequestProcessed(IndexedDBConnectionRequest request)
    {
        IndexedDBConnectionRequest? next = null;
        lock (Lock)
        {
            if (_queue.First?.Value == request)
            {
                _queue.RemoveFirst();
                next = _queue.First?.Value;
            }
            else
            {
                _queue.Remove(request);
            }
        }

        next?.Start();
    }

    public void AddConnection(IdbDatabase connection)
    {
        lock (Lock)
        {
            _connections.Add(connection);
        }
    }

    /// <summary>Returns the connections that are not closed, except <paramref name="except"/>.</summary>
    public List<IdbDatabase> GetOpenConnections(IdbDatabase? except)
    {
        lock (Lock)
        {
            return [.. _connections.Where(connection => connection != except && !connection.IsClosed)];
        }
    }

    /// <summary>Called when a connection is closed: the waiting open or delete request may continue.</summary>
    public void OnConnectionClosed(IdbDatabase connection)
    {
        IndexedDBConnectionRequest? current;
        lock (Lock)
        {
            _connections.Remove(connection);
            current = _queue.First?.Value;
        }

        current?.OnConnectionClosed();
    }

    /// <summary>The previous version of the database when an upgrade transaction is live, or null.</summary>
    public ulong? GetUpgradeOldVersion()
    {
        lock (Lock)
        {
            return _transactions.Find(transaction => transaction.Mode == IdbTransactionMode.VersionChange && !transaction.IsFinished)?.OldVersion;
        }
    }

    /// <summary>Adds a live transaction (in creation order), and starts the transactions that can start.</summary>
    public void AddTransaction(IdbTransaction transaction)
    {
        lock (Lock)
        {
            _transactions.Add(transaction);
        }

        ScheduleTransactions();
    }

    /// <summary>Removes a finished transaction, and starts the transactions that can now start.</summary>
    public void OnTransactionFinished(IdbTransaction transaction)
    {
        lock (Lock)
        {
            _transactions.Remove(transaction);
        }

        ScheduleTransactions();
    }

    /// <summary>
    /// https://w3c.github.io/IndexedDB/#transaction-scheduling: a read-only transaction starts when no read/write transaction
    /// created before it with an overlapping scope is live; a read/write transaction when no transaction created before it
    /// with an overlapping scope is live, and no other read/write transaction is live (the guarantee given to the backends).
    /// </summary>
    public void ScheduleTransactions()
    {
        List<IdbTransaction>? toStart = null;
        lock (Lock)
        {
            for (var i = 0; i < _transactions.Count; i++)
            {
                var transaction = _transactions[i];
                if (transaction.IsStarted)
                    continue;

                var canStart = true;
                for (var j = 0; j < i && canStart; j++)
                {
                    var previous = _transactions[j];
                    if (transaction.Mode == IdbTransactionMode.ReadOnly)
                    {
                        canStart = previous.Mode == IdbTransactionMode.ReadOnly || !previous.Overlaps(transaction);
                    }
                    else
                    {
                        canStart = previous.Mode == IdbTransactionMode.ReadOnly ? !previous.Overlaps(transaction) : false;
                    }
                }

                if (canStart)
                {
                    transaction.IsStarted = true;
                    (toStart ??= []).Add(transaction);
                }
            }
        }

        if (toStart is not null)
        {
            foreach (var transaction in toStart)
            {
                transaction.Start();
            }
        }
    }

    /// <summary>Runs a call of the backend for the database (the calls for a database are never concurrent).</summary>
    public T CallBackend<T>(Func<IndexedDBBackend, T> call)
    {
        lock (BackendLock)
        {
            return call(Backend);
        }
    }
}

/// <summary>
/// An open request in a connection queue (https://w3c.github.io/IndexedDB/#open-a-database-connection or
/// https://w3c.github.io/IndexedDB/#delete-a-database). The steps run on the event loop of the global object of the
/// request, and wait for the other connections (possibly in other agents) to be closed.
/// </summary>
internal abstract class IndexedDBConnectionRequest(IndexedDBDatabaseState state, IdbOpenDBRequest request)
{
    private List<IdbDatabase>? _waitingConnections;
    private bool _waitingForClose;
    private bool _operationStarted;

    protected IndexedDBDatabaseState State { get; } = state;

    protected IdbOpenDBRequest Request { get; } = request;

    protected GlobalScope Global => Request.Global;

    /// <summary>Starts the steps (the previous requests of the queue are processed), on the event loop of the request.</summary>
    public void Start()
    {
        BeginOperation();
        Global.QueueGlobalTask(() =>
        {
            try
            {
                Run();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Finish(ex as DomException ?? new DomException("The database could not be opened: " + ex.Message, "UnknownError"));
            }
        });
    }

    /// <summary>The steps of the request, after the previous requests of the queue are processed.</summary>
    protected abstract void Run();

    /// <summary>
    /// Fires the <c>versionchange</c> events at the other connections, then the <c>blocked</c> event at the request when
    /// they are not all closed, and runs <paramref name="continuation"/> on the event loop of the request when they are all closed.
    /// </summary>
    protected void CloseOtherConnections(IdbDatabase? connection, ulong oldVersion, ulong? newVersion, Action continuation)
    {
        var openConnections = State.GetOpenConnections(connection);
        if (openConnections.Count == 0)
        {
            continuation();
            return;
        }

        var pending = openConnections.Count;
        foreach (var openConnection in openConnections)
        {
            openConnection.Global.QueueGlobalTask(() =>
            {
                try
                {
                    // The event is not fired at a connection closed by the previous events
                    if (!openConnection.ClosePending)
                    {
                        openConnection.FireVersionChangeEvent("versionchange", oldVersion, newVersion);
                    }
                }
                finally
                {
                    if (Interlocked.Decrement(ref pending) == 0)
                    {
                        Global.QueueGlobalTask(AfterVersionChangeEvents);
                    }
                }
            });
        }

        void AfterVersionChangeEvents()
        {
            if (openConnections.Exists(c => !c.IsClosed))
            {
                Request.FireVersionChangeEvent("blocked", oldVersion, newVersion);

                // The request waits for the scripts to close the connections: the event loop is not busy
                EndOperation();
            }

            lock (State.Lock)
            {
                _waitingConnections = openConnections;
                _waitingForClose = true;
                _continuation = continuation;
            }

            OnConnectionClosed();
        }
    }

    private Action? _continuation;

    /// <summary>Called when a connection of the database is closed: continues when the connections it waits for are closed.</summary>
    public void OnConnectionClosed()
    {
        Action? continuation;
        lock (State.Lock)
        {
            if (!_waitingForClose || _waitingConnections!.Exists(c => !c.IsClosed))
                return;

            _waitingForClose = false;
            continuation = _continuation;
            _continuation = null;
        }

        BeginOperation();
        Global.QueueGlobalTask(() =>
        {
            try
            {
                continuation!();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Finish(ex as DomException ?? new DomException("The database could not be opened: " + ex.Message, "UnknownError"));
            }
        });
    }

    /// <summary>Drops the request (its global object is destroyed): the next request of the queue starts.</summary>
    public void Cancel()
    {
        _canceled = true;
        State.OnRequestProcessed(this);
        EndOperation();
    }

    private bool _canceled;

    /// <summary>Completes the request with an error: the next request of the queue starts, and the error event is fired.</summary>
    protected void Finish(DomException error)
    {
        if (_canceled)
            return;

        Request.Global.IndexedDB.RemoveRequest(this);
        State.OnRequestProcessed(this);
        Global.QueueGlobalTask(() =>
        {
            Request.SetError(error);
            Request.FireEvent(new Event("error", new EventInit { Bubbles = true, Cancelable = true }));
        });
        EndOperation();
    }

    /// <summary>Completes the request successfully: the next request of the queue starts, and <paramref name="fireSuccess"/> runs in a task.</summary>
    protected void Finish(Action fireSuccess)
    {
        if (_canceled)
            return;

        Request.Global.IndexedDB.RemoveRequest(this);
        State.OnRequestProcessed(this);
        Global.QueueGlobalTask(fireSuccess);
        EndOperation();
    }

    // The event loop of the request waits for the steps (e.g. the other connections closing in another agent)
    private void BeginOperation()
    {
        lock (State.Lock)
        {
            if (_operationStarted)
                return;

            _operationStarted = true;
        }

        Global.EventLoop.BeginOperation();
    }

    private void EndOperation()
    {
        lock (State.Lock)
        {
            if (!_operationStarted)
                return;

            _operationStarted = false;
        }

        Global.EventLoop.EndOperation();
    }
}

/// <summary>https://w3c.github.io/IndexedDB/#open-a-database-connection</summary>
internal sealed class IndexedDBOpenRequest(IndexedDBDatabaseState state, IdbOpenDBRequest request, ulong? version) : IndexedDBConnectionRequest(state, request)
{
    protected override void Run()
    {
        var schema = State.CallBackend(backend => backend.GetDatabase(State.Origin, State.Name));
        var databaseVersion = (ulong)(schema?.Version ?? 0);
        var requestedVersion = version ?? (schema is null ? 1 : databaseVersion);
        if (databaseVersion > requestedVersion)
        {
            Finish(new DomException($"The requested version ({requestedVersion}) is less than the existing version ({databaseVersion})", "VersionError"));
            return;
        }

        var connection = new IdbDatabase(Global, State, Request.Operations, State.Name, requestedVersion, schema);
        State.AddConnection(connection);
        Global.IndexedDB.AddConnection(connection);
        if (databaseVersion >= requestedVersion)
        {
            Succeed(connection);
            return;
        }

        CloseOtherConnections(connection, databaseVersion, requestedVersion, () =>
        {
            // https://w3c.github.io/IndexedDB/#upgrade-a-database
            var transaction = connection.CreateUpgradeTransaction(Request, databaseVersion, requestedVersion, () =>
            {
                // The upgrade transaction is finished
                if (connection.IsClosed)
                {
                    Finish(new DomException("The connection was closed", "AbortError"));
                }
                else if (connection.UpgradeAborted)
                {
                    connection.CloseConnection(forced: false);
                    Finish(new DomException("The upgrade transaction was aborted", "AbortError"));
                }
                else
                {
                    Succeed(connection);
                }
            });
            transaction.FireUpgradeNeeded();
        });
    }

    private void Succeed(IdbDatabase connection) => Finish(() =>
    {
        Request.SetResult(connection);
        Request.FireEvent(new Event("success"));
    });
}

/// <summary>https://w3c.github.io/IndexedDB/#delete-a-database</summary>
internal sealed class IndexedDBDeleteRequest(IndexedDBDatabaseState state, IdbOpenDBRequest request) : IndexedDBConnectionRequest(state, request)
{
    protected override void Run()
    {
        var schema = State.CallBackend(backend => backend.GetDatabase(State.Origin, State.Name));
        if (schema is null)
        {
            Succeed(0);
            return;
        }

        var version = (ulong)schema.Version;
        CloseOtherConnections(connection: null, version, newVersion: null, () =>
        {
            State.CallBackend(backend =>
            {
                backend.DeleteDatabase(State.Origin, State.Name);
                return true;
            });
            Succeed(version);
        });
    }

    private void Succeed(ulong version) => Finish(() =>
    {
        Request.SetResult(null);
        Request.FireVersionChangeEvent("success", version, newVersion: null);
    });
}
