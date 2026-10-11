using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants and the signature rule of the Windows animated cursor container (<c>ANI</c>): a RIFF file of form type
/// <c>ACON</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is a list of chunks, each an 8-byte header (a four-character code and a little-endian 32-bit data size)
/// followed by the data and, when the size is odd, one pad byte:
/// </para>
/// <list type="bullet">
/// <item><description><c>anih</c>: the 36-byte header (number of stored frames, number of displayed steps, default display rate, flags);</description></item>
/// <item><description><c>rate</c> (optional): one display rate per step, in jiffies (sixtieths of a second);</description></item>
/// <item><description><c>seq </c> (optional): the index of the stored frame shown at each step;</description></item>
/// <item><description><c>LIST</c> of type <c>fram</c>: one <c>icon</c> chunk per stored frame, each a complete icon or cursor file;</description></item>
/// <item><description><c>LIST</c> of type <c>INFO</c> (optional): <c>INAM</c> (title) and <c>IART</c> (author) strings.</description></item>
/// </list>
/// <para>
/// An animated cursor is an animation, not a collection of representations: a step has a duration and the steps are played
/// in order, forever.
/// </para>
/// </remarks>
internal static class AniFormat
{
    /// <summary>The length of the RIFF header: <c>RIFF</c>, the size of what follows it, and the form type.</summary>
    public const int RiffHeaderLength = 12;

    /// <summary>The length of a chunk header: the four-character code and the data size.</summary>
    public const int ChunkHeaderLength = 8;

    /// <summary>The length of the <c>anih</c> data, which is also the value of its first field.</summary>
    public const int HeaderLength = 36;

    /// <summary>The length of one entry of the <c>rate</c> and <c>seq </c> tables.</summary>
    public const int TableEntryLength = 4;

    /// <summary>The <c>anih</c> flag of frames stored as icon or cursor files (otherwise they are raw bitmaps).</summary>
    public const uint IconFlag = 1;

    /// <summary>The <c>anih</c> flag announcing a <c>seq </c> chunk.</summary>
    public const uint SequenceFlag = 2;

    /// <summary>The largest value of the RIFF size field, which counts every byte after it.</summary>
    public const long MaxRiffSize = uint.MaxValue;

    /// <summary>The keyword of the text entry holding the <c>INAM</c> string.</summary>
    public const string TitleKeyword = "Title";

    /// <summary>The keyword of the text entry holding the <c>IART</c> string.</summary>
    public const string AuthorKeyword = "Author";

    public static ReadOnlySpan<byte> Riff => "RIFF"u8;

    public static ReadOnlySpan<byte> Acon => "ACON"u8;

    public static ReadOnlySpan<byte> List => "LIST"u8;

    public static ReadOnlySpan<byte> Info => "INFO"u8;

    public static ReadOnlySpan<byte> Title => "INAM"u8;

    public static ReadOnlySpan<byte> Author => "IART"u8;

    public static ReadOnlySpan<byte> Header => "anih"u8;

    public static ReadOnlySpan<byte> Rate => "rate"u8;

    public static ReadOnlySpan<byte> Sequence => "seq "u8;

    public static ReadOnlySpan<byte> Frames => "fram"u8;

    public static ReadOnlySpan<byte> Icon => "icon"u8;

    /// <summary>Determines whether a prefix is the start of an animated cursor: <c>RIFF</c>, a size, then <c>ACON</c>.</summary>
    /// <param name="prefix">The first bytes of the data.</param>
    /// <returns><see langword="true"/> when the prefix holds the whole 12-byte signature.</returns>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
        => prefix.Length >= RiffHeaderLength && prefix.StartsWith(Riff) && prefix.Slice(8, 4).SequenceEqual(Acon);

    /// <summary>Writes a chunk header.</summary>
    /// <param name="destination">At least <see cref="ChunkHeaderLength"/> bytes.</param>
    /// <param name="fourCC">The four-character code.</param>
    /// <param name="size">The size of the data, without the pad byte.</param>
    public static void WriteChunkHeader(Span<byte> destination, ReadOnlySpan<byte> fourCC, uint size)
    {
        fourCC.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], size);
    }

    /// <summary>Validates that a file of <paramref name="length"/> bytes can declare its size in the RIFF header.</summary>
    /// <param name="length">The length of the whole file.</param>
    /// <exception cref="UnsupportedImageFeatureException">The file would be larger than a 32-bit RIFF size can declare.</exception>
    public static void EnsureFileLength(long length)
    {
        if (length - 8 > MaxRiffSize)
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"The animated cursor would be {length} bytes long, more than the 4 GiB its 32-bit RIFF size can declare."), "ANI file size");
    }

    /// <summary>Creates the exception reported for malformed animated cursor data.</summary>
    public static InvalidImageContentException Invalid(string message, Exception? innerException = null) => new(message, ImageFormat.Ani, innerException);

    /// <summary>Creates the exception reported for a recognized but unsupported animated cursor feature.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Ani, feature);
}
