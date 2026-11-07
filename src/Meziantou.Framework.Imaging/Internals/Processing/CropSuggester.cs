using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Suggests the crop of a target aspect ratio that keeps the most content
/// (<see cref="ImageProcessingExtensions.SuggestCrop(Image, int, int, CropSuggestionOptions?, CancellationToken)"/>): every
/// pixel gets an integer energy, the energies of the frames are added, and the rectangle of the requested size whose
/// energy is the largest is found with a summed-area table.
/// </summary>
/// <remarks>
/// <para>
/// The frames are read as fractions (<see cref="WideRowReader"/>), clamped to [0, 1], with the colors premultiplied by
/// alpha: a transparent pixel is black. <see cref="CropEnergy.Edges"/> is the absolute difference of the luma (BT.601
/// weights of 16-bit samples, times 1,000, so every value is an integer) and of the alpha (times 1,000) of every pixel with
/// its right neighbor and with its lower neighbor (none beyond the edges): unlike a Sobel gradient, it sees the finest
/// textures, down to a checkerboard of single pixels. <see cref="CropEnergy.Saliency"/> is the frequency-tuned saliency of Achanta et al.
/// (2009) in Oklab: the distance between the color of every pixel, blurred by the binomial filter <c>[1 4 6 4 1] / 16</c>
/// in both directions, and the mean color of the frame, rounded to 1/65,536.
/// </para>
/// <para>
/// The energies are integers, so the sums of the summed-area table are exact and the ties are exact: among the positions of
/// the largest energy, the one nearest to the center of the frame (by the sum of the horizontal and vertical distances)
/// wins, then the topmost, then the leftmost. A frame without energy (a flat color) therefore gets the centered crop. The
/// table (8 bytes per pixel) is a storage of the owner of the image; the row buffers are charged to its scope. The frames
/// are read on the calling thread.
/// </para>
/// </remarks>
internal static class CropSuggester
{
    /// <summary>Gets the size of the crop: the largest rectangle of the aspect ratio inside the frame, scaled and rounded to whole pixels (at least 1×1).</summary>
    public static Size GetCropSize(Size size, int aspectWidth, int aspectHeight, double scale)
    {
        int width;
        int height;
        if ((long)size.Width * aspectHeight >= (long)size.Height * aspectWidth)
        {
            height = Math.Clamp((int)Math.Round(size.Height * scale, MidpointRounding.AwayFromZero), 1, size.Height);
            width = Math.Clamp((int)Math.Round((double)height * aspectWidth / aspectHeight, MidpointRounding.AwayFromZero), 1, size.Width);
        }
        else
        {
            width = Math.Clamp((int)Math.Round(size.Width * scale, MidpointRounding.AwayFromZero), 1, size.Width);
            height = Math.Clamp((int)Math.Round((double)width * aspectHeight / aspectWidth, MidpointRounding.AwayFromZero), 1, size.Height);
        }

        return new Size(width, height);
    }

    /// <summary>Gets the energy table of frames of a size and a format, empty; the caller disposes it.</summary>
    /// <exception cref="ImageResourceLimitException">The table or the buffers exceed a limit.</exception>
    public static EnergyTable CreateTable(StorageOwner owner, PixelFormat format, Size size) => new(owner, format, size);

    private static Span<long> GetTableRow(PixelStorage table, int y, int width) => unsafe(MemoryMarshal.Cast<byte, long>(table.GetRowSpanCore(y)))[..(width + 1)];

