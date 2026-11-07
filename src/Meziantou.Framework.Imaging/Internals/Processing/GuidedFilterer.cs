using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The guided filter of <see cref="ImageProcessingExtensions.GuidedFilter(Image, int, double, ConvolutionEdgeMode, CancellationToken)"/>
/// (He, Sun and Tang, "Guided Image Filtering", 2010 and 2013), with the frame as its own guide: in every window, each
/// sample is approached by a linear function of the colors of the window, regularized by <c>ε</c>, and every pixel takes the
/// mean of the functions of the windows that contain it. Flat areas are smoothed, edges, where the colors vary much more
/// than <c>√ε</c>, are kept.
/// </summary>
/// <remarks>
/// <para>
/// The guide <c>I</c> is the colors of the frame, as fractions of the sample range (premultiplied by alpha): three
/// colors, with the 3×3 covariance of the color version of the paper, or one gray level. The filtered samples <c>p</c> are
/// the colors and the alpha. For every window <c>k</c> of <c>(2 r + 1)²</c> pixels: <c>a_k = (Σ_k + ε U)⁻¹ cov_k(I, p)</c>
/// and <c>b_k = mean_k(p) - a_k · mean_k(I)</c>, with the population covariances; then <c>q = mean(a) · I + mean(b)</c>.
/// Windows reach beyond the frame by the edge mode, every pixel of a window counting, as in OpenCV's
/// <c>cv2.ximgproc.guidedFilter</c> (whose box filters use <c>BORDER_REFLECT</c>, <see cref="ConvolutionEdgeMode.Mirror"/>).
/// </para>
/// <para>
/// The window sums are computed in double precision, in a constant time per sample whatever the radius: the rows are
/// summed horizontally by blocks (<see cref="BoxBlurrer.SumWindows"/>), and the summed rows are combined vertically the same
/// way, by blocks of <c>2 r + 1</c> rows counted from the first row read above the frame, each window the suffix sum of a
/// block plus the prefix sum of the next one. A window only adds its own values, so a sample that is not a number only
/// reaches the windows that contain it. The two passes (statistics, then coefficients) stream their rows through a ring of
/// <c>2 r + 1</c> summed rows; the coefficients of every pixel are kept in a frame of single-precision values, charged to
/// the image. The frames are filtered on the calling thread.
/// </para>
/// </remarks>
internal static class GuidedFilterer
{
    /// <summary>Gets the number of guide samples of a frame: 3 colors, or 1 gray level.</summary>
    public static int GetGuideChannels(PixelFormat format) => PixelFormats.GetComponentCount(format) == 1 ? 1 : 3;

    /// <summary>Gets the number of window statistics of a pixel: the guide, its products, the filtered samples and their products with the guide.</summary>
    public static int GetStatisticCount(int guide, int filtered) => guide + (guide * (guide + 1) / 2) + filtered + (guide * filtered);

    /// <summary>Gets the number of coefficients of a pixel: <c>a</c> (one per guide sample) and <c>b</c>, per filtered sample.</summary>
    public static int GetCoefficientCount(int guide, int filtered) => filtered * (guide + 1);

    /// <summary>Gets the number of doubles of the scratch of a frame of <paramref name="format"/>: the ring, the prefix and output rows, and a padded row with its block sums.</summary>
    public static long GetScratchDoubles(PixelFormat format, Size size, int radius)
    {
        var guide = GetGuideChannels(format);
        var filtered = PixelFormats.GetComponentCount(format);
        long values = Math.Max(GetStatisticCount(guide, filtered), GetCoefficientCount(guide, filtered));
        var diameter = (2L * radius) + 1;
        var rowLength = size.Width * values;
        var paddedLength = (size.Width + (2L * radius)) * values;
        return checked(((diameter + 2) * rowLength) + (2 * paddedLength));
    }

