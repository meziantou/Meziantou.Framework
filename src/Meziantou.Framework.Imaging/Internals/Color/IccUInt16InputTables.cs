using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The values of the first stage of a conversion (one curve per source channel, <see cref="IccPipeline.HasInputCurves"/>)
/// for every 16-bit sample of each source channel, for one conversion of many 16-bit colors: each entry is computed by
/// the same evaluation as the stage on the same normalized sample (<see cref="IccUInt16Sample.Load"/>), so reading the
/// table gives bit-identical results. The channels of the same curve share their table.
/// </summary>
/// <remarks>
/// A table costs 65,536 evaluations of its curve (one or two <see cref="Math.Pow(double, double)"/> each) and 512 KiB,
/// rented from the scope of the conversion and released with the instance: it is only built for a conversion of at
/// least <see cref="Threshold"/> colors, which evaluates the curve at least twice as many times.
/// </remarks>
internal sealed class IccUInt16InputTables : IDisposable
{
    /// <summary>The number of colors of a conversion from which the tables are built.</summary>
    public const long Threshold = 2 * TableSize;

    private const int TableSize = ushort.MaxValue + 1;

    private readonly int[] _tableIndexes;
    private PooledBuffer? _tables;

    private IccUInt16InputTables(int[] tableIndexes, PooledBuffer tables)
    {
        _tableIndexes = tableIndexes;
        _tables = tables;
    }

    /// <summary>Creates the tables of a conversion, when it pays.</summary>
    /// <param name="pipeline">The conversion.</param>
    /// <param name="colors">The number of colors that will be converted with the tables.</param>
    /// <param name="scope">The scope the tables are charged to.</param>
    /// <returns>The tables, or <see langword="null"/> when the first stage is not one curve per channel or the conversion is too small for the tables to pay.</returns>
    /// <exception cref="ImageResourceLimitException">The tables exceed the allocation limit.</exception>
    public static IccUInt16InputTables? Create(IccPipeline pipeline, long colors, AllocationScope scope)
    {
        if (colors < Threshold || !pipeline.HasInputCurves)
            return null;

        var channels = pipeline.SourceChannelCount;
        var indexes = new int[channels];
        var distinct = 0;
        for (var channel = 0; channel < channels; channel++)
        {
            indexes[channel] = distinct;
            for (var previous = 0; previous < channel; previous++)
            {
                if (pipeline.HaveSameInputCurve(channel, previous))
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

        var buffer = ResampleWeights.Rent<double>(scope, (long)distinct * TableSize);
        var values = unsafe(MemoryMarshal.Cast<byte, double>(buffer.Span));
        for (var channel = 0; channel < channels; channel++)
        {
            // The first channel of each table fills it
            if (Array.IndexOf(indexes, indexes[channel]) < channel)
                continue;

            var table = values.Slice(indexes[channel] * TableSize, TableSize);
            for (var sample = 0; sample < TableSize; sample++)
            {
                table[sample] = pipeline.EvaluateInputCurve(channel, IccUInt16Sample.Load((ushort)sample));
            }
        }

        return new IccUInt16InputTables(indexes, buffer);
    }

    /// <summary>Gets the value of the first stage for every 16-bit sample of a source channel.</summary>
    public ReadOnlySpan<double> GetTable(int channel)
        => unsafe(MemoryMarshal.Cast<byte, double>((_tables ?? throw new ObjectDisposedException(nameof(IccUInt16InputTables))).Span)).Slice(_tableIndexes[channel] * TableSize, TableSize);

    public void Dispose()
    {
        _tables?.Dispose();
        _tables = null;
    }
}
