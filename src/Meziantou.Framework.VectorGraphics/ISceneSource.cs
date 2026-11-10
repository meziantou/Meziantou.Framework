namespace Meziantou.Framework.VectorGraphics;

/// <summary>A drawing that lays itself out into a <see cref="Scene"/>, such as a chart or a diagram.</summary>
public interface ISceneSource
{
    /// <summary>Lays the drawing out into drawing primitives.</summary>
    /// <param name="textMeasurer">The measurer of the texts, or <see langword="null"/> for <see cref="AfmTextMeasurer.Default"/>. Pass the measurer of the fonts that draw the scene, so the texts fit.</param>
    /// <returns>The scene.</returns>
    Scene CreateScene(ITextMeasurer? textMeasurer = null);
}
