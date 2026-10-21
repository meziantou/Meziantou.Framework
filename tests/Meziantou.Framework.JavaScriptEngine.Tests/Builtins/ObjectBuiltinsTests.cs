namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class ObjectBuiltinsTests
{
    [Fact]
    public void Constructor_WrapsPrimitivesAndCreatesObjects()
    {
        using var context = new FundamentalsTestContext();
        var objectConstructor = context.Global("Object");
        Assert.Same(context.Intrinsics.Object, objectConstructor);

        var created = objectConstructor.Call(JsValue.Undefined, []).AsObject();
        Assert.Same(context.Intrinsics.ObjectPrototype, created.GetPrototypeOf());

        var wrapper = objectConstructor.Call(JsValue.Undefined, [true]).AsObject();
        Assert.Equal(ObjectClass.Boolean, wrapper.Class);

        var obj = context.NewObject();
        Assert.Same(obj, objectConstructor.Call(JsValue.Undefined, [obj]).AsObject());
        Assert.Same(obj, JsOperations.Construct(objectConstructor, [obj]).AsObject());
    }

    [Fact]
    public void Constructor_SubclassUsesNewTargetPrototype()
    {
        using var context = new FundamentalsTestContext();
        var prototype = context.NewObject();
        var newTarget = context.Function((_, _) => JsValue.Undefined, isConstructor: true);
        newTarget.DefineBuiltIn("prototype", prototype);

        var result = JsOperations.Construct(context.Global("Object"), [42], newTarget).AsObject();
        Assert.Same(prototype, result.GetPrototypeOf());
        Assert.Equal(ObjectClass.Object, result.Class);
    }

    [Fact]
    public void FunctionLengthsAndNames()
    {
        using var context = new FundamentalsTestContext();
        var objectConstructor = context.Global("Object");
        Assert.Equal(1, FundamentalsTestContext.Get(objectConstructor, "length").AsNumber());
        Assert.Equal(2, FundamentalsTestContext.Get(FundamentalsTestContext.Get(objectConstructor, "assign"), "length").AsNumber());
        Assert.Equal(3, FundamentalsTestContext.Get(FundamentalsTestContext.Get(objectConstructor, "defineProperty"), "length").AsNumber());

        context.Intrinsics.ObjectPrototype.GetOwnProperty("__proto__", out var proto);
        Assert.True(proto.IsAccessorDescriptor);
        Assert.False(proto.Enumerable);
        Assert.True(proto.Configurable);
        Assert.Equal("get __proto__", FundamentalsTestContext.Get(proto.Get, "name").AsString());
        Assert.Equal("set __proto__", FundamentalsTestContext.Get(proto.Set, "name").AsString());
    }

    [Fact]
    public void DefineProperty_ValidatesTheDescriptor()
    {
        using var context = new FundamentalsTestContext();
        var obj = context.NewObject();

        // Accessor and data fields together
        var invalid = context.NewObject();
        invalid.CreateDataPropertyOrThrow("get", context.Function((_, _) => 1));
        invalid.CreateDataPropertyOrThrow("value", 1);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "defineProperty", obj, "x", invalid));

        // Non-callable getter
        var badGetter = context.NewObject();
        badGetter.CreateDataPropertyOrThrow("get", 1);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "defineProperty", obj, "x", badGetter));

        // Not an object
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "defineProperty", obj, "x", 1));
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "defineProperty", 1, "x", context.NewObject()));

        // A partial descriptor creates a non-writable, non-enumerable, non-configurable property
        var partial = context.NewObject();
        partial.CreateDataPropertyOrThrow("value", 42);
        Assert.Same(obj, context.CallStatic("Object", "defineProperty", obj, "x", partial).AsObject());
        Assert.True(obj.GetOwnProperty("x", out var descriptor));
        Assert.Equal(42, descriptor.Value.AsNumber());
        Assert.False(descriptor.Writable);
        Assert.False(descriptor.Enumerable);
        Assert.False(descriptor.Configurable);

        // Redefining a non-configurable property throws
        var redefine = context.NewObject();
        redefine.CreateDataPropertyOrThrow("value", 43);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "defineProperty", obj, "x", redefine));
    }

    [Fact]
    public void ToPropertyDescriptor_ReadsTheFieldsInSpecOrder()
    {
        using var context = new FundamentalsTestContext();
        var log = new List<string>();
        var attributes = context.NewObject();
        foreach (var name in new[] { "value", "set", "writable", "get", "configurable", "enumerable" })
        {
            var getter = context.Function((_, _) =>
            {
                log.Add(name);
                return name is "get" or "set" ? JsValue.Undefined : JsValue.True;
            });
            attributes.DefineBuiltInAccessor(name, getter, setter: null, PropertyFlags.Configurable | PropertyFlags.Enumerable);
        }

        context.AssertThrows(ErrorType.TypeError, () => PropertyOperations.ToPropertyDescriptor(attributes));
        Assert.Equal(["enumerable", "configurable", "value", "writable", "get", "set"], log);
    }

    [Fact]
    public void GetOwnPropertyDescriptor_RoundTrips()
    {
        using var context = new FundamentalsTestContext();
        var obj = context.NewObject();
        obj.CreateDataPropertyOrThrow("a", 1);
        var descriptor = context.CallStatic("Object", "getOwnPropertyDescriptor", obj, "a").AsObject();
        Assert.Equal(["value", "writable", "enumerable", "configurable"], descriptor.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
        Assert.True(context.CallStatic("Object", "getOwnPropertyDescriptor", obj, "missing").IsUndefined);

        var descriptors = context.CallStatic("Object", "getOwnPropertyDescriptors", context.NewArray(1)).AsObject();
        Assert.Equal(["0", "length"], descriptors.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
    }

    [Fact]
    public void Freeze_Array()
    {
        using var context = new FundamentalsTestContext();
        var array = context.NewArray(1, 2, 3);
        context.CallStatic("Object", "freeze", array);

        Assert.True(context.CallStatic("Object", "isFrozen", array).AsBoolean());
        Assert.True(context.CallStatic("Object", "isSealed", array).AsBoolean());
        Assert.False(context.CallStatic("Object", "isExtensible", array).AsBoolean());
        Assert.True(array.GetOwnProperty("length", out var length));
        Assert.False(length.Writable);
        Assert.True(array.GetOwnProperty(new PropertyKey(1u), out var element));
        Assert.False(element.Writable);
        Assert.False(element.Configurable);
        Assert.False(array.Set(new PropertyKey(0u), 10, array));
        Assert.Equal(1, array.Get(new PropertyKey(0u)).AsNumber());
        Assert.False(array.Set("length", 0, array));
        Assert.Equal(3u, array.Length);
        Assert.False(array.Set(new PropertyKey(3u), 4, array));
    }

    [Fact]
    public void Seal_Array()
    {
        using var context = new FundamentalsTestContext();
        var array = context.NewArray(1, 2);
        context.CallStatic("Object", "seal", array);

        Assert.True(context.CallStatic("Object", "isSealed", array).AsBoolean());
        Assert.False(context.CallStatic("Object", "isFrozen", array).AsBoolean());
        Assert.True(array.Set(new PropertyKey(0u), 10, array));
        Assert.Equal(10, array.Get(new PropertyKey(0u)).AsNumber());
        Assert.False(array.Delete(new PropertyKey(1u)));
        Assert.False(array.Set("length", 0, array));
        Assert.Equal(2u, array.Length);
    }

    [Fact]
    public void IntegrityLevels_OfPrimitives()
    {
        using var context = new FundamentalsTestContext();
        Assert.Equal(1, context.CallStatic("Object", "freeze", 1).AsNumber());
        Assert.True(context.CallStatic("Object", "isFrozen", 1).AsBoolean());
        Assert.True(context.CallStatic("Object", "isSealed", "a").AsBoolean());
        Assert.False(context.CallStatic("Object", "isExtensible", 1).AsBoolean());
        Assert.True(context.CallStatic("Object", "isFrozen", context.NewObject()).IsBoolean);
        Assert.False(context.CallStatic("Object", "isFrozen", context.NewObject()).AsBoolean());

        // An empty non-extensible object is frozen
        var empty = context.NewObject();
        context.CallStatic("Object", "preventExtensions", empty);
        Assert.True(context.CallStatic("Object", "isFrozen", empty).AsBoolean());
    }

    [Fact]
    public void KeysValuesEntries()
    {
        using var context = new FundamentalsTestContext();
        var obj = context.NewObject();
        obj.CreateDataPropertyOrThrow("b", 1);
        obj.CreateDataPropertyOrThrow(new PropertyKey(2u), 2);
        obj.CreateDataPropertyOrThrow("a", 3);
        obj.CreateDataPropertyOrThrow(new JsSymbol("s"), 4);
        obj.DefineBuiltIn("hidden", 5);

        var keys = (JsArray)context.CallStatic("Object", "keys", obj).AsObject();
        Assert.Equal(["2", "b", "a"], ToStrings(keys));

        var values = (JsArray)context.CallStatic("Object", "values", obj).AsObject();
        Assert.Equal([2.0, 1.0, 3.0], Enumerable.Range(0, (int)values.Length).Select(i => values.Get(new PropertyKey((uint)i)).AsNumber()).ToArray());

        var entries = (JsArray)context.CallStatic("Object", "entries", "ab").AsObject();
        Assert.Equal(2u, entries.Length);
        var first = entries.Get(new PropertyKey(0u)).AsObject();
        Assert.Equal("0", first.Get(new PropertyKey(0u)).AsString());
        Assert.Equal("a", first.Get(new PropertyKey(1u)).AsString());
    }

    [Fact]
    public void Assign_UsesSetAndSkipsNullSources()
    {
        using var context = new FundamentalsTestContext();
        var source = context.NewObject();
        source.CreateDataPropertyOrThrow("a", 1);
        source.DefineBuiltIn("hidden", 2);
        var target = context.NewObject();
        var result = context.CallStatic("Object", "assign", target, JsValue.Null, source, JsValue.Undefined).AsObject();
        Assert.Same(target, result);
        Assert.Equal(1, target.Get("a").AsNumber());
        Assert.False(target.HasOwnProperty("hidden"));

        var frozen = context.NewObject();
        frozen.DefineBuiltIn("a", 0, PropertyFlags.None);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "assign", frozen, source));
    }

    [Fact]
    public void FromEntries_And_GroupBy()
    {
        using var context = new FundamentalsTestContext();
        var entries = context.NewIterable(context.NewArray("a", 1), context.NewArray("b", 2), context.NewArray("a", 3));
        var obj = context.CallStatic("Object", "fromEntries", entries).AsObject();
        Assert.Equal(3, obj.Get("a").AsNumber());
        Assert.Equal(2, obj.Get("b").AsNumber());

        // A non-object entry closes the iterator
        var log = new List<string>();
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "fromEntries", context.NewIterable(log, context.NewArray("a", 1), 1, 2)));
        Assert.Equal(["return"], log);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "fromEntries", JsValue.Undefined));

        // An exception of the callback closes the iterator
        log.Clear();
        var throwing = context.Function((_, _) => throw ThrowHelper.CreateException(ErrorType.RangeError, "callback"));
        context.AssertThrows(ErrorType.RangeError, () => context.CallStatic("Object", "groupBy", context.NewIterable(log, 1, 2), throwing));
        Assert.Equal(["return"], log);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "groupBy", context.NewIterable(1), 1));

        var callback = context.Function((_, args) => args[0].AsNumber() % 2 == 0 ? "even" : "odd");
        var groups = context.CallStatic("Object", "groupBy", context.NewIterable(1, 2, 3, 4, 5), callback).AsObject();
        Assert.Null(groups.GetPrototypeOf());
        Assert.Equal(["odd", "even"], groups.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
        Assert.Equal(3u, ((JsArray)groups.Get("odd").AsObject()).Length);
        Assert.Equal(2u, ((JsArray)groups.Get("even").AsObject()).Length);
    }

    [Fact]
    public void Create_And_Prototypes()
    {
        using var context = new FundamentalsTestContext();
        var properties = context.NewObject();
        var descriptor = context.NewObject();
        descriptor.CreateDataPropertyOrThrow("value", 1);
        descriptor.CreateDataPropertyOrThrow("enumerable", true);
        properties.CreateDataPropertyOrThrow("x", descriptor);
        var obj = context.CallStatic("Object", "create", JsValue.Null, properties).AsObject();
        Assert.Null(obj.GetPrototypeOf());
        Assert.Equal(1, obj.Get("x").AsNumber());

        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "create", 1));

        var proto = context.NewObject();
        context.CallStatic("Object", "setPrototypeOf", obj, proto);
        Assert.Same(proto, context.CallStatic("Object", "getPrototypeOf", obj).AsObject());
        Assert.True(context.CallPrototypeMethod("Object", "isPrototypeOf", proto, obj).AsBoolean());

        // A cycle is rejected
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "setPrototypeOf", proto, obj));

        // Object.prototype is an immutable prototype exotic object
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "setPrototypeOf", context.Intrinsics.ObjectPrototype, proto));
        Assert.Equal(1, context.CallStatic("Object", "setPrototypeOf", 1, proto).AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "setPrototypeOf", JsValue.Undefined, proto));

        // __proto__
        Assert.Same(proto, FundamentalsTestContext.Get(obj, "__proto__").AsObject());
        Assert.True(FundamentalsTestContext.Get(context.Intrinsics.ObjectPrototype, "__proto__").IsNull);
        Assert.True(obj.Set("__proto__", JsValue.Null, obj));
        Assert.Null(obj.GetPrototypeOf());
    }

    [Fact]
    public void ToString_Tags()
    {
        using var context = new FundamentalsTestContext();
        string Tag(JsValue value) => context.CallPrototypeMethod("Object", "toString", value).AsString();

        Assert.Equal("[object Undefined]", Tag(JsValue.Undefined));
        Assert.Equal("[object Null]", Tag(JsValue.Null));
        Assert.Equal("[object Number]", Tag(1));
        Assert.Equal("[object String]", Tag("a"));
        Assert.Equal("[object Boolean]", Tag(true));
        Assert.Equal("[object Symbol]", Tag(new JsSymbol("x")));
        Assert.Equal("[object Object]", Tag(context.NewObject()));
        Assert.Equal("[object Array]", Tag(context.NewArray()));
        Assert.Equal("[object Function]", Tag(context.Global("Object")));
        Assert.Equal("[object Error]", Tag(context.Realm.CreateError(ErrorType.TypeError, "x")));
        Assert.Equal("[object Reflect]", Tag(context.Global("Reflect")));

        var tagged = context.NewObject();
        tagged.CreateDataPropertyOrThrow(WellKnownSymbols.ToStringTag, "Custom");
        Assert.Equal("[object Custom]", Tag(tagged));

        // A non-string tag is ignored
        var array = context.NewArray();
        array.CreateDataPropertyOrThrow(WellKnownSymbols.ToStringTag, 1);
        Assert.Equal("[object Array]", Tag(array));

        // A proxy of an array is an array; a proxy of a function is a function
        Assert.Equal("[object Array]", Tag(context.NewProxy(context.NewArray())));
        Assert.Equal("[object Function]", Tag(context.NewProxy(context.Global("Object"))));
        Assert.Equal("[object Object]", Tag(context.NewProxy(context.Realm.CreateError(ErrorType.TypeError, "x"))));

        // IsArray throws on a revoked proxy
        var revocable = context.CallStatic("Proxy", "revocable", context.NewArray(), context.NewObject());
        FundamentalsTestContext.Invoke(revocable, "revoke");
        context.AssertThrows(ErrorType.TypeError, () => Tag(FundamentalsTestContext.Get(revocable, "proxy")));
    }

    [Fact]
    public void PrototypeMethods()
    {
        using var context = new FundamentalsTestContext();
        var obj = context.NewObject();
        obj.CreateDataPropertyOrThrow("a", 1);
        obj.DefineBuiltIn("b", 2);
        Assert.True(context.CallPrototypeMethod("Object", "hasOwnProperty", obj, "a").AsBoolean());
        Assert.False(context.CallPrototypeMethod("Object", "hasOwnProperty", obj, "toString").AsBoolean());
        Assert.True(context.CallPrototypeMethod("Object", "propertyIsEnumerable", obj, "a").AsBoolean());
        Assert.False(context.CallPrototypeMethod("Object", "propertyIsEnumerable", obj, "b").AsBoolean());
        Assert.True(context.CallStatic("Object", "hasOwn", obj, "b").AsBoolean());
        Assert.True(context.CallStatic("Object", "is", double.NaN, double.NaN).AsBoolean());
        Assert.False(context.CallStatic("Object", "is", 0.0, -0.0).AsBoolean());
        Assert.Equal("[object Object]", context.CallPrototypeMethod("Object", "toLocaleString", obj).AsString());

        // ToPropertyKey runs before ToObject
        var log = new List<string>();
        var key = context.NewObject();
        key.CreateDataPropertyOrThrow("toString", context.Function((_, _) =>
        {
            log.Add("toString");
            return "a";
        }));
        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Object", "hasOwnProperty", JsValue.Undefined, key));
        Assert.Equal(["toString"], log);
    }

    [Fact]
    public void AnnexB_DefineAndLookupAccessors()
    {
        using var context = new FundamentalsTestContext();
        var getter = context.Function((_, _) => 42);
        var setter = context.Function((_, _) => JsValue.Undefined);
        var proto = context.NewObject();
        context.CallPrototypeMethod("Object", "__defineGetter__", proto, "x", getter);
        context.CallPrototypeMethod("Object", "__defineSetter__", proto, "x", setter);
        Assert.True(proto.GetOwnProperty("x", out var descriptor));
        Assert.True(descriptor.Enumerable);
        Assert.True(descriptor.Configurable);

        var obj = new JsObject(proto);
        Assert.Same(getter, context.CallPrototypeMethod("Object", "__lookupGetter__", obj, "x").AsObject());
        Assert.Same(setter, context.CallPrototypeMethod("Object", "__lookupSetter__", obj, "x").AsObject());
        Assert.True(context.CallPrototypeMethod("Object", "__lookupGetter__", obj, "y").IsUndefined);
        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Object", "__defineGetter__", obj, "x", 1));

        // A data property shadows the accessor
        obj.CreateDataPropertyOrThrow("x", 1);
        Assert.True(context.CallPrototypeMethod("Object", "__lookupGetter__", obj, "x").IsUndefined);
    }

    [Fact]
    public void Boolean()
    {
        using var context = new FundamentalsTestContext();
        var boolean = context.Global("Boolean");
        Assert.True(boolean.Call(JsValue.Undefined, ["a"]).AsBoolean());
        Assert.False(boolean.Call(JsValue.Undefined, []).AsBoolean());
        var wrapper = JsOperations.Construct(boolean, [1]).AsObject();
        Assert.Equal(ObjectClass.Boolean, wrapper.Class);
        Assert.True(FundamentalsTestContext.Invoke(wrapper, "valueOf").AsBoolean());
        Assert.Equal("true", FundamentalsTestContext.Invoke(wrapper, "toString").AsString());
        Assert.Equal("false", context.CallPrototypeMethod("Boolean", "toString", false).AsString());
        Assert.False(context.CallPrototypeMethod("Boolean", "valueOf", context.Intrinsics.BooleanPrototype).AsBoolean());
        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Boolean", "valueOf", 1));
        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Boolean", "toString", context.NewObject()));
    }

    [Fact]
    public void Symbol()
    {
        using var context = new FundamentalsTestContext();
        var symbol = context.Global("Symbol");
        var s = symbol.Call(JsValue.Undefined, ["desc"]);
        Assert.Equal("desc", s.AsSymbol().Description);
        Assert.Null(symbol.Call(JsValue.Undefined, []).AsSymbol().Description);
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Construct(symbol, []));

        Assert.Equal("Symbol(desc)", context.CallPrototypeMethod("Symbol", "toString", s).AsString());
        Assert.Equal("desc", FundamentalsTestContext.Get(s, "description").AsString());
        Assert.True(FundamentalsTestContext.Get(symbol.Call(JsValue.Undefined, []), "description").IsUndefined);
        Assert.Same(s.AsSymbol(), context.CallPrototypeMethod("Symbol", "valueOf", JsOperations.ToObject(s)).AsSymbol());
        var toPrimitive = FundamentalsTestContext.Get(context.Intrinsics.SymbolPrototype, WellKnownSymbols.ToPrimitive);
        Assert.Equal("[Symbol.toPrimitive]", FundamentalsTestContext.Get(toPrimitive, "name").AsString());
        Assert.Same(s.AsSymbol(), JsOperations.Call(toPrimitive, s, ["string"]).AsSymbol());

        // Registry
        var registered = context.CallStatic("Symbol", "for", "app");
        Assert.Same(registered.AsSymbol(), context.CallStatic("Symbol", "for", "app").AsSymbol());
        Assert.True(registered.AsSymbol().IsRegistered);
        Assert.Equal("app", context.CallStatic("Symbol", "keyFor", registered).AsString());
        Assert.True(context.CallStatic("Symbol", "keyFor", s).IsUndefined);
        Assert.True(context.CallStatic("Symbol", "keyFor", WellKnownSymbols.Iterator).IsUndefined);
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Symbol", "keyFor", "app"));

        // The registry is shared by the realms of the engine
        var otherRealm = context.Engine.CreateRealm();
        var otherFor = otherRealm.GlobalObject.Get("Symbol").AsObject().Get("for");
        Assert.Same(registered.AsSymbol(), JsOperations.Call(otherFor, JsValue.Undefined, ["app"]).AsSymbol());

        // Well-known symbols
        Assert.Same(WellKnownSymbols.Iterator, FundamentalsTestContext.Get(symbol, "iterator").AsSymbol());
        Assert.True(symbol.GetOwnProperty("asyncDispose", out var descriptor));
        Assert.Equal(PropertyFlags.None, descriptor.Flags);
    }

    [Fact]
    public void Reflect()
    {
        using var context = new FundamentalsTestContext();
        var obj = context.NewObject();
        Assert.True(context.CallStatic("Reflect", "set", obj, "a", 1).AsBoolean());
        Assert.Equal(1, context.CallStatic("Reflect", "get", obj, "a").AsNumber());
        Assert.True(context.CallStatic("Reflect", "has", obj, "toString").AsBoolean());
        Assert.True(context.CallStatic("Reflect", "deleteProperty", obj, "a").AsBoolean());
        Assert.False(context.CallStatic("Reflect", "has", obj, "a").AsBoolean());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Reflect", "get", 1, "a"));

        var descriptor = context.NewObject();
        descriptor.CreateDataPropertyOrThrow("value", 1);
        Assert.True(context.CallStatic("Reflect", "defineProperty", obj, "x", descriptor).AsBoolean());
        Assert.False(context.CallStatic("Reflect", "defineProperty", obj, "x", context.NewObject().Also(o => o.CreateDataPropertyOrThrow("value", 2))).AsBoolean());
        Assert.True(context.CallStatic("Reflect", "preventExtensions", obj).AsBoolean());
        Assert.False(context.CallStatic("Reflect", "isExtensible", obj).AsBoolean());
        Assert.False(context.CallStatic("Reflect", "setPrototypeOf", obj, JsValue.Null).AsBoolean());

        var keys = (JsArray)context.CallStatic("Reflect", "ownKeys", context.NewArray(1)).AsObject();
        Assert.Equal(["0", "length"], ToStrings(keys));

        // Receiver of get/set
        var receiver = context.NewObject();
        var accessorTarget = context.NewObject();
        accessorTarget.DefineBuiltInAccessor("x", context.Function((thisValue, _) => thisValue), setter: null);
        Assert.Same(receiver, context.CallStatic("Reflect", "get", accessorTarget, "x", receiver).AsObject());

        // apply and construct
        var function = context.Function((thisValue, args) => args.Length, isConstructor: false);
        Assert.Equal(2, context.CallStatic("Reflect", "apply", function, JsValue.Undefined, context.NewArray(1, 2)).AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Reflect", "apply", function, JsValue.Undefined));
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Reflect", "construct", function, context.NewArray()));
        var created = context.CallStatic("Reflect", "construct", context.Global("Boolean"), context.NewArray(1), context.Global("Object")).AsObject();
        Assert.Equal(ObjectClass.Boolean, created.Class);
        Assert.Same(context.Intrinsics.ObjectPrototype, created.GetPrototypeOf());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Reflect", "construct", context.Global("Boolean"), context.NewArray(), function));
    }

    private static string[] ToStrings(JsArray array)
    {
        return Enumerable.Range(0, (int)array.Length).Select(i => array.Get(new PropertyKey((uint)i)).AsString()).ToArray();
    }
}

internal static class ObjectTestExtensions
{
    public static JsObject Also(this JsObject obj, Action<JsObject> action)
    {
        action(obj);
        return obj;
    }
}
