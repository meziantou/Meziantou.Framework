using System.Diagnostics;

namespace Meziantou.Framework.Imaging;

/// <summary>Immutable resource limits applied while decoding, processing and encoding images.</summary>
/// <remarks>
/// <para>
/// Every limit must be positive; there is no "zero means unlimited" convention. To effectively disable a limit, set it to
/// the maximum value of its type. Exceeding a limit throws an <see cref="ImageResourceLimitException"/>; it is never turned
/// into a silently truncated result.
/// </para>
/// <para>
/// Limits are safety bounds, not a selection mechanism: use <see cref="ImageDecodeOptions.FrameLimit"/> to deliberately
/// decode only a prefix of an animation. Allocation limits apply to library-controlled buffers within an allocation scope
/// (an image and the operation working on it); they are not a process-wide memory cap.
/// </para>
/// </remarks>
public sealed class ImageResourceLimits
{
    /// <summary>The default maximum canvas width and height, in pixels (32,768).</summary>
    public const int DefaultMaxDimension = 32_768;

    /// <summary>The default maximum number of pixels of one displayed frame (100,000,000).</summary>
    public const long DefaultMaxFramePixels = 100_000_000;

    /// <summary>The default maximum number of frames processed from one input (1,000).</summary>
    public const int DefaultMaxFrames = 1_000;

    /// <summary>The default maximum cumulative number of displayed pixels produced while reading one input (1,000,000,000).</summary>
    public const long DefaultMaxTotalPixels = 1_000_000_000;

    /// <summary>The default maximum number of encoded bytes read from one input (256 MiB).</summary>
    public const long DefaultMaxEncodedBytes = 256L * 1024 * 1024;

    /// <summary>The default maximum number of decompressed or retained metadata bytes (16 MiB).</summary>
    public const long DefaultMaxMetadataBytes = 16L * 1024 * 1024;

    /// <summary>The default maximum number of live library-controlled bytes per allocation scope (512 MiB).</summary>
    public const long DefaultMaxLiveAllocationBytes = 512L * 1024 * 1024;

    /// <summary>The default maximum number of bytes a font keeps in memory (256 MiB).</summary>
    public const long DefaultMaxFontBytes = 256L * 1024 * 1024;

    /// <summary>The default maximum number of elementary operations to produce one glyph (1,000,000).</summary>
    public const long DefaultMaxGlyphComplexity = 1_000_000;

    /// <summary>The default maximum length of a text that is laid out, in UTF-16 code units (1,000,000).</summary>
    public const long DefaultMaxTextLength = 1_000_000;

    /// <summary>The default maximum number of glyphs of a text layout (1,000,000).</summary>
    public const long DefaultMaxTextGlyphs = 1_000_000;

    /// <summary>The default maximum number of lines of a text layout (100,000).</summary>
    public const int DefaultMaxTextLines = 100_000;

    /// <summary>Gets the default limits.</summary>
    public static ImageResourceLimits Default { get; } = new();

    /// <summary>Gets the maximum canvas width, in pixels. Defaults to <see cref="DefaultMaxDimension"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxWidth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxDimension;

    /// <summary>Gets the maximum canvas height, in pixels. Defaults to <see cref="DefaultMaxDimension"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxHeight
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxDimension;

    /// <summary>Gets the maximum number of pixels of one displayed frame. Defaults to <see cref="DefaultMaxFramePixels"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxFramePixels
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxFramePixels;

    /// <summary>Gets the maximum number of frames (displayed frames plus poster frame) processed from one input. Defaults to <see cref="DefaultMaxFrames"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxFrames
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxFrames;

    /// <summary>Gets the maximum cumulative number of displayed pixels produced while reading one input. Defaults to <see cref="DefaultMaxTotalPixels"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxTotalPixels
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxTotalPixels;

    /// <summary>Gets the maximum number of encoded bytes read from one input. Defaults to <see cref="DefaultMaxEncodedBytes"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxEncodedBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxEncodedBytes;

