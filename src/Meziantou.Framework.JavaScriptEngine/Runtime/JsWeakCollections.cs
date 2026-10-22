using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The weak references of the engine (https://tc39.es/ecma262/#sec-weakref-processing-model).</summary>
internal static class WeakReferences
{
    /// <summary>CanBeHeldWeakly(v): an object, or a symbol that is not in the global symbol registry.</summary>
    public static bool CanBeHeldWeakly(JsValue value) => value.Cell is JsObject or JsSymbol { IsRegistered: false };
}

/// <summary>A WeakMap instance: [[WeakMapData]] is an ephemeron table keyed by the object or symbol cell.</summary>
internal sealed class JsWeakMap(JsObject prototype) : JsObject(prototype, ObjectClass.WeakMap)
{
    /// <summary>[[WeakMapData]]</summary>
    public ConditionalWeakTable<JsCell, StrongBox<JsValue>> Data { get; } = [];
}

/// <summary>A WeakSet instance: [[WeakSetData]] is an ephemeron table keyed by the object or symbol cell.</summary>
internal sealed class JsWeakSet(JsObject prototype) : JsObject(prototype, ObjectClass.WeakSet)
{
    /// <summary>The value of the entries of <see cref="Data"/> (only the keys matter).</summary>
    public static readonly object Present = new();

    /// <summary>[[WeakSetData]]</summary>
    public ConditionalWeakTable<JsCell, object> Data { get; } = [];
}

/// <summary>A WeakRef instance: [[WeakRefTarget]] is a .NET weak reference.</summary>
internal sealed class JsWeakRef(JsObject prototype, JsCell target) : JsObject(prototype, ObjectClass.WeakRef)
{
    private readonly WeakReference<JsCell> _target = new(target);

    /// <summary>WeakRefDeref(weakRef): the target (added to the kept objects), or undefined once it was collected.</summary>
    public JsValue Deref(JsEngine engine)
    {
        if (!_target.TryGetTarget(out var target))
            return JsValue.Undefined;

        engine.AddToKeptObjects(target);
        return new JsValue(target);
    }
}

/// <summary>
/// A FinalizationRegistry instance. Each target has a finalizable sentinel, attached with a <see cref="ConditionalWeakTable{TKey, TValue}"/>:
/// when the GC collects the target, the finalizer of the sentinel (on the finalizer thread) queues the cells and
/// enqueues a cleanup job with HostEnqueueGenericJob. The cleanup callbacks run in that job, on the engine thread.
/// </summary>
internal sealed class JsFinalizationRegistry : JsObject
{
    private static readonly JsJobAction CleanupJobAction = static (state, _) => ((JsFinalizationRegistry)state!).Cleanup();

    // [[Cells]]: the registry holds the cells (and their held values) strongly
    private readonly HashSet<FinalizationRegistryCell> _cells = [];
    private readonly ConditionalWeakTable<JsCell, TargetSentinel> _targets = [];
    private readonly ConditionalWeakTable<JsCell, List<FinalizationRegistryCell>> _cellsByToken = [];
    private readonly ConcurrentQueue<FinalizationRegistryCell> _collectedCells = new();
    private int _cleanupScheduled;

    public JsFinalizationRegistry(JsObject prototype, JsRealm realm, JsJobCallback cleanupCallback)
        : base(prototype, ObjectClass.FinalizationRegistry)
    {
        Realm = realm;
        CleanupCallback = cleanupCallback;
    }

    /// <summary>[[Realm]]</summary>
    public JsRealm Realm { get; }

    /// <summary>[[CleanupCallback]]</summary>
    public JsJobCallback CleanupCallback { get; }

    /// <summary>Gets the number of cells (the targets not yet cleaned up nor unregistered).</summary>
    public int CellCount => _cells.Count;

    /// <summary>Appends a cell to [[Cells]].</summary>
    public void Register(JsCell target, JsValue heldValue, JsCell? unregisterToken)
    {
        var cell = new FinalizationRegistryCell(heldValue);
        _cells.Add(cell);
        _targets.GetValue(target, _ => new TargetSentinel(this)).Add(cell);
        if (unregisterToken is not null)
        {
            _cellsByToken.GetOrCreateValue(unregisterToken).Add(cell);
        }
    }

    /// <summary>Removes the cells whose [[UnregisterToken]] is the token; returns whether a cell was removed.</summary>
    public bool Unregister(JsCell unregisterToken)
    {
        if (!_cellsByToken.TryGetValue(unregisterToken, out var cells))
            return false;

        _cellsByToken.Remove(unregisterToken);
        var removed = false;
        foreach (var cell in cells)
        {
            if (_cells.Remove(cell))
            {
                removed = true;
            }
        }

        return removed;
    }

    /// <summary>Called on the finalizer thread when a target was collected.</summary>
    private void OnTargetCollected(List<FinalizationRegistryCell> cells)
    {
        foreach (var cell in cells)
        {
            _collectedCells.Enqueue(cell);
        }

        ScheduleCleanup();
    }

    /// <summary>HostEnqueueFinalizationRegistryCleanupJob(finalizationRegistry): one job at a time.</summary>
    private void ScheduleCleanup()
    {
        if (Interlocked.Exchange(ref _cleanupScheduled, 1) == 0)
        {
            Realm.Engine.EnqueueGenericJob(CleanupJobAction, this, default, Realm);
        }
    }

    /// <summary>CleanupFinalizationRegistry(finalizationRegistry), in a job on the engine thread.</summary>
    private void Cleanup()
    {
        Volatile.Write(ref _cleanupScheduled, 0);
        var engine = Realm.Engine;
        while (_collectedCells.TryDequeue(out var cell))
        {
            // An unregistered cell is not in [[Cells]] anymore
            if (!_cells.Remove(cell))
                continue;

            JsException? exception = null;
            try
            {
                engine.CallJobCallback(CleanupCallback, JsValue.Undefined, [cell.HeldValue]);
            }
            catch (JsException ex)
            {
                exception = ex;
            }

            if (exception is not null)
            {
                // The error is reported by the job; the other cells are cleaned up by another job. The exception is
                // rethrown after the catch block: a throw in a catch block nests the exception dispatch.
                if (!_collectedCells.IsEmpty)
                {
                    ScheduleCleanup();
                }

                ExceptionDispatchInfo.Throw(exception);
            }
        }
    }

    /// <summary>The value attached to a target: its finalizer runs when the target is collected.</summary>
    private sealed class TargetSentinel(JsFinalizationRegistry registry)
    {
        private readonly List<FinalizationRegistryCell> _cells = [];

        public void Add(FinalizationRegistryCell cell) => _cells.Add(cell);

#pragma warning disable MA0055 // The finalizer is how the collection of the target is detected
        ~TargetSentinel()
        {
            registry.OnTargetCollected(_cells);
        }
#pragma warning restore MA0055
    }
}

/// <summary>A FinalizationRegistry cell: [[HeldValue]] (the target and the unregister token are held weakly by the registry).</summary>
internal sealed class FinalizationRegistryCell(JsValue heldValue)
{
    public JsValue HeldValue { get; } = heldValue;
}
