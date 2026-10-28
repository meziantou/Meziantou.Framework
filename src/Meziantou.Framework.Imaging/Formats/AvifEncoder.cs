namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for AVIF encoding.</summary>
/// <remarks>
/// <para>
/// <see cref="Compression"/> selects the AV1 coding: <see cref="AvifCompression.Lossless"/> (the default) preserves every
/// sample; <see cref="AvifCompression.Lossy"/> trades fidelity for size according to <see cref="Quality"/>.
/// <see cref="Effort"/> trades encoding time for size and never changes the decoded pixels of lossless output. Lossless AVIF
/// files are large: select the lossy coding for photographs.
/// </para>
/// <para>
/// AVIF stores 8, 10 or 12 bits per sample. 8-bit pixel formats are written with 8 bits unless <see cref="BitDepth"/> asks for
/// more; 16-bit pixel formats need <see cref="BitDepth"/> or <see cref="AllowBitDepthReduction"/>, because they lose
/// precision. Gray pixel formats are written as monochrome images. Alpha is always stored when the image has a pixel that is
/// not opaque, losslessly, as an auxiliary image.
/// </para>
/// <para>
/// An image with several frames is written as an image sequence: every frame is a full image, with its exact duration, and
/// the play count of the animation. The first frame is a key frame; the others are predicted from the frame that precedes
/// them, unless <see cref="KeyFrameInterval"/> asks for more key frames. A sequence needs a seekable destination and the
/// number of its frames
/// (<see cref="ImageWriterOptions.ExpectedFrameCount"/> for a writer), because the box that describes the frames precedes
/// them and is written last. A still image goes to any stream. An image sequence of one image cannot store animation
/// settings: it is read back as a still image.
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): the CICP color description (<c>colr</c> box, with the
/// matrix coefficients and the range that the encoder used), the ICC profile, EXIF and XMP items, the orientation
/// (<c>irot</c> and <c>imir</c> properties of a still image, the matrix of the track headers of an image sequence), the
/// content light level, the mastering display color volume and the pixel
/// aspect ratio (<c>clli</c>, <c>mdcv</c> and <c>pasp</c> boxes). AVIF has no text or resolution field.
/// </para>
/// </remarks>
public sealed class AvifEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Avif;

    /// <summary>Gets the AV1 coding of every image. Defaults to <see cref="AvifCompression.Lossless"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="AvifCompression"/>.</exception>
    public AvifCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the quality of <see cref="AvifCompression.Lossy"/> output, from 0 (smallest) to 100 (best). Defaults to 75. Ignored by
    /// lossless output.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 100.</exception>
    public int Quality
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 75;

    /// <summary>
    /// Gets the compression effort, from 0 (fastest, largest output) to 9 (slowest, smallest output). Defaults to 5. The effort
    /// never changes the decoded pixels of lossless output.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 9.</exception>
    public int Effort
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9);
            field = value;
        }
    } = 5;

    /// <summary>
    /// Gets the chroma subsampling of <see cref="AvifCompression.Lossy"/> color output. Defaults to
    /// <see cref="AvifChromaSubsampling.Auto"/> (4:2:0). Ignored by lossless output (4:4:4) and by gray pixel formats.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="AvifChromaSubsampling"/>.</exception>
    public AvifChromaSubsampling ChromaSubsampling
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The chroma subsampling is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the number of bits of a sample: 8, 10 or 12, or <see langword="null"/> (the default) for the depth of the pixel
    /// format. 8-bit pixels are scaled up exactly. A value for a 16-bit pixel format is the consent to reduce its samples to
    /// this depth (nearest rounding).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not 8, 10 or 12.</exception>
    public int? BitDepth
    {
        get;
        init
        {
            if (value is not (null or 8 or 10 or 12))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The bit depth must be 8, 10 or 12.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether 16-bit pixel formats may be reduced to 12 bits per sample (nearest rounding) when
    /// <see cref="BitDepth"/> is not set. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>
    /// Gets a value indicating whether the color of fully transparent pixels is replaced with transparent black before encoding.
    /// Defaults to <see langword="false"/>: lossless output then preserves the color of fully transparent pixels exactly.
    /// </summary>
    /// <remarks>Setting it usually makes the output smaller; the visible pixels and every alpha value are unchanged.</remarks>
    public bool ClearTransparentColors { get; init; }

    /// <summary>
    /// Gets the size of the tiles of a still image that is coded as a grid, or <see langword="null"/> (the default) for an
    /// image that is coded whole. An image that is larger than a tile is cut into tiles of this size, each one coded on
    /// its own, which a grid item assembles; an image that one tile holds is coded whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A grid bounds the size of what a decoder decodes at once, and the memory that encoding takes (one tile at a time):
    /// it is what the profiles of the format expect of large images. The tiles of the right column and of the bottom row
    /// extend past the image, whose last column and last row they repeat. A tile is coded without its neighbors: lossy
    /// output can show the edges of the tiles, and is larger than the image coded whole.
    /// </para>
    /// <para>
    /// With <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1, the tiles of a grid of at least 65,536 samples
    /// are coded by up to that many workers at once, each with encoders of its own (charged to
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>); the output is byte-identical to the one of one worker.
    /// </para>
    /// <para>
    /// Each side of a tile is between 64 and 65,536 pixels, and a grid has at most 256 columns and 256 rows. With
    /// subsampled chroma (<see cref="ChromaSubsampling"/>), the widths of the tiles and of the image must be even, and
    /// their heights too for 4:2:0. Ignored by an image sequence.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A side of the tiles is not between 64 and 65,536.</exception>
    public Size? GridTileSize
    {
        get;
        init
        {
            if (value is { } tile && (tile.Width is < 64 or > 65536 || tile.Height is < 64 or > 65536))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Each side of a tile must be between 64 and 65,536 pixels.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the color of every pixel is stored multiplied by its alpha (premultiplied alpha).
    /// Defaults to <see langword="false"/>: colors and alpha are stored independently.
    /// </summary>
    /// <remarks>
    /// The multiplication is rounded to nearest at the precision of the pixels, then coded like any color, so it is the
    /// consent to that rounding: a reader divides the colors by the alpha, which does not give the original color back for
    /// the pixels that are nearly transparent, and nothing is left of the color of a fully transparent pixel
    /// (<see cref="ClearTransparentColors"/> then changes nothing). The alpha values are unchanged. Lossless output codes
    /// the multiplied samples exactly. Ignored when the image has no alpha.
    /// </remarks>
    public bool PremultiplyAlpha { get; init; }

    /// <summary>
    /// Gets the clean aperture of the image: the rectangle that readers show, or <see langword="null"/> (the default) for
    /// the whole image. Every pixel is encoded; the ones outside the rectangle stay in the file and are hidden
    /// (<c>clap</c> property of a still image, <c>clap</c> box of the tracks of an image sequence), so that loading the
    /// file gives the rectangle.
    /// </summary>
    /// <remarks>
    /// The rectangle is in the coordinates of the stored pixels, before the orientation of the image. It must be inside
    /// the image. With subsampled chroma (<see cref="ChromaSubsampling"/>), its left coordinate must be even, and its top
    /// coordinate too for 4:2:0: the aperture then selects whole chroma samples, which the format requires.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The rectangle is empty, or its left or top coordinate is negative.</exception>
    public Rectangle? CleanAperture
    {
        get;
        init
        {
            if (value is { } aperture && (aperture.Width is 0 || aperture.Height is 0 || aperture.X < 0 || aperture.Y < 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The clean aperture must not be empty and must have non-negative coordinates.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the number of images from a key frame to the next one in an image sequence. Defaults to 0: the first image is
    /// the only key frame. 1 makes every image a key frame. An image that is not a key frame is predicted from the image
    /// that precedes it, which makes it smaller; a reader that seeks decodes from the last key frame. Ignored by a still
    /// image, and never changes the decoded pixels of lossless output.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int KeyFrameInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets how the durations of the images of a sequence are stored. Defaults to
    /// <see cref="FrameDurationRounding.RequireExact"/>: the time scale of the sequence is the smallest one that holds every
    /// duration exactly, and a sequence whose durations have no common time scale of 32 bits is an error.
    /// <see cref="FrameDurationRounding.RoundToNearest"/> then rounds the durations to microseconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FrameDurationRounding"/>.</exception>
    public FrameDurationRounding DurationRounding
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duration rounding is not valid.");

            field = value;
        }
    }
}
