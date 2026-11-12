namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that computes each pixel from the pixels around it with a matrix of weights: <c>feConvolveMatrix</c> of SVG.</summary>
/// <remarks>
/// With a matrix of <c>orderX</c> columns and <c>orderY</c> rows and a target <c>(tx, ty)</c>, a pixel of the result is
/// <c>sum(I(x - tx + j, y - ty + i) K(orderX - j - 1, orderY - i - 1)) / divisor + bias</c>. The weights are one pixel of
/// the rendered image apart.
/// </remarks>
public sealed class SceneConvolveMatrixPrimitive : SceneFilterPrimitive
{
    private readonly double[] _kernel;

    /// <summary>Initializes a new instance of the <see cref="SceneConvolveMatrixPrimitive"/> class.</summary>
    /// <param name="orderX">The number of columns of the matrix: <c>order</c>. Must be between 1 and 255.</param>
    /// <param name="orderY">The number of rows of the matrix. Must be between 1 and 255.</param>
    /// <param name="kernel">The weights, row by row: <c>kernelMatrix</c>. They are copied.</param>
    /// <exception cref="ArgumentOutOfRangeException">An order is not between 1 and 255.</exception>
    /// <exception cref="ArgumentException">The number of weights is not <c>orderX orderY</c>, or a weight is not a finite number.</exception>
    public SceneConvolveMatrixPrimitive(int orderX, int orderY, params ReadOnlySpan<double> kernel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(orderX, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(orderX, 255);
        ArgumentOutOfRangeException.ThrowIfLessThan(orderY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(orderY, 255);
        if (kernel.Length != orderX * orderY)
            throw new ArgumentException($"A matrix of {orderX} by {orderY} has {orderX * orderY} weights, but {kernel.Length} were provided.", nameof(kernel));

        foreach (var weight in kernel)
        {
            FilterValidation.Finite(weight, nameof(kernel));
        }

        OrderX = orderX;
        OrderY = orderY;
        _kernel = kernel.ToArray();
    }

    /// <summary>Gets the number of columns of the matrix.</summary>
    public int OrderX { get; }

    /// <summary>Gets the number of rows of the matrix.</summary>
    public int OrderY { get; }

    /// <summary>Gets the weights, row by row.</summary>
    public IReadOnlyList<double> Kernel => _kernel;

    /// <summary>Gets what the sum is divided by: <c>divisor</c>. The default is <see langword="null"/>: the sum of the weights, or 1 when it is zero.</summary>
    /// <exception cref="ArgumentException">The value is zero or not a finite number.</exception>
    public double? Divisor
    {
        get;
        init
        {
            if (value is { } divisor && (divisor == 0 || !double.IsFinite(divisor)))
                throw new ArgumentException("The divisor must be a finite number other than zero.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets what is added to every component of the result: <c>bias</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double Bias { get; init => field = FilterValidation.Finite(value, nameof(value)); }

    /// <summary>Gets the column of the matrix that is on the pixel: <c>targetX</c>. The default is <see langword="null"/>: <c>floor(orderX / 2)</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a column of the matrix.</exception>
    public int? TargetX
    {
        get;
        init
        {
            if (value is < 0 || value >= OrderX)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The target must be a column of the matrix.");

            field = value;
        }
    }

    /// <summary>Gets the row of the matrix that is on the pixel: <c>targetY</c>. The default is <see langword="null"/>: <c>floor(orderY / 2)</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a row of the matrix.</exception>
    public int? TargetY
    {
        get;
        init
        {
            if (value is < 0 || value >= OrderY)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The target must be a row of the matrix.");

            field = value;
        }
    }

    /// <summary>Gets what is read beyond the edges of the input: <c>edgeMode</c>. The default is <see cref="SceneFilterEdgeMode.Duplicate"/>, as in SVG.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="SceneFilterEdgeMode"/>.</exception>
    public SceneFilterEdgeMode EdgeMode
    {
        get;
        init => field = FilterValidation.Defined(value, SceneFilterEdgeMode.Mirror, nameof(value));
    } = SceneFilterEdgeMode.Duplicate;

    /// <summary>Gets a value indicating whether the colors are computed without their alpha, which each pixel keeps: <c>preserveAlpha</c>.</summary>
    public bool PreserveAlpha { get; init; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => Math.Max(OrderX, OrderY);
}
