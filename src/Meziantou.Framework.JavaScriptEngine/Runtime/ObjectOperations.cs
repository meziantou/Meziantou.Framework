namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Operations on objects and functions (https://tc39.es/ecma262/#sec-operations-on-objects).</summary>
internal static class ObjectOperations
{
    /// <summary>GetFunctionRealm(obj)</summary>
    public static JsRealm GetFunctionRealm(JsObject obj)
    {
        while (true)
        {
            switch (obj)
            {
                case JsFunction function:
                    return function.Realm;
                case IBoundFunction bound:
                    obj = bound.BoundTargetFunction;
                    continue;
                case IProxyTarget proxy:
                    obj = proxy.GetProxyTargetOrThrow();
                    continue;
                default:
                    return JsEngine.Current!.CurrentRealm!;
            }
        }
    }

    /// <summary>GetPrototypeFromConstructor(constructor, intrinsicDefaultProto)</summary>
    public static JsObject GetPrototypeFromConstructor(JsObject constructor, Func<Intrinsics, JsObject> intrinsicDefaultPrototype)
    {
        var prototype = constructor.Get(new PropertyKey("prototype"));
        if (prototype.Cell is JsObject obj)
            return obj;

        return intrinsicDefaultPrototype(GetFunctionRealm(constructor).Intrinsics);
    }

    /// <summary>The prototype of an object created by a built-in constructor: from newTarget, or the intrinsic of the realm of the function when it is called.</summary>
    public static JsObject GetPrototypeForConstruct(JsBuiltinFunction function, JsObject? newTarget, Func<Intrinsics, JsObject> intrinsicDefaultPrototype)
    {
        return GetPrototypeFromConstructor(newTarget ?? function, intrinsicDefaultPrototype);
    }

    /// <summary>CreateNonEnumerableDataPropertyOrThrow(O, P, V)</summary>
    public static void CreateNonEnumerableDataPropertyOrThrow(JsObject obj, in PropertyKey key, JsValue value)
    {
        obj.DefinePropertyOrThrow(key, PropertyDescriptor.Data(value, PropertyFlags.BuiltIn));
    }

    /// <summary>OrdinaryHasInstance(C, O)</summary>
    public static bool OrdinaryHasInstance(JsValue constructor, JsValue value)
    {
        if (constructor.Cell is not JsObject { IsCallable: true } c)
            return false;

        if (c is IBoundFunction bound)
            return InstanceOf(value, new JsValue(bound.BoundTargetFunction));

        if (value.Cell is not JsObject obj)
            return false;

        var prototype = c.Get(new PropertyKey("prototype"));
        if (prototype.Cell is not JsObject prototypeObject)
            return ThrowHelper.ThrowTypeError<bool>("Function has non-object prototype in instanceof check");

        while (true)
        {
            var parent = obj.GetPrototypeOf();
            if (parent is null)
                return false;

            if (ReferenceEquals(parent, prototypeObject))
                return true;

            obj = parent;
        }
    }

    /// <summary>InstanceofOperator(V, target)</summary>
    public static bool InstanceOf(JsValue value, JsValue target)
    {
        if (target.Cell is not JsObject)
            return ThrowHelper.ThrowTypeError<bool>("Right-hand side of 'instanceof' is not an object");

        var instanceOfHandler = JsOperations.GetMethod(target, new PropertyKey(WellKnownSymbols.HasInstance));
        if (instanceOfHandler is not null)
            return JsOperations.ToBoolean(instanceOfHandler.Call(target, [value]));

        if (!target.IsCallable)
            return ThrowHelper.ThrowTypeError<bool>("Right-hand side of 'instanceof' is not callable");

        return OrdinaryHasInstance(target, value);
    }

    /// <summary>SpeciesConstructor(O, defaultConstructor)</summary>
    public static JsObject SpeciesConstructor(JsObject obj, JsObject defaultConstructor)
    {
        var constructor = obj.Get(new PropertyKey("constructor"));
        if (constructor.IsUndefined)
            return defaultConstructor;

        if (constructor.Cell is not JsObject constructorObject)
            return ThrowHelper.ThrowTypeError<JsObject>("object.constructor is not an object");

        var species = constructorObject.Get(new PropertyKey(WellKnownSymbols.Species));
        if (species.IsNullOrUndefined)
            return defaultConstructor;

        if (species.Cell is JsObject { IsConstructor: true } speciesConstructor)
            return speciesConstructor;

        return ThrowHelper.ThrowTypeError<JsObject>("object.constructor[Symbol.species] is not a constructor");
    }
}
