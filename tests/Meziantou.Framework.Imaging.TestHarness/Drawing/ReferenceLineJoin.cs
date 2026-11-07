namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>The shape of the corners of a reference stroke.</summary>
public enum ReferenceLineJoin
{
    /// <summary>The outer edges are extended until they meet, within the miter limit.</summary>
    Miter,

    /// <summary>The corner is rounded.</summary>
    Round,

    /// <summary>The corner is cut.</summary>
    Bevel,
}