    /// <summary>Gets the maximum number of decompressed or retained metadata bytes (profiles, text, EXIF, XMP) per input. Defaults to <see cref="DefaultMaxMetadataBytes"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxMetadataBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxMetadataBytes;

    /// <summary>Gets the maximum number of live library-controlled bytes per allocation scope. Defaults to <see cref="DefaultMaxLiveAllocationBytes"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxLiveAllocationBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxLiveAllocationBytes;

    /// <summary>
    /// Gets the maximum number of bytes a font keeps in memory: the size of a font file, once decompressed when it is a
    /// WOFF or WOFF2 file. Defaults to <see cref="DefaultMaxFontBytes"/>.
    /// </summary>
    /// <remarks>The bytes read from the input are bounded by <see cref="MaxEncodedBytes"/>, as for an image.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxFontBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxFontBytes;

    /// <summary>
    /// Gets the maximum number of elementary operations spent to produce one glyph: the points of its outline, the
    /// components of a composite glyph, the operators of a charstring, the nodes of a color glyph. Defaults to
    /// <see cref="DefaultMaxGlyphComplexity"/>.
    /// </summary>
    /// <remarks>
    /// A font can describe, in a few bytes, a glyph whose outline is exponentially large (glyphs made of glyphs made of
    /// glyphs). This limit bounds the work and the memory a hostile font can ask for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxGlyphComplexity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxGlyphComplexity;

    /// <summary>
    /// Gets the maximum length of a text that is laid out, in UTF-16 code units. Defaults to
    /// <see cref="DefaultMaxTextLength"/>.
    /// </summary>
    /// <remarks>The limits of a text layout are those of the configuration its font was loaded with.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxTextLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxTextLength;

    /// <summary>
    /// Gets the maximum number of glyphs of a text layout. Defaults to <see cref="DefaultMaxTextGlyphs"/>.
    /// </summary>
    /// <remarks>
    /// A layout keeps the identifier and the position of each of its glyphs: this limit bounds the memory of a layout
    /// and the work of drawing it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxTextGlyphs
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxTextGlyphs;

    /// <summary>Gets the maximum number of lines of a text layout. Defaults to <see cref="DefaultMaxTextLines"/>.</summary>
    /// <remarks>
    /// A text that needs more lines is an error; <see cref="Typography.TextLayoutOptions.MaxLines"/> is what cuts a text
    /// at a number of lines.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxTextLines
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxTextLines;

    /// <summary>
    /// Validates canvas dimensions against <see cref="MaxWidth"/>, <see cref="MaxHeight"/> and <see cref="MaxFramePixels"/>,
    /// in that order. Used by every consumer that creates a canvas (decoders, constructors, imports, resize targets).
    /// </summary>
    /// <param name="width">The width. Must be positive (validated by the caller as an argument or as content).</param>
    /// <param name="height">The height. Must be positive.</param>
    /// <exception cref="ImageResourceLimitException">A limit is exceeded.</exception>
    internal void EnsureCanvasWithinLimits(long width, long height)
    {
        Debug.Assert(width > 0 && height > 0);
        if (width > MaxWidth)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, MaxWidth, width);

        if (height > MaxHeight)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, MaxHeight, height);

        // Both dimensions fit in 32 bits here: the product cannot overflow
        var pixels = width * height;
        if (pixels > MaxFramePixels)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FramePixels, MaxFramePixels, pixels);
    }

    /// <summary>Validates that an allocation scope may hold <paramref name="requestedBytes"/> live bytes.</summary>
    /// <param name="requestedBytes">The total live bytes the scope would hold.</param>
    /// <exception cref="ImageResourceLimitException">The total exceeds <see cref="MaxLiveAllocationBytes"/>.</exception>
    internal void EnsureLiveAllocationWithinLimit(long requestedBytes)
    {
        if (requestedBytes > MaxLiveAllocationBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, MaxLiveAllocationBytes, requestedBytes);
    }
}
