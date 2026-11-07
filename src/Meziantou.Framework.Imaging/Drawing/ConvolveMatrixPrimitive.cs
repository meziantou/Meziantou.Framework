using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that computes each pixel from the pixels around it with a matrix of weights: <c>feConvolveMatrix</c> of SVG.</summary>
/// <remarks>
/// <para>
/// With a matrix of <c>orderX</c> columns and <c>orderY</c> rows and a target <c>(tx, ty)</c>, a pixel of the result is
/// <c>sum(I(x - tx + j, y - ty + i) K(orderX - j - 1, orderY - i - 1)) / divisor + bias</c>: the matrix is turned by a
/// half turn, as in SVG. The weights are one pixel of the canvas apart, whatever the transformation. What is read
/// beyond the subregion of the primitive is given by <see cref="EdgeMode"/> (the nearest pixel by default, as in SVG);
/// inside it, an input is transparent outside its own subregion, so that a bias colors the pixels around a smaller
/// input, as browsers do.
/// </para>
/// <para>
/// The bias is added to every component. Without <see cref="PreserveAlpha"/>, the four components, the colors multiplied
/// by their alpha, are computed, the alpha is clamped to the range from 0 to 1 and each color to the range from 0 to the
/// alpha. With it, the straight colors are computed and clamped to the range from 0 to 1, and the pixel keeps its alpha.
/// </para>
/// </remarks>
public sealed class ConvolveMatrixPrimitive : FilterPrimitive
{
    private readonly double[] _kernel;

    /// <summary>Initializes a new instance of the <see cref="ConvolveMatrixPrimitive"/> class.</summary>
    /// <param name="orderX">The number of columns of the matrix: <c>order</c>. Must be between 1 and 255.</param>
    /// <param name="orderY">The number of rows of the matrix. Must be between 1 and 255.</param>
    /// <param name="kernel">The weights, row by row: <c>kernelMatrix</c>. They are copied.</param>
    /// <exception cref="ArgumentOutOfRangeException">An order is not between 1 and 255.</exception>
    /// <exception cref="ArgumentException">The number of weights is not <c>orderX orderY</c>, or a weight is not a finite number.</exception>
    public ConvolveMatrixPrimitive(int orderX, int orderY, ReadOnlySpan<double> kernel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(orderX, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(orderX, 255);
        ArgumentOutOfRangeException.ThrowIfLessThan(orderY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(orderY, 255);
        if (kernel.Length != orderX * orderY)
            throw new ArgumentException("The matrix must have orderX times orderY weights.", nameof(kernel));

        foreach (var weight in kernel)
        {
            if (!double.IsFinite(weight))
                throw new ArgumentException("A weight is not a finite number.", nameof(kernel));
        }

        OrderX = orderX;
        OrderY = orderY;
        _kernel = kernel.ToArray();
        TargetX = orderX / 2;
        TargetY = orderY / 2;
        EdgeMode = FilterEdgeMode.Duplicate;
    }

    /// <summary>Gets the number of columns of the matrix.</summary>
    public int OrderX { get; }

    /// <summary>Gets the number of rows of the matrix.</summary>
    public int OrderY { get; }

    /// <summary>Gets the weights, row by row.</summary>
    public IReadOnlyList<double> Kernel => _kernel;

    /// <summary>Gets what the sum is divided by: <c>divisor</c>. Defaults to <see langword="null"/>: the sum of the weights, or 1 when it is zero.</summary>
    /// <exception cref="ArgumentException">The value is zero or not a finite number.</exception>
    public double? Divisor
    {
        get;
        init
        {
            if (value is { } divisor && (!double.IsFinite(divisor) || divisor == 0))
                throw new ArgumentException("The divisor must be a finite number other than zero.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets what is added to every component of the result: <c>bias</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double Bias
    {
        get;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The bias must be a finite number.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets the column of the matrix that is on the pixel: <c>targetX</c>. Defaults to <c>floor(orderX / 2)</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a column of the matrix.</exception>
    public int TargetX
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, OrderX);
            field = value;
        }
    }

    /// <summary>Gets the row of the matrix that is on the pixel: <c>targetY</c>. Defaults to <c>floor(orderY / 2)</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a row of the matrix.</exception>
    public int TargetY
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, OrderY);
            field = value;
        }
    }

    /// <summary>Gets what is read beyond the subregion of the primitive: <c>edgeMode</c>. Defaults to <see cref="FilterEdgeMode.Duplicate"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FilterEdgeMode"/>.</exception>
    public FilterEdgeMode EdgeMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The edge mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether the colors are computed without their alpha, which each pixel keeps: <c>preserveAlpha</c>.</summary>
    public bool PreserveAlpha { get; init; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (OrderX, OrderY);

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var input = context.GetInput(0);
        var source = input.Values;
        var bounds = input.Bounds;
        var edges = context.Subregion;
        var mode = context.Processor.EdgeOverride ?? EdgeMode;
        var divisor = Divisor ?? GetSum(_kernel);
        var values = result.Values;
        Span<double> sum = stackalloc double[4];
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 15) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = subregion.Left; x < subregion.Right; x++)
            {
                sum.Clear();
                for (var i = 0; i < OrderY; i++)
                {
                    var py = y - TargetY + i;
                    if (!FilterProcessor.TryMap(ref py, edges.Top, edges.Bottom, mode) || py < bounds.Top || py >= bounds.Bottom)
                        continue;

                    for (var j = 0; j < OrderX; j++)
                    {
                        var px = x - TargetX + j;
                        if (!FilterProcessor.TryMap(ref px, edges.Left, edges.Right, mode) || px < bounds.Left || px >= bounds.Right)
                            continue;

                        var weight = _kernel[((OrderY - i - 1) * OrderX) + (OrderX - j - 1)];
                        var offset = input.IndexOf(px, py);
                        if (PreserveAlpha)
                        {
                            double alpha = source[offset + 3];
                            if (alpha > 0)
                            {
                                sum[0] += weight * (source[offset] / alpha);
                                sum[1] += weight * (source[offset + 1] / alpha);
                                sum[2] += weight * (source[offset + 2] / alpha);
                            }
                        }
                        else
                        {
                            sum[0] += weight * source[offset];
                            sum[1] += weight * source[offset + 1];
                            sum[2] += weight * source[offset + 2];
                            sum[3] += weight * source[offset + 3];
                        }
                    }
                }

                var target = result.IndexOf(x, y);
                if (PreserveAlpha)
                {
                    double alpha = source[input.IndexOf(x, y) + 3];
                    for (var channel = 0; channel < 3; channel++)
                    {
                        values[target + channel] = (float)(Clamp((sum[channel] / divisor) + Bias, 1) * alpha);
                    }

                    values[target + 3] = (float)alpha;
                }
                else
                {
                    var alpha = Clamp((sum[3] / divisor) + Bias, 1);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        values[target + channel] = (float)Clamp((sum[channel] / divisor) + Bias, alpha);
                    }

                    values[target + 3] = (float)alpha;
                }
            }
        }

        // Written so that a value that is not a number is zero
        static double Clamp(double value, double max) => value > 0 ? (value < max ? value : max) : 0;

        static double GetSum(double[] kernel)
        {
            double sum = 0;
            foreach (var weight in kernel)
            {
                sum += weight;
            }

            return sum == 0 ? 1 : sum;
        }
    }
}
