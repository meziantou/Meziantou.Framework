using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

/// <summary>
/// A frame of an ID3v2 tag, as stored in the file and as decoded.
/// </summary>
/// <remarks>
/// The data references the buffer the tag was read into, so a frame must not outlive it.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Id3v2Frame
{
    /// <summary>Gets the frame identifier as stored: three characters for ID3v2.2, four otherwise.</summary>
    public string Id { get; init; }

    /// <summary>Gets the frame flags as stored. Always 0 for ID3v2.2, which has no frame flags.</summary>
    public ushort Flags { get; init; }

    /// <summary>Gets the frame content as stored, after the tag-wide unsynchronisation of ID3v2.2 and ID3v2.3 is undone.</summary>
    public ReadOnlyMemory<byte> StoredData { get; init; }

    /// <summary>
    /// Gets the frame content with the grouping byte, encryption method, data length indicator, unsynchronisation
    /// and compression removed, or <see langword="null"/> when it cannot be decoded (an encrypted frame).
    /// </summary>
    public ReadOnlyMemory<byte>? Payload { get; init; }
}
