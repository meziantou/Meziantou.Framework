using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The ShadowRealm constructor and ShadowRealm.prototype (https://tc39.es/proposal-shadowrealm/#sec-shadowrealm-objects).</summary>
internal static class ShadowRealmBuiltins
{
    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("evaluate", 1, Evaluate),
        new("importValue", 2, ImportValue),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("ShadowRealm", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethods(prototype, PrototypeMethods);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "ShadowRealm", PropertyFlags.Configurable);

        intrinsics.AddGlobal("ShadowRealm", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>ValidateShadowRealmObject(O)</summary>
    private static JsShadowRealm ThisShadowRealm(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsShadowRealm shadowRealm)
            return shadowRealm;

        return ThrowHelper.ThrowTypeError<JsShadowRealm>($"Method ShadowRealm.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    /// <summary>ShadowRealm()</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor ShadowRealm requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.ShadowRealmPrototype);
        var realm = function.Realm.Engine.CreateShadowRealm();
        return new JsShadowRealm(prototype, realm);
    }

    /// <summary>ShadowRealm.prototype.evaluate(sourceText)</summary>
    private static JsValue Evaluate(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var shadowRealm = ThisShadowRealm(thisValue, "evaluate");
        var sourceText = GetArgument(arguments, 0);
        if (!sourceText.IsString)
            return ThrowHelper.ThrowTypeError<JsValue>("ShadowRealm.prototype.evaluate: the source text must be a string");

        // The caller realm is the current realm: the realm of the function while it runs
        return Interpreter.JsInterpreter.PerformShadowRealmEval(sourceText, function.Realm, shadowRealm.Realm);
    }

    /// <summary>ShadowRealm.prototype.importValue(specifier, exportName)</summary>
    private static JsValue ImportValue(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var shadowRealm = ThisShadowRealm(thisValue, "importValue");
        var specifierString = JsOperations.ToJsString(GetArgument(arguments, 0));
        var exportName = GetArgument(arguments, 1);
        if (!exportName.IsString)
            return ThrowHelper.ThrowTypeError<JsValue>("ShadowRealm.prototype.importValue: the export name must be a string");

        return ShadowRealmImportValue(specifierString, exportName.AsString(), function.Realm, shadowRealm.Realm);
    }

    /// <summary>ShadowRealmImportValue(specifierString, exportNameString, callerRealm, evalRealm)</summary>
    private static JsValue ShadowRealmImportValue(JsString specifierString, string exportNameString, JsRealm callerRealm, JsRealm evalRealm)
    {
        // The module is loaded in the ShadowRealm, as an import() of a code without script or module
        var engine = callerRealm.Engine;
        var previousRealm = engine.CurrentRealm;
        engine.CurrentRealm = evalRealm;
        JsValue innerPromise;
        try
        {
            innerPromise = ModuleOperations.EvaluateImportCall(evalRealm, referrer: null, JsValue.FromJsString(specifierString), options: default);
        }
        finally
        {
            engine.CurrentRealm = previousRealm;
        }

        // The handlers of the specification are built-in functions of the caller realm: the continuations run in that realm
        var capability = PromiseCapability.CreateIntrinsic(callerRealm);
        var state = new ImportValueState(capability, exportNameString, callerRealm);
        JsPromise.PerformPromiseThen((JsPromise)innerPromise.Cell!, OnImportFulfilled, OnImportRejected, state, callerRealm);
        return capability.Promise;
    }

    /// <summary>The ExportGetter function: the wrapped value of the export of the module namespace.</summary>
    private static void OnImportFulfilled(object? state, JsValue exports)
    {
        var importState = (ImportValueState)state!;
        try
        {
            var namespaceObject = exports.AsObject();
            var key = new PropertyKey(importState.ExportName);
            if (!namespaceObject.HasOwnProperty(key))
                ThrowHelper.ThrowTypeError($"The module has no export named '{importState.ExportName}'");

            var value = namespaceObject.Get(key);
            importState.Capability.Resolve(JsWrappedFunction.GetWrappedValue(importState.CallerRealm, value));
        }
        catch (JsException ex)
        {
            importState.Capability.Reject(ex.Value);
        }
    }

    /// <summary>The rejection handler: a TypeError of the caller realm (CreateTypeErrorCopy).</summary>
    private static void OnImportRejected(object? state, JsValue error)
    {
        var importState = (ImportValueState)state!;
        importState.Capability.Reject(JsWrappedFunction.CreateTypeErrorCopy(importState.CallerRealm, error).Value);
    }

    private sealed record ImportValueState(PromiseCapability Capability, string ExportName, JsRealm CallerRealm);
}

/// <summary>A ShadowRealm object: its [[ShadowRealm]] internal slot is a realm of the engine.</summary>
internal sealed class JsShadowRealm(JsObject prototype, JsRealm realm) : JsObject(prototype)
{
    /// <summary>Gets the [[ShadowRealm]].</summary>
    public JsRealm Realm { get; } = realm;
}
