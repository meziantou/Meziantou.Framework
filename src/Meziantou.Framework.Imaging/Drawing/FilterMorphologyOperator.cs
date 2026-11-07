namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>What a <see cref="MorphologyPrimitive"/> keeps of the pixels around each pixel: the <c>operator</c> of <c>feMorphology</c> of SVG.</summary>
public enum FilterMorphologyOperator
{
    /// <summary>The smallest value of each component: shapes get thinner.</summary>
    Erode = 0,

    /// <summary>The largest value of each component: shapes get thicker.</summary>
    Dilate = 1,
}
