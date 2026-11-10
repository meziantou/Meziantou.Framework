namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Represents a frame, item or attribute of a tag that <see cref="MediaTagInfo"/> has no property for, as stored in the file.
/// </summary>
/// <remarks>
/// <para>The meaning of <see cref="Id"/>, <see cref="Data"/> and <see cref="Flags"/> depends on <see cref="TagType"/>:</para>
/// <list type="table">
/// <listheader><term>Tag type</term><description>Id, data and flags</description></listheader>
/// <item>
/// <term><see cref="MediaTagType.Id3v2"/></term>
/// <description>
/// The four-character ID3v2.4 frame identifier, such as <c>PRIV</c> or <c>UFID</c>, the content of the frame, and the ID3v2.4
/// frame flags. When the flags mark the frame as encrypted, the data is the content as stored.
/// </description>
/// </item>
/// <item>
/// <term><see cref="MediaTagType.Mp4"/></term>
/// <description>
/// The four-character type of the item, such as <c>©too</c> or <c>pgap</c>, and the content of the item atom: its <c>data</c>
/// atoms, headers included. The flags must be 0.
/// </description>
/// </item>
/// <item>
/// <term><see cref="MediaTagType.Ape"/></term>
/// <description>The key of the item, its value, and the item flags: binary (2) or external link (4), and read-only (1).</description>
/// </item>
/// <item>
/// <term><see cref="MediaTagType.Asf"/></term>
/// <description>
/// The name of the attribute, its value, and its data type: byte array (1), boolean (2, stored as a 32-bit value of 0 or 1),
/// 32-bit (3), 64-bit (4) or 16-bit (5) little-endian integer, or GUID (6).
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class MediaNativeField
{
    /// <summary>Gets the tag format this field belongs to.</summary>
    public required MediaTagType TagType { get; init; }

    /// <summary>Gets the identifier of the field: a frame identifier, an atom type, an item key or an attribute name.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the content of the field, as stored.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Data { get; init; } = [];

    /// <summary>Gets the flags or the data type of the field. Their meaning depends on <see cref="TagType"/>.</summary>
    public int Flags { get; init; }
}
