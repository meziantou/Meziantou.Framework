namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The baseline of a text that is on its y coordinate, as the SVG <c>dominant-baseline</c> property
/// (<see cref="SceneText.Baseline"/>). The renderers compute it from the metrics of the font of each span: its ascent,
/// its descent and its x-height (half its ascent when the font does not give it).
/// </summary>
public enum SceneTextBaseline
{
    /// <summary>The baseline the letters stand on, as SVG <c>alphabetic</c>: the default.</summary>
    Alphabetic,

    /// <summary>Half the x-height above the alphabetic baseline, as SVG <c>middle</c>: the middle of the lowercase letters.</summary>
    Middle,

    /// <summary>Halfway between the ascent and the descent, as SVG <c>central</c>: the middle of the em box.</summary>
    Central,

    /// <summary>Eight tenths of the ascent above the alphabetic baseline, as SVG <c>hanging</c> in browsers: the line the letters of Devanagari hang from.</summary>
    Hanging,

    /// <summary>Half the ascent above the alphabetic baseline, as SVG <c>mathematical</c> in browsers.</summary>
    Mathematical,

    /// <summary>The descent below the alphabetic baseline, as SVG <c>ideographic</c>: the bottom of the ideographs.</summary>
    Ideographic,

    /// <summary>The ascent above the alphabetic baseline, as SVG <c>text-top</c>: the text hangs below its y coordinate.</summary>
    TextTop,

    /// <summary>The descent below the alphabetic baseline, as SVG <c>text-bottom</c>: the text stands on its y coordinate, descenders included.</summary>
    TextBottom,
}
