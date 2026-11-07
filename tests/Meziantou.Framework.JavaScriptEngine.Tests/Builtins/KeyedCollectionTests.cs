namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class KeyedCollectionTests
{
    private static JsValue Invoke(JsValue target, string name, params JsValue[] arguments) => CollectionsTestEngine.Invoke(target, name, arguments);

    private static List<string> Keys(JsValue collection) => CollectionsTestEngine.Iterate(Invoke(collection, "keys")).ConvertAll(v => v.ToString());

    private static JsValue Next(JsValue iterator) => Invoke(iterator, "next");

    private static string NextValue(JsValue iterator)
    {
        var result = Next(iterator);
        return CollectionsTestEngine.Get(result, "done").AsBoolean() ? "<done>" : CollectionsTestEngine.Get(result, "value").ToString();
    }

    private static JsValue NewMap(CollectionsTestEngine test, params JsValue[] keys)
    {
        var map = test.New("Map");
        foreach (var key in keys)
        {
            Invoke(map, "set", key, key.IsNumber ? key.AsNumber() * 10 : key);
        }

        return map;
    }

    private static JsValue NewSet(CollectionsTestEngine test, params JsValue[] values) => test.New("Set", test.Iterable(values));

    /// <summary>An object with size, has and keys, whose has and keys calls are logged.</summary>
    private static JsObject SetLike(CollectionsTestEngine test, double size, params JsValue[] values)
    {
        return test.Object(
            ("size", size),
            ("has", test.Function((_, arguments) =>
            {
                test.Log.Add("has:" + arguments[0]);
                return values.Any(v => JsOperations.SameValueZero(v, arguments[0]));
            })),
            ("keys", test.Function((_, _) =>
            {
                test.Log.Add("keys");
                return IteratorOperations.GetIterator(test.Iterable(values)).Iterator;
            })));
    }

    [Fact]
    public void Map_Properties()
    {
        using var test = new CollectionsTestEngine();
        var map = test.Global("Map");
        var prototype = test.Realm.Intrinsics.MapPrototype;
        Assert.Equal(0, CollectionsTestEngine.Get(map, "length").AsNumber());
        Assert.Same(CollectionsTestEngine.Get(prototype, "entries").AsObject(), CollectionsTestEngine.Get(prototype, WellKnownSymbols.Iterator).AsObject());
        Assert.Equal("Map", CollectionsTestEngine.Get(prototype, WellKnownSymbols.ToStringTag).AsString());
        Assert.True(prototype.GetOwnProperty(new PropertyKey("size"), out var size));
        Assert.Equal("get size", CollectionsTestEngine.Get(size.Get, "name").AsString());
        Assert.True(size.Set.IsUndefined);
        Assert.Equal(2, CollectionsTestEngine.Get(CollectionsTestEngine.Get(prototype, "getOrInsertComputed"), "length").AsNumber());
        Assert.Same(test.Realm.Intrinsics.IteratorPrototype, test.Realm.Intrinsics.MapIteratorPrototype.PrototypeSlot);
        Assert.Equal("Map Iterator", CollectionsTestEngine.Get(test.Realm.Intrinsics.MapIteratorPrototype, WellKnownSymbols.ToStringTag).AsString());
        test.AssertThrows(ErrorType.TypeError, () => map.Call(JsValue.Undefined, []));
    }

    [Fact]
    public void Map_SameValueZeroKeys()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("Map");
        Invoke(map, "set", -0d, "zero");
        Invoke(map, "set", double.NaN, "nan");
        Invoke(map, "set", "a" + new string('b', 100), "long string");
        Invoke(map, "set", JsValue.FromJsString(JsString.Concat(JsString.Create("a"), JsString.Create(new string('b', 100)))), "rope");
        Invoke(map, "set", JsValue.Undefined, "undefined");
        Invoke(map, "set", JsValue.Null, "null");
        Invoke(map, "set", "1", "string 1");
        Invoke(map, "set", 1, "number 1");

        Assert.Equal(7, CollectionsTestEngine.Get(map, "size").AsNumber());
        Assert.Equal("zero", Invoke(map, "get", 0).AsString());
        Assert.Equal("nan", Invoke(map, "get", double.NaN).AsString());
        Assert.Equal("rope", Invoke(map, "get", "a" + new string('b', 100)).AsString());
        Assert.Equal("undefined", Invoke(map, "get").AsString());
        Assert.Equal("string 1", Invoke(map, "get", "1").AsString());
        Assert.Equal("number 1", Invoke(map, "get", 1).AsString());
        Assert.True(Invoke(map, "has", JsValue.Null).AsBoolean());

        // -0 is stored as +0
        var firstKey = CollectionsTestEngine.Iterate(Invoke(map, "keys"))[0];
        Assert.False(double.IsNegative(firstKey.AsNumber()));
    }

    [Fact]
    public void Map_SetGetDeleteClear()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("Map");
        var key = test.Object();
        Assert.Same(map.AsObject(), Invoke(map, "set", key, 1).AsObject());
        Invoke(map, "set", key, 2);
        Assert.Equal(2, Invoke(map, "get", key).AsNumber());
        Assert.True(Invoke(map, "get", test.Object()).IsUndefined);
        Assert.True(Invoke(map, "delete", key).AsBoolean());
        Assert.False(Invoke(map, "delete", key).AsBoolean());
        Assert.Equal(0, CollectionsTestEngine.Get(map, "size").AsNumber());

        for (var i = 0; i < 100; i++)
        {
            Invoke(map, "set", i, i);
        }

        Assert.Equal(100, CollectionsTestEngine.Get(map, "size").AsNumber());
        Assert.True(Invoke(map, "clear").IsUndefined);
        Assert.Equal(0, CollectionsTestEngine.Get(map, "size").AsNumber());
        Assert.False(Invoke(map, "has", 5).AsBoolean());
    }

    [Fact]
    public void Map_IncompatibleReceiver_Throws()
    {
        using var test = new CollectionsTestEngine();
        var get = CollectionsTestEngine.Get(test.Realm.Intrinsics.MapPrototype, "get").AsObject();
        test.AssertThrows(ErrorType.TypeError, () => get.Call(test.New("Set"), [1]));
        test.AssertThrows(ErrorType.TypeError, () => get.Call(test.New("WeakMap"), [1]));
        test.AssertThrows(ErrorType.TypeError, () => get.Call(test.Realm.Intrinsics.MapPrototype, [1]));
        test.AssertThrows(ErrorType.TypeError, () => CollectionsTestEngine.Get(test.Realm.Intrinsics.MapPrototype, "size"));
    }

    [Fact]
    public void Map_ConstructorFromIterable()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("Map", test.Iterable(test.Array(1, "a"), test.Array(2, "b"), test.Array(1, "c")));
        Assert.Equal(["1", "2"], Keys(map));
        Assert.Equal("c", Invoke(map, "get", 1).AsString());
        Assert.Empty(test.Log);
    }

    [Fact]
    public void Map_ConstructorNonObjectEntry_ClosesIterator()
    {
        using var test = new CollectionsTestEngine();
        test.AssertThrows(ErrorType.TypeError, () => test.New("Map", test.Iterable(test.Array(1, 2), 3)));
        Assert.Equal(["return"], test.Log);
    }

    [Fact]
    public void Map_ConstructorUsesSetMethod()
    {
        using var test = new CollectionsTestEngine();
        var prototype = test.Realm.Intrinsics.MapPrototype;
        var originalSet = CollectionsTestEngine.Get(prototype, "set");
        prototype.Set(new PropertyKey("set"), test.Function((thisValue, arguments) =>
        {
            test.Log.Add("set:" + arguments[0] + "=" + arguments[1]);
            return originalSet.AsObject().Call(thisValue, arguments);
        }), throwOnFailure: true);

        var map = test.New("Map", test.Iterable(test.Array(1, 2)));
        Assert.Equal(["set:1=2"], test.Log);
        Assert.Equal(2, Invoke(map, "get", 1).AsNumber());

        prototype.Set(new PropertyKey("set"), 1, throwOnFailure: true);
        test.AssertThrows(ErrorType.TypeError, () => test.New("Map", test.Iterable()));
        Assert.Equal(0, CollectionsTestEngine.Get(test.New("Map"), "size").AsNumber());
    }

    [Fact]
    public void Map_IteratorFollowsMutations()
    {
        using var test = new CollectionsTestEngine();
        var map = NewMap(test, 1, 2, 3, 4);
        var iterator = Invoke(map, "keys");
        Assert.Equal("1", NextValue(iterator));

        Invoke(map, "delete", 2);
        Invoke(map, "set", 5, 0);
        Assert.Equal("3", NextValue(iterator));

        // Deleted then re-added: visited again at the end
        Invoke(map, "delete", 1);
        Invoke(map, "set", 1, 0);
        Assert.Equal("4", NextValue(iterator));
        Assert.Equal("5", NextValue(iterator));
        Assert.Equal("1", NextValue(iterator));
        Assert.Equal("<done>", NextValue(iterator));

        // A completed iterator stays completed
        Invoke(map, "set", 6, 0);
        Assert.Equal("<done>", NextValue(iterator));
    }

    [Fact]
    public void Map_IteratorAfterClear()
    {
        using var test = new CollectionsTestEngine();
        var map = NewMap(test, 1, 2, 3);
        var iterator = Invoke(map, "values");
        Assert.Equal("10", NextValue(iterator));
        Invoke(map, "clear");
        Invoke(map, "set", 4, 40);
        Assert.Equal("40", NextValue(iterator));
        Assert.Equal("<done>", NextValue(iterator));
    }

    [Fact]
    public void Map_IteratorSurvivesCompaction()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("Map");
        for (var i = 0; i < 100; i++)
        {
            Invoke(map, "set", i, i);
        }

        var iterator = Invoke(map, "keys");
        var iterator2 = Invoke(map, "keys");
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), NextValue(iterator));
        }

        // Delete most entries (the table shrinks), then append new ones (the table compacts or grows)
        for (var i = 5; i < 95; i++)
        {
            Invoke(map, "delete", i);
        }

        for (var i = 100; i < 200; i++)
        {
            Invoke(map, "set", i, i);
        }

        for (var i = 100; i < 190; i++)
        {
            Invoke(map, "delete", i);
        }

        var remaining = new List<string>();
        string value;
        while ((value = NextValue(iterator)) != "<done>")
        {
            remaining.Add(value);
        }

        Assert.Equal(["95", "96", "97", "98", "99", "190", "191", "192", "193", "194", "195", "196", "197", "198", "199"], remaining);
        Assert.Equal(["0", "1", "2", "3", "4", "95", "96", "97", "98", "99", "190", "191", "192", "193", "194", "195", "196", "197", "198", "199"], CollectionsTestEngine.Iterate(iterator2).ConvertAll(v => v.ToString()));
    }

    [Fact]
    public void Map_IteratorEntries()
    {
        using var test = new CollectionsTestEngine();
        var map = NewMap(test, 1);
        var entry = CollectionsTestEngine.Get(Next(Invoke(map, "entries")), "value");
        Assert.Equal([1d, 10d], CollectionsTestEngine.ToList(entry).Select(v => v.AsNumber()));

        var next = CollectionsTestEngine.Get(test.Realm.Intrinsics.MapIteratorPrototype, "next").AsObject();
        test.AssertThrows(ErrorType.TypeError, () => next.Call(Invoke(NewSet(test), "values"), []));
        test.AssertThrows(ErrorType.TypeError, () => next.Call(test.Object(), []));
    }

    [Fact]
    public void Map_ForEachFollowsMutations()
    {
        using var test = new CollectionsTestEngine();
        var map = NewMap(test, 1, 2, 3);
        var visited = new List<string>();
        JsValue thisArg = test.Object();
        Invoke(map, "forEach", test.Function((thisValue, arguments) =>
        {
            Assert.Same(thisArg.AsObject(), thisValue.AsObject());
            Assert.Same(map.AsObject(), arguments[2].AsObject());
            visited.Add(arguments[1] + "=" + arguments[0]);
            if (arguments[1].AsNumber() == 1)
            {
                Invoke(map, "delete", 2);
                Invoke(map, "set", 4, 40);
            }

            return default;
        }), thisArg);

        Assert.Equal(["1=10", "3=30", "4=40"], visited);
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "forEach", 1));
    }

    [Fact]
    public void Map_GroupBy()
    {
        using var test = new CollectionsTestEngine();
        var map = Invoke(test.Global("Map"), "groupBy", test.Iterable(1, 2, 3, 4, 5, -0d), test.Function((_, arguments) =>
        {
            test.Log.Add("index:" + arguments[1]);
            var value = arguments[0].AsNumber();
            return value == 0 ? -0d : value % 2;
        }));

        Assert.Same(test.Realm.Intrinsics.MapPrototype, map.AsObject().PrototypeSlot);
        Assert.Equal(["1", "0"], Keys(map));
        Assert.Equal([1d, 3d, 5d], CollectionsTestEngine.ToList(Invoke(map, "get", 1)).Select(v => v.AsNumber()));
        Assert.Equal([2d, 4d, 0d], CollectionsTestEngine.ToList(Invoke(map, "get", 0)).Select(v => v.AsNumber()));
        Assert.Equal(["index:0", "index:1", "index:2", "index:3", "index:4", "index:5"], test.Log);

        test.AssertThrows(ErrorType.TypeError, () => Invoke(test.Global("Map"), "groupBy", JsValue.Null, test.Function((_, _) => default)));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(test.Global("Map"), "groupBy", test.Iterable(), 1));
    }

    [Fact]
    public void Map_GroupBy_CallbackThrows_ClosesIterator()
    {
        using var test = new CollectionsTestEngine();
        Assert.Throws<JsException>(() => Invoke(test.Global("Map"), "groupBy", test.Iterable(1, 2), test.Function((_, _) => throw new JsException(new JsValue(1)))));
        Assert.Equal(["return"], test.Log);
    }

    [Fact]
    public void Map_GetOrInsert()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("Map");
        Assert.Equal(1, Invoke(map, "getOrInsert", -0d, 1).AsNumber());
        Assert.Equal(1, Invoke(map, "getOrInsert", 0, 2).AsNumber());
        Assert.Equal(1, Invoke(map, "get", 0).AsNumber());
        Assert.False(double.IsNegative(CollectionsTestEngine.Iterate(Invoke(map, "keys"))[0].AsNumber()));
    }

    [Fact]
    public void Map_GetOrInsertComputed()
    {
        using var test = new CollectionsTestEngine();
        var map = test.New("Map");
        JsValue receivedKey = default;
        Assert.Equal(5, Invoke(map, "getOrInsertComputed", -0d, test.Function((_, arguments) =>
        {
            receivedKey = arguments[0];
            return 5;
        })).AsNumber());
        Assert.False(double.IsNegative(receivedKey.AsNumber()));

        // Not called when the key is present
        Assert.Equal(5, Invoke(map, "getOrInsertComputed", 0, test.Function((_, _) => throw new InvalidOperationException())).AsNumber());

        // The value overwrites what the callback stored
        Assert.Equal(2, Invoke(map, "getOrInsertComputed", "k", test.Function((_, _) =>
        {
            Invoke(map, "set", "k", 1);
            Invoke(map, "set", "other", 1);
            return 2;
        })).AsNumber());
        Assert.Equal(2, Invoke(map, "get", "k").AsNumber());
        Assert.Equal(["0", "k", "other"], Keys(map));

        // The callback is checked before the key is looked up
        test.AssertThrows(ErrorType.TypeError, () => Invoke(map, "getOrInsertComputed", 0, 1));
    }

    [Fact]
    public void Set_Properties()
    {
        using var test = new CollectionsTestEngine();
        var prototype = test.Realm.Intrinsics.SetPrototype;
        var values = CollectionsTestEngine.Get(prototype, "values").AsObject();
        Assert.Same(values, CollectionsTestEngine.Get(prototype, "keys").AsObject());
        Assert.Same(values, CollectionsTestEngine.Get(prototype, WellKnownSymbols.Iterator).AsObject());
        Assert.Equal("values", CollectionsTestEngine.Get(values, "name").AsString());
        Assert.Equal("Set Iterator", CollectionsTestEngine.Get(test.Realm.Intrinsics.SetIteratorPrototype, WellKnownSymbols.ToStringTag).AsString());
        foreach (var name in new[] { "union", "intersection", "difference", "symmetricDifference", "isSubsetOf", "isSupersetOf", "isDisjointFrom" })
        {
            Assert.Equal(1, CollectionsTestEngine.Get(CollectionsTestEngine.Get(prototype, name), "length").AsNumber());
        }
    }

    [Fact]
    public void Set_AddHasDelete()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2, 2, -0d, double.NaN, double.NaN);
        Assert.Equal(4, CollectionsTestEngine.Get(set, "size").AsNumber());
        Assert.True(Invoke(set, "has", 0).AsBoolean());
        Assert.True(Invoke(set, "has", double.NaN).AsBoolean());
        Assert.Same(set.AsObject(), Invoke(set, "add", 3).AsObject());
        Assert.True(Invoke(set, "delete", 1).AsBoolean());
        Assert.Equal(["2", "0", "NaN", "3"], Keys(set));

        var entry = CollectionsTestEngine.Get(Next(Invoke(set, "entries")), "value");
        Assert.Equal([2d, 2d], CollectionsTestEngine.ToList(entry).Select(v => v.AsNumber()));
    }

    [Fact]
    public void Set_ForEachVisitsAddedValues()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2);
        var visited = new List<string>();
        Invoke(set, "forEach", test.Function((_, arguments) =>
        {
            visited.Add(arguments[0] + "," + arguments[1]);
            if (arguments[0].AsNumber() < 3)
            {
                Invoke(set, "add", arguments[0].AsNumber() + 2);
            }

            return default;
        }));

        Assert.Equal(["1,1", "2,2", "3,3", "4,4"], visited);
    }

    [Fact]
    public void Set_Union()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2);
        var result = Invoke(set, "union", SetLike(test, 2, 2, -0d, 3));
        Assert.Equal(["1", "2", "0", "3"], Keys(result));
        Assert.False(double.IsNegative(CollectionsTestEngine.Iterate(Invoke(result, "values"))[2].AsNumber()));
        Assert.Equal(["keys"], test.Log);
        Assert.Same(test.Realm.Intrinsics.SetPrototype, result.AsObject().PrototypeSlot);
        Assert.Equal(["1", "2"], Keys(set));
    }

    [Fact]
    public void Set_Intersection_UsesHasWhenThisIsSmaller()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2, 3);
        Assert.Equal(["1", "3"], Keys(Invoke(set, "intersection", SetLike(test, 5, 3, 1))));
        Assert.Equal(["has:1", "has:2", "has:3"], test.Log);

        test.Log.Clear();
        Assert.Equal(["3", "1"], Keys(Invoke(set, "intersection", SetLike(test, 2, 3, 1))));
        Assert.Equal(["keys"], test.Log);
    }

    [Fact]
    public void Set_Intersection_VisitsElementsAddedByHas()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1);
        var other = test.Object(
            ("size", 10),
            ("has", test.Function((_, arguments) =>
            {
                if (arguments[0].AsNumber() < 3)
                {
                    Invoke(set, "add", arguments[0].AsNumber() + 1);
                }

                return true;
            })),
            ("keys", test.Function((_, _) => throw new InvalidOperationException())));
        Assert.Equal(["1", "2", "3"], Keys(Invoke(set, "intersection", other)));
    }

    [Fact]
    public void Set_Difference()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2, 3);
        Assert.Equal(["1", "3"], Keys(Invoke(set, "difference", SetLike(test, 10, 2, 4))));
        Assert.Equal(["has:1", "has:2", "has:3"], test.Log);

        test.Log.Clear();
        Assert.Equal(["1", "3"], Keys(Invoke(set, "difference", SetLike(test, 1, 2))));
        Assert.Equal(["keys"], test.Log);
    }

    [Fact]
    public void Set_SymmetricDifference()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2, 3);
        Assert.Equal(["1", "3", "4"], Keys(Invoke(set, "symmetricDifference", SetLike(test, 3, 2, 4, 4, 2))));
    }

    [Fact]
    public void Set_IsSubsetOf()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2);
        Assert.True(Invoke(set, "isSubsetOf", SetLike(test, 3, 1, 2, 3)).AsBoolean());
        Assert.False(Invoke(set, "isSubsetOf", SetLike(test, 3, 1, 3, 4)).AsBoolean());

        // Larger than the other: false without calling has
        test.Log.Clear();
        Assert.False(Invoke(set, "isSubsetOf", SetLike(test, 1, 1, 2)).AsBoolean());
        Assert.Empty(test.Log);
    }

    [Fact]
    public void Set_IsSupersetOf_ClosesIteratorOnEarlyExit()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2, 3);
        Assert.True(Invoke(set, "isSupersetOf", SetLike(test, 2, 1, 3)).AsBoolean());
        Assert.Equal(["keys"], test.Log);

        test.Log.Clear();
        Assert.False(Invoke(set, "isSupersetOf", SetLike(test, 2, 4, 1)).AsBoolean());
        Assert.Equal(["keys", "return"], test.Log);
    }

    [Fact]
    public void Set_IsDisjointFrom()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1, 2);
        Assert.True(Invoke(set, "isDisjointFrom", SetLike(test, 5, 3, 4)).AsBoolean());
        Assert.Equal(["has:1", "has:2"], test.Log);

        test.Log.Clear();
        Assert.False(Invoke(set, "isDisjointFrom", SetLike(test, 1, 2)).AsBoolean());
        Assert.Equal(["keys", "return"], test.Log);
    }

    [Fact]
    public void Set_GetSetRecord_Validation()
    {
        using var test = new CollectionsTestEngine();
        var set = NewSet(test, 1);
        var has = test.Function((_, _) => true);
        var keys = test.Function((_, _) => default);
        test.AssertThrows(ErrorType.TypeError, () => Invoke(set, "union", 1));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(set, "union", test.Object(("has", has), ("keys", keys))));
        test.AssertThrows(ErrorType.RangeError, () => Invoke(set, "union", test.Object(("size", -1), ("has", has), ("keys", keys))));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(set, "union", test.Object(("size", 1), ("has", 1), ("keys", keys))));
        test.AssertThrows(ErrorType.TypeError, () => Invoke(set, "union", test.Object(("size", 1), ("has", has), ("keys", 1))));

        // keys() must return an object
        test.AssertThrows(ErrorType.TypeError, () => Invoke(set, "union", test.Object(("size", 1), ("has", has), ("keys", keys))));

        // An infinite size is valid
        Assert.True(Invoke(set, "isSubsetOf", test.Object(("size", double.PositiveInfinity), ("has", has), ("keys", keys))).AsBoolean());

        // A Set is a valid set-like object
        Assert.True(Invoke(set, "isSubsetOf", NewSet(test, 1, 2)).AsBoolean());
    }
}
