namespace Meziantou.Framework.Imaging;

/// <summary>Selects the part of the image kept when <see cref="ResizeMode.Cover"/> crops the overflow.</summary>
public enum ResizeAnchor
{
    /// <summary>Keeps the center. This is the default.</summary>
    Center = 0,

    /// <summary>Keeps the top-left corner.</summary>
    TopLeft = 1,

    /// <summary>Keeps the top edge, centered horizontally.</summary>
    Top = 2,

    /// <summary>Keeps the top-right corner.</summary>
    TopRight = 3,

    /// <summary>Keeps the left edge, centered vertically.</summary>
    Left = 4,

    /// <summary>Keeps the right edge, centered vertically.</summary>
    Right = 5,

    /// <summary>Keeps the bottom-left corner.</summary>
    BottomLeft = 6,

    /// <summary>Keeps the bottom edge, centered horizontally.</summary>
    Bottom = 7,

    /// <summary>Keeps the bottom-right corner.</summary>
    BottomRight = 8,
}
