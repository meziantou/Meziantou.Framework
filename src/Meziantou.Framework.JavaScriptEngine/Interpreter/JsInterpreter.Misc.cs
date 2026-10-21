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
        }
        else
        {
            obj = new JsObject(intrinsics.ObjectPrototype, ObjectClass.Arguments);
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            obj.SetOwnSlot(new PropertyKey((uint)i), arguments[i], PropertyFlags.Default);
        }

        obj.DefineBuiltIn("length", arguments.Length);
        obj.DefineBuiltIn(WellKnownSymbols.Iterator, intrinsics.ArrayPrototypeValues);

        // A function with a mapped arguments object (sloppy, simple parameters) exposes itself as callee
        if (mapped)
        {
            obj.DefineBuiltIn("callee", frame.Function is { } function ? new JsValue(function) : default);
        }
        else
        {
            obj.DefineBuiltInAccessor("callee", intrinsics.ThrowTypeError, intrinsics.ThrowTypeError, PropertyFlags.None);
        }

        return obj;
    }

    /// <summary>import.meta: the object of the module of the running code.</summary>
    private static JsValue GetImportMeta(Frame frame)
    {
        if (frame.Script is not JsSourceTextModule module)
            return ThrowHelper.ThrowSyntaxError<JsValue>("import.meta is only valid in modules");

        return Modules.ModuleOperations.GetImportMeta(module);
    }

    /// <summary>import(specifier, options) or import.defer(specifier, options): a promise of the namespace object of the module.</summary>
    private static JsValue ImportCall(Frame frame, JsValue specifier, JsValue options, bool isDefer = false)
    {
        return Modules.ModuleOperations.EvaluateImportCall(frame.Realm, GetActiveScriptOrModule(frame), specifier, options, isDefer ? Modules.ModulePhase.Defer : Modules.ModulePhase.Evaluation);
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
