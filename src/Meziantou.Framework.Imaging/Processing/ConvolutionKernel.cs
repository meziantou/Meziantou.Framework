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
/// </remarks>
public sealed class ConvolutionKernel
{
    private readonly double[] _values;

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

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The kernel weights must be finite.", nameof(values));
        }

        Width = width;
        Height = height;
        _values = values.ToArray();
    }

    /// <summary>Gets the number of columns.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }

    /// <summary>Gets the weights, row by row.</summary>
    internal ReadOnlySpan<double> Values => _values;

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

            return _values[(y * Width) + x];
        }
    }
}
