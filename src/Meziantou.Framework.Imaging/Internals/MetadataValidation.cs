using System.Buffers.Binary;
using System.Text.Unicode;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Structural validation of metadata payloads. Caller-created blobs are validated when they are adopted by a codec or
/// serialized, never when they are constructed.
/// </summary>
internal static class MetadataValidation
{
    private const int IccHeaderSize = 128;
    private const int IccTagEntrySize = 12;

    /// <summary>Validates the structure of an uncompressed ICC profile: header size field, <c>acsp</c> signature, and tag table bounds.</summary>
    /// <param name="data">The profile bytes.</param>
    /// <param name="error">The reason the profile is invalid.</param>
    /// <returns><see langword="true"/> if the profile is structurally valid.</returns>
    public static bool TryValidateIccProfile(ReadOnlySpan<byte> data, [NotNullWhen(false)] out string? error)
    {
        if (data.Length < IccHeaderSize + 4)
        {
            error = "the profile is shorter than an ICC header and tag count (132 bytes).";
            return false;
        }

        var declaredSize = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (declaredSize != (uint)data.Length)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"the header declares {declaredSize} bytes but the profile has {data.Length} bytes.");
            return false;
        }

        if (!data.Slice(36, 4).SequenceEqual("acsp"u8))
        {
            error = "the 'acsp' profile file signature is missing.";
            return false;
        }

        var tagCount = BinaryPrimitives.ReadUInt32BigEndian(data[IccHeaderSize..]);
        if (IccHeaderSize + 4 + ((long)tagCount * IccTagEntrySize) > data.Length)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"the tag table ({tagCount} tags) exceeds the profile.");
            return false;
        }

        for (var i = 0; i < tagCount; i++)
        {
            var entry = data.Slice(IccHeaderSize + 4 + (i * IccTagEntrySize), IccTagEntrySize);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            if ((ulong)offset + size > (ulong)data.Length || offset < IccHeaderSize)
            {
                error = string.Create(CultureInfo.InvariantCulture, $"tag {i} ({size} bytes at offset {offset}) is outside the profile.");
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Validates an XMP packet: non-empty, well-formed UTF-8.</summary>
    /// <param name="data">The packet bytes.</param>
    /// <param name="error">The reason the packet is invalid.</param>
    /// <returns><see langword="true"/> if the packet is valid.</returns>
    public static bool TryValidateXmpPacket(ReadOnlySpan<byte> data, [NotNullWhen(false)] out string? error)
    {
        if (data.IsEmpty)
        {
            error = "the XMP packet is empty.";
            return false;
        }

        if (!Utf8.IsValid(data))
        {
            error = "the XMP packet is not valid UTF-8.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Validates TIFF-structured EXIF data.</summary>
    /// <param name="data">The EXIF bytes.</param>
    /// <param name="error">The reason the data is invalid.</param>
    /// <returns><see langword="true"/> if the data is structurally valid.</returns>
    public static bool TryValidateExif(ReadOnlySpan<byte> data, [NotNullWhen(false)] out string? error)
        => ExifTiff.ExifStructure.TryParse(data, out _, out error);
}
