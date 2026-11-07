namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A component of a pixel that a <see cref="DisplacementMapPrimitive"/> reads: <c>xChannelSelector</c> and <c>yChannelSelector</c> of SVG.</summary>
public enum FilterChannel
{
    /// <summary>The red.</summary>
    Red = 0,

    /// <summary>The green.</summary>
    Green = 1,

    /// <summary>The blue.</summary>
    Blue = 2,

    /// <summary>The alpha.</summary>
    Alpha = 3,
}
