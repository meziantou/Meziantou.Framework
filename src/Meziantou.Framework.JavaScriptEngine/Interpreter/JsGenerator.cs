using System.Diagnostics;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal enum GeneratorState
{
    SuspendedStart,
    SuspendedYield,
    Executing,

    /// <summary>An async generator awaits the value of a return request while it is completed.</summary>
    AwaitingReturn,
    Completed,
}

/// <summary>A generator object (https://tc39.es/ecma262/#sec-generator-objects): its suspended frame.</summary>
internal sealed class JsGenerator(JsObject prototype, Frame frame) : JsObject(prototype, ObjectClass.Generator)
{
    public Frame Frame { get; } = frame;

    public GeneratorState State { get; set; } = GeneratorState.SuspendedStart;

    /// <summary>GeneratorResume / GeneratorResumeAbrupt: kind 0 next, 1 throw, 2 return.</summary>
    public JsValue Resume(JsValue value, int kind)
    {
        var realm = Frame.Realm;
        switch (State)
        {
            case GeneratorState.Executing:
                return ThrowHelper.ThrowTypeError<JsValue>("Generator is already running");
            case GeneratorState.Completed:
                if (kind == 1)
                    throw new JsException(value);

                return IteratorOperations.CreateIteratorResultObject(realm, kind == 2 ? value : default, done: true);
            case GeneratorState.SuspendedStart when kind != 0:
                State = GeneratorState.Completed;
                if (kind == 1)
                    throw new JsException(value);

                return IteratorOperations.CreateIteratorResultObject(realm, value, done: true);
        }

        var wasStart = State == GeneratorState.SuspendedStart;
        State = GeneratorState.Executing;
        JsValue result;
        try
        {
            if (wasStart)
            {
                Frame.State = FrameState.Running;
                result = JsInterpreter.RunGeneratorFrame(Frame);
            }
            else
            {
                result = JsInterpreter.ResumeGenerator(Frame, value, kind);
            }
        }
        catch (Exception ex) when (ex is JsException or JsTerminationException && SetCompleted())
        {
            throw new UnreachableException();
        }

        if (Frame.State == FrameState.SuspendedYield)
        {
            State = GeneratorState.SuspendedYield;
            return result;
        }

        State = GeneratorState.Completed;
        return IteratorOperations.CreateIteratorResultObject(realm, result, done: true);
    }

    /// <summary>
    /// An exception filter that completes the generator when its code throws, and does not catch the exception: a catch
    /// block that rethrows nests the exception dispatch in each .NET level of a deep recursion through generators, which
    /// overflows the stack.
    /// </summary>
    private bool SetCompleted()
    {
        State = GeneratorState.Completed;
        return false;
    }
}

/// <summary>An async generator object (https://tc39.es/ecma262/#sec-asyncgenerator-objects): its frame and its queue of requests.</summary>
internal sealed class JsAsyncGenerator(JsObject prototype, Frame frame) : JsObject(prototype, ObjectClass.AsyncGenerator)
{
    public Frame Frame { get; } = frame;

    public GeneratorState State { get; set; } = GeneratorState.SuspendedStart;

    /// <summary>[[AsyncGeneratorQueue]]</summary>
    public Queue<AsyncGeneratorRequest> Queue { get; } = new();
}

/// <summary>An AsyncGeneratorRequest: a call of next, throw or return and its promise.</summary>
internal sealed record AsyncGeneratorRequest(int Kind, JsValue Value, PromiseCapability Capability);
