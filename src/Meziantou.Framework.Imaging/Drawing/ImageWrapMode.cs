namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>What an <see cref="ImageBrush"/> paints outside the pixels of its image.</summary>
public enum ImageWrapMode
{
    /// <summary>Nothing: the image is surrounded by transparent pixels. This is the default.</summary>
    Transparent = 0,

    /// <summary>The pixels of the edges of the image are extended.</summary>
    Pad = 1,

    /// <summary>The image starts over, as tiles.</summary>
    Repeat = 2,

    /// <summary>The image is mirrored, then starts over: neighboring tiles are mirror images of each other.</summary>
    Reflect = 3,
}
