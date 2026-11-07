namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal static partial class JsInterpreter
{
    /// <summary>The call of a generator, an async function or an async generator: the frame runs separately from the caller.</summary>
    private static JsValue StartGeneratorOrAsyncFunction(JsClosure closure, JsValue thisArgument, ReadOnlySpan<JsValue> arguments)
    {
        var template = closure.Template;
        var engine = closure.Realm.Engine;
        var frame = CreateCallFrame(closure, thisArgument, arguments, newTarget: null, pooled: false);
        if (template.IsGenerator && !template.IsAsync)
        {
            // The prologue (parameters) runs now; GeneratorStart suspends the frame
            RunWithDepth(engine, frame);
            var prototype = ObjectOperations.GetPrototypeFromConstructor(closure, static intrinsics => intrinsics.GeneratorPrototype);
            var generator = new JsGenerator(prototype, frame);
            frame.Owner = generator;
            return generator;
        }

        if (template.IsGenerator)
        {
            // An async generator: an exception of the prologue is thrown by the call
            RunWithDepth(engine, frame);
            var prototype = ObjectOperations.GetPrototypeFromConstructor(closure, static intrinsics => intrinsics.AsyncGeneratorPrototype);
            var generator = new JsAsyncGenerator(prototype, frame);
            frame.Owner = generator;
            return generator;
        }

        // An async function: the result is a promise, rejected when the body (or the prologue) throws
        var capability = PromiseCapability.CreateIntrinsic(closure.Realm);
        var driver = new AsyncFunctionDriver(frame, capability);
        frame.Owner = driver;
        AsyncFunctionStep(driver, default, isThrow: false, isStart: true);
        return capability.Promise;
    }

    private static JsValue RunWithDepth(JsEngine engine, Frame frame)
    {
        engine.CheckCallDepth();
        engine.CallDepth++;
        try
        {
            return Run(frame);
        }
        finally
        {
            engine.CallDepth--;
        }
    }

    /// <summary>Resumes a suspended frame: with a value (next), or by throwing an exception at the suspension point.</summary>
    internal static JsValue Resume(Frame frame, JsValue value, bool isThrow)
    {
        frame.State = FrameState.Running;
        if (isThrow)
        {
            frame.PendingThrow = true;
            frame.ResumeValue = value;
        }
        else
        {
            frame.Values[frame.Sp++] = value;
        }

        return RunWithDepth(frame.Realm.Engine, frame);
    }

    /// <summary>Resumes a frame suspended by a yield: the received value and the kind of the resumption (0 next, 1 throw, 2 return) are pushed.</summary>
    internal static JsValue ResumeGenerator(Frame frame, JsValue value, int kind)
    {
        frame.State = FrameState.Running;
        frame.Values[frame.Sp++] = value;
        frame.Values[frame.Sp++] = kind;
        return RunWithDepth(frame.Realm.Engine, frame);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Async functions
    // ------------------------------------------------------------------------------------------------------------------

    internal sealed class AsyncFunctionDriver(Frame frame, PromiseCapability capability)
    {
        public Frame Frame { get; } = frame;

        public PromiseCapability Capability { get; } = capability;

        /// <summary>Gets the module whose body runs, for a module with a top-level await.</summary>
        public JsSourceTextModule? Module { get; init; }
    }

    /// <summary>AsyncBlockStart: runs a frame as the body of an async function (the body of a module with a top-level await).</summary>
    internal static void StartAsyncBlock(Frame frame, PromiseCapability capability, JsSourceTextModule? module = null)
    {
        var driver = new AsyncFunctionDriver(frame, capability) { Module = module };
        frame.Owner = driver;
        AsyncFunctionStep(driver, default, isThrow: false, isStart: true);
    }

    private static void AsyncFunctionStep(AsyncFunctionDriver driver, JsValue value, bool isThrow, bool isStart = false)
    {
        var frame = driver.Frame;
        JsValue result;
        try
        {
            result = isStart ? RunWithDepth(frame.Realm.Engine, frame) : Resume(frame, value, isThrow);
        }
        catch (JsException ex)
        {
            driver.Capability.Reject(ex.Value);
            return;
        }
        catch (Exception ex)
        {
            // A termination or a .NET exception: the function cannot complete, its promise is rejected
            var error = CreateAbortError(frame.Realm, ex);
            if (driver.Module is { } module)
            {
                Modules.ModuleOperations.AsyncModuleExecutionRejected(module, error);
            }

            driver.Capability.Reject(error);
            throw;
        }

        if (frame.State == FrameState.SuspendedAwait)
        {
            AwaitValue(frame, result, driver, static (state, v) => AsyncFunctionStep((AsyncFunctionDriver)state!, v, isThrow: false), static (state, v) => AsyncFunctionStep((AsyncFunctionDriver)state!, v, isThrow: true));
            return;
        }

        driver.Capability.Resolve(result);
    }

    /// <summary>
    /// The error that settles the promise of an async function, an async generator or a module whose code is stopped by an
    /// exception that is not a JavaScript exception (a <see cref="JsTerminationException"/> or a .NET exception): the
    /// exception goes to the host, the code that waits for the promise sees this error.
    /// </summary>
    internal static JsValue CreateAbortError(JsRealm realm, Exception exception)
    {
        var message = exception is JsTerminationException
            ? "The execution was terminated: " + exception.Message
            : "The execution was stopped by a .NET exception: " + exception.GetType().FullName + ": " + exception.Message;
        return realm.CreateError(ErrorType.Error, message);
    }

    /// <summary>Await(value): the continuations run in a promise job when the promise settles.</summary>
    private static void AwaitValue(Frame frame, JsValue value, object state, PromiseContinuation onFulfilled, PromiseContinuation onRejected)
    {
        JsPromise promise;
        try
        {
            promise = JsPromise.PromiseResolve(frame.Realm, value);
        }
        catch (JsException ex)
        {
            // PromiseResolve threw (e.g. a getter of constructor): the exception is thrown at the await
            onRejected(state, ex.Value);
            return;
        }

        // The continuations are the closures of Await, created in the realm of the frame: the job runs in this realm (e.g.
        // the HostPromiseRejectionTracker of the promise of the async function uses it), not in the one that settles the promise
        JsPromise.PerformPromiseThen(promise, onFulfilled, onRejected, state, frame.Realm);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // yield*
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>One step of yield*: calls next, throw or return of the inner iterator; a hole when a return finds no return method.</summary>
    private static JsValue DelegateStep(IteratorRecord record, JsValue received, int kind, bool isAsync)
    {
        var iterator = record.Iterator;
        JsValue result;
        switch (kind)
        {
            case 0:
                result = JsOperations.Call(record.NextMethod, iterator, [received]);
                break;
            case 1:
                {
                    var throwMethod = JsOperations.GetMethod(iterator, new PropertyKey("throw"));
                    if (throwMethod is null)
                    {
                        // The iterator does not handle exceptions: close it, then throw a TypeError
                        if (isAsync)
                        {
                            var returnMethod = JsOperations.GetMethod(iterator, new PropertyKey("return"));
                            returnMethod?.Call(iterator, []);
                        }
                        else
                        {
                            IteratorOperations.IteratorClose(record);
                        }

                        return ThrowHelper.ThrowTypeError<JsValue>("The iterator does not provide a 'throw' method");
                    }

                    result = throwMethod.Call(iterator, [received]);
                    break;
                }

            default:
                {
                    var returnMethod = JsOperations.GetMethod(iterator, new PropertyKey("return"));
                    if (returnMethod is null)
                        return JsValue.Hole;

                    result = returnMethod.Call(iterator, [received]);
                    break;
                }
        }

        if (!isAsync && !result.IsObject)
            return ThrowHelper.ThrowTypeError<JsValue>("Iterator result " + JsOperations.DescribeForError(result) + " is not an object");

        return result;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Async iteration
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>GetIterator(obj, async): the @@asyncIterator method, or an async-from-sync iterator around the @@iterator method.</summary>
    private static IteratorRecord GetAsyncIterator(JsRealm realm, JsValue value)
    {
        var method = JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.AsyncIterator));
        if (method is null)
        {
            var syncMethod = JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.Iterator));
            if (syncMethod is null)
                return ThrowHelper.ThrowTypeError<IteratorRecord>($"{JsOperations.DescribeForError(value)} is not async iterable");

            var syncRecord = IteratorOperations.GetIteratorFromMethod(value, syncMethod);
            return AsyncFromSyncIterator.Create(realm, syncRecord);
        }

        return IteratorOperations.GetIteratorFromMethod(value, method);
    }
}
