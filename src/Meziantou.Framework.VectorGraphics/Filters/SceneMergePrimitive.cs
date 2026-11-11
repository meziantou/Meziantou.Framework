namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that draws its inputs over each other, the first one at the bottom: <c>feMerge</c> of SVG.</summary>
public sealed class SceneMergePrimitive : SceneFilterPrimitive
{
    private readonly SceneFilterInput[] _inputs;

    /// <summary>Initializes a new instance of the <see cref="SceneMergePrimitive"/> class.</summary>
    /// <param name="inputs">The inputs, from the bottom to the top: the <c>in</c> of each <c>feMergeNode</c>. They are copied.</param>
    public SceneMergePrimitive(params ReadOnlySpan<SceneFilterInput> inputs)
    {
        _inputs = inputs.ToArray();
    }

    /// <summary>Gets the inputs, from the bottom to the top.</summary>
    public IReadOnlyList<SceneFilterInput> Inputs => _inputs;

    internal override IEnumerable<SceneFilterInput> GetInputs() => _inputs;
}
