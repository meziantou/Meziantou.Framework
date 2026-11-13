using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The operations of <see cref="IDoubleLanes{TVector}"/> on <see cref="Vector512{T}"/>: eight lanes.</summary>
internal readonly struct DoubleLanes512 : IDoubleLanes<Vector512<double>>
{
    public static int Count => Vector512<double>.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Create(double value) => Vector512.Create(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Sequence(int start) => Vector512.Create((double)start) + Vector512.Create(0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Load(ReadOnlySpan<double> source) => Vector512.Create(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store(Vector512<double> value, Span<double> destination) => value.CopyTo(destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Add(Vector512<double> left, Vector512<double> right) => left + right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Subtract(Vector512<double> left, Vector512<double> right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Multiply(Vector512<double> left, Vector512<double> right) => left * right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Divide(Vector512<double> left, Vector512<double> right) => left / right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Sqrt(Vector512<double> value) => Vector512.Sqrt(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Abs(Vector512<double> value) => Vector512.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Floor(Vector512<double> value) => Vector512.Floor(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Min(Vector512<double> left, Vector512<double> right) => Vector512.Min(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Max(Vector512<double> left, Vector512<double> right) => Vector512.Max(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> LessThanOrEqual(Vector512<double> left, Vector512<double> right) => Vector512.LessThanOrEqual(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Equal(Vector512<double> left, Vector512<double> right) => Vector512.Equals(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> LessThan(Vector512<double> left, Vector512<double> right) => Vector512.LessThan(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> GreaterThan(Vector512<double> left, Vector512<double> right) => Vector512.GreaterThan(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> GreaterThanOrEqual(Vector512<double> left, Vector512<double> right) => Vector512.GreaterThanOrEqual(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> And(Vector512<double> left, Vector512<double> right) => left & right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Or(Vector512<double> left, Vector512<double> right) => left | right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> AndNot(Vector512<double> left, Vector512<double> right) => Vector512.AndNot(left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> Select(Vector512<double> mask, Vector512<double> left, Vector512<double> right) => Vector512.ConditionalSelect(mask, left, right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool All(Vector512<double> mask) => Vector512.EqualsAll(mask.AsUInt64(), Vector512<ulong>.AllBitsSet);

    /// <remarks>
    /// A whole number <c>v</c> from 0 to 2^52 plus 2^52 is a double whose low bits are <c>v</c>: the four samples of a
    /// pixel are the four 16-bit parts of one 64-bit lane, stored in little-endian order.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreSamples(Vector512<double> c0, Vector512<double> c1, Vector512<double> c2, Vector512<double> c3, Vector512<double> clear, Span<ushort> destination)
    {
        var magic = Vector512.Create(4503599627370496.0);
        var low = Vector512.Create((ulong)ushort.MaxValue);
        var pixels = ((c0 + magic).AsUInt64() & low) | (((c1 + magic).AsUInt64() & low) << 16) | (((c2 + magic).AsUInt64() & low) << 32) | ((c3 + magic).AsUInt64() << 48);
        Vector512.AndNot(pixels, clear.AsUInt64()).AsUInt16().CopyTo(destination);
    }
}
