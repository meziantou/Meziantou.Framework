using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors an image paints on the pixels of a row, in the pixel format of the frame that is drawn on.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The color of a pixel is the color of the image at the position <c>(u, v)</c> its center
/// <c>(x + 1/2, y + 1/2)</c> comes from through the inverse of the transformation of the image. The pixel
/// <c>(i, j)</c> of the image covers the square from <c>(i, j)</c> to <c>(i + 1, j + 1)</c>.
/// </para>
/// <list type="bullet">
/// <item><description>Nearest: the pixel <c>(floor(u), floor(v))</c>.</description></item>
/// <item><description>
/// Bilinear: with <c>i = floor(u - 1/2)</c>, <c>fx = u - 1/2 - i</c>, and the same for <c>j</c> and <c>fy</c>, the pixels
/// <c>(i, j)</c>, <c>(i + 1, j)</c>, <c>(i, j + 1)</c> and <c>(i + 1, j + 1)</c> with the weights <c>(1 - fx)(1 - fy)</c>,
/// <c>fx (1 - fy)</c>, <c>(1 - fx) fy</c> and <c>fx fy</c>. The alpha is the weighted sum of the alphas, and each color the
/// weighted sum of the colors multiplied by their alpha, divided by that alpha, all at 16 bits and rounded as
/// <c>floor(v + 1/2 + 2^-20)</c>. A pixel whose weight is zero is not read: when <c>fx</c> and <c>fy</c> are zero, the
/// color is the pixel, exactly.
/// </description></item>
/// </list>
/// <para>
/// <b>Smooth.</b> With <c>(u, v)</c> = <c>(a x + c y + e, b x + d y + f)</c>, a pixel of the canvas covers
/// <c>sx = sqrt(a^2 + c^2)</c> columns and <c>sy = sqrt(b^2 + d^2)</c> rows of the image, each limited to the size of
/// the region. When neither exceeds 1, the image is not reduced and the color is the bilinear one, bit for bit.
/// Otherwise, with both brought up to 1, the pixel <c>(i, j)</c> has the weight <c>wx(i) wy(j)</c>, where
/// <c>wx(i) = 1 - |i + 1/2 - u| / sx</c> for the columns that make it positive (and the same for the rows): the
/// weights of the bilinear interpolation, stretched over what the pixel of the canvas covers. For each row in
/// ascending order, <c>Sa = sum wx(i) a(i, j)</c> and <c>Sc = sum (wx(i) a(i, j)) c(i, j)</c> over its columns in
/// ascending order, then <c>Ta += wy(j) Sa</c> and <c>Tc += wy(j) Sc</c>. The alpha is
/// <c>Ta / (sum wx sum wy)</c>, the sums being over every pixel of the support, in the region or not, and each color
/// is <c>Tc / Ta</c>, rounded as above.
/// </para>
/// <para>
/// A pixel outside the region of the image is, by the wrap mode of each axis (a column by the one of x, a row by the
/// one of y): transparent, the nearest pixel of the region, or the pixel the repeated or mirrored region has there. The colors are converted to the pixel format of the frame last, as
/// any brush color is.
/// </para>
/// <para>
/// <b>Perspective.</b> An image drawn on a quadrilateral is placed by a projective transformation: the position
/// <c>(u, v)</c> the center <c>(x, y)</c> of a pixel comes from is
/// <c>((h0 x + h1 y + h2) / w, (h3 x + h4 y + h5) / w)</c> with <c>w = h6 x + h7 y + h8</c>, the nine numbers being
/// those of the inverse of the transformation, with the sign that makes <c>w</c> positive on the quadrilateral. The
/// color is then the one above for that position. With smooth sampling, what a pixel of the canvas covers of the
/// image changes from a pixel to the next: <c>sx</c> and <c>sy</c> are the lengths of the derivatives of <c>u</c> and
/// of <c>v</c> at the center of the pixel, <c>sqrt(((h0 - h6 u) / w)^2 + ((h1 - h7 u) / w)^2)</c> and
/// <c>sqrt(((h3 - h6 v) / w)^2 + ((h4 - h7 v) / w)^2)</c>.
/// </para>
/// <para>
/// An image placed by a projective transformation of the canvas (the one of a brush, then the one of the canvas) is
/// read the same way, with the nine numbers of the inverse of that transformation, and the region and the wrap mode
/// of the brush.
/// </para>
/// <para>
/// <b>Method.</b> An image moved by whole pixels (without bicubic sampling, reduction or a rectangle that is not made of
/// whole pixels) is read run by run: the center of the pixel <c>(x, y)</c> comes from <c>(x + tx + 1/2, y + ty + 1/2)</c>
/// exactly (half-integers below 2^30), which is the center of the pixel <c>(x + tx, y + ty)</c> for every sampling, so
/// that pixel is converted as it is. When the rows of the canvas stay rows of the image (no rotation, no skew), the two rows of the image
/// around a row of the canvas are converted once and kept until another row needs their place: the image is read row
/// by row. Otherwise each pixel is read on its own, from segments of 32 pixels of a row of the image converted once
/// during the lease (when the scope has room for them). A reduced image is read the same two ways, one row of the image at
/// a time for every pixel of a run, with the same operations in the same order.
/// </para>
/// </remarks>
internal sealed class ImageEvaluator : PaintEvaluator, IDisposable
{
    private const double RoundingOffset = 0.5 + (1.0 / 1048576);

    /// <summary>The largest magnitude of a position in the image: beyond it, every position counts as that far.</summary>
    private const double MaxPosition = 1 << 30;

    /// <summary>The largest magnitude of a column or a row read by an image moved by whole pixels, far below <see cref="MaxPosition"/>.</summary>
    private const long MaxShift = 1 << 29;

    /// <summary>The values kept for each pixel of a run of a reduced image: the sums of the alpha and of the three colors, and the sum of the weights of its columns.</summary>
    private const int ValuesPerSum = 5;

    /// <summary>The most columns of the image kept with their weights for the pixels of a run of a reduced image, unless a single pixel reads more.</summary>
    private const int MaxColumnTaps = 1 << 16;

    /// <summary>The most bytes of the sums of the rows of the image kept for the pixels of a run of a reduced image, unless a single row takes more.</summary>
    private const long MaxRowSumBytes = 1 << 22;

    /// <summary>The columns of a segment of the pixels read one by one, as a power of two: a segment is the 16-bit colors of 32 pixels of a row of the region.</summary>
    private const int SegmentShift = 5;

    private const int SegmentLength = 1 << SegmentShift;

    /// <summary>The bits of a slot of the segments of a region that has more of them than are kept.</summary>
    private const int MaxSegmentBits = 13;

    /// <summary>The most segments kept of the pixels read one by one: 2 MiB of colors.</summary>
    private const int MaxSegments = 1 << MaxSegmentBits;

    private readonly AllocationScope _scope;
    private PooledBuffer? _colors;
    private PooledBuffer? _samples;
    private PooledBuffer? _floatColors;
    private int _width;
    private PooledBuffer? _rows;
    private int _rowCapacity;
    private int _cachedRow0;
    private int _cachedRow1;

    private ImageFrame? _source;
    private ImageFrame? _destination;
    private PixelStorage? _storage;
    private long _token;
    private int _left;
    private int _top;
    private int _regionWidth;
    private int _regionHeight;
    private Affine2D _inverse;
    private bool _isEmpty;
    private bool _keepsRows;

    // An image moved by whole pixels, and by how many: the pixel (x, y) of the canvas is the pixel (x + shiftX, y + shiftY) of the frame
    private bool _isShifted;
    private long _shiftX;
    private long _shiftY;
    private bool _isSameFormat;
    private ImageWrapMode _wrapModeX;
    private ImageWrapMode _wrapModeY;
    private bool _isNearest;
    private bool _isBicubic;

    // The rectangle of the image the positions are brought into, when an image is drawn from a rectangle of its pixels
    // that is not made of whole pixels
    private bool _hasDomain;
    private double _domainLeft;
    private double _domainTop;
    private double _domainRight;
    private double _domainBottom;
    private bool _isReduced;
    private double _spanX;
    private double _spanY;
    private bool _isProjective;
    private bool _isSmooth;

    // A reduced image is averaged over what a pixel of the canvas covers of it (ImageSampling.Box), instead of weighted
    // by the stretched interpolation
    private bool _isBox;
    private double _h0;
    private double _h1;
    private double _h2;
    private double _h3;
    private double _h4;
    private double _h5;
    private double _h6;
    private double _h7;
    private double _h8;
    private PooledBuffer? _sums;
    private PooledBuffer? _tapCounts;
    private int _sumCapacity;
    private PooledBuffer? _tapWeights;
    private PooledBuffer? _tapColumns;
    private long _tapCapacity;

    // The vector path of a reduced image read row by row: the taps of the last run, kept for the next rows when the
    // columns do not depend on the row, and the sums of the rows of the image for the pixels of that run, by slot
    private bool _hasRunTaps;
    private int _runTapX;
    private int _runTapCount;
    private int _runFirstColumn;
    private int _runLastColumn;
    private PooledBuffer? _rowSums;
    private PooledBuffer? _rowSumKeys;
    private int _rowSumSlots;
    private int _rowSumWidth;
    private uint _rowSumGeneration;

    // The pixels read one by one, converted once per lease by segments of a row of the region: the segment of the key
    // k = row * _segmentsPerRow + column / SegmentLength is in the slot k, or, for a region with more segments than are
    // kept, in the slot its hash gives. A slot holds the key and the generation (one per lease) of its segment.
    private PooledBuffer? _segmentColors;
    private PooledBuffer? _segmentKeys;
    private byte[]? _segmentColorBytes;
    private byte[]? _segmentKeyBytes;
    private int _segmentCapacity;
    private int _segmentSlots;
    private int _segmentsPerRow;
    private bool _isSegmentHashed;
    private uint _segmentGeneration;

    // 0 until a pixel is read one by one during the lease, then 1 when the segments are used, -1 when they cannot be
    private int _segmentState;

    public ImageEvaluator(AllocationScope scope)
    {
        _scope = scope;
    }

    private Span<Rgba64> Samples => unsafe(MemoryMarshal.Cast<byte, Rgba64>(_samples!.Span));

    /// <summary>
    /// Gets or sets a value indicating whether the colors are summed with vectors (the default when vectors are
    /// accelerated): a reduced image read row by row, the sums of each row of the image kept for the next rows of the
    /// canvas and the cubic filter of the pixels read one by one, rather than by the scalar references.
    /// Both give the same bits.
    /// </summary>
    internal bool UseVectorKernels { get; set; } = Vector128.IsHardwareAccelerated;

    /// <summary>Converts an opacity to a coverage: the nearest multiple of 1/65536.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The opacity is not between 0 and 1.</exception>
    public static uint GetOpacityCoverage(double opacity, string paramName)
    {
        if (!(opacity >= 0 && opacity <= 1))
            throw new ArgumentOutOfRangeException(paramName, opacity, "The opacity must be between 0 and 1.");

        return (uint)Math.Floor((opacity * CoverageRasterizer.FullCoverage) + 0.5);
    }

