namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>The colors of a reference gradient outside the range of its stops.</summary>
public enum ReferenceSpreadMode
{
    /// <summary>The first and last colors are extended.</summary>
    Pad,

    /// <summary>The gradient starts over.</summary>
    Repeat,

    /// <summary>The gradient is mirrored, then starts over.</summary>
    Reflect,
}
