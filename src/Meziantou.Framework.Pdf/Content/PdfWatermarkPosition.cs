namespace Meziantou.Framework.Pdf;

/// <summary>Where a watermark is placed on the page as it is displayed (<see cref="PdfWatermarkOptions.Position"/>).</summary>
public enum PdfWatermarkPosition
{
    /// <summary>In the center of the page.</summary>
    Center,

    /// <summary>In the upper-left corner.</summary>
    TopLeft,

    /// <summary>At the top, centered horizontally.</summary>
    Top,

    /// <summary>In the upper-right corner.</summary>
    TopRight,

    /// <summary>On the left, centered vertically.</summary>
    Left,

    /// <summary>On the right, centered vertically.</summary>
    Right,

    /// <summary>In the lower-left corner.</summary>
    BottomLeft,

    /// <summary>At the bottom, centered horizontally.</summary>
    Bottom,

    /// <summary>In the lower-right corner.</summary>
    BottomRight,
}
