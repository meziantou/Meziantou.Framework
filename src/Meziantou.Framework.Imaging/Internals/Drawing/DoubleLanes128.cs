using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The operations of <see cref="IDoubleLanes{TVector}"/> on <see cref="Vector128{T}"/>: two lanes.</summary>
internal readonly struct DoubleLanes128 : IDoubleLanes<Vector128<double>>
{
    public static int Count => Vector128<double>.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Create(double value) => Vector128.Create(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Sequence(int start) => Vector128.Create((double)start) + Vector128.Create(0.0, 1.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Create(double a, double b, double c, double d, int lane) => lane == 0 ? Vector128.Create(a, b) : Vector128.Create(c, d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Load(ReadOnlySpan<double> source) => Vector128.Create(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store(Vector128<double> value, Span<double> destination) => value.CopyTo(destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Add(Vector128<double> left, Vector128<double> right) => left + right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Subtract(Vector128<double> left, Vector128<double> right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Multiply(Vector128<double> left, Vector128<double> right) => left * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Divide(Vector128<double> left, Vector128<double> right) => left / right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Sqrt(Vector128<double> value) => Vector128.Sqrt(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Abs(Vector128<double> value) => Vector128.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Floor(Vector128<double> value) => Vector128.Floor(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Min(Vector128<double> left, Vector128<double> right) => Vector128.Min(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Max(Vector128<double> left, Vector128<double> right) => Vector128.Max(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> LessThanOrEqual(Vector128<double> left, Vector128<double> right) => Vector128.LessThanOrEqual(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Equal(Vector128<double> left, Vector128<double> right) => Vector128.Equals(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> LessThan(Vector128<double> left, Vector128<double> right) => Vector128.LessThan(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> GreaterThan(Vector128<double> left, Vector128<double> right) => Vector128.GreaterThan(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> GreaterThanOrEqual(Vector128<double> left, Vector128<double> right) => Vector128.GreaterThanOrEqual(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> And(Vector128<double> left, Vector128<double> right) => left & right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Or(Vector128<double> left, Vector128<double> right) => left | right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> AndNot(Vector128<double> left, Vector128<double> right) => Vector128.AndNot(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Select(Vector128<double> mask, Vector128<double> left, Vector128<double> right) => Vector128.ConditionalSelect(mask, left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool All(Vector128<double> mask) => Vector128.EqualsAll(mask.AsUInt64(), Vector128<ulong>.AllBitsSet);

    /// <remarks>
    /// A whole number <c>v</c> from 0 to 2^52 plus 2^52 is a double whose low bits are <c>v</c>: the four samples of a
    /// pixel are the four 16-bit parts of one 64-bit lane, stored in little-endian order.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreSamples(Vector128<double> c0, Vector128<double> c1, Vector128<double> c2, Vector128<double> c3, Vector128<double> clear, Span<ushort> destination)
    {
        var magic = Vector128.Create(4503599627370496.0);
        var low = Vector128.Create((ulong)ushort.MaxValue);
        var pixels = ((c0 + magic).AsUInt64() & low) | (((c1 + magic).AsUInt64() & low) << 16) | (((c2 + magic).AsUInt64() & low) << 32) | ((c3 + magic).AsUInt64() << 48);
        Vector128.AndNot(pixels, clear.AsUInt64()).AsUInt16().CopyTo(destination);
    }
}
