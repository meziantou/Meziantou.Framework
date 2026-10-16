using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies the filters of <see cref="Filter"/> to the surfaces of a draw call.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> A surface holds a rectangle of the canvas, its pixels as colors multiplied by their alpha, as
/// fractions of one. A filter reads, beyond the surface, pixels that the edge mode gives: relative to the rectangle of
/// the edges (the canvas for a drawing, the rectangle of a backdrop), a pixel outside it is transparent, the nearest
/// pixel on it, the pixel of the opposite side, or the mirrored pixel (<see cref="FilterEdgeMode"/>); a pixel inside the
/// rectangle of the edges but outside the surface is transparent. A backdrop is always read mirrored. Sums are made in
/// double precision, in the order of the weights, and kept in single precision. A blur of a deviation of 2 pixels or
/// more is three box blurs (<see cref="BlurAxis"/>): its cost does not grow with the deviation.
/// </para>
/// <para>
/// The lengths of a filter are lengths of the drawing: they are multiplied by the scale of each axis, and an offset
/// goes through the linear part of the transformation of the canvas (<see cref="Configure"/>).
/// </para>
/// </remarks>
internal sealed class FilterProcessor : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly ImageConfiguration _configuration;
    private PooledBuffer? _row;
    private PooledBuffer? _weights;
    private FilterSurface? _scratch;
    private Rectangle _edges;
    private Rectangle _extent;
    private FilterEdgeMode? _edgeOverride;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _m11 = 1;
    private double _m12;
    private double _m21;
    private double _m22 = 1;
    private double _offsetX;
    private double _offsetY;
    private CancellationToken _cancellationToken;

    public FilterProcessor(AllocationScope scope, ImageConfiguration configuration)
    {
        _scope = scope;
        _configuration = configuration;
    }

    /// <summary>Sets what the next filters read beyond the surface, and how their lengths become pixels.</summary>
    /// <param name="edges">The rectangle the edge modes apply to.</param>
    /// <param name="edgeOverride">The edge mode of every filter, or <see langword="null"/> for the one of each filter.</param>
    /// <param name="scaleX">The factor of the lengths along x.</param>
    /// <param name="scaleY">The factor of the lengths along y.</param>
    /// <param name="m11">The linear part of the transformation of the canvas, for the offsets.</param>
    /// <param name="m12">The factor of x in the transformed y.</param>
    /// <param name="m21">The factor of y in the transformed x.</param>
    /// <param name="m22">The factor of y in the transformed y.</param>
    /// <param name="offsetX">The translation along x of the transformation of the canvas, for the rectangles of the filters.</param>
    /// <param name="offsetY">The translation along y of the transformation of the canvas.</param>
    /// <param name="cancellationToken">The token checked every few rows.</param>
    public void Configure(Rectangle edges, FilterEdgeMode? edgeOverride, double scaleX, double scaleY, double m11, double m12, double m21, double m22, double offsetX, double offsetY, CancellationToken cancellationToken)
    {
        _offsetX = offsetX;
        _offsetY = offsetY;
        _edges = edges;
        _extent = edges;
        _edgeOverride = edgeOverride;
        _scaleX = scaleX;
        _scaleY = scaleY;
        _m11 = m11;
        _m12 = m12;
        _m21 = m21;
        _m22 = m22;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Gets or sets the rectangle the edge modes apply to; setting it sets <see cref="Extent"/> too.</summary>
    public Rectangle Edges
    {
        get => _edges;
        set => _edges = _extent = value;
    }

    /// <summary>
    /// Gets or sets the rectangle beyond which a filter whose edge mode is transparent reads transparent pixels: the
    /// edges, or a layer that holds what is drawn beyond the canvas, which such a filter reads.
    /// </summary>
    public Rectangle Extent
    {
        get => _extent;
        set => _extent = value;
    }

    /// <summary>Gets the edge mode of every filter, or <see langword="null"/> for the one of each filter: mirrored for a backdrop.</summary>
    public FilterEdgeMode? EdgeOverride => _edgeOverride;

    /// <summary>Gets the scope the surfaces of the filters are rented from.</summary>
    public AllocationScope Scope => _scope;

    /// <summary>Gets the token checked every few rows.</summary>
    public CancellationToken CancellationToken => _cancellationToken;

    /// <summary>Gets the factor of the lengths along x.</summary>
    public double ScaleX => _scaleX;

    /// <summary>Gets the factor of the lengths along y.</summary>
    public double ScaleY => _scaleY;

    /// <summary>Gets the image on the canvas of a vector of the drawing: through the linear part of the transformation.</summary>
    public (double X, double Y) MapVector(PointD vector) => ((_m11 * vector.X) + (_m21 * vector.Y), (_m12 * vector.X) + (_m22 * vector.Y));

    /// <summary>
    /// Draws a callback on a rectangle of a surface, through the transformation of the canvas: on an image of the
    /// rectangle, 16-bit, then read as colors multiplied by their alpha.
    /// </summary>
    public void Draw(DrawingAction action, FilterSurface surface, Rectangle area)
    {
        using var image = new Image<Rgba64>(_configuration, new Size(area.Width, area.Height), _scope, layoutOptions: null);
        var placement = new AffineTransform(_m11, _m12, _m21, _m22, _offsetX - area.Left, _offsetY - area.Top);
        var frame = image.Frames[0];
        frame.Draw(canvas => canvas.Transform(placement, action), _cancellationToken);
        var values = surface.Values;
        for (var y = 0; y < area.Height; y++)
        {
            var offset = surface.IndexOf(area.Left, area.Top + y);
            for (var x = 0; x < area.Width; x++)
            {
                var pixel = frame[x, y];
                var alpha = pixel.A / 65535.0;
                values[offset + (4 * x)] = (float)(pixel.R / 65535.0 * alpha);
                values[offset + (4 * x) + 1] = (float)(pixel.G / 65535.0 * alpha);
                values[offset + (4 * x) + 2] = (float)(pixel.B / 65535.0 * alpha);
                values[offset + (4 * x) + 3] = (float)alpha;
            }
        }
    }

    /// <summary>Gets the image on the canvas of a point of the drawing.</summary>
    public (double X, double Y) ToCanvas(double x, double y) => ((_m11 * x) + (_m21 * y) + _offsetX, (_m12 * x) + (_m22 * y) + _offsetY);

    /// <summary>Gets the point of the drawing whose image is a point of the canvas.</summary>
    public (double X, double Y) ToDrawing(double x, double y)
    {
        var determinant = (_m11 * _m22) - (_m12 * _m21);
        var dx = x - _offsetX;
        var dy = y - _offsetY;
        return (((_m22 * dx) - (_m21 * dy)) / determinant, ((_m11 * dy) - (_m12 * dx)) / determinant);
    }

    /// <summary>Gets the pixels of the canvas whose center is in the box of the image of a rectangle of the drawing.</summary>
    public Rectangle ToPixels(RectangleD rectangle)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? rectangle.Left : rectangle.Right;
            var y = (corner & 2) == 0 ? rectangle.Top : rectangle.Bottom;
            var px = (_m11 * x) + (_m21 * y) + _offsetX;
            var py = (_m12 * x) + (_m22 * y) + _offsetY;
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        const double Limit = 1 << 29;
        var left = (int)Math.Clamp(Math.Floor(minX + 0.5), -Limit, Limit);
        var top = (int)Math.Clamp(Math.Floor(minY + 0.5), -Limit, Limit);
        var right = (int)Math.Clamp(Math.Floor(maxX + 0.5), left, Limit);
        var bottom = (int)Math.Clamp(Math.Floor(maxY + 0.5), top, Limit);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>Reads pixels of a frame into a surface: 16-bit colors multiplied by their alpha.</summary>
    public static void Read(ImageFrame frame, PixelStorage storage, FilterSurface surface, Span<Rgba64> row)
    {
        var bounds = surface.Bounds;
        row = row[..bounds.Width];
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            frame.ReadRgba64(storage, y, bounds.Left, row);
            var values = surface.GetRow(y);
            for (var x = 0; x < row.Length; x++)
            {
                var pixel = row[x];
                var alpha = pixel.A / 65535.0;
                values[4 * x] = (float)(pixel.R / 65535.0 * alpha);
                values[(4 * x) + 1] = (float)(pixel.G / 65535.0 * alpha);
                values[(4 * x) + 2] = (float)(pixel.B / 65535.0 * alpha);
                values[(4 * x) + 3] = (float)alpha;
            }
        }
    }

    /// <summary>Writes a part of a surface to the storage of a layer (8-bit or 16-bit colors with alpha), as straight colors rounded to its samples.</summary>
    public static void Write(FilterSurface surface, PixelStorage storage, Rectangle area)
    {
        area = Rectangle.Intersect(area, surface.Bounds);
        if (area.IsEmpty)
            return;

        var isWide = storage.BytesPerPixel == 8;
        var max = isWide ? 65535.0 : 255.0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var values = surface.GetRow(y);
            var row = storage.GetRowSpanCore(y);
            var wide = isWide ? unsafe(MemoryMarshal.Cast<byte, Rgba64>(row)) : default;
            var narrow = isWide ? default : unsafe(MemoryMarshal.Cast<byte, Rgba32>(row));
            for (var x = area.Left; x < area.Right; x++)
            {
                var offset = 4 * (x - surface.Bounds.Left);
                double alpha = values[offset + 3];
                var a = Round(alpha * max, max);
                double r = 0, g = 0, b = 0;
                if (a != 0)
                {
                    r = Round(values[offset] / alpha * max, max);
                    g = Round(values[offset + 1] / alpha * max, max);
                    b = Round(values[offset + 2] / alpha * max, max);
                }

                if (isWide)
                {
                    wide[x] = a == 0 ? default : new Rgba64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
                }
                else
                {
                    narrow[x] = a == 0 ? default : new Rgba32((byte)r, (byte)g, (byte)b, (byte)a);
                }
            }
        }

        // Written so that a value that is not a number is zero
        static double Round(double value, double max)
        {
            var rounded = Math.Floor(value + 0.5 + (1.0 / 1048576));
            return rounded >= max ? max : rounded > 0 ? rounded : 0;
        }
    }

    /// <summary>Blurs a surface: the rows, then the columns of the result.</summary>
    public void Blur(FilterSurface surface, double sigmaX, double sigmaY, FilterEdgeMode edgeMode)
    {
        var mode = _edgeOverride ?? edgeMode;
        BlurAxis(surface, Math.Min(sigmaX * _scaleX, GaussianKernel.MaxSigma), mode, horizontal: true);
        BlurAxis(surface, Math.Min(sigmaY * _scaleY, GaussianKernel.MaxSigma), mode, horizontal: false);
    }

    /// <summary>Applies a color matrix to the straight colors of the pixels of a surface.</summary>
    public void ApplyColorMatrix(FilterSurface surface, ColorMatrix matrix) => ApplyColorMatrix(surface, matrix, surface.Bounds);

    /// <summary>Applies a color matrix to the straight colors of the pixels of a rectangle of a surface.</summary>
    public void ApplyColorMatrix(FilterSurface surface, ColorMatrix matrix, Rectangle area)
    {
        if (matrix.IsIdentity)
            return;

        area = Rectangle.Intersect(area, surface.Bounds);
        var m = matrix.Values;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var values = surface.GetRow(y).Slice(4 * (area.Left - surface.Bounds.Left), 4 * area.Width);
            ApplyColorMatrix(values, m);
        }
    }

    private static void ApplyColorMatrix(Span<float> values, ReadOnlySpan<double> m)
    {
        for (var i = 0; i < values.Length; i += 4)
        {
            double alpha = values[i + 3];
            double red = 0, green = 0, blue = 0;
            if (alpha > 0)
            {
                red = values[i] / alpha;
                green = values[i + 1] / alpha;
                blue = values[i + 2] / alpha;
            }

            var resultAlpha = Clamp((((((m[15] * red) + (m[16] * green)) + (m[17] * blue)) + (m[18] * alpha)) + m[19]));
            values[i] = (float)(Clamp((((((m[0] * red) + (m[1] * green)) + (m[2] * blue)) + (m[3] * alpha)) + m[4])) * resultAlpha);
            values[i + 1] = (float)(Clamp((((((m[5] * red) + (m[6] * green)) + (m[7] * blue)) + (m[8] * alpha)) + m[9])) * resultAlpha);
            values[i + 2] = (float)(Clamp((((((m[10] * red) + (m[11] * green)) + (m[12] * blue)) + (m[13] * alpha)) + m[14])) * resultAlpha);
            values[i + 3] = (float)resultAlpha;
        }
    }

    /// <summary>Gets the color of a surface between pixels: the four pixels around the position, interpolated; transparent beyond the surface.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="x">The position along x, in pixels of the canvas: the center of a pixel is at its index.</param>
    /// <param name="y">The position along y.</param>
    /// <param name="result">The red, green, blue and alpha, multiplied by the alpha.</param>
    public static void Interpolate(FilterSurface surface, double x, double y, Span<double> result)
    {
        result.Clear();
        var column = Math.Floor(x);
        var row = Math.Floor(y);
        var fx = x - column;
        var fy = y - row;
        var bounds = surface.Bounds;
        var values = surface.Values;
        for (var corner = 0; corner < 4; corner++)
        {
            var wx = (corner & 1) == 0 ? 1 - fx : fx;
            var wy = (corner & 2) == 0 ? 1 - fy : fy;
            var weight = wy * wx;
            if (weight == 0)
                continue;

            var px = column + (corner & 1);
            var py = row + ((corner >> 1) & 1);
            if (px < bounds.Left || px >= bounds.Right || py < bounds.Top || py >= bounds.Bottom)
                continue;

            var offset = surface.IndexOf((int)px, (int)py);
            result[0] += weight * values[offset];
            result[1] += weight * values[offset + 1];
            result[2] += weight * values[offset + 2];
            result[3] += weight * values[offset + 3];
        }
    }

    /// <summary>Draws the shadow of a surface under it, or replaces the surface by its shadow.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="red">The red of the shadow, as a fraction of one.</param>
    /// <param name="green">The green of the shadow.</param>
    /// <param name="blue">The blue of the shadow.</param>
    /// <param name="opacity">The alpha of the shadow.</param>
    /// <param name="offset">The offset of the shadow, in the drawing.</param>
    /// <param name="sigmaX">The deviation of the blur of the shadow along x, in the drawing.</param>
    /// <param name="sigmaY">The deviation of the blur of the shadow along y, in the drawing.</param>
    /// <param name="shadowOnly"><see langword="true"/> to replace the surface by its shadow.</param>
    public void DropShadow(FilterSurface surface, double red, double green, double blue, double opacity, PointD offset, double sigmaX, double sigmaY, bool shadowOnly)
    {
        // The offset on the canvas, through the linear part of the transformation
        var dx = (_m11 * offset.X) + (_m21 * offset.Y);
        var dy = (_m12 * offset.X) + (_m22 * offset.Y);
        var shadow = GetScratch(surface.Bounds);
        var bounds = surface.Bounds;
        var source = surface.Values;
        var target = shadow.Values;

        // The alpha at the position the offset moves each pixel from: the four pixels around it, interpolated
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var sourceY = y - dy;
            var row = (int)Math.Floor(sourceY);
            var fy = sourceY - row;
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                var sourceX = x - dx;
                var column = (int)Math.Floor(sourceX);
                var fx = sourceX - column;
                var alpha = ((1 - fy) * (((1 - fx) * ReadAlpha(surface, column, row, FilterEdgeMode.Transparent)) + (fx * ReadAlpha(surface, column + 1, row, FilterEdgeMode.Transparent))))
                    + (fy * (((1 - fx) * ReadAlpha(surface, column, row + 1, FilterEdgeMode.Transparent)) + (fx * ReadAlpha(surface, column + 1, row + 1, FilterEdgeMode.Transparent))));
                var offsetIndex = 4 * (((y - bounds.Top) * bounds.Width) + (x - bounds.Left));
                target[offsetIndex] = 0;
                target[offsetIndex + 1] = 0;
                target[offsetIndex + 2] = 0;
                target[offsetIndex + 3] = (float)alpha;
            }
        }

        var mode = _edgeOverride ?? FilterEdgeMode.Transparent;
        BlurAxis(shadow, Math.Min(sigmaX * _scaleX, GaussianKernel.MaxSigma), mode, horizontal: true);
        BlurAxis(shadow, Math.Min(sigmaY * _scaleY, GaussianKernel.MaxSigma), mode, horizontal: false);

        // The color of the shadow, multiplied by its alpha; what casts it over it, unless the shadow is alone
        for (var i = 0; i < source.Length; i += 4)
        {
            var alpha = Math.Clamp(target[i + 3] * opacity, 0, 1);
            var keep = shadowOnly ? 0 : 1d;
            var under = 1 - (keep * source[i + 3]);
            source[i] = (float)((keep * source[i]) + (under * red * alpha));
            source[i + 1] = (float)((keep * source[i + 1]) + (under * green * alpha));
            source[i + 2] = (float)((keep * source[i + 2]) + (under * blue * alpha));
            source[i + 3] = (float)((keep * source[i + 3]) + (under * alpha));
        }
    }

    public void Dispose()
    {
        _row?.Dispose();
        _weights?.Dispose();
        _scratch?.Dispose();
        _row = _weights = null;
        _scratch = null;
    }

    /// <summary>Brings a value between 0 and 1. Written so that a value that is not a number is zero.</summary>
    private static double Clamp(double value) => value > 0 ? (value < 1 ? value : 1) : 0;

    /// <summary>Gets a surface of the size of another one, kept for the filters that follow.</summary>
    private FilterSurface GetScratch(Rectangle bounds)
    {
        if (_scratch is null || _scratch.Bounds != bounds)
        {
            _scratch?.Dispose();
            _scratch = null;
            _scratch = new FilterSurface(_scope, bounds);
        }

        return _scratch;
    }

    /// <summary>
    /// Blurs a surface along one axis, in place: with the weights of <see cref="GaussianKernel"/> below a deviation of
    /// 2 pixels, and with three box blurs from it, as "Filter Effects Module Level 1" defines <c>feGaussianBlur</c>:
    /// <c>d = floor(3 sqrt(2 pi) s / 4 + 1/2)</c>; three boxes of <c>d</c> pixels centered on the pixel when <c>d</c> is
    /// odd, and otherwise one of <c>d</c> pixels ending on the pixel, one of <c>d</c> pixels starting on it, and one of
    /// <c>d + 1</c> pixels centered on it.
    /// </summary>
    private void BlurAxis(FilterSurface surface, double sigma, FilterEdgeMode mode, bool horizontal)
    {
        if (!(sigma > 0))
            return;

        if (sigma >= 2)
        {
            var size = (int)Math.Floor((3 * Math.Sqrt(2 * Math.PI) * sigma / 4) + 0.5);
            if ((size & 1) != 0)
            {
                var half = size / 2;
                BoxAxis(surface, half, half, mode, horizontal);
                BoxAxis(surface, half, half, mode, horizontal);
                BoxAxis(surface, half, half, mode, horizontal);
            }
            else
            {
                var half = size / 2;
                BoxAxis(surface, half, half - 1, mode, horizontal);
                BoxAxis(surface, half - 1, half, mode, horizontal);
                BoxAxis(surface, half, half, mode, horizontal);
            }

            return;
        }

        var radius = GaussianKernel.GetRadius(sigma);
        var taps = (2 * radius) + 1;
        if (_weights is null || _weights.Length < taps * sizeof(double))
        {
            _weights?.Dispose();
            _weights = null;
            _weights = ResampleWeights.Rent<double>(_scope, taps);
        }

        var weights = unsafe(MemoryMarshal.Cast<byte, double>(_weights.Span))[..taps];
        GaussianKernel.Fill(sigma, weights);
        var bounds = surface.Bounds;
        var length = horizontal ? bounds.Width : bounds.Height;
        var lines = horizontal ? bounds.Height : bounds.Width;
        if (_row is null || _row.Length < 4 * length * sizeof(float))
        {
            _row?.Dispose();
            _row = null;
            _row = ResampleWeights.Rent<float>(_scope, 4L * length);
        }

        var line = unsafe(MemoryMarshal.Cast<byte, float>(_row.Span))[..(4 * length)];
        var values = surface.Values;
        var stride = horizontal ? 4 : 4 * bounds.Width;
        var start = horizontal ? bounds.Left : bounds.Top;
        var edges = mode == FilterEdgeMode.Transparent ? _extent : _edges;
        var edgeStart = horizontal ? edges.Left : edges.Top;
        var edgeEnd = horizontal ? edges.Right : edges.Bottom;
        for (var index = 0; index < lines; index++)
        {
            if ((index & 15) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            // The line, copied: the blur reads it while it is written
            var first = horizontal ? 4 * index * bounds.Width : 4 * index;
            for (var i = 0; i < length; i++)
            {
                values.Slice(first + (i * stride), 4).CopyTo(line.Slice(4 * i, 4));
            }

            for (var i = 0; i < length; i++)
            {
                double red = 0, green = 0, blue = 0, alpha = 0;
                for (var tap = 0; tap < taps; tap++)
                {
                    var position = start + i - radius + tap;
                    if (!TryMap(ref position, edgeStart, edgeEnd, mode))
                        continue;

                    var at = position - start;
                    if ((uint)at >= (uint)length)
                        continue;

                    var weight = weights[tap];
                    var entry = line.Slice(4 * at, 4);
                    red += weight * entry[0];
                    green += weight * entry[1];
                    blue += weight * entry[2];
                    alpha += weight * entry[3];
                }

                var output = values.Slice(first + (i * stride), 4);
                output[0] = (float)red;
                output[1] = (float)green;
                output[2] = (float)blue;
                output[3] = (float)alpha;
            }
        }
    }

    /// <summary>Averages the pixels of a window around each pixel of a surface along one axis, in place: from <paramref name="before"/> pixels before it to <paramref name="after"/> pixels after it.</summary>
    private void BoxAxis(FilterSurface surface, int before, int after, FilterEdgeMode mode, bool horizontal)
    {
        var bounds = surface.Bounds;
        var length = horizontal ? bounds.Width : bounds.Height;
        var lines = horizontal ? bounds.Height : bounds.Width;
        if (_row is null || _row.Length < 4 * length * sizeof(float))
        {
            _row?.Dispose();
            _row = null;
            _row = ResampleWeights.Rent<float>(_scope, 4L * length);
        }

        var line = unsafe(MemoryMarshal.Cast<byte, float>(_row.Span))[..(4 * length)];
        var values = surface.Values;
        var stride = horizontal ? 4 : 4 * bounds.Width;
        var start = horizontal ? bounds.Left : bounds.Top;
        var edges = mode == FilterEdgeMode.Transparent ? _extent : _edges;
        var edgeStart = horizontal ? edges.Left : edges.Top;
        var edgeEnd = horizontal ? edges.Right : edges.Bottom;
        var size = before + after + 1;
        Span<double> sum = stackalloc double[4];
        for (var index = 0; index < lines; index++)
        {
            if ((index & 15) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var first = horizontal ? 4 * index * bounds.Width : 4 * index;
            for (var i = 0; i < length; i++)
            {
                values.Slice(first + (i * stride), 4).CopyTo(line.Slice(4 * i, 4));
            }

            // The window of the first pixel, then each step adds the pixel that enters it and removes the one that leaves it
            sum.Clear();
            for (var offset = -before; offset <= after; offset++)
            {
                Add(sum, line, start + offset, 1);
            }

            for (var i = 0; i < length; i++)
            {
                var output = values.Slice(first + (i * stride), 4);
                output[0] = (float)(sum[0] / size);
                output[1] = (float)(sum[1] / size);
                output[2] = (float)(sum[2] / size);
                output[3] = (float)(sum[3] / size);
                Add(sum, line, start + i + after + 1, 1);
                Add(sum, line, start + i - before, -1);
            }
        }

        void Add(Span<double> sum, ReadOnlySpan<float> line, int position, int sign)
        {
            if (!TryMap(ref position, edgeStart, edgeEnd, mode))
                return;

            var at = position - start;
            if ((uint)at >= (uint)line.Length / 4)
                return;

            var entry = line.Slice(4 * at, 4);
            sum[0] += sign * entry[0];
            sum[1] += sign * entry[1];
            sum[2] += sign * entry[2];
            sum[3] += sign * entry[3];
        }
    }

    /// <summary>Gets the alpha of a pixel of a surface, or of the pixel the edge mode gives beyond it.</summary>
    private float ReadAlpha(FilterSurface surface, int x, int y, FilterEdgeMode mode)
    {
        mode = _edgeOverride ?? mode;
        var edges = mode == FilterEdgeMode.Transparent ? _extent : _edges;
        if (!TryMap(ref x, edges.Left, edges.Right, mode) || !TryMap(ref y, edges.Top, edges.Bottom, mode))
            return 0;

        var bounds = surface.Bounds;
        if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)
            return 0;

        return surface.Values[(4 * (((y - bounds.Top) * bounds.Width) + (x - bounds.Left))) + 3];
    }

    /// <summary>Brings a position into the range of the edges by an edge mode.</summary>
    /// <returns><see langword="false"/> when the position is outside and the mode is transparent.</returns>
    public static bool TryMap(ref int position, int start, int end, FilterEdgeMode mode)
    {
        if (position >= start && position < end)
            return true;

        var size = end - start;
        if (size <= 0)
            return false;

        var offset = (long)position - start;
        switch (mode)
        {
            case FilterEdgeMode.Duplicate:
                position = offset < 0 ? start : end - 1;
                return true;

            case FilterEdgeMode.Wrap:
                offset %= size;
                position = start + (int)(offset < 0 ? offset + size : offset);
                return true;

            case FilterEdgeMode.Mirror:
                var period = 2L * size;
                offset %= period;
                if (offset < 0)
                {
                    offset += period;
                }

                position = start + (int)(offset < size ? offset : period - 1 - offset);
                return true;

            default:
                return false;
        }
    }
}
