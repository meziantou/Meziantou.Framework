namespace Meziantou.Framework.Imaging;

/// <summary>Where <see cref="ImageProcessingExtensions.AddWatermark(Image, string, ImageWatermarkOptions, CancellationToken)"/> places a single watermark.</summary>
public enum WatermarkPosition
{
    /// <summary>In the center of the image. This is the default.</summary>
    Center = 0,

    /// <summary>In the top-left corner.</summary>
    TopLeft = 1,

    /// <summary>Against the top edge, centered horizontally.</summary>
    Top = 2,

    /// <summary>In the top-right corner.</summary>
    TopRight = 3,

    /// <summary>Against the left edge, centered vertically.</summary>
    Left = 4,

    /// <summary>Against the right edge, centered vertically.</summary>
    Right = 5,

    /// <summary>In the bottom-left corner.</summary>
    BottomLeft = 6,

    /// <summary>Against the bottom edge, centered horizontally.</summary>
    Bottom = 7,

    /// <summary>In the bottom-right corner.</summary>
    BottomRight = 8,
}
