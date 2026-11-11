namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that combines two inputs with an operator of Porter and Duff, their sum, or an arithmetic of their components: <c>feComposite</c> of SVG.</summary>
/// <remarks>The operators combine the colors multiplied by their alpha. The first input is <c>A</c> (<c>in</c>), the second one <c>B</c> (<c>in2</c>).</remarks>
public sealed class SceneCompositePrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneCompositePrimitive"/> class.</summary>
    /// <param name="operator">The operator.</param>
    /// <param name="input2">The second input: <c>in2</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operator"/> is not a defined <see cref="SceneCompositeOperator"/>.</exception>
    public SceneCompositePrimitive(SceneCompositeOperator @operator, SceneFilterInput input2)
    {
        Operator = FilterValidation.Defined(@operator, SceneCompositeOperator.Arithmetic, nameof(@operator));
        Input2 = input2;
    }

    /// <summary>Gets the operator.</summary>
    public SceneCompositeOperator Operator { get; }

    /// <summary>Gets the first input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    /// <summary>Gets the second input: <c>in2</c>.</summary>
    public SceneFilterInput Input2 { get; }

    /// <summary>Gets the factor of the product of the inputs, for <see cref="SceneCompositeOperator.Arithmetic"/>: <c>k1</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K1 { get; init => field = FilterValidation.Finite(value, nameof(value)); }

    /// <summary>Gets the factor of the first input, for <see cref="SceneCompositeOperator.Arithmetic"/>: <c>k2</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K2 { get; init => field = FilterValidation.Finite(value, nameof(value)); }

    /// <summary>Gets the factor of the second input, for <see cref="SceneCompositeOperator.Arithmetic"/>: <c>k3</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K3 { get; init => field = FilterValidation.Finite(value, nameof(value)); }

    /// <summary>Gets the constant, for <see cref="SceneCompositeOperator.Arithmetic"/>: <c>k4</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double K4 { get; init => field = FilterValidation.Finite(value, nameof(value)); }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input, Input2];
}
