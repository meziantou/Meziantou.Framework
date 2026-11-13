using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The operations of the drawing kernels that are written once for vectors of doubles of any width: each lane computes
/// what the scalar code computes for one element, with the same operations in the same order (no fused multiply-add).
/// </summary>
/// <typeparam name="TVector">The type of the vectors.</typeparam>
internal interface IDoubleLanes<TVector>
    where TVector : unmanaged
{
    /// <summary>Gets the number of lanes.</summary>
    static abstract int Count { get; }

    /// <summary>A vector whose lanes are <paramref name="value"/>.</summary>
    static abstract TVector Create(double value);

    /// <summary>The lanes <c>start</c>, <c>start + 1</c>, ...: whole numbers, exact.</summary>
    static abstract TVector Sequence(int start);

    /// <summary>The first <see cref="Count"/> values of a span.</summary>
    static abstract TVector Load(ReadOnlySpan<double> source);

    /// <summary>Stores the lanes in the first <see cref="Count"/> values of a span.</summary>
    static abstract void Store(TVector value, Span<double> destination);

    static abstract TVector Add(TVector left, TVector right);

    static abstract TVector Subtract(TVector left, TVector right);

    static abstract TVector Multiply(TVector left, TVector right);

    static abstract TVector Divide(TVector left, TVector right);

    static abstract TVector Sqrt(TVector value);

    static abstract TVector Abs(TVector value);

    static abstract TVector Floor(TVector value);

    /// <summary>All the bits of the lanes where <c>left == right</c>, none elsewhere.</summary>
    static abstract TVector Equal(TVector left, TVector right);

    /// <summary>All the bits of the lanes where <c>left &lt; right</c>, none elsewhere (and where a lane is not a number).</summary>
    static abstract TVector LessThan(TVector left, TVector right);

    static abstract TVector GreaterThan(TVector left, TVector right);

    static abstract TVector GreaterThanOrEqual(TVector left, TVector right);

    static abstract TVector And(TVector left, TVector right);

    static abstract TVector Or(TVector left, TVector right);

    /// <summary>The bits of <paramref name="left"/> that are not set in <paramref name="right"/>.</summary>
    static abstract TVector AndNot(TVector left, TVector right);

    /// <summary>The lanes of <paramref name="left"/> where the mask is set, those of <paramref name="right"/> elsewhere.</summary>
    static abstract TVector Select(TVector mask, TVector left, TVector right);

    /// <summary>Gets a value indicating whether every lane of a mask is set.</summary>
    static abstract bool All(TVector mask);

    /// <summary>
    /// Stores four channels of whole numbers from 0 to 65535 as the interleaved 16-bit samples of <see cref="Count"/>
    /// pixels, the pixels whose lane of <paramref name="clear"/> is set as zeros.
    /// </summary>
    static abstract void StoreSamples(TVector c0, TVector c1, TVector c2, TVector c3, TVector clear, Span<ushort> destination);
}
