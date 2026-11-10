namespace Meziantou.Framework.Diagrams;

/// <summary>The directions a block arrow points to.</summary>
[Flags]
public enum BlockArrowDirections
{
    /// <summary>No direction.</summary>
    None = 0,

    /// <summary>To the right.</summary>
    Right = 1,

    /// <summary>To the left.</summary>
    Left = 2,

    /// <summary>Up.</summary>
    Up = 4,

    /// <summary>Down.</summary>
    Down = 8,
}
