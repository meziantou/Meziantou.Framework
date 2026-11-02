namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class ArrayBuiltinsTests
{
    private static JsValue CallArray(BuiltinsTestContext context, string method, JsValue thisValue, params JsValue[] arguments) => context.CallPrototype("Array", method, thisValue, arguments);

    /// <summary>An array with holes: the null values of the list are holes.</summary>
    private static JsArray SparseArray(BuiltinsTestContext context, params JsValue?[] values)
    {
        var array = context.Array();
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is { } value)
            {
                array.CreateDataPropertyOrThrow(new PropertyKey((uint)i), value);
            }
        }

        array.Set(new PropertyKey("length"), values.Length, throwOnFailure: true);
        return array;
    }

    /// <summary>An ordinary object with a length and the elements: the generic paths.</summary>
    private static JsObject ArrayLike(BuiltinsTestContext context, params JsValue[] values)
    {
        var obj = context.Object();
        for (var i = 0; i < values.Length; i++)
        {
            obj.CreateDataPropertyOrThrow(new PropertyKey((uint)i), values[i]);
        }

        obj.CreateDataPropertyOrThrow("length", values.Length);
        return obj;
    }

    [Fact]
    public void Constructor()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Global("Array");
        Assert.Equal(5u, ((JsArray)array.Call(JsValue.Undefined, [5]).AsObject()).Length);
        Assert.Equal("a", BuiltinsTestContext.Format(array.Call(JsValue.Undefined, ["a"])));
        Assert.Equal("1,2", BuiltinsTestContext.Format(JsOperations.Construct(array, [1, 2])));
        context.AssertThrows("RangeError", () => array.Call(JsValue.Undefined, [-1]));
        context.AssertThrows("RangeError", () => array.Call(JsValue.Undefined, [1.5]));
        context.AssertThrows("RangeError", () => array.Call(JsValue.Undefined, [4294967296]));
        Assert.Equal(4294967295u, ((JsArray)array.Call(JsValue.Undefined, [4294967295]).AsObject()).Length);
    }

    [Fact]
    public void FromAndOf()
    {
        using var context = new BuiltinsTestContext();
        Assert.Equal("a,b", BuiltinsTestContext.Format(context.CallStatic("Array", "from", "ab")));
        Assert.Equal("x,undefined", BuiltinsTestContext.Format(context.CallStatic("Array", "from", ArrayLike(context, "x", JsValue.Undefined))));
        var mapped = context.CallStatic("Array", "from", context.Array(1, 2), context.Function((_, a) => a[0].AsNumber() * 10 + a[1].AsNumber()));
        Assert.Equal("10,21", BuiltinsTestContext.Format(mapped));
        context.AssertThrows("TypeError", () => context.CallStatic("Array", "from", context.Array(), 1));

        Assert.Equal("1,2,3", BuiltinsTestContext.Format(context.CallStatic("Array", "of", 1, 2, 3)));

        // A non-constructor this value creates an array
        var from = context.Global("Array").Get(new PropertyKey("from")).AsObject();
        Assert.IsType<JsArray>(from.Call(context.Object(), [context.Array(1)]).AsObject());
        Assert.True(context.CallStatic("Array", "isArray", context.Array()).AsBoolean());
        Assert.False(context.CallStatic("Array", "isArray", ArrayLike(context)).AsBoolean());
    }

    [Fact]
    public void Sort_IsStable()
    {
        using var context = new BuiltinsTestContext();
        var values = new JsValue[100];
        for (var i = 0; i < values.Length; i++)
        {
            var item = context.Object();
            item.CreateDataPropertyOrThrow("key", i % 3);
            item.CreateDataPropertyOrThrow("index", i);
            values[i] = item;
        }

        var array = context.Array(values);
        CallArray(context, "sort", array, context.Function((_, a) => BuiltinsTestContext.Get(a[0], "key").AsNumber() - BuiltinsTestContext.Get(a[1], "key").AsNumber()));
        var sorted = BuiltinsTestContext.ToArray(array);
        for (var i = 1; i < sorted.Length; i++)
        {
            var previousKey = BuiltinsTestContext.Get(sorted[i - 1], "key").AsNumber();
            var key = BuiltinsTestContext.Get(sorted[i], "key").AsNumber();
            Assert.True(previousKey <= key);
            if (previousKey == key)
            {
                Assert.True(BuiltinsTestContext.Get(sorted[i - 1], "index").AsNumber() < BuiltinsTestContext.Get(sorted[i], "index").AsNumber());
            }
        }
    }

    [Fact]
    public void Sort_DefaultComparisonUndefinedAndHoles()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, 10, null, JsValue.Undefined, 9, 1, null, "b", "a", 100);
        CallArray(context, "sort", array);
        Assert.Equal("1,10,100,9,a,b,undefined,<hole>,<hole>", BuiltinsTestContext.Format(array));
        Assert.Equal(9u, array.Length);

        // The code units are compared, not the code points
        var units = context.Array("\uFF61", "\U0001F600");
        CallArray(context, "sort", units);
        Assert.Equal("\U0001F600", units.Get(new PropertyKey(0)).AsString());
    }

    [Fact]
    public void Sort_Comparator()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Array(3, 1, JsValue.Undefined, 2);
        var calls = 0;
        CallArray(context, "sort", array, context.Function((_, a) =>
        {
            calls++;
            Assert.False(a[0].IsUndefined);
            Assert.False(a[1].IsUndefined);
            return a[1].AsNumber() - a[0].AsNumber();
        }));
        Assert.Equal("3,2,1,undefined", BuiltinsTestContext.Format(array));
        Assert.True(calls > 0);

        // NaN is +0: the order is kept
        var nan = context.Array(3, 1, 2);
        CallArray(context, "sort", nan, context.Function((_, _) => double.NaN));
        Assert.Equal("3,1,2", BuiltinsTestContext.Format(nan));

        context.AssertThrows("TypeError", () => CallArray(context, "sort", context.Array(), 1));

        // An exception stops the sort and leaves the array unchanged
        var unchanged = context.Array(3, 1, 2);
        context.AssertThrows("SyntaxError", () => CallArray(context, "sort", unchanged, context.Function((_, _) => throw ThrowHelper.CreateException(ErrorType.SyntaxError, "boom"))));
        Assert.Equal("3,1,2", BuiltinsTestContext.Format(unchanged));
    }

    [Fact]
    public void ToSorted_ReadsThroughHoles()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, 3, null, 1);
        var sorted = CallArray(context, "toSorted", array);
        Assert.Equal("1,3,undefined", BuiltinsTestContext.Format(sorted));
        Assert.Equal("3,<hole>,1", BuiltinsTestContext.Format(array));
    }

    [Theory]
    [InlineData(new double[] { 1, 2 }, "1,a,b,4,5", "2,3")]
    [InlineData(new double[] { 1, 0 }, "1,a,b,2,3,4,5", "")]
    [InlineData(new double[] { -2, 1 }, "1,2,3,a,b,5", "4")]
    [InlineData(new double[] { 1, 10 }, "1,a,b", "2,3,4,5")]
    [InlineData(new double[] { 10, 1 }, "1,2,3,4,5,a,b", "")]
    [InlineData(new double[] { double.NegativeInfinity, double.PositiveInfinity }, "a,b", "1,2,3,4,5")]
    public void Splice(double[] arguments, string expected, string expectedDeleted)
    {
        using var context = new BuiltinsTestContext();
        JsValue[] args = [.. arguments.Select(a => (JsValue)a), "a", "b"];

        var array = context.Array(1, 2, 3, 4, 5);
        var deleted = CallArray(context, "splice", array, args);
        Assert.Equal(expected, BuiltinsTestContext.Format(array));
        Assert.Equal(expectedDeleted, BuiltinsTestContext.Format(deleted));

        // The generic path gives the same result
        var arrayLike = ArrayLike(context, 1, 2, 3, 4, 5);
        var deletedGeneric = CallArray(context, "splice", arrayLike, args);
        Assert.Equal(expected, BuiltinsTestContext.Format(arrayLike));
        Assert.Equal(expectedDeleted, BuiltinsTestContext.Format(deletedGeneric));

        var toSpliced = CallArray(context, "toSpliced", context.Array(1, 2, 3, 4, 5), args);
        Assert.Equal(expected, BuiltinsTestContext.Format(toSpliced));
    }

    [Fact]
    public void Splice_ArgumentCounts()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Array(1, 2, 3);
        Assert.Equal("", BuiltinsTestContext.Format(CallArray(context, "splice", array)));
        Assert.Equal("1,2,3", BuiltinsTestContext.Format(array));
        Assert.Equal("2,3", BuiltinsTestContext.Format(CallArray(context, "splice", array, 1)));
        Assert.Equal("1", BuiltinsTestContext.Format(array));

        var sparse = SparseArray(context, 1, null, 3, null);
        CallArray(context, "splice", sparse, 0, 1);
        Assert.Equal("<hole>,3,<hole>", BuiltinsTestContext.Format(sparse));
    }

    [Theory]
    [InlineData(0, 3, null, "4,5,3,4,5")]
    [InlineData(1, 3, null, "1,4,5,4,5")]
    [InlineData(1, 2, null, "1,3,4,5,5")]
    [InlineData(2, 0, null, "1,2,1,2,3")]
    [InlineData(0, -2, null, "4,5,3,4,5")]
    [InlineData(-2, -3, -1.0, "1,2,3,3,4")]
    [InlineData(0, 1, 1.0, "1,2,3,4,5")]
    public void CopyWithin(double target, double start, double? end, string expected)
    {
        using var context = new BuiltinsTestContext();
        JsValue[] args = end is null ? [target, start] : [target, start, end.Value];
        Assert.Equal(expected, BuiltinsTestContext.Format(CallArray(context, "copyWithin", context.Array(1, 2, 3, 4, 5), args)));
        Assert.Equal(expected, BuiltinsTestContext.Format(CallArray(context, "copyWithin", ArrayLike(context, 1, 2, 3, 4, 5), args)));
    }

    [Fact]
    public void CopyWithin_Holes()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, 1, null, 3);
        CallArray(context, "copyWithin", array, 0, 1);
        Assert.Equal("<hole>,3,3", BuiltinsTestContext.Format(array));
    }

    [Fact]
    public void PushPopShiftUnshift()
    {
        using var context = new BuiltinsTestContext();
        foreach (JsObject target in (JsObject[])[context.Array(1, 2), ArrayLike(context, 1, 2)])
        {
            Assert.Equal(4, CallArray(context, "push", target, 3, 4).AsNumber());
            Assert.Equal(4, CallArray(context, "pop", target).AsNumber());
            Assert.Equal(1, CallArray(context, "shift", target).AsNumber());
            Assert.Equal(4, CallArray(context, "unshift", target, "a", "b").AsNumber());
            Assert.Equal("a,b,2,3", BuiltinsTestContext.Format(target));
            Assert.Equal(4, JsOperations.LengthOfArrayLike(target));
        }

        var empty = ArrayLike(context);
        Assert.True(CallArray(context, "pop", empty).IsUndefined);
        Assert.True(CallArray(context, "shift", empty).IsUndefined);

        var sparse = SparseArray(context, null, 2, null);
        Assert.True(CallArray(context, "shift", sparse).IsUndefined);
        Assert.Equal("2,<hole>", BuiltinsTestContext.Format(sparse));
        CallArray(context, "unshift", sparse, 1);
        Assert.Equal("1,2,<hole>", BuiltinsTestContext.Format(sparse));

        // Many elements: the dense paths
        var big = context.Array();
        for (var i = 0; i < 1000; i++)
        {
            CallArray(context, "push", big, i);
        }

        Assert.Equal(0, CallArray(context, "shift", big).AsNumber());
        Assert.Equal(999u, big.Length);
        Assert.Equal(999, CallArray(context, "pop", big).AsNumber());
        Assert.Equal(998u, big.Length);
        Assert.Equal(1, big.Get(new PropertyKey(0)).AsNumber());
    }

    [Fact]
    public void Push_NonWritableLength()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Array(1);
        array.DefinePropertyOrThrow("length", new PropertyDescriptor { Flags = PropertyFlags.None, Fields = DescriptorFields.Writable });
        context.AssertThrows("TypeError", () => CallArray(context, "push", array, 2));
        Assert.Equal(1u, array.Length);
        context.AssertThrows("TypeError", () => CallArray(context, "shift", array));
    }

    [Fact]
    public void Push_GenericLengthLimit()
    {
        using var context = new BuiltinsTestContext();
        var obj = context.Object();
        obj.CreateDataPropertyOrThrow("length", 9007199254740991);
        context.AssertThrows("TypeError", () => CallArray(context, "push", obj, 1));
        Assert.Equal(9007199254740991, CallArray(context, "push", obj).AsNumber());
    }

    [Fact]
    public void ConcatAndSpreadable()
    {
        using var context = new BuiltinsTestContext();
        var spreadable = ArrayLike(context, "x", "y");
        spreadable.CreateDataPropertyOrThrow(WellKnownSymbols.IsConcatSpreadable, true);
        var notSpread = context.Array(5);
        notSpread.CreateDataPropertyOrThrow(WellKnownSymbols.IsConcatSpreadable, false);
        var result = CallArray(context, "concat", SparseArray(context, 1, null), 2, spreadable, notSpread);
        var values = BuiltinsTestContext.ToArray(result);
        Assert.HasCount(6, values);
        Assert.Equal("1,<hole>,2,x,y,[object Array]", BuiltinsTestContext.Format(result));
    }

    [Fact]
    public void Species()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Array(1, 2, 3);
        var created = new List<double>();
        var species = context.Realm.CreateBuiltinFunction("Species", 1, (_, _, arguments, newTarget) =>
        {
            Assert.NotNull(newTarget);
            created.Add(arguments[0].AsNumber());
            return context.Object();
        }, isConstructor: true);
        var constructor = context.Object();
        constructor.CreateDataPropertyOrThrow(WellKnownSymbols.Species, species);
        array.CreateDataPropertyOrThrow("constructor", constructor);

        var mapped = CallArray(context, "map", array, context.Function((_, a) => a[0]));
        Assert.IsNotType<JsArray>(mapped.AsObject());
        Assert.Equal("1,2,3", string.Join(',', new[] { 0u, 1u, 2u }.Select(i => mapped.AsObject().Get(new PropertyKey(i)).ToString())));
        CallArray(context, "slice", array, 1);
        CallArray(context, "filter", array, context.Function((_, _) => true));
        Assert.Equal([3.0, 2.0, 0.0], created.ToArray());

        // A non-array uses ArrayCreate; a null species too
        Assert.IsType<JsArray>(CallArray(context, "map", ArrayLike(context, 1), context.Function((_, a) => a[0])).AsObject());
        constructor.CreateDataPropertyOrThrow(WellKnownSymbols.Species, JsValue.Null);
        Assert.IsType<JsArray>(CallArray(context, "slice", array).AsObject());
        constructor.CreateDataPropertyOrThrow(WellKnownSymbols.Species, 1);
        context.AssertThrows("TypeError", () => CallArray(context, "slice", array));
        array.CreateDataPropertyOrThrow("constructor", 1);
        context.AssertThrows("TypeError", () => CallArray(context, "slice", array));
    }

    [Fact]
    public void Species_CrossRealmArrayConstructorIsIgnored()
    {
        using var context = new BuiltinsTestContext();
        var otherRealm = context.Realm.Engine.CreateRealm();
        var otherArray = otherRealm.GlobalObject.Get(new PropertyKey("Array")).AsObject();
        var array = (JsArray)JsOperations.Construct(otherArray, [1, 2]).AsObject();
        var sliced = CallArray(context, "slice", array).AsObject();
        Assert.Same(context.Realm.Intrinsics.ArrayPrototype, sliced.GetPrototypeOf());
    }

    [Fact]
    public void SearchMethods()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, 1, null, double.NaN, 1, JsValue.Undefined);
        Assert.Equal(3, CallArray(context, "indexOf", array, 1, 1).AsNumber());
        Assert.Equal(0, CallArray(context, "lastIndexOf", array, 1, -3).AsNumber());
        Assert.Equal(-1, CallArray(context, "lastIndexOf", array, 1, double.NegativeInfinity).AsNumber());
        Assert.Equal(3, CallArray(context, "lastIndexOf", array, 1).AsNumber());
        Assert.Equal(-1, CallArray(context, "indexOf", array, double.NaN).AsNumber());
        Assert.True(CallArray(context, "includes", array, double.NaN).AsBoolean());
        Assert.Equal(4, CallArray(context, "indexOf", array, JsValue.Undefined).AsNumber());

        var holes = SparseArray(context, 1, null);
        Assert.True(CallArray(context, "includes", holes, JsValue.Undefined).AsBoolean());
        Assert.Equal(-1, CallArray(context, "indexOf", holes, JsValue.Undefined).AsNumber());

        var found = CallArray(context, "findLast", context.Array(1, 2, 3, 4), context.Function((_, a) => a[0].AsNumber() % 2 == 1));
        Assert.Equal(3, found.AsNumber());
        Assert.Equal(-1, CallArray(context, "findIndex", context.Array(1), context.Function((_, _) => false)).AsNumber());
        Assert.Equal("c", CallArray(context, "at", context.Array("a", "b", "c"), -1).AsString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Sparse_PowersOfTwo_StaySparse(string initializer)
    {
        // The reverse mappings of a flags enum compiled by TypeScript: each write is at the end of the storage
        using var engine = new JsEngine();
        var result = engine.CreateRealm().Execute("var E = " + initializer + "; for (var i = 0; i < 30; i++) E[E['F' + i] = 2 ** i] = 'F' + i; E");
        using (engine.Enter())
        {
            var obj = result.AsObject();
            Assert.True(obj.DenseCapacity <= 4096, $"DenseCapacity: {obj.DenseCapacity}");
            Assert.Equal("F29", obj.Get(new PropertyKey(1u << 29)).AsString());
            Assert.Equal("F0", obj.Get(new PropertyKey(1)).AsString());
        }
    }

    [Fact]
    public void Dense_SequentialWritesWithGaps_StayDense()
    {
        using var engine = new JsEngine();
        var result = engine.CreateRealm().Execute("var a = []; for (var i = 0; i < 100000; i += 4) a[i] = i; a");
        using (engine.Enter())
        {
            var array = result.AsObject();
            Assert.True(array.DenseCapacity >= 99997, $"DenseCapacity: {array.DenseCapacity}");
            Assert.Equal(99996, array.Get(new PropertyKey(99996)).AsNumber());
        }
    }

    [Fact]
    public void Sparse_Iteration_ScalesLinearly()
    {
        // Each step used to scan all the indices of the property table
        var result = BuiltinsTestContext.ExecuteWithinBudget("""
            const a = [];
            for (let i = 0; i < 50000; i++) a[i * 100] = 1;
            let count = 0;
            a.forEach(() => count++);
            const sum = a.reduce((s, v) => s + v, 0);
            const right = a.reduceRight((s, v) => s + v, 0);
            const mapped = a.map(v => v * 2);
            [count, sum, right, mapped[4999900], a.indexOf(2), a.lastIndexOf(1), Object.keys(a).length].join()
            """);
        Assert.Equal("50000,50000,50000,2,-1,4999900,50000", result);
    }

    [Fact]
    public void Sparse_Iteration_SeesTheMutationsOfTheCallback()
    {
        using var engine = new JsEngine();
        var result = engine.CreateRealm().Execute("""
            const a = [];
            for (let i = 0; i < 10; i++) a[i * 1000] = i;
            const visited = [];
            a.forEach((v, i) => {
              visited.push(i);
              if (i === 2000) { delete a[3000]; a[2500] = 'added'; a[100000] = 'after'; }
              if (i === 5000) a[4000] = 'behind';
            });
            const backward = [];
            a.reduceRight((acc, v, i) => { backward.push(i); if (i === 8000) delete a[7000]; if (i === 6000) a[6500] = 'x'; return acc; }, 0);
            visited.join() + ' / ' + backward.join()
            """);
        Assert.Equal(
            "0,1000,2000,2500,4000,5000,6000,7000,8000,9000 / 100000,9000,8000,6000,5000,4000,2500,2000,1000,0",
            engine.ToString(result));
    }

    [Fact]
    public void Sparse_HugeLength_ReverseAndJoin()
    {
        // The pairs whose elements are both holes are skipped (the length is 2^32 - 1)
        var result = BuiltinsTestContext.ExecuteWithinBudget("""
            const a = [];
            a[4294967294] = 'last';
            a[3] = 'three';
            const joined = a.join('');
            a.reverse();
            const b = [];
            b[4294967294] = 1;
            Object.defineProperty(b, 5, { get() { b[10] = 'added'; return 'getter'; }, configurable: true });
            b.reverse();
            [joined, a[0], a[4294967291], 3 in a, 4294967294 in a, Object.keys(a).length, b[4294967289], b[4294967284], b[0], Object.keys(b).length].join()
            """);
        Assert.Equal("threelast,last,three,false,false,2,getter,added,1,3", result);
    }

    [Fact]
    public void Packed_ShiftUnshiftSplice_ScaleLinearly()
    {
        // Each operation used to check every element and move all of them
        var result = BuiltinsTestContext.ExecuteWithinBudget("""
            const queue = [];
            for (let i = 0; i < 100000; i++) queue.push(i);
            let sum = 0;
            while (queue.length) sum += queue.shift();
            const front = [];
            for (let i = 0; i < 50000; i++) front.unshift(i);
            const spliced = [];
            for (let i = 0; i < 50000; i++) spliced.push(i);
            for (let i = 0; i < 25000; i++) spliced.splice(0, 1);
            for (let i = 0; i < 25000; i++) spliced.splice(1, 0, -i);
            [sum, front.length, front[0], front[49999], spliced.length, spliced[0], spliced[1], spliced[25000], spliced[49999]].join()
            """);
        Assert.Equal("4999950000,50000,49999,0,50000,25000,-24999,0,49999", result);
    }

    [Fact]
    public void Packed_SpliceMatchesTheGenericAlgorithm()
    {
        // The same operations on a packed array (the fast path moves the elements before or after the deleted ones) and
        // on an array-like object (the generic algorithm)
        using var engine = new JsEngine();
        var result = engine.CreateRealm().Execute("""
            let seed = 1;
            const random = n => (seed = (seed * 1103515245 + 12345) % 2147483648) % n;
            const array = [];
            const arrayLike = { length: 0 };
            const errors = [];
            for (let step = 0; step < 5000; step++) {
              const length = array.length;
              const operation = random(6);
              let args;
              if (operation === 0) args = ['push', random(1000)];
              else if (operation === 1) args = ['pop'];
              else if (operation === 2) args = ['shift'];
              else if (operation === 3) args = ['unshift', random(1000), 'u'];
              else args = ['splice', random(length + 1), random(4), ...Array.from({ length: random(4) }, (_, i) => i % 2 ? 's' : { o: i })];
              const r1 = Array.prototype[args[0]].apply(array, args.slice(1));
              const r2 = Array.prototype[args[0]].apply(arrayLike, args.slice(1));
              const s1 = JSON.stringify([r1, array]);
              const s2 = JSON.stringify([r2, Array.from(arrayLike)]);
              if (s1 !== s2) { errors.push(step + ': ' + args.join() + ' ' + s1 + ' ' + s2); break; }
            }
            errors.join(' / ')
            """);
        Assert.Equal("", engine.ToString(result));
    }

    [Fact]
    public void Sparse_HugeIndices()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Array();
        array.CreateDataPropertyOrThrow(new PropertyKey(4294967294), "last");
        array.CreateDataPropertyOrThrow(new PropertyKey(5), "five");
        Assert.Equal(4294967294, CallArray(context, "indexOf", array, "last").AsNumber());
        Assert.Equal(5, CallArray(context, "lastIndexOf", array, "five").AsNumber());
        Assert.True(CallArray(context, "includes", array, JsValue.Undefined).AsBoolean());

        var visited = new List<double>();
        CallArray(context, "forEach", array, context.Function((_, a) =>
        {
            visited.Add(a[1].AsNumber());
            return JsValue.Undefined;
        }));
        Assert.Equal([5.0, 4294967294.0], visited.ToArray());
        Assert.Equal("five", CallArray(context, "reduceRight", array, context.Function((_, a) => a[1])).AsString());
        CallArray(context, "sort", array);
        Assert.Equal("five", array.Get(new PropertyKey(0)).AsString());
        Assert.Equal("last", array.Get(new PropertyKey(1)).AsString());
        Assert.False(array.HasProperty(new PropertyKey(4294967294)));

        var spliced = context.Array();
        spliced.CreateDataPropertyOrThrow(new PropertyKey(4294967294), "last");
        spliced.CreateDataPropertyOrThrow(new PropertyKey(7), "seven");
        var deleted = CallArray(context, "splice", spliced, 1).AsObject();
        Assert.Equal(1u, spliced.Length);
        Assert.Equal(4294967294u, ((JsArray)deleted).Length);
        Assert.Equal("seven", deleted.Get(new PropertyKey(6)).AsString());
        Assert.Equal("last", deleted.Get(new PropertyKey(4294967293)).AsString());

        var shifted = context.Array();
        shifted.CreateDataPropertyOrThrow(new PropertyKey(5), "five");
        shifted.CreateDataPropertyOrThrow(new PropertyKey(4294967294), "last");
        Assert.True(CallArray(context, "shift", shifted).IsUndefined);
        Assert.Equal(4294967294u, shifted.Length);
        Assert.Equal("five", shifted.Get(new PropertyKey(4)).AsString());
        Assert.Equal("last", shifted.Get(new PropertyKey(4294967293)).AsString());
        Assert.False(shifted.HasProperty(new PropertyKey(5)));

        CallArray(context, "splice", shifted, 1, 1);
        Assert.Equal(4294967293u, shifted.Length);
        Assert.Equal("five", shifted.Get(new PropertyKey(3)).AsString());
        Assert.Equal("last", shifted.Get(new PropertyKey(4294967292)).AsString());

        CallArray(context, "unshift", shifted, "first");
        Assert.Equal(4294967294u, shifted.Length);
        Assert.Equal("first", shifted.Get(new PropertyKey(0)).AsString());
        Assert.Equal("five", shifted.Get(new PropertyKey(4)).AsString());
        Assert.Equal("last", shifted.Get(new PropertyKey(4294967293)).AsString());
        Assert.False(shifted.HasProperty(new PropertyKey(3)));

        var arrayLike = context.Object();
        arrayLike.CreateDataPropertyOrThrow("length", 9007199254740991);
        arrayLike.CreateDataPropertyOrThrow("9007199254740990", "x");
        Assert.Equal(9007199254740990, CallArray(context, "lastIndexOf", arrayLike, "x").AsNumber());
    }

    [Fact]
    public void JoinAndToString()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, null, 1, JsValue.Null, null, JsValue.Undefined, "a", null);
        Assert.Equal(",1,,,,a,", CallArray(context, "join", array).AsString());
        Assert.Equal("-1----a-", CallArray(context, "join", array, "-").AsString());
        Assert.Equal("1a", CallArray(context, "join", array, "").AsString());
        Assert.Equal(",1,,,,a,", CallArray(context, "toString", array).AsString());
        Assert.Equal("", CallArray(context, "join", context.Array()).AsString());
        Assert.Equal(",,", CallArray(context, "join", context.Global("Array").Call(JsValue.Undefined, [3])).AsString());
        context.AssertThrows("RangeError", () => CallArray(context, "join", context.Global("Array").Call(JsValue.Undefined, [4294967295])));

        var obj = context.Object();
        obj.CreateDataPropertyOrThrow(WellKnownSymbols.ToStringTag, "Custom");
        Assert.Equal("[object Custom]", CallArray(context, "toString", obj).AsString());
        var localized = context.Object();
        localized.CreateDataPropertyOrThrow("toLocaleString", context.Function((_, _) => "L"));
        Assert.Equal("L,,L", CallArray(context, "toLocaleString", context.Array(localized, JsValue.Null, localized)).AsString());
    }

    [Fact]
    public void Join_Recursion()
    {
        // An array that contains itself is joined as an empty string, as in the browsers
        using var context = new BuiltinsTestContext();
        var array = context.Array(1);
        array.CreateDataPropertyOrThrow(new PropertyKey(1), array);
        Assert.Equal("1,", CallArray(context, "join", array).AsString());
        Assert.Equal("1,", CallArray(context, "toString", array).AsString());
        Assert.Equal("1,1,", CallArray(context, "join", context.Array(1, context.Array(array))).AsString());
    }

    [Fact]
    public void Flat()
    {
        using var context = new BuiltinsTestContext();
        var nested = context.Array(1, context.Array(2, context.Array(3, context.Array(4))), SparseArray(context, null, 5));
        Assert.Equal("1,2,[object Array],5", BuiltinsTestContext.Format(CallArray(context, "flat", nested)));
        Assert.Equal("1,2,3,4,5", BuiltinsTestContext.Format(CallArray(context, "flat", nested, double.PositiveInfinity)));
        Assert.Equal("1,[object Array],[object Array]", BuiltinsTestContext.Format(CallArray(context, "flat", nested, -1)));

        var flatMapped = CallArray(context, "flatMap", context.Array(1, 2), context.Function((_, a) => context.Array(a[0], context.Array(a[0]))));
        Assert.Equal("1,[object Array],2,[object Array]", BuiltinsTestContext.Format(flatMapped));

        var deep = context.Array();
        var current = deep;
        for (var i = 0; i < 100000; i++)
        {
            var next = context.Array();
            current.Push(next);
            current = next;
        }

        context.AssertThrows("RangeError", () => CallArray(context, "flat", deep, double.PositiveInfinity));
    }

    [Fact]
    public void CallbackMethods()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, 1, null, 3);
        Assert.Equal("2,<hole>,6", BuiltinsTestContext.Format(CallArray(context, "map", array, context.Function((_, a) => a[0].AsNumber() * 2))));
        Assert.Equal("3", BuiltinsTestContext.Format(CallArray(context, "filter", array, context.Function((_, a) => a[0].AsNumber() > 1))));
        Assert.True(CallArray(context, "every", array, context.Function((_, a) => !a[0].IsUndefined)).AsBoolean());
        Assert.False(CallArray(context, "some", array, context.Function((_, a) => a[0].IsUndefined)).AsBoolean());
        Assert.Equal(4, CallArray(context, "reduce", array, context.Function((_, a) => a[0].AsNumber() + a[1].AsNumber())).AsNumber());
        Assert.Equal("31", CallArray(context, "reduceRight", array, context.Function((_, a) => JsOperations.ToString(a[0]) + JsOperations.ToString(a[1])), "").AsString());
        context.AssertThrows("TypeError", () => CallArray(context, "reduce", SparseArray(context, null, null), context.Function((_, _) => 0)));
        context.AssertThrows("TypeError", () => CallArray(context, "forEach", array));

        // Elements added during the iteration are not visited; deleted elements are skipped
        var mutated = context.Array(1, 2, 3);
        var seen = new List<double>();
        CallArray(context, "forEach", mutated, context.Function((_, a) =>
        {
            seen.Add(a[0].AsNumber());
            if (seen.Count == 1)
            {
                mutated.Push(4);
                mutated.Delete(new PropertyKey(1));
            }

            return JsValue.Undefined;
        }));
        Assert.Equal([1.0, 3.0], seen.ToArray());
    }

    [Fact]
    public void CopyMethods()
    {
        using var context = new BuiltinsTestContext();
        var array = SparseArray(context, 1, null, 3);
        Assert.Equal("3,undefined,1", BuiltinsTestContext.Format(CallArray(context, "toReversed", array)));
        Assert.Equal("1,x,3", BuiltinsTestContext.Format(CallArray(context, "with", array, -2, "x")));
        context.AssertThrows("RangeError", () => CallArray(context, "with", array, 3, "x"));
        context.AssertThrows("RangeError", () => CallArray(context, "with", array, -4, "x"));

        var reversed = SparseArray(context, 1, null, 3, 4);
        CallArray(context, "reverse", reversed);
        Assert.Equal("4,3,<hole>,1", BuiltinsTestContext.Format(reversed));
        Assert.Equal("c,b,a", BuiltinsTestContext.Format(CallArray(context, "reverse", context.Array("a", "b", "c"))));
        Assert.Equal("1,0,0,4", BuiltinsTestContext.Format(CallArray(context, "fill", context.Array(1, 2, 3, 4), 0, 1, -1)));
        Assert.Equal("2,3", BuiltinsTestContext.Format(CallArray(context, "slice", context.Array(1, 2, 3, 4), 1, -1)));

        var large = context.Object();
        large.CreateDataPropertyOrThrow("length", 4294967296);
        context.AssertThrows("RangeError", () => CallArray(context, "toReversed", large));
    }

    [Fact]
    public void CreatedArrayCountsItsElements()
    {
        using var context = new BuiltinsTestContext();
        JsArray[] arrays = [context.Array(1, 2, 3), context.Array("a", "b", "c"), context.Array(1, "b", context.Object())];
        foreach (var array in arrays)
        {
            Assert.Equal(["0", "1", "2", "length"], array.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
            Assert.True(array.Delete(new PropertyKey(1)));
            Assert.Equal(["0", "2", "length"], array.OwnPropertyKeys().Select(k => k.ToString()).ToArray());
            Assert.Equal(2, CallArray(context, "filter", array, context.Function((_, _) => true)).AsObject().Get(new PropertyKey("length")).AsNumber());
        }
    }

    [Fact]
    public void Unscopables()
    {
        using var context = new BuiltinsTestContext();
        var unscopables = context.Realm.Intrinsics.ArrayPrototype.Get(new PropertyKey(WellKnownSymbols.Unscopables)).AsObject();
        Assert.Null(unscopables.GetPrototypeOf());
        var keys = unscopables.OwnPropertyKeys().Select(k => k.ToString()).ToArray();
        Assert.Equal(["at", "copyWithin", "entries", "fill", "find", "findIndex", "findLast", "findLastIndex", "flat", "flatMap", "includes", "keys", "toReversed", "toSorted", "toSpliced", "values"], keys);
    }

    [Fact]
    public void Iterators()
    {
        using var context = new BuiltinsTestContext();
        var array = context.Array("a", "b");
        Assert.Equal([0.0, 1.0], BuiltinsTestContext.Iterate(CallArray(context, "keys", array)).Select(v => v.AsNumber()).ToArray());
        Assert.Equal(["a", "b"], BuiltinsTestContext.Iterate(CallArray(context, "values", array)).Select(v => v.AsString()).ToArray());
        Assert.Equal(["0,a", "1,b"], BuiltinsTestContext.Iterate(CallArray(context, "entries", array)).Select(BuiltinsTestContext.Format).ToArray());

        var prototype = context.Realm.Intrinsics.ArrayPrototype;
        Assert.Same(prototype.Get(new PropertyKey("values")).AsObject(), prototype.Get(new PropertyKey(WellKnownSymbols.Iterator)).AsObject());
        Assert.Same(context.Realm.Intrinsics.ArrayPrototypeValues, prototype.Get(new PropertyKey("values")).AsObject());

        // The length is read at each step; the iterator is done for ever once it reaches the end
        var growing = context.Array(1);
        var iterator = CallArray(context, "values", growing);
        BuiltinsTestContext.Invoke(iterator, "next");
        growing.Push(2);
        Assert.Equal(2, BuiltinsTestContext.Get(BuiltinsTestContext.Invoke(iterator, "next"), "value").AsNumber());
        Assert.True(BuiltinsTestContext.Get(BuiltinsTestContext.Invoke(iterator, "next"), "done").AsBoolean());
        growing.Push(3);
        Assert.True(BuiltinsTestContext.Get(BuiltinsTestContext.Invoke(iterator, "next"), "done").AsBoolean());

        var stringIterator = CallArray(context, "values", "ab");
        Assert.Equal(["a", "b"], BuiltinsTestContext.Iterate(stringIterator).Select(v => v.AsString()).ToArray());
        context.AssertThrows("TypeError", () => CallArray(context, "values", JsValue.Undefined));
        context.AssertThrows("TypeError", () => context.Realm.Intrinsics.ArrayIteratorPrototype.Get(new PropertyKey("next")).AsObject().Call(context.Object(), []));
    }
}
