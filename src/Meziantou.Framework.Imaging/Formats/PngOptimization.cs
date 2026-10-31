namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects how much <see cref="PngEncoder"/> searches for a smaller file.</summary>
public enum PngOptimization
{
    /// <summary>
    /// The output follows the settings of the encoder: the layout of <see cref="PngEncoder.PixelLayout"/>, the filters of
    /// <see cref="PngEncoder.Filter"/>, the zlib level of <see cref="PngEncoder.CompressionLevel"/> and full-canvas
    /// animation frames. This is the default.
    /// </summary>
    None = 0,

    /// <summary>
    /// Searches the lossless options and writes the smallest file it finds; the pixels, the alpha values (the color of
    /// fully transparent pixels included), the frames, their durations, the poster and the metadata are stored exactly.
    /// Encoding is several times slower than with <see cref="None"/>.
    /// <list type="bullet">
    /// <item><description>
    /// <b>Layout.</b> The color type and the bit depth are the ones the pixels need: grayscale (1, 2, 4, 8 or 16 bits),
    /// grayscale with alpha, RGB or RGBA (8 or 16 bits), a <c>tRNS</c> color key when every transparent pixel has one
    /// color that no opaque pixel has, or a palette of at most 256 colors. The candidate layouts are compared by
    /// compressing every image known before the header is written, and the smallest total is kept. With
    /// <see cref="PngPixelLayout.Indexed"/>, only palettes are candidates.
    /// </description></item>
    /// <item><description>
    /// <b>Filters and compression.</b> Every image is compressed with each filter strategy (<see cref="PngFilter"/>,
    /// or only the fixed filter that <see cref="PngEncoder.Filter"/> selects) at the zlib levels
    /// <see cref="System.IO.Compression.CompressionLevel.SmallestSize"/>, <see cref="System.IO.Compression.CompressionLevel.Optimal"/>
    /// and <see cref="System.IO.Compression.CompressionLevel.Fastest"/> (a lower level sometimes finds smaller data), and the
    /// smallest is written: <see cref="PngEncoder.CompressionLevel"/> is ignored. <see cref="PngEncoder.Interlaced"/> is
    /// respected.
    /// </description></item>
    /// <item><description>
    /// <b>Animation frames.</b> Each frame stores the rectangle that differs from the canvas it is drawn on, with the
    /// blending and the disposal of the previous frame that give the smallest data. The frames are computed from the
    /// displayed frames, never from the delta information of a decoded source. Frame zero covers the canvas when it is
    /// the default image. For an animation saved at once, a layout without a transparent value is also tried with one (a
    /// <c>tRNS</c> color key that no pixel has, or a transparent palette entry), which lets the frames skip their unchanged
    /// pixels: the layout trial compresses the frames as the regions that changed, so the transparent value is only
    /// written when it saves more than it costs. The samples and the palette indexes of an animation have 8 bits at least,
    /// because some decoders misplace the packed pixels of a frame region.
    /// </description></item>
    /// </list>
    /// <para>
    /// A file has a single layout, written before its first image. The eager save methods know every frame and the poster
    /// of an animation, so the layout fits all of them. A sequential <see cref="ImageWriter{TPixel}"/> only knows the first
    /// image it is given: a later frame that the layout cannot store (more colors, alpha values, or samples that need more
    /// bits) is rejected with an <see cref="UnsupportedImageFeatureException"/> before anything of it is written. A
    /// sequential writer also writes each animation frame when it is given the next one (or when it completes), because
    /// the control data of a frame holds the disposal chosen for the next one.
    /// </para>
    /// <para>
    /// The file may be read back with another pixel format than the one it was written from (for example
    /// <see cref="PixelFormat.Rgb24"/> for an opaque <see cref="PixelFormat.Rgba32"/> image, or
    /// <see cref="PixelFormat.Rgba32"/> for a file with a color key or a palette); the pixel values are the same.
    /// </para>
    /// </summary>
    MinimumSize = 1,

    /// <summary>
    /// Reads the pixels of a still image once and writes them in the narrowest exact layout they allow, instead of the
    /// layout of the pixel format; the filters follow <see cref="PngEncoder.Filter"/> and the zlib level
    /// <see cref="PngEncoder.CompressionLevel"/>, as with <see cref="None"/>. Nothing is compressed twice, so encoding
    /// costs about one more pass over the pixels.
    /// <list type="bullet">
    /// <item><description>
    /// The candidates are the ones of <see cref="MinimumSize"/> without the <c>tRNS</c> color key: grayscale when every
    /// pixel is gray (1, 2, 4, 8 or 16 bits), without alpha when every pixel is opaque, 8-bit samples when the 16-bit ones
    /// are 8-bit values, and a palette of 1, 2, 4 or 8 bits (with <c>tRNS</c> for its translucent entries) when the image
    /// has at most 256 colors and they are 8-bit colors. The one with the smallest unfiltered data (its <c>PLTE</c> and
    /// <c>tRNS</c> chunks included) is written; on a tie, the layout without a palette. With
    /// <see cref="PngPixelLayout.Indexed"/>, only palettes are candidates.
    /// </description></item>
    /// <item><description>
    /// The analysis stops as soon as nothing more can change the choice: once there are more than 256 colors, a pixel is
    /// not gray, a pixel is translucent (or the pixel format has no alpha) and a 16-bit sample is not an 8-bit value (or
    /// the samples are 8-bit). An opaque image of a pixel format with alpha is read to its last pixel.
    /// </description></item>
    /// <item><description>
    /// An ICC profile keeps its color space: a grayscale layout is a candidate only when the profile describes gray, and
    /// the RGB and palette layouts only when it describes RGB.
    /// </description></item>
    /// <item><description>
    /// An animated PNG is written as with <see cref="None"/>: the layout of a sequential writer would have to hold the
    /// frames that come after the header.
    /// </description></item>
    /// </list>
    /// <para>
    /// The pixel values are stored exactly, but the file may be read back with another pixel format than the one it was
    /// written from, as with <see cref="MinimumSize"/>: for example <see cref="PixelFormat.Rgb24"/> for an opaque
    /// <see cref="PixelFormat.Rgba32"/> image, <see cref="PixelFormat.Gray8"/> for a gray one, or
    /// <see cref="PixelFormat.Rgba32"/> for a palette. The smallest unfiltered data is usually, not always, the smallest
    /// file: <see cref="MinimumSize"/> compresses the candidates to find it.
    /// </para>
    /// </summary>
    Layout = 2,
}
