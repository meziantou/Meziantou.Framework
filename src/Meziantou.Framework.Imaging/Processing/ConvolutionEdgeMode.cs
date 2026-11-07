namespace Meziantou.Framework.Imaging;

/// <summary>Selects the pixels a convolution reads outside the image.</summary>
public enum ConvolutionEdgeMode
{
    /// <summary>Repeats the nearest edge pixel (<c>aaa|abcd|ddd</c>). This is the default.</summary>
    Clamp = 0,

    /// <summary>Reflects the image about its border, repeating the edge pixel (<c>cba|abcd|dcb</c>).</summary>
    Mirror = 1,

    /// <summary>Tiles the image (<c>bcd|abcd|abc</c>).</summary>
    Wrap = 2,

    /// <summary>
    /// Reads nothing: outside pixels contribute zero to every sample (transparent black; black for pixel formats without
    /// alpha).
    /// </summary>
    Zero = 3,
}
