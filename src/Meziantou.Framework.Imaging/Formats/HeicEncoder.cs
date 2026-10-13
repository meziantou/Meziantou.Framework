namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for HEIC encoding.</summary>
/// <remarks>
/// <para>
/// <see cref="Compression"/> selects the HEVC coding: <see cref="HeicCompression.Lossless"/> (the default) preserves every
/// sample; <see cref="HeicCompression.Lossy"/> trades fidelity for size according to <see cref="Quality"/>.
/// <see cref="Effort"/> trades encoding time for size and never changes the decoded pixels of lossless output. Lossless HEIC
/// files are large: select the lossy coding for photographs.
/// </para>
/// <para>
/// A file is read by the software that has a decoder for the profile of its HEVC bitstream. Lossy 4:2:0 output of 8 or 10
/// bits uses the Main Still Picture, Main and Main 10 profiles, which every HEIC reader decodes. Lossless output, 4:4:4
/// and 4:2:2 output, 12-bit output, gray images and alpha planes use the format range extensions profiles, which fewer
/// readers decode: select the lossy coding with <see cref="HeicChromaSubsampling.Ratio420"/> for files that other devices
/// must open.
/// </para>
/// <para>
/// HEIC stores 8, 10 or 12 bits per sample. 8-bit pixel formats are written with 8 bits unless <see cref="BitDepth"/> asks for
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
/// matrix coefficients and the range that the encoder used), the ICC profile, EXIF and XMP items, and the orientation
/// (<c>irot</c> and <c>imir</c> properties). HEIC has no text or resolution field.
/// </para>
/// <para>
/// HEVC is covered by patents in many countries: check the licensing terms that apply to what you distribute.
/// </para>
/// </remarks>
public sealed class HeicEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Heic;

    /// <summary>Gets the HEVC coding of every image. Defaults to <see cref="HeicCompression.Lossless"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HeicCompression"/>.</exception>
    public HeicCompression Compression
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
    /// Gets the quality of <see cref="HeicCompression.Lossy"/> output, from 0 (smallest) to 100 (best). Defaults to 75. Ignored by
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
    /// Gets the chroma subsampling of <see cref="HeicCompression.Lossy"/> color output. Defaults to
    /// <see cref="HeicChromaSubsampling.Auto"/> (4:2:0). Ignored by lossless output (4:4:4) and by gray pixel formats.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HeicChromaSubsampling"/>.</exception>
    public HeicChromaSubsampling ChromaSubsampling
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
