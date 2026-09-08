using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Diagnostics;

/// <summary>
/// Captures the <see cref="Activity"/> instances started within the current asynchronous scope.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="ActivityListener"/> is global: it observes every activity created in the process. This type registers such a listener but
/// only reports the activities started by the asynchronous flow that created it, using an <see cref="AsyncLocal{T}"/>. Disposing the
/// instance unregisters the listener.
/// </para>
/// <para>
/// The membership of an activity is decided when it starts, because an activity is frequently stopped on another thread, where the
/// asynchronous scope is no longer available. As a consequence, the activities that are still running when the listener is disposed are
/// never reported.
/// </para>
/// <para>
/// The captured activities are live references, not snapshots: their tags, status and duration are only final once they are stopped.
/// </para>
/// <para>
/// While the listener is registered, <see cref="ActivitySource.HasListeners"/> returns <see langword="true"/> for every matching source in
/// the whole process. The libraries that check it, such as <c>HttpClient</c> or ASP.NET Core, switch to their instrumented code path, so
/// <c>HttpClient</c> starts sending the <c>traceparent</c> header for the requests made inside the scope. Use
/// <see cref="ScopedActivityListenerOptions.SourceNames"/> to reduce the number of matching sources.
/// </para>
/// </remarks>
/// <example>
/// <code><![CDATA[
/// using var listener = new ScopedActivityListener(new() { SourceNames = ["Meziantou.Framework.DnsClient"] });
/// await DoWorkAsync();
/// foreach (var activity in listener.Activities)
/// {
///     Console.WriteLine($"{activity.OperationName}: {activity.Duration}");
/// }
/// ]]></code>
/// </example>
public sealed class ScopedActivityListener : IDisposable
{
    // Set while a listener callback is running on the current thread, so an event handler that starts an activity does not recurse
    [ThreadStatic]
    private static bool s_inCallback;

    private readonly AsyncLocal<Guid> _currentScopeId = new();
    private readonly Guid _scopeId = Guid.NewGuid();
    private readonly ActivityListener _listener;
    private readonly ActivitySamplingResult _samplingResult;
    private readonly bool _captureChildActivities;
    private readonly CapturedActivityCollection _activities;

    // Activity does not override Equals nor GetHashCode, so the default comparer compares the references
    private readonly ConcurrentDictionary<Activity, byte> _startedActivities = new();
    private readonly ConcurrentDictionary<ActivitySpanId, byte> _capturedSpanIds = new();

    private readonly Lock _subscribersLock = new();
    private readonly List<Channel<Activity>> _subscribers = [];

    private volatile bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ScopedActivityListener"/> class listening to every <see cref="ActivitySource"/>.</summary>
    public ScopedActivityListener()
        : this(options: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ScopedActivityListener"/> class.</summary>
    /// <param name="options">The options of the listener, or <see langword="null"/> to use the default options.</param>
    public ScopedActivityListener(ScopedActivityListenerOptions? options)
    {
        options ??= new ScopedActivityListenerOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxActivityCount);

        _samplingResult = options.SamplingResult;
        _captureChildActivities = options.CaptureChildActivities;
        _activities = new CapturedActivityCollection(options.MaxActivityCount);

        _currentScopeId.Value = _scopeId;

        var shouldListenTo = options.ShouldListenTo;
        if (shouldListenTo is null && options.SourceNames is { } sourceNames)
        {
            var names = sourceNames.ToArray();
            shouldListenTo = source => Array.IndexOf(names, source.Name) >= 0;
        }

        _listener = new ActivityListener
        {
            ShouldListenTo = shouldListenTo ?? (_ => true),
            Sample = (ref ActivityCreationOptions<ActivityContext> creationOptions) => Sample(creationOptions.Parent.SpanId),
            SampleUsingParentId = (ref ActivityCreationOptions<string> creationOptions) => Sample(parentSpanId: default),
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Gets the activities captured so far, in completion order. The collection is safe to enumerate while new activities are captured.</summary>
    public IReadOnlyCollection<Activity> Activities => _activities;

    /// <summary>
    /// Occurs when an activity of the scope starts. The event is raised synchronously on the thread starting the activity, so its tags,
    /// status and duration are not set yet. The exceptions thrown by the handlers are ignored to avoid breaking the observed code.
    /// </summary>
    public event EventHandler<ActivityEventArgs>? ActivityStarted;

    /// <summary>
    /// Occurs when an activity of the scope stops. The event is raised synchronously on the thread stopping the activity. The exceptions
    /// thrown by the handlers are ignored to avoid breaking the observed code.
    /// </summary>
    public event EventHandler<ActivityEventArgs>? ActivityStopped;

    /// <summary>Streams the activities of the scope as they complete.</summary>
    /// <param name="cancellationToken">A token to stop the enumeration.</param>
    /// <returns>
    /// The activities already captured, followed by the activities completing afterwards. The enumeration ends when the listener is
    /// disposed. Multiple enumerations can run concurrently, and each of them reports every activity.
    /// </returns>
    public async IAsyncEnumerable<Activity> GetActivitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<Activity>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_subscribersLock)
        {
            foreach (var activity in _activities.ToArray())
            {
                channel.Writer.TryWrite(activity);
            }

            if (_disposed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                _subscribers.Add(channel);
            }
        }

        try
        {
            await foreach (var activity in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return activity;
            }
        }
        finally
        {
            lock (_subscribersLock)
            {
                _subscribers.Remove(channel);
            }
        }
    }

