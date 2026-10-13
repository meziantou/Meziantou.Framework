namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Where a stroke is, relative to the path it follows.</summary>
public enum PenAlignment
{
    /// <summary>The stroke is centered on the path: half its width on each side. This is the default.</summary>
    Center = 0,

    /// <summary>The stroke is inside what the path fills: an outline that does not make a shape larger.</summary>
    Inside = 1,

    /// <summary>The stroke is outside what the path fills: an outline that does not cover the shape, or a text.</summary>
    Outside = 2,
}
