using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
    /// 8 bits, floating-point samples are stored as the nearest <see cref="float"/> or <see cref="Half"/> of the result,
    /// clipped to [0, 1] like every result), alpha is copied unchanged, hidden colors of transparent pixels included.
    /// The caller has checked that floating-point colors are numbers in [0, 1].
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of both storages; its color channels match the channel counts of the conversion.</typeparam>
    /// <param name="source">The leased source pixels.</param>
    /// <param name="destination">The leased destination pixels.</param>
    /// <param name="pipeline">The conversion.</param>
    /// <param name="uint16Tables">The values of the first stage for 16-bit samples, or <see langword="null"/> to evaluate it.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers: the rows of a large frame are split into bands (<see cref="RowBands"/>), and each row is converted alone, so the pixels do not depend on the number of workers.</param>
    /// <param name="cancellationToken">The token checked between row bands.</param>
    public static void ConvertRows<TPixel>(scoped in PixelLease source, scoped in PixelLease destination, IccPipeline pipeline, IccUInt16InputTables? uint16Tables, int maxDegreeOfParallelism, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var sourceStorage = source.Storage;
        var destinationStorage = destination.Storage;
        var height = sourceStorage.Height;
        var workers = RowBands.GetWorkerCount(new Size(sourceStorage.Width, height), maxDegreeOfParallelism);
        if (workers == 1)
        {
            ConvertRows<TPixel>(sourceStorage, destinationStorage, pipeline, uint16Tables, 0, height, cancellationToken);
            return;
        }

        // The pipeline is thread-safe: its lookup tables are built once, whichever worker needs them first, and every
        // color gets the samples of the scalar reference with or without them
        RowBands.RunParallelInBandOrder(workers, band => ConvertRows<TPixel>(sourceStorage, destinationStorage, pipeline, uint16Tables, RowBands.GetBandStart(height, band, workers), RowBands.GetBandStart(height, band + 1, workers), cancellationToken));
    }

    private static void ConvertRows<TPixel>(PixelStorage source, PixelStorage destination, IccPipeline pipeline, IccUInt16InputTables? uint16Tables, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (typeof(TPixel) == typeof(Rgba32))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowSpanCore(y), destination.GetRowSpanCore(y), 4, 4);
            }
            else if (typeof(TPixel) == typeof(Bgra32))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowSpanCore(y), destination.GetRowSpanCore(y), 4, 4, reverseColorOrder: true);
            }
            else if (typeof(TPixel) == typeof(Rgb24))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowSpanCore(y), destination.GetRowSpanCore(y), 3, 3);
            }
            else if (typeof(TPixel) == typeof(Gray8))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowSpanCore(y), destination.GetRowSpanCore(y), 1, 1);
            }
            else if (typeof(TPixel) == typeof(Rgba64))
            {
                Convert<ushort, IccUInt16Sample>(pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, ushort>(destination.GetRowSpanCore(y))), 4, 4, uint16Tables: uint16Tables);
            }
            else if (typeof(TPixel) == typeof(Gray16))
            {
                Convert<ushort, IccUInt16Sample>(pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, ushort>(destination.GetRowSpanCore(y))), 1, 1, uint16Tables: uint16Tables);
            }
            else if (typeof(TPixel) == typeof(La16))
            {
                Convert<byte, IccByteSample>(pipeline, source.GetRowSpanCore(y), destination.GetRowSpanCore(y), 2, 2);
            }
            else if (typeof(TPixel) == typeof(La32))
            {
                Convert<ushort, IccUInt16Sample>(pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, ushort>(destination.GetRowSpanCore(y))), 2, 2, uint16Tables: uint16Tables);
            }
            else if (typeof(TPixel) == typeof(Rgb48))
            {
                Convert<ushort, IccUInt16Sample>(pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, ushort>(destination.GetRowSpanCore(y))), 3, 3, uint16Tables: uint16Tables);
            }
            else if (typeof(TPixel) == typeof(RgbaFloat))
            {
                Convert<float, IccSingleSample>(pipeline, unsafe(MemoryMarshal.Cast<byte, float>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, float>(destination.GetRowSpanCore(y))), 4, 4);
            }
            else if (typeof(TPixel) == typeof(RgbaHalf))
            {
                Convert<Half, IccHalfSample>(pipeline, unsafe(MemoryMarshal.Cast<byte, Half>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, Half>(destination.GetRowSpanCore(y))), 4, 4);
            }
            else if (typeof(TPixel) == typeof(GrayFloat) || typeof(TPixel) == typeof(LaFloat))
            {
                var channels = typeof(TPixel) == typeof(LaFloat) ? 2 : 1;
                Convert<float, IccSingleSample>(pipeline, unsafe(MemoryMarshal.Cast<byte, float>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, float>(destination.GetRowSpanCore(y))), channels, channels);
            }
            else if (typeof(TPixel) == typeof(GrayHalf) || typeof(TPixel) == typeof(LaHalf))
            {
                var channels = typeof(TPixel) == typeof(LaHalf) ? 2 : 1;
                Convert<Half, IccHalfSample>(pipeline, unsafe(MemoryMarshal.Cast<byte, Half>(source.GetRowSpanCore(y))), unsafe(MemoryMarshal.Cast<byte, Half>(destination.GetRowSpanCore(y))), channels, channels);
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
    /// <param name="uint16Tables">The values of the first stage for 16-bit samples, or <see langword="null"/> to evaluate it.</param>
    public static void Convert<T, TSample>(IccPipeline pipeline, ReadOnlySpan<T> source, Span<T> destination, int sourceStride, int destinationStride, bool reverseColorOrder = false, IccUInt16InputTables? uint16Tables = null)
        where T : unmanaged
        where TSample : struct, IIccSample<T>
    {
        if (pipeline.IsIdentity)
        {
            ConvertScalar<T, TSample>(pipeline, source, destination, sourceStride, destinationStride, reverseColorOrder);
            return;
        }

        if (pipeline.HasLookupTable)
        {
            pipeline.ReserveLookupTables(source.Length / sourceStride);
        }

        if (typeof(T) == typeof(byte) && pipeline.HasLookupTable && pipeline.ByteInputTables is { } tables)
        {
            ConvertBytesCached(pipeline, tables, unsafe(MemoryMarshal.Cast<T, byte>(source)), unsafe(MemoryMarshal.Cast<T, byte>(destination)), sourceStride, destinationStride, reverseColorOrder);
            return;
        }

        ConvertBatches<T, TSample>(pipeline, source, destination, sourceStride, destinationStride, reverseColorOrder, typeof(T) == typeof(ushort) ? uint16Tables : null);
    }

    /// <summary>
    /// Converts interleaved colors like <see cref="ConvertScalar"/>, <see cref="IccPipeline.BatchSize"/> colors at a time
    /// stored by component (<see cref="IccPipeline.ApplyBatch"/>), so that each stage loops over the colors instead of
    /// being called for each of them. The first stage of 8-bit colors (one curve per source channel) is read from the
    /// tables of the pipeline, as is the first stage of 16-bit colors when <paramref name="uint16Tables"/> are given, and
    /// once the pipeline has them, the 8-bit samples of the last stage (one curve per destination channel) are read from
    /// its threshold tables: every color gets the samples of <see cref="ConvertScalar"/>.
    /// </summary>
    private static void ConvertBatches<T, TSample>(IccPipeline pipeline, ReadOnlySpan<T> source, Span<T> destination, int sourceStride, int destinationStride, bool reverseColorOrder, IccUInt16InputTables? uint16Tables)
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
        var firstStage = byteTables is not null || uint16Tables is not null ? 1 : 0;
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
                else if (uint16Tables is not null)
                {
                    LoadFromTable(uint16Tables.GetTable(channel), unsafe(MemoryMarshal.Cast<T, ushort>(input)), offset, sourceStride, values);
                }
                else
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = TSample.Load(input[(i * sourceStride) + offset]);
                    }
                }
            }

            // A sample at a time: the copies of one or two samples are shorter than a call
            for (var i = 0; i < batch && copied > 0; i++)
            {
                for (var c = 0; c < copied; c++)
                {
                    kept[(i * copied) + c] = input[(i * sourceStride) + sourceChannels + c];
                }
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
                    var i = 0;
                    if (Vector128.IsHardwareAccelerated && !table.Intervals.IsEmpty)
                    {
                        // Two values at a time, one comparison from their interval: the samples of Quantize(double)
                        ref var intervals = ref unsafe(MemoryMarshal.GetReference(table.Intervals));
                        for (; i + 2 <= values.Length; i += 2)
                        {
                            var samples = QuantizationThresholds.Quantize(Vector128.Create(values.Slice(i, 2)), ref intervals);
                            bytes[(i * destinationStride) + offset] = (byte)samples.GetElement(0);
                            bytes[((i + 1) * destinationStride) + offset] = (byte)samples.GetElement(1);
                        }
                    }

                    for (; i < values.Length; i++)
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
                for (var c = 0; c < copied; c++)
                {
                    output[(i * destinationStride) + destinationChannels + c] = kept[(i * copied) + c];
                }
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

    private static void LoadFromTable(ReadOnlySpan<double> table, ReadOnlySpan<ushort> input, int offset, int stride, Span<double> values)
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
    /// a color found there gets the samples computed for it before. The other colors of each group of
    /// <see cref="IccPipeline.BatchSize"/> pixels are converted together, stored by component. Matrix-based conversions
    /// are cheaper than the cache when colors do not repeat.
    /// </summary>
    private static void ConvertBytesCached(IccPipeline pipeline, double[][] tables, ReadOnlySpan<byte> source, Span<byte> destination, int sourceStride, int destinationStride, bool reverseColorOrder)
    {
        const int BatchSize = IccPipeline.BatchSize;
        var sourceChannels = pipeline.SourceChannelCount;
        var destinationChannels = pipeline.DestinationChannelCount;
        var copied = sourceStride - sourceChannels;
        var count = source.Length / sourceStride;
        var outputTables = pipeline.GetByteOutputTables(count);
        var endStage = outputTables is not null ? pipeline.StageCount - 1 : pipeline.StageCount;

        // An entry is (source samples << 1) | 1, so that the zeroed entries of the new cache match no color; the result is
        // the destination samples packed the same way. A color that is not in the cache is added to a batch of the colors
        // that are converted together, stage by stage (IccPipeline.ApplyBatch, the same arithmetic for each color), and its
        // slot is pending until then: the next pixels of the same color take the result of its batch entry
        Span<ulong> cachedColors = stackalloc ulong[CacheSize];
        Span<uint> cachedResults = stackalloc uint[CacheSize];
        Span<byte> pendingSlots = stackalloc byte[CacheSize];
        Span<double> components = stackalloc double[IccPipeline.MaxChannels * BatchSize];
        Span<byte> batchSlots = stackalloc byte[BatchSize];
        Span<uint> batchResults = stackalloc uint[BatchSize];
        Span<uint> results = stackalloc uint[BatchSize];
        Span<int> fromBatch = stackalloc int[BatchSize];
        Span<byte> kept = stackalloc byte[IccPipeline.MaxChannels * BatchSize];
        for (var start = 0; start < count; start += BatchSize)
        {
            // The whole chunk is read before it is written: the spans may be the same
            var chunk = Math.Min(BatchSize, count - start);
            var batch = 0;
            for (var k = 0; k < chunk; k++)
            {
                var input = source.Slice((start + k) * sourceStride, sourceStride);
                uint color;
                if (sourceChannels == 4)
                {
                    // The packing of the loop below: the first sample in the low byte
                    color = BinaryPrimitives.ReadUInt32LittleEndian(input);
                }
                else
                {
                    color = 0u;
                    for (var channel = 0; channel < sourceChannels; channel++)
                    {
                        color |= (uint)input[reverseColorOrder ? 2 - channel : channel] << (8 * channel);
                    }
                }

                if (copied > 0)
                {
                    input[sourceChannels..].CopyTo(kept[(k * copied)..]);
                }
                var slot = (int)((color * 2654435761u) >> (32 - CacheBits));
                var entry = ((ulong)color << 1) | 1;
                if (cachedColors[slot] == entry)
                {
                    var pending = pendingSlots[slot];
                    fromBatch[k] = pending - 1;
                    results[k] = cachedResults[slot];
                    continue;
                }

                for (var channel = 0; channel < sourceChannels; channel++)
                {
                    components[(channel * BatchSize) + batch] = tables[channel][(byte)(color >> (8 * channel))];
                }

                cachedColors[slot] = entry;
                pendingSlots[slot] = (byte)(batch + 1);
                batchSlots[batch] = (byte)slot;
                fromBatch[k] = batch;
                batch++;
            }

            if (batch > 0)
            {
                pipeline.ApplyBatch(components, batch, first: 1, endStage);
                for (var b = 0; b < batch; b++)
                {
                    var result = 0u;
                    for (var channel = 0; channel < destinationChannels; channel++)
                    {
                        var value = components[(channel * BatchSize) + b];
                        result |= (uint)(outputTables is null ? IccByteSample.Store(value) : outputTables[channel].Quantize(value)) << (8 * channel);
                    }

                    batchResults[b] = result;

                    // A later color of the chunk may have taken the slot: it stays pending for that color
                    var slot = batchSlots[b];
                    if (pendingSlots[slot] == b + 1)
                    {
                        cachedResults[slot] = result;
                        pendingSlots[slot] = 0;
                    }
                }
            }

            for (var k = 0; k < chunk; k++)
            {
                var result = fromBatch[k] < 0 ? results[k] : batchResults[fromBatch[k]];
                var output = destination.Slice((start + k) * destinationStride, destinationStride);
                for (var channel = 0; channel < destinationChannels; channel++)
                {
                    output[reverseColorOrder ? 2 - channel : channel] = (byte)(result >> (8 * channel));
                }

                if (copied > 0)
                {
                    kept.Slice(k * copied, copied).CopyTo(output[destinationChannels..]);
                }
            }
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