    /// <summary>Filters a leased frame of the format of the copy in place.</summary>
    /// <param name="storage">The frame.</param>
    /// <param name="copy">The working copy, prepared.</param>
    /// <param name="grayGuide">Whether the guide is the first color, the gray level of a gray format processed as colors (<see cref="ProcessingBridge"/>).</param>
    /// <param name="coefficients">A frame of <see cref="GetCoefficientCount"/> single-precision values per pixel.</param>
    /// <param name="scratch">The scratch, of <see cref="GetScratchDoubles"/> doubles.</param>
    /// <param name="radius">The radius of the windows, positive.</param>
    /// <param name="epsilon">The regularization, positive.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static void Apply(PixelStorage storage, WorkingFrameCopy copy, bool grayGuide, PixelStorage coefficients, Span<double> scratch, int radius, double epsilon, CancellationToken cancellationToken)
    {
        copy.Load(storage, cancellationToken);
        var state = new State(copy, grayGuide, coefficients, radius, epsilon);
        SumWindows(copy, scratch, GetStatisticCount(state.Guide, state.Filtered), radius, state.LoadStatistics, state.StoreCoefficients, cancellationToken);
        SumWindows(copy, scratch, GetCoefficientCount(state.Guide, state.Filtered), radius, state.LoadCoefficients, (y, sums) => state.StoreOutput(storage, y, sums), cancellationToken);
    }

