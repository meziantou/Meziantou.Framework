---
title: Async code and promises
description: Run the promise jobs, wait for the result of an async function, let the scripts await .NET tasks, and report unhandled rejections.
---

## Run the jobs of the promises

The callbacks of the promises (`then`, the continuations of `await`...) are *jobs*: they wait in the job queue of the
engine until you call `engine.RunJobs()`, which runs them, and the jobs they enqueue, until the queue is empty. A
script or a call from .NET returns as soon as its synchronous part ends:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_AsyncFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var promise = realm.Execute("""
    async function compute() {
      const values = await Promise.all([1, 2, 3].map(async x => x * 10));
      return values.reduce((a, b) => a + b);
    }
    compute()
    """).AsObject();

// The script ran until its first await: the rest runs from the job queue
Assert.True(promise.TryGetPromiseState(out var state, out _, out _));
Assert.Equal(JsPromiseState.Pending, state);

engine.RunJobs();
Assert.True(promise.TryGetPromiseState(out state, out var result, out _));
Assert.Equal(JsPromiseState.Fulfilled, state);
Assert.Equal(60, result.AsNumber());
```
<!-- endref -->

- Call [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) after each script and each call from .NET that can start asynchronous work, as a browser runs its
  microtasks after each task.
- [TryGetPromiseState](cref:Meziantou.Framework.JavaScriptEngine.JsObject.TryGetPromiseState) reads the state of a promise ([Pending](cref:Meziantou.Framework.JavaScriptEngine.JsPromiseState.Pending), [Fulfilled](cref:Meziantou.Framework.JavaScriptEngine.JsPromiseState.Fulfilled) or [Rejected](cref:Meziantou.Framework.JavaScriptEngine.JsPromiseState.Rejected)), its result or its
  rejection reason, and whether a handler is attached. It returns false when the object is not a promise.
- A promise that stays [Pending](cref:Meziantou.Framework.JavaScriptEngine.JsPromiseState.Pending) after [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) waits for something outside of the engine, such as the .NET task
  below.
- `engine.EnqueueJob` adds a .NET action to the queue, after the jobs already in it.

When a job throws, the exception goes to [JsHost.ReportJobException](cref:Meziantou.Framework.JavaScriptEngine.JsHost.ReportJobException). The default implementation rethrows it: it comes
out of [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs), and the other jobs stay in the queue for the next call.

## Await a .NET task from a script

A .NET function returns a promise to the scripts, and settles it when its `Task` completes. The engine runs on one
thread at a time, so the host waits for the tasks itself, then settles the promises and runs the jobs, in its own
event loop:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_AwaitDotNetTasks;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var pending = new Queue<(Task<string> Task, JsValue Resolve, JsValue Reject)>();
using (engine.Enter())
{
    var promiseConstructor = realm.GlobalObject.GetProperty("Promise");
    realm.GlobalObject.SetProperty("readText", realm.CreateFunction("readText", 1, (_, arguments) =>
    {
        var task = ReadTextAsync(arguments.Length > 0 ? engine.ToString(arguments[0]) : "");

        // new Promise((resolve, reject) => ...): keep the resolving functions until the task completes
        var executor = realm.CreateFunction("executor", 2, (_, resolvingFunctions) =>
        {
            pending.Enqueue((task, resolvingFunctions[0], resolvingFunctions[1]));
            return JsValue.Undefined;
        });
        return engine.Construct(promiseConstructor, [executor]);
    }));
}

var promise = realm.Execute("""
    (async () => {
      const text = await readText('a.txt');
      try {
        await readText('missing.txt');
      } catch (e) {
        return `${text} | ${e.message}`;
      }
    })()
    """).AsObject();

// The event loop of the host: wait for the .NET work, settle its promise, then run the jobs it unblocks
while (pending.TryDequeue(out var item))
{
    try
    {
        var text = await item.Task;
        engine.Call(item.Resolve, JsValue.Undefined, text);
    }
    catch (FileNotFoundException exception)
    {
        using (engine.Enter())
        {
            engine.Call(item.Reject, JsValue.Undefined, realm.CreateError(JsErrorType.Error, exception.Message));
        }
    }

    engine.RunJobs();
}

Assert.True(promise.TryGetPromiseState(out var state, out var result, out _));
Assert.Equal(JsPromiseState.Fulfilled, state);
Assert.Equal("content of a.txt | missing.txt not found", result.AsString());

static async Task<string> ReadTextAsync(string name)
{
    await Task.Yield();
    return name is "missing.txt" ? throw new FileNotFoundException(name + " not found") : "content of " + name;
}
```
<!-- endref -->

