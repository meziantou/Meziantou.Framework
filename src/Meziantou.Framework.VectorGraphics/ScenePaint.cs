namespace Meziantou.Framework.VectorGraphics;

/// <summary>How a shape is painted.</summary>
public abstract class ScenePaint
{
    private protected ScenePaint()
    {
    }

    /// <summary>Converts a color to a <see cref="SolidPaint"/>.</summary>
    /// <param name="color">The color.</param>
    public static implicit operator ScenePaint(SceneColor color) => new SolidPaint(color);
}
