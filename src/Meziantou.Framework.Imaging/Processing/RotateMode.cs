namespace Meziantou.Framework.Imaging;

/// <summary>A clockwise rotation by a multiple of 90 degrees. Rotations are exact pixel permutations, never resampling.</summary>
public enum RotateMode
{
    /// <summary>No rotation.</summary>
    None = 0,

    /// <summary>Rotates 90 degrees clockwise; width and height are swapped.</summary>
    Rotate90 = 90,

    /// <summary>Rotates 180 degrees.</summary>
    Rotate180 = 180,

    /// <summary>Rotates 270 degrees clockwise (90 degrees counter-clockwise); width and height are swapped.</summary>
    Rotate270 = 270,
}
