using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The kernels of ICC color conversion: they evaluate the stages of an <see cref="IccPipeline"/> for each color of a row,
/// in <see cref="double"/>. <see cref="ConvertScalar"/> is the numerical reference; <see cref="Convert"/> reads the first
/// stage of 8-bit conversions, and the 8-bit samples of their last stage, from precomputed tables, with bit-identical
/// results.
/// </summary>
internal static class ColorConversionKernels
{
    // The direct-mapped cache of the colors converted by one call of ConvertBytesCached: 2^CacheBits entries
    private const int CacheBits = 8;
    private const int CacheSize = 1 << CacheBits;

    /// <summary>
    /// Converts every pixel of a frame from the leased source to the leased destination of the same size and pixel
    /// format: the color channels go through the conversion at the storage precision (16-bit samples never go through
    /// 8 bits), alpha is copied unchanged, hidden colors of transparent pixels included.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of both storages; its color channels match the channel counts of the conversion.</typeparam>
    public static void ConvertRows<TPixel>(scoped in PixelLease source, scoped in PixelLease destination, IccPipeline pipeline, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var height = source.Height;
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (typeof(TPixel) == typeof(Rgba32))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowBytes(y), destination.GetRowBytes(y), 4, 4);
            }
            else if (typeof(TPixel) == typeof(Bgra32))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowBytes(y), destination.GetRowBytes(y), 4, 4, reverseColorOrder: true);
            }
            else if (typeof(TPixel) == typeof(Rgb24))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowBytes(y), destination.GetRowBytes(y), 3, 3);
            }
            else if (typeof(TPixel) == typeof(Gray8))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowBytes(y), destination.GetRowBytes(y), 1, 1);
            }
            else if (typeof(TPixel) == typeof(Rgba64))
            {
                Convert<ushort, IccUInt16Sample>(pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(source.GetRowBytes(y))), unsafe(MemoryMarshal.Cast<byte, ushort>(destination.GetRowBytes(y))), 4, 4);
            }
            else if (typeof(TPixel) == typeof(Gray16))
            {
                Convert<ushort, IccUInt16Sample>(pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(source.GetRowBytes(y))), unsafe(MemoryMarshal.Cast<byte, ushort>(destination.GetRowBytes(y))), 1, 1);
            }
            else
            {
                throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
            }
        }
    }

    /// <summary>Converts interleaved colors.</summary>
    /// <typeparam name="T">The stored sample type.</typeparam>
    /// <typeparam name="TSample">The sample conversion.</typeparam>
    /// <param name="pipeline">The conversion.</param>
    /// <param name="source">The source samples: a whole number of colors of <paramref name="sourceStride"/> samples.</param>
    /// <param name="destination">The destination samples, for the same number of colors. It may be exactly <paramref name="source"/> when the strides are equal.</param>
    /// <param name="sourceStride">The samples per source color: the source channels, then the samples copied unchanged (alpha).</param>
    /// <param name="destinationStride">The samples per destination color: the destination channels, then the copied samples.</param>
    /// <param name="reverseColorOrder">Whether three-channel colors are stored blue first on both sides.</param>
    public static void Convert<T, TSample>(IccPipeline pipeline, ReadOnlySpan<T> source, Span<T> destination, int sourceStride, int destinationStride, bool reverseColorOrder = false)
        where T : unmanaged
        where TSample : struct, IIccSample<T>
    {
        if (pipeline.IsIdentity)
        {
            ConvertScalar<T, TSample>(pipeline, source, destination, sourceStride, destinationStride, reverseColorOrder);
            return;
        }

        if (typeof(T) == typeof(byte) && pipeline.HasLookupTable && pipeline.ByteInputTables is { } tables)
        {
            ConvertBytesCached(pipeline, tables, unsafe(MemoryMarshal.Cast<T, byte>(source)), unsafe(MemoryMarshal.Cast<T, byte>(destination)), sourceStride, destinationStride, reverseColorOrder);
            return;
        }

        ConvertBatches<T, TSample>(pipeline, source, destination, sourceStride, destinationStride, reverseColorOrder);
    }

    /// <summary>
    /// Converts interleaved colors like <see cref="ConvertScalar"/>, <see cref="IccPipeline.BatchSize"/> colors at a time
    /// stored by component (<see cref="IccPipeline.ApplyBatch"/>), so that each stage loops over the colors instead of
    /// being called for each of them. The first stage of 8-bit colors (one curve per source channel) is read from the
    /// tables of the pipeline, and once the pipeline has them, the 8-bit samples of the last stage (one curve per
    /// destination channel) are read from its threshold tables: every color gets the samples of <see cref="ConvertScalar"/>.
    /// </summary>
    private static void ConvertBatches<T, TSample>(IccPipeline pipeline, ReadOnlySpan<T> source, Span<T> destination, int sourceStride, int destinationStride, bool reverseColorOrder)
        where T : unmanaged
        where TSample : struct, IIccSample<T>
    {
        var sourceChannels = pipeline.SourceChannelCount;
        var destinationChannels = pipeline.DestinationChannelCount;
        var copied = sourceStride - sourceChannels;
        Debug.Assert(copied is >= 0 and <= IccPipeline.MaxChannels && copied == destinationStride - destinationChannels);
        Debug.Assert(!reverseColorOrder || (sourceChannels == 3 && destinationChannels == 3));
        var count = source.Length / sourceStride;
        var byteTables = typeof(T) == typeof(byte) ? pipeline.ByteInputTables : null;
        var outputTables = byteTables is not null ? pipeline.GetByteOutputTables(count) : null;
        var firstStage = byteTables is not null ? 1 : 0;
        var endStage = outputTables is not null ? pipeline.StageCount - 1 : pipeline.StageCount;

        // Component c of color i of the batch is at c * BatchSize + i; the samples copied unchanged are kept apart, so that
        // the whole batch is read before anything is written (the spans may be the same)
        Span<double> components = stackalloc double[IccPipeline.MaxChannels * IccPipeline.BatchSize];
        Span<T> kept = stackalloc T[IccPipeline.MaxChannels * IccPipeline.BatchSize];
        for (var start = 0; start < count; start += IccPipeline.BatchSize)
        {
            var batch = Math.Min(IccPipeline.BatchSize, count - start);
            var input = source.Slice(start * sourceStride, batch * sourceStride);
            for (var channel = 0; channel < sourceChannels; channel++)
            {
                var values = components.Slice(channel * IccPipeline.BatchSize, batch);
                var offset = reverseColorOrder ? 2 - channel : channel;
                if (byteTables is not null)
                {
                    LoadFromTable(byteTables[channel], unsafe(MemoryMarshal.Cast<T, byte>(input)), offset, sourceStride, values);
                }
                else
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = TSample.Load(input[(i * sourceStride) + offset]);
                    }
                }
            }

            for (var i = 0; i < batch && copied > 0; i++)
            {
                input.Slice((i * sourceStride) + sourceChannels, copied).CopyTo(kept[(i * copied)..]);
            }

            pipeline.ApplyBatch(components, batch, firstStage, endStage);

            var output = destination.Slice(start * destinationStride, batch * destinationStride);
            for (var channel = 0; channel < destinationChannels; channel++)
            {
                var values = components.Slice(channel * IccPipeline.BatchSize, batch);
                var offset = reverseColorOrder ? 2 - channel : channel;
                if (outputTables is not null)
                {
                    var table = outputTables[channel];
                    var bytes = unsafe(MemoryMarshal.Cast<T, byte>(output));
                    for (var i = 0; i < values.Length; i++)
                    {
                        bytes[(i * destinationStride) + offset] = (byte)table.Quantize(values[i]);
                    }
                }
                else
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        output[(i * destinationStride) + offset] = TSample.Store(values[i]);
                    }
                }
            }

            for (var i = 0; i < batch && copied > 0; i++)
            {
                kept.Slice(i * copied, copied).CopyTo(output[((i * destinationStride) + destinationChannels)..]);
            }
        }
    }

    // Reads the first stage of one channel of a batch from its table
    private static void LoadFromTable(ReadOnlySpan<double> table, ReadOnlySpan<byte> input, int offset, int stride, Span<double> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = table[input[(i * stride) + offset]];
        }
    }

    /// <summary>
    /// Converts interleaved 8-bit colors like <see cref="ConvertBatches"/>, for the conversions that interpolate a color lookup
    /// table (<see cref="IccPipeline.HasLookupTable"/>, hundreds of operations per color). The conversion of a color depends
    /// only on its samples, and photographs repeat colors within a row (flat or smooth areas): the colors of the call are
    /// remembered in a small direct-mapped cache (<see cref="CacheSize"/> entries, keyed by the packed source samples), and
    /// a color found there gets the samples computed for it before. Matrix-based conversions are cheaper than the cache when
    /// colors do not repeat.
    /// </summary>
    private static void ConvertBytesCached(IccPipeline pipeline, double[][] tables, ReadOnlySpan<byte> source, Span<byte> destination, int sourceStride, int destinationStride, bool reverseColorOrder)
    {
        var sourceChannels = pipeline.SourceChannelCount;
        var destinationChannels = pipeline.DestinationChannelCount;
        var copied = sourceStride - sourceChannels;
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        Span<byte> kept = stackalloc byte[IccPipeline.MaxChannels];

        // An entry is (source samples << 1) | 1, so that the zeroed entries of the new cache match no color; the result is
        // the destination samples packed the same way
        Span<ulong> cachedColors = stackalloc ulong[CacheSize];
        Span<uint> cachedResults = stackalloc uint[CacheSize];
        kept = kept[..copied];
        var count = source.Length / sourceStride;
        var outputTables = pipeline.GetByteOutputTables(count);
        for (var i = 0; i < count; i++)
        {
            // The whole source color is read before the destination is written: the spans may be the same
            var input = source.Slice(i * sourceStride, sourceStride);
            var color = 0u;
            for (var channel = 0; channel < sourceChannels; channel++)
            {
                color |= (uint)input[reverseColorOrder ? 2 - channel : channel] << (8 * channel);
            }

            input[sourceChannels..].CopyTo(kept);
            var slot = (int)((color * 2654435761u) >> (32 - CacheBits));
            var entry = ((ulong)color << 1) | 1;
            uint result;
            if (cachedColors[slot] == entry)
            {
                result = cachedResults[slot];
            }
            else
            {
                for (var channel = 0; channel < sourceChannels; channel++)
                {
                    values[channel] = tables[channel][(byte)(color >> (8 * channel))];
                }

                result = 0;
                if (outputTables is null)
                {
                    pipeline.ApplyAfterInputCurves(values);
                    for (var channel = 0; channel < destinationChannels; channel++)
                    {
                        result |= (uint)IccByteSample.Store(values[channel]) << (8 * channel);
                    }
                }
                else
                {
                    pipeline.ApplyBetweenCurves(values);
                    for (var channel = 0; channel < destinationChannels; channel++)
                    {
                        result |= (uint)outputTables[channel].Quantize(values[channel]) << (8 * channel);
                    }
                }

                cachedColors[slot] = entry;
                cachedResults[slot] = result;
            }

            var output = destination.Slice(i * destinationStride, destinationStride);
            for (var channel = 0; channel < destinationChannels; channel++)
            {
                output[reverseColorOrder ? 2 - channel : channel] = (byte)(result >> (8 * channel));
            }

            kept.CopyTo(output[destinationChannels..]);
        }
    }

    /// <summary>Converts interleaved colors by evaluating every stage for each color: the numerical reference of <see cref="Convert"/>.</summary>
    internal static void ConvertScalar<T, TSample>(IccPipeline pipeline, ReadOnlySpan<T> source, Span<T> destination, int sourceStride, int destinationStride, bool reverseColorOrder = false)
        where T : unmanaged
        where TSample : struct, IIccSample<T>
    {
        var sourceChannels = pipeline.SourceChannelCount;
        var destinationChannels = pipeline.DestinationChannelCount;
        var copied = sourceStride - sourceChannels;
        Debug.Assert(copied is >= 0 and <= IccPipeline.MaxChannels && copied == destinationStride - destinationChannels);
        Debug.Assert(!reverseColorOrder || (sourceChannels == 3 && destinationChannels == 3));
        if (pipeline.IsIdentity)
        {
            // Identical profiles: the samples are kept exactly, whatever their type
            source.CopyTo(destination);
            return;
        }

        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        Span<T> kept = stackalloc T[IccPipeline.MaxChannels];
        kept = kept[..copied];
        var count = source.Length / sourceStride;
        for (var i = 0; i < count; i++)
        {
            // The whole source color is read before the destination is written: the spans may be the same
            var input = source.Slice(i * sourceStride, sourceStride);
            for (var channel = 0; channel < sourceChannels; channel++)
            {
                values[channel] = TSample.Load(input[reverseColorOrder ? 2 - channel : channel]);
            }

            input[sourceChannels..].CopyTo(kept);
            pipeline.Apply(values);

            var output = destination.Slice(i * destinationStride, destinationStride);
            for (var channel = 0; channel < destinationChannels; channel++)
            {
                output[reverseColorOrder ? 2 - channel : channel] = TSample.Store(values[channel]);
            }

            kept.CopyTo(output[destinationChannels..]);
        }
    }
}
