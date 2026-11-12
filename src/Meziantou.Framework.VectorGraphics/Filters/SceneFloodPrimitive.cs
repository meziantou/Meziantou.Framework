namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that fills its subregion with a color: <c>feFlood</c> of SVG.</summary>
/// <param name="color">The color, with its opacity: <c>flood-color</c> and <c>flood-opacity</c>.</param>
public sealed class SceneFloodPrimitive(SceneColor color) : SceneFilterPrimitive
{
    /// <summary>Gets the color.</summary>
    public SceneColor Color { get; } = color;

    internal override IEnumerable<SceneFilterInput> GetInputs() => [];
}
