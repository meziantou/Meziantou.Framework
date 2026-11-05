using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A source type of the Compute Pressure API (https://w3c.github.io/compute-pressure/#dom-pressuresource).</summary>
/// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressuresource"/>
[IdlInterface("PressureSource", "https://w3c.github.io/compute-pressure/#dom-pressuresource")]
public enum PressureSource
{
    /// <summary>The central processing unit.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressuresource-cpu"/>
    [IdlMember("cpu", "https://w3c.github.io/compute-pressure/#dom-pressuresource-cpu")]
    Cpu,
}

/// <summary>A pressure state (https://w3c.github.io/compute-pressure/#dom-pressurestate).</summary>
/// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurestate"/>
[IdlInterface("PressureState", "https://w3c.github.io/compute-pressure/#dom-pressurestate")]
public enum PressureState
{
    /// <summary>The conditions of the device are at an acceptable level.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurestate-nominal"/>
    [IdlMember("nominal", "https://w3c.github.io/compute-pressure/#dom-pressurestate-nominal")]
    Nominal,

    /// <summary>The pressure, the temperature or the energy usage of the device are slightly elevated.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurestate-fair"/>
    [IdlMember("fair", "https://w3c.github.io/compute-pressure/#dom-pressurestate-fair")]
    Fair,

    /// <summary>The pressure, the temperature or the energy usage of the device are consistently highly elevated.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurestate-serious"/>
    [IdlMember("serious", "https://w3c.github.io/compute-pressure/#dom-pressurestate-serious")]
    Serious,

    /// <summary>The temperature of the device is significantly elevated, and it requires cooling down.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurestate-critical"/>
    [IdlMember("critical", "https://w3c.github.io/compute-pressure/#dom-pressurestate-critical")]
    Critical,
}

/// <summary>The host hooks of the Compute Pressure API (https://w3c.github.io/compute-pressure/, <see cref="BrowsingContextOptions.ComputePressure"/>).</summary>
public sealed class ComputePressureOptions
{
    /// <summary>
    /// Gets or sets the source of the pressure states of the device (<c>PressureObserver</c>), e.g. a
    /// <see cref="PressureSourceProvider"/> whose samples are set by the application with
    /// <see cref="PressureSourceProvider.SetSample"/>, or a subclass that reads the pressure of the operating system. A
    /// provider can be shared by several browsing contexts. The default is null: the <c>cpu</c> source is supported, but it
    /// reports no state.
    /// </summary>
    public PressureSourceProvider? Provider { get; init; }
}

/// <summary>A sample of a pressure source (https://w3c.github.io/compute-pressure/#dfn-pressure-source-sample), returned by a <see cref="PressureSourceProvider"/>.</summary>
/// <param name="State">The pressure state of the source.</param>
public sealed record PressureSample(PressureState State)
{
    /// <summary>
    /// Gets the estimated contribution of the web site to the pressure of the source, between 0 and 1
    /// (https://w3c.github.io/compute-pressure/#the-owncontributionestimate-attribute), or null when it is unknown. The
    /// default is null.
    /// </summary>
    public double? OwnContributionEstimate
    {
        get;
        init
        {
            if (value is { } estimate && (!double.IsFinite(estimate) || estimate is < 0 or > 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The estimate must be between 0 and 1");

            field = value;
        }
    }
}

/// <summary>
/// The pressure sources of the device (https://w3c.github.io/compute-pressure/#dfn-pressure-source), read by the
/// <c>PressureObserver</c> objects of the browsing contexts that share a <see cref="BrowsingContextOptions"/>
/// (<see cref="ComputePressureOptions.Provider"/>). The default implementation returns the samples set by the application
/// (<see cref="SetSample"/>), as the virtual pressure sources of WebDriver
/// (https://w3c.github.io/compute-pressure/#virtual-pressure-source). Override <see cref="IsSupported"/> and
/// <see cref="GetSample"/> to read another source, and call <see cref="OnSampleChanged"/> when a source reports a new sample.
/// </summary>
/// <remarks>
/// A provider can be shared by several browsing contexts, and used from any thread. While an observer requests a sample
/// interval (<c>sampleInterval</c>), the latest sample is read again at that interval, as a platform collector samples the
/// source at its sampling rate.
/// </remarks>
public class PressureSourceProvider
{
    private readonly Lock _lock = new();
    private readonly Dictionary<PressureSource, PressureSample?> _samples = [];
    private readonly HashSet<PressureSource> _unsupported = [];
    private readonly List<WeakReference<PressureObserverRegistry>> _registries = [];

    /// <summary>
    /// Returns whether the source can provide samples. The default implementation returns true, unless the source was made
    /// unsupported by <see cref="SetSupported"/>. The observers of an unsupported source are rejected with a
    /// <c>NotSupportedError</c>. It is called on the thread of an event loop, and must not block it.
    /// </summary>
    /// <param name="source">The source type.</param>
    public virtual bool IsSupported(PressureSource source)
    {
        lock (_lock)
        {
            return !_unsupported.Contains(source);
        }
    }

    /// <summary>Sets whether the source can provide samples (see <see cref="IsSupported"/>). It applies to the next calls of <c>observe()</c>.</summary>
    /// <param name="source">The source type.</param>
    /// <param name="supported">Whether the source can provide samples.</param>
    public void SetSupported(PressureSource source, bool supported)
    {
        lock (_lock)
        {
            if (supported)
            {
                _unsupported.Remove(source);
            }
            else
            {
                _unsupported.Add(source);
            }
        }
    }

    /// <summary>
    /// Returns the latest sample of the source (https://w3c.github.io/compute-pressure/#dfn-latest-sample), or null when it
    /// has no sample. The default implementation returns the sample set by <see cref="SetSample"/>. It is called on the
    /// thread of an event loop, and must not block it.
    /// </summary>
    /// <param name="source">The source type.</param>
    public virtual PressureSample? GetSample(PressureSource source)
    {
        lock (_lock)
        {
            return _samples.GetValueOrDefault(source);
        }
    }

    /// <summary>
    /// Sets the latest sample of a source (null to remove it), returned by the default implementation of
    /// <see cref="GetSample"/>, then delivers it to the observers of the documents (<see cref="OnSampleChanged"/>).
    /// </summary>
    /// <param name="source">The source type.</param>
    /// <param name="sample">The new sample, or null.</param>
    public void SetSample(PressureSource source, PressureSample? sample)
    {
        lock (_lock)
        {
            _samples[source] = sample;
        }

        OnSampleChanged(source);
    }

    /// <summary>
    /// Reads the sample of the source again for the observers of the documents and the workers, which are called from a task
    /// when the state changed. It can be called from any thread.
    /// </summary>
    /// <param name="source">The source type that reported a new sample.</param>
    protected void OnSampleChanged(PressureSource source)
    {
        List<PressureObserverRegistry> registries = [];
        lock (_lock)
        {
            foreach (var reference in _registries)
            {
                if (reference.TryGetTarget(out var registry))
                {
                    registries.Add(registry);
                }
            }
        }

        foreach (var registry in registries)
        {
            registry.OnSampleChanged(source);
        }
    }

    /// <summary>Registers the observers of a global object, to deliver the samples to them.</summary>
    internal void Register(PressureObserverRegistry registry)
    {
        lock (_lock)
        {
            _registries.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, registry));
            _registries.Add(new WeakReference<PressureObserverRegistry>(registry));
        }
    }
}

/// <summary>
/// Observes the pressure of the sources of the device (https://w3c.github.io/compute-pressure/#the-pressureobserver-object),
/// read from the <see cref="PressureSourceProvider"/> of the browsing context (<see cref="ComputePressureOptions.Provider"/>).
/// Its callback is invoked from a task with the records queued since the previous call, while the document has the focus.
/// </summary>
/// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver"/>
[IdlInterface("PressureObserver", "https://w3c.github.io/compute-pressure/#dom-pressureobserver")]
public sealed class PressureObserver
{
    /// <summary>https://w3c.github.io/compute-pressure/#dfn-max-queued-records</summary>
    private const int MaxQueuedRecords = 10;

    private static readonly PressureSource[] SupportedSourceTypes = [PressureSource.Cpu];

    private readonly PressureUpdateCallback _callback;
    private readonly List<(PressureSource Source, TaskCompletionSource Promise)> _pendingObservePromises = [];
    private readonly List<PressureRecord> _queuedRecords = [];
    private readonly Dictionary<PressureSource, PressureRecord> _lastRecordMap = [];
    private readonly Dictionary<PressureSource, uint> _sampleIntervalMap = [];

    /// <summary>Creates an observer of the pressure of the current global object (https://w3c.github.io/compute-pressure/#the-constructor-method).</summary>
    /// <param name="callback">The callback invoked with the records of the changes.</param>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/compute-pressure/#dom-pressureobserver-constructor")]
    public PressureObserver(PressureUpdateCallback callback)
        : this(GlobalScope.Current ?? throw new InvalidOperationException("A pressure observer can only be created by a window or a worker"), callback)
    {
    }

    /// <summary>Creates an observer of the pressure of a global object.</summary>
    /// <param name="global">The window or the worker whose observer it is.</param>
    /// <param name="callback">The callback invoked with the records of the changes.</param>
    [NotInIdl("The .NET constructor whose global object is explicit")]
    public PressureObserver(GlobalScope global, PressureUpdateCallback callback)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(callback);
        Global = global;
        _callback = callback;
    }

    /// <summary>The relevant global object of the observer.</summary>
    internal GlobalScope Global { get; }

    /// <summary>Gets the source types supported by the user agent, in alphabetical order (https://w3c.github.io/compute-pressure/#the-knownsources-attribute).</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver-knownsources"/>
    [IdlMember("knownSources", "https://w3c.github.io/compute-pressure/#dom-pressureobserver-knownsources")]
    public static IReadOnlyList<PressureSource> KnownSources => SupportedSourceTypes;

    /// <summary>
    /// Starts observing a source (https://w3c.github.io/compute-pressure/#the-observe-method). The task fails with a
    /// <c>NotAllowedError</c> <see cref="DomException"/> when a document of the global object is not allowed to use the
    /// <c>compute-pressure</c> feature, a <c>NotSupportedError</c> when the source is not supported by the
    /// <see cref="PressureSourceProvider"/>, and an <c>AbortError</c> when <see cref="Unobserve"/> or <see cref="Disconnect"/>
    /// is called before it completes.
    /// </summary>
    /// <param name="source">The observed source.</param>
    /// <param name="options">The requested sample interval.</param>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver-observe"/>
    [IdlMember("observe", "https://w3c.github.io/compute-pressure/#dom-pressureobserver-observe")]
    public Task Observe(PressureSource source, PressureObserverOptions? options = null)
    {
        // Steps 1-2
        foreach (var document in GetOwningDocumentSet(Global))
        {
            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.ComputePressure))
                return Task.FromException(DomException.NotAllowed("The document is not allowed to use the compute-pressure feature"));
        }

        // Steps 3-6
        _sampleIntervalMap[source] = options?.SampleInterval ?? 0;
        var promise = new TaskCompletionSource();
        _pendingObservePromises.Add((source, promise));

        // Step 7 (the parallel steps): the platform collector of the global object is created or reused, then a task
        // registers the observer
        var registry = PressureObserverRegistry.Get(Global);
        var hasCollector = registry.EnsureCollector(source);
        Global.QueueGlobalTask(() =>
        {
            if (!hasCollector)
            {
                RemovePendingPromise(promise);
                promise.TrySetException(DomException.NotSupported("The pressure source is not supported"));
                return;
            }

            if (promise.Task.IsCompleted)
            {
                RemovePendingPromise(promise);
                registry.DeactivateIfUnobserved(source);
                return;
            }

            // The collector is created again when the observers of an earlier call deactivated it meanwhile
            if (!registry.EnsureCollector(source))
            {
                RemovePendingPromise(promise);
                promise.TrySetException(DomException.NotSupported("The pressure source is not supported"));
                return;
            }

            registry.Register(source, this);
            RemovePendingPromise(promise);
            promise.TrySetResult();
            registry.Deliver(source);
        });
        return promise.Task;
    }

    /// <summary>Stops observing a source (https://w3c.github.io/compute-pressure/#the-unobserve-method): its queued records are removed.</summary>
    /// <param name="source">The source that is no longer observed.</param>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver-unobserve"/>
    [IdlMember("unobserve", "https://w3c.github.io/compute-pressure/#dom-pressureobserver-unobserve")]
    public void Unobserve(PressureSource source)
    {
        if (!SupportedSourceTypes.Contains(source))
            throw DomException.NotSupported("The pressure source is not supported");

        _queuedRecords.RemoveAll(record => record.Source == source);
        _sampleIntervalMap.Remove(source);
        _lastRecordMap.Remove(source);
        foreach (var (promiseSource, promise) in _pendingObservePromises.ToArray())
        {
            if (promiseSource == source)
            {
                promise.TrySetException(new DomException("The source is no longer observed", "AbortError"));
            }
        }

        PressureObserverRegistry.GetOrNull(Global)?.Unregister(source, this);
    }

    /// <summary>Stops observing all the sources (https://w3c.github.io/compute-pressure/#the-disconnect-method): the queued records are removed.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver-disconnect"/>
    [IdlMember("disconnect", "https://w3c.github.io/compute-pressure/#dom-pressureobserver-disconnect")]
    public void Disconnect()
    {
        _queuedRecords.Clear();
        _sampleIntervalMap.Clear();
        _lastRecordMap.Clear();
        foreach (var (_, promise) in _pendingObservePromises.ToArray())
        {
            promise.TrySetException(new DomException("The observer is disconnected", "AbortError"));
        }

        if (PressureObserverRegistry.GetOrNull(Global) is { } registry)
        {
            foreach (var source in SupportedSourceTypes)
            {
                registry.Unregister(source, this);
            }
        }
    }

    /// <summary>Returns the queued records and empties the queue (https://w3c.github.io/compute-pressure/#the-takerecords-method).</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserver-takerecords"/>
    [IdlMember("takeRecords", "https://w3c.github.io/compute-pressure/#dom-pressureobserver-takerecords")]
    public IReadOnlyList<PressureRecord> TakeRecords()
    {
        var records = _queuedRecords.ToArray();
        _queuedRecords.Clear();
        return records;
    }

    /// <summary>The sample interval requested for a source, in milliseconds (0 when the observer only wants the changes).</summary>
    internal uint GetSampleInterval(PressureSource source) => _sampleIntervalMap.GetValueOrDefault(source);

    /// <summary>
    /// The steps of https://w3c.github.io/compute-pressure/#dfn-data-collection for one observer: the sample becomes a record
    /// when the observer may receive data, passes the rate test and should be dispatched.
    /// </summary>
    /// <returns>Whether a record was queued.</returns>
    internal bool OnSample(PressureSource source, PressureSample sample, double time)
    {
        if (!MayReceiveData(Global))
            return false;

        // https://w3c.github.io/compute-pressure/#dfn-passes-rate-test
        _lastRecordMap.TryGetValue(source, out var last);
        var sampleInterval = GetSampleInterval(source);
        if (last is not null && time - last.Time < sampleInterval)
            return false;

        // https://w3c.github.io/compute-pressure/#dfn-should-dispatch
        if (sampleInterval == 0 && last is not null && last.State == sample.State && last.OwnContributionEstimate == sample.OwnContributionEstimate)
            return false;

        // https://w3c.github.io/compute-pressure/#dfn-queue-a-record: the rate obfuscation is not applied (the samples come
        // from the host)
        var record = new PressureRecord(source, sample.State, sample.OwnContributionEstimate, time);
        if (_queuedRecords.Count > MaxQueuedRecords)
        {
            _queuedRecords.RemoveAt(0);
        }

        _queuedRecords.Add(record);
        _lastRecordMap[source] = record;
        return true;
    }

    /// <summary>Invokes the callback with the queued records (https://w3c.github.io/compute-pressure/#dfn-notify-pressure-observers).</summary>
    internal void Notify()
    {
        if (_queuedRecords.Count == 0)
            return;

        var records = TakeRecords();
        try
        {
            _callback(records, this);
        }
#pragma warning disable CA1031 // "report the exception": an exception of a callback does not stop the notification of the other observers
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, Global);
        }
    }

    private void RemovePendingPromise(TaskCompletionSource promise) => _pendingObservePromises.RemoveAll(tuple => ReferenceEquals(tuple.Promise, promise));

    /// <summary>
    /// https://w3c.github.io/compute-pressure/#dfn-owning-document-set: the associated document of a window, or the documents
    /// that own a dedicated worker (directly or through other dedicated workers).
    /// </summary>
    internal static IEnumerable<Document> GetOwningDocumentSet(GlobalScope global)
    {
        var current = global;
        while (true)
        {
            switch (current)
            {
                case Window window:
                    yield return window.Document;
                    yield break;

                case WorkerGlobalScope { Agent.Owner: { } owner }:
                    current = owner;
                    break;

                default:
                    yield break;
            }
        }
    }

    /// <summary>https://w3c.github.io/compute-pressure/#dfn-may-receive-data</summary>
    private static bool MayReceiveData(GlobalScope global)
    {
        foreach (var document in GetOwningDocumentSet(global))
        {
            if (HasImplicitFocus(document))
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/compute-pressure/#dfn-document-has-implicit-focus: the top-level browsing context has the system
    /// focus, and the document that contains the focused area is same origin with the document (there is no
    /// Picture-in-Picture session, nor capture).
    /// </summary>
    private static bool HasImplicitFocus(Document document)
    {
        if (!document.IsFullyActive || document.BrowsingContext is not { } browsingContext || !browsingContext.HasSystemFocus)
            return false;

        var focusedArea = FocusModel.GetCurrentlyFocusedArea(browsingContext.Top.Document);
        var focusedDocument = focusedArea as Document ?? focusedArea.NodeDocument;
        return OriginRecord.IsSameOrigin(document.Origin, focusedDocument.Origin);
    }
}

/// <summary>https://w3c.github.io/compute-pressure/#dom-pressureupdatecallback</summary>
/// <param name="changes">The records of the changes, in the order in which they were observed.</param>
/// <param name="observer">The observer.</param>
/// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureupdatecallback"/>
[IdlInterface("PressureUpdateCallback", "https://w3c.github.io/compute-pressure/#dom-pressureupdatecallback")]
public delegate void PressureUpdateCallback(IReadOnlyList<PressureRecord> changes, PressureObserver observer);

/// <summary>The options of <see cref="PressureObserver.Observe"/> (https://w3c.github.io/compute-pressure/#dom-pressureobserveroptions).</summary>
/// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserveroptions"/>
[IdlInterface("PressureObserverOptions", "https://w3c.github.io/compute-pressure/#dom-pressureobserveroptions")]
public sealed class PressureObserverOptions
{
    /// <summary>
    /// Gets or sets the requested sample interval, in milliseconds: the callback is not invoked more often. With 0 (the
    /// default), the callback is only invoked when the state changes; otherwise it is invoked at this interval, whether the
    /// state changed or not.
    /// </summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressureobserveroptions-sampleinterval"/>
    [IdlMember("sampleInterval", "https://w3c.github.io/compute-pressure/#dom-pressureobserveroptions-sampleinterval")]
    public uint SampleInterval { get; set; }
}

/// <summary>A pressure state observed by a <see cref="PressureObserver"/> (https://w3c.github.io/compute-pressure/#the-pressurerecord-interface).</summary>
/// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurerecord"/>
[IdlInterface("PressureRecord", "https://w3c.github.io/compute-pressure/#dom-pressurerecord")]
public sealed class PressureRecord
{
    internal PressureRecord(PressureSource source, PressureState state, double? ownContributionEstimate, double time)
    {
        Source = source;
        State = state;
        OwnContributionEstimate = ownContributionEstimate;
        Time = time;
    }

    /// <summary>Gets the observed source.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurerecord-source"/>
    [IdlMember("source", "https://w3c.github.io/compute-pressure/#dom-pressurerecord-source")]
    public PressureSource Source { get; }

    /// <summary>Gets the pressure state of the source.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurerecord-state"/>
    [IdlMember("state", "https://w3c.github.io/compute-pressure/#dom-pressurerecord-state")]
    public PressureState State { get; }

    /// <summary>Gets the estimated contribution of the web site to the pressure, between 0 and 1, or null.</summary>
    [NotInIdl("ownContributionEstimate is in the experimental partial interface of PressureRecord, which webref does not extract")]
    public double? OwnContributionEstimate { get; }

    /// <summary>Gets the time of the sample, in milliseconds relative to the time origin of the global object of the observer.</summary>
    /// <seealso href="https://w3c.github.io/compute-pressure/#dom-pressurerecord-time"/>
    [IdlMember("time", "https://w3c.github.io/compute-pressure/#dom-pressurerecord-time")]
    public double Time { get; }
}

/// <summary>
/// The registered observer lists and the platform collectors of a global object
/// (https://w3c.github.io/compute-pressure/#dfn-registered-observer-list, https://w3c.github.io/compute-pressure/#dfn-platform-collector-mapping).
/// </summary>
internal sealed class PressureObserverRegistry
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GlobalScope, PressureObserverRegistry> Registries = [];

    private readonly GlobalScope _global;
    private readonly Dictionary<PressureSource, List<PressureObserver>> _registeredObservers = [];

    /// <summary>The platform collectors: the sources whose data is collected, with the version of the poll timer.</summary>
    private readonly Dictionary<PressureSource, int> _collectors = [];

    /// <summary>https://w3c.github.io/compute-pressure/#dfn-pressure-observer-task-queued</summary>
    private bool _taskQueued;

    /// <summary>The last version given to a poll timer: a timer only polls while its version is the one of its collector.</summary>
    private int _lastVersion;

    private PressureObserverRegistry(GlobalScope global) => _global = global;

    public static PressureObserverRegistry Get(GlobalScope global) => Registries.GetValue(global, static global => new PressureObserverRegistry(global));

    public static PressureObserverRegistry? GetOrNull(GlobalScope global) => Registries.TryGetValue(global, out var registry) ? registry : null;

    private PressureSourceProvider? Provider => _global.Options.ComputePressure?.Provider;

    /// <summary>Creates the platform collector of a source if needed, and activates it; false when the source is not supported.</summary>
    public bool EnsureCollector(PressureSource source)
    {
        if (_collectors.ContainsKey(source))
            return true;

        if (Provider is { } provider)
        {
            bool supported;
            try
            {
                supported = provider.IsSupported(source);
            }
#pragma warning disable CA1031 // An error of the provider makes the source unsupported
            catch (Exception)
#pragma warning restore CA1031
            {
                supported = false;
            }

            if (!supported)
                return false;

            provider.Register(this);
        }

        _collectors[source] = 0;
        return true;
    }

    public void Register(PressureSource source, PressureObserver observer)
    {
        if (!_registeredObservers.TryGetValue(source, out var observers))
        {
            observers = [];
            _registeredObservers[source] = observers;
        }

        if (!observers.Contains(observer))
        {
            observers.Add(observer);
        }
    }

    public void Unregister(PressureSource source, PressureObserver observer)
    {
        if (_registeredObservers.TryGetValue(source, out var observers))
        {
            observers.Remove(observer);
        }

        DeactivateIfUnobserved(source);
    }

    /// <summary>Deactivates the data collection of a source that no observer observes, and removes its platform collector.</summary>
    public void DeactivateIfUnobserved(PressureSource source)
    {
        if (_registeredObservers.TryGetValue(source, out var observers) && observers.Count > 0)
            return;

        _registeredObservers.Remove(source);
        _collectors.Remove(source);
    }

    /// <summary>
    /// https://w3c.github.io/compute-pressure/#handling-change-of-fully-active-status: the data collection of the sources
    /// stops when the document is unloaded.
    /// </summary>
    public static void OnUnloaded(GlobalScope global)
    {
        if (GetOrNull(global) is { } registry)
        {
            registry._collectors.Clear();
        }
    }

    /// <summary>A source reported a new sample: it is delivered by a task of the global object. Called from any thread.</summary>
    public void OnSampleChanged(PressureSource source) => _global.QueueGlobalTask(() => Deliver(source));

    /// <summary>
    /// https://w3c.github.io/compute-pressure/#dfn-data-collection: the latest sample of the source is delivered to the
    /// observers. While an observer requests a sample interval, the sample is read again at that interval (the sampling rate
    /// of the platform collector).
    /// </summary>
    public void Deliver(PressureSource source)
    {
        if (!_collectors.ContainsKey(source) || !_global.CanRunScript)
            return;

        // A new delivery restarts the poll timer
        var version = ++_lastVersion;
        _collectors[source] = version;
        if (!_registeredObservers.TryGetValue(source, out var observers) || observers.Count == 0)
            return;

        PressureSample? sample;
        try
        {
            sample = Provider?.GetSample(source);
        }
#pragma warning disable CA1031 // An error of the provider is a missing sample
        catch (Exception)
#pragma warning restore CA1031
        {
            sample = null;
        }

        if (sample is null)
            return;

        var time = _global.GetCurrentHighResolutionTime();
        var queued = false;
        uint pollInterval = 0;
        foreach (var observer in observers.ToArray())
        {
            queued |= observer.OnSample(source, sample, time);
            var interval = observer.GetSampleInterval(source);
            if (interval > 0 && (pollInterval == 0 || interval < pollInterval))
            {
                pollInterval = interval;
            }
        }

        if (queued)
        {
            QueuePressureObserverTask();
        }

        if (pollInterval > 0)
        {
            _global.EventLoop.QueueTimer(TimeSpan.FromMilliseconds(pollInterval), () => _global.QueueGlobalTask(() =>
            {
                if (_collectors.TryGetValue(source, out var current) && current == version)
                {
                    Deliver(source);
                }
            }));
        }
    }

    /// <summary>https://w3c.github.io/compute-pressure/#dfn-queue-a-pressure-observer-task</summary>
    private void QueuePressureObserverTask()
    {
        if (_taskQueued)
            return;

        _taskQueued = true;
        _global.QueueGlobalTask(() =>
        {
            // https://w3c.github.io/compute-pressure/#dfn-notify-pressure-observers
            _taskQueued = false;
            var notifySet = new List<PressureObserver>();
            foreach (var observers in _registeredObservers.Values)
            {
                foreach (var observer in observers)
                {
                    if (!notifySet.Contains(observer))
                    {
                        notifySet.Add(observer);
                    }
                }
            }

            foreach (var observer in notifySet)
            {
                observer.Notify();
            }
        });
    }
}
