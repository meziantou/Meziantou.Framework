using System.Numerics;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

// The embedding API of the engine: calls, conversions, realms, jobs and the state of the execution context stack.
public sealed partial class JsEngine
{
    /// <summary>Runs <paramref name="action"/> with <paramref name="realm"/> as the current realm (the realm of the objects created by the built-ins and the API).</summary>
    public T RunInRealm<T>(JsRealm realm, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(action);
        CheckRealmOwner(realm);
        using var scope = Enter();
        var previous = CurrentRealm;
        CurrentRealm = realm;
        try
        {
            return action();
        }
        finally
        {
            CurrentRealm = previous;
        }
    }

    /// <summary>Call(F, V, argumentsList) (https://tc39.es/ecma262/#sec-call).</summary>
    /// <exception cref="JsException">The value is not callable (a TypeError), or the function threw an exception.</exception>
    /// <exception cref="InvalidOperationException">The function, the this value or an argument belongs to another engine.</exception>
    public JsValue Call(JsValue function, JsValue thisValue, params ReadOnlySpan<JsValue> arguments)
    {
        CheckValuesOwner(function, thisValue, arguments);
        using var entry = EnterHostEntry();
        return JsOperations.Call(function, thisValue, arguments);
    }

    /// <summary>Construct(F, argumentsList, newTarget) (https://tc39.es/ecma262/#sec-construct); <paramref name="newTarget"/> is the constructor when it is undefined.</summary>
    /// <exception cref="JsException">The value is not a constructor (a TypeError), or the constructor threw an exception.</exception>
    /// <exception cref="InvalidOperationException">The constructor, the new target or an argument belongs to another engine.</exception>
    public JsValue Construct(JsValue constructor, ReadOnlySpan<JsValue> arguments, JsValue newTarget = default)
    {
        CheckValuesOwner(constructor, newTarget, arguments);
        using var entry = EnterHostEntry();
        if (constructor.TryGetObject() is not { IsConstructor: true } constructorObject)
            return ThrowHelper.ThrowTypeError<JsValue>(JsOperations.DescribeForError(constructor) + " is not a constructor");

        JsObject? target = null;
        if (!newTarget.IsUndefined)
        {
            if (newTarget.TryGetObject() is not { IsConstructor: true } newTargetObject)
                return ThrowHelper.ThrowTypeError<JsValue>(JsOperations.DescribeForError(newTarget) + " is not a constructor");

            target = newTargetObject;
        }

        return JsOperations.Construct(constructorObject, arguments, target);
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> when one of the values is an object of another engine.</summary>
    private void CheckValuesOwner(JsValue first, JsValue second, ReadOnlySpan<JsValue> others)
    {
        JsObject.CheckValueOwner(this, first);
        JsObject.CheckValueOwner(this, second);
        foreach (var value in others)
        {
            JsObject.CheckValueOwner(this, value);
        }
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> when the realm belongs to another engine.</summary>
    internal void CheckRealmOwner(JsRealm realm)
    {
        if (!ReferenceEquals(realm.Engine, this))
            throw new InvalidOperationException("The realm belongs to another engine");
    }

    /// <summary>ToNumber(value) (https://tc39.es/ecma262/#sec-tonumber); it may run code (valueOf).</summary>
    public double ToNumber(JsValue value)
    {
        using var entry = EnterHostEntry();
        return JsOperations.ToNumber(value);
    }

    /// <summary>ToString(value) (https://tc39.es/ecma262/#sec-tostring); it may run code, and throws a TypeError for a symbol.</summary>
    public string ToString(JsValue value)
    {
        using var entry = EnterHostEntry();
        return JsOperations.ToString(value);
    }

    /// <summary>ToBoolean(value) (https://tc39.es/ecma262/#sec-toboolean).</summary>
    public static bool ToBoolean(JsValue value) => JsOperations.ToBoolean(value);

    /// <summary>ToBigInt(value) (https://tc39.es/ecma262/#sec-tobigint).</summary>
    public BigInteger ToBigInt(JsValue value)
    {
        using var entry = EnterHostEntry();
        return BigIntOperations.ToBigInt(value);
    }

    /// <summary>ToObject(value) (https://tc39.es/ecma262/#sec-toobject): a TypeError for undefined and null.</summary>
    public JsObject ToObject(JsValue value)
    {
        using var entry = EnterHostEntry();
        return JsOperations.ToObject(value);
    }

    /// <summary>ToPropertyKey(value) (https://tc39.es/ecma262/#sec-topropertykey); it may run code.</summary>
    public JsPropertyKey ToPropertyKey(JsValue value)
    {
        using var entry = EnterHostEntry();
        return new JsPropertyKey(JsOperations.ToPropertyKey(value));
    }

    /// <summary>SameValue(x, y) (https://tc39.es/ecma262/#sec-samevalue).</summary>
    public static bool SameValue(JsValue x, JsValue y) => JsOperations.SameValue(x, y);

    /// <summary>IsArray(value) (https://tc39.es/ecma262/#sec-isarray): true for arrays and proxies of arrays; throws for a revoked proxy.</summary>
    public bool IsArray(JsValue value)
    {
        using var entry = EnterHostEntry();
        return JsOperations.IsArray(value);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Jobs
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Enqueues a job in the promise job queue (a microtask): it runs from <see cref="RunJobs"/>, after the jobs already in the queue.</summary>
    /// <param name="job">The job.</param>
    /// <param name="realm">The realm that is current while the job runs, or null to keep the current realm.</param>
    public void EnqueueJob(Action job, JsRealm? realm = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        EnqueuePromiseJob(static (state, _) => ((Action)state!)(), job, default, realm);
    }

    /// <summary>
    /// Makes the jobs enqueued from now on go to a new queue, which <see cref="RunJobs"/> runs, until the returned scope is
    /// disposed (e.g. the microtasks of a nested event loop). The jobs left in the nested queue then go to the outer queue.
    /// </summary>
    public IDisposable EnterNestedJobQueue() => EnterNestedJobQueueCore();

    // ------------------------------------------------------------------------------------------------------------------
    // Execution context stack
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the position of the running JavaScript code (the call of the running .NET callback), or null when no script runs.</summary>
    public JsSourceLocation? GetCurrentLocation()
    {
        if (CurrentFrame is not { } frame)
            return null;

        // The running frame is at the start of its current instruction (e.g. the call of the .NET callback), as the top
        // frame of the stack traces
        var offset = frame.Code.GetSourceOffset(frame.Pc);
        var (line, column) = frame.Code.Source.GetLineAndColumn(Math.Max(0, offset));
        return new JsSourceLocation(frame.Code.Source.Url, line, column);
    }

    /// <summary>GetActiveScriptOrModule(): the script or the module of the innermost running code that has one, or null.</summary>
    public JsScriptOrModule? GetActiveScriptOrModule() => JsInterpreter.GetActiveScriptOrModule(CurrentFrame) as JsScriptOrModule;

    /// <summary>Captures the stack trace of the running code, in the format of the <c>stack</c> property of the errors (without the first line).</summary>
    public string CaptureStackTrace()
    {
        var error = new JsErrorObject(prototype: null);
        error.CaptureStackTrace(this);
        var stack = error.StackTrace ?? "";
        var newLine = stack.IndexOf('\n', StringComparison.Ordinal);
        return newLine < 0 ? "" : stack[(newLine + 1)..];
    }
}
