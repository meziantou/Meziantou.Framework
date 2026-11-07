namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable textual metadata entry (PNG tEXt/zTXt/iTXt chunk, GIF comment extension, JPEG COM segment).</summary>
/// <remarks>
/// Whether an entry is representable depends on the output format (for example PNG keywords are limited to 1 to 79
/// Latin-1 characters, and GIF/JPEG only store comments). Entries are validated when they are serialized; unsupported
/// entries follow the encoder's <see cref="Formats.MetadataHandling"/> policy. The compression of PNG text chunks is an
/// encoding detail chosen by the encoder.
/// </remarks>
public sealed class ImageTextEntry : IEquatable<ImageTextEntry>
{
    /// <summary>The keyword used for comments (GIF comment extensions, JPEG COM segments, PNG "Comment" keyword).</summary>
    public const string CommentKeyword = "Comment";

    /// <summary>Initializes a new instance of the <see cref="ImageTextEntry"/> class.</summary>
    /// <param name="keyword">The keyword (for example <c>"Title"</c>, <c>"Author"</c> or <see cref="CommentKeyword"/>).</param>
    /// <param name="value">The text.</param>
    /// <param name="languageTag">The optional RFC 5646 language tag of <paramref name="value"/>.</param>
    /// <param name="translatedKeyword">The optional translation of <paramref name="keyword"/> into <paramref name="languageTag"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="keyword"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="keyword"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public ImageTextEntry(string keyword, string value, string? languageTag = null, string? translatedKeyword = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyword);
        ArgumentNullException.ThrowIfNull(value);
        Keyword = keyword;
        Value = value;
        LanguageTag = languageTag;
        TranslatedKeyword = translatedKeyword;
    }

    /// <summary>Gets the keyword.</summary>
    public string Keyword { get; }

    /// <summary>Gets the text.</summary>
    public string Value { get; }

    /// <summary>Gets the RFC 5646 language tag of <see cref="Value"/>, if any.</summary>
    public string? LanguageTag { get; }

    /// <summary>Gets the translated keyword, if any.</summary>
    public string? TranslatedKeyword { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] ImageTextEntry? other)
        => other is not null
        && string.Equals(Keyword, other.Keyword, StringComparison.Ordinal)
        && string.Equals(Value, other.Value, StringComparison.Ordinal)
        && string.Equals(LanguageTag, other.LanguageTag, StringComparison.Ordinal)
        && string.Equals(TranslatedKeyword, other.TranslatedKeyword, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ImageTextEntry);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(Keyword),
        StringComparer.Ordinal.GetHashCode(Value),
        LanguageTag is null ? 0 : StringComparer.Ordinal.GetHashCode(LanguageTag),
        TranslatedKeyword is null ? 0 : StringComparer.Ordinal.GetHashCode(TranslatedKeyword));

    /// <inheritdoc />
    public override string ToString() => $"{Keyword}: {Value}";
}
