namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A function that gives a new value to a component of each pixel: a <c>feFuncR</c>, <c>feFuncG</c>, <c>feFuncB</c> or <c>feFuncA</c> element of SVG.</summary>
/// <remarks>
/// The function takes the value <c>C</c> of a straight color or of the alpha, as a fraction of one, and its result is
/// clamped to the range from 0 to 1. With <c>n</c> values <c>v0</c> to <c>v(n-1)</c>: a table gives
/// <c>vk + (C - k / (n - 1)) (n - 1) (v(k+1) - vk)</c> with <c>k = floor(C (n - 1))</c> (<c>v(n-1)</c> for <c>C = 1</c>);
/// a discrete table gives <c>vk</c> with <c>k = floor(C n)</c> (<c>v(n-1)</c> for <c>C = 1</c>); a linear function
/// gives <c>slope C + intercept</c>; a gamma function gives <c>amplitude C^exponent + offset</c>, with
/// <see cref="Math.Pow(double, double)"/>. A table without values is the identity. Transfer functions are immutable and
/// thread-safe.
/// </remarks>
public sealed class TransferFunction
{
    private readonly double[] _values;

    private TransferFunction(TransferFunctionKind kind, double[] values, double a, double b, double c)
    {
        Kind = kind;
        _values = values;
        switch (kind)
        {
            case TransferFunctionKind.Linear:
                Slope = a;
                Intercept = b;
                break;

            case TransferFunctionKind.Gamma:
                Amplitude = a;
                Exponent = b;
                Offset = c;
                break;
        }
    }

    /// <summary>Gets the function that changes nothing.</summary>
    public static TransferFunction Identity { get; } = new(TransferFunctionKind.Identity, [], 0, 0, 0);

    /// <summary>Gets the kind of the function.</summary>
    public TransferFunctionKind Kind { get; }

    /// <summary>Gets the values of a table: <c>tableValues</c>.</summary>
    public IReadOnlyList<double> Values => _values;

    /// <summary>Gets the slope of a linear function.</summary>
    public double Slope { get; }

    /// <summary>Gets the intercept of a linear function.</summary>
    public double Intercept { get; }

    /// <summary>Gets the amplitude of a gamma function.</summary>
    public double Amplitude { get; }

    /// <summary>Gets the exponent of a gamma function.</summary>
    public double Exponent { get; }

    /// <summary>Gets the offset of a gamma function.</summary>
    public double Offset { get; }

    /// <summary>Creates a function that interpolates in a table: <c>type="table"</c>.</summary>
    /// <param name="values">The values, spread evenly from 0 to 1. They are copied; without values, the function is the identity.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A value is not a finite number.</exception>
    public static TransferFunction CreateTable(params ReadOnlySpan<double> values) => Create(TransferFunctionKind.Table, values);

    /// <summary>Creates a function of steps: <c>type="discrete"</c>.</summary>
    /// <param name="values">The values of the steps, from 0 to 1. They are copied; without values, the function is the identity.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A value is not a finite number.</exception>
    public static TransferFunction CreateDiscrete(params ReadOnlySpan<double> values) => Create(TransferFunctionKind.Discrete, values);

    /// <summary>Creates a linear function: <c>type="linear"</c>.</summary>
    /// <param name="slope">The factor of the value.</param>
    /// <param name="intercept">The value added.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A parameter is not a finite number.</exception>
    public static TransferFunction CreateLinear(double slope, double intercept)
    {
        Validate(slope, nameof(slope));
        Validate(intercept, nameof(intercept));
        return new TransferFunction(TransferFunctionKind.Linear, [], slope, intercept, 0);
    }

    /// <summary>Creates a gamma function: <c>type="gamma"</c>.</summary>
    /// <param name="amplitude">The factor of the power.</param>
    /// <param name="exponent">The exponent.</param>
    /// <param name="offset">The value added.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A parameter is not a finite number.</exception>
    public static TransferFunction CreateGamma(double amplitude, double exponent, double offset)
    {
        Validate(amplitude, nameof(amplitude));
        Validate(exponent, nameof(exponent));
        Validate(offset, nameof(offset));
        return new TransferFunction(TransferFunctionKind.Gamma, [], amplitude, exponent, offset);
    }

    /// <summary>Gets the value of the function, clamped to the range from 0 to 1.</summary>
    internal double Evaluate(double value)
    {
        var values = _values;
        var result = Kind switch
        {
            TransferFunctionKind.Table when values.Length > 0 => Interpolate(values, value),
            TransferFunctionKind.Discrete when values.Length > 0 => values[Math.Min((int)Math.Floor(value * values.Length), values.Length - 1)],
            TransferFunctionKind.Linear => (Slope * value) + Intercept,
            TransferFunctionKind.Gamma => (Amplitude * Math.Pow(value, Exponent)) + Offset,
            _ => value,
        };

        // Written so that a value that is not a number is zero
        return result > 0 ? (result < 1 ? result : 1) : 0;

        static double Interpolate(double[] values, double value)
        {
            var n = values.Length - 1;
            if (n == 0 || value >= 1)
                return values[n];

            var k = (int)Math.Floor(value * n);
            return values[k] + ((value - ((double)k / n)) * n * (values[k + 1] - values[k]));
        }
    }

    private static TransferFunction Create(TransferFunctionKind kind, ReadOnlySpan<double> values)
    {
        foreach (var value in values)
        {
            Validate(value, nameof(values));
        }

        return new TransferFunction(kind, values.ToArray(), 0, 0, 0);
    }

    private static void Validate(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be a finite number.", paramName);
    }
}
