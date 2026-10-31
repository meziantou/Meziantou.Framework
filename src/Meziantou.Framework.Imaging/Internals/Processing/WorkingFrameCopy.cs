using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A copy of a frame as single-precision working samples (<see cref="WorkingSamples"/>), for the filters that read any
/// pixel of the frame while they rewrite it (<see cref="RadialBlurrer"/>, <see cref="BilateralFilterer"/>,
/// <see cref="GuidedFilterer"/>), and the row buffers of the workers that store their results. One copy serves every frame
/// of an image.
/// </summary>
/// <remarks>
/// <para>
/// The working samples are those of the convolution: the stored values (or their linear light), the colors premultiplied
/// by the alpha sample when the format has alpha. When every pixel of a frame has the maximum alpha and the edge mode
/// reads no pixel beyond the frame, the copy holds the three colors only (<see cref="WorkingAlpha.Opaque"/>), like the
/// blurs. The copy is a pixel storage of the owner (rows of 4 bytes per working sample), so it is bounded by the limits of
/// the image like its pixels; the row buffers are charged to its scope. Both are rented by <see cref="Prepare"/>, before any
/// pixel changes.
/// </para>
/// <para>
/// A single-precision working sample holds every 8-bit product exactly; a 16-bit product (up to 65,535²) is rounded to
/// 24 significant bits, about 0.004 of a 16-bit unit once divided by its alpha.
/// </para>
/// </remarks>
internal sealed class WorkingFrameCopy : IDisposable
{
    private readonly int _rowDoubles;
    private PixelStorage? _copy;
    private PooledBuffer?[] _rows = [];

