namespace Meziantou.Framework.VectorGraphics;

/// <summary>An effect applied to what an element draws (<see cref="SceneElement.Filter"/>): <see cref="SceneBlurFilter"/> or <see cref="SceneDropShadowFilter"/>.</summary>
/// <remarks>The SVG and image renderers draw the filters; PDF has no filters, so the PDF renderer throws <see cref="NotSupportedException"/> for a scene that has one.</remarks>
public abstract class SceneFilter
{
    private protected SceneFilter()
    {
    }
}
