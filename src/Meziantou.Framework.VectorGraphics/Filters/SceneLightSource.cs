namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The light of a <see cref="SceneDiffuseLightingPrimitive"/> or a <see cref="SceneSpecularLightingPrimitive"/>:
/// <see cref="SceneDistantLight"/>, <see cref="ScenePointLight"/> or <see cref="SceneSpotLight"/>.
/// </summary>
/// <remarks>The positions of the lights are in the coordinates of the parent of the element, as the region of the filter.</remarks>
public abstract class SceneLightSource
{
    private protected SceneLightSource()
    {
    }
}
