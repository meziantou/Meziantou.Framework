using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The tasks that represent the promise held by an internal slot of a platform object (e.g. the [[FontStatusPromise]] of a
/// FontFace): the bindings convert such a task to the same promise each time, even once it is completed. The other
/// completed tasks (e.g. <see cref="Task.CompletedTask"/>, shared by unrelated calls) are converted to a new promise.
/// </summary>
internal static class PromiseSlots
{
    private static readonly ConditionalWeakTable<Task, object?> Tasks = [];
    private static readonly ConditionalWeakTable<Task, object?> HandledTasks = [];

    /// <summary>Marks the task as the promise of an internal slot.</summary>
    public static void Register(Task task) => Tasks.AddOrUpdate(task, null);

    public static bool Contains(Task task) => Tasks.TryGetValue(task, out _);

    /// <summary>
    /// Marks the promise of the task as handled (https://webidl.spec.whatwg.org/#mark-a-promise-as-handled): its rejection is
    /// not reported as an unhandled rejection.
    /// </summary>
    public static void MarkAsHandled(Task task) => HandledTasks.AddOrUpdate(task, null);

    public static bool IsMarkedAsHandled(Task task) => HandledTasks.TryGetValue(task, out _);
}
