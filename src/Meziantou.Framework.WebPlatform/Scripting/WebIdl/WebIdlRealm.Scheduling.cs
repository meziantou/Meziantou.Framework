using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// Prioritized Task Scheduling (https://wicg.github.io/scheduling-apis/#sec-patches-html-hostmakejobcallback): the promise
// reactions inherit the continuation state of the code that creates them (HostMakeJobCallback), which is restored while
// they run (HostCallJobCallback), so that scheduler.yield() inherits the priority and the signal of the task that the
// reaction descends from.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>
    /// The script engines have no HostMakeJobCallback hook: the reactions that the scripts create with <c>then()</c> (and
    /// with the methods that call it: <c>catch()</c>, <c>finally()</c>, <c>Promise.all()</c>...) are wrapped by a
    /// <c>Promise.prototype.then</c> built-in function that captures the continuation state of the event loop. The reactions
    /// of <c>await</c> are created by the engine: they run with the state of the task that runs them (see
    /// <see cref="DomEventLoop.CurrentContinuationState"/>). Nothing is wrapped until a scheduler is used in the event loop.
    /// </summary>
    private void DefineContinuationStatePropagation()
    {
        var eventLoop = GlobalScope.EventLoop;
        var promisePrototype = EvaluateBuiltIn("Promise.prototype");
        var originalThen = Adapter.Get(promisePrototype, Key("then"));

        // The reactions of the implementation have no scheduling state
        _promiseThen = originalThen;
        var then = Adapter.RunInRealm(EngineRealm, () => Adapter.CreateFunction("then", 2, (thisValue, arguments) =>
        {
            if (!eventLoop.PropagatesContinuationState)
                return Adapter.Call(originalThen, thisValue, arguments);

            var continuationState = eventLoop.CurrentContinuationState;
            var onFulfilled = arguments.Length > 0 ? WrapReaction(eventLoop, arguments[0], continuationState) : Adapter.Undefined;
            var onRejected = arguments.Length > 1 ? WrapReaction(eventLoop, arguments[1], continuationState) : Adapter.Undefined;
            return Adapter.Call(originalThen, thisValue, [onFulfilled, onRejected]);
        }));
        Adapter.DefineOwnProperty(promisePrototype, Key("then"), JsPropertyDescriptor<TValue>.Data(then, writable: true, enumerable: false, configurable: true));
    }

    private TValue WrapReaction(DomEventLoop eventLoop, TValue handler, SchedulerContinuationState? continuationState)
    {
        // "If IsCallable(onFulfilled) is false, then let onFulfilledJobCallback be empty"
        if (!Adapter.IsCallable(handler))
            return handler;

        var adapter = Adapter;
        return adapter.CreateFunction("", 1, (_, arguments) =>
        {
            var previousContinuationState = eventLoop.CurrentContinuationState;
            eventLoop.CurrentContinuationState = continuationState;
            try
            {
                return adapter.Call(handler, adapter.Undefined, arguments);
            }
            finally
            {
                eventLoop.CurrentContinuationState = previousContinuationState;
            }
        });
    }
}
