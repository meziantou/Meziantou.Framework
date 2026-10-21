namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class ProxyTests
{
    [Fact]
    public void Constructor()
    {
        using var context = new FundamentalsTestContext();
        var proxyConstructor = context.Global("Proxy");
        Assert.False(proxyConstructor.HasOwnProperty("prototype"));
        Assert.Equal(2, proxyConstructor.Get("length").AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => proxyConstructor.Call(JsValue.Undefined, [context.NewObject(), context.NewObject()]));
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Construct(proxyConstructor, [1, context.NewObject()]));
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Construct(proxyConstructor, [context.NewObject(), JsValue.Null]));

        var target = context.NewObject();
        var handler = context.NewObject();
        var proxy = (JsProxy)JsOperations.Construct(proxyConstructor, [target, handler]).AsObject();
        Assert.Same(target, proxy.Target);
        Assert.Same(handler, proxy.Handler);
        Assert.False(proxy.IsRevoked);
        Assert.False(proxy.IsCallable);

        var callable = context.NewProxy(context.Global("Object")).AsObject();
        Assert.True(callable.IsCallable);
        Assert.True(callable.IsConstructor);
        Assert.Equal("function", JsOperations.TypeOf(callable));

        var callableNotConstructor = context.NewProxy(context.Intrinsics.FunctionPrototype.Get("call")).AsObject();
        Assert.True(callableNotConstructor.IsCallable);
        Assert.False(callableNotConstructor.IsConstructor);
    }

    [Fact]
    public void ForwardsToTheTargetWithoutTraps()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewArray(1, 2);
        var proxy = context.NewProxy(target).AsObject();
        Assert.Equal(2, proxy.Get("length").AsNumber());
        Assert.True(proxy.Set(new PropertyKey(2u), 3, proxy));
        Assert.Equal(3u, target.Length);
        Assert.False(proxy.HasProperty("missing"));
        Assert.Equal(["0", "1", "2", "length"], proxy.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
        Assert.True(proxy.Delete(new PropertyKey(0u)));
        Assert.False(target.HasOwnProperty(new PropertyKey(0u)));
        Assert.True(JsOperations.IsArray(proxy));
        Assert.True(proxy.PreventExtensions());
        Assert.False(target.IsExtensible());
    }

    [Fact]
    public void Revocable()
    {
        using var context = new FundamentalsTestContext();
        var result = context.CallStatic("Proxy", "revocable", context.NewObject(), context.NewObject()).AsObject();
        Assert.Equal(["proxy", "revoke"], result.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
        var proxy = (JsProxy)result.Get("proxy").AsObject();
        var revoke = result.Get("revoke").AsObject();
        Assert.Equal("", revoke.Get("name").AsString());
        Assert.Equal(0, revoke.Get("length").AsNumber());
        Assert.False(revoke.IsConstructor);

        Assert.True(revoke.Call(JsValue.Undefined, []).IsUndefined);
        Assert.True(proxy.IsRevoked);
        Assert.Null(proxy.Target);
        Assert.True(revoke.Call(JsValue.Undefined, []).IsUndefined);

        context.AssertThrows(ErrorType.TypeError, () => proxy.Get("x"));
        context.AssertThrows(ErrorType.TypeError, () => proxy.GetPrototypeOf());
        context.AssertThrows(ErrorType.TypeError, () => proxy.OwnPropertyKeys());
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.IsArray(proxy));
        context.AssertThrows(ErrorType.TypeError, () => ObjectOperations.GetFunctionRealm(proxy));
    }

    [Fact]
    public void TrapsReceiveTheArguments()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        JsValue[]? received = null;
        var proxy = context.NewProxy(target, ("get", args =>
        {
            received = args;
            return 42;
        })).AsObject();
        var receiver = context.NewObject();
        Assert.Equal(42, proxy.Get("x", receiver).AsNumber());
        Assert.NotNull(received);
        Assert.HasCount(3, received);
        Assert.Same(target, received[0].AsObject());
        Assert.Equal("x", received[1].AsString());
        Assert.Same(receiver, received[2].AsObject());

        // The key of an array index is a string
        proxy.Get(new PropertyKey(1u), proxy);
        Assert.Equal("1", received[1].AsString());

        // A proxy in the prototype chain receives the original receiver
        var child = new JsObject(proxy);
        Assert.Equal(42, child.Get("y").AsNumber());
        Assert.Same(child, received[2].AsObject());
    }

    [Fact]
    public void TrapThatIsNotCallableThrows()
    {
        using var context = new FundamentalsTestContext();
        var handler = context.NewObject();
        handler.CreateDataPropertyOrThrow("get", 1);
        var proxy = JsOperations.Construct(context.Global("Proxy"), [context.NewObject(), handler]).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => proxy.Get("x"));

        // null and undefined forward to the target
        handler.CreateDataPropertyOrThrow("get", JsValue.Null);
        Assert.True(proxy.Get("x").IsUndefined);
    }

    [Fact]
    public void GetInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        target.DefineBuiltIn("fixed", 1, PropertyFlags.None);
        target.DefineBuiltInAccessor("noGetter", getter: null, setter: context.Function((_, _) => JsValue.Undefined), PropertyFlags.None);
        target.DefineBuiltIn("writable", 1, PropertyFlags.Writable);
        var proxy = context.NewProxy(target, ("get", _ => 2)).AsObject();

        context.AssertThrows(ErrorType.TypeError, () => proxy.Get("fixed"));
        context.AssertThrows(ErrorType.TypeError, () => proxy.Get("noGetter"));
        Assert.Equal(2, proxy.Get("writable").AsNumber());
        Assert.Equal(2, proxy.Get("other").AsNumber());

        var sameValue = context.NewProxy(target, ("get", args => args[1].AsString() == "fixed" ? 1 : JsValue.Undefined)).AsObject();
        Assert.Equal(1, sameValue.Get("fixed").AsNumber());
        Assert.True(sameValue.Get("noGetter").IsUndefined);
    }

    [Fact]
    public void SetInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        target.DefineBuiltIn("fixed", 1, PropertyFlags.None);
        target.DefineBuiltInAccessor("noSetter", context.Function((_, _) => 1), setter: null, PropertyFlags.None);
        var proxy = context.NewProxy(target, ("set", _ => true)).AsObject();

        context.AssertThrows(ErrorType.TypeError, () => proxy.Set("fixed", 2, proxy));
        Assert.True(proxy.Set("fixed", 1, proxy));
        context.AssertThrows(ErrorType.TypeError, () => proxy.Set("noSetter", 2, proxy));
        Assert.True(proxy.Set("other", 2, proxy));
        Assert.False(target.HasOwnProperty("other"));

        var falsish = context.NewProxy(target, ("set", _ => 0)).AsObject();
        Assert.False(falsish.Set("fixed", 2, falsish));
    }

    [Fact]
    public void HasAndDeleteInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        target.DefineBuiltIn("fixed", 1, PropertyFlags.None);
        target.CreateDataPropertyOrThrow("configurable", 1);
        var hasFalse = context.NewProxy(target, ("has", _ => false)).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => hasFalse.HasProperty("fixed"));
        Assert.False(hasFalse.HasProperty("configurable"));

        var deleteTrue = context.NewProxy(target, ("deleteProperty", _ => true)).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => deleteTrue.Delete("fixed"));
        Assert.True(deleteTrue.Delete("configurable"));
        Assert.True(target.HasOwnProperty("configurable"));

        target.PreventExtensions();
        context.AssertThrows(ErrorType.TypeError, () => hasFalse.HasProperty("configurable"));
        context.AssertThrows(ErrorType.TypeError, () => deleteTrue.Delete("configurable"));
        Assert.True(deleteTrue.Delete("missing"));
    }

    [Fact]
    public void ExtensibilityInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        var lies = context.NewProxy(target, ("isExtensible", _ => false), ("preventExtensions", _ => true)).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => lies.IsExtensible());
        context.AssertThrows(ErrorType.TypeError, () => lies.PreventExtensions());

        var refuses = context.NewProxy(target, ("preventExtensions", _ => false)).AsObject();
        Assert.False(refuses.PreventExtensions());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "preventExtensions", refuses));
        Assert.False(context.CallStatic("Reflect", "preventExtensions", refuses).AsBoolean());

        target.PreventExtensions();
        Assert.True(lies.PreventExtensions());
        Assert.False(lies.IsExtensible());
    }

    [Fact]
    public void PrototypeInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        var other = context.NewObject();
        var proxy = context.NewProxy(target, ("getPrototypeOf", _ => other), ("setPrototypeOf", _ => true)).AsObject();
        Assert.Same(other, proxy.GetPrototypeOf());
        Assert.True(proxy.SetPrototypeOf(null));
        Assert.Same(context.Intrinsics.ObjectPrototype, target.GetPrototypeOf());

        var invalid = context.NewProxy(target, ("getPrototypeOf", _ => 1)).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => invalid.GetPrototypeOf());

        target.PreventExtensions();
        context.AssertThrows(ErrorType.TypeError, () => proxy.GetPrototypeOf());
        context.AssertThrows(ErrorType.TypeError, () => proxy.SetPrototypeOf(null));
        Assert.True(proxy.SetPrototypeOf(context.Intrinsics.ObjectPrototype));

        // isPrototypeOf uses the trap
        var child = new JsObject(context.NewProxy(context.NewObject(), ("getPrototypeOf", _ => other)).AsObject());
        Assert.True(context.CallPrototypeMethod("Object", "isPrototypeOf", other, child).AsBoolean());
    }

    [Fact]
    public void GetOwnPropertyDescriptorInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        target.DefineBuiltIn("fixed", 1, PropertyFlags.None);
        target.DefineBuiltIn("fixedWritable", 1, PropertyFlags.Writable);
        target.CreateDataPropertyOrThrow("configurable", 1);

        JsValue Descriptor(bool configurable, bool writable)
        {
            var obj = context.NewObject();
            obj.CreateDataPropertyOrThrow("value", 1);
            obj.CreateDataPropertyOrThrow("configurable", configurable);
            obj.CreateDataPropertyOrThrow("writable", writable);
            return obj;
        }

        var returnsUndefined = context.NewProxy(target, ("getOwnPropertyDescriptor", _ => JsValue.Undefined)).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => returnsUndefined.GetOwnProperty("fixed", out _));
        Assert.False(returnsUndefined.GetOwnProperty("configurable", out _));

        var returnsPrimitive = context.NewProxy(target, ("getOwnPropertyDescriptor", _ => 1)).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => returnsPrimitive.GetOwnProperty("configurable", out _));

        // Non-configurable for a configurable or missing property
        var nonConfigurable = context.NewProxy(target, ("getOwnPropertyDescriptor", _ => Descriptor(configurable: false, writable: true))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => nonConfigurable.GetOwnProperty("configurable", out _));
        context.AssertThrows(ErrorType.TypeError, () => nonConfigurable.GetOwnProperty("missing", out _));
        Assert.True(nonConfigurable.GetOwnProperty("fixedWritable", out var descriptor));
        Assert.True(descriptor.Writable);

        // Non-configurable non-writable for a non-configurable writable property
        var nonWritable = context.NewProxy(target, ("getOwnPropertyDescriptor", _ => Descriptor(configurable: false, writable: false))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => nonWritable.GetOwnProperty("fixedWritable", out _));
        Assert.True(nonWritable.GetOwnProperty("fixed", out descriptor));
        Assert.True(descriptor.HasEnumerable);
        Assert.False(descriptor.Enumerable);

        // Incompatible with the target (writable for a non-writable property)
        var incompatible = context.NewProxy(target, ("getOwnPropertyDescriptor", _ => Descriptor(configurable: false, writable: true))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => incompatible.GetOwnProperty("fixed", out _));

        // A new property on a non-extensible target
        var configurable = context.NewProxy(target, ("getOwnPropertyDescriptor", _ => Descriptor(configurable: true, writable: true))).AsObject();
        Assert.True(configurable.GetOwnProperty("missing", out _));
        target.PreventExtensions();
        context.AssertThrows(ErrorType.TypeError, () => configurable.GetOwnProperty("missing", out _));
        context.AssertThrows(ErrorType.TypeError, () => returnsUndefined.GetOwnProperty("configurable", out _));
    }

    [Fact]
    public void DefinePropertyInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        target.DefineBuiltIn("fixed", 1, PropertyFlags.None);
        target.DefineBuiltIn("fixedWritable", 1, PropertyFlags.Writable);
        target.CreateDataPropertyOrThrow("configurable", 1);
        JsValue[]? received = null;
        var proxy = context.NewProxy(target, ("defineProperty", args =>
        {
            received = args;
            return true;
        })).AsObject();

        var nonConfigurable = new PropertyDescriptor { Fields = DescriptorFields.Configurable };
        context.AssertThrows(ErrorType.TypeError, () => proxy.DefineOwnProperty("missing", nonConfigurable));
        context.AssertThrows(ErrorType.TypeError, () => proxy.DefineOwnProperty("configurable", nonConfigurable));
        Assert.True(proxy.DefineOwnProperty("fixed", nonConfigurable));

        // The descriptor object has only the present fields
        Assert.NotNull(received);
        Assert.Equal(["configurable"], received[2].AsObject().OwnPropertyKeys().Select(k => k.ToString()).ToArray());

        var nonWritable = new PropertyDescriptor { Fields = DescriptorFields.Writable };
        context.AssertThrows(ErrorType.TypeError, () => proxy.DefineOwnProperty("fixedWritable", nonWritable));
        context.AssertThrows(ErrorType.TypeError, () => proxy.DefineOwnProperty("fixed", PropertyDescriptor.Data(2, PropertyFlags.None)));

        Assert.True(proxy.DefineOwnProperty("missing", PropertyDescriptor.Data(1)));
        target.PreventExtensions();
        context.AssertThrows(ErrorType.TypeError, () => proxy.DefineOwnProperty("missing", PropertyDescriptor.Data(1)));

        var falsish = context.NewProxy(target, ("defineProperty", _ => false)).AsObject();
        Assert.False(falsish.DefineOwnProperty("missing", PropertyDescriptor.Data(1)));
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Object", "defineProperty", falsish, "missing", context.NewObject()));
    }

    [Fact]
    public void OwnKeysInvariants()
    {
        using var context = new FundamentalsTestContext();
        var target = context.NewObject();
        target.DefineBuiltIn("fixed", 1, PropertyFlags.None);
        target.CreateDataPropertyOrThrow("configurable", 1);

        JsValue OwnKeys(params JsValue[] keys) => context.NewArray(keys);

        var duplicates = context.NewProxy(target, ("ownKeys", _ => OwnKeys("fixed", "fixed"))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => duplicates.OwnPropertyKeys());

        var notAKey = context.NewProxy(target, ("ownKeys", _ => OwnKeys("fixed", 1))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => notAKey.OwnPropertyKeys());

        var notAnObject = context.NewProxy(target, ("ownKeys", _ => "fixed")).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => notAnObject.OwnPropertyKeys());

        var missingNonConfigurable = context.NewProxy(target, ("ownKeys", _ => OwnKeys("configurable"))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => missingNonConfigurable.OwnPropertyKeys());

        var extra = context.NewProxy(target, ("ownKeys", _ => OwnKeys("fixed", "extra", "1"))).AsObject();
        var keys = extra.OwnPropertyKeys();
        Assert.Equal(["fixed", "extra", "1"], keys.Select(k => k.ToString()).ToArray());
        Assert.True(keys[2].IsArrayIndex);

        // A non-extensible target: exactly the keys of the target
        target.PreventExtensions();
        context.AssertThrows(ErrorType.TypeError, () => extra.OwnPropertyKeys());
        var missingConfigurable = context.NewProxy(target, ("ownKeys", _ => OwnKeys("fixed"))).AsObject();
        context.AssertThrows(ErrorType.TypeError, () => missingConfigurable.OwnPropertyKeys());
        var exact = context.NewProxy(target, ("ownKeys", _ => OwnKeys("configurable", "fixed"))).AsObject();
        Assert.Equal(["configurable", "fixed"], exact.OwnPropertyKeys().Select(k => k.ToString()).ToArray());

        // Object.keys reads the descriptors of the trap result
        var keysResult = (JsArray)context.CallStatic("Object", "keys", exact).AsObject();
        Assert.Equal(1u, keysResult.Length);
        Assert.Equal("configurable", keysResult.Get(new PropertyKey(0u)).AsString());
    }

    [Fact]
    public void CallAndConstructTraps()
    {
        using var context = new FundamentalsTestContext();
        var target = context.Function((_, _) => "target", isConstructor: true);
        JsValue[]? received = null;
        var proxy = context.NewProxy(target, ("apply", args =>
        {
            received = args;
            return "apply";
        }), ("construct", args =>
        {
            received = args;
            return args[1].AsObject().Get(new PropertyKey(0u));
        })).AsObject();

        var thisValue = context.NewObject();
        Assert.Equal("apply", proxy.Call(thisValue, [1, 2]).AsString());
        Assert.NotNull(received);
        Assert.Same(target, received[0].AsObject());
        Assert.Same(thisValue, received[1].AsObject());
        Assert.Equal(2u, ((JsArray)received[2].AsObject()).Length);

        var result = context.NewObject();
        Assert.Same(result, JsOperations.Construct(proxy, [result]).AsObject());
        Assert.Same(proxy, received[2].AsObject());
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Construct(proxy, [1]));

        // Without traps
        var forward = context.NewProxy(target).AsObject();
        Assert.Equal("target", forward.Call(JsValue.Undefined, []).AsString());
        Assert.Same(context.Realm, ObjectOperations.GetFunctionRealm(forward));
    }

    [Fact]
    public void DeepProxyChainThrowsRangeError()
    {
        using var context = new FundamentalsTestContext();
        JsObject obj = context.NewObject();
        var handler = context.NewObject();
        for (var i = 0; i < 100_000; i++)
        {
            obj = JsProxy.Create(context.Engine, obj, handler);
        }

        context.AssertThrows(ErrorType.RangeError, () => obj.Get("x"));
        context.AssertThrows(ErrorType.RangeError, () => obj.GetPrototypeOf());
        Assert.False(JsOperations.IsArray(obj));
    }

    [Fact]
    public void ProxyAsHandlerOfAnotherProxy()
    {
        using var context = new FundamentalsTestContext();
        var log = new List<string>();
        var handlerTarget = context.NewObject();
        var metaHandler = context.NewProxy(handlerTarget, ("get", args =>
        {
            log.Add(args[1].AsString());
            return JsValue.Undefined;
        }));
        var proxy = JsOperations.Construct(context.Global("Proxy"), [context.NewObject(), metaHandler]).AsObject();
        proxy.Get("x");
        proxy.HasProperty("x");
        proxy.OwnPropertyKeys();
        Assert.Equal(["get", "has", "ownKeys"], log);
    }
}