    /// <summary>Sets the image and its placement, and rents the rows the evaluation needs. The image is not read yet.</summary>
    /// <param name="source">The frame whose pixels are painted.</param>
    /// <param name="region">The pixels of the frame that are painted, or <see langword="null"/> for all of them.</param>
    /// <param name="transform">The transformation that places the image on the canvas.</param>
    /// <param name="wrapMode">What is painted outside the region.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="destination">A frame that is drawn on: the colors are converted to its pixel format.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="regionParameterName">The name of the parameter the region comes from, for the exception.</param>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The region is not inside the frame.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the allocation limit.</exception>
    public void Prepare(ImageFrame source, Rectangle? region, AffineTransform transform, ImageWrapMode wrapMode, ImageSampling sampling, ImageFrame destination, int width, string regionParameterName)
        => Prepare(source, region, transform, wrapMode, wrapMode, sampling, destination, width, regionParameterName);

    /// <summary>Sets the image and its placement, with a wrap mode per axis, and rents the rows the evaluation needs. The image is not read yet.</summary>
    /// <param name="source">The frame whose pixels are painted.</param>
    /// <param name="region">The pixels of the frame that are painted, or <see langword="null"/> for all of them.</param>
    /// <param name="transform">The transformation that places the image on the canvas.</param>
    /// <param name="wrapModeX">What is painted on the left and on the right of the region.</param>
    /// <param name="wrapModeY">What is painted above and below the region.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="destination">A frame that is drawn on: the colors are converted to its pixel format.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="regionParameterName">The name of the parameter the region comes from, for the exception.</param>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The region is not inside the frame.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the allocation limit.</exception>
    public void Prepare(ImageFrame source, Rectangle? region, AffineTransform transform, ImageWrapMode wrapModeX, ImageWrapMode wrapModeY, ImageSampling sampling, ImageFrame destination, int width, string regionParameterName)
    {
        var storage = source.GetStorage();
        var area = region ?? new Rectangle(0, 0, storage.Width, storage.Height);
        if (area.Left < 0 || area.Top < 0 || area.Right > storage.Width || area.Bottom > storage.Height)
            throw new ArgumentOutOfRangeException(regionParameterName, area, "The region is not inside the frame.");

        _source = source;
        _destination = destination;
        _storage = storage;
        _left = area.Left;
        _top = area.Top;
        _regionWidth = area.Width;
        _regionHeight = area.Height;
        _wrapModeX = wrapModeX;
        _wrapModeY = wrapModeY;
        _isNearest = sampling == ImageSampling.Nearest;
        _isBicubic = sampling == ImageSampling.Bicubic;
        _hasDomain = false;
        _isProjective = false;
        _isEmpty = area.IsEmpty || !new Affine2D(transform.M11, transform.M12, transform.M21, transform.M22, transform.OffsetX, transform.OffsetY).TryInvert(out _inverse);

        // The segments of the pixels read one by one: every segment of the region when they fit, otherwise a table indexed by a hash of the key
        _segmentsPerRow = (int)(((long)_regionWidth + SegmentLength - 1) >> SegmentShift);
        var segments = (long)_segmentsPerRow * _regionHeight;
        _isSegmentHashed = segments > MaxSegments;
        _segmentSlots = _isEmpty || segments > uint.MaxValue ? 0 : (int)Math.Min(segments, MaxSegments);

        // The row of the image a pixel reads does not depend on its column
        _keepsRows = _inverse.B == 0;
        _isShifted = !_isEmpty && _inverse.A == 1 && _inverse.B == 0 && _inverse.C == 0 && _inverse.D == 1 && IsWholeShift(_inverse.Tx) && IsWholeShift(_inverse.Ty);
        _shiftX = _isShifted ? (long)_inverse.Tx : 0;
        _shiftY = _isShifted ? (long)_inverse.Ty : 0;
        _isSameFormat = source.PixelFormat == destination.PixelFormat;

        // What a pixel of the canvas covers of the image: more than a pixel when the image is reduced
        _isReduced = false;
        InvalidateRunTaps();
        _isBox = sampling == ImageSampling.Box;
        if (sampling is ImageSampling.Smooth or ImageSampling.Box && !_isEmpty)
        {
            _spanX = Math.Min(Math.Sqrt((_inverse.A * _inverse.A) + (_inverse.C * _inverse.C)), _regionWidth);
            _spanY = Math.Min(Math.Sqrt((_inverse.B * _inverse.B) + (_inverse.D * _inverse.D)), _regionHeight);
            _isReduced = _spanX > 1 || _spanY > 1;
            _spanX = Math.Max(_spanX, 1);
            _spanY = Math.Max(_spanY, 1);
        }

        if (width > _width)
        {
            _colors?.Dispose();
            _samples?.Dispose();
            _colors = _samples = null;
            _width = 0;
            _colors = ResampleWeights.Rent<ushort>(_scope, 4L * width);
            _samples = ResampleWeights.Rent<Rgba64>(_scope, width);
            _width = width;
        }

        if (_isReduced && _keepsRows)
        {
            if (width > _sumCapacity)
            {
                _sums?.Dispose();
                _tapCounts?.Dispose();
                _sums = _tapCounts = null;
                _sumCapacity = 0;
                _sums = ResampleWeights.Rent<double>(_scope, (long)ValuesPerSum * width);
                _tapCounts = ResampleWeights.Rent<int>(_scope, width);
                _sumCapacity = width;
            }

            // A pixel reads at most ceil(2 sx) + 2 columns, fewer with a box
            var pixelTaps = (long)Math.Ceiling(2 * _spanX) + 2;
            var taps = Math.Min(pixelTaps * width, Math.Max(pixelTaps, MaxColumnTaps));
            if (taps > _tapCapacity)
            {
                _tapWeights?.Dispose();
                _tapColumns?.Dispose();
                _tapWeights = _tapColumns = null;
                _tapCapacity = 0;
                _tapWeights = ResampleWeights.Rent<double>(_scope, taps);
                _tapColumns = ResampleWeights.Rent<int>(_scope, taps);
                _tapCapacity = taps;
            }
        }

        if (_keepsRows && !_isEmpty && _regionWidth > _rowCapacity)
        {
            _rows?.Dispose();
            _rows = null;
            _rowCapacity = 0;
            _rows = ResampleWeights.Rent<Rgba64>(_scope, 2L * _regionWidth);
            _rowCapacity = _regionWidth;
        }
    }

    /// <summary>Sets the image and its placement by a transformation that may be projective, and rents the rows the evaluation needs. The image is not read yet.</summary>
    /// <param name="source">The frame whose pixels are painted.</param>
    /// <param name="region">The pixels of the frame that are painted, or <see langword="null"/> for all of them.</param>
    /// <param name="transform">The transformation that places the image on the canvas.</param>
    /// <param name="wrapMode">What is painted outside the region.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="destination">A frame that is drawn on: the colors are converted to its pixel format.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="regionParameterName">The name of the parameter the region comes from, for the exception.</param>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The region is not inside the frame.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the allocation limit.</exception>
    public void Prepare(ImageFrame source, Rectangle? region, in ProjectiveTransform transform, ImageWrapMode wrapMode, ImageSampling sampling, ImageFrame destination, int width, string regionParameterName)
        => Prepare(source, region, transform, wrapMode, wrapMode, sampling, destination, width, regionParameterName);

    /// <summary>Sets the image and its placement by a transformation that may be projective, with a wrap mode per axis. The image is not read yet.</summary>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The region is not inside the frame.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the allocation limit.</exception>
    public void Prepare(ImageFrame source, Rectangle? region, in ProjectiveTransform transform, ImageWrapMode wrapModeX, ImageWrapMode wrapModeY, ImageSampling sampling, ImageFrame destination, int width, string regionParameterName)
    {
        if (transform.TryGetExactAffine(out var affine))
        {
            Prepare(source, region, affine, wrapModeX, wrapModeY, sampling, destination, width, regionParameterName);
            return;
        }

        Prepare(source, region, AffineTransform.Identity, wrapModeX, wrapModeY, ImageSampling.Bilinear, destination, width, regionParameterName);
        _isNearest = sampling == ImageSampling.Nearest;
        _isBicubic = sampling == ImageSampling.Bicubic;
        _hasDomain = false;
        _isSmooth = sampling is ImageSampling.Smooth or ImageSampling.Box;
        _isBox = sampling == ImageSampling.Box;
        _isProjective = true;
        if (!transform.TryInvert(out var inverse))
        {
            _isEmpty = true;
            return;
        }

        // The inverse keeps the sign of W: a pixel whose center comes from a point in front of the viewer has a positive one
        _h0 = inverse.M11;
        _h1 = inverse.M21;
        _h2 = inverse.M31;
        _h3 = inverse.M12;
        _h4 = inverse.M22;
        _h5 = inverse.M32;
        _h6 = inverse.M13;
        _h7 = inverse.M23;
        _h8 = inverse.M33;
    }

    /// <summary>Sets an image that is placed by a projective transformation, its pixels on the edges extended. The image is not read yet.</summary>
    /// <param name="source">The frame whose pixels are painted, all of them.</param>
    /// <param name="inverse">The nine numbers that bring a position of the canvas back to the image, row by row; empty when the image is flattened to a line: it paints nothing.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="destination">A frame that is drawn on: the colors are converted to its pixel format.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the allocation limit.</exception>
    public void PrepareProjective(ImageFrame source, ReadOnlySpan<double> inverse, ImageSampling sampling, ImageFrame destination, int width)
    {
        Prepare(source, region: null, AffineTransform.Identity, ImageWrapMode.Pad, ImageSampling.Bilinear, destination, width, nameof(source));
        _isNearest = sampling == ImageSampling.Nearest;
        _isBicubic = sampling == ImageSampling.Bicubic;
        _hasDomain = false;
        _isSmooth = sampling is ImageSampling.Smooth or ImageSampling.Box;
        _isBox = sampling == ImageSampling.Box;
        _isProjective = true;
        if (inverse.IsEmpty)
        {
            _isEmpty = true;
            return;
        }

        _h0 = inverse[0];
        _h1 = inverse[1];
        _h2 = inverse[2];
        _h3 = inverse[3];
        _h4 = inverse[4];
        _h5 = inverse[5];
        _h6 = inverse[6];
        _h7 = inverse[7];
        _h8 = inverse[8];
    }

    /// <summary>Takes the lease of the image: its pixels cannot change while a shape is painted with them.</summary>
    /// <exception cref="InvalidOperationException">The image is being drawn on, or its pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException">The frame no longer belongs to a live image.</exception>
    public override void Begin()
    {
        _segmentState = 0;

        // The pixels may have changed since the previous lease
        InvalidateRowSums();
        if (_isEmpty)
            return;

        _token = _storage!.AcquireLease().Token;
        _cachedRow0 = _cachedRow1 = int.MinValue;
    }

    public override void End()
    {
        if (_token != 0)
        {
            _storage!.Owner.ReleaseLease(_storage, _token);
            _token = 0;
        }
    }

