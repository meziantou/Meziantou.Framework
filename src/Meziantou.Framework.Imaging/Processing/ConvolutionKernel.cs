namespace Meziantou.Framework.Imaging;

/// <summary>An immutable convolution matrix for <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// <para>
/// The matrix is applied as written, without being flipped: the weight at column <c>x</c> and row <c>y</c> multiplies the
/// pixel <c>x - Width / 2</c> columns to the right of and <c>y - Height / 2</c> rows below the pixel being computed, so the
/// top-left weight applies to the top-left neighbor. Both dimensions are odd, which makes the middle weight the one of
/// the pixel itself.
/// </para>
/// <para>
/// The weights are used as given: they are not normalized. A matrix whose weights add up to 1 keeps the overall
/// brightness; divide the weights by their sum to obtain one.
/// </para>
/// <para>
/// A separable matrix, whose every weight is the product of a weight of a column and a weight of a row
/// (<see cref="CreateSeparable"/>, or a matrix the constructor recognizes as one, see <see cref="IsSeparable"/>), is
/// applied in two passes: every row is filtered by the row weights, then the filtered rows are combined by the column
/// weights, in double precision without rounding in between. The time then grows with the sum of the dimensions of the
/// matrix instead of their product.
/// </para>
/// </remarks>
public sealed class ConvolutionKernel
{
    private readonly double[]? _values;
    private readonly double[]? _rowWeights;
    private readonly double[]? _columnWeights;

