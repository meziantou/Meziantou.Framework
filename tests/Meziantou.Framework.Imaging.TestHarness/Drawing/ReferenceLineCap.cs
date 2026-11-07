namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>The shape of the ends of a reference stroke.</summary>
public enum ReferenceLineCap
{
    /// <summary>The stroke stops at the end point.</summary>
    Butt,

    /// <summary>The stroke ends with a half disk.</summary>
    Round,

    /// <summary>The stroke is extended by half its width.</summary>
    Square,
}
