namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that repeats the subregion of its input over its own subregion: <c>feTile</c> of SVG.</summary>
public sealed class SceneTilePrimitive : SceneFilterPrimitive
{
    /// <summary>Gets the input, whose subregion is the tile: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];
}
