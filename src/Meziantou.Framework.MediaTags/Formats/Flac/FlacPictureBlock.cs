using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Flac;

/// <summary>
/// Parses and builds FLAC PICTURE metadata blocks.
/// Also used by Vorbis Comments METADATA_BLOCK_PICTURE field.
/// </summary>
internal static class FlacPictureBlock
{
    public static bool TryParse(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        if (data.Length < 32)
            return false;

        var offset = 0;

        // Picture type (4 bytes, big-endian)
        var pictureType = (MediaPictureType)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;

        // MIME type length + MIME type
        var mimeLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        if (offset + mimeLength > data.Length)
            return false;
        var mimeType = Encoding.ASCII.GetString(data.Slice(offset, mimeLength));
        offset += mimeLength;

        // Description length + description
        if (offset + 4 > data.Length)
            return false;
        var descLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        if (offset + descLength > data.Length)
            return false;
        var description = Encoding.UTF8.GetString(data.Slice(offset, descLength));
        offset += descLength;

        // Width, height, color depth, colors used (4 * 4 bytes)
        if (offset + 16 > data.Length)
            return false;
        offset += 16; // Skip image dimensions

        // Picture data length + data
        if (offset + 4 > data.Length)
            return false;
        var dataLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        if (offset + dataLength > data.Length)
            return false;

        var pictureData = data.Slice(offset, dataLength).ToArray();

        tags.Pictures.Add(new MediaPicture
        {
            PictureType = pictureType,
            MimeType = mimeType,
            Description = description,
            Data = pictureData,
        });

        return true;
    }

    public static byte[] Build(MediaPicture picture)
    {
        var mimeBytes = Encoding.ASCII.GetBytes(picture.MimeType ?? "image/jpeg");
        var descBytes = Encoding.UTF8.GetBytes(picture.Description ?? "");

        var size = 4 + 4 + mimeBytes.Length + 4 + descBytes.Length + 16 + 4 + picture.Data.Length;
        var result = new byte[size];
        var offset = 0;

        // Picture type
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset), (uint)picture.PictureType);
        offset += 4;

        // MIME type
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset), (uint)mimeBytes.Length);
        offset += 4;
        mimeBytes.CopyTo(result, offset);
        offset += mimeBytes.Length;

        // Description
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset), (uint)descBytes.Length);
        offset += 4;
        descBytes.CopyTo(result, offset);
        offset += descBytes.Length;

        // Width, height, color depth, colors used (all 0)
        offset += 16;

        // Picture data
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset), (uint)picture.Data.Length);
        offset += 4;
        picture.Data.CopyTo(result, offset);

        return result;
    }
}
