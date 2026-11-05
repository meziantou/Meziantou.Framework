namespace Meziantou.Framework.WebPlatform.Dom;

// The long tasks (https://w3c.github.io/longtasks/): the duration of the tasks that run scripts
public sealed partial class DomEventLoop
{
    private int _taskDepth;
    private List<Window>? _taskScriptEvaluationWindows;
    private bool _taskTimed;
    private long _taskStartTimestamp;
    private long _taskEndTimestamp;

    /// <summary>
    /// Adds a window to the script evaluation environment settings object set of the currently running task
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#prepare-to-run-script), used to report the long tasks. With
    /// <see cref="TimeProvider.System"/>, the task is timed from its first script. Another clock is not read for the long
    /// tasks, as reading a fake clock can advance it: the task is timed from the first to the last time it reads the clock
    /// (see <see cref="GetTimestamp"/>).
    /// </summary>
    internal void RecordScriptEvaluation(GlobalScope global)
    {
        if (_taskDepth == 0 || global is not Window window)
            return;

        if (_taskScriptEvaluationWindows is null)
        {
            _taskScriptEvaluationWindows = [window];
            if (ReferenceEquals(TimeProvider, TimeProvider.System))
            {
                ObserveTimestamp(TimeProvider.GetTimestamp());
            }
        }
        else if (!_taskScriptEvaluationWindows.Contains(window))
        {
            _taskScriptEvaluationWindows.Add(window);
        }
    }

    /// <summary>
    /// Reads the clock of the event loop for the current high resolution time of a global object (e.g. <c>performance.now()</c>):
    /// the reads of a task that runs scripts time it for the long tasks.
    /// </summary>
    internal long GetTimestamp()
    {
        var timestamp = TimeProvider.GetTimestamp();
        if (_taskScriptEvaluationWindows is not null)
        {
            ObserveTimestamp(timestamp);
        }

        return timestamp;
    }

    private void ObserveTimestamp(long timestamp)
    {
        if (!_taskTimed)
        {
            _taskTimed = true;
            _taskStartTimestamp = timestamp;
        }

        _taskEndTimestamp = timestamp;
    }

    /// <summary>
    /// Runs a task followed by a microtask checkpoint, then reports it when it is a long task (step "report long tasks" of
    /// https://html.spec.whatwg.org/multipage/webappapis.html#event-loop-processing-model).
    /// </summary>
    private void RunTaskAndMicrotasks(Action task)
    {
        using var scope = EnterTask();
        task();
        PerformMicrotaskCheckpoint();
    }

    /// <summary>
    /// Marks the code that runs until the scope is disposed as a task, whose scripts are timed to report the long tasks:
    /// a task of the event loop, or the work that the host does synchronously (e.g. the parsing of the document of
    /// <see cref="BrowsingContext.Navigate(string, Func{Url, DocumentResource?})"/>, which a browser does in the tasks of the
    /// networking task source). The tasks that run while a task spins the event loop belong to that task.
    /// </summary>
    internal TaskScope EnterTask()
    {
        _taskDepth++;
        return new TaskScope(this);
    }

    private void ExitTask()
    {
        _taskDepth--;
        if (_taskDepth > 0 || _taskScriptEvaluationWindows is not { } windows)
            return;

        if (ReferenceEquals(TimeProvider, TimeProvider.System))
        {
            ObserveTimestamp(TimeProvider.GetTimestamp());
        }

        _taskScriptEvaluationWindows = null;
        var timed = _taskTimed;
        _taskTimed = false;
        if (timed)
        {
            PerformanceLongTaskTiming.Report(TimeProvider, _taskStartTimestamp, _taskEndTimestamp, windows);
        }
    }

    internal readonly struct TaskScope(DomEventLoop eventLoop) : IDisposable
    {
        public void Dispose() => eventLoop.ExitTask();
    }
}
