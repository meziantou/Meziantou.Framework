using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal static partial class JsInterpreter
{
    /// <summary>CreateUnmappedArgumentsObject / CreateMappedArgumentsObject.</summary>
    private static JsValue CreateArgumentsObject(Frame frame, bool mapped)
    {
        var realm = frame.Realm;
        var intrinsics = realm.Intrinsics;
        var arguments = frame.Arguments;
        JsObject obj;
        if (mapped && frame.Code.ParameterNames is { Length: > 0 })
        {
            // The indices of the parameters alias the slots of the parameters in the environment of the function
            var parameterNames = frame.Code.ParameterNames;
            var map = new int[Math.Min(arguments.Length, parameterNames.Length)];
            map.AsSpan().Fill(-1);
            var environment = (DeclarativeEnvironment)frame.Environment;
            var mappedNames = new HashSet<string>(StringComparer.Ordinal);
            for (var index = parameterNames.Length - 1; index >= 0; index--)
            {
                var name = parameterNames[index];
                if (mappedNames.Add(name) && index < map.Length)
                {
                    map[index] = environment.Info.IndexOf(name);
                }
            }

            obj = new JsArgumentsObject(intrinsics.ObjectPrototype, environment, map);
            InitializeArgumentsObject(obj, frame, arguments, realm.Engine.ArgumentsShapes.MappedCallee, exposeCallee: true);
            return obj;
        }

        obj = new JsObject(intrinsics.ObjectPrototype, ObjectClass.Arguments);
        var shapes = realm.Engine.ArgumentsShapes;
        InitializeArgumentsObject(obj, frame, arguments, mapped ? shapes.Callee : shapes.ThrowingCallee, exposeCallee: mapped);
        return obj;
    }

    /// <summary>
    /// A new arguments object of a function whose arguments object is only read (FunctionInfo.HasSimpleArguments): it is
    /// not observable, and the parameters do not change, so the indices need not be mapped (callee is the function when
    /// the object would be mapped).
    /// </summary>
    private static JsValue CreateArgumentsForRead(Frame frame, bool mapped) => mapped ? CreateArgumentsCopy(frame) : CreateArgumentsObject(frame, mapped: false);

    /// <summary>arguments[key] that is not an index of an argument, in a function whose arguments object is not created.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue GetArgumentsProperty(Frame frame, JsValue key, bool mapped) => GetElement(frame.Realm, CreateArgumentsForRead(frame, mapped), key);

    /// <summary>The value of the arguments own property of a sloppy function: a new arguments object of a running call (its indices are not mapped).</summary>
    internal static JsValue CreateArgumentsCopy(Frame frame)
    {
        var obj = new JsObject(frame.Realm.Intrinsics.ObjectPrototype, ObjectClass.Arguments);
        InitializeArgumentsObject(obj, frame, frame.Arguments, frame.Realm.Engine.ArgumentsShapes.Callee, exposeCallee: true);
        return obj;
    }

    /// <summary>
    /// Gives its properties to a new arguments object: the arguments as its elements, then length, @@iterator and callee
    /// (in their final shape at once: the same shape as the one their definitions one by one give).
    /// </summary>
    private static void InitializeArgumentsObject(JsObject obj, Frame frame, ReadOnlySpan<JsValue> arguments, Shape shape, bool exposeCallee)
    {
        // The arguments are never holes: they are the dense elements an index-by-index definition gives
        obj.InitializeDenseElements(arguments);

        // A function with a mapped arguments object (sloppy, simple parameters) exposes itself as callee
        var intrinsics = frame.Realm.Intrinsics;
        var callee = exposeCallee
            ? frame.Function is { } function ? new JsValue(function) : default
            : new JsValue(new AccessorPair(new JsValue(intrinsics.ThrowTypeError), new JsValue(intrinsics.ThrowTypeError)));
        obj.InitializeProperties(shape, [arguments.Length, new JsValue(intrinsics.ArrayPrototypeValues), callee]);
    }

    /// <summary>import.meta: the object of the module of the running code.</summary>
    private static JsValue GetImportMeta(Frame frame)
    {
        if (frame.Script is not JsSourceTextModule module)
            return ThrowHelper.ThrowSyntaxError<JsValue>("import.meta is only valid in modules");

        return Modules.ModuleOperations.GetImportMeta(module);
    }

    /// <summary>import(specifier, options), import.defer(...) or import.source(...): a promise of the namespace object (or of the source) of the module.</summary>
    private static JsValue ImportCall(Frame frame, JsValue specifier, JsValue options, Modules.ModulePhase phase = Modules.ModulePhase.Evaluation)
    {
        return Modules.ModuleOperations.EvaluateImportCall(frame.Realm, GetActiveScriptOrModule(frame), specifier, options, phase);
    }

    /// <summary>GetActiveScriptOrModule(): the script or the module of the innermost frame that has one.</summary>
    internal static IScriptOrModule? GetActiveScriptOrModule(Frame? frame)
    {
        if (frame?.Script is { } script)
            return script;

        return (frame?.Realm.Engine ?? JsEngine.Current)?.FindActiveScriptOrModule();
    }

    /// <summary>Runs a generator frame suspended at its start.</summary>
    internal static JsValue RunGeneratorFrame(Frame frame) => RunWithDepth(frame.Realm.Engine, frame);

    /// <summary>Await in the frame of an async generator: the continuations resume it.</summary>
    internal static void AwaitInFrame(Frame frame, JsValue value, object state, PromiseContinuation onFulfilled, PromiseContinuation onRejected) => AwaitValue(frame, value, state, onFulfilled, onRejected);
}

/// <summary>A DisposeCapability stored in a register.</summary>
internal sealed class DisposeCapabilityCell(DisposeCapability capability) : JsCell(JsValueKind.Undefined)
{
    public DisposeCapability Capability { get; } = capability;
}

/// <summary>The disposal of the resources of an await using scope, stored in a register.</summary>
internal sealed class AsyncDisposeOperationCell(AsyncDisposeResourcesOperation operation) : JsCell(JsValueKind.Undefined)
{
    public AsyncDisposeResourcesOperation Operation { get; } = operation;
}
