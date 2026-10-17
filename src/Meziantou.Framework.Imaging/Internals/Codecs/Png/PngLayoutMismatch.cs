namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Why a pixel cannot be stored in a <see cref="PngOptimizedLayout"/>.</summary>
internal enum PngLayoutMismatch
{
    /// <summary>Every pixel can be stored.</summary>
    None,

    /// <summary>The pixel is not opaque and the layout has no alpha.</summary>
    Alpha,

    /// <summary>The pixel is transparent with another color than the color key, or opaque with the color of the key.</summary>
    ColorKey,

    /// <summary>The pixel is not gray in a grayscale layout, or its color is not in the palette.</summary>
    Color,

    /// <summary>A sample needs more bits than the bit depth of the layout.</summary>
    BitDepth,
}
