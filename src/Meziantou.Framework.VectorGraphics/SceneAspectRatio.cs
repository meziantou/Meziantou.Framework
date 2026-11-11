namespace Meziantou.Framework.VectorGraphics;

/// <summary>How a view box keeps its proportions in its viewport (<see cref="SceneViewBox.Alignment"/>), as the SVG <c>preserveAspectRatio</c>.</summary>
public enum SceneAspectRatio
{
    /// <summary>The box is stretched to the viewport, as <c>none</c>.</summary>
    None,

    /// <summary>The box keeps its proportions, aligned on the left and the top of the viewport.</summary>
    XMinYMin,

    /// <summary>The box keeps its proportions, centered horizontally, aligned on the top of the viewport.</summary>
    XMidYMin,

    /// <summary>The box keeps its proportions, aligned on the right and the top of the viewport.</summary>
    XMaxYMin,

    /// <summary>The box keeps its proportions, aligned on the left, centered vertically.</summary>
    XMinYMid,

    /// <summary>The box keeps its proportions, centered in the viewport. The default, as in SVG.</summary>
    XMidYMid,

    /// <summary>The box keeps its proportions, aligned on the right, centered vertically.</summary>
    XMaxYMid,

    /// <summary>The box keeps its proportions, aligned on the left and the bottom of the viewport.</summary>
    XMinYMax,

    /// <summary>The box keeps its proportions, centered horizontally, aligned on the bottom of the viewport.</summary>
    XMidYMax,

    /// <summary>The box keeps its proportions, aligned on the right and the bottom of the viewport.</summary>
    XMaxYMax,
}
