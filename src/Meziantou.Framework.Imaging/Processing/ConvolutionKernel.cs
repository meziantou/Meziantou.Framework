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
