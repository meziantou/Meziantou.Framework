using System.Buffers.Binary;

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

        // MIME type length + MIME type. Lengths are unsigned 32-bit values, so they are compared as such: cast to
        // int, a large one turns negative and passes the bounds check.
        var mimeLength = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        if (mimeLength > (uint)(data.Length - offset))
            return false;
        var mimeType = Encoding.ASCII.GetString(data.Slice(offset, (int)mimeLength));
        offset += (int)mimeLength;

        // Description length + description
        if (offset + 4 > data.Length)
            return false;
        var descLength = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        if (descLength > (uint)(data.Length - offset))
            return false;
        var description = Encoding.UTF8.GetString(data.Slice(offset, (int)descLength));
        offset += (int)descLength;

        // Width, height, color depth, colors used (4 * 4 bytes)
        if (offset + 16 > data.Length)
            return false;
        offset += 16; // Skip image dimensions

        // Picture data length + data
        if (offset + 4 > data.Length)
            return false;
        var dataLength = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        if (dataLength > (uint)(data.Length - offset))
            return false;

        var pictureData = data.Slice(offset, (int)dataLength).ToArray();

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
