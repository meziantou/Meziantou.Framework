namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The priority of the tasks and of the continuations of a <see cref="Scheduler"/> (https://wicg.github.io/scheduling-apis/#enumdef-taskpriority).</summary>
[IdlInterface("TaskPriority", "https://wicg.github.io/scheduling-apis/#enumdef-taskpriority")]
public enum TaskPriority
{
    /// <summary>The highest priority: the tasks that should run as soon as possible, e.g. the work that responds to the input of the user.</summary>
    [IdlMember("user-blocking", "https://wicg.github.io/scheduling-apis/#dom-taskpriority-user-blocking")]
    UserBlocking,

    /// <summary>The default priority: the tasks whose effects are visible to the user, but not immediately.</summary>
    [IdlMember("user-visible", "https://wicg.github.io/scheduling-apis/#dom-taskpriority-user-visible")]
    UserVisible,

    /// <summary>The lowest priority: the tasks that are not time-critical.</summary>
    [IdlMember("background", "https://wicg.github.io/scheduling-apis/#dom-taskpriority-background")]
    Background,
}

/// <summary>The initialization dictionary of a <see cref="TaskPriorityChangeEvent"/> (https://wicg.github.io/scheduling-apis/#dictdef-taskprioritychangeeventinit).</summary>
[IdlInterface("TaskPriorityChangeEventInit", "https://wicg.github.io/scheduling-apis/#dictdef-taskprioritychangeeventinit")]
public sealed class TaskPriorityChangeEventInit : EventInit
{
    /// <summary>Gets the priority of the signal before the change.</summary>
    [IdlMember("previousPriority", "https://wicg.github.io/scheduling-apis/#dom-taskprioritychangeeventinit-previouspriority")]
    public required TaskPriority PreviousPriority { get; init; }
}

/// <summary>
/// The <c>prioritychange</c> event fired at a <see cref="TaskSignal"/> when its priority changes
/// (https://wicg.github.io/scheduling-apis/#sec-task-priority-change-event): the new priority is the one of its target.
/// </summary>
[IdlInterface("TaskPriorityChangeEvent", "https://wicg.github.io/scheduling-apis/#taskprioritychangeevent")]
public sealed class TaskPriorityChangeEvent : Event
{
    /// <summary>Creates an event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://wicg.github.io/scheduling-apis/#dom-taskprioritychangeevent-taskprioritychangeevent")]
    public TaskPriorityChangeEvent(string type, TaskPriorityChangeEventInit priorityChangeEventInitDict)
        : base(type, priorityChangeEventInitDict)
    {
        ArgumentNullException.ThrowIfNull(priorityChangeEventInitDict);
        PreviousPriority = priorityChangeEventInitDict.PreviousPriority;
    }

    /// <summary>Gets the priority of the signal before the change.</summary>
    [IdlMember("previousPriority", "https://wicg.github.io/scheduling-apis/#dom-taskprioritychangeevent-previouspriority")]
    public TaskPriority PreviousPriority { get; }
}

/// <summary>The initialization dictionary of a <see cref="TaskController"/> (https://wicg.github.io/scheduling-apis/#dictdef-taskcontrollerinit).</summary>
[IdlInterface("TaskControllerInit", "https://wicg.github.io/scheduling-apis/#dictdef-taskcontrollerinit")]
public sealed class TaskControllerInit
{
    /// <summary>Gets the initial priority of the signal of the controller.</summary>
    [IdlMember("priority", "https://wicg.github.io/scheduling-apis/#dom-taskcontrollerinit-priority")]
    public TaskPriority Priority { get; init; } = TaskPriority.UserVisible;
}

/// <summary>
/// Aborts the tasks of a <see cref="Scheduler"/> that were posted with its signal and changes their priority
/// (https://wicg.github.io/scheduling-apis/#sec-task-controller).
/// </summary>
[IdlInterface("TaskController", "https://wicg.github.io/scheduling-apis/#taskcontroller")]
public sealed class TaskController : AbortController
{
    /// <summary>Creates a controller whose signal is a new <see cref="TaskSignal"/> with the priority of <paramref name="init"/> (<see cref="TaskPriority.UserVisible"/> by default).</summary>
    [IdlMember("constructor", "https://wicg.github.io/scheduling-apis/#dom-taskcontroller-taskcontroller")]
    public TaskController(TaskControllerInit? init = null)
        : base(new TaskSignal(init?.Priority ?? TaskPriority.UserVisible))
    {
    }

    /// <summary>Gets the signal controlled by the controller, which is a <see cref="TaskSignal"/>.</summary>
    [NotInIdl("The signal attribute of AbortController, typed as the TaskSignal it returns")]
    public new TaskSignal Signal => (TaskSignal)base.Signal;

    /// <summary>
    /// Changes the priority of the signal (https://wicg.github.io/scheduling-apis/#dom-taskcontroller-setpriority): the
    /// tasks and the continuations that were scheduled with the signal get the priority, and <c>prioritychange</c> is fired
    /// at the signal and at the signals that depend on it.
    /// </summary>
    /// <exception cref="DomException">The priority of the signal is being changed (<c>NotAllowedError</c>).</exception>
    [IdlMember("setPriority", "https://wicg.github.io/scheduling-apis/#dom-taskcontroller-setpriority")]
    public void SetPriority(TaskPriority priority) => Signal.SignalPriorityChange(priority);
}

/// <summary>The priority of <see cref="TaskSignalAnyInit"/>: a fixed <see cref="TaskPriority"/>, or the <see cref="TaskSignal"/> whose priority is followed.</summary>
[IdlInterface("(TaskPriority or TaskSignal)", "https://wicg.github.io/scheduling-apis/#dom-tasksignalanyinit-priority")]
public readonly struct TaskPriorityOrSignal : IUnionValue, IEquatable<TaskPriorityOrSignal>
{
    private readonly object? _value;

    private TaskPriorityOrSignal(object value) => _value = value;

    /// <summary>Gets the value: a <see cref="TaskPriority"/> or a <see cref="TaskSignal"/>.</summary>
    [NotInIdl]
    public object Value => _value ?? TaskPriority.UserVisible;

    /// <summary>Gets the value as a <see cref="TaskSignal"/>, or null when the value is a priority.</summary>
    [NotInIdl]
    public TaskSignal? Signal => _value as TaskSignal;

    /// <summary>Gets the value as a <see cref="TaskPriority"/>, or null when the value is a signal.</summary>
    [NotInIdl]
    public TaskPriority? Priority => Value as TaskPriority?;

    /// <summary>Creates a union value from a priority.</summary>
    [NotInIdl]
    public static implicit operator TaskPriorityOrSignal(TaskPriority value) => new(value);

    /// <summary>Creates a union value from a signal.</summary>
    [NotInIdl]
    public static implicit operator TaskPriorityOrSignal(TaskSignal value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a union value from a priority.</summary>
    [NotInIdl]
    public static TaskPriorityOrSignal FromTaskPriority(TaskPriority value) => value;

    /// <summary>Creates a union value from a signal.</summary>
    [NotInIdl]
    public static TaskPriorityOrSignal FromTaskSignal(TaskSignal value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(TaskPriorityOrSignal other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TaskPriorityOrSignal other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value.ToString();

    /// <summary>Returns whether the two unions hold equal values.</summary>
    [NotInIdl]
    public static bool operator ==(TaskPriorityOrSignal left, TaskPriorityOrSignal right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(TaskPriorityOrSignal left, TaskPriorityOrSignal right) => !left.Equals(right);
}

/// <summary>The options of <see cref="TaskSignal.Any"/> (https://wicg.github.io/scheduling-apis/#dictdef-tasksignalanyinit).</summary>
[IdlInterface("TaskSignalAnyInit", "https://wicg.github.io/scheduling-apis/#dictdef-tasksignalanyinit")]
public sealed class TaskSignalAnyInit
{
    /// <summary>Gets the priority of the signal: a fixed priority, or a signal whose priority the new signal follows.</summary>
    [IdlMember("priority", "https://wicg.github.io/scheduling-apis/#dom-tasksignalanyinit-priority")]
    public TaskPriorityOrSignal Priority { get; init; } = TaskPriority.UserVisible;
}

/// <summary>
/// An <see cref="AbortSignal"/> that also has a priority (https://wicg.github.io/scheduling-apis/#sec-task-signal): the
/// tasks posted to a <see cref="Scheduler"/> with the signal of a <see cref="TaskController"/> follow its priority.
/// </summary>
[IdlInterface("TaskSignal", "https://wicg.github.io/scheduling-apis/#tasksignal")]
public sealed class TaskSignal : AbortSignal
{
    // https://wicg.github.io/scheduling-apis/#tasksignal-source-signal and #tasksignal-dependent-signals are weak: a source
    // signal that is collected can no longer change its priority
    private WeakReference<TaskSignal>? _sourceSignal;
    private List<WeakReference<TaskSignal>>? _dependentSignals;

    // The dependent signals that must not be garbage collected (https://wicg.github.io/scheduling-apis/#sec-task-signal-garbage-collection)
    private HashSet<TaskSignal>? _retainedDependentSignals;
    private bool _retainedBySourceSignal;

    /// <summary>https://wicg.github.io/scheduling-apis/#tasksignal-priority-changing</summary>
    private bool _priorityChanging;

    /// <summary>https://wicg.github.io/scheduling-apis/#tasksignal-dependent</summary>
    private bool _priorityDependent;

    internal TaskSignal(TaskPriority priority)
    {
        Priority = priority;
    }

    /// <summary>Gets the priority of the signal (https://wicg.github.io/scheduling-apis/#dom-tasksignal-priority).</summary>
    [IdlMember("priority", "https://wicg.github.io/scheduling-apis/#dom-tasksignal-priority")]
    public TaskPriority Priority { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>prioritychange</c> event.</summary>
    [IdlMember("onprioritychange", "https://wicg.github.io/scheduling-apis/#dom-tasksignal-onprioritychange")]
    public Action<Event>? OnPriorityChange
    {
        get => GetEventHandler("prioritychange");
        set => SetEventHandler("prioritychange", value);
    }

    /// <summary>
    /// https://wicg.github.io/scheduling-apis/#tasksignal-has-fixed-priority: "a dependent signal with a null source signal".
    /// The tasks of the signals that have a fixed priority share the task queues of their priority.
    /// </summary>
    internal bool HasFixedPriority => _priorityDependent && (_sourceSignal is null || !_sourceSignal.TryGetTarget(out _));

    /// <summary>
    /// Returns a signal that is aborted when one of <paramref name="signals"/> is, and whose priority is the one of
    /// <paramref name="init"/>: a fixed priority, or the priority of a signal, which the new signal follows
    /// (https://wicg.github.io/scheduling-apis/#dom-tasksignal-any).
    /// </summary>
    [IdlMember("any", "https://wicg.github.io/scheduling-apis/#dom-tasksignal-any")]
    public static TaskSignal Any(IEnumerable<AbortSignal> signals, TaskSignalAnyInit? init = null)
    {
        ArgumentNullException.ThrowIfNull(signals);
        return CreateDependentTaskSignal(signals, (init ?? new TaskSignalAnyInit()).Priority);
    }

    /// <summary>https://wicg.github.io/scheduling-apis/#create-a-fixed-priority-unabortable-task-signal</summary>
    internal static TaskSignal CreateFixedPriorityUnabortableSignal(TaskPriority priority) => CreateDependentTaskSignal([], priority);

    /// <summary>https://wicg.github.io/scheduling-apis/#create-a-dependent-task-signal</summary>
    private static TaskSignal CreateDependentTaskSignal(IEnumerable<AbortSignal> signals, TaskPriorityOrSignal priority)
    {
        var resultSignal = new TaskSignal(TaskPriority.UserVisible);
        InitializeDependentSignal(resultSignal, signals);
        resultSignal._priorityDependent = true;
        if (priority.Signal is not { } sourceSignal)
        {
            resultSignal.Priority = priority.Priority ?? TaskPriority.UserVisible;
            return resultSignal;
        }

        resultSignal.Priority = sourceSignal.Priority;
        if (!sourceSignal.HasFixedPriority)
        {
            // "If sourceSignal's dependent is true, then set sourceSignal to sourceSignal's source signal"
            if (sourceSignal._priorityDependent)
            {
                if (sourceSignal._sourceSignal is null || !sourceSignal._sourceSignal.TryGetTarget(out var source))
                    return resultSignal;

                sourceSignal = source;
            }

            resultSignal._sourceSignal = new WeakReference<TaskSignal>(sourceSignal);
            var dependentSignals = sourceSignal._dependentSignals ??= [];
            dependentSignals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            dependentSignals.Add(new WeakReference<TaskSignal>(resultSignal));
        }

        return resultSignal;
    }

    /// <summary>https://wicg.github.io/scheduling-apis/#tasksignal-signal-priority-change</summary>
    internal void SignalPriorityChange(TaskPriority priority)
    {
        if (_priorityChanging)
            throw DomException.NotAllowed("The priority of the signal is being changed");

        if (Priority == priority)
            return;

        _priorityChanging = true;
        try
        {
            // The priority change algorithms are the ones of the task queues of the schedulers, which read the priority of the signal
            var previousPriority = Priority;
            Priority = priority;
            FireEvent(new TaskPriorityChangeEvent("prioritychange", new TaskPriorityChangeEventInit { PreviousPriority = previousPriority }));
            if (_dependentSignals is { Count: > 0 } dependentSignals)
            {
                foreach (var reference in dependentSignals.ToArray())
                {
                    if (reference.TryGetTarget(out var dependentSignal))
                    {
                        dependentSignal.SignalPriorityChange(priority);
                    }
                }
            }
        }
        finally
        {
            _priorityChanging = false;
        }
    }

    /// <summary>
    /// https://wicg.github.io/scheduling-apis/#sec-task-signal-garbage-collection: "a dependent TaskSignal object must not be
    /// garbage collected while its source signal is non-null and it has registered event listeners for its prioritychange
    /// event". The source signal only references its dependent signals weakly, so it also references this signal strongly
    /// while it has listeners.
    /// </summary>
    private void UpdateRetentionBySourceSignal()
    {
        if (_sourceSignal is null || !_sourceSignal.TryGetTarget(out var sourceSignal))
            return;

        var retain = HasEventListener("prioritychange");
        if (retain == _retainedBySourceSignal)
            return;

        _retainedBySourceSignal = retain;
        if (retain)
        {
            (sourceSignal._retainedDependentSignals ??= new HashSet<TaskSignal>(ReferenceEqualityComparer.Instance)).Add(this);
        }
        else
        {
            sourceSignal._retainedDependentSignals?.Remove(this);
        }
    }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        base.OnEventListenerAdded(listener);
        if (listener.Type == "prioritychange")
        {
            UpdateRetentionBySourceSignal();
        }
    }

    private protected override void OnEventListenerRemoved(EventListenerEntry listener)
    {
        base.OnEventListenerRemoved(listener);
        if (listener.Type == "prioritychange")
        {
            UpdateRetentionBySourceSignal();
        }
    }
}
