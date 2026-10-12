namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>What happens to the text that is beyond the ends of the path it follows.</summary>
public enum TextPathOverflow
{
    /// <summary>The text continues straight, in the direction of the path at its end.</summary>
    Extend = 0,

    /// <summary>The text beyond an end of the path is not drawn.</summary>
    Clip = 1,
}