    /// <summary>Unregisters the listener and completes the enumerations started by <see cref="GetActivitiesAsync"/>.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _listener.Dispose();
        _currentScopeId.Value = default;

        lock (_subscribersLock)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryComplete();
            }
        }

        _startedActivities.Clear();
        _capturedSpanIds.Clear();
    }

    private bool IsInScope => _currentScopeId.Value == _scopeId;

    private ActivitySamplingResult Sample(ActivitySpanId parentSpanId)
    {
        if (_disposed)
            return ActivitySamplingResult.None;

        if (IsInScope)
            return _samplingResult;

        if (_captureChildActivities && parentSpanId != default && _capturedSpanIds.ContainsKey(parentSpanId))
            return _samplingResult;

        return ActivitySamplingResult.None;
    }

    private void OnActivityStarted(Activity activity)
    {
        // The runtime enumerates the listeners without a lock, so a callback can run after Dispose returned
        if (_disposed || s_inCallback || !ShouldCapture(activity))
            return;

        _startedActivities.TryAdd(activity, 0);

        var spanId = activity.SpanId;
        if (spanId != default)
        {
            _capturedSpanIds.TryAdd(spanId, 0);
        }

        Raise(ActivityStarted, activity);
    }

    private void OnActivityStopped(Activity activity)
    {
        // The membership is decided when the activity starts: an activity is often stopped on another thread, where the scope is not available
        if (s_inCallback || !_startedActivities.TryRemove(activity, out _))
            return;

        lock (_subscribersLock)
        {
            _activities.Add(activity);
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(activity);
            }
        }

        Raise(ActivityStopped, activity);
    }

    private bool ShouldCapture(Activity activity)
    {
        if (IsInScope)
            return true;

        if (!_captureChildActivities)
            return false;

        if (activity.Parent is not null && _startedActivities.ContainsKey(activity.Parent))
            return true;

        var parentSpanId = activity.ParentSpanId;
        return parentSpanId != default && _capturedSpanIds.ContainsKey(parentSpanId);
    }

    private void Raise(EventHandler<ActivityEventArgs>? handler, Activity activity)
    {
        if (handler is null)
            return;

        s_inCallback = true;
        try
        {
            handler(this, new ActivityEventArgs(activity));
        }
        catch
        {
            // A handler must not break the code being observed
        }
        finally
        {
            s_inCallback = false;
        }
    }

    private sealed class CapturedActivityCollection : IReadOnlyCollection<Activity>
    {
        private readonly ICollection<Activity> _items;
        private readonly bool _threadSafe;

        public CapturedActivityCollection(int maxActivityCount)
        {
            if (maxActivityCount == int.MaxValue)
            {
                _items = new AppendOnlyCollection<Activity>();
                _threadSafe = true;
            }
            else
            {
                _items = new CircularBuffer<Activity>(maxActivityCount) { AllowOverwrite = true };
            }
        }

        public int Count => _items.Count;

        public void Add(Activity activity)
        {
            if (_threadSafe)
            {
                _items.Add(activity);
            }
            else
            {
                lock (_items)
                {
                    _items.Add(activity);
                }
            }
        }

        public Activity[] ToArray()
        {
            if (_threadSafe)
                return [.. _items];

            lock (_items)
            {
                return [.. _items];
            }
        }

        public IEnumerator<Activity> GetEnumerator()
        {
            if (_threadSafe)
                return _items.GetEnumerator();

            return ((IEnumerable<Activity>)ToArray()).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
