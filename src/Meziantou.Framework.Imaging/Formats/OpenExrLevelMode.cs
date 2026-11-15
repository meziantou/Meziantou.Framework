namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The resolution levels of a tiled OpenEXR file written by <see cref="OpenExrEncoder"/>.</summary>
public enum OpenExrLevelMode
{
    /// <summary>The full-resolution image only (<c>ONE_LEVEL</c>). This is the default.</summary>
    OneLevel = 0,

    /// <summary>
    /// The full-resolution image, then levels whose width and height are both halved down to one pixel (<c>MIPMAP_LEVELS</c>).
    /// </summary>
    MipmapLevels = 1,

    /// <summary>
    /// Every combination of halved widths and halved heights down to one pixel (<c>RIPMAP_LEVELS</c>).
    /// </summary>
    RipmapLevels = 2,
}