    /// <summary>
    /// Computes the sums of the <c>(2 r + 1)²</c> windows of every row, in order, of a frame whose pixels have
    /// <paramref name="values"/> values each, given by <paramref name="load"/> for a row of the frame.
    /// </summary>
    private static void SumWindows(WorkingFrameCopy copy, Span<double> scratch, int values, int radius, RowLoader load, WindowRowSink sink, CancellationToken cancellationToken)
    {
        var width = copy.Size.Width;
        var height = copy.Size.Height;
        var diameter = (2 * radius) + 1;
        var rowLength = width * values;
        var paddedLength = (width + (2 * radius)) * values;
        var ring = scratch[..(diameter * rowLength)];
        var prefix = scratch.Slice(diameter * rowLength, rowLength);
        var output = scratch.Slice((diameter + 1) * rowLength, rowLength);
        var padded = scratch.Slice((diameter + 2) * rowLength, paddedLength);
        var suffix = scratch.Slice(((diameter + 2) * rowLength) + paddedLength, paddedLength);

        // Row t of the stream is the row t - radius of the frame, mapped by the edge mode
        var loaded = 0;
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var block = y / diameter;
            var offset = y % diameter;
            if (offset == 0)
            {
                // The rows of the block (all but the last one are there already, except for the first block), then their
                // suffix sums in place: slot s holds x_s + (x_{s+1} + (... + x_{n-1}))
                for (var t = Math.Max(loaded, block * diameter); t < (block + 1) * diameter; t++)
                {
                    LoadRow(copy, load, padded, suffix, radius, values, t, GetSlot(ring, t % diameter, rowLength));
                    loaded = t + 1;
                }

                for (var slot = diameter - 2; slot >= 0; slot--)
                {
                    var current = GetSlot(ring, slot, rowLength);
                    var next = GetSlot(ring, slot + 1, rowLength);
                    for (var i = 0; i < rowLength; i++)
                    {
                        current[i] += next[i];
                    }
                }

                sink(y, GetSlot(ring, 0, rowLength));
            }
            else
            {
                // The window starts at slot `offset` of the block: its suffix sum, plus the prefix of the next block, whose
                // rows replace the slots the windows no longer read
                var t = ((block + 1) * diameter) + offset - 1;
                var row = GetSlot(ring, offset - 1, rowLength);
                LoadRow(copy, load, padded, suffix, radius, values, t, row);
                loaded = t + 1;
                if (offset == 1)
                {
                    row.CopyTo(prefix);
                }
                else
                {
                    for (var i = 0; i < rowLength; i++)
                    {
                        prefix[i] += row[i];
                    }
                }

                var current = GetSlot(ring, offset, rowLength);
                for (var i = 0; i < rowLength; i++)
                {
                    output[i] = current[i] + prefix[i];
                }

                sink(y, output);
            }
        }
    }

    /// <summary>Sets <paramref name="destination"/> to the horizontal window sums of row <paramref name="t"/> of the stream (row <c>t - radius</c> of the frame, mapped by the edge mode).</summary>
    private static void LoadRow(WorkingFrameCopy copy, RowLoader load, Span<double> padded, Span<double> suffix, int radius, int values, int t, Span<double> destination)
    {
        var source = copy.MapRow(t - radius);
        if (source < 0)
        {
            destination.Clear();
            return;
        }

        var width = copy.Size.Width;
        load(source, padded.Slice(radius * values, width * values));
        for (var i = 0; i < radius; i++)
        {
            FillPadding(padded, i, copy.MapColumn(i - radius), radius, values);
            FillPadding(padded, radius + width + i, copy.MapColumn(width + i), radius, values);
        }

        BoxBlurrer.SumWindows(padded, suffix, destination, width, (2 * radius) + 1, values);
    }

    private static Span<double> GetSlot(Span<double> ring, int slot, int rowLength) => ring.Slice(slot * rowLength, rowLength);

    private static void FillPadding(Span<double> padded, int pixel, int sourceColumn, int radius, int values)
    {
        var destination = padded.Slice(pixel * values, values);
        if (sourceColumn < 0)
        {
            destination.Clear();
        }
        else
        {
            padded.Slice((radius + sourceColumn) * values, values).CopyTo(destination);
        }
    }

    private delegate void RowLoader(int y, Span<double> values);

    private delegate void WindowRowSink(int y, ReadOnlySpan<double> sums);

    /// <summary>The state of the filtering of a frame: what the two passes read and write.</summary>
    private sealed class State
    {
        private readonly WorkingFrameCopy _copy;
        private readonly PixelStorage _coefficients;
        private readonly double _epsilon;
        private readonly double _count;
        private readonly double[] _scales;

        public State(WorkingFrameCopy copy, bool grayGuide, PixelStorage coefficients, int radius, double epsilon)
        {
            _copy = copy;
            _coefficients = coefficients;
            _epsilon = epsilon;
            var diameter = (2d * radius) + 1;
            _count = diameter * diameter;
            Filtered = copy.WorkingChannels;
            Guide = Filtered == 1 || grayGuide ? 1 : 3;
            _scales = new double[Filtered];
            for (var c = 0; c < Filtered; c++)
            {
                _scales[c] = Filtered == 4 && c == 3 ? copy.AlphaScale : copy.ColorScale;
            }
        }

        /// <summary>Gets the number of guide samples of a pixel.</summary>
        public int Guide { get; }

        /// <summary>Gets the number of filtered samples of a pixel.</summary>
        public int Filtered { get; }

        private int CoefficientCount => GetCoefficientCount(Guide, Filtered);

        /// <summary>Sets the statistics of the pixels of row <paramref name="y"/>: I, the products I_i I_j (i ≤ j), p, and the products I_i p_j.</summary>
        public void LoadStatistics(int y, Span<double> values)
        {
            var row = _copy.GetRow(y);
            var statistics = GetStatisticCount(Guide, Filtered);
            Span<double> p = stackalloc double[4];
            for (var x = 0; x < _copy.Size.Width; x++)
            {
                var pixel = row.Slice(x * Filtered, Filtered);
                var destination = values.Slice(x * statistics, statistics);
                for (var c = 0; c < Filtered; c++)
                {
                    p[c] = pixel[c] * _scales[c];
                }

                var i = 0;
                for (var g = 0; g < Guide; g++)
                {
                    destination[i++] = p[g];
                }

                for (var g = 0; g < Guide; g++)
                {
                    for (var h = g; h < Guide; h++)
                    {
                        destination[i++] = p[g] * p[h];
                    }
                }

                for (var c = 0; c < Filtered; c++)
                {
                    destination[i++] = p[c];
                }

                for (var g = 0; g < Guide; g++)
                {
                    for (var c = 0; c < Filtered; c++)
                    {
                        destination[i++] = p[g] * p[c];
                    }
                }
            }
        }

        /// <summary>Computes the coefficients a and b of the windows of row <paramref name="y"/> from their sums, and stores them.</summary>
        public void StoreCoefficients(int y, ReadOnlySpan<double> sums)
        {
            var statistics = GetStatisticCount(Guide, Filtered);
            var coefficientCount = CoefficientCount;
            var destination = unsafe(MemoryMarshal.Cast<byte, float>(_coefficients.GetRowSpanCore(y)));
            Span<double> mean = stackalloc double[3];
            Span<double> inverse = stackalloc double[9];
            Span<double> covariance = stackalloc double[3];
            for (var x = 0; x < _copy.Size.Width; x++)
            {
                var window = sums.Slice(x * statistics, statistics);
                var coefficients = destination.Slice(x * coefficientCount, coefficientCount);
                for (var g = 0; g < Guide; g++)
                {
                    mean[g] = window[g] / _count;
                }

                var products = window.Slice(Guide, Guide * (Guide + 1) / 2);
                var filteredMeans = window.Slice(Guide + products.Length, Filtered);
                var crossProducts = window[(Guide + products.Length + Filtered)..];
                if (Guide == 1)
                {
                    var variance = (products[0] / _count) - (mean[0] * mean[0]);
                    for (var c = 0; c < Filtered; c++)
                    {
                        var meanP = filteredMeans[c] / _count;
                        var a = ((crossProducts[c] / _count) - (mean[0] * meanP)) / (variance + _epsilon);
                        coefficients[2 * c] = (float)a;
                        coefficients[(2 * c) + 1] = (float)(meanP - (a * mean[0]));
                    }

                    continue;
                }

                // Σ + εU, symmetric: products in the order 00, 01, 02, 11, 12, 22
                var s00 = (products[0] / _count) - (mean[0] * mean[0]) + _epsilon;
                var s01 = (products[1] / _count) - (mean[0] * mean[1]);
                var s02 = (products[2] / _count) - (mean[0] * mean[2]);
                var s11 = (products[3] / _count) - (mean[1] * mean[1]) + _epsilon;
                var s12 = (products[4] / _count) - (mean[1] * mean[2]);
                var s22 = (products[5] / _count) - (mean[2] * mean[2]) + _epsilon;
                Invert(s00, s01, s02, s11, s12, s22, inverse);
                for (var c = 0; c < Filtered; c++)
                {
                    var meanP = filteredMeans[c] / _count;
                    for (var g = 0; g < 3; g++)
                    {
                        covariance[g] = (crossProducts[(g * Filtered) + c] / _count) - (mean[g] * meanP);
                    }

                    var b = meanP;
                    var target = coefficients.Slice(c * 4, 4);
                    for (var g = 0; g < 3; g++)
                    {
                        var a = (inverse[g * 3] * covariance[0]) + (inverse[(g * 3) + 1] * covariance[1]) + (inverse[(g * 3) + 2] * covariance[2]);
                        target[g] = (float)a;
                        b -= a * mean[g];
                    }

                    target[3] = (float)b;
                }
            }
        }

        /// <summary>Sets the coefficients of the pixels of row <paramref name="y"/>.</summary>
        public void LoadCoefficients(int y, Span<double> values)
        {
            var source = unsafe(MemoryMarshal.Cast<byte, float>(_coefficients.GetRowSpanCore(y)))[..values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = source[i];
            }
        }

        /// <summary>Computes the output of row <paramref name="y"/> from the sums of the coefficients of its windows, and stores it.</summary>
        public void StoreOutput(PixelStorage storage, int y, ReadOnlySpan<double> sums)
        {
            var coefficientCount = CoefficientCount;
            var row = _copy.GetRow(y);
            var output = _copy.GetRowBuffer(0)[..row.Length];
            Span<double> guide = stackalloc double[3];
            for (var x = 0; x < _copy.Size.Width; x++)
            {
                var pixel = row.Slice(x * Filtered, Filtered);
                var window = sums.Slice(x * coefficientCount, coefficientCount);
                for (var g = 0; g < Guide; g++)
                {
                    guide[g] = pixel[g] * _scales[g];
                }

                for (var c = 0; c < Filtered; c++)
                {
                    var coefficients = window.Slice(c * (Guide + 1), Guide + 1);
                    var q = coefficients[Guide] / _count;
                    for (var g = 0; g < Guide; g++)
                    {
                        q += coefficients[g] / _count * guide[g];
                    }

                    output[(x * Filtered) + c] = q / _scales[c];
                }
            }

            _copy.Store(storage, y, output);
        }

        /// <summary>Inverts a symmetric 3×3 matrix by its adjugate.</summary>
        private static void Invert(double s00, double s01, double s02, double s11, double s12, double s22, Span<double> inverse)
        {
            var c00 = (s11 * s22) - (s12 * s12);
            var c01 = (s02 * s12) - (s01 * s22);
            var c02 = (s01 * s12) - (s02 * s11);
            var c11 = (s00 * s22) - (s02 * s02);
            var c12 = (s01 * s02) - (s00 * s12);
            var c22 = (s00 * s11) - (s01 * s01);
            var determinant = (s00 * c00) + (s01 * c01) + (s02 * c02);
            inverse[0] = c00 / determinant;
            inverse[1] = c01 / determinant;
            inverse[2] = c02 / determinant;
            inverse[3] = c01 / determinant;
            inverse[4] = c11 / determinant;
            inverse[5] = c12 / determinant;
            inverse[6] = c02 / determinant;
            inverse[7] = c12 / determinant;
            inverse[8] = c22 / determinant;
        }
    }
}
