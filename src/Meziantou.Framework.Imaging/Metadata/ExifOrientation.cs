namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The EXIF orientation of the stored pixels: how the stored rows and columns map to the visual top and left of the
/// displayed image. Values match the EXIF/TIFF <c>Orientation</c> tag (1 to 8).
/// </summary>
/// <remarks>
/// The library never applies the orientation implicitly while loading. Use <c>AutoOrient</c> to transform the pixels to
/// <see cref="TopLeft"/>. Other operations work in stored-pixel coordinates and keep the orientation value.
/// </remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "EXIF orientation values are 1 to 8; 0 is not a valid orientation.")]
public enum ExifOrientation
{
    /// <summary>1: the 0th row is the visual top, the 0th column is the visual left (normal).</summary>
    TopLeft = 1,

    /// <summary>2: the 0th row is the visual top, the 0th column is the visual right (mirrored horizontally).</summary>
    TopRight = 2,

    /// <summary>3: the 0th row is the visual bottom, the 0th column is the visual right (rotated 180°).</summary>
    BottomRight = 3,

    /// <summary>4: the 0th row is the visual bottom, the 0th column is the visual left (mirrored vertically).</summary>
    BottomLeft = 4,

    /// <summary>5: the 0th row is the visual left, the 0th column is the visual top (transposed).</summary>
    LeftTop = 5,

    /// <summary>6: the 0th row is the visual right, the 0th column is the visual top (display requires a 90° clockwise rotation).</summary>
    RightTop = 6,

    /// <summary>7: the 0th row is the visual right, the 0th column is the visual bottom (transversed).</summary>
    RightBottom = 7,

    /// <summary>8: the 0th row is the visual left, the 0th column is the visual bottom (display requires a 90° counter-clockwise rotation).</summary>
    LeftBottom = 8,
}