- `engine.Construct` of the `Promise` constructor of the realm, with a .NET executor, gives the resolving functions of
  the promise.
- The tasks complete on other threads, but the engine is called only from the loop: [Call](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Call) resolves or rejects the
  promise, and [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) runs the code that waited for it.
- Reject a promise with an error object (`realm.CreateError`), so that the script gets a `message` and a `stack`.

The engine has no timers: to provide `setTimeout`, keep the callbacks and their due times in your event loop, and call
them with `engine.Call` when they are due, followed by [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs).

## Report unhandled rejections

A [JsHost](cref:Meziantou.Framework.JavaScriptEngine.JsHost) receives the events of the engine that the specification leaves to the host. [PromiseRejectionTracker](cref:Meziantou.Framework.JavaScriptEngine.JsHost.PromiseRejectionTracker) is
called when a promise is rejected without a handler, and when a handler is attached to it later:

<!-- ref:../../samples/Meziantou.Framework.JavaScriptEngine.Samples/RejectionTrackingHost.cs;region=JavaScriptEngine_RejectionTrackingHost;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Records the promises that are rejected while no handler is attached to them.</summary>
public sealed class RejectionTrackingHost : JsHost
{
    private readonly HashSet<JsObject> _unhandledRejections = [];

    public IReadOnlyCollection<JsObject> UnhandledRejections => _unhandledRejections;

    public override void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation)
    {
        // Reject: the promise is rejected and has no handler. Handle: a handler was attached later.
        if (operation is JsPromiseRejectionOperation.Reject)
        {
            _unhandledRejections.Add(promise);
        }
        else
        {
            _unhandledRejections.Remove(promise);
        }
    }
}
```
<!-- endref -->

Set the host in the options of the engine, and check the remaining promises once the jobs have run:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_UnhandledRejections;format=md-fenced-code-block;dedent=true -->
```csharp
var host = new RejectionTrackingHost();
using var engine = new JsEngine(new JsEngineOptions { Host = host });
var realm = engine.CreateRealm();
realm.Execute("""
    Promise.reject(new Error('handled')).catch(() => {});
    Promise.reject(new Error('forgotten'));
    """);
engine.RunJobs();

// Once the jobs have run, report the promises that are still rejected without a handler
var promise = Assert.Single(host.UnhandledRejections);
Assert.True(promise.TryGetPromiseState(out _, out var reason, out _));
Assert.Equal("Error: forgotten", engine.ToString(reason));
```
<!-- endref -->

## Integrate with an event loop

A host that has its own event loop, such as a browser, overrides more methods of [JsHost](cref:Meziantou.Framework.JavaScriptEngine.JsHost):

- [EnqueueGenericJob](cref:Meziantou.Framework.JavaScriptEngine.JsHost.EnqueueGenericJob) and [EnqueueTimeoutJob](cref:Meziantou.Framework.JavaScriptEngine.JsHost.EnqueueTimeoutJob) receive the jobs that the engine does not put in the promise queue: the
  cleanup callbacks of `FinalizationRegistry`, the results and the timeouts of `Atomics.waitAsync`.
  [EnqueueGenericJob](cref:Meziantou.Framework.JavaScriptEngine.JsHost.EnqueueGenericJob) can be called from any thread. The host runs each job later with [JsHostJob.Run()](cref:Meziantou.Framework.JavaScriptEngine.JsHostJob.Run), on the
  thread of the engine. By default, the engine keeps them and [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) runs them, the timeout jobs once their delay
  has elapsed on the [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider) of the options.
- [MakeJobCallback](cref:Meziantou.Framework.JavaScriptEngine.JsHost.MakeJobCallback) and [CallJobCallback](cref:Meziantou.Framework.JavaScriptEngine.JsHost.CallJobCallback) capture a state of the host when a callback is registered (by `then`...) and
  restore it when the callback runs.
- `engine.EnterNestedJobQueue()` sends the new jobs to a separate queue until the returned scope is disposed, for a
  nested event loop.
