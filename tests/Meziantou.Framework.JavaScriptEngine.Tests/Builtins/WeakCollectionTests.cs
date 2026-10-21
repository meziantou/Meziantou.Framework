using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class WeakCollectionTests
{
    private static JsValue Invoke(JsValue target, string name, params JsValue[] arguments) => CollectionsTestEngine.Invoke(target, name, arguments);

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // In a separate method: in Debug builds, the local of the out argument would keep the target alive until the end of the test
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<JsObject> reference) => reference.TryGetTarget(out _);

    [Fact]
    public void WeakMap_SetGetHasDelete()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("WeakMap");
        var key = test.Object();
        var symbol = new JsSymbol("local");
        Assert.Same(map.AsObject(), Invoke(map, "set", key, 1).AsObject());
        Invoke(map, "set", key, 2);
        Invoke(map, "set", symbol, 3);
        Invoke(map, "set", WellKnownSymbols.Iterator, 4);
        Assert.Equal(2, Invoke(map, "get", key).AsNumber());
        Assert.Equal(3, Invoke(map, "get", symbol).AsNumber());
        Assert.Equal(4, Invoke(map, "get", WellKnownSymbols.Iterator).AsNumber());
        Assert.True(Invoke(map, "has", key).AsBoolean());
        Assert.True(Invoke(map, "delete", key).AsBoolean());
        Assert.False(Invoke(map, "delete", key).AsBoolean());
        Assert.False(Invoke(map, "has", key).AsBoolean());

        // Values that cannot be held weakly
        var registered = new JsSymbol("registered", isRegistered: true);
        Assert.True(Invoke(map, "get", 1).IsUndefined);
        Assert.False(Invoke(map, "has", registered).AsBoolean());
        Assert.False(Invoke(map, "delete", "a").AsBoolean());
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "set", 1, 1));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "set", registered, 1));
        test.AssertThrows(ErrorType.TypeError, () => CollectionsTestEngine.Get(test.Realm.Intrinsics.WeakMapPrototype, "get").AsObject().Call(test.New("Map"), [key]));
    }

    [Fact]
    public void WeakMap_ConstructorFromIterable()
    {
        using var test = new CollectionsTestEngine();
        var key = test.Object();
        var map = test.New("WeakMap", test.Iterable(test.Array(key, 1)));
        Assert.Equal(1, Invoke(map, "get", key).AsNumber());

        test.AssertThrows(ErrorType.TypeError, () => test.New("WeakMap", test.Iterable(test.Array(1, 1))));
        Assert.Equal(["return"], test.Log);
    }

    [Fact]
    public void WeakMap_GetOrInsert()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("WeakMap");
        var key = test.Object();
        Assert.Equal(1, Invoke(map, "getOrInsert", key, 1).AsNumber());
        Assert.Equal(1, Invoke(map, "getOrInsert", key, 2).AsNumber());
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "getOrInsert", 1, 2));

        var other = test.Object();
        Assert.Equal(3, Invoke(map, "getOrInsertComputed", other, test.Function((_, arguments) =>
        {
            Assert.Same(other, arguments[0].AsObject());
            Invoke(map, "set", other, 2);
            return 3;
        })).AsNumber());
        Assert.Equal(3, Invoke(map, "get", other).AsNumber());
        Assert.Equal(3, Invoke(map, "getOrInsertComputed", other, test.Function((_, _) => throw new InvalidOperationException())).AsNumber());

        // The callback is validated before the key
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "getOrInsertComputed", 1, test.Function((_, _) => throw new InvalidOperationException())));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "getOrInsertComputed", key, 1));
    }

    [Fact]
    public void WeakSet_AddHasDelete()
    {
        using var test = new CollectionsTestEngine();
        var value = test.Object();
        var set = test.New("WeakSet", test.Iterable(value));
        Assert.True(Invoke(set, "has", value).AsBoolean());
        Assert.Same(set.AsObject(), Invoke(set, "add", value).AsObject());
        Assert.True(Invoke(set, "delete", value).AsBoolean());
        Assert.False(Invoke(set, "has", value).AsBoolean());
        Assert.False(Invoke(set, "delete", 1).AsBoolean());
        test.AssertThrows(ErrorType.TypeError, () => Invoke(set, "add", 1));
        test.AssertThrows(ErrorType.TypeError, () => test.New("WeakSet", test.Iterable(1)));
        Assert.Equal(["return"], test.Log);
        Assert.Equal("WeakSet", CollectionsTestEngine.Get(set, WellKnownSymbols.ToStringTag).AsString());
    }

    [Fact]
    public void WeakMap_DoesNotKeepKeysAlive()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("WeakMap");
        var key = AddObjectKey(test, map);
        CollectGarbage();
        Assert.False(IsAlive(key));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<JsObject> AddObjectKey(CollectionsTestEngine test, JsValue map)
    {
        var key = test.Object();
        Invoke(map, "set", key, test.Object(("self", key)));
        return new WeakReference<JsObject>(key);
    }

    [Fact]
    public void WeakRef_Deref()
    {
        using var test = new CollectionsTestEngine();
        var target = test.Object();
        var weakRef = test.New("WeakRef", target);
        Assert.Same(target, Invoke(weakRef, "deref").AsObject());
        Assert.Equal("WeakRef", CollectionsTestEngine.Get(weakRef, WellKnownSymbols.ToStringTag).AsString());
        test.AssertThrows(ErrorType.TypeError, () => test.New("WeakRef", 1));
        test.AssertThrows(ErrorType.TypeError, () => test.New("WeakRef", new JsSymbol("registered", isRegistered: true)));
        test.AssertThrows(ErrorType.TypeError, () => test.Global("WeakRef").Call(JsValue.Undefined, [target]));
        test.AssertThrows(ErrorType.TypeError, () => CollectionsTestEngine.Get(test.Realm.Intrinsics.WeakRefPrototype, "deref").AsObject().Call(target, []));
    }

    [Fact]
    public void WeakRef_TargetIsKeptUntilClearKeptObjects()
    {
        using var test = new CollectionsTestEngine();
        var weakRef = CreateWeakRef(test, out var target);

        // The constructor added the target to the kept objects
        CollectGarbage();
        Assert.True(IsAlive(target));
        Assert.False(IsDerefUndefined(weakRef));

        // deref() kept it again; the end of a job clears the list
        test.Engine.EnqueuePromiseJob(static (_, _) => { }, null, default, test.Realm);
        test.Engine.RunJobs();
        CollectGarbage();
        Assert.False(IsAlive(target));
        Assert.True(IsDerefUndefined(weakRef));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue CreateWeakRef(CollectionsTestEngine test, out WeakReference<JsObject> target)
    {
        var obj = test.Object();
        target = new WeakReference<JsObject>(obj);
        return test.New("WeakRef", obj);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsDerefUndefined(JsValue weakRef) => Invoke(weakRef, "deref").IsUndefined;

    [Fact]
    public void FinalizationRegistry_Validation()
    {
        using var test = new CollectionsTestEngine();
        test.AssertThrows(ErrorType.TypeError, () => test.New("FinalizationRegistry", 1));
        test.AssertThrows(ErrorType.TypeError, () => test.Global("FinalizationRegistry").Call(JsValue.Undefined, [test.Function((_, _) => default)]));

        var registry = test.New("FinalizationRegistry", test.Function((_, _) => default));
        var target = test.Object();
        Assert.True(Invoke(registry, "register", target, 1).IsUndefined);
        test.AssertThrows(ErrorType.TypeError, () => Invoke(registry, "register", 1, 1));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(registry, "register", target, target));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(registry, "register", target, 1, 1));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(registry, "unregister", 1));
        Assert.False(Invoke(registry, "unregister", test.Object()).AsBoolean());

        var token = test.Object();
        Invoke(registry, "register", target, 1, token);
        Invoke(registry, "register", test.Object(), 2, token);
        Assert.True(Invoke(registry, "unregister", token).AsBoolean());
        Assert.False(Invoke(registry, "unregister", token).AsBoolean());
        Assert.Equal(1, ((JsFinalizationRegistry)registry.AsObject()).CellCount);
        Assert.Equal("FinalizationRegistry", CollectionsTestEngine.Get(registry, WellKnownSymbols.ToStringTag).AsString());
    }

    [Fact]
    public void FinalizationRegistry_CallsCleanupInAJob()
    {
        using var test = new CollectionsTestEngine();
        var heldValues = new List<string>();
        var registry = test.New("FinalizationRegistry", test.Function((thisValue, arguments) =>
        {
            Assert.True(thisValue.IsUndefined);
            heldValues.Add(arguments[0].ToString());
            return default;
        }));
        var token = test.Object();
        RegisterTargets(test, registry, token);

        // The finalizers enqueue a job; the callbacks run on this thread, from RunJobs
        for (var i = 0; i < 20 && heldValues.Count < 2; i++)
        {
            CollectGarbage();
            test.Engine.RunJobs();
        }

        heldValues.Sort(StringComparer.Ordinal);
        Assert.Equal(["first", "second"], heldValues);
        Assert.Equal(0, ((JsFinalizationRegistry)registry.AsObject()).CellCount);
        GC.KeepAlive(token);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterTargets(CollectionsTestEngine test, JsValue registry, JsObject token)
    {
        Invoke(registry, "register", test.Object(), "first");
        Invoke(registry, "register", test.Object(), "second", test.Object());
        Invoke(registry, "register", test.Object(), "unregistered", token);
        Invoke(registry, "unregister", token);
    }
}
