namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The floating-point constants that the compiled functions read from fields (see <see cref="IlCompiler"/>). The
/// optimizations of the JIT of .NET replace <c>x * 1</c>, <c>x / 1</c>, <c>x - 0</c> and <c>-1 * x</c> with <c>x</c> or
/// <c>-x</c>, which keeps a signaling NaN, while WebAssembly requires a quiet NaN: the JIT does not know the value of a
/// field that is not read-only.
/// </summary>
#pragma warning disable CA2211, MA0069 // Non-constant fields should not be visible: the fields are mutable so that the JIT does not read their values
internal static class JitConstants
{
    public static double DoubleZero;
    public static double DoubleNegativeZero = -0d;
    public static double DoubleOne = 1d;
    public static double DoubleNegativeOne = -1d;
    public static float SingleZero;
    public static float SingleNegativeZero = -0f;
    public static float SingleOne = 1f;
    public static float SingleNegativeOne = -1f;
}
#pragma warning restore CA2211, MA0069
