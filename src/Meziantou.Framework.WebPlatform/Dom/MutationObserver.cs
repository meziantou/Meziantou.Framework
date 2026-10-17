namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#callbackdef-mutationcallback</summary>
[IdlInterface("MutationCallback", "https://dom.spec.whatwg.org/#callbackdef-mutationcallback")]
public delegate void MutationCallback(IReadOnlyList<MutationRecord> mutations, MutationObserver observer);

/// <summary>https://dom.spec.whatwg.org/#dictdef-mutationobserverinit</summary>
[IdlInterface("MutationObserverInit", "https://dom.spec.whatwg.org/#dictdef-mutationobserverinit")]
public sealed class MutationObserverInit
{
    /// <summary>
    /// Gets or sets a value indicating whether additions and removals of the children of the target are observed.
    /// </summary>
    [IdlMember("childList", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-childlist")]
    public bool ChildList { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the attribute changes of the target are observed (implied when
    /// <c>attributeOldValue</c> or <c>attributeFilter</c> is set).
    /// </summary>
    [IdlMember("attributes", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-attributes")]
    public bool? Attributes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the data changes of the target are observed (implied when
    /// <c>characterDataOldValue</c> is set).
    /// </summary>
    [IdlMember("characterData", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-characterdata")]
    public bool? CharacterData { get; set; }

    /// <summary>Gets or sets a value indicating whether the descendants of the target are observed too.</summary>
    [IdlMember("subtree", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-subtree")]
    public bool Subtree { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the previous value of the changed attribute is recorded.
    /// </summary>
    [IdlMember("attributeOldValue", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-attributeoldvalue")]
    public bool? AttributeOldValue { get; set; }

    /// <summary>Gets or sets a value indicating whether the previous data of the changed node is recorded.</summary>
    [IdlMember("characterDataOldValue", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-characterdataoldvalue")]
    public bool? CharacterDataOldValue { get; set; }

    /// <summary>
    /// Gets or sets the local names of the attributes to observe; all the attributes are observed when null.
    /// </summary>
    [IdlMember("attributeFilter", "https://dom.spec.whatwg.org/#dom-mutationobserverinit-attributefilter")]
    public IReadOnlyList<string>? AttributeFilter { get; set; }

    internal MutationObserverInit Clone() => (MutationObserverInit)MemberwiseClone();
}

/// <summary>A registered observer (https://dom.spec.whatwg.org/#registered-observer).</summary>
internal class RegisteredObserver(MutationObserver observer, MutationObserverInit options)
{
    public MutationObserver Observer { get; } = observer;

    public MutationObserverInit Options { get; set; } = options;
}

/// <summary>A transient registered observer (https://dom.spec.whatwg.org/#transient-registered-observer).</summary>
internal sealed class TransientRegisteredObserver(MutationObserver observer, MutationObserverInit options, RegisteredObserver source) : RegisteredObserver(observer, options)
{
    public RegisteredObserver Source { get; } = source;
}

/// <summary>https://dom.spec.whatwg.org/#interface-mutationobserver</summary>
[IdlInterface("MutationObserver", "https://dom.spec.whatwg.org/#mutationobserver")]
public sealed class MutationObserver
{
    private readonly MutationCallback _callback;
    private readonly List<WeakReference<Node>> _nodeList = [];
    private readonly List<MutationRecord> _recordQueue = [];

    /// <summary>Creates an observer that invokes the callback with the batched mutation records.</summary>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-mutationobserver-mutationobserver")]
    public MutationObserver(MutationCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-mutationobserver-observe</summary>
    [IdlMember("observe", "https://dom.spec.whatwg.org/#dom-mutationobserver-observe")]
    public void Observe(Node target, MutationObserverInit? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        options = options?.Clone() ?? new MutationObserverInit();
        if ((options.AttributeOldValue is not null || options.AttributeFilter is not null) && options.Attributes is null)
        {
            options.Attributes = true;
        }

        if (options.CharacterDataOldValue is not null && options.CharacterData is null)
        {
            options.CharacterData = true;
        }

        if (!options.ChildList && options.Attributes != true && options.CharacterData != true)
            throw new TypeErrorException("The options must include childList, attributes or characterData");

        if (options.AttributeOldValue == true && options.Attributes == false)
            throw new TypeErrorException("attributeOldValue requires attributes");

        if (options.AttributeFilter is not null && options.Attributes == false)
            throw new TypeErrorException("attributeFilter requires attributes");

        if (options.CharacterDataOldValue == true && options.CharacterData == false)
            throw new TypeErrorException("characterDataOldValue requires characterData");

        foreach (var registered in target.RegisteredObservers)
        {
            if (!ReferenceEquals(registered.Observer, this))
                continue;

            foreach (var node in EnumerateNodes())
            {
                node.RegisteredObserverList?.RemoveAll(r => r is TransientRegisteredObserver transient && ReferenceEquals(transient.Source, registered));
            }

            registered.Options = options;
            return;
        }

        target.AddRegisteredObserver(new RegisteredObserver(this, options));
        _nodeList.Add(new WeakReference<Node>(target));
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-mutationobserver-disconnect</summary>
    [IdlMember("disconnect", "https://dom.spec.whatwg.org/#dom-mutationobserver-disconnect")]
    public void Disconnect()
    {
        foreach (var node in EnumerateNodes())
        {
            node.RegisteredObserverList?.RemoveAll(r => ReferenceEquals(r.Observer, this));
            node.UpdateObserverCount();
        }

        _recordQueue.Clear();
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-mutationobserver-takerecords</summary>
    [IdlMember("takeRecords", "https://dom.spec.whatwg.org/#dom-mutationobserver-takerecords")]
    public IReadOnlyList<MutationRecord> TakeRecords()
    {
        var records = _recordQueue.ToArray();
        _recordQueue.Clear();
        return records;
    }

    internal void Enqueue(MutationRecord record) => _recordQueue.Add(record);

    internal void AddTransientNode(Node node) => _nodeList.Add(new WeakReference<Node>(node));

    private IEnumerable<Node> EnumerateNodes()
    {
        foreach (var reference in _nodeList.ToArray())
        {
            if (reference.TryGetTarget(out var node))
                yield return node;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#queue-a-mutation-observer-compound-microtask</summary>
    internal static void QueueMutationObserverMicrotask(DomEventLoop eventLoop)
    {
        if (eventLoop.MutationObserverMicrotaskQueued)
            return;

        eventLoop.MutationObserverMicrotaskQueued = true;
        eventLoop.QueueMicrotask(() => NotifyMutationObservers(eventLoop));
    }

    /// <summary>https://dom.spec.whatwg.org/#notify-mutation-observers</summary>
    internal static void NotifyMutationObservers(DomEventLoop eventLoop)
    {
        eventLoop.MutationObserverMicrotaskQueued = false;
        var notifySet = eventLoop.PendingMutationObservers.ToArray();
        eventLoop.PendingMutationObservers.Clear();
        var signalSet = eventLoop.SignalSlots.ToArray();
        eventLoop.SignalSlots.Clear();
        foreach (var mo in notifySet)
        {
            var records = mo._recordQueue.ToArray();
            mo._recordQueue.Clear();
            foreach (var node in mo.EnumerateNodes())
            {
                node.RegisteredObserverList?.RemoveAll(r => r is TransientRegisteredObserver && ReferenceEquals(r.Observer, mo));
                node.UpdateObserverCount();
            }

            if (records.Length > 0)
            {
                try
                {
                    mo._callback(records, mo);
                }
#pragma warning disable CA1031 // "report the exception": exceptions of callbacks do not stop the notification of the other observers
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    Scripting.ScriptErrors.Report(ex);
                }
            }
        }

        foreach (var slot in signalSet)
        {
            slot.FireEvent(new Event("slotchange", new EventInit { Bubbles = true }));
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#queue-a-mutation-record</summary>
    internal static void QueueMutationRecord(string type, Node target, string? name, string? @namespace, string? oldValue, IReadOnlyList<Node> addedNodes, IReadOnlyList<Node> removedNodes, Node? previousSibling, Node? nextSibling)
    {
        Dictionary<MutationObserver, string?>? interestedObservers = null;
        for (Node? node = target; node is not null; node = node.ParentNode)
        {
            if (node.RegisteredObserverList is not { Count: > 0 } registeredObservers)
                continue;

            foreach (var registered in registeredObservers)
            {
                var options = registered.Options;
                if (!ReferenceEquals(node, target) && !options.Subtree)
                    continue;

                if (type == "attributes" && options.Attributes != true)
                    continue;

                if (type == "attributes" && options.AttributeFilter is not null && (!options.AttributeFilter.Contains(name) || @namespace is not null))
                    continue;

                if (type == "characterData" && options.CharacterData != true)
                    continue;

                if (type == "childList" && !options.ChildList)
                    continue;

                interestedObservers ??= [];
                var mo = registered.Observer;
                interestedObservers.TryAdd(mo, null);
                if ((type == "attributes" && options.AttributeOldValue == true) || (type == "characterData" && options.CharacterDataOldValue == true))
                {
                    interestedObservers[mo] = oldValue;
                }
            }
        }

        if (interestedObservers is null)
            return;

        // The surrounding agent is the one of the node document of the target. The observers of the documents without a
        // browsing context are not notified, but takeRecords() returns their records.
        var eventLoop = target.NodeDocument.EventLoop;
        foreach (var (observer, mappedOldValue) in interestedObservers)
        {
            var record = new MutationRecord(type, target, name, @namespace, mappedOldValue, new StaticNodeList(addedNodes), new StaticNodeList(removedNodes), previousSibling, nextSibling);
            observer.Enqueue(record);
            if (eventLoop is null)
                continue;

            if (!eventLoop.PendingMutationObservers.Contains(observer))
            {
                eventLoop.PendingMutationObservers.Add(observer);
            }

            QueueMutationObserverMicrotask(eventLoop);
        }
    }
}

/// <summary>https://dom.spec.whatwg.org/#interface-mutationrecord</summary>
[IdlInterface("MutationRecord", "https://dom.spec.whatwg.org/#mutationrecord")]
public sealed class MutationRecord
{
    internal MutationRecord(string type, Node target, string? attributeName, string? attributeNamespace, string? oldValue, NodeList addedNodes, NodeList removedNodes, Node? previousSibling, Node? nextSibling)
    {
        Type = type;
        Target = target;
        AttributeName = attributeName;
        AttributeNamespace = attributeNamespace;
        OldValue = oldValue;
        AddedNodes = addedNodes;
        RemovedNodes = removedNodes;
        PreviousSibling = previousSibling;
        NextSibling = nextSibling;
    }

    /// <summary>Gets the type of the mutation: <c>attributes</c>, <c>characterData</c> or <c>childList</c>.</summary>
    [IdlMember("type", "https://dom.spec.whatwg.org/#dom-mutationrecord-type")]
    public string Type { get; }

    /// <summary>Gets the node the mutation affected.</summary>
    [IdlMember("target", "https://dom.spec.whatwg.org/#dom-mutationrecord-target")]
    public Node Target { get; }

    /// <summary>Gets the nodes added by the mutation.</summary>
    [IdlMember("addedNodes", "https://dom.spec.whatwg.org/#dom-mutationrecord-addednodes")]
    public NodeList AddedNodes { get; }

    /// <summary>Gets the nodes removed by the mutation.</summary>
    [IdlMember("removedNodes", "https://dom.spec.whatwg.org/#dom-mutationrecord-removednodes")]
    public NodeList RemovedNodes { get; }

    /// <summary>Gets the previous sibling of the added or removed nodes, or null.</summary>
    [IdlMember("previousSibling", "https://dom.spec.whatwg.org/#dom-mutationrecord-previoussibling")]
    public Node? PreviousSibling { get; }

    /// <summary>Gets the next sibling of the added or removed nodes, or null.</summary>
    [IdlMember("nextSibling", "https://dom.spec.whatwg.org/#dom-mutationrecord-nextsibling")]
    public Node? NextSibling { get; }

    /// <summary>Gets the local name of the changed attribute, or null.</summary>
    [IdlMember("attributeName", "https://dom.spec.whatwg.org/#dom-mutationrecord-attributename")]
    public string? AttributeName { get; }

    /// <summary>Gets the namespace of the changed attribute, or null.</summary>
    [IdlMember("attributeNamespace", "https://dom.spec.whatwg.org/#dom-mutationrecord-attributenamespace")]
    public string? AttributeNamespace { get; }

    /// <summary>
    /// Gets the previous value of the attribute or of the data when the observer requested it; otherwise null.
    /// </summary>
    [IdlMember("oldValue", "https://dom.spec.whatwg.org/#dom-mutationrecord-oldvalue")]
    public string? OldValue { get; }
}

/// <summary>"Report an exception" (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception).</summary>
[Obsolete("Use BrowsingContextOptions.UnhandledScriptError, or listen to the error event of the window (Window.ReportException).")]
public static class ExceptionReporter
{
    /// <summary>Raised when an exception is reported (e.g. thrown by an event listener or a mutation observer callback).</summary>
    public static event EventHandler<ExceptionReportedEventArgs>? ExceptionReported;

    internal static void Report(Exception exception)
    {
        ExceptionReported?.Invoke(null, new ExceptionReportedEventArgs(exception));
    }
}

/// <summary>The data of the <see cref="ExceptionReporter.ExceptionReported"/> event.</summary>
[Obsolete("Use BrowsingContextOptions.UnhandledScriptError, or listen to the error event of the window (Window.ReportException).")]
public sealed class ExceptionReportedEventArgs(Exception exception) : EventArgs
{
    /// <summary>The reported exception.</summary>
    public Exception Exception { get; } = exception;
}
