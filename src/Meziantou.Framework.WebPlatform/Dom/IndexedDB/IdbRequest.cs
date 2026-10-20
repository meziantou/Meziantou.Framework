namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A request (https://w3c.github.io/IndexedDB/#request-api): the result of an asynchronous operation on a database, made
/// available when the <c>success</c> or <c>error</c> event is fired at the request.
/// </summary>
/// <seealso href="https://w3c.github.io/IndexedDB/#idbrequest"/>
[IdlInterface("IDBRequest", "https://w3c.github.io/IndexedDB/#idbrequest")]
public class IdbRequest : EventTarget
{
    private object? _result;
    private DomException? _error;

    internal IdbRequest(GlobalScope global, IIndexedDBValueOperations operations, object? source, IdbTransaction? transaction)
    {
        Global = global;
        Operations = operations;
        SourceObject = source;
        TransactionObject = transaction;
    }

    internal GlobalScope Global { get; }

    internal IIndexedDBValueOperations Operations { get; }

    internal override GlobalScope? RelevantGlobal => Global;

    /// <summary>The source of the request: an <see cref="IdbObjectStore"/>, an <see cref="IdbIndex"/>, an <see cref="IdbCursor"/>, or null.</summary>
    internal object? SourceObject { get; set; }

    internal IdbTransaction? TransactionObject { get; set; }

    /// <summary>https://w3c.github.io/IndexedDB/#request-processed</summary>
    internal bool Processed { get; set; }

    /// <summary>https://w3c.github.io/IndexedDB/#request-done</summary>
    internal bool IsDone { get; set; }

    /// <summary>Gets the result of the request (an IDL <c>any</c> value: null is undefined).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the request is not done.</exception>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-result"/>
    [IdlMember("result", "https://w3c.github.io/IndexedDB/#dom-idbrequest-result")]
    public object? Result => IsDone ? _result : throw DomException.InvalidState("The request is not done");

    /// <summary>Gets the error of the request, or null.</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the request is not done.</exception>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-error"/>
    [IdlMember("error", "https://w3c.github.io/IndexedDB/#dom-idbrequest-error")]
    public DomException? Error => IsDone ? _error : throw DomException.InvalidState("The request is not done");

    /// <summary>Gets the source of the request: an <see cref="IdbObjectStore"/>, an <see cref="IdbIndex"/>, an <see cref="IdbCursor"/>, or null.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-source"/>
    [IdlMember("source", "https://w3c.github.io/IndexedDB/#dom-idbrequest-source")]
    public object? Source => SourceObject;

    /// <summary>Gets the transaction of the request, or null.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-transaction"/>
    [IdlMember("transaction", "https://w3c.github.io/IndexedDB/#dom-idbrequest-transaction")]
    public IdbTransaction? Transaction => TransactionObject;

    /// <summary>Gets the state of the request.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-readystate"/>
    [IdlMember("readyState", "https://w3c.github.io/IndexedDB/#dom-idbrequest-readystate")]
    public IdbRequestReadyState ReadyState => IsDone ? IdbRequestReadyState.Done : IdbRequestReadyState.Pending;

    /// <summary>The event handler of the success events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-onsuccess"/>
    [IdlMember("onsuccess", "https://w3c.github.io/IndexedDB/#dom-idbrequest-onsuccess")]
    public Action<Event>? OnSuccess
    {
        get => GetEventHandler("success");
        set => SetEventHandler("success", value);
    }

    /// <summary>The event handler of the error events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbrequest-onerror"/>
    [IdlMember("onerror", "https://w3c.github.io/IndexedDB/#dom-idbrequest-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    // https://w3c.github.io/IndexedDB/#request-construct: the parent of a request is its transaction
    internal override EventTarget? GetTheParent(Event @event) => TransactionObject;

    internal void SetResult(object? result)
    {
        IsDone = true;
        _result = result;
        _error = null;
    }

    internal void SetError(DomException error)
    {
        IsDone = true;
        _result = null;
        _error = error;
    }

    /// <summary>Clears the result of an open request whose upgrade transaction is aborted (https://w3c.github.io/IndexedDB/#abort-a-transaction).</summary>
    internal void Reset()
    {
        _result = null;
        Processed = false;
        IsDone = false;
    }

    /// <summary>https://w3c.github.io/IndexedDB/#fire-a-version-change-event: returns whether a listener threw an exception.</summary>
    internal bool FireVersionChangeEvent(string type, ulong oldVersion, ulong? newVersion)
    {
        var @event = new IdbVersionChangeEvent(type, new IdbVersionChangeEventInit { OldVersion = oldVersion, NewVersion = newVersion });
        FireEvent(@event);
        return @event.LegacyOutputDidListenersThrowFlag;
    }
}

/// <summary>A request to open or delete a database (https://w3c.github.io/IndexedDB/#idbopendbrequest).</summary>
/// <seealso href="https://w3c.github.io/IndexedDB/#idbopendbrequest"/>
[IdlInterface("IDBOpenDBRequest", "https://w3c.github.io/IndexedDB/#idbopendbrequest")]
public sealed class IdbOpenDBRequest : IdbRequest
{
    internal IdbOpenDBRequest(GlobalScope global, IIndexedDBValueOperations operations)
        : base(global, operations, source: null, transaction: null)
    {
    }

    /// <summary>The event handler of the blocked events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbopendbrequest-onblocked"/>
    [IdlMember("onblocked", "https://w3c.github.io/IndexedDB/#dom-idbopendbrequest-onblocked")]
    public Action<Event>? OnBlocked
    {
        get => GetEventHandler("blocked");
        set => SetEventHandler("blocked", value);
    }

    /// <summary>The event handler of the upgradeneeded events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbopendbrequest-onupgradeneeded"/>
    [IdlMember("onupgradeneeded", "https://w3c.github.io/IndexedDB/#dom-idbopendbrequest-onupgradeneeded")]
    public Action<Event>? OnUpgradeNeeded
    {
        get => GetEventHandler("upgradeneeded");
        set => SetEventHandler("upgradeneeded", value);
    }

    // https://w3c.github.io/IndexedDB/#open-request: the parent of an open request is null
    internal override EventTarget? GetTheParent(Event @event) => null;
}
