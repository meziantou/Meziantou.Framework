using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
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
    /// <summary>The number of columns the vertical passes blur at once, row by row.</summary>
    private const int StripColumns = 8;

    /// <summary>The number of weights of a blur below a deviation of 2 pixels: three deviations on each side, rounded up.</summary>
    private const int MaxStripWeights = 13;

    private readonly AllocationScope _scope;
    private readonly ImageConfiguration _configuration;
    private PooledBuffer? _row;
    private PooledBuffer? _weights;
    private PooledBuffer? _strip;
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

    /// <summary>
    /// Gets or sets a value indicating whether the vertical passes of the blurs read the rows of strips of columns, with
    /// vectors (the default when they are accelerated), rather than each column (the scalar reference). Both give the
    /// same bits.
    /// </summary>
    internal bool UseColumnStrips { get; set; } = Vector128.IsHardwareAccelerated;

    /// <summary>
    /// Gets or sets a value indicating whether the passes of the blurs that read lines of pixels sum each pixel with
    /// vectors, its four channels as the lanes (the default when vectors are accelerated), rather than channel by channel
    /// (the scalar reference). Both give the same bits.
    /// </summary>
    internal bool UseVectorPixels { get; set; } = Vector128.IsHardwareAccelerated;

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
            ConvertRow(row, surface.GetRow(y));
        }
    }

    /// <summary>Converts 16-bit colors to colors multiplied by their alpha, as fractions of one, with vectors when they are accelerated.</summary>
    internal static void ConvertRow(ReadOnlySpan<Rgba64> row, Span<float> values)
    {
        if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
        {
            ConvertRow<Quad256>(row, values);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            ConvertRow<Quad128>(row, values);
        }
        else
        {
            ConvertRowScalar(row, values);
        }
    }

    /// <summary>Converts 16-bit colors as <see cref="ConvertRowScalar"/> does, a pixel at a time as the lanes of <typeparamref name="TQuad"/>: <c>(R, G, B, A) / 65535 * (a, a, a, 1)</c>.</summary>
    private static void ConvertRow<TQuad>(ReadOnlySpan<Rgba64> row, Span<float> values)
        where TQuad : struct, IQuad<TQuad>
    {
        values = values[..(4 * row.Length)];
        ref var target = ref unsafe(MemoryMarshal.GetReference(values));
        var scale = TQuad.Create(65535.0);
        for (var x = 0; x < row.Length; x++)
        {
            var pixel = row[x];
            var alpha = pixel.A / 65535.0;
            TQuad.Store(TQuad.FromSamples(unsafe(Unsafe.BitCast<Rgba64, ulong>(pixel))) / scale * TQuad.Create(alpha, alpha, alpha, 1), ref unsafe(Unsafe.Add(ref target, 4 * x)));
        }
    }

    /// <summary>Converts 16-bit colors to colors multiplied by their alpha, as fractions of one (the scalar reference of <see cref="ConvertRow"/>).</summary>
    internal static void ConvertRowScalar(ReadOnlySpan<Rgba64> row, Span<float> values)
    {
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

    /// <summary>Writes a part of a surface to the storage of a layer (8-bit or 16-bit colors with alpha), as straight colors rounded to its samples.</summary>
    public static void Write(FilterSurface surface, PixelStorage storage, Rectangle area)
    {
        area = Rectangle.Intersect(area, surface.Bounds);
        if (area.IsEmpty)
            return;

        var isWide = storage.BytesPerPixel == 8;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var values = surface.GetRow(y).Slice(4 * (area.Left - surface.Bounds.Left), 4 * area.Width);
            var row = storage.GetRowSpanCore(y);
            if (isWide)
            {
                WriteRow(values, unsafe(MemoryMarshal.Cast<byte, Rgba64>(row)).Slice(area.Left, area.Width));
            }
            else
            {
                WriteRow(values, unsafe(MemoryMarshal.Cast<byte, Rgba32>(row)).Slice(area.Left, area.Width));
            }
        }
    }

    /// <summary>Converts colors multiplied by their alpha to straight 16-bit colors, rounded, with vectors when they are accelerated.</summary>
    internal static void WriteRow(ReadOnlySpan<float> values, Span<Rgba64> pixels)
    {
        if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
        {
            WriteRow<Quad256>(values, pixels, unsafe(MemoryMarshal.Cast<Rgba64, byte>(pixels)), isWide: true);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            WriteRow<Quad128>(values, pixels, unsafe(MemoryMarshal.Cast<Rgba64, byte>(pixels)), isWide: true);
        }
        else
        {
            WriteRowScalar(values, pixels, default, isWide: true);
        }
    }

    /// <summary>Converts colors multiplied by their alpha to straight 8-bit colors, rounded, with vectors when they are accelerated.</summary>
    internal static void WriteRow(ReadOnlySpan<float> values, Span<Rgba32> pixels)
    {
        if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
        {
            WriteRow<Quad256>(values, default, unsafe(MemoryMarshal.Cast<Rgba32, byte>(pixels)), isWide: false);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            WriteRow<Quad128>(values, default, unsafe(MemoryMarshal.Cast<Rgba32, byte>(pixels)), isWide: false);
        }
        else
        {
            WriteRowScalar(values, default, pixels, isWide: false);
        }
    }

    /// <summary>
    /// Converts colors multiplied by their alpha to straight colors as <see cref="WriteRowScalar"/> does, a pixel at a time
    /// as the lanes of <typeparamref name="TQuad"/>: <c>(r, g, b, a) / (a, a, a, 1) * max</c>, rounded.
    /// </summary>
    private static void WriteRow<TQuad>(ReadOnlySpan<float> values, Span<Rgba64> wide, Span<byte> bytes, bool isWide)
        where TQuad : struct, IQuad<TQuad>
    {
        var count = values.Length / 4;
        if (bytes.Length < count * (isWide ? 8 : 4))
            throw new ArgumentException("The pixels must hold the values.", nameof(bytes));

        var max = isWide ? 65535.0 : 255.0;
        var scale = TQuad.Create(max);
        ref var source = ref unsafe(MemoryMarshal.GetReference(values));
        for (var x = 0; x < count; x++)
        {
            ref var entry = ref unsafe(Unsafe.Add(ref source, 4 * x));
            double alpha = unsafe(Unsafe.Add(ref entry, 3));
            var rounded = TQuad.RoundSamples(TQuad.Load(ref entry) / TQuad.Create(alpha, alpha, alpha, 1) * scale, max);
            var samples = TQuad.GetAlpha(rounded) == 0 ? 0 : TQuad.ToSamples(rounded);
            if (isWide)
            {
                wide[x] = unsafe(Unsafe.BitCast<ulong, Rgba64>(samples));
            }
            else
            {
                // The samples are below 256: the low byte of each
                unsafe { Unsafe.WriteUnaligned(ref bytes[4 * x], (uint)((samples & 0xFF) | ((samples >> 8) & 0xFF00) | ((samples >> 16) & 0xFF_0000) | ((samples >> 24) & 0xFF00_0000))); }
            }
        }
    }

    /// <summary>Converts colors multiplied by their alpha to straight colors, rounded (the scalar reference of <see cref="WriteRow(ReadOnlySpan{float}, Span{Rgba64})"/>).</summary>
    internal static void WriteRowScalar(ReadOnlySpan<float> values, Span<Rgba64> wide, Span<Rgba32> narrow, bool isWide)
    {
        var max = isWide ? 65535.0 : 255.0;
        for (var x = 0; x < values.Length / 4; x++)
        {
            var offset = 4 * x;
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

        // Written so that a value that is not a number is zero
        static double Round(double value, double max)
        {
            var rounded = Math.Floor(value + 0.5 + (1.0 / 1048576));
            return rounded >= max ? max : rounded > 0 ? rounded : 0;
        }
    }

    /// <summary>Makes the pixels of a rectangle of the storage of a layer transparent, except those of another rectangle.</summary>
    public static void Clear(PixelStorage storage, Rectangle area, Rectangle keep)
    {
        keep = Rectangle.Intersect(keep, area);
        var bytesPerPixel = storage.BytesPerPixel;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var row = storage.GetRowSpanCore(y)[(area.Left * bytesPerPixel)..(area.Right * bytesPerPixel)];
            if (keep.IsEmpty || y < keep.Top || y >= keep.Bottom)
            {
                row.Clear();
                continue;
            }

            row[..((keep.Left - area.Left) * bytesPerPixel)].Clear();
            row[((keep.Right - area.Left) * bytesPerPixel)..].Clear();
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

    /// <summary>Applies a color matrix to the straight colors of pixels, with vectors when they are accelerated.</summary>
    internal static void ApplyColorMatrix(Span<float> values, ReadOnlySpan<double> m)
    {
        if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
        {
            ApplyColorMatrix<Quad256>(values, m);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            ApplyColorMatrix<Quad128>(values, m);
        }
        else
        {
            ApplyColorMatrixScalar(values, m);
        }
    }

    /// <summary>
    /// Applies a color matrix as <see cref="ApplyColorMatrixScalar"/> does, a pixel at a time: the four results are the
    /// lanes of <typeparamref name="TQuad"/>, each the sum of the same five products in the same order, the columns of the
    /// matrix being the vectors.
    /// </summary>
    private static void ApplyColorMatrix<TQuad>(Span<float> values, ReadOnlySpan<double> m)
        where TQuad : struct, IQuad<TQuad>
    {
        if (m.Length < 20)
            throw new ArgumentException("The matrix must have 20 values.", nameof(m));

        var red = TQuad.Create(m[0], m[5], m[10], m[15]);
        var green = TQuad.Create(m[1], m[6], m[11], m[16]);
        var blue = TQuad.Create(m[2], m[7], m[12], m[17]);
        var alpha = TQuad.Create(m[3], m[8], m[13], m[18]);
        var offset = TQuad.Create(m[4], m[9], m[14], m[19]);
        ref var source = ref unsafe(MemoryMarshal.GetReference(values));
        for (var i = 0; i + 4 <= values.Length; i += 4)
        {
            ref var entry = ref unsafe(Unsafe.Add(ref source, i));
            double a = unsafe(Unsafe.Add(ref entry, 3));
            double r = 0, g = 0, b = 0;
            if (a > 0)
            {
                r = entry / a;
                g = unsafe(Unsafe.Add(ref entry, 1)) / a;
                b = unsafe(Unsafe.Add(ref entry, 2)) / a;
            }

            var result = TQuad.Clamp((red * TQuad.Create(r)) + (green * TQuad.Create(g)) + (blue * TQuad.Create(b)) + (alpha * TQuad.Create(a)) + offset);
            var resultAlpha = TQuad.GetAlpha(result);
            TQuad.Store(result * TQuad.Create(resultAlpha, resultAlpha, resultAlpha, 1), ref entry);
        }
    }

    /// <summary>Applies a color matrix to the straight colors of pixels (the scalar reference of <see cref="ApplyColorMatrix(Span{float}, ReadOnlySpan{double})"/>).</summary>
    internal static void ApplyColorMatrixScalar(Span<float> values, ReadOnlySpan<double> m)
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
        var shadow = GetShadowAlpha(surface, dx, dy, sigmaX, sigmaY);
        CompositeShadow(surface, shadow, 0, 0, red, green, blue, opacity, shadowOnly);
    }

    /// <summary>
    /// Gets the alpha of the shadow of a surface: its alpha at the position an offset of the canvas moves each pixel
    /// from, blurred. The result is a surface of the bounds of the surface, kept until the next filter, whose alpha is
    /// the shadow (its colors are zero).
    /// </summary>
    /// <param name="surface">The surface that casts the shadow.</param>
    /// <param name="dx">The offset of the shadow along x, in pixels of the canvas.</param>
    /// <param name="dy">The offset of the shadow along y, in pixels of the canvas.</param>
    /// <param name="sigmaX">The deviation of the blur of the shadow along x, in the drawing.</param>
    /// <param name="sigmaY">The deviation of the blur of the shadow along y, in the drawing.</param>
    public FilterSurface GetShadowAlpha(FilterSurface surface, double dx, double dy, double sigmaX, double sigmaY)
    {
        var shadow = GetScratch(surface.Bounds);
        var bounds = surface.Bounds;
        var target = shadow.Values;

        // The pixels read where they are: in the surface, and in the edges, which leave them where they are
        var values = surface.Values;
        var inner = Rectangle.Intersect((_edgeOverride ?? FilterEdgeMode.Transparent) == FilterEdgeMode.Transparent ? _extent : _edges, bounds);

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
            var hasRows = row >= inner.Top && (long)row + 1 < inner.Bottom;
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                var sourceX = x - dx;
                var column = (int)Math.Floor(sourceX);
                var fx = sourceX - column;
                float topLeft, topRight, bottomLeft, bottomRight;
                if (hasRows && column >= inner.Left && (long)column + 1 < inner.Right)
                {
                    var index = (4 * (((row - bounds.Top) * bounds.Width) + (column - bounds.Left))) + 3;
                    topLeft = values[index];
                    topRight = values[index + 4];
                    bottomLeft = values[index + (4 * bounds.Width)];
                    bottomRight = values[index + (4 * bounds.Width) + 4];
                }
                else
                {
                    topLeft = ReadAlpha(surface, column, row, FilterEdgeMode.Transparent);
                    topRight = ReadAlpha(surface, column + 1, row, FilterEdgeMode.Transparent);
                    bottomLeft = ReadAlpha(surface, column, row + 1, FilterEdgeMode.Transparent);
                    bottomRight = ReadAlpha(surface, column + 1, row + 1, FilterEdgeMode.Transparent);
                }

                var alpha = ((1 - fy) * (((1 - fx) * topLeft) + (fx * topRight)))
                    + (fy * (((1 - fx) * bottomLeft) + (fx * bottomRight)));
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
        return shadow;
    }

    /// <summary>
    /// Paints the color of a shadow under the pixels of a surface, or replaces them by it. The shadow of the pixel
    /// <c>(x, y)</c> of the surface is the alpha of the pixel <c>(x + shiftX, y + shiftY)</c> of <paramref name="shadow"/>,
    /// and nothing beyond it.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="shadow">The alpha of the shadow (<see cref="GetShadowAlpha"/>).</param>
    /// <param name="shiftX">The column of the shadow of the first column of the surface, relative to that column.</param>
    /// <param name="shiftY">The row of the shadow of the first row of the surface, relative to that row.</param>
    /// <param name="red">The red of the shadow, as a fraction of one.</param>
    /// <param name="green">The green of the shadow.</param>
    /// <param name="blue">The blue of the shadow.</param>
    /// <param name="opacity">The alpha of the shadow.</param>
    /// <param name="shadowOnly"><see langword="true"/> to replace the surface by its shadow.</param>
    public static void CompositeShadow(FilterSurface surface, FilterSurface shadow, int shiftX, int shiftY, double red, double green, double blue, double opacity, bool shadowOnly)
    {
        if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
        {
            CompositeShadow<Quad256>(surface, shadow, shiftX, shiftY, red, green, blue, opacity, shadowOnly);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            CompositeShadow<Quad128>(surface, shadow, shiftX, shiftY, red, green, blue, opacity, shadowOnly);
        }
        else
        {
            CompositeShadowScalar(surface, shadow, shiftX, shiftY, red, green, blue, opacity, shadowOnly);
        }
    }

    /// <summary>
    /// Paints the color of a shadow as <see cref="CompositeShadowScalar"/> does, a pixel at a time as the lanes of
    /// <typeparamref name="TQuad"/>: <c>keep * pixel + (under * (red, green, blue, 1)) * alpha</c>.
    /// </summary>
    private static void CompositeShadow<TQuad>(FilterSurface surface, FilterSurface shadow, int shiftX, int shiftY, double red, double green, double blue, double opacity, bool shadowOnly)
        where TQuad : struct, IQuad<TQuad>
    {
        var target = shadow.Values;
        var bounds = surface.Bounds;
        var shadowBounds = shadow.Bounds;
        var color = TQuad.Create(red, green, blue, 1);
        var keep = shadowOnly ? 0 : 1d;
        var keepFactor = TQuad.Create(keep);
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            var shadowY = y + shiftY;
            var hasRow = shadowY >= shadowBounds.Top && shadowY < shadowBounds.Bottom;
            var row = surface.GetRow(y);
            ref var pixels = ref unsafe(MemoryMarshal.GetReference(row));
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                ref var pixel = ref unsafe(Unsafe.Add(ref pixels, 4 * (x - bounds.Left)));
                var shadowX = x + shiftX;
                var shadowAlpha = hasRow && shadowX >= shadowBounds.Left && shadowX < shadowBounds.Right ? target[shadow.IndexOf(shadowX, shadowY) + 3] : 0f;
                var alpha = Math.Clamp(shadowAlpha * opacity, 0, 1);
                var under = 1 - (keep * unsafe(Unsafe.Add(ref pixel, 3)));
                TQuad.Store((keepFactor * TQuad.Load(ref pixel)) + (TQuad.Create(under) * color * TQuad.Create(alpha)), ref pixel);
            }
        }
    }

    /// <summary>Paints the color of a shadow under the pixels of a surface, or replaces them by it (the scalar reference of <see cref="CompositeShadow"/>).</summary>
    internal static void CompositeShadowScalar(FilterSurface surface, FilterSurface shadow, int shiftX, int shiftY, double red, double green, double blue, double opacity, bool shadowOnly)
    {
        var source = surface.Values;
        var target = shadow.Values;
        var bounds = surface.Bounds;
        var shadowBounds = shadow.Bounds;

        // The color of the shadow, multiplied by its alpha; what casts it over it, unless the shadow is alone
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            var shadowY = y + shiftY;
            var hasRow = shadowY >= shadowBounds.Top && shadowY < shadowBounds.Bottom;
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                var i = surface.IndexOf(x, y);
                var shadowX = x + shiftX;
                var shadowAlpha = hasRow && shadowX >= shadowBounds.Left && shadowX < shadowBounds.Right ? target[shadow.IndexOf(shadowX, shadowY) + 3] : 0f;
                var alpha = Math.Clamp(shadowAlpha * opacity, 0, 1);
                var keep = shadowOnly ? 0 : 1d;
                var under = 1 - (keep * source[i + 3]);
                source[i] = (float)((keep * source[i]) + (under * red * alpha));
                source[i + 1] = (float)((keep * source[i + 1]) + (under * green * alpha));
                source[i + 2] = (float)((keep * source[i + 2]) + (under * blue * alpha));
                source[i + 3] = (float)((keep * source[i + 3]) + (under * alpha));
            }
        }
    }

    public void Dispose()
    {
        _row?.Dispose();
        _weights?.Dispose();
        _strip?.Dispose();
        _scratch?.Dispose();
        _row = _weights = _strip = null;
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
            if (!horizontal && UseColumnStrips)
            {
                var half = size / 2;
                var even = (size & 1) == 0 ? 1 : 0;
                BoxColumns(surface, [half, half - even, half - even, half, half, half], mode);
                return;
            }

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
        if (!horizontal && UseColumnStrips)
        {
            BlurColumns(surface, weights, radius, mode);
            return;
        }

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
            CopyLine(values, first, stride, line);
            if (UseVectorPixels)
            {
                if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
                {
                    WeightLine<Quad256>(line, values, first, stride, weights, radius, start, edgeStart, edgeEnd, mode);
                }
                else
                {
                    WeightLine<Quad128>(line, values, first, stride, weights, radius, start, edgeStart, edgeEnd, mode);
                }

                continue;
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
        Span<WindowRun> runBuffer = stackalloc WindowRun[MaxWindowRuns];
        for (var index = 0; index < lines; index++)
        {
            if ((index & 15) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var first = horizontal ? 4 * index * bounds.Width : 4 * index;
            CopyLine(values, first, stride, line);
            if (UseVectorPixels)
            {
                if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
                {
                    BoxLine<Quad256>(line, values, first, stride, before, after, start, edgeStart, edgeEnd, mode);
                }
                else
                {
                    BoxLine<Quad128>(line, values, first, stride, before, after, start, edgeStart, edgeEnd, mode);
                }

                continue;
            }

            // The window of the first pixel, then each step adds the pixel that enters it and removes the one that leaves it
            sum.Clear();
            var runs = runBuffer[..GetFirstWindow(before, after, start, edgeStart, edgeEnd, mode, runBuffer)];
            foreach (var run in runs)
            {
                for (var offset = run.First; offset <= run.Last; offset++)
                {
                    Add(sum, line, start + offset, run.Weight);
                }
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

        void Add(Span<double> sum, ReadOnlySpan<float> line, int position, double sign)
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

    /// <summary>Copies the pixels of a line of a surface, one after the other.</summary>
    private static void CopyLine(ReadOnlySpan<float> values, int first, int stride, Span<float> line)
    {
        if (stride == 4)
        {
            values.Slice(first, line.Length).CopyTo(line);
            return;
        }

        for (var i = 0; i < line.Length / 4; i++)
        {
            values.Slice(first + (i * stride), 4).CopyTo(line.Slice(4 * i, 4));
        }
    }

    /// <summary>
    /// Blurs a copied line with weights into a surface, as <see cref="BlurAxis"/> does, a pixel at a time as the lanes of
    /// <typeparamref name="TQuad"/>: the sum of the weight of each tap times its pixel, in the order of the taps.
    /// </summary>
    private static void WeightLine<TQuad>(ReadOnlySpan<float> line, Span<float> values, int first, int stride, ReadOnlySpan<double> weights, int radius, int start, int edgeStart, int edgeEnd, FilterEdgeMode mode)
        where TQuad : struct, IQuad<TQuad>
    {
        var length = line.Length / 4;
        if ((long)first + ((long)(length - 1) * stride) + 4 > values.Length)
            throw new ArgumentOutOfRangeException(nameof(first));

        ref var source = ref unsafe(MemoryMarshal.GetReference(line));
        ref var target = ref unsafe(MemoryMarshal.GetReference(values));
        // The pixels whose taps are all in the edges and in the line read them where they are
        var interiorStart = Math.Max(edgeStart, start) - start + radius;
        var interiorEnd = Math.Min(edgeEnd, start + length) - start - radius;
        for (var i = 0; i < length; i++)
        {
            var sum = TQuad.Zero;
            if (i >= interiorStart && i < interiorEnd)
            {
                ref var window = ref unsafe(Unsafe.Add(ref source, 4 * (i - radius)));
                for (var tap = 0; tap < weights.Length; tap++)
                {
                    sum += TQuad.Create(weights[tap]) * TQuad.Load(ref unsafe(Unsafe.Add(ref window, 4 * tap)));
                }

                TQuad.Store(sum, ref unsafe(Unsafe.Add(ref target, first + (i * stride))));
                continue;
            }

            for (var tap = 0; tap < weights.Length; tap++)
            {
                // The index is in the line, or negative
                var at = MapIndex(start + i - radius + tap, start, length, edgeStart, edgeEnd, mode);
                if (at >= 0)
                {
                    sum += TQuad.Create(weights[tap]) * TQuad.Load(ref unsafe(Unsafe.Add(ref source, 4 * at)));
                }
            }

            TQuad.Store(sum, ref unsafe(Unsafe.Add(ref target, first + (i * stride))));
        }
    }

    /// <summary>
    /// Averages the windows of a copied line into a surface, as <see cref="BoxAxis"/> does, a pixel at a time as the lanes
    /// of <typeparamref name="TQuad"/>: the sums of the first window, then each step adds the pixel that enters the window
    /// and subtracts the one that leaves it.
    /// </summary>
    private static void BoxLine<TQuad>(ReadOnlySpan<float> line, Span<float> values, int first, int stride, int before, int after, int start, int edgeStart, int edgeEnd, FilterEdgeMode mode)
        where TQuad : struct, IQuad<TQuad>
    {
        var length = line.Length / 4;
        if ((long)first + ((long)(length - 1) * stride) + 4 > values.Length)
            throw new ArgumentOutOfRangeException(nameof(first));

        // The indexes are in the line, or negative
        ref var source = ref unsafe(MemoryMarshal.GetReference(line));
        ref var target = ref unsafe(MemoryMarshal.GetReference(values));
        var sum = TQuad.Zero;
        Span<WindowRun> runs = stackalloc WindowRun[MaxWindowRuns];
        runs = runs[..GetFirstWindow(before, after, start, edgeStart, edgeEnd, mode, runs)];
        foreach (var run in runs)
        {
            var weight = TQuad.Create(run.Weight);
            for (var offset = run.First; offset <= run.Last; offset++)
            {
                var at = MapIndex(start + offset, start, length, edgeStart, edgeEnd, mode);
                if (at < 0)
                    continue;

                // A weight of one adds the value as it is, as the product of the scalar path does
                var value = TQuad.Load(ref unsafe(Unsafe.Add(ref source, 4 * at)));
                sum += run.Weight == 1 ? value : weight * value;
            }
        }

        var size = TQuad.Create(before + after + 1);
        for (var i = 0; i < length; i++)
        {
            TQuad.Store(sum / size, ref unsafe(Unsafe.Add(ref target, first + (i * stride))));
            var entering = MapIndex(start + i + after + 1, start, length, edgeStart, edgeEnd, mode);
            if (entering >= 0)
            {
                sum += TQuad.Load(ref unsafe(Unsafe.Add(ref source, 4 * entering)));
            }

            var leaving = MapIndex(start + i - before, start, length, edgeStart, edgeEnd, mode);
            if (leaving >= 0)
            {
                sum -= TQuad.Load(ref unsafe(Unsafe.Add(ref source, 4 * leaving)));
            }
        }
    }

    /// <summary>
    /// Blurs the columns of a surface with weights, in place, as <see cref="BlurAxis"/> blurs one column: a strip of
    /// columns is copied, then each row of the result sums the copied rows that its weights read, in the order of the
    /// weights, every column of the strip at once.
    /// </summary>
    private void BlurColumns(FilterSurface surface, ReadOnlySpan<double> weights, int radius, FilterEdgeMode mode)
    {
        var bounds = surface.Bounds;
        var width = bounds.Width;
        var length = bounds.Height;
        var edges = mode == FilterEdgeMode.Transparent ? _extent : _edges;
        var strip = GetStrip(bounds);
        var values = surface.Values;
        Span<int> rows = stackalloc int[MaxStripWeights];
        Span<double> rowWeights = stackalloc double[MaxStripWeights];
        for (var x = 0; x < width; x += StripColumns)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var lineLength = 4 * Math.Min(StripColumns, width - x);
            CopyStrip(values, width, x, strip, lineLength, length);
            for (var y = 0; y < length; y++)
            {
                // The rows the weights read, and their weights, in the order of the weights
                var count = 0;
                for (var tap = 0; tap < weights.Length; tap++)
                {
                    var at = MapIndex(bounds.Top + y - radius + tap, bounds.Top, length, edges.Top, edges.Bottom, mode);
                    if (at < 0)
                        continue;

                    rows[count] = at * lineLength;
                    rowWeights[count] = weights[tap];
                    count++;
                }

                WeightRows(strip, rows[..count], rowWeights[..count], values.Slice(4 * ((y * width) + x), lineLength));
            }
        }
    }

    /// <summary>
    /// Applies three box blurs to the columns of a surface, in place, as <see cref="BoxAxis"/> blurs one column: a strip
    /// of columns is copied, blurred into the surface, blurred back into the copy, then into the surface, every column
    /// of the strip at once.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="windows">The pixels before and after the pixel of each of the three windows.</param>
    /// <param name="mode">The edge mode.</param>
    private void BoxColumns(FilterSurface surface, ReadOnlySpan<int> windows, FilterEdgeMode mode)
    {
        var bounds = surface.Bounds;
        var width = bounds.Width;
        var length = bounds.Height;
        var edges = mode == FilterEdgeMode.Transparent ? _extent : _edges;
        var strip = GetStrip(bounds);
        var values = surface.Values;
        var stride = 4 * width;
        Span<double> sums = stackalloc double[4 * StripColumns];
        for (var x = 0; x < width; x += StripColumns)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var lineLength = 4 * Math.Min(StripColumns, width - x);
            var lineSums = sums[..lineLength];
            CopyStrip(values, width, x, strip, lineLength, length);
            BoxLines(strip, 0, lineLength, values, 4 * x, stride, lineSums, windows[0], windows[1], bounds.Top, length, edges.Top, edges.Bottom, mode);
            BoxLines(values, 4 * x, stride, strip, 0, lineLength, lineSums, windows[2], windows[3], bounds.Top, length, edges.Top, edges.Bottom, mode);
            BoxLines(strip, 0, lineLength, values, 4 * x, stride, lineSums, windows[4], windows[5], bounds.Top, length, edges.Top, edges.Bottom, mode);
        }
    }

    /// <summary>
    /// Averages the windows of lines of values, from <paramref name="before"/> lines before each line to
    /// <paramref name="after"/> lines after it: the sums of the first window, then each line adds the line that enters
    /// the window and removes the one that leaves it, as <see cref="BoxAxis"/> does for each value.
    /// </summary>
    private static void BoxLines(ReadOnlySpan<float> source, int sourceOffset, int sourceStride, Span<float> target, int targetOffset, int targetStride, Span<double> sums, int before, int after, int start, int length, int edgeStart, int edgeEnd, FilterEdgeMode mode)
    {
        var lineLength = sums.Length;
        sums.Clear();
        Span<WindowRun> runs = stackalloc WindowRun[MaxWindowRuns];
        runs = runs[..GetFirstWindow(before, after, start, edgeStart, edgeEnd, mode, runs)];
        foreach (var run in runs)
        {
            for (var offset = run.First; offset <= run.Last; offset++)
            {
                var at = MapIndex(start + offset, start, length, edgeStart, edgeEnd, mode);
                if (at < 0)
                    continue;

                var line = source.Slice(sourceOffset + (at * sourceStride), lineLength);
                if (run.Weight == 1)
                {
                    StepBox(sums, size: 0, output: default, line, leaving: default);
                }
                else
                {
                    for (var i = 0; i < lineLength; i++)
                    {
                        sums[i] += run.Weight * line[i];
                    }
                }
            }
        }

        var size = before + after + 1;
        for (var i = 0; i < length; i++)
        {
            var entering = MapIndex(start + i + after + 1, start, length, edgeStart, edgeEnd, mode);
            var leaving = MapIndex(start + i - before, start, length, edgeStart, edgeEnd, mode);
            StepBox(
                sums,
                size,
                target.Slice(targetOffset + (i * targetStride), lineLength),
                entering < 0 ? default : source.Slice(sourceOffset + (entering * sourceStride), lineLength),
                leaving < 0 ? default : source.Slice(sourceOffset + (leaving * sourceStride), lineLength));
        }
    }

    /// <summary>
    /// Writes sums divided by the size of their window, unless the output is empty, then adds the values of a line that
    /// enters the window and subtracts those of a line that leaves it, unless they are empty.
    /// </summary>
    private static void StepBox(Span<double> sums, int size, Span<float> output, ReadOnlySpan<float> entering, ReadOnlySpan<float> leaving)
    {
        var count = sums.Length;
        var hasOutput = !output.IsEmpty;
        var hasEntering = !entering.IsEmpty;
        var hasLeaving = !leaving.IsEmpty;
        if ((hasOutput && output.Length != count) || (hasEntering && entering.Length != count) || (hasLeaving && leaving.Length != count))
            throw new ArgumentException("The lines must have the length of the sums.", nameof(sums));

        var i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            var divisor = Vector256.Create((double)size);
            ref var sum = ref unsafe(MemoryMarshal.GetReference(sums));
            for (; i + 8 <= count; i += 8)
            {
                var low = unsafe(Vector256.LoadUnsafe(ref sum, (nuint)i));
                var high = unsafe(Vector256.LoadUnsafe(ref sum, (nuint)(i + 4)));
                if (hasOutput)
                {
                    unsafe { Vector256.Narrow(low / divisor, high / divisor).StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)i); }
                }

                if (hasEntering)
                {
                    var value = unsafe(Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(entering), (nuint)i));
                    low += Vector256.WidenLower(value);
                    high += Vector256.WidenUpper(value);
                }

                if (hasLeaving)
                {
                    var value = unsafe(Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(leaving), (nuint)i));
                    low -= Vector256.WidenLower(value);
                    high -= Vector256.WidenUpper(value);
                }

                unsafe
                {
                    low.StoreUnsafe(ref sum, (nuint)i);
                    high.StoreUnsafe(ref sum, (nuint)(i + 4));
                }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            var divisor = Vector128.Create((double)size);
            ref var sum = ref unsafe(MemoryMarshal.GetReference(sums));

            // The references of the optional lines are taken in their branches only: the reference of an empty span may be
            // null or point past the end of its array
            for (; i + 4 <= count; i += 4)
            {
                var low = unsafe(Vector128.LoadUnsafe(ref sum, (nuint)i));
                var high = unsafe(Vector128.LoadUnsafe(ref sum, (nuint)(i + 2)));
                if (hasOutput)
                {
                    unsafe { Vector128.Narrow(low / divisor, high / divisor).StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)i); }
                }

                if (hasEntering)
                {
                    var value = unsafe(Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(entering), (nuint)i));
                    low += Vector128.WidenLower(value);
                    high += Vector128.WidenUpper(value);
                }

                if (hasLeaving)
                {
                    var value = unsafe(Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(leaving), (nuint)i));
                    low -= Vector128.WidenLower(value);
                    high -= Vector128.WidenUpper(value);
                }

                unsafe
                {
                    low.StoreUnsafe(ref sum, (nuint)i);
                    high.StoreUnsafe(ref sum, (nuint)(i + 2));
                }
            }
        }

        for (; i < count; i++)
        {
            if (hasOutput)
            {
                output[i] = (float)(sums[i] / size);
            }

            if (hasEntering)
            {
                sums[i] += entering[i];
            }

            if (hasLeaving)
            {
                sums[i] -= leaving[i];
            }
        }
    }

    /// <summary>
    /// Sums rows of values with weights: each output value is the sum, in double precision and in the order of the rows,
    /// of the weight of each row times the value of its column, kept in single precision.
    /// </summary>
    /// <param name="lines">The rows, one after the other.</param>
    /// <param name="rows">The index of the first value of each row in <paramref name="lines"/>.</param>
    /// <param name="weights">The weight of each row.</param>
    /// <param name="output">The sums: as many as the values of a row.</param>
    private static void WeightRows(ReadOnlySpan<float> lines, ReadOnlySpan<int> rows, ReadOnlySpan<double> weights, Span<float> output)
    {
        var count = output.Length;
        foreach (var row in rows)
        {
            if ((uint)row > (uint)(lines.Length - count))
                throw new ArgumentOutOfRangeException(nameof(rows));
        }

        weights = weights[..rows.Length];
        var i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(lines));
            ref var target = ref unsafe(MemoryMarshal.GetReference(output));
            for (; i + 16 <= count; i += 16)
            {
                var sum0 = Vector256<double>.Zero;
                var sum1 = Vector256<double>.Zero;
                var sum2 = Vector256<double>.Zero;
                var sum3 = Vector256<double>.Zero;
                for (var row = 0; row < rows.Length; row++)
                {
                    var weight = Vector256.Create(weights[row]);
                    var offset = (nuint)(rows[row] + i);
                    var first = unsafe(Vector256.LoadUnsafe(ref source, offset));
                    var second = unsafe(Vector256.LoadUnsafe(ref source, offset + 8));
                    sum0 += weight * Vector256.WidenLower(first);
                    sum1 += weight * Vector256.WidenUpper(first);
                    sum2 += weight * Vector256.WidenLower(second);
                    sum3 += weight * Vector256.WidenUpper(second);
                }

                unsafe
                {
                    Vector256.Narrow(sum0, sum1).StoreUnsafe(ref target, (nuint)i);
                    Vector256.Narrow(sum2, sum3).StoreUnsafe(ref target, (nuint)(i + 8));
                }
            }

            for (; i + 8 <= count; i += 8)
            {
                var sum0 = Vector256<double>.Zero;
                var sum1 = Vector256<double>.Zero;
                for (var row = 0; row < rows.Length; row++)
                {
                    var weight = Vector256.Create(weights[row]);
                    var value = unsafe(Vector256.LoadUnsafe(ref source, (nuint)(rows[row] + i)));
                    sum0 += weight * Vector256.WidenLower(value);
                    sum1 += weight * Vector256.WidenUpper(value);
                }

                unsafe { Vector256.Narrow(sum0, sum1).StoreUnsafe(ref target, (nuint)i); }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(lines));
            ref var target = ref unsafe(MemoryMarshal.GetReference(output));
            for (; i + 8 <= count; i += 8)
            {
                var sum0 = Vector128<double>.Zero;
                var sum1 = Vector128<double>.Zero;
                var sum2 = Vector128<double>.Zero;
                var sum3 = Vector128<double>.Zero;
                for (var row = 0; row < rows.Length; row++)
                {
                    var weight = Vector128.Create(weights[row]);
                    var offset = (nuint)(rows[row] + i);
                    var first = unsafe(Vector128.LoadUnsafe(ref source, offset));
                    var second = unsafe(Vector128.LoadUnsafe(ref source, offset + 4));
                    sum0 += weight * Vector128.WidenLower(first);
                    sum1 += weight * Vector128.WidenUpper(first);
                    sum2 += weight * Vector128.WidenLower(second);
                    sum3 += weight * Vector128.WidenUpper(second);
                }

                unsafe
                {
                    Vector128.Narrow(sum0, sum1).StoreUnsafe(ref target, (nuint)i);
                    Vector128.Narrow(sum2, sum3).StoreUnsafe(ref target, (nuint)(i + 4));
                }
            }

            for (; i + 4 <= count; i += 4)
            {
                var sum0 = Vector128<double>.Zero;
                var sum1 = Vector128<double>.Zero;
                for (var row = 0; row < rows.Length; row++)
                {
                    var weight = Vector128.Create(weights[row]);
                    var value = unsafe(Vector128.LoadUnsafe(ref source, (nuint)(rows[row] + i)));
                    sum0 += weight * Vector128.WidenLower(value);
                    sum1 += weight * Vector128.WidenUpper(value);
                }

                unsafe { Vector128.Narrow(sum0, sum1).StoreUnsafe(ref target, (nuint)i); }
            }
        }

        for (; i < count; i++)
        {
            double sum = 0;
            for (var row = 0; row < rows.Length; row++)
            {
                sum += weights[row] * lines[rows[row] + i];
            }

            output[i] = (float)sum;
        }
    }

    /// <summary>Copies a strip of columns of a surface, row by row, one row after the other.</summary>
    private static void CopyStrip(ReadOnlySpan<float> values, int width, int x, Span<float> strip, int lineLength, int length)
    {
        for (var y = 0; y < length; y++)
        {
            values.Slice(4 * ((y * width) + x), lineLength).CopyTo(strip.Slice(y * lineLength, lineLength));
        }
    }

    /// <summary>Gets the buffer of a strip of columns of a surface: <see cref="StripColumns"/> columns, or fewer, of each row.</summary>
    private Span<float> GetStrip(Rectangle bounds)
    {
        var count = 4 * Math.Min(StripColumns, bounds.Width) * bounds.Height;
        if (_strip is null || _strip.Length < (long)count * sizeof(float))
        {
            _strip?.Dispose();
            _strip = null;
            _strip = ResampleWeights.Rent<float>(_scope, count);
        }

        return unsafe(MemoryMarshal.Cast<byte, float>(_strip.Span))[..count];
    }

    /// <summary>The most runs <see cref="GetFirstWindow"/> gives.</summary>
    internal const int MaxWindowRuns = 3;

    /// <summary>Offsets from the first position of a line, each summed with a weight.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct WindowRun(int First, int Last, double Weight);

    /// <summary>
    /// Gets the offsets, from the first position of a line, of the positions the window of the first position sums
    /// (<c>-before</c> to <c>after</c>), as runs of offsets whose values are summed with a weight.
    /// </summary>
    /// <returns>The number of runs written to <paramref name="runs"/>.</returns>
    /// <remarks>
    /// <para>
    /// A window far wider than the edges (the blur of a large deviation) would otherwise cost its own width for each
    /// line, while the positions it holds take few distinct values: the sums are the same with fewer steps.
    /// </para>
    /// <list type="bullet">
    /// <item>Out of its edges, a transparent edge mode gives nothing: only the positions within them can change the sums.</item>
    /// <item>A duplicate edge mode gives the first or the last position for every position before or after the edges: each of them is summed once, weighted by their count.</item>
    /// <item>A wrap or mirror edge mode gives positions that repeat with a period of the size of the edges, or twice that: whole periods are summed once, weighted by their count, and the rest of the window as it is.</item>
    /// </list>
    /// <para>
    /// A window within twice the period is summed position by position, so that the blurs of a usual deviation add the
    /// same values in the same order as they always did.
    /// </para>
    /// </remarks>
    internal static int GetFirstWindow(int before, int after, int start, int edgeStart, int edgeEnd, FilterEdgeMode mode, Span<WindowRun> runs)
    {
        var first = -before;
        var last = after;
        var size = edgeEnd - edgeStart;
        if (mode is not (FilterEdgeMode.Duplicate or FilterEdgeMode.Wrap or FilterEdgeMode.Mirror) || size <= 0)
        {
            // Nothing is read out of the edges (TryMap)
            first = Math.Max(first, edgeStart - start);
            last = Math.Min(last, edgeEnd - 1 - start);
            if (first > last)
                return 0;

            runs[0] = new(first, last, 1);
            return 1;
        }

        var period = mode == FilterEdgeMode.Mirror ? 2L * size : size;
        var width = (long)last - first + 1;
        if (width <= 2 * period)
        {
            runs[0] = new(first, last, 1);
            return 1;
        }

        var count = 0;
        if (mode == FilterEdgeMode.Duplicate)
        {
            var lowEdge = edgeStart - start;
            var highEdge = edgeEnd - 1 - start;
            if (first < lowEdge)
            {
                runs[count++] = new(lowEdge, lowEdge, Math.Min(last, lowEdge - 1) - first + 1);
            }

            var middleFirst = Math.Max(first, lowEdge);
            var middleLast = Math.Min(last, highEdge);
            if (middleFirst <= middleLast)
            {
                runs[count++] = new(middleFirst, middleLast, 1);
            }

            if (last > highEdge)
            {
                runs[count++] = new(highEdge, highEdge, last - Math.Max(first, highEdge + 1) + 1);
            }

            return count;
        }

        var remainder = (int)(width % period);
        if (remainder > 0)
        {
            runs[count++] = new(first, first + remainder - 1, 1);
        }

        runs[count++] = new(first + remainder, first + remainder + (int)period - 1, width / period);
        return count;
    }

    /// <summary>Gets the index in a line of the pixel an edge mode reads at a position, or -1 for a transparent pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MapIndex(int position, int start, int length, int edgeStart, int edgeEnd, FilterEdgeMode mode)
    {
        // A position within the edges is not moved
        if (position >= edgeStart && position < edgeEnd)
        {
            var inside = position - start;
            return (uint)inside < (uint)length ? inside : -1;
        }

        return MapIndexOutside(position, start, length, edgeStart, edgeEnd, mode);
    }

    /// <summary>Gets the index in a line of the pixel an edge mode reads at a position outside the edges, or -1 for a transparent pixel.</summary>
    private static int MapIndexOutside(int position, int start, int length, int edgeStart, int edgeEnd, FilterEdgeMode mode)
    {
        if (!TryMap(ref position, edgeStart, edgeEnd, mode))
            return -1;

        var at = position - start;
        return (uint)at < (uint)length ? at : -1;
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

    /// <summary>
    /// The four channels of a pixel as doubles, as the lanes of vectors, (red, green, blue, alpha): each lane has the
    /// operations of the scalar code on its channel, in the same order, without a fused multiply-add.
    /// </summary>
    internal interface IQuad<TSelf>
        where TSelf : struct, IQuad<TSelf>
    {
        static abstract TSelf Zero { get; }

        static abstract TSelf Create(double value);

        static abstract TSelf Create(double red, double green, double blue, double alpha);

        /// <summary>Converts the four 16-bit samples of a color (the bits of an <see cref="Rgba64"/>) to doubles.</summary>
        static abstract TSelf FromSamples(ulong color);

        /// <summary>Loads four floats, as doubles.</summary>
        static abstract TSelf Load(ref float source);

        /// <summary>Stores the lanes as four floats, each rounded as a conversion of a double to a float is.</summary>
        static abstract void Store(TSelf value, ref float target);

        static abstract double GetAlpha(TSelf value);

        /// <summary>Brings each lane between 0 and 1: <c>v &gt; 0 ? (v &lt; 1 ? v : 1) : 0</c>, so a value that is not a number is zero.</summary>
        static abstract TSelf Clamp(TSelf value);

        /// <summary>Rounds each lane as <see cref="Write"/> does: <c>floor(v + 1/2 + 2^-20)</c>, then <c>max</c> from <c>max</c> up, 0 when it is not positive.</summary>
        static abstract TSelf RoundSamples(TSelf value, double max);

        /// <summary>Converts lanes that are whole numbers from 0 to 65535 to the bits of an <see cref="Rgba64"/>.</summary>
        static abstract ulong ToSamples(TSelf value);

        static abstract TSelf operator +(TSelf left, TSelf right);

        static abstract TSelf operator -(TSelf left, TSelf right);

        static abstract TSelf operator *(TSelf left, TSelf right);

        static abstract TSelf operator /(TSelf left, TSelf right);
    }

    /// <summary>The four channels of a pixel in one vector of 256 bits, with AVX.</summary>
    internal readonly struct Quad256 : IQuad<Quad256>
    {
        private readonly Vector256<double> _value;

        private Quad256(Vector256<double> value) => _value = value;

        public static Quad256 Zero => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 Create(double value) => new(Vector256.Create(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 Create(double red, double green, double blue, double alpha) => new(Vector256.Create(red, green, blue, alpha));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 FromSamples(ulong color) => new(Avx.ConvertToVector256Double(Sse41.ConvertToVector128Int32(Vector128.CreateScalar(color).AsUInt16())));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 Load(ref float source) => new(Avx.ConvertToVector256Double(unsafe(Vector128.LoadUnsafe(ref source))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Quad256 value, ref float target)
        {
            unsafe { Avx.ConvertToVector128Single(value._value).StoreUnsafe(ref target); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetAlpha(Quad256 value) => value._value.GetElement(3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 Clamp(Quad256 value)
        {
            var one = Vector256.Create(1.0);
            return new(Vector256.ConditionalSelect(Vector256.GreaterThan(value._value, Vector256<double>.Zero), Vector256.ConditionalSelect(Vector256.LessThan(value._value, one), value._value, one), Vector256<double>.Zero));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 RoundSamples(Quad256 value, double max)
        {
            var rounded = Vector256.Floor(value._value + Vector256.Create(0.5) + Vector256.Create(1.0 / 1048576));
            var limit = Vector256.Create(max);
            return new(Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(rounded, limit), limit, Vector256.ConditionalSelect(Vector256.GreaterThan(rounded, Vector256<double>.Zero), rounded, Vector256<double>.Zero)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ToSamples(Quad256 value)
        {
            var integers = Avx.ConvertToVector128Int32WithTruncation(value._value);
            return Sse41.PackUnsignedSaturate(integers, integers).AsUInt64().ToScalar();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 operator +(Quad256 left, Quad256 right) => new(left._value + right._value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 operator -(Quad256 left, Quad256 right) => new(left._value - right._value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 operator *(Quad256 left, Quad256 right) => new(left._value * right._value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad256 operator /(Quad256 left, Quad256 right) => new(left._value / right._value);
    }

    /// <summary>The four channels of a pixel in two vectors of 128 bits.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Quad128 : IQuad<Quad128>
    {
        private readonly Vector128<double> _low;
        private readonly Vector128<double> _high;

        private Quad128(Vector128<double> low, Vector128<double> high)
        {
            _low = low;
            _high = high;
        }

        public static Quad128 Zero => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 Create(double value)
        {
            var broadcast = Vector128.Create(value);
            return new(broadcast, broadcast);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 Create(double red, double green, double blue, double alpha) => new(Vector128.Create(red, green), Vector128.Create(blue, alpha));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 FromSamples(ulong color)
        {
            var samples = Vector128.WidenLower(Vector128.CreateScalar(color).AsUInt16());
            return new(ImageEvaluator.Lanes128.ToDouble(Vector128.WidenLower(samples)), ImageEvaluator.Lanes128.ToDouble(Vector128.WidenUpper(samples)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 Load(ref float source)
        {
            var value = unsafe(Vector128.LoadUnsafe(ref source));
            return new(Vector128.WidenLower(value), Vector128.WidenUpper(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Quad128 value, ref float target)
        {
            unsafe { Vector128.Narrow(value._low, value._high).StoreUnsafe(ref target); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetAlpha(Quad128 value) => value._high.GetElement(1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 Clamp(Quad128 value) => new(Clamp(value._low), Clamp(value._high));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 RoundSamples(Quad128 value, double max) => new(RoundSamples(value._low, max), RoundSamples(value._high, max));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ToSamples(Quad128 value)
        {
            var integers = Vector128.Narrow(Vector128.ConvertToUInt64(value._low), Vector128.ConvertToUInt64(value._high));
            return Vector128.Narrow(integers, integers).AsUInt64().ToScalar();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 operator +(Quad128 left, Quad128 right) => new(left._low + right._low, left._high + right._high);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 operator -(Quad128 left, Quad128 right) => new(left._low - right._low, left._high - right._high);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 operator *(Quad128 left, Quad128 right) => new(left._low * right._low, left._high * right._high);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quad128 operator /(Quad128 left, Quad128 right) => new(left._low / right._low, left._high / right._high);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<double> Clamp(Vector128<double> value)
        {
            var one = Vector128.Create(1.0);
            return Vector128.ConditionalSelect(Vector128.GreaterThan(value, Vector128<double>.Zero), Vector128.ConditionalSelect(Vector128.LessThan(value, one), value, one), Vector128<double>.Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<double> RoundSamples(Vector128<double> value, double max)
        {
            var rounded = Vector128.Floor(value + Vector128.Create(0.5) + Vector128.Create(1.0 / 1048576));
            var limit = Vector128.Create(max);
            return Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(rounded, limit), limit, Vector128.ConditionalSelect(Vector128.GreaterThan(rounded, Vector128<double>.Zero), rounded, Vector128<double>.Zero));
        }
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
