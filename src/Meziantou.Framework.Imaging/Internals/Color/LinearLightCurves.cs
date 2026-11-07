using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tone curves of an ICC profile, for what combines the samples of an image in linear light: the light of every
/// 8-bit and 16-bit sample of each channel, and the function that encodes light back to a sample.
/// </summary>
/// <remarks>
/// <para>
/// A matrix-based profile (ICC.1:2022, annex F.3) relates each channel to linear light by a tone curve
/// (<c>rTRC</c>, <c>gTRC</c>, <c>bTRC</c>), and a monochrome profile (annex F.2) its channel by <c>kTRC</c>: the
/// curves are those <see cref="IccProfileModel"/> converts colors with, evaluated the same way, and inverted the same
/// way. A profile that only has lookup tables has no function of one channel, and no linear light.
/// </para>
/// <para>
/// The tables are rented from an allocation scope, which is charged for them, and released with the instance; the
/// curves that are the same curve share theirs.
/// </para>
/// </remarks>
internal sealed class LinearLightCurves : IDisposable
{
    private const int Size8 = byte.MaxValue + 1;
    private const int Size16 = ushort.MaxValue + 1;

    private readonly IccInverseCurve[] _inverses;
    private readonly int[] _tableIndexes;
    private PooledBuffer? _tables;

    private LinearLightCurves(IccInverseCurve[] inverses, int[] tableIndexes, PooledBuffer tables)
    {
        _inverses = inverses;
        _tableIndexes = tableIndexes;
        _tables = tables;
    }

    /// <summary>Creates the curves of a profile.</summary>
    /// <param name="profile">The profile of the image.</param>
    /// <param name="scope">The scope the tables are charged to.</param>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile has no tone curve per channel, or a curve that cannot be inverted.</exception>
    /// <exception cref="ImageResourceLimitException">The tables exceed the allocation limit.</exception>
    public static LinearLightCurves Create(IccProfile profile, AllocationScope scope)
    {
        var curves = IccProfileModel.Create(profile, "image").GetToneCurves();
        var inverses = new IccInverseCurve[3];
        var indexes = new int[3];
        var distinct = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            // A monochrome profile has one curve, for the gray level that is red, green and blue
            var curve = curves[Math.Min(channel, curves.Length - 1)];
            if (!curve.TryCreateInverse(out var inverse))
                throw new UnsupportedImageFeatureException("The ICC profile of the image has a constant tone curve: its samples have no linear light to be combined in.", ImageFormat.Unknown, "Linear-light compositing with a constant tone curve");

            inverses[channel] = inverse;
            indexes[channel] = distinct;
            for (var previous = 0; previous < channel; previous++)
            {
                if (ReferenceEquals(curves[Math.Min(previous, curves.Length - 1)], curve))
                {
                    indexes[channel] = indexes[previous];
                    break;
                }
            }

            if (indexes[channel] == distinct)
            {
                distinct++;
            }
        }

        var tables = ResampleWeights.Rent<double>(scope, (long)distinct * (Size8 + Size16));
        var values = unsafe(MemoryMarshal.Cast<byte, double>(tables.Span));
        for (var channel = 0; channel < 3; channel++)
        {
            var curve = curves[Math.Min(channel, curves.Length - 1)];
            var table = values.Slice(indexes[channel] * (Size8 + Size16), Size8 + Size16);
            for (var sample = 0; sample < Size8; sample++)
            {
                table[sample] = curve.Evaluate(IccByteSample.Load((byte)sample));
            }

            for (var sample = 0; sample < Size16; sample++)
            {
                table[Size8 + sample] = curve.Evaluate(IccUInt16Sample.Load((ushort)sample));
            }
        }

        return new LinearLightCurves(inverses, indexes, tables);
    }

    /// <summary>Gets the light of every 8-bit sample of a channel (0 for red or gray, 1 for green, 2 for blue).</summary>
    public ReadOnlySpan<double> GetDecode8(int channel)
        => unsafe(MemoryMarshal.Cast<byte, double>(_tables!.Span)).Slice(_tableIndexes[channel] * (Size8 + Size16), Size8);

    /// <summary>Gets the light of every 16-bit sample of a channel.</summary>
    public ReadOnlySpan<double> GetDecode16(int channel)
        => unsafe(MemoryMarshal.Cast<byte, double>(_tables!.Span)).Slice((_tableIndexes[channel] * (Size8 + Size16)) + Size8, Size16);

    /// <summary>Encodes light to a sample of a channel, as a fraction of the largest one, clipped to [0, 1]; a value that is not a number is 0.</summary>
    public double Encode(int channel, double value)
    {
        var encoded = _inverses[channel].Evaluate(!(value > 0) ? 0 : value > 1 ? 1 : value);
        return !(encoded > 0) ? 0 : encoded > 1 ? 1 : encoded;
    }

    public void Dispose()
    {
        _tables?.Dispose();
        _tables = null;
    }
}
