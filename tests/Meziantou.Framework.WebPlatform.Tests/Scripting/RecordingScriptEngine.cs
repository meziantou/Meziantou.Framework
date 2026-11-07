using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Scripting;

/// <summary>
/// A script engine that records the calls of the library. It does not run JavaScript: a script runs the .NET behavior
/// registered for its source text, and an event handler content attribute runs the behavior registered for its body.
/// </summary>
internal sealed class RecordingScriptEngine : ScriptEngine
{
    private readonly Queue<Action> _jobs = new();
    private int _depth;

    public List<string> Calls { get; } = [];

    public List<ClassicScript> Scripts { get; } = [];

    /// <summary>The behaviors of the scripts, by source text.</summary>
    public Dictionary<string, Func<ClassicScript, object?>> Behaviors { get; } = new(StringComparer.Ordinal);

    /// <summary>The behaviors of the event handler content attributes, by body. The result is the return value of the handler.</summary>
    public Dictionary<string, Func<EventTarget, object?[], object?>> HandlerBehaviors { get; } = new(StringComparer.Ordinal);

    /// <summary>Queues a job of the engine (e.g. a promise reaction).</summary>
    public void QueueJob(Action job) => _jobs.Enqueue(job);

    /// <summary>Runs code as if a script was running.</summary>
    public void RunScript(Action action)
    {
        _depth++;
        try
        {
            action();
        }
        finally
        {
            _depth--;
        }
    }

    protected internal override void CreateRealm(GlobalScope global) => Calls.Add("CreateRealm " + global.ScriptUrl);

    protected internal override void OnDocumentActivated(Document document) => Calls.Add("DocumentActivated " + document.Url);

    protected internal override void DestroyRealm(GlobalScope global) => Calls.Add("DestroyRealm " + global.ScriptUrl);

    protected internal override void OnChildNavigablesChanged(Window window) => Calls.Add("ChildNavigablesChanged " + window.Length);

    protected internal override object? ExecuteClassicScript(ClassicScript script)
    {
        Calls.Add("Execute " + script.SourceText);
        Scripts.Add(script);
        _depth++;
        try
        {
            return Behaviors.TryGetValue(script.SourceText, out var behavior) ? behavior(script) : null;
        }
        finally
        {
            _depth--;
        }
    }

    protected internal override EventHandlerCallback CompileEventHandler(EventHandlerSource source)
    {
        Calls.Add("Compile " + source.Name + " " + source.Body);
        if (!HandlerBehaviors.TryGetValue(source.Body, out var behavior))
            throw new InvalidOperationException("SyntaxError: " + source.Body);

        return new Callback(this, behavior);
    }

    protected internal override void QueueMicrotask(Action microtask) => _jobs.Enqueue(microtask);

    protected internal override void PerformMicrotaskCheckpoint()
    {
        Calls.Add("Checkpoint");
        _depth++;
        try
        {
            while (_jobs.TryDequeue(out var job))
            {
                job();
            }
        }
        finally
        {
            _depth--;
        }
    }

    protected internal override bool IsScriptRunning => _depth > 0;

    private sealed class Callback(RecordingScriptEngine engine, Func<EventTarget, object?[], object?> behavior) : EventHandlerCallback
    {
        protected internal override object? Invoke(EventTarget thisValue, object?[] arguments)
        {
            engine._depth++;
            try
            {
                return behavior(thisValue, arguments);
            }
            finally
            {
                engine._depth--;
            }
        }
    }
}

/// <summary>A loader that returns the registered resources synchronously and records the requests.</summary>
internal sealed class RecordingLoader : DocumentLoader
{
    public List<DocumentRequest> Requests { get; } = [];

    public Dictionary<string, DocumentResource> Resources { get; } = new(StringComparer.Ordinal);

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        // The workers load their scripts from their own threads
        lock (Requests)
        {
            Requests.Add(request);
            return ValueTask.FromResult(Resources.GetValueOrDefault(request.Url));
        }
    }
}
