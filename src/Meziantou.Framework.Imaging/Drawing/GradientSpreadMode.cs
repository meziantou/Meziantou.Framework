namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The colors of a gradient outside the range of its stops.</summary>
public enum GradientSpreadMode
{
    /// <summary>The first and the last colors are extended. This is the default.</summary>
    Pad = 0,

    /// <summary>The gradient starts over.</summary>
    Repeat = 1,

    /// <summary>The gradient is mirrored, then starts over.</summary>
    Reflect = 2,

    /// <summary>
    /// Nothing is painted outside the range from 0 to 1: the gradient ends at its first and its last offsets, as the
    /// decal tile mode of graphics libraries.
    /// </summary>
    Transparent = 3,
}
