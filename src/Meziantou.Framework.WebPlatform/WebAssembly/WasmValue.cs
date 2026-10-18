using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.WebAssembly;

/// <summary>
/// A WebAssembly value (https://webassembly.github.io/spec/core/exec/runtime.html#values). The references are null (a
/// null reference of any type), a <see cref="WasmFunction"/>, a <see cref="WasmHostReference"/> (a JavaScript value), a
/// <see cref="WasmI31Reference"/>, a <see cref="WasmGcObject"/> or a <see cref="WasmExceptionReference"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct WasmValue
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

    public WasmValueKind Kind { get; }

    /// <summary>The referenced object of a reference, or null for a null reference.</summary>
    public object? Reference { get; }

    public int I32 => (int)_bits;

    public long I64 => _bits;

    public float F32 => BitConverter.Int32BitsToSingle((int)_bits);

    public double F64 => BitConverter.Int64BitsToDouble(_bits);

    /// <summary>The bits of a <c>f32</c> value (the payload of a NaN is preserved).</summary>
    public int F32Bits => (int)_bits;

    /// <summary>The bits of a <c>f64</c> value (the payload of a NaN is preserved).</summary>
    public long F64Bits => _bits;

    /// <summary>The low 64 bits of a <c>v128</c> value.</summary>
    public long V128Low => _bits;

    /// <summary>The high 64 bits of a <c>v128</c> value.</summary>
    public long V128High => _high;

    public static WasmValue FromI32(int value) => new(WasmValueKind.I32, value, 0, null);

    public static WasmValue FromI64(long value) => new(WasmValueKind.I64, value, 0, null);

    public static WasmValue FromF32(float value) => new(WasmValueKind.F32, BitConverter.SingleToInt32Bits(value), 0, null);

    public static WasmValue FromF32Bits(int bits) => new(WasmValueKind.F32, bits, 0, null);

    public static WasmValue FromF64(double value) => new(WasmValueKind.F64, BitConverter.DoubleToInt64Bits(value), 0, null);

    public static WasmValue FromF64Bits(long bits) => new(WasmValueKind.F64, bits, 0, null);

    public static WasmValue FromV128(long low, long high) => new(WasmValueKind.V128, low, high, null);

    /// <summary>A reference, or a null reference when <paramref name="reference"/> is null.</summary>
    public static WasmValue FromReference(object? reference) => new(WasmValueKind.Ref, 0, 0, reference);

    public static WasmValue Null => FromReference(null);

    /// <summary>The default value of a type (https://webassembly.github.io/spec/core/exec/runtime.html#default-val): zero, or a null reference.</summary>
    public static WasmValue Default(WasmValueType type) => new(type.Kind, 0, 0, null);
}

/// <summary>A host reference: a JavaScript value given to WebAssembly (<c>ref.host</c> and <c>ref.extern</c>).</summary>
internal sealed class WasmHostReference(object value)
{
    /// <summary>The JavaScript value.</summary>
    public object Value { get; } = value;
}

/// <summary>An unboxed scalar reference (<c>ref.i31</c>).</summary>
internal sealed class WasmI31Reference(int value)
{
    /// <summary>The signed value, in the range [-2^30, 2^30 - 1].</summary>
    public int Value { get; } = value;
}

/// <summary>A struct or an array of WebAssembly (an Exported GC Object in JavaScript). The store returns the same object for the same struct or array.</summary>
internal abstract class WasmGcObject
{
    public abstract bool IsArray { get; }
}

/// <summary>An exception of WebAssembly (an exception address).</summary>
internal abstract class WasmExceptionReference
{
    /// <summary>The tag of the exception.</summary>
    public abstract WasmTag Tag { get; }

    /// <summary>The payload of the exception.</summary>
    public abstract WasmValue GetArgument(int index);

    /// <summary>The number of values of the payload.</summary>
    public abstract int ArgumentCount { get; }
}
