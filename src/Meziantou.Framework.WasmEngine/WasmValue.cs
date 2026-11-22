using System.Runtime.InteropServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// A WebAssembly value (https://webassembly.github.io/spec/core/exec/runtime.html#values). The references are null (a
/// null reference of any type), a <see cref="WasmFunction"/>, a <see cref="WasmHostReference"/> (a value of the host), a
/// <see cref="WasmI31Reference"/>, a <see cref="WasmGcObject"/> or a <see cref="WasmExceptionReference"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct WasmValue : IEquatable<WasmValue>
{
    private readonly long _bits;
    private readonly long _high;

    private WasmValue(WasmValueKind kind, long bits, long high, object? reference)
    {
        Kind = kind;
        _bits = bits;
        _high = high;
        Reference = reference;
    }

    /// <summary>Gets the kind of the value.</summary>
    public WasmValueKind Kind { get; }

    /// <summary>Gets the referenced object of a reference, or null for a null reference.</summary>
    public object? Reference { get; }

    /// <summary>Gets the value of an <c>i32</c>.</summary>
    public int I32 => (int)_bits;

    /// <summary>Gets the value of an <c>i64</c>.</summary>
    public long I64 => _bits;

    /// <summary>Gets the value of an <c>f32</c>.</summary>
    public float F32 => BitConverter.Int32BitsToSingle((int)_bits);

    /// <summary>Gets the value of an <c>f64</c>.</summary>
    public double F64 => BitConverter.Int64BitsToDouble(_bits);

    /// <summary>Gets the bits of an <c>f32</c> value (the payload of a NaN is preserved).</summary>
    public int F32Bits => (int)_bits;

    /// <summary>Gets the bits of an <c>f64</c> value (the payload of a NaN is preserved).</summary>
    public long F64Bits => _bits;

    /// <summary>Gets the low 64 bits of a <c>v128</c> value.</summary>
    public long V128Low => _bits;

    /// <summary>Gets the high 64 bits of a <c>v128</c> value.</summary>
    public long V128High => _high;

    /// <summary>Gets a null reference.</summary>
    public static WasmValue Null => FromReference(null);

    /// <summary>Creates a number from its bits (<c>i32</c> and <c>f32</c> values have the bits of an <see cref="int"/>).</summary>
    internal static WasmValue FromNumberBits(WasmValueKind kind, long bits) => new(kind, bits, 0, null);

    /// <summary>The bits of a number.</summary>
    internal long Bits => _bits;

    /// <summary>Creates an <c>i32</c> value.</summary>
    public static WasmValue FromI32(int value) => new(WasmValueKind.I32, value, 0, null);

    /// <summary>Creates an <c>i64</c> value.</summary>
    public static WasmValue FromI64(long value) => new(WasmValueKind.I64, value, 0, null);

    /// <summary>Creates an <c>f32</c> value.</summary>
    public static WasmValue FromF32(float value) => new(WasmValueKind.F32, BitConverter.SingleToInt32Bits(value), 0, null);

    /// <summary>Creates an <c>f32</c> value from its bits.</summary>
    public static WasmValue FromF32Bits(int bits) => new(WasmValueKind.F32, bits, 0, null);

    /// <summary>Creates an <c>f64</c> value.</summary>
    public static WasmValue FromF64(double value) => new(WasmValueKind.F64, BitConverter.DoubleToInt64Bits(value), 0, null);

    /// <summary>Creates an <c>f64</c> value from its bits.</summary>
    public static WasmValue FromF64Bits(long bits) => new(WasmValueKind.F64, bits, 0, null);

    /// <summary>Creates a <c>v128</c> value.</summary>
    public static WasmValue FromV128(long low, long high) => new(WasmValueKind.V128, low, high, null);

    /// <summary>
    /// Creates a reference, or a null reference when <paramref name="reference"/> is null: a <see cref="WasmFunction"/>,
    /// a <see cref="WasmHostReference"/>, a <see cref="WasmI31Reference"/>, a <see cref="WasmGcObject"/> or a
    /// <see cref="WasmExceptionReference"/>.
    /// </summary>
    public static WasmValue FromReference(object? reference) => new(WasmValueKind.Ref, 0, 0, reference);

    /// <summary>Creates an <c>externref</c> to a value of the host, or a null reference when <paramref name="value"/> is null.</summary>
    public static WasmValue FromHostValue(object? value) => FromReference(value is null ? null : new WasmHostReference(value));

    /// <summary>Returns the default value of a type (https://webassembly.github.io/spec/core/exec/runtime.html#default-val): zero, or a null reference.</summary>
    public static WasmValue Default(WasmValueType type) => new(type.Kind, 0, 0, null);

    /// <summary>Compares two values: the bits of the numbers, and the identity of the references.</summary>
    public static bool operator ==(WasmValue left, WasmValue right) => left.Equals(right);

    /// <summary>Compares two values: the bits of the numbers, and the identity of the references.</summary>
    public static bool operator !=(WasmValue left, WasmValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(WasmValue other) => Kind == other.Kind && _bits == other._bits && _high == other._high && Equals(Reference, other.Reference);

    /// <inheritdoc/>
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is WasmValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, _bits, _high, Reference);

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        WasmValueKind.I32 => "i32:" + I32.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WasmValueKind.I64 => "i64:" + I64.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WasmValueKind.F32 => "f32:" + F32.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WasmValueKind.F64 => "f64:" + F64.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WasmValueKind.V128 => "v128:0x" + _high.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) + _bits.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
        _ => Reference is null ? "ref.null" : "ref:" + Reference,
    };
}

/// <summary>A host reference: a value of the host given to WebAssembly (an <c>externref</c>).</summary>
/// <param name="value">The value of the host.</param>
public sealed class WasmHostReference(object value)
{
    /// <summary>Gets the value of the host.</summary>
    public object Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <inheritdoc/>
    public override string? ToString() => Value.ToString();
}

/// <summary>An unboxed scalar reference (<c>ref.i31</c>).</summary>
/// <param name="value">The signed value, in the range [-2^30, 2^30 - 1].</param>
public sealed class WasmI31Reference(int value)
{
    /// <summary>Gets the signed value, in the range [-2^30, 2^30 - 1].</summary>
    public int Value { get; } = value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A struct or an array of WebAssembly. The store returns the same object for the same struct or array.</summary>
public abstract class WasmGcObject
{
    private protected WasmGcObject()
    {
    }

    /// <summary>Gets a value indicating whether the object is an array (otherwise it is a struct).</summary>
    public abstract bool IsArray { get; }
}

/// <summary>An exception of WebAssembly (an exception address). The store returns the same object for the same exception.</summary>
public abstract class WasmExceptionReference
{
    private protected WasmExceptionReference()
    {
    }

    /// <summary>Gets the tag of the exception.</summary>
    public abstract WasmTag Tag { get; }

    /// <summary>Gets the number of values of the payload.</summary>
    public abstract int ArgumentCount { get; }

    /// <summary>Returns a value of the payload.</summary>
    public abstract WasmValue GetArgument(int index);
}
