namespace Meziantou.Framework.Imaging;

/// <summary>The axis of a mirror operation.</summary>
public enum FlipMode
{
    /// <summary>Mirrors left and right (reverses each row).</summary>
    Horizontal = 0,

    /// <summary>Mirrors top and bottom (reverses the row order).</summary>
    Vertical = 1,
}