    /// <summary>Initializes a new instance of the <see cref="ConvolutionKernel"/> class.</summary>
    /// <param name="width">The number of columns. Must be positive and odd.</param>
    /// <param name="height">The number of rows. Must be positive and odd.</param>
    /// <param name="values">The weights, row by row from the top-left one (<paramref name="width"/> times <paramref name="height"/> finite values). They are copied.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive or is even.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not hold exactly <paramref name="width"/> times <paramref name="height"/> values, or a value is not finite.</exception>
    public ConvolutionKernel(int width, int height, ReadOnlySpan<double> values)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "The kernel width must be odd.");

        if (height % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "The kernel height must be odd.");

        if ((long)width * height != values.Length)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A {width}x{height} kernel has {(long)width * height} weights, but {values.Length} were provided."), nameof(values));

        ValidateWeights(values, nameof(values));
        Width = width;
        Height = height;
        _values = values.ToArray();
        (_rowWeights, _columnWeights) = Factor(width, height, _values);
    }

    private ConvolutionKernel(double[] rowWeights, double[] columnWeights)
    {
        Width = rowWeights.Length;
        Height = columnWeights.Length;
        _rowWeights = rowWeights;
        _columnWeights = columnWeights;
    }

    /// <summary>
    /// Gets the horizontal Sobel operator, the derivative <c>[-1, 0, 1]</c> of the rows smoothed by <c>[1, 2, 1]</c> across
    /// them: <c>[-1, 0, 1; -2, 0, 2; -1, 0, 1]</c>. It responds to vertical edges, positively where the right side is
    /// brighter, as <c>cv2.Sobel</c> with <c>dx = 1</c> and <c>dy = 0</c>.
    /// </summary>
    public static ConvolutionKernel SobelX { get; } = new(3, 3, [-1, 0, 1, -2, 0, 2, -1, 0, 1]);

    /// <summary>
    /// Gets the vertical Sobel operator, <see cref="SobelX"/> transposed: <c>[-1, -2, -1; 0, 0, 0; 1, 2, 1]</c>. It
    /// responds to horizontal edges, positively where the bottom side is brighter.
    /// </summary>
    public static ConvolutionKernel SobelY { get; } = new(3, 3, [-1, -2, -1, 0, 0, 0, 1, 2, 1]);

    /// <summary>
    /// Gets the horizontal Scharr operator, a Sobel operator whose smoothing <c>[3, 10, 3]</c> makes the response closer to
    /// the same for every direction of the edge: <c>[-3, 0, 3; -10, 0, 10; -3, 0, 3]</c>, as <c>cv2.Scharr</c> with
    /// <c>dx = 1</c> and <c>dy = 0</c>.
    /// </summary>
    public static ConvolutionKernel ScharrX { get; } = new(3, 3, [-3, 0, 3, -10, 0, 10, -3, 0, 3]);

    /// <summary>Gets the vertical Scharr operator, <see cref="ScharrX"/> transposed: <c>[-3, -10, -3; 0, 0, 0; 3, 10, 3]</c>.</summary>
    public static ConvolutionKernel ScharrY { get; } = new(3, 3, [-3, -10, -3, 0, 0, 0, 3, 10, 3]);

    /// <summary>Gets the horizontal Prewitt operator, the derivative <c>[-1, 0, 1]</c> of the rows averaged across them: <c>[-1, 0, 1; -1, 0, 1; -1, 0, 1]</c>.</summary>
    public static ConvolutionKernel PrewittX { get; } = new(3, 3, [-1, 0, 1, -1, 0, 1, -1, 0, 1]);

    /// <summary>Gets the vertical Prewitt operator, <see cref="PrewittX"/> transposed: <c>[-1, -1, -1; 0, 0, 0; 1, 1, 1]</c>.</summary>
    public static ConvolutionKernel PrewittY { get; } = new(3, 3, [-1, -1, -1, 0, 0, 0, 1, 1, 1]);

    /// <summary>
    /// Gets the Laplacian of the four nearest neighbors, the sum of the second derivatives <c>[1, -2, 1]</c> of both
    /// directions: <c>[0, 1, 0; 1, -4, 1; 0, 1, 0]</c>, as <c>cv2.Laplacian</c> with <c>ksize = 1</c>. Its weights add up to
    /// zero, and it is negative on the bright side of an edge.
    /// </summary>
    public static ConvolutionKernel Laplacian3x3 { get; } = new(3, 3, [0, 1, 0, 1, -4, 1, 0, 1, 0]);

    /// <summary>
    /// Gets the 5x5 Laplacian, the sum of the second derivatives <c>[1, 0, -2, 0, 1]</c> of each direction smoothed by
    /// <c>[1, 4, 6, 4, 1]</c> across it (the second derivatives of the 5x5 Sobel operator), as <c>cv2.Laplacian</c> with
    /// <c>ksize = 5</c>: <c>[2, 4, 4, 4, 2; 4, 0, -8, 0, 4; 4, -8, -24, -8, 4; 4, 0, -8, 0, 4; 2, 4, 4, 4, 2]</c>. Its weights
    /// add up to zero.
    /// </summary>
    public static ConvolutionKernel Laplacian5x5 { get; } = new(5, 5, [2, 4, 4, 4, 2, 4, 0, -8, 0, 4, 4, -8, -24, -8, 4, 4, 0, -8, 0, 4, 2, 4, 4, 4, 2]);

    /// <summary>
    /// Gets the 5x5 Laplacian of Gaussian, the Laplacian of a slightly blurred image, which responds less to noise:
    /// <c>[0, 0, 1, 0, 0; 0, 1, 2, 1, 0; 1, 2, -16, 2, 1; 0, 1, 2, 1, 0; 0, 0, 1, 0, 0]</c>. Its weights add up to zero. It is
    /// the classic 5x5 approximation with the sign of the Laplacian (negative in the middle, like
    /// <see cref="Laplacian3x3"/>); the "Mexican hat" form of that approximation is its negation.
    /// </summary>
    public static ConvolutionKernel LaplacianOfGaussian { get; } = new(5, 5, [0, 0, 1, 0, 0, 0, 1, 2, 1, 0, 1, 2, -16, 2, 1, 0, 1, 2, 1, 0, 0, 0, 1, 0, 0]);

    /// <summary>
    /// Gets the emboss matrix <c>[-2, -1, 0; -1, 1, 1; 0, 1, 2]</c>, which lights the image from the bottom right. Its
    /// weights add up to 1, so flat areas keep their color.
    /// </summary>
    public static ConvolutionKernel Emboss { get; } = new(3, 3, [-2, -1, 0, -1, 1, 1, 0, 1, 2]);

    /// <summary>
    /// Gets the sharpen matrix <c>[0, -1, 0; -1, 5, -1; 0, -1, 0]</c>: the pixel minus the Laplacian of its four nearest
    /// neighbors. Its weights add up to 1, so flat areas keep their color.
    /// </summary>
    public static ConvolutionKernel Sharpen3x3 { get; } = new(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);

    /// <summary>Gets the number of columns.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the matrix is applied in two passes, a row then a column. It is the case of a
    /// matrix created by <see cref="CreateSeparable"/>, and of a matrix given weight by weight whose every weight is
    /// exactly, in double precision, the product of a weight of its largest weight's column, divided by that weight, and a
    /// weight of its row (a box, a binomial or a Sobel matrix): finding the factors costs one pass over the weights.
    /// </summary>
    public bool IsSeparable => _rowWeights is not null;

    /// <summary>Gets the weights every row is filtered by, from left to right (separable matrices), or an empty span.</summary>
    internal ReadOnlySpan<double> RowWeights => _rowWeights;

    /// <summary>Gets the weights that combine the filtered rows, from top to bottom (separable matrices), or an empty span.</summary>
    internal ReadOnlySpan<double> ColumnWeights => _columnWeights;

    /// <summary>Gets a value indicating whether the matrix was given weight by weight (<see cref="GetValues"/> does not compute it).</summary>
    internal bool HasValues => _values is not null;

    /// <summary>Gets a weight.</summary>
    /// <param name="x">The column, from 0 (left) to <see cref="Width"/> - 1.</param>
    /// <param name="y">The row, from 0 (top) to <see cref="Height"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="x"/> or <paramref name="y"/> is out of range.</exception>
    public double this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(nameof(x), x, "The column is out of range.");

            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y), y, "The row is out of range.");

            return _values is not null ? _values[(y * Width) + x] : _columnWeights![y] * _rowWeights![x];
        }
    }

    /// <summary>
    /// Creates a separable matrix: the weight at column <c>x</c> and row <c>y</c> is
    /// <c>columnWeights[y] * rowWeights[x]</c>. It is applied in two passes (every row filtered by
    /// <paramref name="rowWeights"/>, then the filtered rows combined by <paramref name="columnWeights"/>), so the time grows
    /// with the sum of the two lengths instead of their product.
    /// </summary>
    /// <param name="rowWeights">The weights of a row, from left to right: an odd number of finite values. They are copied.</param>
    /// <param name="columnWeights">The weights of a column, from top to bottom: an odd number of finite values. They are copied.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentException">A span is empty or holds an even number of values, or a value is not finite.</exception>
    public static ConvolutionKernel CreateSeparable(ReadOnlySpan<double> rowWeights, ReadOnlySpan<double> columnWeights)
    {
        if (rowWeights.Length % 2 == 0)
            throw new ArgumentException("The row weights must be an odd number of values.", nameof(rowWeights));

        if (columnWeights.Length % 2 == 0)
            throw new ArgumentException("The column weights must be an odd number of values.", nameof(columnWeights));

        ValidateWeights(rowWeights, nameof(rowWeights));
        ValidateWeights(columnWeights, nameof(columnWeights));
        return new ConvolutionKernel(rowWeights.ToArray(), columnWeights.ToArray());
    }

    /// <summary>
    /// Creates the matrix of a motion blur: a straight segment of <paramref name="length"/> pixels through the center, at
    /// <paramref name="degrees"/> from the horizontal, as the trail of a camera or a subject that moved while the shutter
    /// was open. Apply it with <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The weight of a pixel is the length of the part of the segment inside its square, divided by the length of the
    /// segment, so the weights add up to 1 and the brightness is kept: a horizontal segment of 5 pixels is the row
    /// <c>[1, 1, 1, 1, 1] / 5</c>, and a segment of 3 pixels at 45 degrees gives <c>sqrt(2) / 3</c> to the center and
    /// <c>(3 - sqrt(2)) / 6</c> to each of the two diagonal neighbors. The segment is centered, so the blur does not move
    /// the image. The matrix is as small as the segment allows: its width and height are odd.
    /// </para>
    /// <para>
    /// As for <see cref="ImageProcessingExtensions.Rotate(Image, double, TransformOptions?, CancellationToken)"/>, a positive
    /// angle turns clockwise on the screen (the vertical axis points down); a segment and its opposite are the same, so
    /// 0 and 180 give the same matrix.
    /// </para>
    /// </remarks>
    /// <param name="length">The length of the segment, in pixels: at least 1 and at most 2000. A length of 1 keeps the image as it is.</param>
    /// <param name="degrees">The angle of the segment, in degrees.</param>
    /// <returns>The matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not between 1 and 2000.</exception>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number.</exception>
    public static ConvolutionKernel CreateMotionBlur(double length, double degrees)
    {
        if (!(length is >= 1 and <= 2000))
            throw new ArgumentOutOfRangeException(nameof(length), length, "The length must be between 1 and 2000.");

        if (!double.IsFinite(degrees))
            throw new ArgumentException("The angle must be a finite number.", nameof(degrees));

        // The direction, with the components that are 0 or 1 to 1e-12 made exact (multiples of 90 degrees)
        var radians = Math.IEEERemainder(degrees, 360) * Math.PI / 180;
        var dx = Snap(Math.Cos(radians));
        var dy = Snap(Math.Sin(radians));
        var half = length / 2;
        var radiusX = Math.Max(0, (int)Math.Ceiling(Snap(half * Math.Abs(dx)) - 0.5));
        var radiusY = Math.Max(0, (int)Math.Ceiling(Snap(half * Math.Abs(dy)) - 0.5));
        var width = (2 * radiusX) + 1;
        var height = (2 * radiusY) + 1;
        var values = new double[width * height];
        var sum = 0.0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = GetLengthInside(-half * dx, -half * dy, half * dx, half * dy, x - radiusX - 0.5, y - radiusY - 0.5);
                values[(y * width) + x] = inside;
                sum += inside;
            }
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] /= sum;
        }

        return new ConvolutionKernel(width, height, values);

        static double Snap(double value)
        {
            var rounded = Math.Round(value, MidpointRounding.ToEven);
            return Math.Abs(value - rounded) < 1e-12 ? rounded : value;
        }
    }

    /// <summary>Gets the length of the part of the segment from (x0, y0) to (x1, y1) inside the unit square whose top-left corner is (left, top), clipping it to the square (Liang and Barsky).</summary>
    private static double GetLengthInside(double x0, double y0, double x1, double y1, double left, double top)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var start = 0.0;
        var end = 1.0;
        if (!Clip(-dx, x0 - left, ref start, ref end) || !Clip(dx, left + 1 - x0, ref start, ref end) || !Clip(-dy, y0 - top, ref start, ref end) || !Clip(dy, top + 1 - y0, ref start, ref end))
            return 0;

        return (end - start) * Math.Sqrt((dx * dx) + (dy * dy));

        // Keeps the part of the segment where p t <= q
        static bool Clip(double p, double q, ref double start, ref double end)
        {
            if (p == 0)
                return q >= 0;

            var t = q / p;
            if (p < 0)
            {
                if (t > end)
                    return false;

                start = Math.Max(start, t);
            }
            else
            {
                if (t < start)
                    return false;

                end = Math.Min(end, t);
            }

            return true;
        }
    }

    /// <summary>Gets the weights, row by row (computed from the factors of a matrix created by <see cref="CreateSeparable"/>).</summary>
    internal double[] GetValues()
    {
        if (_values is not null)
            return _values;

        var values = new double[(long)Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                values[(y * Width) + x] = _columnWeights![y] * _rowWeights![x];
            }
        }

        return values;
    }

    private static void ValidateWeights(ReadOnlySpan<double> values, string paramName)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The kernel weights must be finite.", paramName);
        }
    }

    /// <summary>
    /// Finds the factors of a matrix of at least two rows and two columns whose every weight is exactly the product of a
    /// column weight and a row weight: the row of the largest weight (the first one of that magnitude), and its column
    /// divided by it. Returns <see langword="null"/> factors when a product differs from the weight.
    /// </summary>
    private static (double[]? Row, double[]? Column) Factor(int width, int height, double[] values)
    {
        if (width == 1 || height == 1)
            return default;

        var pivot = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (Math.Abs(values[i]) > Math.Abs(values[pivot]))
            {
                pivot = i;
            }
        }

        var pivotValue = values[pivot];
        if (pivotValue == 0)
            return default;

        var pivotX = pivot % width;
        var pivotY = pivot / width;
        var row = values.AsSpan(pivotY * width, width).ToArray();
        var column = new double[height];
        for (var y = 0; y < height; y++)
        {
            column[y] = values[(y * width) + pivotX] / pivotValue;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (column[y] * row[x] != values[(y * width) + x])
                    return default;
            }
        }

        return (row, column);
    }
}
