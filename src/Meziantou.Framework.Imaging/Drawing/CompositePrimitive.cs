using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that combines two inputs with an operator of Porter and Duff, their sum, or an arithmetic of their components: <c>feComposite</c> of SVG.</summary>
/// <remarks>The operators are those of <see cref="FilterCompositeOperator"/>, on colors multiplied by their alpha. The first input is <c>A</c> (<c>in</c>), the second one <c>B</c> (<c>in2</c>).</remarks>
public sealed class CompositePrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="CompositePrimitive"/> class.</summary>
    /// <param name="operator">The operator.</param>
    /// <param name="input2">The second input: <c>in2</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operator"/> is not a defined <see cref="FilterCompositeOperator"/>.</exception>
    public CompositePrimitive(FilterCompositeOperator @operator, FilterInput input2)
    {
        if (!Enum.IsDefined(@operator))
            throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "The operator is not valid.");

        Operator = @operator;
        Input2 = input2;
    }

    /// <summary>Gets the operator.</summary>
    public FilterCompositeOperator Operator { get; }

    /// <summary>Gets the first input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    /// <summary>Gets the second input: <c>in2</c>.</summary>
    public FilterInput Input2 { get; }

    /// <summary>Gets the factor of the product of the inputs, for <see cref="FilterCompositeOperator.Arithmetic"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K1 { get; init => field = Validate(value); }

    /// <summary>Gets the factor of the first input, for <see cref="FilterCompositeOperator.Arithmetic"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K2 { get; init => field = Validate(value); }

    /// <summary>Gets the factor of the second input, for <see cref="FilterCompositeOperator.Arithmetic"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K3 { get; init => field = Validate(value); }

    /// <summary>Gets the constant, for <see cref="FilterCompositeOperator.Arithmetic"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K4 { get; init => field = Validate(value); }

    internal override int InputCount => 2;

    internal override FilterInput GetInput(int index) => index switch
    {
        0 => Input,
        1 => Input2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var a = context.GetInput(0).Values;
        var b = context.GetInput(1).Values;
        var values = result.Values;
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            var offset = result.IndexOf(subregion.Left, y);
            for (var i = offset; i < offset + (4 * subregion.Width); i += 4)
            {
                if (Operator == FilterCompositeOperator.Arithmetic)
                {
                    var alpha = Clamp((K1 * a[i + 3] * b[i + 3]) + (K2 * a[i + 3]) + (K3 * b[i + 3]) + K4);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var color = Clamp((K1 * a[i + channel] * b[i + channel]) + (K2 * a[i + channel]) + (K3 * b[i + channel]) + K4);
                        values[i + channel] = (float)Math.Min(color, alpha);
                    }

                    values[i + 3] = (float)alpha;
                    continue;
                }

                double aa = a[i + 3];
                double ba = b[i + 3];
                var (fa, fb) = Operator switch
                {
                    FilterCompositeOperator.Over => (1, 1 - aa),
                    FilterCompositeOperator.In => (ba, 0),
                    FilterCompositeOperator.Out => (1 - ba, 0),
                    FilterCompositeOperator.Atop => (ba, 1 - aa),
                    FilterCompositeOperator.Xor => (1 - ba, 1 - aa),
                    _ => (1d, 1d),
                };

                for (var channel = 0; channel < 4; channel++)
                {
                    var value = (fa * a[i + channel]) + (fb * b[i + channel]);
                    values[i + channel] = (float)(Operator == FilterCompositeOperator.Lighter ? Math.Min(value, 1) : value);
                }
            }
        }

        // Written so that a value that is not a number is zero
        static double Clamp(double value) => value > 0 ? (value < 1 ? value : 1) : 0;
    }

    private static double Validate(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be a finite number.", nameof(value));

        return value;
    }
}
