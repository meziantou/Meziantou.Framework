namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// An immutable ancillary chunk of a PNG file that the library does not interpret (a private chunk, or a public chunk such
/// as <c>gAMA</c>, <c>bKGD</c> or <c>tIME</c> that it reads into no other metadata): its type, its data and its position.
/// </summary>
/// <remarks>
/// <para>
/// The case of the letters of the type carries its properties (PNG specification, section 5.4): an ancillary chunk has a
/// lowercase first letter, a private chunk a lowercase second letter, and a chunk that can be copied whatever the changes
/// to the image has a lowercase fourth letter (<see cref="IsSafeToCopy"/>). The third letter is uppercase.
/// </para>
/// <para>
/// <see cref="Formats.PngEncoder"/> writes the chunks at their position. Because it encodes the image data again, it only
/// writes the chunks that are safe to copy unless <see cref="Formats.PngEncoder.WriteUnsafeToCopyChunks"/> says that the
/// others still describe the image (PNG specification, section 14.2). The other encoders ignore the chunks.
/// </para>
/// </remarks>
public sealed class PngChunk : IEquatable<PngChunk>
{
    // The chunks the library reads or writes itself: they are never stored as raw chunks
    private static readonly string[] ManagedTypes = ["tRNS", "pHYs", "iCCP", "cICP", "mDCV", "cLLI", "eXIf", "tEXt", "zTXt", "iTXt", "acTL", "fcTL", "fdAT"];

    /// <summary>Initializes a new instance of the <see cref="PngChunk"/> class.</summary>
    /// <param name="type">The type: four ASCII letters, the first lowercase (ancillary) and the third uppercase.</param>
    /// <param name="data">The data of the chunk, without its length, type and CRC.</param>
    /// <param name="position">Where the chunk is written relative to the critical chunks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="type"/> is not four ASCII letters, is a critical chunk (uppercase first letter), has a lowercase third
    /// letter (reserved), or is a chunk the library reads or writes itself (<c>tRNS</c>, <c>pHYs</c>, <c>iCCP</c>,
    /// <c>cICP</c>, <c>mDCV</c>, <c>cLLI</c>, <c>eXIf</c>, <c>tEXt</c>, <c>zTXt</c>, <c>iTXt</c>, <c>acTL</c>, <c>fcTL</c>,
    /// <c>fdAT</c>).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is not a defined <see cref="PngChunkPosition"/>.</exception>
    public PngChunk(string type, MetadataBlob data, PngChunkPosition position = PngChunkPosition.BeforeImageData)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(data);
        if (!IsValidType(type, out var reason))
            throw new ArgumentException(reason, nameof(type));

        if (!Enum.IsDefined(position))
            throw new ArgumentOutOfRangeException(nameof(position), position, "The position is not valid.");

        Type = type;
        Data = data;
        Position = position;
    }

    /// <summary>Gets the type of the chunk: four ASCII letters.</summary>
    public string Type { get; }

    /// <summary>Gets the data of the chunk.</summary>
    public MetadataBlob Data { get; }

    /// <summary>Gets where the chunk lies relative to the critical chunks.</summary>
    public PngChunkPosition Position { get; }

    /// <summary>
    /// Gets a value indicating whether the chunk can be copied to a file whose image data changed (a lowercase fourth letter):
    /// otherwise it depends on the image data, and is only valid for the image it was written with.
    /// </summary>
    public bool IsSafeToCopy => char.IsAsciiLetterLower(Type[3]);

    /// <summary>Gets a value indicating whether the chunk is private (a lowercase second letter), rather than defined by the PNG specification or registered.</summary>
    public bool IsPrivate => char.IsAsciiLetterLower(Type[1]);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] PngChunk? other)
        => other is not null
        && string.Equals(Type, other.Type, StringComparison.Ordinal)
        && Position == other.Position
        && Data.Equals(other.Data);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PngChunk);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Type), Position, Data);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Type} ({Data.Length} bytes, {Position})");

    /// <summary>Determines whether a chunk type can be stored as a raw chunk.</summary>
    internal static bool IsValidType(string type, [NotNullWhen(false)] out string? reason)
    {
        if (type.Length != 4 || !type.All(char.IsAsciiLetter))
        {
            reason = "A PNG chunk type is four ASCII letters.";
            return false;
        }

        if (!char.IsAsciiLetterLower(type[0]))
        {
            reason = $"The PNG chunk type '{type}' is a critical chunk (uppercase first letter): only ancillary chunks can be stored.";
            return false;
        }

        if (!char.IsAsciiLetterUpper(type[2]))
        {
            reason = $"The PNG chunk type '{type}' has a lowercase third letter, which the PNG specification reserves.";
            return false;
        }

        if (ManagedTypes.Contains(type, StringComparer.Ordinal))
        {
            reason = $"The PNG chunk '{type}' is read and written by the library from the other metadata of the image.";
            return false;
        }

        reason = null;
        return true;
    }
}
