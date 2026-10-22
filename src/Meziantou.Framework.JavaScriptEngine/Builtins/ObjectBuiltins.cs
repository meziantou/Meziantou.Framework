namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Object constructor and Object.prototype (https://tc39.es/ecma262/#sec-object-objects), with the Annex B methods.</summary>
internal static class ObjectBuiltins
{
    private static readonly PropertyKey ToStringKey = new("toString");

    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("assign", 2, Assign),
        new("create", 2, Create),
        new("defineProperties", 2, DefineProperties),
        new("defineProperty", 3, DefineProperty),
        new("entries", 1, Entries),
        new("freeze", 1, Freeze),
        new("fromEntries", 1, FromEntries),
        new("getOwnPropertyDescriptor", 2, GetOwnPropertyDescriptor),
        new("getOwnPropertyDescriptors", 1, GetOwnPropertyDescriptors),
        new("getOwnPropertyNames", 1, GetOwnPropertyNames),
        new("getOwnPropertySymbols", 1, GetOwnPropertySymbols),
        new("getPrototypeOf", 1, GetPrototypeOf),
        new("groupBy", 2, GroupBy),
        new("hasOwn", 2, HasOwn),
        new("is", 2, Is),
        new("isExtensible", 1, IsExtensible),
        new("isFrozen", 1, IsFrozen),
        new("isSealed", 1, IsSealed),
        new("keys", 1, Keys),
        new("preventExtensions", 1, PreventExtensions),
        new("seal", 1, Seal),
        new("setPrototypeOf", 2, SetPrototypeOf),
        new("values", 1, Values),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("__defineGetter__", 2, DefineGetter),
        new("__defineSetter__", 2, DefineSetter),
        new("hasOwnProperty", 1, HasOwnProperty),
        new("isPrototypeOf", 1, IsPrototypeOf),
        new("__lookupGetter__", 1, LookupGetter),
        new("__lookupSetter__", 1, LookupSetter),
        new("propertyIsEnumerable", 1, PropertyIsEnumerable),
        new("toLocaleString", 0, ToLocaleString),
        new("toString", 0, ToString),
        new("valueOf", 0, ValueOf),
    ];

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.ObjectPrototype;
        var constructor = realm.CreateBuiltinFunction("Object", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        realm.DefineMethods(constructor, ConstructorMethods);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethods(prototype, PrototypeMethods);
        realm.DefineAccessor(prototype, "__proto__", GetProto, SetProto);

        intrinsics.AddGlobal("Object", constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;


    /// <summary>Object([value])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is not null && !ReferenceEquals(newTarget, function))
        {
            var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, static i => i.ObjectPrototype);
            return new JsObject(prototype);
        }

        var value = Arg(arguments, 0);
        if (value.IsNullOrUndefined)
            return new JsObject(function.Realm.Intrinsics.ObjectPrototype);

        return JsOperations.ToObject(value);
    }

    /// <summary>Object.assign(target, ...sources)</summary>
    private static JsValue Assign(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var to = JsOperations.ToObject(Arg(arguments, 0));
        var toValue = new JsValue(to);
        for (var i = 1; i < arguments.Length; i++)
        {
            var source = arguments[i];
            if (source.IsNullOrUndefined)
                continue;

            var from = JsOperations.ToObject(source);
            foreach (var key in from.OwnPropertyKeys())
            {
                if (from.GetOwnProperty(key, out var descriptor) && descriptor.Enumerable)
                {
                    to.Set(key, from.Get(key), throwOnFailure: true);
                }
            }
        }

        return toValue;
    }

    /// <summary>Object.create(O, Properties)</summary>
    private static JsValue Create(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var proto = Arg(arguments, 0);
        if (!proto.IsObject && !proto.IsNull)
            return ThrowHelper.ThrowTypeError<JsValue>($"Object prototype may only be an Object or null: {JsOperations.DescribeForError(proto)}");

        var obj = new JsObject(proto.TryGetObject());
        var properties = Arg(arguments, 1);
        if (!properties.IsUndefined)
        {
            ObjectDefineProperties(obj, properties);
        }

        return obj;
    }

    /// <summary>ObjectDefineProperties(O, Properties)</summary>
    private static void ObjectDefineProperties(JsObject obj, JsValue properties)
    {
        var props = JsOperations.ToObject(properties);
        var keys = props.OwnPropertyKeys();
        var descriptors = new List<(PropertyKey Key, PropertyDescriptor Descriptor)>(keys.Count);
        foreach (var key in keys)
        {
            if (props.GetOwnProperty(key, out var propDesc) && propDesc.Enumerable)
            {
                var descObj = props.Get(key);
                descriptors.Add((key, PropertyOperations.ToPropertyDescriptor(descObj)));
            }
        }

        foreach (var (key, descriptor) in descriptors)
        {
            obj.DefinePropertyOrThrow(key, descriptor);
        }
    }

    /// <summary>Object.defineProperties(O, Properties)</summary>
    private static JsValue DefineProperties(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = Arg(arguments, 0);
        if (o.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("Object.defineProperties called on non-object");

        ObjectDefineProperties(obj, Arg(arguments, 1));
        return o;
    }

    /// <summary>Object.defineProperty(O, P, Attributes)</summary>
    private static JsValue DefineProperty(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = Arg(arguments, 0);
        if (o.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("Object.defineProperty called on non-object");

        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        var descriptor = PropertyOperations.ToPropertyDescriptor(Arg(arguments, 2));
        obj.DefinePropertyOrThrow(key, descriptor);
        return o;
    }

    /// <summary>Object.entries(O)</summary>
    private static JsValue Entries(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        var entries = PropertyOperations.EnumerableOwnProperties(obj, EnumerableOwnPropertiesKind.KeyValue);
        return JsArray.Create(function.Realm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(entries));
    }

    /// <summary>Object.keys(O)</summary>
    private static JsValue Keys(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        var keys = PropertyOperations.EnumerableOwnProperties(obj, EnumerableOwnPropertiesKind.Key);
        return JsArray.Create(function.Realm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(keys));
    }

    /// <summary>Object.values(O)</summary>
    private static JsValue Values(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        var values = PropertyOperations.EnumerableOwnProperties(obj, EnumerableOwnPropertiesKind.Value);
        return JsArray.Create(function.Realm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values));
    }

    /// <summary>Object.freeze(O)</summary>
    private static JsValue Freeze(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = Arg(arguments, 0);
        if (o.Cell is JsObject obj && !PropertyOperations.SetIntegrityLevel(obj, IntegrityLevel.Frozen))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot freeze");

        return o;
    }

    /// <summary>Object.seal(O)</summary>
    private static JsValue Seal(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = Arg(arguments, 0);
        if (o.Cell is JsObject obj && !PropertyOperations.SetIntegrityLevel(obj, IntegrityLevel.Sealed))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot seal");

        return o;
    }

    /// <summary>Object.preventExtensions(O)</summary>
    private static JsValue PreventExtensions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = Arg(arguments, 0);
        if (o.Cell is JsObject obj && !obj.PreventExtensions())
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot prevent extensions");

        return o;
    }

    /// <summary>Object.isFrozen(O)</summary>
    private static JsValue IsFrozen(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return Arg(arguments, 0).Cell is not JsObject obj || PropertyOperations.TestIntegrityLevel(obj, IntegrityLevel.Frozen);
    }

    /// <summary>Object.isSealed(O)</summary>
    private static JsValue IsSealed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return Arg(arguments, 0).Cell is not JsObject obj || PropertyOperations.TestIntegrityLevel(obj, IntegrityLevel.Sealed);
    }

    /// <summary>Object.isExtensible(O)</summary>
    private static JsValue IsExtensible(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return Arg(arguments, 0).Cell is JsObject obj && obj.IsExtensible();
    }

    /// <summary>Object.fromEntries(iterable)</summary>
    private static JsValue FromEntries(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var iterable = Arg(arguments, 0);
        JsOperations.RequireObjectCoercible(iterable, "Object.fromEntries");
        var obj = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        PropertyOperations.AddEntriesFromIterable(iterable, (key, value) => obj.CreateDataPropertyOrThrow(JsOperations.ToPropertyKey(key), value));
        return obj;
    }

    /// <summary>Object.getOwnPropertyDescriptor(O, P)</summary>
    private static JsValue GetOwnPropertyDescriptor(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        var found = obj.GetOwnProperty(key, out var descriptor);
        return PropertyOperations.FromPropertyDescriptor(function.Realm, found, descriptor);
    }

    /// <summary>Object.getOwnPropertyDescriptors(O)</summary>
    private static JsValue GetOwnPropertyDescriptors(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        var descriptors = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        foreach (var key in obj.OwnPropertyKeys())
        {
            if (obj.GetOwnProperty(key, out var descriptor))
            {
                descriptors.CreateDataPropertyOrThrow(key, PropertyOperations.FromPropertyDescriptor(function.Realm, descriptor));
            }
        }

        return descriptors;
    }

    /// <summary>Object.getOwnPropertyNames(O)</summary>
    private static JsValue GetOwnPropertyNames(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return GetOwnPropertyKeys(function.Realm, Arg(arguments, 0), symbols: false);
    }

    /// <summary>Object.getOwnPropertySymbols(O)</summary>
    private static JsValue GetOwnPropertySymbols(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return GetOwnPropertyKeys(function.Realm, Arg(arguments, 0), symbols: true);
    }

    /// <summary>GetOwnPropertyKeys(O, type)</summary>
    private static JsArray GetOwnPropertyKeys(JsRealm realm, JsValue value, bool symbols)
    {
        var obj = JsOperations.ToObject(value);
        var keys = obj.OwnPropertyKeys();
        var array = new JsArray(realm.Intrinsics.ArrayPrototype);
        foreach (var key in keys)
        {
            if (key.IsSymbol == symbols)
            {
                array.Push(key.ToJsValue());
            }
        }

        return array;
    }

    /// <summary>Object.getPrototypeOf(O)</summary>
    private static JsValue GetPrototypeOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        return JsValue.FromObject(obj.GetPrototypeOf());
    }

    /// <summary>Object.setPrototypeOf(O, proto)</summary>
    private static JsValue SetPrototypeOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = Arg(arguments, 0);
        JsOperations.RequireObjectCoercible(o, "Object.setPrototypeOf");
        var proto = Arg(arguments, 1);
        if (!proto.IsObject && !proto.IsNull)
            return ThrowHelper.ThrowTypeError<JsValue>($"Object prototype may only be an Object or null: {JsOperations.DescribeForError(proto)}");

        if (o.Cell is not JsObject obj)
            return o;

        if (!obj.SetPrototypeOf(proto.TryGetObject()))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot set the prototype of the object");

        return o;
    }

    /// <summary>Object.groupBy(items, callback)</summary>
    private static JsValue GroupBy(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var groups = PropertyOperations.GroupBy(Arg(arguments, 0), Arg(arguments, 1), propertyKeys: true);
        var obj = new JsObject(prototype: null);
        foreach (var (key, elements) in groups)
        {
            obj.CreateDataPropertyOrThrow(JsOperations.ToPropertyKey(key), JsArray.Create(function.Realm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(elements)));
        }

        return obj;
    }

    /// <summary>Object.hasOwn(O, P)</summary>
    private static JsValue HasOwn(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(Arg(arguments, 0));
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        return obj.HasOwnProperty(key);
    }

    /// <summary>Object.is(value1, value2)</summary>
    private static JsValue Is(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return JsOperations.SameValue(Arg(arguments, 0), Arg(arguments, 1));
    }

    /// <summary>Object.prototype.hasOwnProperty(V)</summary>
    private static JsValue HasOwnProperty(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var key = JsOperations.ToPropertyKey(Arg(arguments, 0));
        var obj = JsOperations.ToObject(thisValue);
        return obj.HasOwnProperty(key);
    }

    /// <summary>Object.prototype.isPrototypeOf(V)</summary>
    private static JsValue IsPrototypeOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (Arg(arguments, 0).Cell is not JsObject v)
            return false;

        var obj = JsOperations.ToObject(thisValue);
        while (true)
        {
            var parent = v.GetPrototypeOf();
            if (parent is null)
                return false;

            if (ReferenceEquals(parent, obj))
                return true;

            v = parent;
        }
    }

    /// <summary>Object.prototype.propertyIsEnumerable(V)</summary>
    private static JsValue PropertyIsEnumerable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var key = JsOperations.ToPropertyKey(Arg(arguments, 0));
        var obj = JsOperations.ToObject(thisValue);
        return obj.GetOwnProperty(key, out var descriptor) && descriptor.Enumerable;
    }

    /// <summary>Object.prototype.toLocaleString()</summary>
    private static JsValue ToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return JsOperations.Invoke(thisValue, ToStringKey, []);
    }

    /// <summary>Object.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.IsUndefined)
            return "[object Undefined]";

        if (thisValue.IsNull)
            return "[object Null]";

        var obj = JsOperations.ToObject(thisValue);
        var builtinTag = GetBuiltinTag(obj);
        var tag = obj.Get(new PropertyKey(WellKnownSymbols.ToStringTag));
        if (tag.Cell is JsString tagString)
            return "[object " + tagString.ToString() + "]";

        return "[object " + builtinTag + "]";
    }

    /// <summary>The builtinTag of Object.prototype.toString: from IsArray, [[Call]] and the internal slots of the object.</summary>
    internal static string GetBuiltinTag(JsObject obj)
    {
        if (JsOperations.IsArray(obj))
            return "Array";

        return obj.Class switch
        {
            ObjectClass.Arguments => "Arguments",
            _ when obj.IsCallable => "Function",
            ObjectClass.Error => "Error",
            ObjectClass.Boolean => "Boolean",
            ObjectClass.Number => "Number",
            ObjectClass.String => "String",
            ObjectClass.Date => "Date",
            ObjectClass.RegExp => "RegExp",
            _ => "Object",
        };
    }

    /// <summary>Object.prototype.valueOf()</summary>
    private static JsValue ValueOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return JsOperations.ToObject(thisValue);
    }

    /// <summary>get Object.prototype.__proto__</summary>
    private static JsValue GetProto(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        return JsValue.FromObject(obj.GetPrototypeOf());
    }

    /// <summary>set Object.prototype.__proto__</summary>
    private static JsValue SetProto(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        JsOperations.RequireObjectCoercible(thisValue, "Object.prototype.__proto__");
        var proto = Arg(arguments, 0);
        if (!proto.IsObject && !proto.IsNull)
            return JsValue.Undefined;

        if (thisValue.Cell is not JsObject obj)
            return JsValue.Undefined;

        if (!obj.SetPrototypeOf(proto.TryGetObject()))
            return ThrowHelper.ThrowTypeError<JsValue>("Object.prototype.__proto__ setter: cannot set the prototype of the object");

        return JsValue.Undefined;
    }

    /// <summary>Object.prototype.__defineGetter__(P, getter)</summary>
    private static JsValue DefineGetter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var getter = Arg(arguments, 1);
        if (!getter.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("Object.prototype.__defineGetter__: Expecting function");

        var descriptor = new PropertyDescriptor { Get = getter, Flags = PropertyFlags.Enumerable | PropertyFlags.Configurable, Fields = DescriptorFields.Get | DescriptorFields.Enumerable | DescriptorFields.Configurable };
        var key = JsOperations.ToPropertyKey(Arg(arguments, 0));
        obj.DefinePropertyOrThrow(key, descriptor);
        return JsValue.Undefined;
    }

    /// <summary>Object.prototype.__defineSetter__(P, setter)</summary>
    private static JsValue DefineSetter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var setter = Arg(arguments, 1);
        if (!setter.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("Object.prototype.__defineSetter__: Expecting function");

        var descriptor = new PropertyDescriptor { Set = setter, Flags = PropertyFlags.Enumerable | PropertyFlags.Configurable, Fields = DescriptorFields.Set | DescriptorFields.Enumerable | DescriptorFields.Configurable };
        var key = JsOperations.ToPropertyKey(Arg(arguments, 0));
        obj.DefinePropertyOrThrow(key, descriptor);
        return JsValue.Undefined;
    }

    /// <summary>Object.prototype.__lookupGetter__(P)</summary>
    private static JsValue LookupGetter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return LookupAccessor(thisValue, Arg(arguments, 0), getter: true);
    }

    /// <summary>Object.prototype.__lookupSetter__(P)</summary>
    private static JsValue LookupSetter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return LookupAccessor(thisValue, Arg(arguments, 0), getter: false);
    }

    private static JsValue LookupAccessor(JsValue thisValue, JsValue property, bool getter)
    {
        JsObject? obj = JsOperations.ToObject(thisValue);
        var key = JsOperations.ToPropertyKey(property);
        while (obj is not null)
        {
            if (obj.GetOwnProperty(key, out var descriptor))
            {
                if (descriptor.IsAccessorDescriptor)
                    return getter ? descriptor.Get : descriptor.Set;

                return JsValue.Undefined;
            }

            obj = obj.GetPrototypeOf();
        }

        return JsValue.Undefined;
    }
}
