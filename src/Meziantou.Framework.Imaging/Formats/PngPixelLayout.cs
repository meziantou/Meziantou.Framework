namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The sample layout written by <see cref="PngEncoder"/>.</summary>
public enum PngPixelLayout
{
    /// <summary>
    /// The color type and the bit depth follow the pixel format, losslessly: grayscale for
    /// <see cref="PixelFormat.Gray8"/>/<see cref="PixelFormat.Gray16"/>, RGB for <see cref="PixelFormat.Rgb24"/> and RGBA
    /// for the pixel formats with alpha, with 16-bit samples for the 16-bit pixel formats. This is the default: the
    /// mapping never depends on the pixels, and a palette is never written.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Indexed color (color type 3): indexes into a palette (<c>PLTE</c>) of at most 256 colors, each with its own alpha
    /// (<c>tRNS</c>, written only when an entry is not opaque). The pixels are stored exactly: the palette holds every
    /// distinct combination of color and alpha, the color of fully transparent pixels included, in a deterministic order,
    /// and the indexes use the smallest bit depth that holds it (1, 2, 4 or 8 bits). The encoder never reduces colors: an
    /// image with more than 256 distinct colors is rejected with an <see cref="UnsupportedImageFeatureException"/> before
    /// anything of it is written, and so is a pixel of a 16-bit pixel format whose samples 8 bits cannot represent
    /// exactly (a palette stores 8-bit samples).
    /// <para>
    /// A file has a single palette, written before the first image: it holds the colors of every image that is known at
    /// that point. The eager save methods know every frame and the poster of an animation; a sequential
    /// <see cref="ImageWriter{TPixel}"/> only knows the first image it is given, so every later frame must use the colors
    /// of that image, and a frame that does not is rejected before anything of it is written.
    /// </para>
    /// <para>
    /// An indexed file is read back as <see cref="PixelFormat.Rgba32"/>, whatever the pixel format it was written from. An
    /// ICC profile must be an RGB profile, also for a grayscale pixel format.
    /// </para>
    /// </summary>
    Indexed = 1,
}
