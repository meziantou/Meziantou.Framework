using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A function of the colors of pixels, for the tone operations that give each color a new one from a table or a curve:
/// <see cref="ImageProcessingExtensions.ApplyToneCurves(Image, ToneCurve, ToneCurve, ToneCurve, CancellationToken)"/> and
/// <see cref="ImageProcessingExtensions.ApplyLut(Image, CubeLut, LutInterpolation, CancellationToken)"/>. Values are
/// fractions of the largest sample value; alpha is never read nor changed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Prepare"/> rents the working memory and <see cref="Run.Apply"/> remaps frames in place. Integer samples are read at the precision of their kernel format
/// (<see cref="ToneSamples"/>), mapped, and rounded to the nearest level (ties upward), clamped to the sample range. A
/// function of each channel alone (<see cref="IsPerChannel"/>) is tabulated once per level; a function of whole colors is
/// evaluated for every pixel. A gray pixel is mapped as the color of three equal samples, whose rounded result is stored
/// as its Rec. 709 luma (<see cref="PixelConverter.Luma(uint, uint, uint, uint)"/>), which gives the result of the
/// processing bridge (<see cref="ProcessingBridge"/>) and keeps a gray result exact; the table of a gray format has one
/// entry per level. Floating-point samples are mapped as stored and stored without clamping.
/// </para>
/// <para>
/// The working memory (a row, and the tables) is charged to the scope as <see cref="AllocationKind.Temporary"/> before a
/// pixel changes. Cancellation is checked every <see cref="ProcessingKernels.RowsPerCancellationCheck"/> rows.
/// </para>
/// </remarks>
internal abstract class ColorLookup
{
    /// <summary>Gets a value indicating whether each output channel depends only on the same input channel (<see cref="MapChannel"/>).</summary>
    public abstract bool IsPerChannel { get; }

    /// <summary>Maps one channel (0 red, 1 green, 2 blue). Only called when <see cref="IsPerChannel"/> is set.</summary>
    public abstract double MapChannel(int channel, double value);

    /// <summary>Maps a color.</summary>
    public abstract void MapColor(ref double red, ref double green, ref double blue);

    /// <summary>Rents the working memory of a remapping and fills the tables. Called before a pixel changes.</summary>
    /// <param name="scope">The scope charged for the working memory.</param>
    /// <param name="format">The pixel format of the frames.</param>
    /// <param name="size">The size of the frames.</param>
    /// <returns>The prepared remapping, to dispose.</returns>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    public Run Prepare(AllocationScope scope, PixelFormat format, Size size) => new(this, scope, format, size);

    /// <summary>A prepared remapping: the working memory of the frames of one format and size.</summary>
    internal sealed class Run : IDisposable
    {
        private readonly ColorLookup _lookup;
        private readonly PixelFormat _format;
        private readonly Size _size;
        private readonly int _levels;
        private readonly bool _gray;
        private readonly bool _tabulated;
        private PooledBuffer? _row;
        private PooledBuffer? _tables;

