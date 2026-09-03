namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Controls how tags are written. The defaults match the behaviour of the <see cref="MediaFile.WriteTags(string, MediaTagInfo)"/>
/// overloads that do not take options.
/// </summary>
public sealed class MediaTagWriteOptions
{
    internal static MediaTagWriteOptions Default { get; } = new();

    internal static MediaTagWriteOptions Remove { get; } = new() { WriteId3v1Tag = false, Id3v2PaddingSize = 0 };

    /// <summary>
    /// Gets or sets a value indicating whether an ID3v1 tag is appended to MP3 files. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// An ID3v1 tag stores at most 30 Latin-1 characters per field, so it silently truncates values that an
    /// ID3v2 tag represents in full. It is written by default because some players read nothing else.
    /// </remarks>
    public bool WriteId3v1Tag { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of padding bytes appended to a written ID3v2 tag. The default is 1024.
    /// </summary>
    /// <remarks>
    /// Padding lets another tagger grow the tag later without rewriting the whole file. Set it to 0 to produce
    /// the smallest possible tag.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int Id3v2PaddingSize
    {
        get => field;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1024;
}
