namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the PNG row filters used before compression.</summary>
public enum PngFilter
{
    /// <summary>Chooses a filter per row with a heuristic. This is the default.</summary>
    Adaptive = 0,

    /// <summary>Uses filter type 0 (None) for every row.</summary>
    None = 1,

    /// <summary>Uses filter type 1 (Sub) for every row.</summary>
    Sub = 2,

    /// <summary>Uses filter type 2 (Up) for every row.</summary>
    Up = 3,

    /// <summary>Uses filter type 3 (Average) for every row.</summary>
    Average = 4,

    /// <summary>Uses filter type 4 (Paeth) for every row.</summary>
    Paeth = 5,
}