        public Run(ColorLookup lookup, AllocationScope scope, PixelFormat format, Size size)
        {
            _lookup = lookup;
            _format = format;
            _size = size;
            if (!PixelFormats.HasColor(format) || PixelFormats.IsFloatingPoint(format))
                return;

            _levels = ToneSamples.GetLevelCount(format);
            _gray = PixelFormats.IsGrayscale(format);
            _tabulated = _gray || lookup.IsPerChannel;
            try
            {
                _row = ToneSamples.RentRow(scope, size.Width);
                if (_tabulated)
                {
                    _tables = scope.Rent((_gray ? 1 : 3) * _levels * sizeof(ushort), AllocationKind.Temporary, clear: false);
                    lookup.BuildTables(Tables, _levels, _gray);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private Span<ushort> Tables => unsafe(MemoryMarshal.Cast<byte, ushort>((_tables ?? throw new ObjectDisposedException(nameof(Run))).Span))[..((_gray ? 1 : 3) * _levels)];

        /// <summary>Remaps the colors of frames in place; <see cref="PixelFormat.A8"/> frames are left unchanged.</summary>
        /// <param name="frames">The frames, all of the format and size given at creation.</param>
        /// <param name="cancellationToken">The token checked between rows.</param>
        public void Apply(ImageFrame[] frames, CancellationToken cancellationToken)
        {
            if (!PixelFormats.HasColor(_format))
                return;

            foreach (var frame in frames)
            {
                using var lease = frame.GetStorage().AcquireLease();
                switch (_format)
                {
                    case PixelFormat.RgbaFloat:
                        _lookup.ApplyFloat<float>(lease, _size, cancellationToken);
                        break;
                    case PixelFormat.RgbaHalf:
                        _lookup.ApplyFloat<Half>(lease, _size, cancellationToken);
                        break;
                    default:
                        ApplyInteger(lease, cancellationToken);
                        break;
                }
            }
        }

        public void Dispose()
        {
            _row?.Dispose();
            _row = null;
            _tables?.Dispose();
            _tables = null;
        }

        private void ApplyInteger(scoped in PixelLease lease, CancellationToken cancellationToken)
        {
            var levels = _levels;
            var tables = _tabulated ? Tables : [];
            var samples = ToneSamples.GetSamples(_row ?? throw new ObjectDisposedException(nameof(Run)), _size.Width);
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_format);
            var max = levels - 1;
            var scale = 1.0 / max;
            for (var y = 0; y < _size.Height; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var pixels = lease.GetRowBytes(y)[..(_size.Width * bytesPerPixel)];
                ToneSamples.Read(_format, pixels, samples);
                if (_gray)
                {
                    // The gray level is the red sample: WriteColors stores it
                    for (var i = 0; i < samples.Length; i += 4)
                    {
                        samples[i] = tables[samples[i]];
                    }
                }
                else if (_tabulated)
                {
                    var red = tables[..levels];
                    var green = tables.Slice(levels, levels);
                    var blue = tables.Slice(2 * levels, levels);
                    for (var i = 0; i < samples.Length; i += 4)
                    {
                        samples[i] = red[samples[i]];
                        samples[i + 1] = green[samples[i + 1]];
                        samples[i + 2] = blue[samples[i + 2]];
                    }
                }
                else
                {
                    for (var i = 0; i < samples.Length; i += 4)
                    {
                        var r = samples[i] * scale;
                        var g = samples[i + 1] * scale;
                        var b = samples[i + 2] * scale;
                        _lookup.MapColor(ref r, ref g, ref b);
                        samples[i] = Quantize(r, max);
                        samples[i + 1] = Quantize(g, max);
                        samples[i + 2] = Quantize(b, max);
                    }
                }

                ToneSamples.WriteColors(_format, samples, pixels);
            }
        }
    }

    /// <summary>Rounds a fraction of the largest sample to the nearest level, ties upward, clamped to [0, <paramref name="max"/>].</summary>
    internal static ushort Quantize(double value, int max)
    {
        // A value that is not a number cannot come from finite tables and curves; it would become 0
        if (!(value > 0))
            return 0;

        if (value >= 1)
            return (ushort)max;

        return (ushort)Math.Min(Math.Floor((value * max) + 0.5), max);
    }

    /// <summary>Fills the tables of the levels: one per color channel, or one for a gray format (the luma of the mapped gray color).</summary>
    internal void BuildTables(Span<ushort> tables, int levels, bool gray)
    {
        var max = levels - 1;
        var scale = 1.0 / max;
        for (var level = 0; level < levels; level++)
        {
            var value = level * scale;
            double r, g, b;
            if (IsPerChannel)
            {
                r = MapChannel(0, value);
                g = MapChannel(1, value);
                b = MapChannel(2, value);
            }
            else
            {
                r = value;
                g = value;
                b = value;
                MapColor(ref r, ref g, ref b);
            }

            if (gray)
            {
                tables[level] = (ushort)PixelConverter.Luma(Quantize(r, max), Quantize(g, max), Quantize(b, max), (uint)max);
            }
            else
            {
                tables[level] = Quantize(r, max);
                tables[levels + level] = Quantize(g, max);
                tables[(2 * levels) + level] = Quantize(b, max);
            }
        }
    }

    internal void ApplyFloat<TSample>(scoped in PixelLease lease, Size size, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        for (var y = 0; y < size.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var samples = unsafe(MemoryMarshal.Cast<byte, TSample>(lease.GetRowBytes(y)))[..(4 * size.Width)];
            for (var i = 0; i < samples.Length; i += 4)
            {
                var r = WorkingSamples.ToDouble(samples[i]);
                var g = WorkingSamples.ToDouble(samples[i + 1]);
                var b = WorkingSamples.ToDouble(samples[i + 2]);
                if (IsPerChannel)
                {
                    r = MapChannel(0, r);
                    g = MapChannel(1, g);
                    b = MapChannel(2, b);
                }
                else
                {
                    MapColor(ref r, ref g, ref b);
                }

                samples[i] = FromDouble<TSample>(r);
                samples[i + 1] = FromDouble<TSample>(g);
                samples[i + 2] = FromDouble<TSample>(b);
            }
        }
    }

    private static TSample FromDouble<TSample>(double value)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(float))
        {
            var f = (float)value;
            return unsafe(Unsafe.As<float, TSample>(ref f));
        }

        var h = (Half)value;
        return unsafe(Unsafe.As<Half, TSample>(ref h));
    }
}
