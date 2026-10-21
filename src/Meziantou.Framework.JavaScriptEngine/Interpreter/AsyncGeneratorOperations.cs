namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The abstract operations of the async generators (https://tc39.es/ecma262/#sec-asyncgenerator-abstract-operations).</summary>
internal static class AsyncGeneratorOperations
{
    /// <summary>next (kind 0), throw (kind 1) and return (kind 2) of %AsyncGeneratorPrototype%.</summary>
    public static JsValue Enqueue(JsRealm realm, JsValue thisValue, int kind, JsValue value)
    {
        var capability = PromiseCapability.CreateIntrinsic(realm);
        if (thisValue.Cell is not JsAsyncGenerator generator)
        {
            capability.Reject(realm.CreateError(ErrorType.TypeError, "AsyncGenerator method called on an incompatible receiver"));
            return capability.Promise;
        }

        var state = generator.State;
        if (kind == 1 && state == GeneratorState.SuspendedStart)
        {
            generator.State = GeneratorState.Completed;
            state = GeneratorState.Completed;
        }

        if (state == GeneratorState.Completed)
        {
            if (kind == 0)
            {
                capability.Resolve(IteratorOperations.CreateIteratorResultObject(realm, default, done: true));
                return capability.Promise;
            }

            if (kind == 1)
            {
                capability.Reject(value);
                return capability.Promise;
            }
        }

        generator.Queue.Enqueue(new AsyncGeneratorRequest(kind, value, capability));
        if (kind == 2 && state is GeneratorState.SuspendedStart or GeneratorState.Completed)
        {
            generator.State = GeneratorState.AwaitingReturn;
            AwaitReturn(generator);
        }
        else if (state is GeneratorState.SuspendedStart or GeneratorState.SuspendedYield)
        {
            Resume(generator, generator.Queue.Peek());
        }

        return capability.Promise;
    }

    /// <summary>AsyncGeneratorResume: runs the frame with the completion of a request.</summary>
    private static void Resume(JsAsyncGenerator generator, AsyncGeneratorRequest request)
    {
        var wasStart = generator.State == GeneratorState.SuspendedStart;
        generator.State = GeneratorState.Executing;
        JsValue result;
        try
        {
            if (wasStart)
            {
                generator.Frame.State = FrameState.Running;
                result = JsInterpreter.RunGeneratorFrame(generator.Frame);
            }
            else
            {
                result = JsInterpreter.ResumeGenerator(generator.Frame, request.Value, request.Kind);
            }
        }
        catch (JsException ex)
        {
            Complete(generator, ex.Value, isThrow: true);
            return;
        }

        HandleFrameOutcome(generator, result);
    }

    /// <summary>The frame stopped: it awaits, yields or completed.</summary>
    private static void HandleFrameOutcome(JsAsyncGenerator generator, JsValue result)
    {
        var frame = generator.Frame;
        switch (frame.State)
        {
            case FrameState.SuspendedAwait:
                JsInterpreter.AwaitInFrame(frame, result, generator, static (state, value) => ResumeAfterAwait((JsAsyncGenerator)state!, value, isThrow: false), static (state, value) => ResumeAfterAwait((JsAsyncGenerator)state!, value, isThrow: true));
                return;
            case FrameState.SuspendedYield:
                Yield(generator, result);
                return;
            default:
                Complete(generator, result, isThrow: false);
                return;
        }
    }

    private static void ResumeAfterAwait(JsAsyncGenerator generator, JsValue value, bool isThrow)
    {
        JsValue result;
        try
        {
            result = JsInterpreter.Resume(generator.Frame, value, isThrow);
        }
        catch (JsException ex)
        {
            Complete(generator, ex.Value, isThrow: true);
            return;
        }

        HandleFrameOutcome(generator, result);
    }

    /// <summary>AsyncGeneratorYield: resolves the current request; the next request resumes the generator.</summary>
    private static void Yield(JsAsyncGenerator generator, JsValue value)
    {
        var realm = generator.Frame.Realm;
        CompleteStep(generator, value, isThrow: false, done: false, realm);
        if (generator.Queue.Count > 0)
        {
            generator.State = GeneratorState.SuspendedYield;
            Resume(generator, generator.Queue.Peek());
            return;
        }

        generator.State = GeneratorState.SuspendedYield;
    }

    /// <summary>The body completed (return or throw): the current request settles, then the queue drains.</summary>
    private static void Complete(JsAsyncGenerator generator, JsValue value, bool isThrow)
    {
        generator.State = GeneratorState.Completed;
        if (generator.Queue.Count > 0)
        {
            CompleteStep(generator, value, isThrow, done: true, generator.Frame.Realm);
        }

        DrainQueue(generator);
    }

    /// <summary>AsyncGeneratorCompleteStep: settles the promise of the first request.</summary>
    private static void CompleteStep(JsAsyncGenerator generator, JsValue value, bool isThrow, bool done, JsRealm realm)
    {
        var request = generator.Queue.Dequeue();
        if (isThrow)
        {
            request.Capability.Reject(value);
        }
        else
        {
            request.Capability.Resolve(IteratorOperations.CreateIteratorResultObject(realm, value, done));
        }
    }

    /// <summary>AsyncGeneratorDrainQueue: the requests made to a completed generator.</summary>
    private static void DrainQueue(JsAsyncGenerator generator)
    {
        while (generator.Queue.Count > 0)
        {
            var request = generator.Queue.Peek();
            if (request.Kind == 2)
            {
                generator.State = GeneratorState.AwaitingReturn;
                AwaitReturn(generator);
                return;
            }

            if (request.Kind == 1)
            {
                CompleteStep(generator, request.Value, isThrow: true, done: true, generator.Frame.Realm);
            }
            else
            {
                CompleteStep(generator, default, isThrow: false, done: true, generator.Frame.Realm);
            }
        }
    }

    /// <summary>AsyncGeneratorAwaitReturn: awaits the value of a return request.</summary>
    private static void AwaitReturn(JsAsyncGenerator generator)
    {
        var request = generator.Queue.Peek();
        var realm = generator.Frame.Realm;
        JsPromise promise;
        try
        {
            promise = JsPromise.PromiseResolve(realm, request.Value);
        }
        catch (JsException ex)
        {
            generator.State = GeneratorState.Completed;
            CompleteStep(generator, ex.Value, isThrow: true, done: true, realm);
            DrainQueue(generator);
            return;
        }

        JsPromise.PerformPromiseThen(
            promise,
            static (state, value) =>
            {
                var g = (JsAsyncGenerator)state!;
                g.State = GeneratorState.Completed;
                CompleteStep(g, value, isThrow: false, done: true, g.Frame.Realm);
                DrainQueue(g);
            },
            static (state, reason) =>
            {
                var g = (JsAsyncGenerator)state!;
                g.State = GeneratorState.Completed;
                CompleteStep(g, reason, isThrow: true, done: true, g.Frame.Realm);
                DrainQueue(g);
            },
            generator);
    }
}