    private static Rectangle FindBest(PixelStorage table, int width, int height, Size crop, CancellationToken cancellationToken)
    {
        if (crop.Width == width && crop.Height == height)
            return new Rectangle(0, 0, width, height);

        // The center of the positions, doubled so that it is an integer
        var centerX2 = width - crop.Width;
        var centerY2 = height - crop.Height;
        var best = long.MinValue;
        var bestDistance = long.MaxValue;
        var bestX = 0;
        var bestY = 0;
        for (var y = 0; y + crop.Height <= height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var top = GetTableRow(table, y, width);
            var bottom = GetTableRow(table, y + crop.Height, width);
            for (var x = 0; x + crop.Width <= width; x++)
            {
                var sum = bottom[x + crop.Width] - bottom[x] - top[x + crop.Width] + top[x];
                var distance = Math.Abs((2L * x) - centerX2) + Math.Abs((2L * y) - centerY2);
                if (sum > best || (sum == best && distance < bestDistance))
                {
                    best = sum;
                    bestDistance = distance;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return new Rectangle(bestX, bestY, crop.Width, crop.Height);
    }

    private static void AddEdges(PixelStorage frame, WideRowReader reader, Span<double> buffer, Span<long> energies, PixelStorage table, CancellationToken cancellationToken)
    {
        var width = frame.Width;
        var height = frame.Height;

        // The row and the next one, as luma and alpha integers (stored in doubles)
        var luma = buffer[..(2 * width)];
        var alpha = buffer.Slice(2 * width, 2 * width);
        LoadLuma(frame, reader, 0, luma[..width], alpha[..width]);
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y > 0)
            {
                luma.Slice(width, width).CopyTo(luma);
                alpha.Slice(width, width).CopyTo(alpha);
            }

            // The last row has no row below: its vertical differences are 0
            if (y + 1 < height)
            {
                LoadLuma(frame, reader, y + 1, luma.Slice(width, width), alpha.Slice(width, width));
            }
            else
            {
                luma[..width].CopyTo(luma[width..]);
                alpha[..width].CopyTo(alpha[width..]);
            }

            for (var x = 0; x < width; x++)
            {
                energies[x] = GetDifferences(luma, width, x) + GetDifferences(alpha, width, x);
            }

            AccumulateRow(table, y, energies);
        }
    }

    /// <summary>Gets the absolute differences of a value with its right and lower neighbors (0 beyond the edges).</summary>
    private static long GetDifferences(ReadOnlySpan<double> rows, int width, int x)
    {
        var value = rows[x];
        var right = x + 1 < width ? Math.Abs(rows[x + 1] - value) : 0;
        return (long)(right + Math.Abs(rows[width + x] - value));
    }

    /// <summary>Sets the luma (BT.601, premultiplied, times 1000) and the alpha of a row, from 16-bit samples.</summary>
    private static void LoadLuma(PixelStorage frame, WideRowReader reader, int y, Span<double> luma, Span<double> alpha)
    {
        var row = reader.GetRow(frame, y);
        for (var x = 0; x < luma.Length; x++)
        {
            var a = ToSample(row[(x * 4) + 3]);
            var r = ToSample(row[x * 4]) * a / ushort.MaxValue;
            var g = ToSample(row[(x * 4) + 1]) * a / ushort.MaxValue;
            var b = ToSample(row[(x * 4) + 2]) * a / ushort.MaxValue;
            luma[x] = (299 * r) + (587 * g) + (114 * b);
            alpha[x] = 1000 * a;
        }
    }

    /// <summary>Gets a fraction as a 16-bit sample, clamped.</summary>
    private static long ToSample(double fraction) => (long)Math.Round(Math.Clamp(fraction, 0, 1) * ushort.MaxValue, MidpointRounding.ToEven);

    private static void AddSaliency(PixelStorage frame, WideRowReader reader, Span<double> buffer, Span<long> energies, PixelStorage table, CancellationToken cancellationToken)
    {
        var width = frame.Width;
        var height = frame.Height;

        // The mean color, then the blurred colors: a ring of five rows of Oklab colors blurred horizontally
        var meanL = 0d;
        var meanA = 0d;
        var meanB = 0d;
        var line = buffer[..(3 * width)];
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            LoadOklab(frame, reader, y, line);
            for (var x = 0; x < width; x++)
            {
                meanL += line[x * 3];
                meanA += line[(x * 3) + 1];
                meanB += line[(x * 3) + 2];
            }
        }

        var count = (double)width * height;
        meanL /= count;
        meanA /= count;
        meanB /= count;

        // A ring of the five rows around the output row (row t, clamped, in slot (t + 2) mod 5), each blurred horizontally
        // once, when it enters the ring
        var ring = buffer.Slice(3 * width, 5 * 3 * width);
        var rowLength = 3 * width;
        for (var t = -2; t <= 1; t++)
        {
            LoadOklab(frame, reader, Math.Clamp(t, 0, height - 1), line);
            BlurRow(line, ring.Slice((t + 2) % 5 * rowLength, rowLength), width);
        }

        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            LoadOklab(frame, reader, Math.Min(y + 2, height - 1), line);
            BlurRow(line, ring.Slice((y + 4) % 5 * rowLength, rowLength), width);
            for (var x = 0; x < width; x++)
            {
                var l = Blur(ring, y, width, x, 0);
                var a = Blur(ring, y, width, x, 1);
                var b = Blur(ring, y, width, x, 2);
                var distance = Math.Sqrt(((l - meanL) * (l - meanL)) + ((a - meanA) * (a - meanA)) + ((b - meanB) * (b - meanB)));
                energies[x] = (long)Math.Round(distance * 65536, MidpointRounding.ToEven);
            }

            AccumulateRow(table, y, energies);
        }
    }

    /// <summary>Gets the vertical binomial blur at output row <paramref name="y"/> of a value of the rows of the ring (row <c>t</c> in slot <c>(t + 2) mod 5</c>).</summary>
    private static double Blur(ReadOnlySpan<double> ring, int y, int width, int x, int channel)
    {
        var index = (x * 3) + channel;
        var rowLength = 3 * width;
        return (ring[(y % 5 * rowLength) + index] + (4 * ring[((y + 1) % 5 * rowLength) + index]) + (6 * ring[((y + 2) % 5 * rowLength) + index]) + (4 * ring[((y + 3) % 5 * rowLength) + index]) + ring[((y + 4) % 5 * rowLength) + index]) / 16;
    }

    private static void BlurRow(ReadOnlySpan<double> line, Span<double> destination, int width)
    {
        for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < 3; c++)
            {
                var sum = 0d;
                for (var k = -2; k <= 2; k++)
                {
                    var weight = k is 0 ? 6 : k is -1 or 1 ? 4 : 1;
                    sum += weight * line[(Math.Clamp(x + k, 0, width - 1) * 3) + c];
                }

                destination[(x * 3) + c] = sum / 16;
            }
        }
    }

    /// <summary>Sets the Oklab colors of a row (sRGB samples, clamped to [0, 1], premultiplied by alpha: transparent pixels are black).</summary>
    private static void LoadOklab(PixelStorage frame, WideRowReader reader, int y, Span<double> destination)
    {
        var row = reader.GetRow(frame, y);
        for (var x = 0; x < frame.Width; x++)
        {
            var alpha = Math.Clamp(row[(x * 4) + 3], 0, 1);
            var r = SrgbTransfer.Decode(Math.Clamp(row[x * 4], 0, 1)) * alpha;
            var g = SrgbTransfer.Decode(Math.Clamp(row[(x * 4) + 1], 0, 1)) * alpha;
            var b = SrgbTransfer.Decode(Math.Clamp(row[(x * 4) + 2], 0, 1)) * alpha;
            var (cx, cy, cz) = ColorValueMath.LinearSrgbToXyzD65.Transform(r, g, b);
            var (l, a, bb) = ColorValueMath.XyzD65ToOklab(cx, cy, cz);
            destination[x * 3] = l;
            destination[(x * 3) + 1] = a;
            destination[(x * 3) + 2] = bb;
        }
    }

    /// <summary>
    /// Adds the energies of row <paramref name="y"/> of a frame (the first <c>width</c> values of <paramref name="energies"/>)
    /// to the summed-area table: the next <c>width + 1</c> values hold the sums of the frame up to the row above, by column.
    /// </summary>
    private static void AccumulateRow(PixelStorage table, int y, Span<long> energies)
    {
        var width = (energies.Length - 1) / 2;
        var sums = energies[width..];
        var row = GetTableRow(table, y + 1, width);
        var sum = 0L;
        for (var x = 0; x < width; x++)
        {
            sum += energies[x];
            sums[x + 1] += sum;
            row[x + 1] += sums[x + 1];
        }
    }

    /// <summary>The summed-area table of the energies of frames, and the buffers that compute them.</summary>
    internal sealed class EnergyTable : IDisposable
    {
        private readonly PixelFormat _format;
        private readonly Size _size;
        private PixelStorage? _table;
        private WideRowReader? _reader;
        private PooledBuffer? _rows;
        private PooledBuffer? _energies;

        public EnergyTable(StorageOwner owner, PixelFormat format, Size size)
        {
            _format = format;
            _size = size;
            try
            {
                // Row y + 1 holds the sums of the energies of the rows 0 to y, columns 0 to x - 1 at x
                _table = owner.Allocate(size.Width + 1, size.Height + 1, sizeof(long), options: null, AllocationKind.Temporary);
                _reader = WideRowReader.Create(owner.Scope, format, size.Width, SimilarityPrecision.FloatingPoint);
                _rows = ResampleWeights.Rent<double>(owner.Scope, 32L * size.Width);
                _energies = ResampleWeights.Rent<long>(owner.Scope, (2L * size.Width) + 1);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Adds the energies of a leased frame of the size and the format of the table.</summary>
        /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
        /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not finite.</exception>
        public void Add(PixelStorage frame, CropEnergy energy, CancellationToken cancellationToken)
        {
            if (frame.Width != _size.Width || frame.Height != _size.Height || frame.BytesPerPixel != PixelFormats.GetBytesPerPixel(_format))
                throw new InvalidOperationException("The frame does not have the layout of the table.");

            var table = _table ?? throw new ObjectDisposedException(nameof(EnergyTable));
            var buffer = unsafe(MemoryMarshal.Cast<byte, double>(_rows!.Span));
            var energies = unsafe(MemoryMarshal.Cast<byte, long>(_energies!.Span))[..((2 * _size.Width) + 1)];
            energies[_size.Width..].Clear();
            if (energy == CropEnergy.Saliency)
            {
                AddSaliency(frame, _reader!, buffer, energies, table, cancellationToken);
            }
            else
            {
                AddEdges(frame, _reader!, buffer, energies, table, cancellationToken);
            }
        }

        /// <summary>Finds the rectangle of <paramref name="crop"/> with the most energy.</summary>
        /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
        public Rectangle FindBest(Size crop, CancellationToken cancellationToken)
            => CropSuggester.FindBest(_table ?? throw new ObjectDisposedException(nameof(EnergyTable)), _size.Width, _size.Height, crop, cancellationToken);

        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
            _rows?.Dispose();
            _rows = null;
            _energies?.Dispose();
            _energies = null;
            _table?.Dispose();
            _table = null;
        }
    }
}
