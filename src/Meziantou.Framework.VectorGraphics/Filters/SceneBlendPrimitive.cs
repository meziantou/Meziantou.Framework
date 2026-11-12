namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that draws its first input over its second one with a blend mode: <c>feBlend</c> of SVG.</summary>
public sealed class SceneBlendPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneBlendPrimitive"/> class.</summary>
    /// <param name="mode">The blend mode: <c>mode</c>.</param>
    /// <param name="input2">The input drawn under the first one: <c>in2</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="SceneBlendMode"/>.</exception>
    public SceneBlendPrimitive(SceneBlendMode mode, SceneFilterInput input2)
    {
        Mode = FilterValidation.Defined(mode, SceneBlendMode.Luminosity, nameof(mode));
        Input2 = input2;
    }

    /// <summary>Gets the blend mode.</summary>
    public SceneBlendMode Mode { get; }

    /// <summary>Gets the input drawn over the other one: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    /// <summary>Gets the input drawn under the other one: <c>in2</c>.</summary>
    public SceneFilterInput Input2 { get; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input, Input2];
}
