using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The operations of <see cref="IDoubleLanes{TVector}"/> on <see cref="Vector256{T}"/>: four lanes.</summary>
internal readonly struct DoubleLanes256 : IDoubleLanes<Vector256<double>>
{
    public static int Count => Vector256<double>.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Create(double value) => Vector256.Create(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Sequence(int start) => Vector256.Create((double)start) + Vector256.Create(0.0, 1.0, 2.0, 3.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Load(ReadOnlySpan<double> source) => Vector256.Create(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store(Vector256<double> value, Span<double> destination) => value.CopyTo(destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Add(Vector256<double> left, Vector256<double> right) => left + right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Subtract(Vector256<double> left, Vector256<double> right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Multiply(Vector256<double> left, Vector256<double> right) => left * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Divide(Vector256<double> left, Vector256<double> right) => left / right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Sqrt(Vector256<double> value) => Vector256.Sqrt(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Abs(Vector256<double> value) => Vector256.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Floor(Vector256<double> value) => Vector256.Floor(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Min(Vector256<double> left, Vector256<double> right) => Vector256.Min(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Max(Vector256<double> left, Vector256<double> right) => Vector256.Max(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> LessThanOrEqual(Vector256<double> left, Vector256<double> right) => Vector256.LessThanOrEqual(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Equal(Vector256<double> left, Vector256<double> right) => Vector256.Equals(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> LessThan(Vector256<double> left, Vector256<double> right) => Vector256.LessThan(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> GreaterThan(Vector256<double> left, Vector256<double> right) => Vector256.GreaterThan(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> GreaterThanOrEqual(Vector256<double> left, Vector256<double> right) => Vector256.GreaterThanOrEqual(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> And(Vector256<double> left, Vector256<double> right) => left & right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Or(Vector256<double> left, Vector256<double> right) => left | right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> AndNot(Vector256<double> left, Vector256<double> right) => Vector256.AndNot(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Select(Vector256<double> mask, Vector256<double> left, Vector256<double> right) => Vector256.ConditionalSelect(mask, left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool All(Vector256<double> mask) => Vector256.EqualsAll(mask.AsUInt64(), Vector256<ulong>.AllBitsSet);

    /// <remarks>
    /// A whole number <c>v</c> from 0 to 2^52 plus 2^52 is a double whose low bits are <c>v</c>: the four samples of a
    /// pixel are the four 16-bit parts of one 64-bit lane, stored in little-endian order.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreSamples(Vector256<double> c0, Vector256<double> c1, Vector256<double> c2, Vector256<double> c3, Vector256<double> clear, Span<ushort> destination)
    {
        var magic = Vector256.Create(4503599627370496.0);
        var low = Vector256.Create((ulong)ushort.MaxValue);
        var pixels = ((c0 + magic).AsUInt64() & low) | (((c1 + magic).AsUInt64() & low) << 16) | (((c2 + magic).AsUInt64() & low) << 32) | ((c3 + magic).AsUInt64() << 48);
        Vector256.AndNot(pixels, clear.AsUInt64()).AsUInt16().CopyTo(destination);
    }
}
