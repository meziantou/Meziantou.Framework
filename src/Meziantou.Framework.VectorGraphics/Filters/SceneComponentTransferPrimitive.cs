namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that maps each component of the colors of its input by a function of its own: <c>feComponentTransfer</c> of SVG.</summary>
public sealed class SceneComponentTransferPrimitive : SceneFilterPrimitive
{
    /// <summary>Gets the function of the red: <c>feFuncR</c>. The default keeps it.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SceneTransferFunction Red { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = SceneTransferFunction.Identity;

    /// <summary>Gets the function of the green: <c>feFuncG</c>. The default keeps it.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SceneTransferFunction Green { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = SceneTransferFunction.Identity;

    /// <summary>Gets the function of the blue: <c>feFuncB</c>. The default keeps it.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SceneTransferFunction Blue { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = SceneTransferFunction.Identity;

    /// <summary>Gets the function of the alpha: <c>feFuncA</c>. The default keeps it.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SceneTransferFunction Alpha { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = SceneTransferFunction.Identity;

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];
}
