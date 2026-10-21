namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class IteratorBuiltinsTests
{
    private sealed class TestIterator
    {
        public required JsObject Object { get; init; }

        public int NextCalls { get; set; }

        public int ReturnCalls { get; set; }
    }

    /// <summary>An iterator (inheriting from Iterator.prototype) over the values, which counts the calls of next and return.</summary>
    private static TestIterator CreateIterator(BuiltinsTestContext context, params JsValue[] values)
    {
        var obj = new JsObject(context.Realm.Intrinsics.IteratorPrototype);
        var iterator = new TestIterator { Object = obj };
        var index = 0;
        obj.CreateDataPropertyOrThrow("next", context.Function((_, _) =>
        {
            iterator.NextCalls++;
            if (index < values.Length)
                return IteratorOperations.CreateIteratorResultObject(context.Realm, values[index++], done: false);

            return IteratorOperations.CreateIteratorResultObject(context.Realm, JsValue.Undefined, done: true);
        }));
        obj.CreateDataPropertyOrThrow("return", context.Function((_, _) =>
        {
            iterator.ReturnCalls++;
            return context.Object();
        }));
        return iterator;
    }

    private static JsValue Next(JsValue iterator) => BuiltinsTestContext.Invoke(iterator, "next");

    private static JsValue ValueOf(JsValue result) => BuiltinsTestContext.Get(result, "value");

    private static bool DoneOf(JsValue result) => BuiltinsTestContext.Get(result, "done").AsBoolean();

    [Fact]
    public void Constructor_IsAbstract()
    {
        using var context = new BuiltinsTestContext();
        var iterator = context.Global("Iterator");
        context.AssertThrows("TypeError", () => iterator.Call(JsValue.Undefined, []));
        context.AssertThrows("TypeError", () => JsOperations.Construct(iterator, []));

        var subclass = context.Global("Array");
        var instance = JsOperations.Construct(iterator, [], subclass).AsObject();
        Assert.Same(context.Realm.Intrinsics.ArrayPrototype, instance.GetPrototypeOf());
    }

    [Fact]
    public void Prototype_WeirdAccessors()
    {
        using var context = new BuiltinsTestContext();
        var prototype = context.Realm.Intrinsics.IteratorPrototype;
        Assert.True(prototype.GetOwnProperty(new PropertyKey(WellKnownSymbols.ToStringTag), out var descriptor));
        Assert.True(descriptor.IsAccessorDescriptor);
        Assert.Equal("Iterator", prototype.Get(new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
        Assert.Same(context.Global("Iterator"), prototype.Get(new PropertyKey("constructor")).AsObject());

        // The setter throws on the home object, and defines an own property on the other objects
        context.AssertThrows("TypeError", () => prototype.Set(new PropertyKey(WellKnownSymbols.ToStringTag), "x", throwOnFailure: true));
        var derived = new JsObject(prototype);
        derived.Set(new PropertyKey(WellKnownSymbols.ToStringTag), "x", throwOnFailure: true);
        Assert.True(derived.GetOwnProperty(new PropertyKey(WellKnownSymbols.ToStringTag), out var own));
        Assert.Equal("x", own.Value.AsString());
        Assert.Equal("Iterator", prototype.Get(new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
    }

    [Fact]
    public void Map_YieldsAndCloses()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2, 3);
        var mapped = BuiltinsTestContext.Invoke(source.Object, "map", context.Function((_, a) => a[0].AsNumber() * 10 + a[1].AsNumber()));
        Assert.Equal(0, source.NextCalls);
        Assert.Equal(10, ValueOf(Next(mapped)).AsNumber());
        Assert.Equal(21, ValueOf(Next(mapped)).AsNumber());

        var returned = BuiltinsTestContext.Invoke(mapped, "return");
        Assert.True(DoneOf(returned));
        Assert.Equal(1, source.ReturnCalls);
        Assert.True(DoneOf(Next(mapped)));
        Assert.Equal(2, source.NextCalls);

        // The helper is completed: return does not close the iterator again
        BuiltinsTestContext.Invoke(mapped, "return");
        Assert.Equal(1, source.ReturnCalls);
    }

    [Fact]
    public void Map_NonCallableClosesTheReceiver()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1);
        context.AssertThrows("TypeError", () => BuiltinsTestContext.Invoke(source.Object, "map", 1));
        Assert.Equal(1, source.ReturnCalls);
        Assert.Equal(0, source.NextCalls);
    }

    [Fact]
    public void Map_MapperThrowsClosesTheIterator()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2);
        var mapped = BuiltinsTestContext.Invoke(source.Object, "map", context.Function((_, _) => throw ThrowHelper.CreateException(ErrorType.SyntaxError, "boom")));
        context.AssertThrows("SyntaxError", () => Next(mapped));
        Assert.Equal(1, source.ReturnCalls);
        Assert.True(DoneOf(Next(mapped)));
    }

    [Fact]
    public void ReturnBeforeStart_ClosesTheUnderlyingIterator()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2);
        var filtered = BuiltinsTestContext.Invoke(source.Object, "filter", context.Function((_, _) => true));
        Assert.True(DoneOf(BuiltinsTestContext.Invoke(filtered, "return")));
        Assert.Equal(1, source.ReturnCalls);
        Assert.Equal(0, source.NextCalls);
        Assert.True(DoneOf(Next(filtered)));
        Assert.Equal(0, source.NextCalls);
    }

    [Fact]
    public void ExhaustionDoesNotCallReturn()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1);
        var dropped = BuiltinsTestContext.Invoke(source.Object, "drop", 0);
        Assert.Equal(1, ValueOf(Next(dropped)).AsNumber());
        Assert.True(DoneOf(Next(dropped)));
        BuiltinsTestContext.Invoke(dropped, "return");
        Assert.Equal(0, source.ReturnCalls);
    }

    [Fact]
    public void Take_ClosesWhenTheLimitIsReached()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2, 3);
        var taken = BuiltinsTestContext.Invoke(source.Object, "take", 2);
        Assert.Equal(1, ValueOf(Next(taken)).AsNumber());
        Assert.Equal(2, ValueOf(Next(taken)).AsNumber());
        Assert.Equal(0, source.ReturnCalls);
        Assert.True(DoneOf(Next(taken)));
        Assert.Equal(1, source.ReturnCalls);
        Assert.Equal(2, source.NextCalls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(9007199254740992)]
    public void TakeDrop_InvalidLimitClosesTheReceiver(double limit)
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1);
        context.AssertThrows("RangeError", () => BuiltinsTestContext.Invoke(source.Object, "take", limit));
        context.AssertThrows("RangeError", () => BuiltinsTestContext.Invoke(source.Object, "drop", limit));
        Assert.Equal(2, source.ReturnCalls);
    }

    [Fact]
    public void Drop_SkipsWithoutReadingTheValues()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2, 3);
        var dropped = BuiltinsTestContext.Invoke(source.Object, "drop", double.PositiveInfinity);
        Assert.True(DoneOf(Next(dropped)));
        Assert.Equal(4, source.NextCalls);

        var source2 = CreateIterator(context, 1, 2, 3);
        var values = BuiltinsTestContext.Iterate(BuiltinsTestContext.Invoke(source2.Object, "drop", 1));
        Assert.Equal([2.0, 3.0], values.Select(v => v.AsNumber()).ToArray());
    }

    [Fact]
    public void FlatMap_ClosesTheInnerAndOuterIterators()
    {
        using var context = new BuiltinsTestContext();
        var outer = CreateIterator(context, 1, 2);
        var inner = CreateIterator(context, "a", "b");
        var flat = BuiltinsTestContext.Invoke(outer.Object, "flatMap", context.Function((_, _) => inner.Object));
        Assert.Equal("a", ValueOf(Next(flat)).AsString());
        BuiltinsTestContext.Invoke(flat, "return");
        Assert.Equal(1, inner.ReturnCalls);
        Assert.Equal(1, outer.ReturnCalls);
    }

    [Fact]
    public void FlatMap_FlattensIterablesButNotStrings()
    {
        using var context = new BuiltinsTestContext();
        var outer = CreateIterator(context, 1, 2);
        var flat = BuiltinsTestContext.Invoke(outer.Object, "flatMap", context.Function((_, a) => context.Array(a[0], a[0])));
        Assert.Equal([1.0, 1.0, 2.0, 2.0], BuiltinsTestContext.Iterate(flat).Select(v => v.AsNumber()).ToArray());

        var outer2 = CreateIterator(context, 1);
        var flat2 = BuiltinsTestContext.Invoke(outer2.Object, "flatMap", context.Function((_, _) => "ab"));
        context.AssertThrows("TypeError", () => Next(flat2));
        Assert.Equal(1, outer2.ReturnCalls);
    }

    [Fact]
    public void Next_ThrowsWhenTheGeneratorIsRunning()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2);
        JsValue helper = default;
        helper = BuiltinsTestContext.Invoke(source.Object, "map", context.Function((_, _) =>
        {
            Next(helper);
            return 0;
        }));
        context.AssertThrows("TypeError", () => Next(helper));
        Assert.Equal(1, source.ReturnCalls);
    }

    [Fact]
    public void ConsumingMethods()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("1,2,3", BuiltinsTestContext.Format(BuiltinsTestContext.Invoke(CreateIterator(context, 1, 2, 3).Object, "toArray")));
        Assert.Equal(6, BuiltinsTestContext.Invoke(CreateIterator(context, 1, 2, 3).Object, "reduce", context.Function((_, a) => a[0].AsNumber() + a[1].AsNumber())).AsNumber());
        Assert.Equal(16, BuiltinsTestContext.Invoke(CreateIterator(context, 1, 2, 3).Object, "reduce", context.Function((_, a) => a[0].AsNumber() + a[1].AsNumber()), 10).AsNumber());
        context.AssertThrows("TypeError", () => BuiltinsTestContext.Invoke(CreateIterator(context).Object, "reduce", context.Function((_, a) => 0)));
        Assert.Equal("a-1-b", BuiltinsTestContext.Invoke(CreateIterator(context, "a", 1, "b").Object, "join", "-").AsString());
        Assert.Equal("a,,b", BuiltinsTestContext.Invoke(CreateIterator(context, "a", JsValue.Null, "b").Object, "join").AsString());

        var some = CreateIterator(context, 1, 2, 3);
        Assert.True(BuiltinsTestContext.Invoke(some.Object, "some", context.Function((_, a) => a[0].AsNumber() == 2)).AsBoolean());
        Assert.Equal(1, some.ReturnCalls);
        Assert.Equal(2, some.NextCalls);

        var every = CreateIterator(context, 1, 2, 3);
        Assert.True(BuiltinsTestContext.Invoke(every.Object, "every", context.Function((_, a) => a[0].AsNumber() > 0)).AsBoolean());
        Assert.Equal(0, every.ReturnCalls);

        var find = CreateIterator(context, 1, 2, 3);
        Assert.Equal(2, BuiltinsTestContext.Invoke(find.Object, "find", context.Function((_, a) => a[1].AsNumber() == 1)).AsNumber());
        Assert.Equal(1, find.ReturnCalls);

        var counters = new List<double>();
        BuiltinsTestContext.Invoke(CreateIterator(context, "a", "b").Object, "forEach", context.Function((_, a) =>
        {
            counters.Add(a[1].AsNumber());
            return JsValue.Undefined;
        }));
        Assert.Equal([0.0, 1.0], counters.ToArray());
    }

    [Fact]
    public void From_WrapsOrReturns()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1);
        Assert.Same(source.Object, context.CallStatic("Iterator", "from", source.Object).AsObject());

        // An iterator that does not inherit from Iterator.prototype is wrapped
        var plain = context.Object();
        plain.CreateDataPropertyOrThrow("next", source.Object.Get(new PropertyKey("next")));
        plain.CreateDataPropertyOrThrow("return", source.Object.Get(new PropertyKey("return")));
        var wrapped = context.CallStatic("Iterator", "from", plain).AsObject();
        Assert.Same(context.Realm.Intrinsics.WrapForValidIteratorPrototype, wrapped.GetPrototypeOf());
        Assert.Equal(1, ValueOf(Next(wrapped)).AsNumber());
        BuiltinsTestContext.Invoke(wrapped, "return");
        Assert.Equal(1, source.ReturnCalls);

        var fromString = context.CallStatic("Iterator", "from", "a\U0001F600");
        Assert.Equal(["a", "\U0001F600"], BuiltinsTestContext.Iterate(fromString).Select(v => v.AsString()).ToArray());
        context.AssertThrows("TypeError", () => context.CallStatic("Iterator", "from", 1));
    }

    [Fact]
    public void Concat()
    {
        using var context = new BuiltinsTestContext();
        var concat = context.CallStatic("Iterator", "concat", context.Array(1, 2), context.Array(), context.Array(3));
        Assert.Equal([1.0, 2.0, 3.0], BuiltinsTestContext.Iterate(concat).Select(v => v.AsNumber()).ToArray());
        context.AssertThrows("TypeError", () => context.CallStatic("Iterator", "concat", "abc"));

        var source = CreateIterator(context, 1, 2);
        var iterable = context.Object();
        iterable.CreateDataPropertyOrThrow(WellKnownSymbols.Iterator, context.Function((_, _) => source.Object));
        var concat2 = context.CallStatic("Iterator", "concat", iterable);
        Assert.Equal(0, source.NextCalls);
        Assert.Equal(1, ValueOf(Next(concat2)).AsNumber());
        BuiltinsTestContext.Invoke(concat2, "return");
        Assert.Equal(1, source.ReturnCalls);
    }

    [Fact]
    public void Dispose_CallsReturn()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1);
        var dispose = context.Realm.Intrinsics.IteratorPrototype.Get(new PropertyKey(WellKnownSymbols.Dispose)).AsObject();
        Assert.True(dispose.Call(source.Object, []).IsUndefined);
        Assert.Equal(1, source.ReturnCalls);
    }

    [Theory]
    [InlineData("JSON.stringify([1, 2, 3, 4, 5].values().chunks(2).toArray())", "[[1,2],[3,4],[5]]")]
    [InlineData("JSON.stringify([1, 2, 3, 4].values().windows(3).toArray())", "[[1,2,3],[2,3,4]]")]
    [InlineData("JSON.stringify([1, 2].values().windows(3).toArray())", "[]")]
    [InlineData("JSON.stringify([1, 2].values().windows(3, 'allow-partial').toArray())", "[[1,2]]")]
    [InlineData("[4, 5, NaN].values().includes(NaN) + ',' + [4, 5].values().includes(4, 1) + ',' + [4, 5].values().includes(5, 1)", "true,false,true")]
    [InlineData("JSON.stringify(Iterator.zip([[1, 2, 3], ['a', 'b']]).toArray())", "[[1,\"a\"],[2,\"b\"]]")]
    [InlineData("JSON.stringify(Iterator.zip([[1, 2, 3], ['a']], { mode: 'longest', padding: [0, 'z'] }).toArray())", "[[1,\"a\"],[2,\"z\"],[3,\"z\"]]")]
    [InlineData("try { Iterator.zip([[1, 2], [1]], { mode: 'strict' }).toArray(); 'no error' } catch (e) { e.constructor.name }", "TypeError")]
    [InlineData("JSON.stringify(Iterator.zipKeyed({ a: [1, 2], b: [3] }).toArray())", "[{\"a\":1,\"b\":3}]")]
    [InlineData("Object.getPrototypeOf(Iterator.zipKeyed({ a: [1] }).next().value)", "null")]
    public void Proposals(string code, string expected)
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal(expected, context.Realm.Evaluate(code).ToString());
    }

    [Fact]
    public void Proposals_ValidationClosesTheReceiver()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2);
        context.AssertThrows("RangeError", () => BuiltinsTestContext.Invoke(source.Object, "chunks", 0));
        context.AssertThrows("TypeError", () => BuiltinsTestContext.Invoke(source.Object, "windows", 1.5));
        context.AssertThrows("TypeError", () => BuiltinsTestContext.Invoke(source.Object, "windows", 1, "partial"));
        context.AssertThrows("TypeError", () => BuiltinsTestContext.Invoke(source.Object, "includes", 1, "1"));
        context.AssertThrows("RangeError", () => BuiltinsTestContext.Invoke(source.Object, "includes", 1, -1));
        Assert.Equal(5, source.ReturnCalls);
        Assert.Equal(0, source.NextCalls);
    }

    [Fact]
    public void Chunks_ReturnAfterTheLastPartialChunkDoesNotClose()
    {
        using var context = new BuiltinsTestContext();
        var source = CreateIterator(context, 1, 2, 3);
        var chunks = BuiltinsTestContext.Invoke(source.Object, "chunks", 2);
        Assert.Equal("1,2", BuiltinsTestContext.Format(ValueOf(Next(chunks))));
        Assert.Equal("3", BuiltinsTestContext.Format(ValueOf(Next(chunks))));
        BuiltinsTestContext.Invoke(chunks, "return");
        Assert.Equal(0, source.ReturnCalls);
    }

    [Fact]
    public void Zip_ReturnClosesAllTheOpenIterators()
    {
        using var context = new BuiltinsTestContext();
        var first = CreateIterator(context, 1, 2);
        var second = CreateIterator(context, 3, 4);
        var zip = context.CallStatic("Iterator", "zip", context.Array(first.Object, second.Object));
        Assert.Equal("1,3", BuiltinsTestContext.Format(ValueOf(Next(zip))));
        BuiltinsTestContext.Invoke(zip, "return");
        Assert.Equal(1, first.ReturnCalls);
        Assert.Equal(1, second.ReturnCalls);

        // The shortest mode closes the other iterators when one is done
        var short1 = CreateIterator(context, 1);
        var short2 = CreateIterator(context, 3, 4);
        var zip2 = context.CallStatic("Iterator", "zip", context.Array(short1.Object, short2.Object));
        BuiltinsTestContext.Iterate(zip2);
        Assert.Equal(0, short1.ReturnCalls);
        Assert.Equal(1, short2.ReturnCalls);
    }
}
