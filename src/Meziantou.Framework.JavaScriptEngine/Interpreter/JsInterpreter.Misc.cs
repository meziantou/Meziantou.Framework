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
        if (mapped)
        {
            // The indices of the parameters alias the slots of the parameters in the environment of the function
            var parameterNames = frame.Code.ParameterNames ?? [];
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

    /// <summary>import.meta (modules are not supported yet).</summary>
    private static JsValue GetImportMeta(Frame frame) => ThrowHelper.ThrowSyntaxError<JsValue>("import.meta is only valid in modules");

    /// <summary>import() (modules are not supported yet): a rejected promise.</summary>
    private static JsValue ImportCall(Frame frame, JsValue specifier, JsValue options)
    {
        _ = specifier;
        _ = options;
        var capability = PromiseCapability.CreateIntrinsic(frame.Realm);
        capability.Reject(frame.Realm.CreateError(ErrorType.TypeError, "Dynamic import is not supported yet"));
        return capability.Promise;
    }

    /// <summary>AddDisposableResource for a using declaration (explicit resource management is not supported yet).</summary>
    private static void AddDisposableResource(Frame frame, JsValue value, bool isAsync)
    {
        _ = frame;
        _ = value;
        _ = isAsync;
        ThrowHelper.ThrowSyntaxError("using declarations are not supported yet");
    }

    /// <summary>Runs a generator frame suspended at its start.</summary>
    internal static JsValue RunGeneratorFrame(Frame frame) => RunWithDepth(frame.Realm.Engine, frame);

    /// <summary>Await in the frame of an async generator: the continuations resume it.</summary>
    internal static void AwaitInFrame(Frame frame, JsValue value, object state, PromiseContinuation onFulfilled, PromiseContinuation onRejected) => AwaitValue(frame, value, state, onFulfilled, onRejected);
}