    /// <summary>Initializes a new instance of the <see cref="WorkingFrameCopy"/> class.</summary>
    /// <param name="format">The pixel format the kernel works in (<see cref="ProcessingBridge.GetKernelFormat"/>).</param>
    /// <param name="size">The size of the frames.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the frames.</param>
    /// <param name="workers">The number of workers, each with row buffers of its own.</param>
    /// <param name="transfer">The transfer characteristics of linear-light filtering, or 0 to filter the stored values.</param>
    /// <param name="rowDoublesPerWorker">The number of doubles of the buffer of a worker, at least one working row.</param>
    public WorkingFrameCopy(PixelFormat format, Size size, ConvolutionEdgeMode edgeMode, int workers, byte transfer, int rowDoublesPerWorker)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);
        Format = format;
        Size = size;
        EdgeMode = edgeMode;
        Workers = workers;
        Transfer = transfer;
        Channels = PixelFormats.GetComponentCount(format);
        Alpha = PixelFormats.HasAlpha(format) ? WorkingAlpha.Premultiplied : WorkingAlpha.None;
        MaxValue = WorkingSamples.GetMaxValue(format);
        EffectiveAlpha = Alpha;
        _rowDoubles = Math.Max(rowDoublesPerWorker, size.Width * Channels);
        _ = GetSampleKind(format);
    }

    /// <summary>Gets the pixel format of the frames.</summary>
    public PixelFormat Format { get; }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets the pixels the filter reads beyond the edges of the frames.</summary>
    public ConvolutionEdgeMode EdgeMode { get; }

    /// <summary>Gets the number of workers.</summary>
    public int Workers { get; }

    /// <summary>Gets the transfer characteristics whose function decodes the samples to linear light, or 0 when they are filtered as stored.</summary>
    public byte Transfer { get; }

    /// <summary>Gets the number of stored samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets how alpha takes part in the working samples, before the opaque shortcut.</summary>
    public WorkingAlpha Alpha { get; }

    /// <summary>Gets how alpha takes part in the working samples of the frame last loaded.</summary>
    public WorkingAlpha EffectiveAlpha { get; private set; }

    /// <summary>Gets the number of working samples per pixel of the frame last loaded: the colors only for <see cref="WorkingAlpha.Opaque"/>.</summary>
    public int WorkingChannels => EffectiveAlpha == WorkingAlpha.Opaque ? 3 : Channels;

    /// <summary>Gets the largest stored sample (<see cref="WorkingSamples.GetMaxValue"/>).</summary>
    public int MaxValue { get; }

    /// <summary>
    /// Gets the factor that brings a working color of the frame last loaded to a fraction of the sample range: a
    /// premultiplied color is the product of two samples.
    /// </summary>
    public double ColorScale => EffectiveAlpha == WorkingAlpha.Premultiplied ? 1d / ((double)MaxValue * MaxValue) : 1d / MaxValue;

    /// <summary>Gets the factor that brings a working alpha to a fraction of the sample range.</summary>
    public double AlphaScale => 1d / MaxValue;

    /// <summary>Gets the image row read for row <paramref name="y"/>, which may be outside the frame, or -1 when it reads transparent black.</summary>
    public int MapRow(int y) => ConvolutionPlan.MapIndex(y, Size.Height, EdgeMode);

    /// <summary>Gets the image column read for column <paramref name="x"/>, which may be outside the frame, or -1 when it reads transparent black.</summary>
    public int MapColumn(int x) => ConvolutionPlan.MapIndex(x, Size.Width, EdgeMode);

    /// <summary>Rents the copy and the row buffers of the workers. Called once per operation, before any pixel changes.</summary>
    /// <exception cref="ImageResourceLimitException">The copy or the buffers exceed a limit; the copy must still be disposed.</exception>
    public void Prepare(StorageOwner owner)
    {
        _copy = owner.Allocate(Size.Width, Size.Height, Channels * sizeof(float), options: null, AllocationKind.Temporary);
        _rows = new PooledBuffer?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _rows[worker] = ResampleWeights.Rent<double>(owner.Scope, _rowDoubles);
        }
    }

    /// <summary>Gets the row buffer of a worker, between 0 and <see cref="Workers"/> - 1.</summary>
    public Span<double> GetRowBuffer(int worker)
        => unsafe(MemoryMarshal.Cast<byte, double>((_rows[worker] ?? throw new ObjectDisposedException(nameof(WorkingFrameCopy))).Span))[.._rowDoubles];

    /// <summary>Gets the working samples of row <paramref name="y"/> of the frame last loaded (<see cref="WorkingChannels"/> per pixel).</summary>
    public ReadOnlySpan<float> GetRow(int y)
        => unsafe(MemoryMarshal.Cast<byte, float>(GetCopy().GetRowSpanCore(y)))[..(Size.Width * WorkingChannels)];

    /// <summary>Copies the working samples of a frame of the format of the copy.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public void Load(PixelStorage storage, CancellationToken cancellationToken)
    {
        if (storage.Width != Size.Width || storage.Height != Size.Height || storage.BytesPerPixel != PixelFormats.GetBytesPerPixel(Format))
            throw new InvalidOperationException("The storage does not have the layout the filter was prepared for.");

        EffectiveAlpha = Alpha == WorkingAlpha.Premultiplied && EdgeMode != ConvolutionEdgeMode.Zero && IsOpaque(storage) ? WorkingAlpha.Opaque : Alpha;
        var row = GetRowBuffer(0)[..(Size.Width * WorkingChannels)];
        var copy = GetCopy();
        for (var y = 0; y < Size.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var samples = storage.GetRowSpanCore(y);
            switch (GetSampleKind(Format))
            {
                case SampleKind.Byte:
                    WorkingSamples.Load<byte>(samples[..(Size.Width * Channels)], row, EffectiveAlpha, Transfer);
                    break;
                case SampleKind.UInt16:
                    WorkingSamples.Load<ushort>(unsafe(MemoryMarshal.Cast<byte, ushort>(samples))[..(Size.Width * Channels)], row, EffectiveAlpha, Transfer);
                    break;
                case SampleKind.Single:
                    WorkingSamples.Load<float>(unsafe(MemoryMarshal.Cast<byte, float>(samples))[..(Size.Width * Channels)], row, EffectiveAlpha, Transfer);
                    break;
                default:
                    WorkingSamples.Load<Half>(unsafe(MemoryMarshal.Cast<byte, Half>(samples))[..(Size.Width * Channels)], row, EffectiveAlpha, Transfer);
                    break;
            }

            var destination = unsafe(MemoryMarshal.Cast<byte, float>(copy.GetRowSpanCore(y)));
            for (var i = 0; i < row.Length; i++)
            {
                destination[i] = (float)row[i];
            }
        }
    }

    /// <summary>Stores working samples (<see cref="WorkingChannels"/> per pixel) to row <paramref name="y"/> of a frame of the format of the copy.</summary>
    public void Store(PixelStorage storage, int y, ReadOnlySpan<double> values)
    {
        var samples = storage.GetRowSpanCore(y);
        switch (GetSampleKind(Format))
        {
            case SampleKind.Byte:
                WorkingSamples.Store(values, samples[..(Size.Width * Channels)], EffectiveAlpha, Transfer, MaxValue);
                break;
            case SampleKind.UInt16:
                WorkingSamples.Store(values, unsafe(MemoryMarshal.Cast<byte, ushort>(samples))[..(Size.Width * Channels)], EffectiveAlpha, Transfer, MaxValue);
                break;
            case SampleKind.Single:
                WorkingSamples.Store(values, unsafe(MemoryMarshal.Cast<byte, float>(samples))[..(Size.Width * Channels)], EffectiveAlpha, Transfer, MaxValue);
                break;
            default:
                WorkingSamples.Store(values, unsafe(MemoryMarshal.Cast<byte, Half>(samples))[..(Size.Width * Channels)], EffectiveAlpha, Transfer, MaxValue);
                break;
        }
    }

    public void Dispose()
    {
        foreach (var row in _rows)
        {
            row?.Dispose();
        }

        _rows = [];
        _copy?.Dispose();
        _copy = null;
    }

    private PixelStorage GetCopy() => _copy ?? throw new ObjectDisposedException(nameof(WorkingFrameCopy));

    private bool IsOpaque(PixelStorage storage)
    {
        var length = Size.Width * storage.BytesPerPixel;
        for (var y = 0; y < Size.Height; y++)
        {
            if (!PixelConverter.IsOpaque(Format, storage.GetRowSpanCore(y)[..length]))
                return false;
        }

        return true;
    }

    private static SampleKind GetSampleKind(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Gray8 => SampleKind.Byte,
        PixelFormat.Rgba64 or PixelFormat.Gray16 or PixelFormat.Rgb48 => SampleKind.UInt16,
        PixelFormat.RgbaFloat => SampleKind.Single,
        PixelFormat.RgbaHalf => SampleKind.Half,
        _ => throw new NotSupportedException($"The pixel format '{format}' is not supported."),
    };

    private enum SampleKind
    {
        Byte,
        UInt16,
        Single,
        Half,
    }
}
