using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class BinaryDataTests
{
    private static JsObject New(FundamentalsTestContext context, string constructor, params JsValue[] arguments) => JsOperations.Construct(context.Global(constructor), arguments).AsObject();

    private static JsValue Call(JsValue obj, string method, params JsValue[] arguments) => FundamentalsTestContext.Invoke(obj, method, arguments);

    private static JsValue Get(JsValue obj, string name) => FundamentalsTestContext.Get(obj, name);

    private static JsValue Get(JsValue obj, JsSymbol symbol) => FundamentalsTestContext.Get(obj, new PropertyKey(symbol));

    private static JsValue[] Elements(JsObject typedArray)
    {
        var length = (int)Get(typedArray, "length").AsNumber();
        var result = new JsValue[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = typedArray.Get(new PropertyKey((uint)i));
        }

        return result;
    }

    private static double[] Numbers(JsObject typedArray) => Elements(typedArray).Select(v => v.AsNumber()).ToArray();

    private static JsObject Options(FundamentalsTestContext context, string name, JsValue value)
    {
        var options = context.NewObject();
        options.CreateDataPropertyOrThrow(name, value);
        return options;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // ArrayBuffer
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ArrayBuffer_Constructor()
    {
        using var context = new FundamentalsTestContext();
        var constructor = context.Global("ArrayBuffer");
        Assert.Equal(1, Get(constructor, "length").AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => constructor.Call(JsValue.Undefined, [8]));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "ArrayBuffer", -1));

        // Allocation failures are RangeErrors
        context.AssertThrows(ErrorType.RangeError, () => New(context, "ArrayBuffer", 9007199254740991));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "ArrayBuffer", 9007199254740992));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "ArrayBuffer", 0, Options(context, "maxByteLength", 9007199254740992)));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "ArrayBuffer", 10, Options(context, "maxByteLength", 5)));

        var buffer = New(context, "ArrayBuffer", 8);
        Assert.Equal(8, Get(buffer, "byteLength").AsNumber());
        Assert.Equal(8, Get(buffer, "maxByteLength").AsNumber());
        Assert.False(Get(buffer, "resizable").AsBoolean());
        Assert.False(Get(buffer, "detached").AsBoolean());
        Assert.Equal("[object ArrayBuffer]", context.CallPrototypeMethod("Object", "toString", buffer).AsString());
        Assert.True(Call(constructor, "isView", New(context, "Uint8Array", 1)).AsBoolean());
        Assert.False(Call(constructor, "isView", buffer).AsBoolean());
        Assert.Same(constructor, Get(constructor, WellKnownSymbols.Species).AsObject());

        // Byte length getters reject a SharedArrayBuffer
        var shared = New(context, "SharedArrayBuffer", 8);
        var byteLengthGetter = context.Intrinsics.ArrayBufferPrototype.GetOwnProperty("byteLength", out var descriptor) ? descriptor.Get : default;
        Assert.Equal("get byteLength", Get(byteLengthGetter, "name").AsString());
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Call(byteLengthGetter, shared, []));
    }

    [Fact]
    public void ArrayBuffer_Resize()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 4, Options(context, "maxByteLength", 16));
        Assert.True(Get(buffer, "resizable").AsBoolean());
        Assert.Equal(16, Get(buffer, "maxByteLength").AsNumber());

        var view = New(context, "Uint8Array", buffer);
        Call(view, "fill", 7);
        Call(buffer, "resize", 2);
        Assert.Equal(2, Get(view, "length").AsNumber());
        Call(buffer, "resize", 12);
        Assert.Equal([7, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], Numbers(view));
        context.AssertThrows(ErrorType.RangeError, () => Call(buffer, "resize", 17));
        context.AssertThrows(ErrorType.TypeError, () => Call(New(context, "ArrayBuffer", 4), "resize", 2));
    }

    [Fact]
    public void ArrayBuffer_Slice()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 8);
        var bytes = New(context, "Uint8Array", buffer);
        for (var i = 0; i < 8; i++)
        {
            bytes.Set(new PropertyKey((uint)i), i, throwOnFailure: true);
        }

        var slice = Call(buffer, "slice", 2, -2).AsObject();
        Assert.Equal(4, Get(slice, "byteLength").AsNumber());
        Assert.Equal([2, 3, 4, 5], Numbers(New(context, "Uint8Array", slice)));

        // The species constructor must return a new, large enough ArrayBuffer
        var constructor = context.NewObject();
        var species = context.Function((_, _) => buffer, isConstructor: true);
        constructor.CreateDataPropertyOrThrow(WellKnownSymbols.Species, species);
        buffer.CreateDataPropertyOrThrow("constructor", constructor);
        context.AssertThrows(ErrorType.TypeError, () => Call(buffer, "slice", 0));
    }

    [Fact]
    public void ArrayBuffer_TransferAndDetach()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 4, Options(context, "maxByteLength", 8));
        var view = New(context, "Uint8Array", buffer);
        Call(view, "fill", 1);

        var transferred = Call(buffer, "transfer", 6).AsObject();
        Assert.True(Get(buffer, "detached").AsBoolean());
        Assert.Equal(0, Get(buffer, "byteLength").AsNumber());
        Assert.Equal(0, Get(view, "length").AsNumber());
        Assert.True(Get(transferred, "resizable").AsBoolean());
        Assert.Equal([1, 1, 1, 1, 0, 0], Numbers(New(context, "Uint8Array", transferred)));
        context.AssertThrows(ErrorType.TypeError, () => Call(buffer, "transfer"));
        context.AssertThrows(ErrorType.TypeError, () => Call(buffer, "slice"));

        var fixedLength = Call(transferred, "transferToFixedLength", 2).AsObject();
        Assert.False(Get(fixedLength, "resizable").AsBoolean());
        Assert.Equal([1, 1], Numbers(New(context, "Uint8Array", fixedLength)));

        // transfer preserves the resizability: a length above the maximum is a RangeError, before detaching
        var resizable = New(context, "ArrayBuffer", 4, Options(context, "maxByteLength", 8));
        context.AssertThrows(ErrorType.RangeError, () => Call(resizable, "transfer", 9));
        Assert.False(Get(resizable, "detached").AsBoolean());

        // DetachArrayBuffer with a key
        var arrayBuffer = (JsArrayBuffer)New(context, "ArrayBuffer", 4);
        var key = context.NewObject();
        arrayBuffer.DetachKey = key;
        context.AssertThrows(ErrorType.TypeError, () => arrayBuffer.Detach());
        context.AssertThrows(ErrorType.TypeError, () => Call(arrayBuffer, "transfer"));
        arrayBuffer.Detach(key);
        Assert.True(arrayBuffer.IsDetached);
    }

    [Fact]
    public void ArrayBuffer_ExternalMemory()
    {
        using var context = new FundamentalsTestContext();
        var memory = new byte[] { 1, 2, 3, 4 };
        var key = context.NewObject();
        var buffer = JsArrayBuffer.CreateExternal(context.Realm, memory, key);
        var view = New(context, "Uint8Array", buffer);
        Assert.Equal([1, 2, 3, 4], Numbers(view));
        view.Set(new PropertyKey(0u), 42, throwOnFailure: true);
        Assert.Equal(42, memory[0]);
        Assert.HasCount(4, buffer.GetSpan().ToArray());
        Assert.True(BinaryDataOperations.TryGetBufferSourceSpan(New(context, "DataView", buffer, 1, 2), out var bytes));
        Assert.Equal([2, 3], bytes.ToArray());
        Assert.True(BinaryDataOperations.TryGetBufferSourceSpan(Call(view, "subarray", 3), out bytes));
        Assert.Equal([4], bytes.ToArray());
        Assert.False(BinaryDataOperations.TryGetBufferSourceSpan(context.NewObject(), out _));
        context.AssertThrows(ErrorType.TypeError, () => Call(buffer, "transfer"));
        buffer.Detach(key);
        Assert.Equal(0, Get(view, "length").AsNumber());
    }

    // ------------------------------------------------------------------------------------------------------------------
    // SharedArrayBuffer
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SharedArrayBuffer_Grow()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "SharedArrayBuffer", 4, Options(context, "maxByteLength", 16));
        Assert.True(Get(buffer, "growable").AsBoolean());
        Assert.Equal(16, Get(buffer, "maxByteLength").AsNumber());
        Assert.Equal("[object SharedArrayBuffer]", context.CallPrototypeMethod("Object", "toString", buffer).AsString());

        var view = New(context, "Int32Array", buffer);
        Assert.Equal(1, Get(view, "length").AsNumber());
        Call(buffer, "grow", 12);
        Assert.Equal(12, Get(buffer, "byteLength").AsNumber());
        Assert.Equal(3, Get(view, "length").AsNumber());
        Call(buffer, "grow", 12);
        context.AssertThrows(ErrorType.RangeError, () => Call(buffer, "grow", 8));
        context.AssertThrows(ErrorType.RangeError, () => Call(buffer, "grow", 17));
        context.AssertThrows(ErrorType.TypeError, () => Call(New(context, "SharedArrayBuffer", 4), "grow", 8));

        var slice = Call(buffer, "slice", 4).AsObject();
        Assert.Equal(8, Get(slice, "byteLength").AsNumber());
        Assert.False(Get(slice, "growable").AsBoolean());

        context.AssertThrows(ErrorType.TypeError, () => ((JsArrayBuffer)buffer).Detach());
    }

    [Fact]
    public void SharedArrayBuffer_SharedBetweenEngines()
    {
        using var context = new FundamentalsTestContext();
        var buffer = (JsArrayBuffer)New(context, "SharedArrayBuffer", 16);
        var block = buffer.SharedBlock!;
        var view = New(context, "Int32Array", buffer);

        var thread = new Thread(() =>
        {
            using var other = new FundamentalsTestContext();
            var otherBuffer = JsArrayBuffer.CreateShared(other.Realm, block);
            var otherView = New(other, "Int32Array", otherBuffer);
            for (var i = 0; i < 1000; i++)
            {
                other.CallStatic("Atomics", "add", otherView, 0, 1);
            }
        });
        thread.Start();
        for (var i = 0; i < 1000; i++)
        {
            context.CallStatic("Atomics", "add", view, 0, 1);
        }

        thread.Join();
        Assert.Equal(2000, context.CallStatic("Atomics", "load", view, 0).AsNumber());
    }

    // ------------------------------------------------------------------------------------------------------------------
    // DataView
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void DataView_GetAndSet()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 16);
        var view = New(context, "DataView", buffer, 2);
        Assert.Equal(14, Get(view, "byteLength").AsNumber());
        Assert.Equal(2, Get(view, "byteOffset").AsNumber());

        Call(view, "setUint16", 0, 0x1234);
        Assert.Equal(0x1234, Call(view, "getUint16", 0).AsNumber());
        Assert.Equal(0x3412, Call(view, "getUint16", 0, true).AsNumber());
        Assert.Equal(0x12, Call(view, "getUint8", 0).AsNumber());

        Call(view, "setInt32", 4, -2, true);
        Assert.Equal(-2, Call(view, "getInt32", 4, true).AsNumber());
        Assert.Equal(4294967294, Call(view, "getUint32", 4, true).AsNumber());

        Call(view, "setFloat64", 0, Math.PI);
        Assert.Equal(Math.PI, Call(view, "getFloat64", 0).AsNumber());

        Call(view, "setBigInt64", 0, JsValue.FromBigInt(-1));
        Assert.Equal(new BigInteger(ulong.MaxValue), Call(view, "getBigUint64", 0).AsBigInt());
        context.AssertThrows(ErrorType.TypeError, () => Call(view, "setBigInt64", 0, 1));

        Call(view, "setFloat16", 0, 1.5);
        Assert.Equal(1.5, Call(view, "getFloat16", 0).AsNumber());

        context.AssertThrows(ErrorType.RangeError, () => Call(view, "getInt32", 11));
        context.AssertThrows(ErrorType.RangeError, () => Call(view, "getInt8", -1));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "DataView", buffer, 17));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "DataView", buffer, 8, 9));
        context.AssertThrows(ErrorType.TypeError, () => New(context, "DataView", context.NewObject()));

        ((JsArrayBuffer)buffer).Detach();
        context.AssertThrows(ErrorType.TypeError, () => Get(view, "byteLength"));
        context.AssertThrows(ErrorType.TypeError, () => Call(view, "getInt8", 0));
    }

    [Fact]
    public void DataView_LengthTracking()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 8, Options(context, "maxByteLength", 16));
        var tracking = New(context, "DataView", buffer, 4);
        var fixedView = New(context, "DataView", buffer, 2, 4);
        Assert.Equal(4, Get(tracking, "byteLength").AsNumber());
        Call(buffer, "resize", 12);
        Assert.Equal(8, Get(tracking, "byteLength").AsNumber());
        Call(buffer, "resize", 5);
        Assert.Equal(1, Get(tracking, "byteLength").AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => Get(fixedView, "byteLength"));
        context.AssertThrows(ErrorType.TypeError, () => Get(fixedView, "byteOffset"));
        Call(buffer, "resize", 3);
        context.AssertThrows(ErrorType.TypeError, () => Get(tracking, "byteLength"));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Conversions
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Conversions()
    {
        using var context = new FundamentalsTestContext();
        Assert.Equal([44, -1, 0, 0, -128], Numbers(New(context, "Int8Array", context.NewArray(300, 255, double.NaN, double.PositiveInfinity, 128))));
        Assert.Equal([2, 4, 0, 255, 0, 1], Numbers(New(context, "Uint8ClampedArray", context.NewArray(2.5, 3.5, -1, 300, double.NaN, 0.5000001))));
        Assert.Equal([4294967295, 0], Numbers(New(context, "Uint32Array", context.NewArray(-1, 4294967296))));
        Assert.Equal([65504, double.PositiveInfinity, 0], Numbers(New(context, "Float16Array", context.NewArray(65504, 65520, 1e-8))));

        // The conversion to binary16 rounds once, from the double (not through a float)
        var value = 1 + Math.Pow(2, -11) + Math.Pow(2, -40);
        Assert.Equal(1.0009765625, Numbers(New(context, "Float16Array", context.NewArray(value)))[0]);
        Assert.Equal(1, Numbers(New(context, "Float16Array", context.NewArray(1 + Math.Pow(2, -11))))[0]);

        var bigints = New(context, "BigInt64Array", context.NewArray(JsValue.FromBigInt(BigInteger.Pow(2, 63)), JsValue.FromBigInt(-1)));
        Assert.Equal(BigInteger.MinusOne * BigInteger.Pow(2, 63), bigints.Get(new PropertyKey(0u)).AsBigInt());
        var unsigned = New(context, "BigUint64Array", bigints);
        Assert.Equal(BigInteger.Pow(2, 63), unsigned.Get(new PropertyKey(0u)).AsBigInt());
        Assert.Equal(new BigInteger(ulong.MaxValue), unsigned.Get(new PropertyKey(1u)).AsBigInt());
        context.AssertThrows(ErrorType.TypeError, () => New(context, "BigInt64Array", context.NewArray(1)));
        context.AssertThrows(ErrorType.SyntaxError, () => New(context, "BigInt64Array", context.NewArray("1.5")));
        context.AssertThrows(ErrorType.TypeError, () => New(context, "Int8Array", bigints));
        Assert.Equal(BigInteger.One, New(context, "BigInt64Array", context.NewArray(true)).Get(new PropertyKey(0u)).AsBigInt());
    }

    // ------------------------------------------------------------------------------------------------------------------
    // TypedArray exotic object
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TypedArray_IntegerIndexedExoticObject()
    {
        using var context = new FundamentalsTestContext();
        var array = New(context, "Int16Array", 2);
        Assert.True(array.Set(new PropertyKey(1u), 5, array));
        Assert.Equal(5, array.Get(new PropertyKey(1u)).AsNumber());

        // The canonical numeric strings are not ordinary properties
        foreach (var key in new[] { "-0", "1.5", "Infinity", "-Infinity", "NaN", "-1", "2", "4294967295", "1e+21" })
        {
            Assert.True(array.Set(key, 1, array));
            Assert.True(array.Get(key).IsUndefined, key);
            Assert.False(array.HasProperty(key), key);
            Assert.False(array.DefineOwnProperty(key, PropertyDescriptor.Data(1)), key);
            Assert.True(array.Delete(key), key);
        }

        // The other strings are ordinary properties
        Assert.True(array.Set("1.50", 1, array));
        Assert.Equal(1, array.Get("1.50").AsNumber());
        Assert.True(array.Set("+1", 1, array));
        Assert.Equal(["0", "1", "1.50", "+1"], array.OwnPropertyKeys().Select(k => k.ToString()).ToArray());

        Assert.False(array.Delete(new PropertyKey(0u)));
        Assert.True(array.GetOwnProperty(new PropertyKey(1u), out var descriptor));
        Assert.Equal(PropertyFlags.Default, descriptor.Flags);
        Assert.False(array.DefineOwnProperty(new PropertyKey(0u), PropertyDescriptor.Data(1, PropertyFlags.Writable | PropertyFlags.Enumerable)));
        Assert.True(array.DefineOwnProperty(new PropertyKey(0u), PropertyDescriptor.ValueOnly(9)));
        Assert.Equal(9, array.Get(new PropertyKey(0u)).AsNumber());

        // A set with another receiver on an invalid index succeeds without creating a property
        var receiver = context.NewObject();
        Assert.True(array.Set(new PropertyKey(5u), 1, receiver));
        Assert.False(receiver.HasOwnProperty(new PropertyKey(5u)));
        Assert.True(array.Set(new PropertyKey(0u), 1, receiver));
        Assert.Equal(1, receiver.Get(new PropertyKey(0u)).AsNumber());
        Assert.Equal(9, array.Get(new PropertyKey(0u)).AsNumber());
    }

    [Fact]
    public void TypedArray_Constructors()
    {
        using var context = new FundamentalsTestContext();
        var typedArray = context.Intrinsics.TypedArray;
        Assert.Same(typedArray, context.Global("Int8Array").GetPrototypeOf());
        Assert.Equal(3, Get(context.Global("Float64Array"), "length").AsNumber());
        Assert.Equal(8, Get(context.Global("Float64Array"), "BYTES_PER_ELEMENT").AsNumber());
        Assert.Equal(2, Get(Get(context.Global("Float16Array"), "prototype"), "BYTES_PER_ELEMENT").AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Construct(typedArray, []));
        context.AssertThrows(ErrorType.TypeError, () => context.Global("Int8Array").Call(JsValue.Undefined, []));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "Int8Array", -1));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "Float64Array", 9007199254740991));

        var buffer = New(context, "ArrayBuffer", 8);
        context.AssertThrows(ErrorType.RangeError, () => New(context, "Int32Array", buffer, 1));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "Int32Array", buffer, 4, 2));
        context.AssertThrows(ErrorType.RangeError, () => New(context, "Int32Array", New(context, "ArrayBuffer", 7)));
        var view = New(context, "Int32Array", buffer, 4);
        Assert.Equal(1, Get(view, "length").AsNumber());
        Assert.Equal(4, Get(view, "byteOffset").AsNumber());
        Assert.Same(buffer, Get(view, "buffer").AsObject());

        var iterable = context.NewIterable(1, 2, 3);
        Assert.Equal([1, 2, 3], Numbers(New(context, "Uint8Array", iterable)));

        var arrayLike = context.NewObject();
        arrayLike.CreateDataPropertyOrThrow("length", 2);
        arrayLike.CreateDataPropertyOrThrow("1", 7);
        Assert.Equal([double.NaN, 7], Numbers(New(context, "Float32Array", arrayLike)));

        var copy = New(context, "Float64Array", New(context, "Int8Array", context.NewArray(-1, 2)));
        Assert.Equal([-1, 2], Numbers(copy));
        Assert.Equal("Float64Array", Get(copy, WellKnownSymbols.ToStringTag).AsString());

        // The prototype comes from newTarget
        var prototype = context.NewObject();
        var newTarget = context.Function((_, _) => JsValue.Undefined, isConstructor: true);
        newTarget.DefineBuiltIn("prototype", prototype);
        Assert.Same(prototype, JsOperations.Construct(context.Global("Int8Array"), [1], newTarget).AsObject().GetPrototypeOf());
    }

    [Fact]
    public void TypedArray_LengthTracking()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 8, Options(context, "maxByteLength", 16));
        var tracking = New(context, "Uint16Array", buffer, 2);
        var fixedLength = New(context, "Uint16Array", buffer, 2, 2);
        Assert.Equal(3, Get(tracking, "length").AsNumber());
        Call(buffer, "resize", 11);
        Assert.Equal(4, Get(tracking, "length").AsNumber());
        Assert.Equal(8, Get(tracking, "byteLength").AsNumber());
        Assert.Equal(["0", "1", "2", "3"], tracking.OwnPropertyKeys().Select(k => k.ToString()).ToArray());

        Call(buffer, "resize", 5);
        Assert.Equal(1, Get(tracking, "length").AsNumber());
        Assert.Equal(0, Get(fixedLength, "length").AsNumber());
        Assert.Equal(0, Get(fixedLength, "byteOffset").AsNumber());
        Assert.True(fixedLength.Get(new PropertyKey(0u)).IsUndefined);
        context.AssertThrows(ErrorType.TypeError, () => Call(fixedLength, "fill", 0));

        Call(buffer, "resize", 1);
        Assert.Equal(0, Get(tracking, "length").AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => Call(tracking, "join"));

        // subarray of a length-tracking typed array without end is length-tracking
        Call(buffer, "resize", 8);
        var sub = Call(tracking, "subarray", 1).AsObject();
        Assert.Equal(2, Get(sub, "length").AsNumber());
        Call(buffer, "resize", 12);
        Assert.Equal(4, Get(sub, "length").AsNumber());
    }

    // ------------------------------------------------------------------------------------------------------------------
    // %TypedArray% and %TypedArray%.prototype
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TypedArray_FromAndOf()
    {
        using var context = new FundamentalsTestContext();
        var int8 = context.Global("Int8Array");
        var result = Call(int8, "from", context.NewIterable(1, 2), context.Function((thisValue, args) => args[0].AsNumber() * 10)).AsObject();
        Assert.Equal([10, 20], Numbers(result));
        Assert.Equal([1, 2, 3], Numbers(Call(int8, "of", 1, 2, 3).AsObject()));
        context.AssertThrows(ErrorType.TypeError, () => Call(int8, "from", JsValue.Undefined));
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Call(Get(int8, "of"), context.NewObject(), []));

        // The constructor must create a large enough typed array
        var small = context.Function((_, _) => New(context, "Int8Array", 1), isConstructor: true);
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Call(Get(int8, "of"), small, [1, 2]));
    }

    [Fact]
    public void TypedArray_Methods()
    {
        using var context = new FundamentalsTestContext();
        var array = New(context, "Int32Array", context.NewArray(1, 2, 3, 4, 5));
        Assert.Equal(5, Call(array, "at", -1).AsNumber());
        Assert.True(Call(array, "at", 5).IsUndefined);
        Assert.Equal("1,2,3,4,5", Call(array, "join").AsString());
        Assert.Equal("1-2-3-4-5", Call(array, "join", "-").AsString());
        Assert.Equal("1,2,3,4,5", Call(array, "toString").AsString());
        Assert.Equal(2, Call(array, "indexOf", 3).AsNumber());
        Assert.Equal(-1, Call(array, "indexOf", 3, 3).AsNumber());
        Assert.Equal(2, Call(array, "lastIndexOf", 3).AsNumber());
        Assert.Equal(-1, Call(array, "lastIndexOf", 3, -4).AsNumber());
        Assert.True(Call(array, "includes", 5).AsBoolean());
        Assert.False(Call(array, "includes", 1, 1).AsBoolean());

        var isEven = context.Function((_, args) => args[0].AsNumber() % 2 == 0);
        Assert.Equal([2, 4], Numbers(Call(array, "filter", isEven).AsObject()));
        Assert.Equal(2, Call(array, "find", isEven).AsNumber());
        Assert.Equal(1, Call(array, "findIndex", isEven).AsNumber());
        Assert.Equal(4, Call(array, "findLast", isEven).AsNumber());
        Assert.Equal(3, Call(array, "findLastIndex", isEven).AsNumber());
        Assert.False(Call(array, "every", isEven).AsBoolean());
        Assert.True(Call(array, "some", isEven).AsBoolean());
        Assert.Equal([2, 4, 6, 8, 10], Numbers(Call(array, "map", context.Function((_, args) => args[0].AsNumber() * 2)).AsObject()));
        Assert.Equal(15, Call(array, "reduce", context.Function((_, args) => args[0].AsNumber() + args[1].AsNumber())).AsNumber());
        Assert.Equal("54321", Call(array, "reduceRight", context.Function((_, args) => JsOperations.ToString(args[0]) + JsOperations.ToString(args[1])), "").AsString());
        context.AssertThrows(ErrorType.TypeError, () => Call(New(context, "Int8Array", 0), "reduce", isEven));

        Assert.Equal([5, 4, 3, 2, 1], Numbers(Call(array, "toReversed").AsObject()));
        Assert.Equal([1, 2, 9, 4, 5], Numbers(Call(array, "with", -3, 9).AsObject()));
        context.AssertThrows(ErrorType.RangeError, () => Call(array, "with", 5, 9));
        Assert.Equal([2, 3], Numbers(Call(array, "slice", 1, 3).AsObject()));
        Assert.Equal([4, 5], Numbers(Call(array, "subarray", -2).AsObject()));

        Call(array, "reverse");
        Assert.Equal([5, 4, 3, 2, 1], Numbers(array));
        Call(array, "fill", 0, 1, -1);
        Assert.Equal([5, 0, 0, 0, 1], Numbers(array));

        var bigints = New(context, "BigInt64Array", 2);
        Call(bigints, "fill", JsValue.FromBigInt(3));
        Assert.Equal(new BigInteger(3), bigints.Get(new PropertyKey(1u)).AsBigInt());
        context.AssertThrows(ErrorType.TypeError, () => Call(bigints, "fill", 3));
    }

    [Fact]
    public void TypedArray_CopyWithin()
    {
        using var context = new FundamentalsTestContext();
        var array = New(context, "Int16Array", context.NewArray(1, 2, 3, 4, 5));
        Call(array, "copyWithin", 1, 0, 3);
        Assert.Equal([1, 1, 2, 3, 5], Numbers(array));
        Call(array, "copyWithin", 0, 2);
        Assert.Equal([2, 3, 5, 3, 5], Numbers(array));
        Call(array, "copyWithin", -1, 0);
        Assert.Equal([2, 3, 5, 3, 2], Numbers(array));
    }

    [Fact]
    public void TypedArray_Set()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 8);
        var bytes = New(context, "Uint8Array", buffer);
        Call(bytes, "set", context.NewArray(1, 2, 3, 4, 5, 6, 7, 8));

        // Overlapping source and target of the same type
        Call(bytes, "set", Call(bytes, "subarray", 0, 4), 2);
        Assert.Equal([1, 2, 1, 2, 3, 4, 7, 8], Numbers(bytes));

        // Overlapping source and target of different types
        var shorts = New(context, "Uint16Array", buffer, 0, 2);
        Call(bytes, "set", shorts, 4);
        Assert.Equal([1, 2, 1, 2, 1, 1, 7, 8], Numbers(bytes));

        context.AssertThrows(ErrorType.RangeError, () => Call(bytes, "set", context.NewArray(1), 8));
        context.AssertThrows(ErrorType.RangeError, () => Call(bytes, "set", context.NewArray(), -1));
        context.AssertThrows(ErrorType.RangeError, () => Call(bytes, "set", context.NewArray(), double.PositiveInfinity));
        context.AssertThrows(ErrorType.TypeError, () => Call(bytes, "set", New(context, "BigInt64Array", 1)));
        Call(bytes, "set", context.NewArray(), 8);
    }

    [Fact]
    public void TypedArray_Sort()
    {
        using var context = new FundamentalsTestContext();
        var floats = New(context, "Float64Array", context.NewArray(3, double.NaN, -0.0, 0, -1, double.NegativeInfinity, 0, -0.0));
        Call(floats, "sort");
        var sorted = Numbers(floats);
        Assert.Equal([double.NegativeInfinity, -1, 0, 0, 0, 0, 3, double.NaN], sorted);
        Assert.True(double.IsNegative(sorted[2]) && double.IsNegative(sorted[3]));
        Assert.False(double.IsNegative(sorted[4]) || double.IsNegative(sorted[5]));

        var unsigned = New(context, "BigUint64Array", context.NewArray(JsValue.FromBigInt(ulong.MaxValue), JsValue.FromBigInt(1)));
        Call(unsigned, "sort");
        Assert.Equal(BigInteger.One, unsigned.Get(new PropertyKey(0u)).AsBigInt());

        var ints = New(context, "Int8Array", context.NewArray(5, -3, 1));
        Assert.Equal([-3, 1, 5], Numbers(Call(ints, "toSorted").AsObject()));
        Assert.Equal([5, -3, 1], Numbers(ints));

        // A comparator: the sort is stable
        var descending = context.Function((_, args) => (int)args[1].AsNumber() / 10 - ((int)args[0].AsNumber() / 10));
        var values = New(context, "Int32Array", context.NewArray(11, 21, 12, 22, 13));
        Call(values, "sort", descending);
        Assert.Equal([21, 22, 11, 12, 13], Numbers(values));
        context.AssertThrows(ErrorType.TypeError, () => Call(values, "sort", 1));
    }

    [Fact]
    public void TypedArray_SpeciesCreate()
    {
        using var context = new FundamentalsTestContext();
        var array = New(context, "Int8Array", context.NewArray(1, 2, 3));
        var constructor = context.NewObject();
        constructor.CreateDataPropertyOrThrow(WellKnownSymbols.Species, context.Function((_, args) => New(context, "Float32Array", args), isConstructor: true));
        array.CreateDataPropertyOrThrow("constructor", constructor);
        var mapped = Call(array, "map", context.Function((_, args) => args[0].AsNumber() + 0.5)).AsObject();
        Assert.Equal("Float32Array", Get(mapped, WellKnownSymbols.ToStringTag).AsString());
        Assert.Equal([1.5, 2.5, 3.5], Numbers(mapped));
        Assert.Equal([2, 3], Numbers(Call(array, "slice", 1).AsObject()));

        // The content type must match
        constructor.CreateDataPropertyOrThrow(WellKnownSymbols.Species, context.Function((_, args) => New(context, "BigInt64Array", args), isConstructor: true));
        context.AssertThrows(ErrorType.TypeError, () => Call(array, "slice"));
    }

    [Fact]
    public void TypedArray_ShrinkDuringCoercion()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 4, Options(context, "maxByteLength", 8));
        var array = New(context, "Uint8Array", buffer);
        var shrink = context.NewObject();
        shrink.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) =>
        {
            Call(buffer, "resize", 1);
            return 0;
        }));

        // includes compares the elements beyond the new length to undefined
        Assert.True(Call(array, "includes", JsValue.Undefined, shrink).AsBoolean());
        Call(buffer, "resize", 4);
        Assert.Equal(-1, Call(array, "indexOf", JsValue.Undefined, shrink).AsNumber());
    }

    [Fact]
    public void TypedArray_Iterators()
    {
        using var context = new FundamentalsTestContext();
        var array = New(context, "Int8Array", context.NewArray(4, 5));
        var values = context.Intrinsics.TypedArrayPrototype.Get("values");
        Assert.Same(values.AsObject(), context.Intrinsics.TypedArrayPrototype.Get(WellKnownSymbols.Iterator).AsObject());

        var iterator = Call(array, "entries");
        var first = Call(iterator, "next");
        Assert.False(Get(first, "done").AsBoolean());
        Assert.Equal(4, Get(Get(first, "value"), "1").AsNumber());
        Call(iterator, "next");
        Assert.True(Get(Call(iterator, "next"), "done").AsBoolean());

        var keys = Call(array, "keys");
        Assert.Equal(0, Get(Call(keys, "next"), "value").AsNumber());
        ((JsArrayBuffer)Get(array, "buffer").AsObject()).Detach();
        context.AssertThrows(ErrorType.TypeError, () => Call(keys, "next"));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Atomics
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Atomics_Operations()
    {
        using var context = new FundamentalsTestContext();
        foreach (var shared in new[] { true, false })
        {
            var buffer = New(context, shared ? "SharedArrayBuffer" : "ArrayBuffer", 16);
            var int8 = New(context, "Int8Array", buffer);
            Assert.Equal(0, context.CallStatic("Atomics", "add", int8, 1, 130).AsNumber());
            Assert.Equal(-126, context.CallStatic("Atomics", "load", int8, 1).AsNumber());
            Assert.Equal(-126, context.CallStatic("Atomics", "sub", int8, 1, 2).AsNumber());
            Assert.Equal(-128, context.CallStatic("Atomics", "exchange", int8, 1, 1).AsNumber());

            var uint16 = New(context, "Uint16Array", buffer);
            Assert.Equal(0, context.CallStatic("Atomics", "or", uint16, 2, 0xF0F0).AsNumber());
            Assert.Equal(0xF0F0, context.CallStatic("Atomics", "and", uint16, 2, 0xFF00).AsNumber());
            Assert.Equal(0xF000, context.CallStatic("Atomics", "xor", uint16, 2, 0xFFFF).AsNumber());
            Assert.Equal(0x0FFF, context.CallStatic("Atomics", "load", uint16, 2).AsNumber());

            var int32 = New(context, "Int32Array", buffer);
            Assert.Equal(0, context.CallStatic("Atomics", "compareExchange", int32, 3, 1, 2).AsNumber());
            Assert.Equal(0, context.CallStatic("Atomics", "compareExchange", int32, 3, 0, -5).AsNumber());
            Assert.Equal(-5, context.CallStatic("Atomics", "sub", int32, 3, int.MinValue).AsNumber());
            Assert.Equal(int.MaxValue - 4, context.CallStatic("Atomics", "load", int32, 3).AsNumber());
            Assert.Equal(7, context.CallStatic("Atomics", "store", int32, 0, 7.9).AsNumber());

            var big = New(context, "BigUint64Array", New(context, shared ? "SharedArrayBuffer" : "ArrayBuffer", 16));
            Assert.Equal(BigInteger.Zero, context.CallStatic("Atomics", "sub", big, 1, JsValue.FromBigInt(1)).AsBigInt());
            Assert.Equal(new BigInteger(ulong.MaxValue), context.CallStatic("Atomics", "load", big, 1).AsBigInt());
            Assert.Equal(new BigInteger(ulong.MaxValue), context.CallStatic("Atomics", "compareExchange", big, 1, JsValue.FromBigInt(-1), JsValue.FromBigInt(5)).AsBigInt());
            Assert.Equal(new BigInteger(5), context.CallStatic("Atomics", "load", big, 1).AsBigInt());

            context.AssertThrows(ErrorType.RangeError, () => context.CallStatic("Atomics", "load", int32, 4));
            context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "load", New(context, "Float64Array", buffer), 0));
            context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "load", New(context, "Uint8ClampedArray", buffer), 0));
            context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "add", big, 0, 1));
        }

        Assert.True(context.CallStatic("Atomics", "isLockFree", 4).AsBoolean());
        Assert.False(context.CallStatic("Atomics", "isLockFree", 3).AsBoolean());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "pause", 1.5));
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "pause", "1"));
        Assert.True(context.CallStatic("Atomics", "pause", -0.0).IsUndefined);
    }

    [Fact]
    public void Atomics_RevalidatesAfterCoercion()
    {
        using var context = new FundamentalsTestContext();
        var buffer = New(context, "ArrayBuffer", 8);
        var array = New(context, "Int32Array", buffer);
        var detach = context.NewObject();
        detach.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) =>
        {
            ((JsArrayBuffer)buffer).Detach();
            return 1;
        }));

        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "store", array, 0, detach));
    }

    [Fact]
    public void Atomics_Wait()
    {
        using var context = new FundamentalsTestContext();
        var shared = New(context, "Int32Array", New(context, "SharedArrayBuffer", 8));
        Assert.Equal("not-equal", context.CallStatic("Atomics", "wait", shared, 0, 1).AsString());
        Assert.Equal("timed-out", context.CallStatic("Atomics", "wait", shared, 0, 0, 10).AsString());
        Assert.Equal(0, context.CallStatic("Atomics", "notify", shared, 0).AsNumber());
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "wait", New(context, "Int32Array", 2), 0, 0, 0));
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "wait", New(context, "Int16Array", New(context, "SharedArrayBuffer", 8)), 0, 0, 0));
        Assert.Equal(0, context.CallStatic("Atomics", "notify", New(context, "Int32Array", 2), 0).AsNumber());

        var result = context.CallStatic("Atomics", "waitAsync", shared, 0, 1);
        Assert.False(Get(result, "async").AsBoolean());
        Assert.Equal("not-equal", Get(result, "value").AsString());
        result = context.CallStatic("Atomics", "waitAsync", shared, 0, 0, 0);
        Assert.Equal("timed-out", Get(result, "value").AsString());

        context.Engine.CanBlock = false;
        context.AssertThrows(ErrorType.TypeError, () => context.CallStatic("Atomics", "wait", shared, 0, 0, 0));
    }

    [Fact]
    public void Atomics_WaitAndNotifyAcrossThreads()
    {
        using var context = new FundamentalsTestContext();
        var buffer = (JsArrayBuffer)New(context, "SharedArrayBuffer", 8);
        var block = buffer.SharedBlock!;
        var view = New(context, "Int32Array", buffer);
        string? result = null;
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var other = new FundamentalsTestContext();
            var otherView = New(other, "Int32Array", JsArrayBuffer.CreateShared(other.Realm, block));
            started.Set();
            result = other.CallStatic("Atomics", "wait", otherView, 1, 0).AsString();
        });
        thread.Start();
        started.Wait();

        // Notify until the waiter is woken (it may not be waiting yet)
        var woken = 0.0;
        while (woken == 0)
        {
            woken = context.CallStatic("Atomics", "notify", view, 1, 1).AsNumber();
            Thread.Yield();
        }

        thread.Join();
        Assert.Equal(1, woken);
        Assert.Equal("ok", result);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Uint8Array base64 and hex
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Uint8Array_Base64()
    {
        using var context = new FundamentalsTestContext();
        var uint8 = context.Global("Uint8Array");
        Assert.Equal([102, 111, 111, 98, 97, 114], Numbers(Call(uint8, "fromBase64", "Zm9v YmFy").AsObject()));
        Assert.Equal([199, 239, 242], Numbers(Call(uint8, "fromBase64", "x-_y", Options(context, "alphabet", "base64url")).AsObject()));
        Assert.Equal([102], Numbers(Call(uint8, "fromBase64", "Zg").AsObject()));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromBase64", "Zg", Options(context, "lastChunkHandling", "strict")));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromBase64", "Zh==", Options(context, "lastChunkHandling", "strict")));
        Assert.Equal([102], Numbers(Call(uint8, "fromBase64", "Zh==").AsObject()));
        Assert.Equal([], Numbers(Call(uint8, "fromBase64", "Zg", Options(context, "lastChunkHandling", "stop-before-partial")).AsObject()));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromBase64", "Z"));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromBase64", "Zg==="));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromBase64", "x-_y"));
        context.AssertThrows(ErrorType.TypeError, () => Call(uint8, "fromBase64", 1));
        context.AssertThrows(ErrorType.TypeError, () => Call(uint8, "fromBase64", "", Options(context, "alphabet", "other")));

        var bytes = Call(uint8, "fromBase64", "+/8=").AsObject();
        Assert.Equal("+/8=", Call(bytes, "toBase64").AsString());
        Assert.Equal("-_8=", Call(bytes, "toBase64", Options(context, "alphabet", "base64url")).AsString());
        Assert.Equal("+/8", Call(bytes, "toBase64", Options(context, "omitPadding", true)).AsString());
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Call(Get(bytes, "toBase64"), New(context, "Int8Array", 1), []));

        // setFromBase64 writes up to the error, or up to the length of the target
        var target = New(context, "Uint8Array", context.NewArray(255, 255, 255, 255, 255));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(target, "setFromBase64", "MjYyZm.9v"));
        Assert.Equal([50, 54, 50, 255, 255], Numbers(target));
        var result = Call(target, "setFromBase64", "Zm9vYmFy");
        Assert.Equal(4, Get(result, "read").AsNumber());
        Assert.Equal(3, Get(result, "written").AsNumber());
        result = Call(target, "setFromBase64", "Zm9vYmE");
        Assert.Equal(7, Get(result, "read").AsNumber());
        Assert.Equal(5, Get(result, "written").AsNumber());
        Assert.Equal([102, 111, 111, 98, 97], Numbers(target));
    }

    [Fact]
    public void Uint8Array_Hex()
    {
        using var context = new FundamentalsTestContext();
        var uint8 = context.Global("Uint8Array");
        var bytes = Call(uint8, "fromHex", "00fFa0").AsObject();
        Assert.Equal([0, 255, 160], Numbers(bytes));
        Assert.Equal("00ffa0", Call(bytes, "toHex").AsString());
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromHex", "abc"));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(uint8, "fromHex", "0g"));

        var target = New(context, "Uint8Array", 2);
        var result = Call(target, "setFromHex", "010203");
        Assert.Equal(4, Get(result, "read").AsNumber());
        Assert.Equal(2, Get(result, "written").AsNumber());
        Assert.Equal([1, 2], Numbers(target));
        context.AssertThrows(ErrorType.SyntaxError, () => Call(New(context, "Uint8Array", 4), "setFromHex", "01zz"));
    }
}