    public override ReadOnlySpan<ushort> Evaluate(int y, int x, int count)
    {
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_colors!.Span))[..(count * 4)];
        if (_isEmpty)
        {
            colors.Clear();
            return colors;
        }

        if (_isShifted && !_hasDomain && !_isBicubic && !_isProjective && !_isReduced && TryEvaluateShifted(y, x, colors))
            return colors;

        var samples = Samples[..count];
        if (_hasDomain)
        {
            EvaluateInDomain(y, x, samples);
        }
        else if (_isBicubic)
        {
            EvaluateBicubic(y, x, samples);
        }
        else if (_isProjective)
        {
            EvaluateProjected(y, x, samples);
        }
        else if (_isReduced)
        {
            if (_keepsRows)
            {
                ReduceRows(y, x, samples);
            }
            else
            {
                ReducePixels(y, x, samples);
            }
        }
        else if (_keepsRows)
        {
            EvaluateRows(y, x, samples);
        }
        else
        {
            EvaluatePixels(y, x, samples);
        }

        _destination!.ConvertDrawingColors(samples, colors);
        return colors;
    }

    /// <summary>Samples floating-point images without passing their colors through normalized 16-bit integers.</summary>
    public override bool TryEvaluateFloat(int y, int x, int count, double alphaFactor, out ReadOnlySpan<double> colors)
    {
        colors = default;
        if (_source!.PixelFormat is not (PixelFormat.RgbaHalf or PixelFormat.RgbaFloat) || _destination!.PixelFormat is not (PixelFormat.RgbaHalf or PixelFormat.RgbaFloat))
            return false;

        if (_floatColors is null || _floatColors.Span.Length < count * 4 * sizeof(double))
        {
            _floatColors?.Dispose();
            _floatColors = ResampleWeights.Rent<double>(_scope, 4L * _width);
        }

        var output = unsafe(MemoryMarshal.Cast<byte, double>(_floatColors.Span))[..(count * 4)];
        output.Clear();
        if (!_isEmpty)
        {
            for (var i = 0; i < count; i++)
            {
                var px = x + i + 0.5;
                var py = y + 0.5;
                var u = _inverse.TransformX(px, py);
                var v = _inverse.TransformY(px, py);
                if (_isProjective)
                {
                    var w = (_h6 * px) + (_h7 * py) + _h8;
                    if (!(w > 0))
                        continue;

                    u = ((_h0 * px) + (_h1 * py) + _h2) / w;
                    v = ((_h3 * px) + (_h4 * py) + _h5) / w;
                }

                if (_hasDomain)
                {
                    u = Math.Clamp(u, _domainLeft, _domainRight);
                    v = Math.Clamp(v, _domainTop, _domainBottom);
                }

                u = Math.Abs(u) < MaxPosition ? u : (u < 0 ? -MaxPosition : MaxPosition);
                v = Math.Abs(v) < MaxPosition ? v : (v < 0 ? -MaxPosition : MaxPosition);
                var pixel = output.Slice(i * 4, 4);
                if (_isNearest)
                {
                    AccumulateFloat((int)Math.Floor(u), (int)Math.Floor(v), 1, pixel);
                }
                else if (_isBicubic)
                {
                    var left = (int)Math.Floor(u - 0.5);
                    var top = (int)Math.Floor(v - 0.5);
                    for (var row = top - 1; row <= top + 2; row++)
                    {
                        for (var column = left - 1; column <= left + 2; column++)
                        {
                            AccumulateFloat(column, row, Kernel(u - column - 0.5) * Kernel(v - row - 0.5), pixel);
                        }
                    }
                }
                else if (_isReduced)
                {
                    double totalWeight = 0;
                    for (var row = (int)Math.Floor(v - _spanY); row <= Math.Ceiling(v + _spanY); row++)
                    {
                        var wy = _isBox ? Math.Max(0, Math.Min(row + 1, v + (_spanY / 2)) - Math.Max(row, v - (_spanY / 2))) : Math.Max(0, 1 - (Math.Abs(row + 0.5 - v) / _spanY));
                        for (var column = (int)Math.Floor(u - _spanX); column <= Math.Ceiling(u + _spanX); column++)
                        {
                            var wx = _isBox ? Math.Max(0, Math.Min(column + 1, u + (_spanX / 2)) - Math.Max(column, u - (_spanX / 2))) : Math.Max(0, 1 - (Math.Abs(column + 0.5 - u) / _spanX));
                            var weight = wx * wy;
                            totalWeight += weight;
                            AccumulateFloat(column, row, weight, pixel);
                        }
                    }

                    if (totalWeight > 0)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            pixel[c] /= totalWeight;
                        }
                    }
                }
                else
                {
                    var left = (int)Math.Floor(u - 0.5);
                    var top = (int)Math.Floor(v - 0.5);
                    var fx = u - 0.5 - left;
                    var fy = v - 0.5 - top;
                    for (var row = 0; row < 2; row++)
                    {
                        for (var column = 0; column < 2; column++)
                        {
                            AccumulateFloat(left + column, top + row, (column == 0 ? 1 - fx : fx) * (row == 0 ? 1 - fy : fy), pixel);
                        }
                    }
                }

                var alpha = pixel[3];
                if (alpha > 0)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        pixel[c] /= alpha;
                    }
                }

                pixel[3] = Math.Clamp(alpha, 0, 1) * alphaFactor;
            }
        }

        colors = output;
        return true;
    }

    private void AccumulateFloat(int column, int row, double weight, Span<double> sum)
    {
        if (weight == 0 || !TryWrap(column, _left, _regionWidth, _wrapModeX, out var x) || !TryWrap(row, _top, _regionHeight, _wrapModeY, out var y))
            return;

        var bytes = _storage!.GetRowSpanCore(_top + y);
        double r, g, b, a;
        if (_source!.PixelFormat == PixelFormat.RgbaHalf)
        {
            var pixel = unsafe(MemoryMarshal.Cast<byte, RgbaHalf>(bytes))[_left + x];
            r = (double)pixel.R;
            g = (double)pixel.G;
            b = (double)pixel.B;
            a = (double)pixel.A;
        }
        else
        {
            var pixel = unsafe(MemoryMarshal.Cast<byte, RgbaFloat>(bytes))[_left + x];
            r = pixel.R;
            g = pixel.G;
            b = pixel.B;
            a = pixel.A;
        }

        var alpha = Math.Clamp(a, 0, 1) * weight;
        if (alpha == 0)
            return;

        sum[0] += r * alpha;
        sum[1] += g * alpha;
        sum[2] += b * alpha;
        sum[3] += alpha;
    }

    /// <remarks>
    /// The image and the lease that <see cref="Begin"/> took are shared: the worker only reads the pixels. Its scratch is
    /// its own: the colors, the samples, the rows it keeps, the sums of the reduced images, and the segments of the pixels
    /// read one by one, which it rents when it first needs them.
    /// </remarks>
    public override PaintEvaluator? CreateWorker()
    {
        var worker = (ImageEvaluator)MemberwiseClone();
        worker._token = 0;
        worker._colors = worker._samples = worker._floatColors = worker._rows = null;
        worker._sums = worker._tapCounts = worker._tapWeights = worker._tapColumns = null;
        worker._segmentColors = worker._segmentKeys = null;
        worker._segmentColorBytes = worker._segmentKeyBytes = null;
        worker._segmentCapacity = 0;
        worker._segmentGeneration = 0;
        worker._segmentState = 0;
        worker._cachedRow0 = worker._cachedRow1 = int.MinValue;
        worker._hasRunTaps = false;
        worker._rowSums = worker._rowSumKeys = null;
        worker._rowSumSlots = 0;
        worker._rowSumWidth = 0;
        worker._rowSumGeneration = 0;
        try
        {
            worker._colors = RentLike(_scope, _colors);
            worker._samples = RentLike(_scope, _samples);
            worker._floatColors = RentLike(_scope, _floatColors);
            worker._rows = RentLike(_scope, _rows);
            worker._sums = RentLike(_scope, _sums);
            worker._tapCounts = RentLike(_scope, _tapCounts);
            worker._tapWeights = RentLike(_scope, _tapWeights);
            worker._tapColumns = RentLike(_scope, _tapColumns);
        }
        catch
        {
            worker.ReleaseWorker();
            throw;
        }

        return worker;
    }

    public override void ReleaseWorker()
    {
        // The lease belongs to the evaluator the worker was created from
        _token = 0;
        Dispose();
    }

    public void Dispose()
    {
        End();
        _colors?.Dispose();
        _samples?.Dispose();
        _floatColors?.Dispose();
        _floatColors = null;
        _rows?.Dispose();
        _sums?.Dispose();
        _tapCounts?.Dispose();
        _tapWeights?.Dispose();
        _tapColumns?.Dispose();
        _segmentColors?.Dispose();
        _segmentKeys?.Dispose();
        _rowSums?.Dispose();
        _rowSumKeys?.Dispose();
        _colors = _samples = _rows = _sums = _tapCounts = _tapWeights = _tapColumns = _segmentColors = _segmentKeys = _rowSums = _rowSumKeys = null;
        _segmentColorBytes = _segmentKeyBytes = null;
        _hasRunTaps = false;
        _rowSumSlots = 0;
        _rowSumWidth = 0;
        _width = 0;
        _segmentCapacity = 0;
        _rowCapacity = 0;
        _sumCapacity = 0;
        _tapCapacity = 0;
        _source = null;
        _destination = null;
        _storage = null;
    }

    private static bool IsWholeShift(double offset) => Math.Abs(offset) <= MaxShift && Math.Floor(offset) == offset;

    /// <summary>Evaluates a run of an image moved by whole pixels: the pixels of a run of a row of the frame, as they are.</summary>
    /// <returns><see langword="false"/> when a position of the run is too far for its pixel to be found that way.</returns>
    private bool TryEvaluateShifted(int y, int x, Span<ushort> colors)
    {
        var count = colors.Length / 4;
        var row = (long)y + _shiftY;
        var first = (long)x + _shiftX;
        if (Math.Abs(row) >= MaxShift || Math.Abs(first) >= MaxShift || Math.Abs(first + count) >= MaxShift)
            return false;

        if (!TryWrap((int)row, _top, _regionHeight, _wrapModeY, out var regionRow))
        {
            colors.Clear();
            return true;
        }

        // The pixels of the run in the region are read at once; the others by the wrap mode, on each side
        var frameRow = _top + regionRow;
        var start = (int)Math.Clamp(_left - first, 0, count);
        var end = (int)Math.Clamp(_left + _regionWidth - first, start, count);
        ReadOutside(frameRow, first, colors[..(start * 4)]);
        if (end > start)
        {
            ReadColors(frameRow, (int)(first + start), colors[(start * 4)..(end * 4)]);
        }

        ReadOutside(frameRow, first + end, colors[(end * 4)..]);
        return true;
    }

    /// <summary>
    /// Reads pixels of a row of the frame that are all on the same side of the region, by the wrap mode along x: they
    /// repeat with a period (one pixel for the nearest pixel of the region, its width or twice its width when it is
    /// repeated or mirrored), so the pixels of one period are read, run by run of the region, and copied over the others.
    /// </summary>
    /// <param name="frameRow">The row of the frame.</param>
    /// <param name="first">The column of the first pixel, in the coordinates of the frame.</param>
    /// <param name="colors">The colors of the pixels.</param>
    private void ReadOutside(int frameRow, long first, Span<ushort> colors)
    {
        var count = colors.Length / 4;
        if (count == 0)
            return;

        if (_wrapModeX is not (ImageWrapMode.Pad or ImageWrapMode.Repeat or ImageWrapMode.Reflect))
        {
            colors.Clear();
            return;
        }

        var period = _wrapModeX switch
        {
            ImageWrapMode.Pad => 1,
            ImageWrapMode.Repeat => _regionWidth,
            _ => 2L * _regionWidth,
        };
        var read = (int)Math.Min(count, period);
        for (var i = 0; i < read;)
        {
            // The run of the region the pixel i starts, forward or mirrored: the column of TryWrap for every pixel of it
            var offset = (first + i - _left) % period;
            if (offset < 0)
            {
                offset += period;
            }

            int column;
            int length;
            var isMirrored = false;
            if (_wrapModeX == ImageWrapMode.Pad)
            {
                column = first < _left ? 0 : _regionWidth - 1;
                length = 1;
            }
            else if (offset < _regionWidth)
            {
                column = (int)offset;
                length = Math.Min(read - i, _regionWidth - column);
            }
            else
            {
                // Mirrored: the columns go down from 2 w - 1 - offset
                var last = (int)(period - 1 - offset);
                length = Math.Min(read - i, last + 1);
                column = last - length + 1;
                isMirrored = true;
            }

            var run = colors.Slice(i * 4, length * 4);
            ReadColors(frameRow, _left + column, run);
            if (isMirrored)
            {
                var pixels = unsafe(MemoryMarshal.Cast<ushort, ulong>(run));
                pixels.Reverse();
            }

            i += length;
        }

        // The pixel i is the pixel i - k period: the pixels already written are copied, as many as are a whole number of periods
        for (var i = read; i < count;)
        {
            var copied = (int)(i / period * period);
            var length = Math.Min(count - i, copied);
            colors.Slice((i - copied) * 4, length * 4).CopyTo(colors[(i * 4)..]);
            i += length;
        }
    }

    /// <summary>Reads pixels of a row of the frame as colors of the frame that is drawn on.</summary>
    private void ReadColors(int frameRow, int frameColumn, Span<ushort> colors)
    {
        if (_isSameFormat)
        {
            _source!.ReadDrawingColors(_storage!, frameRow, frameColumn, colors);
            return;
        }

        var samples = Samples[..(colors.Length / 4)];
        _source!.ReadRgba64(_storage!, frameRow, frameColumn, samples);
        _destination!.ConvertDrawingColors(samples, colors);
    }

    /// <summary>Evaluates a run whose pixels all read the same two rows of the image.</summary>
    private void EvaluateRows(int y, int x, Span<Rgba64> samples)
    {
        if (Avx.IsSupported)
        {
            EvaluateRows<VectorAccumulator>(y, x, samples);
        }
        else
        {
            EvaluateRows<Accumulator>(y, x, samples);
        }
    }

    /// <summary>Evaluates a run whose pixels all read the same two rows of the image, with the sums of <typeparamref name="TAccumulator"/>.</summary>
    private void EvaluateRows<TAccumulator>(int y, int x, Span<Rgba64> samples)
        where TAccumulator : struct, IAccumulator
    {
        var centerY = y + 0.5;
        var v = _inverse.TransformY(x + 0.5, centerY);
        Split(v, out var row, out var fy);
        var hasRow0 = TryWrap(row, _top, _regionHeight, _wrapModeY, out var row0);
        var hasRow1 = false;
        var row1 = 0;
        if (fy != 0)
        {
            hasRow1 = TryWrap(row + 1, _top, _regionHeight, _wrapModeY, out row1);
        }

        if (!hasRow0 && !hasRow1)
        {
            samples.Clear();
            return;
        }

        // The rows of the image, converted once and kept
        var first = hasRow0 ? LoadRow(row0, hasRow1 ? row1 : row0) : default;
        var second = hasRow1 ? LoadRow(row1, hasRow0 ? row0 : row1) : default;
        for (var i = 0; i < samples.Length; i++)
        {
            var u = _inverse.TransformX(x + i + 0.5, centerY);
            Split(u, out var column, out var fx);
            var hasColumn0 = TryWrap(column, _left, _regionWidth, _wrapModeX, out var column0);
            var hasColumn1 = false;
            var column1 = 0;
            if (fx != 0)
            {
                hasColumn1 = TryWrap(column + 1, _left, _regionWidth, _wrapModeX, out column1);
            }

            if (fx == 0 && fy == 0)
            {
                // The center of a pixel of the image: that pixel, exactly
                samples[i] = hasRow0 && hasColumn0 ? first[column0] : default;
                continue;
            }

            var accumulator = default(TAccumulator);
            if (hasRow0)
            {
                if (hasColumn0)
                {
                    accumulator.Add(first[column0], (1 - fx) * (1 - fy));
                }

                if (hasColumn1)
                {
                    accumulator.Add(first[column1], fx * (1 - fy));
                }
            }

            if (hasRow1)
            {
                if (hasColumn0)
                {
                    accumulator.Add(second[column0], (1 - fx) * fy);
                }

                if (hasColumn1)
                {
                    accumulator.Add(second[column1], fx * fy);
                }
            }

            samples[i] = accumulator.ToColor();
        }
    }

    /// <summary>Evaluates a run whose pixels read any pixel of the image.</summary>
    private void EvaluatePixels(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            samples[i] = Interpolate(_inverse.TransformX(centerX, centerY), _inverse.TransformY(centerX, centerY));
        }
    }

    /// <summary>Evaluates a run of an image placed by a projective transformation: each pixel has its own position, and its own share of the image.</summary>
    private void EvaluateProjected(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            var w = ((_h6 * centerX) + (_h7 * centerY)) + _h8;

            // Written so that a value that is not a number has no color: what is beyond the horizon of the image is not painted
            if (!(w > 0))
            {
                samples[i] = default;
                continue;
            }

            var u = (((_h0 * centerX) + (_h1 * centerY)) + _h2) / w;
            var v = (((_h3 * centerX) + (_h4 * centerY)) + _h5) / w;
            if (_isSmooth)
            {
                // What the pixel covers of the image, around its center
                var ux = (_h0 - (_h6 * u)) / w;
                var uy = (_h1 - (_h7 * u)) / w;
                var vx = (_h3 - (_h6 * v)) / w;
                var vy = (_h4 - (_h7 * v)) / w;
                var spanX = Math.Min(Math.Sqrt((ux * ux) + (uy * uy)), _regionWidth);
                var spanY = Math.Min(Math.Sqrt((vx * vx) + (vy * vy)), _regionHeight);
                if (spanX > 1 || spanY > 1)
                {
                    samples[i] = Reduce(u, v, Math.Max(spanX, 1), Math.Max(spanY, 1));
                    continue;
                }
            }

            samples[i] = Interpolate(u, v);
        }
    }

    /// <summary>
    /// Brings the positions the pixels read into a rectangle of the image, as browsers draw a rectangle of an image that
    /// is not made of whole pixels: to <c>[left + 1/2, right - 1/2]</c> (the middle of the rectangle when it is narrower
    /// than a pixel) when the image is interpolated, filtered or reduced, so that what is around the rectangle weighs as
    /// little as it can, and to <c>[left, right)</c> for the nearest pixel. Set after <see cref="Prepare(ImageFrame, Rectangle?, AffineTransform, ImageWrapMode, ImageWrapMode, ImageSampling, ImageFrame, int, string)"/>.
    /// </summary>
    public void SetDomain(RectangleD domain)
    {
        _hasDomain = true;
        if (_isNearest)
        {
            _domainLeft = domain.Left;
            _domainTop = domain.Top;
            _domainRight = Math.BitDecrement(domain.Right);
            _domainBottom = Math.BitDecrement(domain.Bottom);
            return;
        }

        (_domainLeft, _domainRight) = domain.Width >= 1 ? (domain.Left + 0.5, domain.Right - 0.5) : ((domain.Left + domain.Right) / 2, (domain.Left + domain.Right) / 2);
        (_domainTop, _domainBottom) = domain.Height >= 1 ? (domain.Top + 0.5, domain.Bottom - 0.5) : ((domain.Top + domain.Bottom) / 2, (domain.Top + domain.Bottom) / 2);
    }

    /// <summary>Evaluates a run whose positions are brought into the rectangle of <see cref="SetDomain"/>: each pixel on its own.</summary>
    private void EvaluateInDomain(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            var u = Math.Clamp(_inverse.TransformX(centerX, centerY), _domainLeft, _domainRight);
            var v = Math.Clamp(_inverse.TransformY(centerX, centerY), _domainTop, _domainBottom);
            samples[i] = _isBicubic ? Bicubic(u, v) : _isReduced ? Reduce(u, v, _spanX, _spanY) : Interpolate(u, v);
        }
    }

    /// <summary>Evaluates a run with the cubic filter: each pixel at its position, placed by the transformation, affine or projective.</summary>
    private void EvaluateBicubic(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            double u, v;
            if (_isProjective)
            {
                var w = ((_h6 * centerX) + (_h7 * centerY)) + _h8;

                // Written so that a value that is not a number has no color
                if (!(w > 0))
                {
                    samples[i] = default;
                    continue;
                }

                u = (((_h0 * centerX) + (_h1 * centerY)) + _h2) / w;
                v = (((_h3 * centerX) + (_h4 * centerY)) + _h5) / w;
            }
            else
            {
                u = _inverse.TransformX(centerX, centerY);
                v = _inverse.TransformY(centerX, centerY);
            }

            samples[i] = Bicubic(u, v);
        }
    }

    /// <summary>
    /// Gets the color of the image at a position with the filter of Mitchell and Netravali (<c>B = C = 1/3</c>): with
    /// <c>i = floor(u - 1/2)</c> and <c>fx = u - 1/2 - i</c>, the columns <c>i - 1</c> to <c>i + 2</c> have the weights
    /// <c>k(1 + fx)</c>, <c>k(fx)</c>, <c>k(1 - fx)</c> and <c>k(2 - fx)</c>, and the rows the same. The alpha is the weighted
    /// sum of the alphas, clamped to the range from 0 to 1, and each color the weighted sum of the colors multiplied by their
    /// alpha, clamped to the range from 0 to that alpha, divided by it, rounded as the bilinear colors are.
    /// </summary>
    private Rgba64 Bicubic(double u, double v)
    {
        if (UseVectorKernels)
        {
            if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
                return Bicubic<Lanes256>(u, v);

            return Bicubic<Lanes128>(u, v);
        }

        return BicubicScalar(u, v);
    }

    /// <summary>
    /// Gets the color of the image at a position with the cubic filter, as <see cref="BicubicScalar"/> does: the weights
    /// of the columns and of the rows computed as lanes of <typeparamref name="TLanes"/>, the 16 pixels read first, by rows
    /// (<see cref="ReadBlock"/>), then summed as the lanes of <typeparamref name="TLanes"/>, with the products and the
    /// additions of the scalar sums in the same order. A pixel that is not there adds zeros, which change no sum: a sum
    /// that starts at a positive zero is never a negative zero.
    /// </summary>
    private Rgba64 Bicubic<TLanes>(double u, double v)
        where TLanes : struct, IColorLanes<TLanes>
    {
        // Written so that a coordinate that is not a number is far away
        u = Math.Abs(u) < MaxPosition ? u : (u < 0 ? -MaxPosition : MaxPosition);
        v = Math.Abs(v) < MaxPosition ? v : (v < 0 ? -MaxPosition : MaxPosition);
        var column = (int)Math.Floor(u - 0.5);
        var row = (int)Math.Floor(v - 0.5);
        var block = default(BicubicBlock);
        TLanes.CubicWeights(u - 0.5 - column, ref block.Weights[0]);
        TLanes.CubicWeights(v - 0.5 - row, ref block.Weights[4]);
        ReadBlock(column - 1, row - 1, block.Pixels);
        return SumBlock<TLanes>(ref block);
    }

    /// <summary>Sums the 16 pixels of a block with the weights of its columns and of its rows, row by row (<see cref="BicubicScalar"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba64 SumBlock<TLanes>(ref BicubicBlock block)
        where TLanes : struct, IColorLanes<TLanes>
    {
        var sum = TLanes.Zero;
        for (var j = 0; j < 4; j++)
        {
            var weightY = block.Weights[4 + j];
            for (var i = 0; i < 4; i++)
            {
                var bits = block.Pixels[(4 * j) + i];
                sum = TLanes.AddProduct(sum, block.Weights[i] * weightY * (long)(bits >> 48), TLanes.Channels((bits & 0x0000_FFFF_FFFF_FFFFUL) | 0x0001_0000_0000_0000UL));
            }
        }

        Span<double> sums = block.Weights;
        TLanes.Store(sum, ref sums[0]);
        var alpha = sums[3];

        // The sums are of 16-bit values times 16-bit alphas: the alpha is up to 65535, each color up to 65535 times it
        var a = Math.Clamp(alpha, 0, 65535);
        var resultAlpha = Round(a);
        if (resultAlpha == 0)
            return default;

        return new Rgba64(Round(Math.Clamp(sums[0], 0, 65535 * a) / a), Round(Math.Clamp(sums[1], 0, 65535 * a) / a), Round(Math.Clamp(sums[2], 0, 65535 * a) / a), resultAlpha);
    }

    /// <summary>
    /// Reads the 4 by 4 pixels of the image from a column and a row, as the bits of their colors, row by row; a pixel that
    /// is not there is transparent. The 4 pixels of a row that are 4 columns of the region one after the other are read
    /// at once, from one segment when they are in the same one.
    /// </summary>
    private void ReadBlock(int left, int top, Span<ulong> pixels)
    {
        var hasColumn0 = TryWrap(left, _left, _regionWidth, _wrapModeX, out var column0);
        var hasColumn1 = TryWrap(left + 1, _left, _regionWidth, _wrapModeX, out var column1);
        var hasColumn2 = TryWrap(left + 2, _left, _regionWidth, _wrapModeX, out var column2);
        var hasColumn3 = TryWrap(left + 3, _left, _regionWidth, _wrapModeX, out var column3);
        var isRun = hasColumn0 && hasColumn1 && hasColumn2 && hasColumn3 && column1 == column0 + 1 && column2 == column0 + 2 && column3 == column0 + 3;
        var hasColumn = hasColumn0 || hasColumn1 || hasColumn2 || hasColumn3;
        var useSegments = false;
        var isOneSegment = false;
        var hasPixel = false;
        for (var j = 0; j < 4; j++)
        {
            var target = pixels.Slice(4 * j, 4);
            if (!hasColumn || !TryWrap(top + j, _top, _regionHeight, _wrapModeY, out var row))
            {
                target.Clear();
                continue;
            }

            // The segments are started by the first pixel read, as TryRead does
            if (!hasPixel)
            {
                hasPixel = true;
                useSegments = _segmentState > 0 || (_segmentState == 0 && StartSegments());
                isOneSegment = isRun && useSegments && (column0 >> SegmentShift) == (column3 >> SegmentShift);
            }

            if (isOneSegment)
            {
                ref var first = ref GetSegmentPixel(column0, row);
                unsafe { MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Rgba64, ulong>(ref first), 4).CopyTo(target); }
            }
            else if (isRun && !useSegments)
            {
                _source!.ReadRgba64(_storage!, _top + row, _left + column0, unsafe(MemoryMarshal.Cast<ulong, Rgba64>(target)));
            }
            else
            {
                target[0] = hasColumn0 ? ReadPixel(column0, row, useSegments) : 0;
                target[1] = hasColumn1 ? ReadPixel(column1, row, useSegments) : 0;
                target[2] = hasColumn2 ? ReadPixel(column2, row, useSegments) : 0;
                target[3] = hasColumn3 ? ReadPixel(column3, row, useSegments) : 0;
            }
        }
    }

    /// <summary>Reads a pixel of the region, from its segment or from the frame, as the bits of its color.</summary>
    private ulong ReadPixel(int x, int y, bool useSegments)
    {
        if (useSegments)
            return unsafe(Unsafe.BitCast<Rgba64, ulong>(GetSegmentPixel(x, y)));

        var color = default(Rgba64);
        _source!.ReadRgba64(_storage!, _top + y, _left + x, new Span<Rgba64>(ref color));
        return unsafe(Unsafe.BitCast<Rgba64, ulong>(color));
    }

    /// <summary>The weights and the pixels of the cubic filter at a position: 4 weights of columns, 4 of rows, then 4 by 4 pixels.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct BicubicBlock
    {
        public Doubles8 Weights;
        public Pixels16 Pixels;
    }

    [InlineArray(8)]
    private struct Doubles8
    {
        private double _element;
    }

    [InlineArray(16)]
    private struct Pixels16
    {
        private ulong _element;
    }

    /// <summary>
    /// Gets the color of the image at a position with the filter of Mitchell and Netravali, one pixel at a time (the
    /// scalar reference of <see cref="Bicubic{TLanes}"/>).
    /// </summary>
    private Rgba64 BicubicScalar(double u, double v)
    {
        // Written so that a coordinate that is not a number is far away
        u = Math.Abs(u) < MaxPosition ? u : (u < 0 ? -MaxPosition : MaxPosition);
        v = Math.Abs(v) < MaxPosition ? v : (v < 0 ? -MaxPosition : MaxPosition);
        var column = (int)Math.Floor(u - 0.5);
        var row = (int)Math.Floor(v - 0.5);
        var fx = u - 0.5 - column;
        var fy = v - 0.5 - row;
        Span<double> weightsX = [Kernel(1 + fx), Kernel(fx), Kernel(1 - fx), Kernel(2 - fx)];
        Span<double> weightsY = [Kernel(1 + fy), Kernel(fy), Kernel(1 - fy), Kernel(2 - fy)];
        double alpha = 0, red = 0, green = 0, blue = 0;
        for (var j = 0; j < 4; j++)
        {
            for (var i = 0; i < 4; i++)
            {
                if (!TryRead(column - 1 + i, row - 1 + j, out var color))
                    continue;

                var weight = weightsX[i] * weightsY[j] * color.A;
                alpha += weight;
                red += weight * color.R;
                green += weight * color.G;
                blue += weight * color.B;
            }
        }

        // The sums are of 16-bit values times 16-bit alphas: the alpha is up to 65535, each color up to 65535 times it
        var a = Math.Clamp(alpha, 0, 65535);
        var resultAlpha = Round(a);
        if (resultAlpha == 0)
            return default;

        return new Rgba64(Round(Math.Clamp(red, 0, 65535 * a) / a), Round(Math.Clamp(green, 0, 65535 * a) / a), Round(Math.Clamp(blue, 0, 65535 * a) / a), resultAlpha);

    }

    private static double Kernel(double distance)
    {
        var t = Math.Abs(distance);
        if (t < 1)
            return ((((7 * t) - 12) * t * t) + (16.0 / 3)) / 6;

        if (t < 2)
            return (((((-7.0 / 3) * t) + 12) * t * t) - (20 * t) + (32.0 / 3)) / 6;

        return 0;
    }

    /// <summary>Gets the color of the image at a position: the pixel that contains it, or the four pixels around it, interpolated.</summary>
    private Rgba64 Interpolate(double u, double v)
    {
        Split(u, out var column, out var fx);
        Split(v, out var row, out var fy);
        if (fx == 0 && fy == 0)
            return TryRead(column, row, out var pixel) ? pixel : default;

        var accumulator = default(Accumulator);
        if (TryRead(column, row, out var color))
        {
            accumulator.Add(color, (1 - fx) * (1 - fy));
        }

        if (fx != 0 && TryRead(column + 1, row, out color))
        {
            accumulator.Add(color, fx * (1 - fy));
        }

        if (fy != 0)
        {
            if (TryRead(column, row + 1, out color))
            {
                accumulator.Add(color, (1 - fx) * fy);
            }

            if (fx != 0 && TryRead(column + 1, row + 1, out color))
            {
                accumulator.Add(color, fx * fy);
            }
        }

        return accumulator.ToColor();
    }

    /// <summary>Evaluates a run of a reduced image whose pixels all read the same rows of the image: one row at a time, for the whole run.</summary>
    private void ReduceRows(int y, int x, Span<Rgba64> samples)
    {
        if (UseVectorKernels)
        {
            if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
            {
                if (TryReduceRows<Lanes256>(y, x, samples))
                    return;
            }
            else if (TryReduceRows<Lanes128>(y, x, samples))
            {
                return;
            }
        }

        ReduceRowsScalar(y, x, samples);
    }

    /// <summary>
    /// Evaluates a run of a reduced image whose pixels all read the same rows of the image, as
    /// <see cref="ReduceRowsScalar"/> does, with the sums of the four channels of a pixel as the lanes of
    /// <typeparamref name="TLanes"/>. The taps of the run are kept for the next rows when the columns do not depend on the
    /// row, and so are the sums of each row of the image for the pixels of the run (<see cref="GetRowSums"/>): a row of
    /// the image is read and summed once for every row of the canvas that reads it. Each lane has the products and the
    /// additions of a field of <see cref="RowSum"/>, in the same order.
    /// </summary>
    /// <returns><see langword="false"/> when the taps of the run or the sums of the rows do not fit in the working storage: the scalar reference evaluates the run.</returns>
    private bool TryReduceRows<TLanes>(int y, int x, Span<Rgba64> samples)
        where TLanes : struct, IColorLanes<TLanes>
    {
        var count = samples.Length;
        if (!TryGetRunTaps(y, x, count) || !TryRentRowSums())
        {
            InvalidateRunTaps();
            return false;
        }

        var centerY = y + 0.5;
        var v = Limit(_inverse.TransformY(x + 0.5, centerY));
        GetSupport(v, _spanY, out var firstRow, out var lastRow);
        var buffer = unsafe(MemoryMarshal.Cast<byte, double>(_sums!.Span));
        var sums = buffer[..(4 * count)];
        sums.Clear();
        var totals = buffer.Slice(4 * _sumCapacity, count);
        var totalY = 0d;
        for (var row = firstRow; row <= lastRow; row++)
        {
            var weightY = GetWeight(row, v, _spanY);
            if (!(weightY > 0))
                continue;

            // A row without pixels, or whose pixels are not read, adds zeros
            totalY += weightY;
            if (_runLastColumn < _runFirstColumn || !TryWrap(row, _top, _regionHeight, _wrapModeY, out var wrapped))
                continue;

            AddRowSums<TLanes>(sums, GetRowSums<TLanes>(wrapped, count), weightY);
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var entry = sums.Slice(4 * i, 4);
            samples[i] = ToColor(entry[3], entry[0], entry[1], entry[2], totals[i] * totalY);
        }

        return true;
    }

    /// <summary>
    /// Computes the columns of the pixels of a run with a positive weight, their weights and the sum of the weights of
    /// each pixel, as <see cref="ReduceRowsScalar"/> does, unless they are those of the previous run: the same pixels, with
    /// columns that do not depend on the row. A column without a pixel is the transparent pixel after the region.
    /// </summary>
    /// <returns><see langword="false"/> when the taps exceed the working storage.</returns>
    private bool TryGetRunTaps(int y, int x, int count)
    {
        if (_hasRunTaps && _inverse.C == 0 && _runTapX == x && _runTapCount == count)
            return true;

        InvalidateRunTaps();
        var centerY = y + 0.5;
        var counts = unsafe(MemoryMarshal.Cast<byte, int>(_tapCounts!.Span))[..count];
        var weights = unsafe(MemoryMarshal.Cast<byte, double>(_tapWeights!.Span))[..(int)_tapCapacity];
        var columns = unsafe(MemoryMarshal.Cast<byte, int>(_tapColumns!.Span))[..(int)_tapCapacity];
        var totals = unsafe(MemoryMarshal.Cast<byte, double>(_sums!.Span)).Slice(4 * _sumCapacity, count);
        var tapCount = 0;
        var firstColumn = int.MaxValue;
        var lastColumn = -1;
        for (var i = 0; i < count; i++)
        {
            var u = Limit(_inverse.TransformX(x + i + 0.5, centerY));
            GetSupport(u, _spanX, out var first, out var last);
            if (tapCount + ((long)last - first + 1) > weights.Length)
                return false;

            var total = 0d;
            var pixelTaps = 0;
            for (var column = first; column <= last; column++)
            {
                var weightX = GetWeight(column, u, _spanX);
                if (!(weightX > 0))
                    continue;

                total += weightX;
                var index = _regionWidth;
                if (TryWrap(column, _left, _regionWidth, _wrapModeX, out var wrapped))
                {
                    index = wrapped;
                    firstColumn = Math.Min(firstColumn, index);
                    lastColumn = Math.Max(lastColumn, index);
                }

                weights[tapCount + pixelTaps] = weightX;
                columns[tapCount + pixelTaps] = index;
                pixelTaps++;
            }

            counts[i] = pixelTaps;
            tapCount += pixelTaps;
            totals[i] = total;
        }

        _runTapX = x;
        _runTapCount = count;
        _runFirstColumn = firstColumn;
        _runLastColumn = lastColumn;
        _hasRunTaps = true;
        return true;
    }

    /// <summary>Rents the sums of the rows of the image for the pixels of a run: a slot per row a pixel of the canvas reads, fewer when they would take too much room.</summary>
    /// <returns><see langword="false"/> when even one slot would exceed the allocation limit.</returns>
    private bool TryRentRowSums()
    {
        // The rows a pixel reads, at most: those within a span on each side of its position
        var slotBytes = 4L * sizeof(double) * _sumCapacity;
        var slots = (int)Math.Clamp(Math.Min((long)Math.Ceiling(2 * _spanY) + 2, MaxRowSumBytes / slotBytes), 1, int.MaxValue);
        if (_rowSums is not null && _rowSumWidth >= _sumCapacity && _rowSumSlots >= slots)
            return true;

        _rowSums?.Dispose();
        _rowSumKeys?.Dispose();
        _rowSums = _rowSumKeys = null;
        _rowSumSlots = 0;
        _rowSumWidth = 0;
        if (_scope.LiveBytes + _scope.GetChargedSize(slots * slotBytes) + _scope.GetChargedSize(slots * sizeof(ulong)) > _scope.Limit)
            return false;

        _rowSums = ResampleWeights.Rent<double>(_scope, slots * 4L * _sumCapacity);
        _rowSumKeys = ResampleWeights.Rent<ulong>(_scope, slots);
        _rowSumSlots = slots;
        _rowSumWidth = _sumCapacity;

        // The cleared keys have the generation 0, which no sums have
        _rowSumGeneration = 1;
        return true;
    }

    /// <summary>Gets the sums of a row of the image for the pixels of the run of <see cref="TryGetRunTaps"/>: from its slot, or read and summed into it.</summary>
    /// <param name="row">The row, from the first one of the region.</param>
    /// <param name="count">The pixels of the run.</param>
    private ReadOnlySpan<double> GetRowSums<TLanes>(int row, int count)
        where TLanes : struct, IColorLanes<TLanes>
    {
        var slot = row % _rowSumSlots;
        var tag = ((ulong)_rowSumGeneration << 32) | (uint)row;
        ref var key = ref unsafe(MemoryMarshal.Cast<byte, ulong>(_rowSumKeys!.Span))[slot];
        var target = unsafe(MemoryMarshal.Cast<byte, double>(_rowSums!.Span)).Slice(slot * 4 * _rowSumWidth, 4 * count);
        if (key == tag)
            return target;

        // The columns the run reads, and the transparent pixel after the region for the columns without a pixel
        var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_rows!.Span))[..(_regionWidth + 1)];
        _source!.ReadRgba64(_storage!, _top + row, _left + _runFirstColumn, pixels[_runFirstColumn..(_runLastColumn + 1)]);
        pixels[_regionWidth] = default;
        SumColumns<TLanes>(unsafe(MemoryMarshal.Cast<Rgba64, ulong>(pixels)), target);
        key = tag;
        return target;
    }

    /// <summary>
    /// Sums the taps of each pixel of the run in a row of the image: <c>(R, G, B, 1)</c> times the weight times the alpha,
    /// the lanes of <see cref="RowSum.Red"/>, <see cref="RowSum.Green"/>, <see cref="RowSum.Blue"/> and
    /// <see cref="RowSum.Alpha"/>. A transparent pixel adds zeros: the sums are never negative zeros, so they do not change.
    /// </summary>
    /// <remarks>Each column feeds about two taps: converting the colors of a row once costs more than it saves.</remarks>
    private void SumColumns<TLanes>(ReadOnlySpan<ulong> pixels, Span<double> target)
        where TLanes : struct, IColorLanes<TLanes>
    {
        var count = target.Length / 4;
        var counts = unsafe(MemoryMarshal.Cast<byte, int>(_tapCounts!.Span))[..count];
        var weights = unsafe(MemoryMarshal.Cast<byte, double>(_tapWeights!.Span));
        var columns = unsafe(MemoryMarshal.Cast<byte, int>(_tapColumns!.Span));
        ref var output = ref unsafe(MemoryMarshal.GetReference(target));
        ref var pixel = ref unsafe(MemoryMarshal.GetReference(pixels));
        var tap = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var sum = TLanes.Zero;
            var end = tap + counts[i];
            for (; tap < end; tap++)
            {
                // The columns of the taps are those of the region, or the transparent pixel after it
                var column = columns[tap];
                Debug.Assert((uint)column < (uint)pixels.Length);
                var bits = unsafe(Unsafe.Add(ref pixel, (uint)column));
                sum = TLanes.AddProduct(sum, weights[tap] * (long)(bits >> 48), TLanes.Channels((bits & 0x0000_FFFF_FFFF_FFFFUL) | 0x0001_0000_0000_0000UL));
            }

            TLanes.Store(sum, ref unsafe(Unsafe.Add(ref output, 4 * i)));
        }
    }

    /// <summary>Adds the sums of a row of the image, times the weight of the row, to the sums of the pixels: <c>sums += weight * row</c>, lane by lane.</summary>
    private static void AddRowSums<TLanes>(Span<double> sums, ReadOnlySpan<double> row, double weight)
        where TLanes : struct, IColorLanes<TLanes>
    {
        if (row.Length != sums.Length)
            throw new ArgumentException("The sums must have the same length.", nameof(row));

        ref var target = ref unsafe(MemoryMarshal.GetReference(sums));
        ref var source = ref unsafe(MemoryMarshal.GetReference(row));
        for (var i = 0; i < sums.Length; i += 4)
        {
            ref var entry = ref unsafe(Unsafe.Add(ref target, i));
            TLanes.Store(TLanes.AddProduct(TLanes.Load(ref entry), weight, TLanes.Load(ref unsafe(Unsafe.Add(ref source, i)))), ref entry);
        }
    }

    /// <summary>Forgets the taps of the last run, and the sums of the rows for its pixels.</summary>
    private void InvalidateRunTaps()
    {
        _hasRunTaps = false;
        InvalidateRowSums();
    }

    /// <summary>Forgets the sums of the rows: a new generation, which the keys of the previous ones do not match.</summary>
    private void InvalidateRowSums()
    {
        _rowSumGeneration++;
        if (_rowSumGeneration == 0)
        {
            _rowSumKeys?.Span.Clear();
            _rowSumGeneration = 1;
        }
    }

    /// <summary>Evaluates a run of a reduced image whose pixels all read the same rows of the image: one row at a time, for the whole run (the scalar reference).</summary>
    /// <remarks>
    /// The columns each pixel reads and their weights do not depend on the row: they are computed once, for as many
    /// pixels as the working storage holds, and the rows are read for those pixels. A row is read only where they read it.
    /// </remarks>
    private void ReduceRowsScalar(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        var v = Limit(_inverse.TransformY(x + 0.5, centerY));
        GetSupport(v, _spanY, out var firstRow, out var lastRow);
        var sums = unsafe(MemoryMarshal.Cast<byte, double>(_sums!.Span))[..(samples.Length * ValuesPerSum)];
        sums.Clear();
        var counts = unsafe(MemoryMarshal.Cast<byte, int>(_tapCounts!.Span))[..samples.Length];
        var weights = unsafe(MemoryMarshal.Cast<byte, double>(_tapWeights!.Span))[..(int)_tapCapacity];
        var columns = unsafe(MemoryMarshal.Cast<byte, int>(_tapColumns!.Span))[..(int)_tapCapacity];
        var rows = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_rows!.Span))[.._regionWidth];
        _cachedRow0 = _cachedRow1 = int.MinValue;
        var totalY = 0d;
        for (var start = 0; start < samples.Length;)
        {
            // The columns of the pixels from 'start' to 'end' with a positive weight, -1 for those without a pixel, and
            // the first and the last column of the region they read
            var end = start;
            var tapCount = 0;
            var firstColumn = int.MaxValue;
            var lastColumn = -1;
            for (; end < samples.Length; end++)
            {
                var u = Limit(_inverse.TransformX(x + end + 0.5, centerY));
                GetSupport(u, _spanX, out var first, out var last);
                if (end > start && tapCount + (last - first + 1) > weights.Length)
                    break;

                var total = 0d;
                var count = 0;
                for (var column = first; column <= last; column++)
                {
                    var weightX = GetWeight(column, u, _spanX);
                    if (!(weightX > 0))
                        continue;

                    total += weightX;
                    var index = -1;
                    if (TryWrap(column, _left, _regionWidth, _wrapModeX, out var wrapped))
                    {
                        index = wrapped;
                        firstColumn = Math.Min(firstColumn, index);
                        lastColumn = Math.Max(lastColumn, index);
                    }

                    weights[tapCount + count] = weightX;
                    columns[tapCount + count] = index;
                    count++;
                }

                counts[end] = count;
                tapCount += count;
                sums[(end * ValuesPerSum) + 4] = total;
            }

            totalY = 0;
            for (var row = firstRow; row <= lastRow; row++)
            {
                var weightY = GetWeight(row, v, _spanY);
                if (!(weightY > 0))
                    continue;

                // A row without pixels, or whose pixels are not read, adds zeros
                totalY += weightY;
                if (lastColumn < firstColumn || !TryWrap(row, _top, _regionHeight, _wrapModeY, out var wrapped))
                    continue;

                _source!.ReadRgba64(_storage!, _top + wrapped, _left + firstColumn, rows[firstColumn..(lastColumn + 1)]);
                var tap = 0;
                for (var i = start; i < end; i++)
                {
                    var sum = default(RowSum);
                    var tapEnd = tap + counts[i];
                    for (; tap < tapEnd; tap++)
                    {
                        var index = columns[tap];
                        if (index >= 0)
                        {
                            sum.Add(rows[index], weights[tap]);
                        }
                    }

                    var entry = sums.Slice(i * ValuesPerSum, ValuesPerSum);
                    entry[0] += weightY * sum.Alpha;
                    entry[1] += weightY * sum.Red;
                    entry[2] += weightY * sum.Green;
                    entry[3] += weightY * sum.Blue;
                }
            }

            start = end;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var entry = sums.Slice(i * ValuesPerSum, ValuesPerSum);
            samples[i] = ToColor(entry[0], entry[1], entry[2], entry[3], entry[4] * totalY);
        }
    }

    /// <summary>Evaluates a run of a reduced image whose pixels read any pixel of the image.</summary>
    private void ReducePixels(int y, int x, Span<Rgba64> samples)
    {
        var centerY = y + 0.5;
        for (var i = 0; i < samples.Length; i++)
        {
            var centerX = x + i + 0.5;
            samples[i] = Reduce(_inverse.TransformX(centerX, centerY), _inverse.TransformY(centerX, centerY), _spanX, _spanY);
        }
    }

    /// <summary>Gets the color of a reduced image at a position: every pixel within a span of it, weighted by its distance.</summary>
    private Rgba64 Reduce(double u, double v, double spanX, double spanY)
    {
        u = Limit(u);
        v = Limit(v);
        GetSupport(u, spanX, out var firstColumn, out var lastColumn);
        GetSupport(v, spanY, out var firstRow, out var lastRow);
        double alpha = 0, red = 0, green = 0, blue = 0, totalX = 0, totalY = 0;
        for (var row = firstRow; row <= lastRow; row++)
        {
            var weightY = GetWeight(row, v, spanY);
            if (!(weightY > 0))
                continue;

            totalY += weightY;
            var sum = default(RowSum);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var weightX = GetWeight(column, u, spanX);
                if (!(weightX > 0))
                    continue;

                sum.Total += weightX;
                if (TryRead(column, row, out var color))
                {
                    sum.Add(color, weightX);
                }
            }

            alpha += weightY * sum.Alpha;
            red += weightY * sum.Red;
            green += weightY * sum.Green;
            blue += weightY * sum.Blue;
            totalX = sum.Total;
        }

        return ToColor(alpha, red, green, blue, totalX * totalY);
    }

    /// <summary>
    /// Gets the rows or the columns of the image a pixel of the canvas reads when the image is reduced: those whose
    /// center is within the span of its position, or, with a box, those that overlap the span centered on it.
    /// </summary>
    private void GetSupport(double position, double span, out int first, out int last)
    {
        if (_isBox)
        {
            first = (int)Math.Floor(position - (span / 2));
            last = (int)Math.Ceiling(position + (span / 2)) - 1;
            return;
        }

        first = (int)Math.Floor(position - span - 0.5) + 1;
        last = (int)Math.Ceiling(position + span - 0.5) - 1;
    }

    /// <summary>
    /// Gets the weight of a row or of a column of the image for a position: 1 at its center, 0 a span away, or, with a
    /// box, the length of the part of it inside the span centered on the position.
    /// </summary>
    private double GetWeight(int index, double position, double span)
    {
        if (_isBox)
            return Math.Min(index + 1, position + (span / 2)) - Math.Max(index, position - (span / 2));

        return 1 - (Math.Abs(index + 0.5 - position) / span);
    }

    /// <summary>Brings a coordinate of the image within the positions that count. Written so that a coordinate that is not a number is far away.</summary>
    private static double Limit(double coordinate) => Math.Abs(coordinate) < MaxPosition ? coordinate : coordinate < 0 ? -MaxPosition : MaxPosition;

    /// <summary>Makes a color of the sums of a reduced image: the alpha over the sum of every weight, the colors over the alpha.</summary>
    private static Rgba64 ToColor(double alpha, double red, double green, double blue, double total)
    {
        // Written so that sums that are not numbers give no color
        if (!(total > 0 && alpha > 0))
            return default;

        var roundedAlpha = Round(alpha / total);
        return roundedAlpha == 0 ? default : new Rgba64(Round(red / alpha), Round(green / alpha), Round(blue / alpha), roundedAlpha);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort Round(double value)
    {
        var rounded = Math.Floor(value + RoundingOffset);
        return rounded <= 0 ? (ushort)0 : rounded >= ushort.MaxValue ? ushort.MaxValue : (ushort)rounded;
    }

    /// <summary>
    /// Gets the first of the two pixels around a coordinate of the image, in the coordinates of the frame, and the weight
    /// of the second one. Nearest sampling has one pixel, the one that contains the coordinate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Split(double coordinate, out int index, out double fraction)
    {
        // Written so that a coordinate that is not a number is far away
        if (!(Math.Abs(coordinate) < MaxPosition))
        {
            coordinate = coordinate < 0 ? -MaxPosition : MaxPosition;
        }

        if (_isNearest)
        {
            index = (int)Math.Floor(coordinate);
            fraction = 0;
            return;
        }

        var position = coordinate - 0.5;
        var floor = Math.Floor(position);
        index = (int)floor;
        fraction = position - floor;
    }

    /// <summary>Brings a row or a column of the image into the region, by the wrap mode.</summary>
    /// <param name="index">The row or the column, in the coordinates of the frame.</param>
    /// <param name="origin">The first row or column of the region.</param>
    /// <param name="size">The height or the width of the region.</param>
    /// <param name="mode">The wrap mode of the axis.</param>
    /// <param name="result">The row or the column, from the first one of the region.</param>
    /// <returns><see langword="false"/> when there is no pixel there: outside the region, without wrapping.</returns>
    /// <remarks>Inlined in the loops over the pixels, where the row or the column is nearly always in the region.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryWrap(int index, int origin, int size, ImageWrapMode mode, out int result)
    {
        var offset = (long)index - origin;
        if ((ulong)offset < (ulong)size)
        {
            result = (int)offset;
            return true;
        }

        return TryWrapOutside(offset, size, mode, out result);
    }

    /// <summary>Brings a row or a column outside the region into it, by the wrap mode.</summary>
    /// <param name="offset">The row or the column, from the first one of the region: outside the region.</param>
    /// <param name="size">The height or the width of the region.</param>
    /// <param name="mode">The wrap mode of the axis.</param>
    /// <param name="result">The row or the column, from the first one of the region.</param>
    /// <returns><see langword="false"/> when there is no pixel there: without wrapping.</returns>
    private static bool TryWrapOutside(long offset, int size, ImageWrapMode mode, out int result)
    {
        switch (mode)
        {
            case ImageWrapMode.Pad:
                result = offset < 0 ? 0 : size - 1;
                return true;

            case ImageWrapMode.Repeat:
                offset %= size;
                result = (int)(offset < 0 ? offset + size : offset);
                return true;

            case ImageWrapMode.Reflect:
                var period = 2L * size;
                offset %= period;
                if (offset < 0)
                {
                    offset += period;
                }

                result = (int)(offset < size ? offset : period - 1 - offset);
                return true;

            default:
                result = 0;
                return false;
        }
    }

    /// <summary>Gets a row of the region converted to 16-bit colors, from the two rows that are kept.</summary>
    /// <param name="row">The row, from the first one of the region.</param>
    /// <param name="other">The other row the caller needs: it is not replaced.</param>
    private ReadOnlySpan<Rgba64> LoadRow(int row, int other)
    {
        var rows = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_rows!.Span));
        if (_cachedRow0 == row)
            return rows[.._regionWidth];

        if (_cachedRow1 == row)
            return rows.Slice(_rowCapacity, _regionWidth);

        var useSecond = _cachedRow0 == other;
        var target = useSecond ? rows.Slice(_rowCapacity, _regionWidth) : rows[.._regionWidth];
        _source!.ReadRgba64(_storage!, _top + row, _left, target);
        if (useSecond)
        {
            _cachedRow1 = row;
        }
        else
        {
            _cachedRow0 = row;
        }

        return target;
    }

    /// <summary>Reads a pixel of the image.</summary>
    /// <returns><see langword="false"/> when there is no pixel there.</returns>
    private bool TryRead(int column, int row, out Rgba64 color)
    {
        color = default;
        if (!TryWrap(column, _left, _regionWidth, _wrapModeX, out var x) || !TryWrap(row, _top, _regionHeight, _wrapModeY, out var y))
            return false;

        if (_segmentState > 0 || (_segmentState == 0 && StartSegments()))
        {
            color = ReadSegment(x, y);
            return true;
        }

        _source!.ReadRgba64(_storage!, _top + y, _left + x, new Span<Rgba64>(ref color));
        return true;
    }

    /// <summary>Gets a pixel of the region from its segment, which is converted first when it is not kept yet.</summary>
    /// <param name="x">The column, from the first one of the region.</param>
    /// <param name="y">The row, from the first one of the region.</param>
    private Rgba64 ReadSegment(int x, int y) => GetSegmentPixel(x, y);

    /// <summary>
    /// Gets a reference to a pixel of the region in its segment, which is converted first when it is not kept yet: the
    /// next pixels of the row follow it, up to the end of the segment.
    /// </summary>
    /// <param name="x">The column, from the first one of the region.</param>
    /// <param name="y">The row, from the first one of the region.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Rgba64 GetSegmentPixel(int x, int y)
    {
        var segment = x >> SegmentShift;
        var key = ((long)y * _segmentsPerRow) + segment;
        var slot = _isSegmentHashed ? (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> (64 - MaxSegmentBits)) : (int)key;
        var tag = ((ulong)_segmentGeneration << 32) | (uint)key;

        // The slot is below the number of slots: the key is below the number of segments, or the hash below MaxSegments
        Debug.Assert((uint)x < (uint)_regionWidth);
        Debug.Assert((uint)slot < (uint)(_segmentKeyBytes!.Length / sizeof(ulong)));
        Debug.Assert(((long)slot + 1) << SegmentShift <= _segmentColorBytes!.Length / Unsafe.SizeOf<Rgba64>());
        ref var keyEntry = ref unsafe(Unsafe.Add(ref Unsafe.As<byte, ulong>(ref MemoryMarshal.GetArrayDataReference(_segmentKeyBytes!)), slot));
        ref var colors = ref unsafe(Unsafe.Add(ref Unsafe.As<byte, Rgba64>(ref MemoryMarshal.GetArrayDataReference(_segmentColorBytes!)), slot << SegmentShift));
        if (keyEntry != tag)
        {
            LoadSegment(ref colors, segment, y);
            keyEntry = tag;
        }

        return ref unsafe(Unsafe.Add(ref colors, x & (SegmentLength - 1)));
    }

    /// <summary>Converts the pixels of a segment into its slot.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LoadSegment(ref Rgba64 colors, int segment, int y)
    {
        var start = segment << SegmentShift;
        _source!.ReadRgba64(_storage!, _top + y, _left + start, unsafe(MemoryMarshal.CreateSpan(ref colors, Math.Min(SegmentLength, _regionWidth - start))));
    }

    /// <summary>Rents the segments for the image of the lease, and forgets those of the previous ones.</summary>
    /// <returns><see langword="false"/> when the pixels are read one by one without segments: they would exceed the allocation limit.</returns>
    private bool StartSegments()
    {
        _segmentState = -1;
        if (_segmentSlots == 0)
            return false;

        if (_segmentSlots > _segmentCapacity)
        {
            // The segments only save time: without room for them, the pixels are read one by one
            var colorBytes = (long)_segmentSlots * SegmentLength * 8;
            var keyBytes = (long)_segmentSlots * sizeof(ulong);
            var previous = _segmentColors is null ? 0 : _scope.GetChargedSize(_segmentColors.Length) + _scope.GetChargedSize(_segmentKeys!.Length);
            if (_scope.LiveBytes - previous + _scope.GetChargedSize(colorBytes) + _scope.GetChargedSize(keyBytes) > _scope.Limit)
                return false;

            _segmentColors?.Dispose();
            _segmentKeys?.Dispose();
            _segmentColors = _segmentKeys = null;
            _segmentColorBytes = _segmentKeyBytes = null;
            _segmentCapacity = 0;
            _segmentColors = _scope.Rent((int)colorBytes, AllocationKind.Temporary, clear: false);
            _segmentKeys = _scope.Rent((int)keyBytes, AllocationKind.Temporary, clear: true);
            _segmentCapacity = _segmentSlots;
            _segmentColorBytes = _segmentColors.RawBuffer;
            _segmentKeyBytes = _segmentKeys.RawBuffer;
            _segmentGeneration = 0;
        }

        // A new generation for the lease: the keys of the previous ones no longer match, and the cleared keys never do
        _segmentGeneration++;
        if (_segmentGeneration == 0)
        {
            _segmentKeys!.Span.Clear();
            _segmentGeneration = 1;
        }

        _segmentState = 1;
        return true;
    }

    /// <summary>The weighted sum of colors multiplied by their alpha.</summary>
    internal interface IAccumulator
    {
        void Add(Rgba64 color, double weight);

        Rgba64 ToColor();
    }

    /// <summary>The weighted sum of colors multiplied by their alpha.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct Accumulator : IAccumulator
    {
        private double _red;
        private double _green;
        private double _blue;
        private double _alpha;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Rgba64 color, double weight)
        {
            var alpha = weight * color.A;
            _red += alpha * color.R;
            _green += alpha * color.G;
            _blue += alpha * color.B;
            _alpha += alpha;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Rgba64 ToColor()
        {
            var alpha = Round(_alpha);
            if (alpha == 0)
                return default;

            return new Rgba64(Round(_red / _alpha), Round(_green / _alpha), Round(_blue / _alpha), alpha);
        }
    }

    /// <summary>The sums of <see cref="Accumulator"/> as the lanes of one vector, (red, green, blue, alpha), with AVX.</summary>
    /// <remarks>
    /// Each lane is computed with the same products, sums, quotient and rounding as the field of <see cref="Accumulator"/>,
    /// so the colors are the same, bit for bit. It is used where no call separates the additions: the upper half of the
    /// vector would be saved and restored around each call.
    /// </remarks>
    internal struct VectorAccumulator : IAccumulator
    {
        private Vector256<double> _sums;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Rgba64 color, double weight)
        {
            // The channels as doubles, the alpha replaced by 1: alpha * 1 is the alpha, exactly
            var alpha = weight * color.A;
            var channels = Avx.ConvertToVector256Double(Sse41.ConvertToVector128Int32(Vector128.CreateScalar(unsafe(Unsafe.BitCast<Rgba64, ulong>(color))).AsUInt16()));
            _sums += Vector256.Create(alpha) * Avx.Blend(channels, Vector256.Create(1.0), 0b1000);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Rgba64 ToColor()
        {
            var total = _sums.GetElement(3);
            var alpha = Round(total);
            if (alpha == 0)
                return default;

            // The sums are finite and not negative: clamping is the comparisons of Round
            var rounded = Vector256.Floor((_sums / Vector256.Create(total)) + Vector256.Create(RoundingOffset));
            rounded = Vector256.Min(Vector256.Max(rounded, Vector256<double>.Zero), Vector256.Create((double)ushort.MaxValue));
            var values = Avx.ConvertToVector128Int32WithTruncation(rounded);
            var color = unsafe(Unsafe.BitCast<ulong, Rgba64>(Sse41.PackUnsignedSaturate(values, values).AsUInt64().ToScalar()));
            color.A = alpha;
            return color;
        }
    }

    /// <summary>The four sums of a pixel as the lanes of vectors, (red, green, blue, alpha), for the vector paths of the reduced images.</summary>
    internal interface IColorLanes<TSelf>
        where TSelf : struct, IColorLanes<TSelf>
    {
        static abstract TSelf Zero { get; }

        static abstract TSelf Load(ref double source);

        static abstract void Store(TSelf value, ref double target);

        /// <summary>Gets <c>sum + factor * value</c>, lane by lane: a product, then a sum, without a fused multiply-add.</summary>
        static abstract TSelf AddProduct(TSelf sum, double factor, TSelf value);

        /// <summary>Converts the four 16-bit samples of a color (the bits of an <see cref="Rgba64"/>) to doubles.</summary>
        static abstract TSelf Channels(ulong color);

        /// <summary>
        /// Writes the weights of the cubic filter for a fraction <c>f</c>: <c>k(1 + f)</c>, <c>k(f)</c>, <c>k(1 - f)</c>
        /// and <c>k(2 - f)</c>, each computed with the operations of <see cref="Kernel"/>, in the same order.
        /// </summary>
        static abstract void CubicWeights(double fraction, ref double target);
    }

    /// <summary>Gets the cubic filter of distances, lane by lane, with the operations of <see cref="Kernel"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> CubicKernel(Vector128<double> distance)
    {
        var t = Vector128.Abs(distance);
        var near = ((((Vector128.Create(7.0) * t) - Vector128.Create(12.0)) * t * t) + Vector128.Create(16.0 / 3)) / Vector128.Create(6.0);
        var far = ((((Vector128.Create(-7.0 / 3) * t) + Vector128.Create(12.0)) * t * t) - (Vector128.Create(20.0) * t) + Vector128.Create(32.0 / 3)) / Vector128.Create(6.0);
        return Vector128.ConditionalSelect(Vector128.LessThan(t, Vector128.Create(1.0)), near, Vector128.ConditionalSelect(Vector128.LessThan(t, Vector128.Create(2.0)), far, Vector128<double>.Zero));
    }

    /// <summary>Gets the cubic filter of distances, lane by lane, with the operations of <see cref="Kernel"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> CubicKernel(Vector256<double> distance)
    {
        var t = Vector256.Abs(distance);
        var near = ((((Vector256.Create(7.0) * t) - Vector256.Create(12.0)) * t * t) + Vector256.Create(16.0 / 3)) / Vector256.Create(6.0);
        var far = ((((Vector256.Create(-7.0 / 3) * t) + Vector256.Create(12.0)) * t * t) - (Vector256.Create(20.0) * t) + Vector256.Create(32.0 / 3)) / Vector256.Create(6.0);
        return Vector256.ConditionalSelect(Vector256.LessThan(t, Vector256.Create(1.0)), near, Vector256.ConditionalSelect(Vector256.LessThan(t, Vector256.Create(2.0)), far, Vector256<double>.Zero));
    }

    /// <summary>The four sums of a pixel in one vector of 256 bits, with AVX.</summary>
    internal readonly struct Lanes256 : IColorLanes<Lanes256>
    {
        private readonly Vector256<double> _value;

        private Lanes256(Vector256<double> value) => _value = value;

        public static Lanes256 Zero => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Lanes256 Load(ref double source) => new(unsafe(Vector256.LoadUnsafe(ref source)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Lanes256 value, ref double target)
        {
            unsafe { value._value.StoreUnsafe(ref target); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Lanes256 AddProduct(Lanes256 sum, double factor, Lanes256 value) => new(sum._value + (Vector256.Create(factor) * value._value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Lanes256 Channels(ulong color) => new(Avx.ConvertToVector256Double(Sse41.ConvertToVector128Int32(Vector128.CreateScalar(color).AsUInt16())));

        /// <remarks><c>1 + 1 f</c>, <c>0 + 1 f</c>, <c>1 + (-1) f</c> and <c>2 + (-1) f</c> are <c>1 + f</c>, <c>f</c>, <c>1 - f</c> and <c>2 - f</c>, exactly.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CubicWeights(double fraction, ref double target)
        {
            var distances = Vector256.Create(1.0, 0, 1, 2) + (Vector256.Create(1.0, 1, -1, -1) * Vector256.Create(fraction));
            unsafe { CubicKernel(distances).StoreUnsafe(ref target); }
        }

    }

    /// <summary>The four sums of a pixel in two vectors of 128 bits.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Lanes128 : IColorLanes<Lanes128>
    {
        private readonly Vector128<double> _low;
        private readonly Vector128<double> _high;

        private Lanes128(Vector128<double> low, Vector128<double> high)
        {
            _low = low;
            _high = high;
        }

        public static Lanes128 Zero => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Lanes128 Load(ref double source) => new(unsafe(Vector128.LoadUnsafe(ref source)), unsafe(Vector128.LoadUnsafe(ref source, 2)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Lanes128 value, ref double target)
        {
            unsafe
            {
                value._low.StoreUnsafe(ref target);
                value._high.StoreUnsafe(ref target, 2);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Lanes128 AddProduct(Lanes128 sum, double factor, Lanes128 value)
        {
            var broadcast = Vector128.Create(factor);
            return new(sum._low + (broadcast * value._low), sum._high + (broadcast * value._high));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Lanes128 Channels(ulong color)
        {
            var samples = Vector128.WidenLower(Vector128.CreateScalar(color).AsUInt16());
            return new(ToDouble(Vector128.WidenLower(samples)), ToDouble(Vector128.WidenUpper(samples)));
        }

        /// <remarks><c>1 + 1 f</c>, <c>0 + 1 f</c>, <c>1 + (-1) f</c> and <c>2 + (-1) f</c> are <c>1 + f</c>, <c>f</c>, <c>1 - f</c> and <c>2 - f</c>, exactly.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CubicWeights(double fraction, ref double target)
        {
            var broadcast = Vector128.Create(fraction);
            var low = Vector128.Create(1.0, 0) + (Vector128.Create(1.0, 1) * broadcast);
            var high = Vector128.Create(1.0, 2) + (Vector128.Create(-1.0, -1) * broadcast);
            unsafe
            {
                CubicKernel(low).StoreUnsafe(ref target);
                CubicKernel(high).StoreUnsafe(ref target, 2);
            }
        }


        /// <summary>Converts integers below 2^52 to doubles, exactly: with the conversion of Arm64, or as the bits of 2^52 plus the integer, minus 2^52.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<double> ToDouble(Vector128<ulong> value)
        {
            if (AdvSimd.Arm64.IsSupported)
                return Vector128.ConvertToDouble(value);

            return (value | Vector128.Create(0x4330_0000_0000_0000UL)).AsDouble() - Vector128.Create(4503599627370496.0);
        }
    }

    /// <summary>The weighted sums of the pixels of a row of the image a pixel of the canvas reads, and the sum of the weights.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct RowSum
    {
        public double Alpha;
        public double Red;
        public double Green;
        public double Blue;
        public double Total;

        public void Add(Rgba64 color, double weight)
        {
            var alpha = weight * color.A;
            Alpha += alpha;
            Red += alpha * color.R;
            Green += alpha * color.G;
            Blue += alpha * color.B;
        }
    }
}
