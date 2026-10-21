namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The WeakRef constructor and WeakRef.prototype (https://tc39.es/ecma262/#sec-weak-ref-objects).</summary>
internal static class WeakRefBuiltins
{
    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("WeakRef", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "deref", 0, Deref);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "WeakRef", PropertyFlags.Configurable);

        intrinsics.AddGlobal("WeakRef", constructor);
        return constructor;
    }

    /// <summary>WeakRef(target)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor WeakRef requires 'new'");

        var target = arguments.Length > 0 ? arguments[0] : default;
        if (!WeakReferences.CanBeHeldWeakly(target))
            return ThrowHelper.ThrowTypeError<JsValue>($"WeakRef: invalid target {JsOperations.DescribeForError(target)}");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.WeakRefPrototype);
        function.Realm.Engine.AddToKeptObjects(target.Cell!);
        return new JsWeakRef(prototype, target.Cell!);
    }

    /// <summary>WeakRef.prototype.deref()</summary>
    private static JsValue Deref(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsWeakRef weakRef)
            return ThrowHelper.ThrowTypeError<JsValue>($"Method WeakRef.prototype.deref called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        return weakRef.Deref(function.Realm.Engine);
    }
}

/// <summary>The FinalizationRegistry constructor and FinalizationRegistry.prototype (https://tc39.es/ecma262/#sec-finalization-registry-objects).</summary>
internal static class FinalizationRegistryBuiltins
{
    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("FinalizationRegistry", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "register", 2, Register);
        realm.DefineMethod(prototype, "unregister", 1, Unregister);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "FinalizationRegistry", PropertyFlags.Configurable);

        intrinsics.AddGlobal("FinalizationRegistry", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsFinalizationRegistry ThisRegistry(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsFinalizationRegistry registry)
            return registry;

        return ThrowHelper.ThrowTypeError<JsFinalizationRegistry>($"Method FinalizationRegistry.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    /// <summary>FinalizationRegistry(cleanupCallback)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor FinalizationRegistry requires 'new'");

        var cleanupCallback = GetArgument(arguments, 0);
        if (!cleanupCallback.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("FinalizationRegistry: cleanup must be callable");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.FinalizationRegistryPrototype);
        var engine = function.Realm.Engine;
        return new JsFinalizationRegistry(prototype, engine.CurrentRealm!, engine.MakeJobCallback(cleanupCallback.AsObject()));
    }

    /// <summary>FinalizationRegistry.prototype.register(target, heldValue [, unregisterToken])</summary>
    private static JsValue Register(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var registry = ThisRegistry(thisValue, "register");
        var target = GetArgument(arguments, 0);
        var heldValue = GetArgument(arguments, 1);
        var unregisterToken = GetArgument(arguments, 2);
        if (!WeakReferences.CanBeHeldWeakly(target))
            return ThrowHelper.ThrowTypeError<JsValue>($"FinalizationRegistry.prototype.register: invalid target {JsOperations.DescribeForError(target)}");

        if (JsOperations.SameValue(target, heldValue))
            return ThrowHelper.ThrowTypeError<JsValue>("FinalizationRegistry.prototype.register: target and holdings must not be same");

        JsCell? token = null;
        if (WeakReferences.CanBeHeldWeakly(unregisterToken))
        {
            token = unregisterToken.Cell;
        }
        else if (!unregisterToken.IsUndefined)
        {
            return ThrowHelper.ThrowTypeError<JsValue>($"FinalizationRegistry.prototype.register: invalid unregister token {JsOperations.DescribeForError(unregisterToken)}");
        }

        registry.Register(target.Cell!, heldValue, token);
        return JsValue.Undefined;
    }

    /// <summary>FinalizationRegistry.prototype.unregister(unregisterToken)</summary>
    private static JsValue Unregister(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var registry = ThisRegistry(thisValue, "unregister");
        var unregisterToken = GetArgument(arguments, 0);
        if (!WeakReferences.CanBeHeldWeakly(unregisterToken))
            return ThrowHelper.ThrowTypeError<JsValue>($"Invalid unregisterToken ('{JsOperations.DescribeForError(unregisterToken)}')");

        return registry.Unregister(unregisterToken.Cell!);
    }
}
