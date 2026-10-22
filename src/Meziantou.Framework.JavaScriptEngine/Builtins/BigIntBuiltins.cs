using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>BigInt (https://tc39.es/ecma262/#sec-bigint-objects).</summary>
internal static class BigIntBuiltins
{
    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("asIntN", 2, AsIntN),
        new("asUintN", 2, AsUintN),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("toLocaleString", 0, ToLocaleString),
        new("toString", 0, ToString),
        new("valueOf", 0, ValueOf),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.BigIntPrototype;
        var constructor = realm.CreateBuiltinFunction("BigInt", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethods(constructor, ConstructorMethods);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethods(prototype, PrototypeMethods);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "BigInt", PropertyFlags.Configurable);

        intrinsics.AddGlobal("BigInt", constructor);
        return constructor;
    }

    /// <summary>BigInt(value)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is not null)
            return ThrowHelper.ThrowTypeError<JsValue>("BigInt is not a constructor");

        var prim = JsOperations.ToPrimitive(arguments.Length > 0 ? arguments[0] : default, JsOperations.PreferredType.Number);
        if (prim.IsNumber)
            return JsValue.FromBigInt(BigIntOperations.NumberToBigInt(prim.NumberValue));

        return JsValue.FromBigInt(BigIntOperations.ToBigInt(prim));
    }

    /// <summary>ThisBigIntValue(value)</summary>
    internal static BigInteger ThisBigIntValue(JsValue value, string methodName)
    {
        if (value.Cell is JsBigInt bigint)
            return bigint.Value;

        if (value.Cell is JsPrimitiveObject { Class: ObjectClass.BigInt } obj)
            return obj.PrimitiveValue.AsBigInt();

        return ThrowHelper.ThrowTypeError<BigInteger>($"BigInt.prototype.{methodName} requires that 'this' be a BigInt");
    }

    /// <summary>BigInt.asIntN(bits, bigint)</summary>
    private static JsValue AsIntN(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var bits = JsOperations.ToIndex(arguments.Length > 0 ? arguments[0] : default);
        var bigint = BigIntOperations.ToBigInt(arguments.Length > 1 ? arguments[1] : default);
        return JsValue.FromBigInt(BigIntOperations.AsIntN(bits, bigint));
    }

    /// <summary>BigInt.asUintN(bits, bigint)</summary>
    private static JsValue AsUintN(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var bits = JsOperations.ToIndex(arguments.Length > 0 ? arguments[0] : default);
        var bigint = BigIntOperations.ToBigInt(arguments.Length > 1 ? arguments[1] : default);
        return JsValue.FromBigInt(BigIntOperations.AsUintN(bits, bigint));
    }

    /// <summary>BigInt.prototype.toLocaleString(): without ECMA-402, the same as toString().</summary>
    private static JsValue ToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisBigIntValue(thisValue, "toLocaleString");
        return x.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>BigInt.prototype.toString(radix)</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisBigIntValue(thisValue, "toString");
        var radix = arguments.Length > 0 ? arguments[0] : default;
        var radixMV = radix.IsUndefined ? 10 : JsOperations.ToIntegerOrInfinity(radix);
        if (radixMV is < 2 or > 36)
            return ThrowHelper.ThrowRangeError<JsValue>("toString() radix must be between 2 and 36");

        return BigIntOperations.ToString(x, (int)radixMV);
    }

    /// <summary>BigInt.prototype.valueOf()</summary>
    private static JsValue ValueOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = thisValue.Cell is JsBigInt ? thisValue : JsValue.FromBigInt(ThisBigIntValue(thisValue, "valueOf"));
        return x;
    }
}
