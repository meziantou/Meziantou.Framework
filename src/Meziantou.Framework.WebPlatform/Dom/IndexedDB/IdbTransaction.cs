namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A transaction (https://w3c.github.io/IndexedDB/#transaction).</summary>
/// <seealso href="https://w3c.github.io/IndexedDB/#idbtransaction"/>
[IdlInterface("IDBTransaction", "https://w3c.github.io/IndexedDB/#idbtransaction")]
public sealed class IdbTransaction : EventTarget
{
    private readonly Queue<PendingOperation> _requests = [];
    private readonly Dictionary<ObjectStoreInfo, IdbObjectStore> _handles = [];
    private readonly IdbOpenDBRequest? _upgradeRequest;
    private readonly List<(ObjectStoreInfo Store, string Name, List<IndexInfo> Indexes, List<string> IndexNames)>? _upgradeSnapshot;
    private TransactionState _state = TransactionState.Active;
    private DomException? _error;
    private IndexedDBBackendTransaction? _backend;
    private bool _processing;

    // The upgrade transaction is not committed before the upgradeneeded event is fired
    private bool _waitingForUpgradeNeeded;

    internal IdbTransaction(IdbDatabase connection, IdbTransactionMode mode, IdbTransactionDurability durability, List<ObjectStoreInfo> scope, IdbOpenDBRequest? upgradeRequest)
    {
        Connection = connection;
        Mode = mode;
        Durability = durability;
        Scope = scope;
        _upgradeRequest = upgradeRequest;
        if (upgradeRequest is not null)
        {
            _waitingForUpgradeNeeded = true;
            _upgradeSnapshot = [.. connection.ObjectStoreSet.Select(store => (store, store.Name, store.Indexes.ToList(), store.Indexes.Select(index => index.Name).ToList()))];
        }
    }

    private enum TransactionState
    {
        Active,
        Inactive,
        Committing,
        Finished,
    }

    internal IdbDatabase Connection { get; }

    internal GlobalScope Global => Connection.Global;

    internal override GlobalScope? RelevantGlobal => Global;

    internal IIndexedDBValueOperations Operations => Connection.Operations;

    /// <summary>The scope of the transaction (all the object stores of the connection for an upgrade transaction).</summary>
    internal List<ObjectStoreInfo> Scope { get; }

    internal bool IsActive => _state == TransactionState.Active;

    internal bool IsFinished => _state == TransactionState.Finished;

    /// <summary>Whether the transaction was started by the scheduler (guarded by the lock of the database state).</summary>
    internal bool IsStarted { get; set; }

    internal ulong OldVersion { get; init; }

    internal ulong NewVersion { get; init; }

    /// <summary>The steps run when the upgrade transaction is finished (the end of https://w3c.github.io/IndexedDB/#upgrade-a-database).</summary>
    internal Action? UpgradeFinished { get; init; }

    /// <summary>Gets the sorted names of the object stores of the scope.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-objectstorenames"/>
    [IdlMember("objectStoreNames", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-objectstorenames")]
    public DomStringList ObjectStoreNames => IdbDatabase.CreateSortedNameList((Mode == IdbTransactionMode.VersionChange ? Connection.ObjectStoreSet : Scope).Select(store => store.Name));

    /// <summary>Gets the mode of the transaction.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-mode"/>
    [IdlMember("mode", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-mode")]
    public IdbTransactionMode Mode { get; }

    /// <summary>Gets the durability hint of the transaction.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-durability"/>
    [IdlMember("durability", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-durability")]
    public IdbTransactionDurability Durability { get; }

    /// <summary>Gets the connection of the transaction.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-db"/>
    [IdlMember("db", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-db")]
    public IdbDatabase Db => Connection;

    /// <summary>Gets the error of an aborted transaction, or null.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-error"/>
    [IdlMember("error", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-error")]
    public DomException? Error => _error;

    /// <summary>The event handler of the abort events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-onabort"/>
    [IdlMember("onabort", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-onabort")]
    public Action<Event>? OnAbort
    {
        get => GetEventHandler("abort");
        set => SetEventHandler("abort", value);
    }

    /// <summary>The event handler of the complete events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-oncomplete"/>
    [IdlMember("oncomplete", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-oncomplete")]
    public Action<Event>? OnComplete
    {
        get => GetEventHandler("complete");
        set => SetEventHandler("complete", value);
    }

    /// <summary>The event handler of the error events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-onerror"/>
    [IdlMember("onerror", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Returns the object store handle of an object store of the scope (https://w3c.github.io/IndexedDB/#dom-idbtransaction-objectstore).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-objectstore"/>
    [IdlMember("objectStore", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-objectstore")]
    public IdbObjectStore ObjectStore(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_state == TransactionState.Finished)
            throw DomException.InvalidState("The transaction is finished");

        var store = Scope.Find(store => store.Name == name && !store.Deleted) ?? throw DomException.NotFound($"The object store '{name}' is not in the scope of the transaction");
        return GetObjectStoreHandle(store);
    }

    /// <summary>Commits the transaction without waiting for the events of the pending requests (https://w3c.github.io/IndexedDB/#dom-idbtransaction-commit).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-commit"/>
    [IdlMember("commit", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-commit")]
    public void Commit()
    {
        if (_state != TransactionState.Active)
            throw DomException.InvalidState("The transaction is not active");

        CommitTransaction();
    }

    /// <summary>Aborts the transaction (https://w3c.github.io/IndexedDB/#dom-idbtransaction-abort).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbtransaction-abort"/>
    [IdlMember("abort", "https://w3c.github.io/IndexedDB/#dom-idbtransaction-abort")]
    public void Abort()
    {
        if (_state is TransactionState.Committing or TransactionState.Finished)
            throw DomException.InvalidState("The transaction is committing or finished");

        Abort(error: null);
    }

    // https://w3c.github.io/IndexedDB/#transaction-construct: the parent of a transaction is its connection
    internal override EventTarget? GetTheParent(Event @event) => Connection;

    internal static DomException InactiveError() => new("The transaction is not active", "TransactionInactiveError");

    internal static DomException ReadOnlyError() => new("The transaction is read-only", "ReadOnlyError");

    internal void SetInactive() => _state = TransactionState.Inactive;

    internal bool Overlaps(IdbTransaction other) => Scope.Exists(store => other.Scope.Exists(otherStore => otherStore.Id == store.Id));

    internal IdbObjectStore GetObjectStoreHandle(ObjectStoreInfo store)
    {
        if (!_handles.TryGetValue(store, out var handle))
        {
            handle = new IdbObjectStore(this, store);
            _handles.Add(store, handle);
        }

        return handle;
    }

    /// <summary>
    /// https://w3c.github.io/IndexedDB/#asynchronously-execute-a-request: adds the request to the request list. The
    /// operation runs on the event loop of the transaction after the previous requests, and returns the result of the
    /// request or throws a <see cref="DomException"/> (the error of the request).
    /// </summary>
    internal IdbRequest ExecuteRequest(object source, Func<IndexedDBBackendTransaction, object?> operation, IdbRequest? request = null)
    {
        request ??= new IdbRequest(Global, Operations, source, this);
        _requests.Enqueue(new PendingOperation(request, operation));
        ScheduleProcessing();
        return request;
    }

    /// <summary>Adds an operation without request (e.g. the creation of an object store) to the request list: an error aborts the transaction.</summary>
    internal void QueueInternalOperation(Action<IndexedDBBackendTransaction> operation)
    {
        _requests.Enqueue(new PendingOperation(Request: null, backend =>
        {
            operation(backend);
            return null;
        }));
        ScheduleProcessing();
    }

    /// <summary>Starts the transaction (called by the scheduler): the backend transaction begins on the event loop of the transaction.</summary>
    internal void Start()
    {
        Global.QueueGlobalTask(() =>
        {
            if (_state == TransactionState.Finished)
                return;

            try
            {
                var state = Connection.State;
                _backend = state.CallBackend(backend => backend.BeginTransaction(state.Origin, state.Name, Mode, [.. Scope.Select(store => store.Id)]));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Abort(ex as DomException ?? new DomException("The transaction could not be started: " + ex.Message, "UnknownError"));
                return;
            }

            // The requests are executed in parallel: their results are delivered by the next tasks
            ScheduleProcessing();
        });
    }

    /// <summary>
    /// Fires the <c>upgradeneeded</c> event at the open request of the upgrade transaction
    /// (https://w3c.github.io/IndexedDB/#upgrade-a-database), in a task.
    /// </summary>
    internal void FireUpgradeNeeded()
    {
        var request = _upgradeRequest!;
        request.Processed = true;
        Global.QueueGlobalTask(() =>
        {
            request.SetResult(Connection);
            request.TransactionObject = this;
            if (_state == TransactionState.Inactive)
            {
                _state = TransactionState.Active;
            }

            var didThrow = request.FireVersionChangeEvent("upgradeneeded", OldVersion, NewVersion);
            _waitingForUpgradeNeeded = false;
            if (_state == TransactionState.Active)
            {
                _state = TransactionState.Inactive;
                if (didThrow)
                {
                    Abort(new DomException("An exception was thrown by an event listener", "AbortError"));
                    return;
                }

                AttemptAutoCommit();
            }
        });
    }

    /// <summary>https://w3c.github.io/IndexedDB/#cleanup-indexed-database-transactions</summary>
    internal void Cleanup()
    {
        if (_state == TransactionState.Active)
        {
            _state = TransactionState.Inactive;
            AttemptAutoCommit();
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the transaction inactive, e.g. the clone of a value
    /// (https://w3c.github.io/IndexedDB/#clone-a-value), so that the getters cannot make requests.
    /// </summary>
    internal T RunInactive<T>(Func<T> action)
    {
        _state = TransactionState.Inactive;
        try
        {
            return action();
        }
        finally
        {
            if (_state == TransactionState.Inactive)
            {
                _state = TransactionState.Active;
            }
        }
    }

    /// <summary>The implementation commits an inactive transaction whose requests are processed (https://w3c.github.io/IndexedDB/#transaction-lifecycle).</summary>
    private void AttemptAutoCommit()
    {
        if (_state == TransactionState.Inactive && _requests.Count == 0 && !_waitingForUpgradeNeeded)
        {
            CommitTransaction();
        }
    }

    /// <summary>https://w3c.github.io/IndexedDB/#commit-a-transaction</summary>
    private void CommitTransaction()
    {
        _state = TransactionState.Committing;
        ScheduleProcessing();
    }

    private void ScheduleProcessing()
    {
        if (_processing || _backend is null)
            return;

        _processing = true;
        Global.QueueGlobalTask(() =>
        {
            _processing = false;
            ProcessRequests();
        });
    }

    /// <summary>Processes the next request of the request list, or commits the transaction when it is committing and has no request.</summary>
    private void ProcessRequests()
    {
        if (_state == TransactionState.Finished || _backend is null)
            return;

        if (_requests.Count == 0)
        {
            if (_state == TransactionState.Committing)
            {
                WriteChanges();
            }

            return;
        }

        var pending = _requests.Peek();
        object? result = null;
        DomException? error = null;
        try
        {
            result = pending.Operation(_backend);
        }
        catch (DomException ex)
        {
            error = ex;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = new DomException("The operation failed: " + ex.Message, "UnknownError");
        }

        // https://w3c.github.io/IndexedDB/#asynchronously-execute-a-request: an error while committing aborts the
        // transaction, and the request is still in the request list (its error event is fired by the abort)
        if (error is not null && pending.Request is not null && _state == TransactionState.Committing)
        {
            Abort(error);
            return;
        }

        _requests.Dequeue();
        if (pending.Request is not { } request)
        {
            // An operation without request (a change of the schema): an error aborts the transaction
            if (error is not null)
            {
                Abort(error);
                return;
            }

            AttemptAutoCommit();
        }
        else
        {
            request.Processed = true;
            if (error is not null)
            {
                request.SetError(error);
                FireErrorEvent(request);
            }
            else
            {
                request.SetResult(result);
                FireSuccessEvent(request);
            }
        }

        if (_state != TransactionState.Finished && (_requests.Count > 0 || _state == TransactionState.Committing))
        {
            ScheduleProcessing();
        }
    }

    /// <summary>https://w3c.github.io/IndexedDB/#fire-a-success-event</summary>
    private void FireSuccessEvent(IdbRequest request)
    {
        var @event = new Event("success");
        if (_state == TransactionState.Inactive)
        {
            _state = TransactionState.Active;
        }

        request.FireEvent(@event);
        if (_state == TransactionState.Active)
        {
            _state = TransactionState.Inactive;
            if (@event.LegacyOutputDidListenersThrowFlag)
            {
                Abort(new DomException("An exception was thrown by an event listener", "AbortError"));
                return;
            }

            AttemptAutoCommit();
        }
    }

    /// <summary>https://w3c.github.io/IndexedDB/#fire-an-error-event</summary>
    private void FireErrorEvent(IdbRequest request)
    {
        var @event = new Event("error", new EventInit { Bubbles = true, Cancelable = true });
        if (_state == TransactionState.Inactive)
        {
            _state = TransactionState.Active;
        }

        request.FireEvent(@event);
        if (_state == TransactionState.Active)
        {
            _state = TransactionState.Inactive;
            if (@event.LegacyOutputDidListenersThrowFlag)
            {
                Abort(new DomException("An exception was thrown by an event listener", "AbortError"));
                return;
            }

            if (!@event.CanceledFlag)
            {
                Abort(request.Error);
                return;
            }

            AttemptAutoCommit();
        }
    }

    /// <summary>Writes the changes of a committing transaction whose requests are processed, then fires the <c>complete</c> event in a task.</summary>
    private void WriteChanges()
    {
        try
        {
            Connection.State.CallBackend(_ =>
            {
                _backend!.Commit();
                return true;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Abort(ex as DomException ?? new DomException("The transaction could not be committed: " + ex.Message, "UnknownError"));
            return;
        }

        _backend!.Dispose();
        Global.QueueGlobalTask(() =>
        {
            if (Mode == IdbTransactionMode.VersionChange)
            {
                Connection.UpgradeTransaction = null;
                foreach (var store in Connection.ObjectStoreSet)
                {
                    store.CreatedInUpgrade = false;
                    foreach (var index in store.Indexes)
                    {
                        index.CreatedInUpgrade = false;
                    }
                }
            }

            _state = TransactionState.Finished;
            Finished();
            FireEvent(new Event("complete"));
            if (_upgradeRequest is { } request)
            {
                request.TransactionObject = null;
                UpgradeFinished?.Invoke();
            }
        });
    }

    /// <summary>https://w3c.github.io/IndexedDB/#abort-a-transaction</summary>
    internal void Abort(DomException? error)
    {
        if (_state == TransactionState.Finished)
            return;

        if (_backend is { } backend)
        {
            try
            {
                Connection.State.CallBackend(_ =>
                {
                    backend.Abort();
                    return true;
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Scripting.ScriptErrors.Report(ex, Global);
            }
            finally
            {
                backend.Dispose();
            }
        }

        if (Mode == IdbTransactionMode.VersionChange)
        {
            AbortUpgrade();
        }

        _state = TransactionState.Finished;
        _error = error;
        var requests = _requests.Where(pending => pending.Request is not null).Select(pending => pending.Request!).ToList();
        _requests.Clear();
        foreach (var request in requests)
        {
            request.Processed = true;
            Global.QueueGlobalTask(() =>
            {
                request.SetError(new DomException("The transaction was aborted", "AbortError"));
                request.FireEvent(new Event("error", new EventInit { Bubbles = true, Cancelable = true }));
            });
        }

        Global.QueueGlobalTask(() =>
        {
            if (Mode == IdbTransactionMode.VersionChange)
            {
                Connection.UpgradeTransaction = null;
            }

            FireEvent(new Event("abort", new EventInit { Bubbles = true }));
            if (_upgradeRequest is { } request)
            {
                request.TransactionObject = null;
                request.Reset();
                UpgradeFinished?.Invoke();
            }
        });
        Finished();
    }

    /// <summary>https://w3c.github.io/IndexedDB/#abort-an-upgrade-transaction</summary>
    private void AbortUpgrade()
    {
        Connection.UpgradeAborted = true;
        Connection.VersionValue = OldVersion;
        var snapshot = _upgradeSnapshot!;
        foreach (var store in Connection.ObjectStoreSet.Concat(Scope).Concat(_handles.Keys).Distinct())
        {
            if (store.CreatedInUpgrade)
            {
                store.Deleted = true;
                foreach (var index in store.Indexes)
                {
                    index.Deleted = true;
                }

                // No index references the store
                store.Indexes = [];
            }
        }

        foreach (var (store, name, indexes, indexNames) in snapshot)
        {
            store.Name = name;
            store.Deleted = false;
            foreach (var index in store.Indexes.Where(index => index.CreatedInUpgrade))
            {
                index.Deleted = true;
            }

            store.Indexes = [.. indexes];
            store.BackendIndexes = [.. indexes];
            for (var i = 0; i < indexes.Count; i++)
            {
                indexes[i].Name = indexNames[i];
                indexes[i].Deleted = false;
            }
        }

        Connection.ObjectStoreSet = [.. snapshot.Select(entry => entry.Store)];
    }

    // The transaction is finished: the scheduler starts the next transactions, and a connection whose close is pending may close
    private void Finished()
    {
        Connection.State.OnTransactionFinished(this);
        Connection.OnTransactionFinished(this);
    }

    private sealed record PendingOperation(IdbRequest? Request, Func<IndexedDBBackendTransaction, object?> Operation);
}
