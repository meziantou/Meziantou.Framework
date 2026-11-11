namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A function of a component of a <see cref="SceneComponentTransferPrimitive"/>: <c>feFuncR</c>, <c>feFuncG</c>,
/// <c>feFuncB</c> or <c>feFuncA</c> of SVG. It applies to the colors without their alpha, from 0 to 1, and its result is
/// clamped to that range.
/// </summary>
public sealed class SceneTransferFunction
{
    private readonly double[] _values;

    private SceneTransferFunction(SceneTransferFunctionKind kind, double[] values, double slope, double intercept, double amplitude, double exponent, double offset)
    {
        Kind = kind;
        _values = values;
        Slope = slope;
        Intercept = intercept;
        Amplitude = amplitude;
        Exponent = exponent;
        Offset = offset;
    }

    /// <summary>Gets the function that keeps the value.</summary>
    public static SceneTransferFunction Identity { get; } = new(SceneTransferFunctionKind.Identity, [], 0, 0, 0, 0, 0);

    /// <summary>Gets the kind of the function.</summary>
    public SceneTransferFunctionKind Kind { get; }

    /// <summary>Gets the values of the table of a <see cref="SceneTransferFunctionKind.Table"/> or <see cref="SceneTransferFunctionKind.Discrete"/> function: <c>tableValues</c>.</summary>
    public IReadOnlyList<double> Values => _values;

    /// <summary>Gets the slope of a <see cref="SceneTransferFunctionKind.Linear"/> function: <c>slope</c>.</summary>
    public double Slope { get; }

    /// <summary>Gets the intercept of a <see cref="SceneTransferFunctionKind.Linear"/> function: <c>intercept</c>.</summary>
    public double Intercept { get; }

    /// <summary>Gets the amplitude of a <see cref="SceneTransferFunctionKind.Gamma"/> function: <c>amplitude</c>.</summary>
    public double Amplitude { get; }

    /// <summary>Gets the exponent of a <see cref="SceneTransferFunctionKind.Gamma"/> function: <c>exponent</c>.</summary>
    public double Exponent { get; }

    /// <summary>Gets the offset of a <see cref="SceneTransferFunctionKind.Gamma"/> function: <c>offset</c>.</summary>
    public double Offset { get; }

    /// <summary>Creates a function that interpolates linearly between the values of a table, spread evenly from 0 to 1. An empty table keeps the value.</summary>
    /// <param name="values">The values. They are copied.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A value is not a finite number.</exception>
    public static SceneTransferFunction CreateTable(params ReadOnlySpan<double> values) => Create(SceneTransferFunctionKind.Table, values);

    /// <summary>Creates a function whose value is the value of the step it is in: <c>n</c> values divide the range from 0 to 1 in <c>n</c> steps. An empty table keeps the value.</summary>
    /// <param name="values">The values. They are copied.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A value is not a finite number.</exception>
    public static SceneTransferFunction CreateDiscrete(params ReadOnlySpan<double> values) => Create(SceneTransferFunctionKind.Discrete, values);

    /// <summary>Creates the function <c>slope C + intercept</c>.</summary>
    /// <param name="slope">The slope.</param>
    /// <param name="intercept">The intercept.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A parameter is not a finite number.</exception>
    public static SceneTransferFunction CreateLinear(double slope, double intercept)
    {
        return new(SceneTransferFunctionKind.Linear, [], FilterValidation.Finite(slope, nameof(slope)), FilterValidation.Finite(intercept, nameof(intercept)), 0, 0, 0);
    }

    /// <summary>Creates the function <c>amplitude C^exponent + offset</c>.</summary>
    /// <param name="amplitude">The amplitude.</param>
    /// <param name="exponent">The exponent.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The function.</returns>
    /// <exception cref="ArgumentException">A parameter is not a finite number.</exception>
    public static SceneTransferFunction CreateGamma(double amplitude, double exponent, double offset)
    {
        return new(SceneTransferFunctionKind.Gamma, [], 0, 0, FilterValidation.Finite(amplitude, nameof(amplitude)), FilterValidation.Finite(exponent, nameof(exponent)), FilterValidation.Finite(offset, nameof(offset)));
    }

    private static SceneTransferFunction Create(SceneTransferFunctionKind kind, ReadOnlySpan<double> values)
    {
        foreach (var value in values)
        {
            FilterValidation.Finite(value, nameof(values));
        }

        return new(kind, values.ToArray(), 0, 0, 0, 0, 0);
    }
}
