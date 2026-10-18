namespace Meziantou.Framework.WebPlatform.Scripting;

// The conversion of the JavaScript values to the IDL promise types (https://webidl.spec.whatwg.org/#js-promise) for the .NET
// members that take a Task: the task is completed when the promise is settled (in a microtask of the event loop).
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private TValue? _promiseResolve;
    private TValue? _promiseThen;

    /// <summary>Converts a value to a promise (https://webidl.spec.whatwg.org/#js-promise), returned as a task completed when the promise is settled.</summary>
    internal Task ToTask(TValue value) => ToTask<object?>(value, static (_, _) => null);

    /// <summary>
    /// Converts a value to a promise (https://webidl.spec.whatwg.org/#js-promise), returned as a task completed with the
    /// fulfillment value converted by <paramref name="convert"/> when the promise is settled. When the promise is rejected,
    /// or the conversion throws, the task fails with the exception that throws the reason in the engine.
    /// </summary>
    internal Task<T> ToTask<T>(TValue value, Func<WebIdlRealm<TAdapter, TValue>, TValue, T> convert)
    {
        // PromiseResolve(%Promise%, V), then PerformPromiseThen with the reactions that complete the task
        _promiseResolve ??= EvaluateBuiltIn("Promise.resolve.bind(Promise)");
        _promiseThen ??= EvaluateBuiltIn("Promise.prototype.then");
        var promise = Adapter.Call(_promiseResolve, Adapter.Undefined, [value]);

        var completion = new TaskCompletionSource<T>();
        var onFulfilled = Adapter.CreateFunction("", 1, (_, arguments) =>
        {
            try
            {
                completion.TrySetResult(convert(this, arguments.Length > 0 ? arguments[0] : Adapter.Undefined));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }

            return Adapter.Undefined;
        });
        var onRejected = Adapter.CreateFunction("", 1, (_, arguments) =>
        {
            completion.TrySetException(Adapter.CreateException(arguments.Length > 0 ? arguments[0] : Adapter.Undefined));
            return Adapter.Undefined;
        });
        Adapter.Call(_promiseThen, promise, [onFulfilled, onRejected]);
        return completion.Task;
    }
}
